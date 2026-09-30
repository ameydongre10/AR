using System;
using System.Collections.Generic;
using System.Text;
using ARLab.Electronics;

namespace ARLab.Simulation
{
    public enum RowStatus { Pass, Fail, Undefined }

    public struct TruthTableRow
    {
        public char VarA;
        public char VarB;
        public bool Expected;
        public LogicValue Actual;
        public RowStatus Status;
    }

    public sealed class TruthTableResult
    {
        public string Expression = string.Empty;
        public readonly List<TruthTableRow> Rows = new List<TruthTableRow>();
        public bool AllCorrect;
        public int PassCount;
        public string Summary = string.Empty;

        public string ToPlainText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("A  B  | F(exp)  F(act)  Result");
            sb.AppendLine("---+---+-------+-------+-------");
            foreach (TruthTableRow r in Rows)
            {
                string st = r.Status == RowStatus.Pass ? "PASS" : r.Status == RowStatus.Fail ? "FAIL" : "UNDEF";
                sb.AppendLine($"{r.VarA}   {r.VarB}  |   {LogicOps.ToChar(r.Expected ? LogicValue.High : LogicValue.Low)}      {LogicOps.ToChar(r.Actual)}      {st}");
            }
            sb.AppendLine();
            sb.AppendLine(Summary);
            return sb.ToString();
        }
    }

    /// <summary>
    /// Drives the real circuit through every input combination and compares the measured
    /// output against the parsed Boolean expression. Nothing here is precomputed: each row
    /// re-solves the netlist the student actually built.
    /// </summary>
    public sealed class TruthTableEvaluator
    {
        private readonly DigitalLogicSimulator _sim;

        public TruthTableEvaluator(DigitalLogicSimulator sim) => _sim = sim;

        /// <summary>Locates an input switch by its signal label (e.g. "A").</summary>
        public static InputSwitch FindSwitch(DigitalLogicSimulator sim, string signal)
        {
            IReadOnlyList<IElectronicComponent> all = sim.Components;
            for (int i = 0; i < all.Count; i++)
                if (all[i] is InputSwitch s && s.SignalName == signal) return s;
            return null;
        }

        public TruthTableResult Evaluate(
            BooleanExpression expression,
            Func<LogicValue> outputReader,
            string switchA = "A",
            string switchB = "B")
        {
            var result = new TruthTableResult();
            if (expression == null || !expression.IsValid)
            {
                result.Summary = "Boolean expression is not valid: " + (expression?.ParseError ?? "null");
                return result;
            }

            result.Expression = expression.Source;

            InputSwitch sa = FindSwitch(_sim, switchA);
            InputSwitch sb = FindSwitch(_sim, switchB);

            for (int mask = 0; mask < 4; mask++)
            {
                bool a = (mask & 1) != 0;
                bool b = (mask & 2) != 0;

                var assignment = new Dictionary<char, bool> { { 'A', a }, { 'B', b } };
                bool expected = expression.Evaluate(assignment);

                if (sa != null) sa.SetPosition(a ? LogicValue.High : LogicValue.Low);
                if (sb != null) sb.SetPosition(b ? LogicValue.High : LogicValue.Low);

                _sim.Solve();

                LogicValue actual = outputReader != null ? outputReader() : LogicValue.Undefined;

                RowStatus st;
                if (!LogicOps.IsDriven(actual)) st = RowStatus.Undefined;
                else st = (actual == LogicValue.High) == expected ? RowStatus.Pass : RowStatus.Fail;

                result.Rows.Add(new TruthTableRow
                {
                    VarA = a ? '1' : '0',
                    VarB = b ? '1' : '0',
                    Expected = expected,
                    Actual = actual,
                    Status = st
                });

                if (st == RowStatus.Pass) result.PassCount++;
            }

            bool anyUndef = false;
            for (int i = 0; i < result.Rows.Count; i++)
                if (result.Rows[i].Status == RowStatus.Undefined) { anyUndef = true; break; }

            result.AllCorrect = result.PassCount == result.Rows.Count;

            if (result.AllCorrect) result.Summary = "Correct - all 4 input combinations match the expected truth table.";
            else if (anyUndef) result.Summary = $"{result.PassCount}/4 rows match. Undefined output indicates a floating or unpowered net.";
            else result.Summary = $"{result.PassCount}/4 rows match. Check gate selection and wiring.";

            return result;
        }
    }
}
