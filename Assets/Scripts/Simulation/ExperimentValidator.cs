using System.Collections.Generic;
using System.Text;
using ARLab.Electronics;
using ARLab.Laboratory;

namespace ARLab.Simulation
{
    public enum CheckLevel { Pass, Warn, Fail, Info }

    public sealed class ValidationCheck
    {
        public string Id;
        public string Title;
        public string Detail;
        public CheckLevel Level;

        public string Glyph
        {
            get
            {
                switch (Level)
                {
                    case CheckLevel.Pass: return "✓";
                    case CheckLevel.Warn: return "⚠";
                    case CheckLevel.Fail: return "✕";
                    default: return "•";
                }
            }
        }
    }

    public sealed class ExperimentReport
    {
        public readonly List<ValidationCheck> Checks = new List<ValidationCheck>(16);
        public bool IsComplete;
        public string Headline = string.Empty;
        public TruthTableResult TruthTable;

        public int FailCount { get { int c = 0; for (int i = 0; i < Checks.Count; i++) if (Checks[i].Level == CheckLevel.Fail) c++; return c; } }
        public int WarnCount { get { int c = 0; for (int i = 0; i < Checks.Count; i++) if (Checks[i].Level == CheckLevel.Warn) c++; return c; } }

        public void Add(string id, CheckLevel level, string title, string detail)
        {
            Checks.Add(new ValidationCheck { Id = id, Level = level, Title = title, Detail = detail });
        }
    }

    /// <summary>What the student is being asked to build.</summary>
    public sealed class ExperimentDefinition
    {
        public string Title;
        public string Expression;
        public string[] RequiredParts;
        public string OutputLabel = "F";

        public static readonly ExperimentDefinition SopXor = new ExperimentDefinition
        {
            Title = "Sum of Products - F = A'B + AB'",
            Expression = "A'B + AB'",
            RequiredParts = new[] { "7404", "7408", "7432" }
        };

        public static readonly ExperimentDefinition PosXor = new ExperimentDefinition
        {
            Title = "Product of Sums - F = (A+B)(A'+B')",
            Expression = "(A+B)(A'+B')",
            RequiredParts = new[] { "7404", "7408", "7432" }
        };

        public static readonly ExperimentDefinition[] All = { SopXor, PosXor };
    }

    /// <summary>
    /// Structural + functional audit of the student's build. Reports precise, actionable
    /// findings instead of a bare pass/fail, and only marks the experiment complete when
    /// every required part is present, powered and producing the correct truth table.
    /// </summary>
    public sealed class ExperimentValidator
    {
        private readonly DigitalLogicSimulator _sim;
        private readonly Breadboard _board;

        public ExperimentValidator(DigitalLogicSimulator sim, Breadboard board)
        {
            _sim = sim;
            _board = board;
        }

        /// <summary>True when this component has at least one pin sharing a net with a breadboard socket.</summary>
        private static bool IsSeated(ConnectionGraph g, IElectronicComponent c)
        {
            IReadOnlyList<ComponentPin> pins = c.Pins;
            for (int i = 0; i < pins.Count; i++)
            {
                ComponentPin p = pins[i];
                if (p == null) continue;
                Net net = g.NetOf(p.TerminalId);
                if (net == null) continue;
                for (int t = 0; t < net.Terminals.Count; t++)
                {
                    Terminal term = g.GetTerminal(net.Terminals[t]);
                    if (term != null && term.Kind == TerminalKind.BreadboardSocket) return true;
                }
            }
            return false;
        }

        private static int CountOf<T>(DigitalLogicSimulator sim) where T : class, IElectronicComponent
        {
            int n = 0;
            IReadOnlyList<IElectronicComponent> all = sim.Components;
            for (int i = 0; i < all.Count; i++) if (all[i] is T) n++;
            return n;
        }

        private static T FirstOf<T>(DigitalLogicSimulator sim) where T : class, IElectronicComponent
        {
            IReadOnlyList<IElectronicComponent> all = sim.Components;
            for (int i = 0; i < all.Count; i++) if (all[i] is T) return (T)all[i];
            return null;
        }

