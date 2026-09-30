using System;
using System.Collections.Generic;
using UnityEngine;
using ARLab.Core;
using ARLab.Electronics;
using ARLab.Laboratory;
using ARLab.Simulation;

namespace ARLab.UI
{
    /// <summary>
    /// Progress-driven guidance. Each step states a goal, a hint and a predicate over the
    /// live bench, so the tutorial advances from what the student actually did rather than
    /// from a fixed timer.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TutorialController : MonoBehaviour
    {
        public sealed class Step
        {
            public string Title;
            public string Goal;
            public string Hint;
            /// <summary>Evaluated against the live lab; true when the step is satisfied.</summary>
            public Func<LaboratoryManager, bool> IsSatisfied;
        }

        [SerializeField] private bool advanceAutomatically = true;

        private LaboratoryManager _lab;
        private ExperimentDefinition _experiment;
        private readonly List<Step> _steps = new List<Step>(10);
        private int _index = -1;

        public int Index => _index;
        public int TotalSteps => _steps.Count;
        public bool IsRunning { get; private set; }
        public bool IsComplete { get; private set; }
        public Step Current => _index >= 0 && _index < _steps.Count ? _steps[_index] : null;

        public event Action<int, Step> StepChanged;

        /// <summary>Explicit dependency, matching the rest of the manager graph.</summary>
        public void Configure(LaboratoryManager lab) => _lab = lab;

        public void Begin(ExperimentDefinition experiment)
        {
            _experiment = experiment ?? ExperimentDefinition.SopXor;
            if (_lab == null) _lab = FindAnyObjectByType<LaboratoryManager>();
            _steps.Clear();
            _steps.AddRange(BuildSteps(_experiment));
            _index = -1;
            IsRunning = true;
            IsComplete = false;
            Advance();
        }

        private List<Step> BuildSteps(ExperimentDefinition def)
        {
            string expr = def.Expression;

            return new List<Step>
            {
                new Step
                {
                    Title = "Place the bench",
                    Goal = "Aim at a flat surface and tap to place the laboratory.",
                    Hint = "Move the phone slowly so ARCore can find a horizontal surface.",
                    IsSatisfied = l => Placed(l)
                },
                new Step
                {
                    Title = "Switch on the supply",
                    Goal = "Turn the DC supply ON so the rails are live.",
                    Hint = "Tap the supply's power switch, or use the Power button.",
                    IsSatisfied = l => Powered(l)
                },
                new Step
                {
                    Title = "Power the rails",
                    Goal = "Wire the supply's +5V and GND posts to the board's red (+) and black (-) rails.",
                    Hint = "Tap a post, then tap a rail hole. Two outputs can never be joined.",
                    IsSatisfied = l => RailLinked(l)
                },
                new Step
                {
                    Title = "Add the logic ICs",
                    Goal = $"Insert 7404, 7408 and 7432 across the centre trench and connect VCC (pin 14) and GND (pin 7).",
                    Hint = "An IC's pins must sit in the sockets for it to be considered seated.",
                    IsSatisfied = l => AllIcsSeatedAndPowered(l, def)
                },
                new Step
                {
                    Title = "Add the inputs",
                    Goal = "Place two input switches (A and B) and tie one side of each to ground.",
                    Hint = "Each switch is a SPDT: its common leg is the signal, the other leg is +5V or GND.",
                    IsSatisfied = l => Count<InputSwitch>(l) >= 2
                },
                new Step
                {
                    Title = "Build the logic",
                    Goal = $"Wire the gates so the output follows F = {expr}.",
                    Hint = "Inverters go in series: 7404 turns A into A' and B into B'.",
                    IsSatisfied = l => Count<IcComponent>(l) >= 3 && CountOfWires(l) >= 6
                },
                new Step
                {
                    Title = "Observe the output",
                    Goal = "Connect the final gate output to an LED anode and provide its return path to ground.",
                    Hint = "An LED needs a path to GND; without one it will not light.",
                    IsSatisfied = l => Count<LedIndicator>(l) >= 1
                },
                new Step
                {
                    Title = "Verify",
                    Goal = "Run the truth-table check. All 4 input combinations must match.",
                    Hint = "Press Evaluate. Every row is simulated against the real netlist, not a formula.",
                    IsSatisfied = l => false   // completed by the Evaluate action
                }
            };
        }

        // ------------------------------------------------------------------ predicates

        private static bool Placed(LaboratoryManager l)
            => l != null && l.LabRoot != null && l.LabRoot.gameObject.activeInHierarchy;

        private static bool Powered(LaboratoryManager l)
        {
            if (l == null) return false;
            for (int i = 0; i < l.Components.Count; i++)
                if (l.Components[i] is PowerSupply psu) return psu.Energised;
            return false;
        }

        private static bool RailLinked(LaboratoryManager l)
        {
            if (l == null) return false;
            ConnectionGraph g = l.Graph;
            for (int i = 0; i < l.Components.Count; i++)
            {
                if (!(l.Components[i] is PowerSupply psu)) continue;
                return TouchesRail(g, psu.VccOutputTerminal) && TouchesRail(g, psu.GndOutputTerminal);
            }
            return false;
        }

        private static bool TouchesRail(ConnectionGraph g, int terminal)
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

        private static bool AllIcsSeatedAndPowered(LaboratoryManager l, ExperimentDefinition def)
        {
            if (l == null) return false;
            ConnectionGraph g = l.Graph;
            for (int i = 0; i < def.RequiredParts.Length; i++)
            {
                bool found = false;
                for (int c = 0; c < l.Components.Count; c++)
                {
                    if (!(l.Components[c] is IcComponent ic)) continue;
                    if (ic.Definition == null || ic.Definition.PartNumber != def.RequiredParts[i]) continue;
                    if (ic.IsPowered && Seated(g, ic)) { found = true; break; }
                }
                if (!found) return false;
            }
            return true;
        }

        private static bool Seated(ConnectionGraph g, IElectronicComponent c)
        {
            IReadOnlyList<ComponentPin> pins = c.Pins;
            for (int i = 0; i < pins.Count; i++)
            {
                ComponentPin p = pins[i];
                if (p == null) continue;
                Net n = g.NetOf(p.TerminalId);
                if (n == null) continue;
                for (int t = 0; t < n.Terminals.Count; t++)
                {
                    Terminal term = g.GetTerminal(n.Terminals[t]);
                    if (term != null && term.Kind != TerminalKind.ComponentPin) return true;
                }
            }
            return false;
        }

        private static int Count<T>(LaboratoryManager l) where T : class, IElectronicComponent
        {
            if (l == null) return 0;
            int n = 0;
            for (int i = 0; i < l.Components.Count; i++) if (l.Components[i] is T) n++;
            return n;
        }

        private static int CountOfWires(LaboratoryManager l) => l == null ? 0 : l.WireCount;

        // ------------------------------------------------------------------ flow

        private void Update()
        {
            if (!IsRunning || IsComplete) return;
            if (Current == null || Current.IsSatisfied == null) return;
            if (Current.IsSatisfied(_lab)) Advance();
        }

        /// <summary>Moves to the next step, or completes the tutorial.</summary>
        public void Advance()
        {
            if (!IsRunning) return;
            _index++;

            if (_index >= _steps.Count)
            {
                Complete();
                return;
            }

            EventBus.Publish(new TutorialAdvancedEvent(_index));
            StepChanged?.Invoke(_index, Current);
            if (!advanceAutomatically) return;
        }

        /// <summary>Let the student skip a step they have already satisfied.</summary>
        public void SkipStep() => Advance();

        public void Complete()
        {
            IsRunning = false;
            IsComplete = true;
            EventBus.Publish(new TutorialCompletedEvent());
        }

        public void Restart()
        {
            if (_experiment == null) return;
            _index = -1;
            IsRunning = true;
            IsComplete = false;
            Advance();
        }
    }
}
