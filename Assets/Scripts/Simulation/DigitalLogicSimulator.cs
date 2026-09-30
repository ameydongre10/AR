using System;
using System.Collections.Generic;
using UnityEngine;
using ARLab.Core;
using ARLab.Electronics;

namespace ARLab.Simulation
{
    public enum SolveStatus
    {
        Converged,
        Unstable,
        NotPowered,
        NoComponents
    }

    public struct SimulationOutcome
    {
        public SolveStatus Status;
        public int Iterations;
        public int PoweredParts;
        public int TotalParts;
        public int FloatingNets;
        public int ContendedNets;
        public string Diagnostic;

        public bool IsStable => Status == SolveStatus.Converged;
    }

    /// <summary>
    /// Gate-level solver. Every pass re-derives each part's supply state, then each part
    /// recomputes its outputs from the current net values, until nothing changes. That is a
    /// fixpoint search over the netlist, so a change on input A genuinely propagates through
    /// however many gate stages the student built, and oscillation is detected rather than
    /// hidden.
    /// </summary>
    public sealed class DigitalLogicSimulator : INetReader, IDriverSink
    {
        private readonly ConnectionGraph _graph;
        private readonly List<IElectronicComponent> _components = new List<IElectronicComponent>(32);
        private readonly List<PowerSupply> _supplies = new List<PowerSupply>(2);

        private readonly Dictionary<int, List<DriveEntry>> _drives = new Dictionary<int, List<DriveEntry>>(32);
        private readonly List<Net> _nets = new List<Net>(64);

        private struct DriveEntry
        {
            public int Terminal;
            public LogicValue Value;
        }

        public DigitalLogicSimulator(ConnectionGraph graph)
        {
            _graph = graph;
            // Net objects are recreated on every structural change, so the driver/reader
            // classification must be recomputed whenever the netlist changes shape.
            _graph.GraphChanged += () => _rolesDirty = true;
        }

        public IReadOnlyList<IElectronicComponent> Components => _components;
        public ConnectionGraph Graph => _graph;

        public void Register(IElectronicComponent c)
        {
            if (c == null || _components.Contains(c)) return;
            _components.Add(c);
            if (c is PowerSupply p) _supplies.Add(p);
        }

        public void Unregister(IElectronicComponent c)
        {
            if (c == null) return;
            _components.Remove(c);
            for (int i = 0; i < _supplies.Count; i++)
                if (ReferenceEquals(_supplies[i], c)) { _supplies.RemoveAt(i); break; }
        }

        public void Clear()
        {
            _components.Clear();
            _supplies.Clear();
        }

        // ------------------------------------------------------------------ classification

        private bool _rolesDirty = true;

        public void MarkRolesDirty() => _rolesDirty = true;

        /// <summary>Rebuilds the driver/reader classification from the live pin table.</summary>
        private void RebuildRoles()
        {
            for (int i = 0; i < _components.Count; i++)
            {
                IElectronicComponent c = _components[i];
                IReadOnlyList<ComponentPin> pins = c.Pins;
                for (int p = 0; p < pins.Count; p++)
                {
                    ComponentPin pin = pins[p];
                    if (pin == null) continue;
                    Net net = _graph.NetOf(pin.TerminalId);
                    if (net == null) continue;
                    if (pin.IsSupply) net.Readers.Add(pin.TerminalId);
                    else if (pin.IsDataOutput) net.Drivers.Add(pin.TerminalId);
                    else net.Readers.Add(pin.TerminalId);
                }
            }
            _rolesDirty = false;
        }

        // ------------------------------------------------------------------ solve

        public SimulationOutcome Solve(int maxIterations = 48)
        {
            if (_rolesDirty)
            {
                _nets.Clear();
                for (int n = 0; n < _graph.Nets.Count; n++) _nets.Add(_graph.Nets[n]);
                for (int i = 0; i < _nets.Count; i++) { _nets[i].Drivers.Clear(); _nets[i].Readers.Clear(); }
                RebuildRoles();
            }

            var outcome = new SimulationOutcome { TotalParts = _components.Count };

            if (_components.Count == 0)
            {
                outcome.Status = SolveStatus.NoComponents;
                outcome.Diagnostic = "No components placed yet.";
                return outcome;
            }

            int iterations = 0;
            bool converged = false;

            // Seed the state: every net is undriven until a component drives it. This happens
            // once per solve, never per pass -- a fixpoint iteration has to be able to read
            // the previous pass's values, otherwise nothing ever settles.
            for (int i = 0; i < _nets.Count; i++) _nets[i].Value = LogicValue.Undefined;
            _prev.Clear();

            for (int pass = 0; pass < maxIterations; pass++)
            {
                iterations = pass + 1;

                RunPass();

                if (pass > 0 && !ChangedSinceLastPass())
                {
                    converged = true;
                    break;
                }
                Snapshot();
            }

            // One last pass so that everything which only *observes* (probes, LEDs) sees the
            // converged net values. Without it every reader is a pass behind, so a freshly
            // solved circuit reports Undefined until the next solve.
            RunPass();
            int powered = 0;
            for (int i = 0; i < _components.Count; i++)
                if (_components[i].IsPowered) powered++;

            int floating = 0, contended = 0;
            for (int i = 0; i < _nets.Count; i++)
            {
                if (_nets[i].IsContended && _nets[i].Drivers.Count > 1)
                {
                    bool disagree = false;
                    if (_drives.TryGetValue(_nets[i].Id, out List<DriveEntry> d) && d.Count > 1)
                    {
                        LogicValue f = d[0].Value;
                        for (int k = 1; k < d.Count; k++) if (d[k].Value != f) { disagree = true; break; }
                    }
                    if (disagree) contended++;
                }
                else if (_nets[i].IsFloating && _nets[i].Readers.Count > 0) floating++;
            }

            outcome.Iterations = iterations;
            outcome.PoweredParts = powered;
            outcome.FloatingNets = floating;
            outcome.ContendedNets = contended;

            bool anyPowered = _supplies.Count == 0 || powered > 0;

            if (!converged)
            {
                outcome.Status = SolveStatus.Unstable;
                outcome.Diagnostic = $"Logic did not settle after {maxIterations} passes. Check for a combinational feedback loop.";
            }
            else if (!anyPowered)
            {
                outcome.Status = SolveStatus.NotPowered;
                outcome.Diagnostic = "Power supply is OFF. Switch it ON and wire +5V and GND.";
            }
            else
            {
                outcome.Status = SolveStatus.Converged;
                outcome.Diagnostic = null;
            }

            EventBus.Publish(new SimulationResultEvent(outcome.Status == SolveStatus.Converged,
                powered > 0, iterations, outcome.Diagnostic));
            PublishNetValues();
            return outcome;
        }

