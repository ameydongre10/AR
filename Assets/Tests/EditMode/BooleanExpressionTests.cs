using System.Collections.Generic;
using NUnit.Framework;
using ARLab.Simulation;

namespace ARLab.Tests
{
    /// <summary>
    /// Parser tests for the two experiment targets. The truth tables here are the reference
    /// the lab's own validation is measured against, so they are written out literally rather
    /// than generated.
    /// </summary>
    public class BooleanExpressionTests
    {
        private static bool Eval(string expr, bool a, bool b)
        {
            BooleanExpression e = BooleanExpression.Parse(expr);
            Assert.IsTrue(e.IsValid, $"{expr} should parse: {e.ParseError}");
            return e.Evaluate(new Dictionary<char, bool> { { 'A', a }, { 'B', b } });
        }

        // ---- SOP XOR: F = A'B + AB' (the lab's headline experiment)

        [Test]
        public void SopXorTruthTable()
        {
            const string expr = "A'B + AB'";
            Assert.IsFalse(Eval(expr, false, false), "0,0 -> 0");
            Assert.IsTrue(Eval(expr, false, true), "0,1 -> 1");
            Assert.IsTrue(Eval(expr, true, false), "1,0 -> 1");
            Assert.IsFalse(Eval(expr, true, true), "1,1 -> 0");
        }

        // ---- POS XOR: F = (A+B)(A'+B') is the same function, written differently

        [Test]
        public void PosXorTruthTable()
        {
            const string expr = "(A+B)(A'+B')";
            Assert.IsFalse(Eval(expr, false, false), "0,0 -> 0");
            Assert.IsTrue(Eval(expr, false, true), "0,1 -> 1");
            Assert.IsTrue(Eval(expr, true, false), "1,0 -> 1");
            Assert.IsFalse(Eval(expr, true, true), "1,1 -> 0");
        }

        [Test]
        public void SopAndPosXorAgree()
        {
            var sop = BooleanExpression.Parse("A'B + AB'");
            var pos = BooleanExpression.Parse("(A+B)(A'+B')");
            Assert.IsTrue(sop.IsValid && pos.IsValid);

            for (int mask = 0; mask < 4; mask++)
            {
                var assign = new Dictionary<char, bool> { { 'A', (mask & 1) != 0 }, { 'B', (mask & 2) != 0 } };
                Assert.AreEqual(sop.Evaluate(assign), pos.Evaluate(assign), $"row {mask} must agree");
            }
        }

        // ---- notation handling

        [Test]
        public void ExplicitAndSymbolsAreAccepted()
        {
            Assert.IsTrue(Eval("A*B", true, true));
            Assert.IsFalse(Eval("A*B", true, false));
            Assert.IsTrue(Eval("A + B", false, true));
        }

        [Test]
        public void WhitespaceIsIgnored()
        {
            Assert.IsTrue(Eval("  A ' B  +  A B ' ", false, true));
        }

        [Test]
        public void ExclamationMarkMeansNot()
        {
            Assert.IsFalse(Eval("!A", true, false));
            Assert.IsTrue(Eval("!A", false, false));
        }

        [Test]
        public void LowercaseVariablesAreNormalised()
        {
            Assert.IsTrue(Eval("a'b + ab'", false, true));
        }

        [Test]
        public void AndBindsTighterThanOr()
        {
            // A + BC must mean A OR (B AND C).
            Assert.IsTrue(Eval("A + BC", true, false));
            Assert.IsFalse(Eval("A + BC", false, true));
            Assert.IsTrue(Eval("A + BC", true, true));
        }

        // ---- diagnostics

        [Test]
        public void EmptyExpressionIsInvalid()
        {
            BooleanExpression e = BooleanExpression.Parse("   ");
            Assert.IsFalse(e.IsValid);
            Assert.IsNotEmpty(e.ParseError);
        }

        [Test]
        public void UnbalancedParenthesisIsInvalid()
        {
            BooleanExpression e = BooleanExpression.Parse("(A + B");
            Assert.IsFalse(e.IsValid);
        }

        [Test]
        public void IllegalCharacterIsInvalid()
        {
            BooleanExpression e = BooleanExpression.Parse("A & B");
            Assert.IsFalse(e.IsValid);
        }

        [Test]
        public void TrailingOperatorIsInvalid()
        {
            BooleanExpression e = BooleanExpression.Parse("A +");
            Assert.IsFalse(e.IsValid);
        }

        [Test]
        public void TrailingJunkIsInvalid()
        {
            // "ABC" is legitimately A AND B AND C, so trailing junk has to be something the
            // grammar cannot absorb at all, like a stray closing bracket.
            Assert.IsTrue(BooleanExpression.Parse("ABC").IsValid, "juxtaposition is AND");

            BooleanExpression e = BooleanExpression.Parse("A)B");
            Assert.IsFalse(e.IsValid);
            Assert.IsNotEmpty(e.ParseError);
        }

        [Test]
        public void PostfixNegationBindsTighterThanAnd()
        {
            // A'B must be (NOT A) AND B, which is the whole point of the SOP notation.
            Assert.IsFalse(Eval("A'B", false, false), "A=0 B=0");
            Assert.IsTrue(Eval("A'B", false, true), "A=0 B=1");
            Assert.IsFalse(Eval("A'B", true, true), "A=1 B=1");
        }

        [Test]
        public void DoubleNegationIsIdentity()
        {
            Assert.IsTrue(Eval("A''", true, false));
            Assert.IsFalse(Eval("A''", false, false));
        }

        [Test]
        public void CanonicalSopIsRendered()
        {
            Assert.AreEqual("A'B + AB'", BooleanExpression.Parse("A'B + AB'").ToSop());
            Assert.AreEqual("A + B", BooleanExpression.Parse("A + B").ToSop(),
                "a term must only list the variables it actually contains");
        }

        [Test]
        public void ValidExpressionHasNoErrorText()
        {
            BooleanExpression e = BooleanExpression.Parse("A'B + AB'");
            Assert.IsTrue(e.IsValid);
            Assert.IsNull(e.ParseError);
        }

        [Test]
        public void VariablesAreDiscovered()
        {
            var list = BooleanExpression.Parse("A'B + AB'").Variables();
            CollectionAssert.AreEqual(new List<char> { 'A', 'B' }, list);
        }

        [Test]
        public void SingleVariableExpressionWorks()
        {
            Assert.IsTrue(Eval("A", true, false));
            Assert.IsFalse(Eval("A", false, false));
        }

        [Test]
        public void EmptyProductIsAlwaysTrue()
        {
            // A + A' is the constant 1.
            for (int mask = 0; mask < 4; mask++)
            {
                bool a = (mask & 1) != 0;
                bool b = (mask & 2) != 0;
                Assert.IsTrue(Eval("A + A'", a, b), $"row {mask} must be 1");
            }
        }
    }
}