        public ExperimentReport Validate(ExperimentDefinition def, System.Func<LogicValue> outputReader)
        {
            var report = new ExperimentReport();
            ConnectionGraph g = _sim.Graph;

            // ---- power supply
            PowerSupply psu = FirstOf<PowerSupply>(_sim);
            if (psu == null)
            {
                report.Add("psu", CheckLevel.Fail, "Missing component", "No DC power supply found in the lab.");
            }
            else if (!psu.Energised)
            {
                report.Add("psu", CheckLevel.Fail, "Power supply OFF", "Switch the supply ON before evaluating the circuit.");
            }
            else
            {
                report.Add("psu", CheckLevel.Pass, "Power supply ON", $"Supplying {psu.Volts:0.0} V.");
            }

            // ---- rails. The supply's *output* posts must reach a board power rail.
            bool vccRailLinked = psu != null && RailLinked(g, psu.VccOutputTerminal);
            bool gndRailLinked = psu != null && RailLinked(g, psu.GndOutputTerminal);

            if (psu != null)
            {
                report.Add("vcc", vccRailLinked ? CheckLevel.Pass : CheckLevel.Fail,
                    vccRailLinked ? "VCC connected" : "Missing VCC connection",
                    vccRailLinked ? "+5V rail reaches the board." : "Connect the supply +5V post to the board's red (+) rail.");
                report.Add("gnd", gndRailLinked ? CheckLevel.Pass : CheckLevel.Fail,
                    gndRailLinked ? "GND connected" : "Missing GND connection",
                    gndRailLinked ? "Ground rail reaches the board." : "Connect the supply GND post to the board's blue/black (-) rail.");
            }

            // ---- required ICs
            foreach (string part in def.RequiredParts)
            {
                IcComponent ic = FindIc(_sim, part);
                if (ic == null)
                {
                    report.Add($"ic_{part}", CheckLevel.Warn, $"Missing {part}", $"Add IC {part} from the component library.");
                    continue;
                }
                if (!IsSeated(g, ic))
                {
                    report.Add($"ic_{part}", CheckLevel.Warn, $"{part} not on the board", "Place the IC so its pins sit in the breadboard sockets.");
                    continue;
                }
                if (!ic.IsPowered)
                {
                    report.Add($"ic_{part}", CheckLevel.Fail, $"{part} unpowered", "VCC (pin 14) and GND (pin 7) are not both at the correct level.");
                    continue;
                }
                report.Add($"ic_{part}", CheckLevel.Pass, $"{part} ready", ic.Definition.Name + " powered and inserted.");
            }

            // ---- inputs and output
            int switches = CountOf<InputSwitch>(_sim);
            if (switches >= 2) report.Add("inputs", CheckLevel.Pass, "Inputs present", $"{switches} input switches connected.");
            else report.Add("inputs", CheckLevel.Warn, "Missing input", $"Two input switches (A and B) are required; found {switches}.");

            LedIndicator led = FirstOf<LedIndicator>(_sim);
            if (led == null) report.Add("led", CheckLevel.Warn, "Missing output indicator", "Add an LED to observe the circuit output.");
            else if (led.State == LogicValue.Undefined) report.Add("led", CheckLevel.Warn, "LED input floating", "The LED anode net is undriven. Connect it to the final gate output.");
            else report.Add("led", CheckLevel.Pass, "LED connected", "Output indicator attached.");

            // ---- simulation health
            var outcome = _sim.Solve();
            if (outcome.Status == SolveStatus.Unstable)
                report.Add("solve", CheckLevel.Fail, "Unstable circuit", outcome.Diagnostic);
            else if (outcome.Status == SolveStatus.NotPowered)
                report.Add("solve", CheckLevel.Fail, "Circuit unpowered", outcome.Diagnostic);
            else if (outcome.FloatingNets > 0)
                report.Add("solve", CheckLevel.Warn, "Floating nets", $"{outcome.FloatingNets} net(s) have no driver. Unconnected inputs read as undefined.");
            else if (outcome.ContendedNets > 0)
                report.Add("solve", CheckLevel.Warn, "Output contention", $"{outcome.ContendedNets} net(s) driven by conflicting outputs.");
            else
                report.Add("solve", CheckLevel.Pass, "Netlist healthy", $"{_sim.Graph.Nets.Count} nets, all driven.");

            // ---- functional truth table
            var expr = BooleanExpression.Parse(def.Expression);
            if (!expr.IsValid)
            {
                report.Add("truth", CheckLevel.Fail, "Invalid expression", expr.ParseError);
            }
            else
            {
                var evaluator = new TruthTableEvaluator(_sim);
                report.TruthTable = evaluator.Evaluate(expr, outputReader);
                if (report.TruthTable.AllCorrect)
                    report.Add("truth", CheckLevel.Pass, "Truth table verified", report.TruthTable.Summary);
                else
                    report.Add("truth", CheckLevel.Fail, "Incorrect logic", report.TruthTable.Summary);
            }

            // ---- overall
            report.IsComplete = report.FailCount == 0
                             && report.TruthTable != null
                             && report.TruthTable.AllCorrect;

            report.Headline = report.IsComplete
                ? "Experiment completed"
                : report.FailCount > 0
                    ? $"{report.FailCount} blocking issue(s), {report.WarnCount} warning(s)"
                    : $"{report.WarnCount} warning(s) - finish the setup";

            return report;
        }

        private static IcComponent FindIc(DigitalLogicSimulator sim, string part)
        {
            IReadOnlyList<IElectronicComponent> all = sim.Components;
            for (int i = 0; i < all.Count; i++)
                if (all[i] is IcComponent ic && ic.Definition != null && ic.Definition.PartNumber == part) return ic;
            return null;
        }

        /// <summary>True when a supply post shares a net with one of the board's power rails.</summary>
        private static bool RailLinked(ConnectionGraph g, int terminal)
        {
            if (terminal < 0) return false;
            Net n = g.NetOf(terminal);
            if (n == null) return false;
            for (int i = 0; i < n.Terminals.Count; i++)
            {
                Terminal t = g.GetTerminal(n.Terminals[i]);
                if (t != null && t.Kind == TerminalKind.PowerRail) return true;
            }
            return false;
        }
    }
}