        private readonly Dictionary<int, LogicValue> _prev = new Dictionary<int, LogicValue>(64);
        private readonly Dictionary<int, LogicValue> _curr = new Dictionary<int, LogicValue>(64);

        /// <summary>
        /// One complete fixpoint step: re-derive supply state, re-evaluate every component
        /// against the current net values, then apply the resulting drives and collapse
        /// disagreeing drivers on a shared net to Undefined. Net values are carried over from
        /// the previous pass, which is what makes this a fixpoint search rather than a
        /// single-shot evaluation.
        /// </summary>
        private void RunPass()
        {
            RefreshAllPower();

            _drives.Clear();
            for (int i = 0; i < _components.Count; i++)
            {
                IElectronicComponent c = _components[i];
                if (c is MonoBehaviour mb && mb == null) continue;
                c.Evaluate(this, this);
            }

            foreach (KeyValuePair<int, List<DriveEntry>> kv in _drives)
            {
                Net net = _graph.NetById(kv.Key);
                if (net == null) continue;
                List<DriveEntry> list = kv.Value;
                if (list.Count == 0) continue;

                LogicValue first = list[0].Value;
                bool conflict = false;
                for (int i = 1; i < list.Count; i++)
                {
                    if (list[i].Value != first) { conflict = true; break; }
                }
                net.Value = conflict ? LogicValue.Undefined : first;
            }

            // Record the whole resulting state, undriven nets included, so the stability
            // check compares like with like.
            _curr.Clear();
            for (int i = 0; i < _nets.Count; i++) _curr[_nets[i].Id] = _nets[i].Value;
        }

        private void Snapshot()
        {
            _prev.Clear();
            foreach (KeyValuePair<int, LogicValue> kv in _curr) _prev[kv.Key] = kv.Value;
            _curr.Clear();
        }

        private bool ChangedSinceLastPass()
        {
            if (_prev.Count != _curr.Count) return true;
            foreach (KeyValuePair<int, LogicValue> kv in _curr)
            {
                if (!_prev.TryGetValue(kv.Key, out LogicValue p) || p != kv.Value) return true;
            }
            return false;
        }

        private void RefreshAllPower()
        {
            for (int i = 0; i < _components.Count; i++)
            {
                IElectronicComponent c = _components[i];
                if (c is MonoBehaviour mb && mb == null) continue;
                c.RefreshPower(this);
            }
        }

        private void PublishNetValues()
        {
            for (int i = 0; i < _nets.Count; i++)
                EventBus.Publish(new NetValueEvent(_nets[i].Id, _nets[i].Value));
        }

        // ------------------------------------------------------------------ interfaces

        public LogicValue ValueOfTerminal(int terminalId)
        {
            Net n = _graph.NetOf(terminalId);
            return n != null ? n.Value : LogicValue.Undefined;
        }

        public LogicValue ValueOfNet(int netId)
        {
            Net n = _graph.NetById(netId);
            return n != null ? n.Value : LogicValue.Undefined;
        }

        public bool TryGetNet(int terminalId, out Net net)
        {
            net = _graph.NetOf(terminalId);
            return net != null;
        }

        public void Drive(int terminalId, LogicValue value)
        {
            Net net = _graph.NetOf(terminalId);
            if (net == null) return;
            if (!_drives.TryGetValue(net.Id, out List<DriveEntry> list))
            {
                list = new List<DriveEntry>(2);
                _drives[net.Id] = list;
            }
            list.Add(new DriveEntry { Terminal = terminalId, Value = value });
        }

        // ------------------------------------------------------------------ queries

        /// <summary>Value on a named IC gate output, e.g. GetGateOutput("7408", 0).</summary>
        public LogicValue GateOutput(IcComponent ic, int gateIndex)
        {
            if (ic == null || ic.Definition == null) return LogicValue.Undefined;
            if (gateIndex < 0 || gateIndex >= ic.Definition.Gates.Length) return LogicValue.Undefined;
            string signal = ic.Definition.Gates[gateIndex].Out;
            for (int i = 0; i < ic.Pins.Count; i++)
            {
                ComponentPin p = ic.Pins[i];
                if (p != null && p.SignalName == signal) return ValueOfTerminal(p.TerminalId);
            }
            return LogicValue.Undefined;
        }

        /// <summary>Value on a discrete gate output, or Undefined if absent.</summary>
        public LogicValue GateOutput(LogicGate gate)
            => gate == null ? LogicValue.Undefined : ValueOfTerminal(gate.OutputTerminal);

        public int PoweredPartCount()
        {
            int c = 0;
            for (int i = 0; i < _components.Count; i++) if (_components[i].IsPowered) c++;
            return c;
        }
    }
}
