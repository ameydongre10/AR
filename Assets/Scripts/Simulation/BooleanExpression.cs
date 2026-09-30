using System;
using System.Collections.Generic;
using System.Text;

namespace ARLab.Simulation
{
    /// <summary>
    /// Recursive-descent parser and evaluator for standard digital-design notation:
    /// juxtaposition is AND, '+' is OR, and ' (apostrophe) or '!' is NOT. This single
    /// grammar covers both SOP (F = A'B + AB') and POS (F = (A+B)(A'+B')) forms.
    /// </summary>
    public sealed class BooleanExpression
    {
        private enum NodeKind { Variable, Not, And, Or }

        private abstract class Node
        {
            public abstract NodeKind Kind { get; }
            public abstract LogicValueEval Eval(Func<char, bool> lookup);
        }

        private sealed class VarNode : Node
        {
            public char Name;
            public override NodeKind Kind => NodeKind.Variable;
            public override LogicValueEval Eval(Func<char, bool> lookup) => lookup(Name) ? LogicValueEval.True : LogicValueEval.False;
        }

        private sealed class NotNode : Node
        {
            public Node Inner;
            public override NodeKind Kind => NodeKind.Not;
            public override LogicValueEval Eval(Func<char, bool> lookup) => Inner.Eval(lookup) == LogicValueEval.True ? LogicValueEval.False : LogicValueEval.True;
        }

        private sealed class AndNode : Node
        {
            public Node A, B;
            public override NodeKind Kind => NodeKind.And;
            public override LogicValueEval Eval(Func<char, bool> lookup) => (A.Eval(lookup) == LogicValueEval.True && B.Eval(lookup) == LogicValueEval.True) ? LogicValueEval.True : LogicValueEval.False;
        }

        private sealed class OrNode : Node
        {
            public Node A, B;
            public override NodeKind Kind => NodeKind.Or;
            public override LogicValueEval Eval(Func<char, bool> lookup) => (A.Eval(lookup) == LogicValueEval.True || B.Eval(lookup) == LogicValueEval.True) ? LogicValueEval.True : LogicValueEval.False;
        }

        public enum LogicValueEval { False, True }

        private Node _root;
        private readonly string _source;
        public string Source => _source;
        public bool IsValid => _root != null;
        public string ParseError { get; private set; }

        public static readonly char[] DefaultVariables = { 'A', 'B' };

        private BooleanExpression(string src, Node root, string error)
        {
            _source = src;
            _root = root;
            ParseError = error;
        }

        public static BooleanExpression Parse(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
                return new BooleanExpression(expression, null, "Expression is empty.");

            try
            {
                // Normalise notation: whitespace, '*' and '·' all mean the same as juxtaposition.
                string cleaned = expression.Replace(" ", string.Empty)
                                            .Replace("*", string.Empty)
                                            .Replace("·", string.Empty);
                var p = new Parser(cleaned);
                Node n = p.ParseExpr();
                // Compare against the cleaned length: the original may have been longer.
                if (n == null || p.Pos != cleaned.Length)
                    return new BooleanExpression(expression, null, "Unexpected trailing input.");
                return new BooleanExpression(expression, n, null);
            }
            catch (FormatException e)
            {
                return new BooleanExpression(expression, null, e.Message);
            }
        }

        /// <summary>Evaluates for a concrete assignment. Inputs are true/false per variable.</summary>
        public bool Evaluate(IDictionary<char, bool> assignment)
        {
            if (_root == null) return false;
            return _root.Eval(c => assignment.TryGetValue(c, out bool v) && v) == LogicValueEval.True;
        }

        public List<char> Variables()
        {
            var set = new SortedSet<char>();
            CollectVars(_root, set);
            return new List<char>(set);
        }

        private static void CollectVars(Node n, SortedSet<char> set)
        {
            if (n == null) return;
            if (n is VarNode v) { set.Add(v.Name); return; }
            if (n is NotNode nt) { CollectVars(nt.Inner, set); return; }
            if (n is AndNode a) { CollectVars(a.A, set); CollectVars(a.B, set); return; }
            if (n is OrNode o) { CollectVars(o.A, set); CollectVars(o.B, set); }
        }

        /// <summary>Renders the expression in canonical Sum-of-Products form, e.g. A'B + AB'.</summary>
        public string ToSop()
        {
            if (_root == null) return string.Empty;
            var terms = new List<List<Minterm>>();

            // Expand into minterms, then absorb duplicates.
            Expand(_root, new List<Minterm>(), terms);
            List<char> byVars = Variables();

            // A term only mentions the variables it contains: 'A + B' must render as "A + B",
            // not "AB + AB", so presence has to be tracked separately from polarity.
            var uniq = new SortedSet<string>();
            foreach (List<Minterm> t in terms)
            {
                var present = new bool[byVars.Count];
                var negated = new bool[byVars.Count];
                for (int i = 0; i < t.Count; i++)
                {
                    int v = t[i].VarIndex;
                    if (v < 0 || v >= byVars.Count) continue;
                    present[v] = true;
                    negated[v] = t[i].Negated;
                }

                var sb = new StringBuilder();
                for (int i = 0; i < byVars.Count; i++)
                {
                    if (!present[i]) continue;
                    if (negated[i]) sb.Append(byVars[i]).Append('\'');
                    else sb.Append(byVars[i]);
                }
                uniq.Add(sb.ToString());
            }
            return string.Join(" + ", uniq);
        }

        private struct Minterm
        {
            public int VarIndex;
            public bool Negated;
        }

        private void Expand(Node n, List<Minterm> acc, List<List<Minterm>> results)
        {
            if (n is VarNode v)
            {
                var vlist = Variables();
                var copy = new List<Minterm>(acc) { new Minterm { VarIndex = vlist.IndexOf(v.Name), Negated = false } };
                results.Add(copy);
                return;
            }
            if (n is NotNode nt)
            {
                var vlist = Variables();
                var copy = new List<Minterm>(acc);
                if (nt.Inner is VarNode nv) copy.Add(new Minterm { VarIndex = vlist.IndexOf(nv.Name), Negated = true });
                results.Add(copy);
                return;
            }
            if (n is AndNode a)
            {
                var l = new List<List<Minterm>>(); Expand(a.A, acc, l);
                var r = new List<List<Minterm>>(); Expand(a.B, acc, r);
                foreach (var x in l) foreach (var y in r)
                {
                    var m = new List<Minterm>(x); m.AddRange(y); results.Add(m);
                }
                return;
            }
            if (n is OrNode o)
            {
                Expand(o.A, acc, results);
                Expand(o.B, acc, results);
            }
        }

        // ------------------------------------------------------------------ parser

        private sealed class Parser
        {
            private readonly string _s;
            public int Pos;

            public Parser(string s) { _s = s; }

            private bool Eof => Pos >= _s.Length;
            private char Cur => _s[Pos];

            public Node ParseExpr()
            {
                Node left = ParseTerm();
                while (!Eof && Cur == '+')
                {
                    Pos++;
                    Node right = ParseTerm();
                    left = new OrNode { A = left, B = right };
                }
                return left;
            }

            private Node ParseTerm()
            {
                Node left = ParseFactor();
                while (!Eof && Cur != '+' && Cur != ')')
                {
                    Node right = ParseFactor();
                    left = new AndNode { A = left, B = right };
                }
                return left;
            }

            private Node ParseFactor()
            {
                if (Eof) throw new FormatException("Expression ended unexpectedly.");

                char c = Cur;
                Node atom;

                if (c == '(')
                {
                    Pos++;
                    atom = ParseExpr();
                    if (Eof || Cur != ')') throw new FormatException("Missing ')'.");
                    Pos++;
                }
                else if (c == '!' || c == '\'')
                {
                    // Prefix negation is accepted for tolerance with ASCII-only keyboards.
                    Pos++;
                    atom = new NotNode { Inner = ParseFactor() };
                }
                else if (char.IsLetter(c))
                {
                    Pos++;
                    atom = new VarNode { Name = char.ToUpperInvariant(c) };
                }
                else
                {
                    throw new FormatException($"Unexpected character '{c}' at position {Pos}.");
                }

                // Postfix is the notation the lab's experiments are written in, so it is the
                // one that must work: A' is NOT A and AB' is A AND NOT B. Chained, so A'' is
                // a double inverter.
                while (!Eof && Cur == '\'')
                {
                    Pos++;
                    atom = new NotNode { Inner = atom };
                }

                return atom;
            }
        }
    }
}
