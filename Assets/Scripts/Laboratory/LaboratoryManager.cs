using System.Collections.Generic;
using UnityEngine;
using ARLab.Core;
using ARLab.Electronics;
using ARLab.Simulation;

namespace ARLab.Laboratory
{
    /// <summary>One entry in the component library panel.</summary>
    public sealed class CatalogEntry
    {
        public string Key;
        public string DisplayName;
        public string PartNumber;
        public string Category;
        public string Blurb;
    }

    /// <summary>
    /// Owner of the bench: the netlist, every placed component, all wires, and the solver.
    /// All mutations funnel through here so the netlist, the visuals and the simulation can
    /// never drift apart.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LaboratoryManager : MonoBehaviour
    {
        [Header("Rig")]
        [SerializeField] private Transform labRoot;
        [SerializeField] private Breadboard breadboard;
        [SerializeField] private PowerSupply powerSupply;
        [SerializeField] private Transform componentAnchor;

        private readonly ConnectionGraph _graph = new ConnectionGraph();
        private DigitalLogicSimulator _sim;
        private readonly List<IElectronicComponent> _components = new List<IElectronicComponent>(32);
        private readonly List<Wire> _wires = new List<Wire>(32);
        private readonly List<string> _pinMapScratch = new List<string>(16);

        private IElectronicComponent _selected;
        private IConnectable _wireStart;
        private Wire _wirePreview;
        private Vector3 _dragPlaneNormal = Vector3.up;

        public ConnectionGraph Graph => _graph;
        public DigitalLogicSimulator Simulator { get { EnsureSimulator(); return _sim; } }
        public IReadOnlyList<IElectronicComponent> Components => _components;
        public IReadOnlyList<Wire> Wires => _wires;
        public Breadboard Breadboard => breadboard;
        public PowerSupply PowerSupply => powerSupply;
        public Transform LabRoot => labRoot;
        public IElectronicComponent Selected => _selected;
        public IConnectable WireStart => _wireStart;
        public bool IsWiring => _wireStart != null;
        public int WireCount => _wires.Count;

        public event System.Action<IElectronicComponent> SelectionChanged;

        public static readonly CatalogEntry[] Catalog =
        {
            new CatalogEntry { Key = "7404", DisplayName = "IC 7404", PartNumber = "SN7404N", Category = "ICs",
                Blurb = "Hex inverter - six NOT gates" },
            new CatalogEntry { Key = "7408", DisplayName = "IC 7408", PartNumber = "SN7408N", Category = "ICs",
                Blurb = "Quad 2-input AND" },
            new CatalogEntry { Key = "7432", DisplayName = "IC 7432", PartNumber = "SN7432N", Category = "ICs",
                Blurb = "Quad 2-input OR" },

            new CatalogEntry { Key = "NOT", DisplayName = "NOT Gate", PartNumber = "74HC04", Category = "Logic",
                Blurb = "Y = NOT A" },
            new CatalogEntry { Key = "AND", DisplayName = "AND Gate", PartNumber = "74HC08", Category = "Logic",
                Blurb = "Y = A AND B" },
            new CatalogEntry { Key = "OR", DisplayName = "OR Gate", PartNumber = "74HC32", Category = "Logic",
                Blurb = "Y = A OR B" },

            new CatalogEntry { Key = "SW", DisplayName = "Input Switch", PartNumber = "SW-SPST", Category = "Power",
                Blurb = "Selects an input between +5V and GND" },
            new CatalogEntry { Key = "PSU", DisplayName = "DC Supply", PartNumber = "PSU-5V", Category = "Power",
                Blurb = "Bench supply, +5V and GND" },
            new CatalogEntry { Key = "VCC", DisplayName = "VCC Rail", PartNumber = "RAIL+", Category = "Power",
                Blurb = "+5V rail tap" },
            new CatalogEntry { Key = "GND", DisplayName = "GND Rail", PartNumber = "RAIL-", Category = "Power",
                Blurb = "Ground rail tap" },

            new CatalogEntry { Key = "LED", DisplayName = "LED", PartNumber = "LED-5MM", Category = "Output",
                Blurb = "Logic output indicator" },
            new CatalogEntry { Key = "PROBE", DisplayName = "Logic Probe", PartNumber = "PROBE", Category = "Output",
                Blurb = "Reads a level without driving it" }
        };

        private void Awake()
        {
            EnsureSimulator();
        }

        /// <summary>
        /// The simulator is created on demand rather than only in Awake, because Awake does not
        /// run for AddComponent in the editor. Anything that reads Simulator before Start would
        /// otherwise get null and fail deep inside the solver.
        /// </summary>
        private void EnsureSimulator()
        {
            if (_sim == null) _sim = new DigitalLogicSimulator(_graph);
        }

        public void Configure(Transform root, Breadboard board, PowerSupply psu, Transform anchor)
        {
            labRoot = root;
            breadboard = board;
            powerSupply = psu;
            componentAnchor = anchor != null ? anchor : root;
            EnsureSimulator();
        }

        private void Start()
        {
            EnsureSimulator();
            if (breadboard != null) breadboard.Register(_graph);
            if (powerSupply != null) AddExisting(powerSupply);
            SolveAndRefresh();
        }

        private void AddExisting(IElectronicComponent c)
        {
            if (c == null || _components.Contains(c)) return;
            EnsureSimulator();
            c.Register(_graph);
            _components.Add(c);
            _sim.Register(c);
            EventBus.Publish(new ComponentAddedEvent(c));
        }

        // ------------------------------------------------------------------ component lifecycle

        /// <summary>Spawns a component from the library. Returns null and explains why if refused.</summary>
        public IElectronicComponent Spawn(string key, Vector3 worldPosition)
        {
            if (string.IsNullOrEmpty(key)) return null;

            IElectronicComponent comp;
            switch (key)
            {
                case "7404": comp = ComponentFactory.CreateIc(IcDefinitions.Hex7404); break;
                case "7408": comp = ComponentFactory.CreateIc(IcDefinitions.Quad7408); break;
                case "7432": comp = ComponentFactory.CreateIc(IcDefinitions.Quad7432); break;
                case "NOT": comp = ComponentFactory.CreateGate(LogicGateKind.Not); break;
                case "AND": comp = ComponentFactory.CreateGate(LogicGateKind.And); break;
                case "OR": comp = ComponentFactory.CreateGate(LogicGateKind.Or); break;
                case "SW": comp = ComponentFactory.CreateSwitch(FreeSignalName()); break;
                case "PSU": comp = ComponentFactory.CreatePowerSupply(); break;
                case "LED": comp = ComponentFactory.CreateLed("F"); break;
                case "PROBE": comp = ComponentFactory.CreateProbe(); break;
                default:
                    EventBus.Publish(new NotificationEvent($"Unknown component '{key}'.", NotificationSeverity.Error));
                    return null;
            }

            comp.Transform.SetParent(componentAnchor != null ? componentAnchor : transform, true);
            comp.Transform.position = worldPosition;
            comp.Transform.localScale = Vector3.one;

            EnsureSimulator();
            comp.Register(_graph);
            _components.Add(comp);
            _sim.Register(comp);

            EventBus.Publish(new ComponentAddedEvent(comp));
            SolveAndRefresh();
            return comp;
        }

        /// <summary>Next free A, B, C... label so multiple switches stay distinguishable.</summary>
        private string FreeSignalName()
        {
            const string letters = "ABCDEFGH";
            for (int i = 0; i < letters.Length; i++)
            {
                bool taken = false;
                for (int c = 0; c < _components.Count; c++)
                {
                    if (_components[c] is InputSwitch s && s.SignalName == letters[i].ToString()) { taken = true; break; }
                }
                if (!taken) return letters[i].ToString();
            }
            return "A";
        }

        /// <summary>Removes a component and every wire attached to it.</summary>
        public void Remove(IElectronicComponent comp)
        {
            if (comp == null) return;

            for (int i = _wires.Count - 1; i >= 0; i--)
            {
                Wire w = _wires[i];
                if (w == null) continue;
                if (WireTouches(w, comp))
                {
                    _graph.RemoveWireByRef(w);
                    _wires.RemoveAt(i);
                    Destroy(w.gameObject);
                }
            }

            _components.Remove(comp);
            _sim?.Unregister(comp);
            comp.Unregister(_graph);

            if (ReferenceEquals(_selected, comp)) Select(null);

            Destroy(comp.GameObject);
            EventBus.Publish(new ComponentRemovedEvent(comp));
            SolveAndRefresh();
        }

        private static bool WireTouches(Wire w, IElectronicComponent comp)
        {
            if (w == null || w.EndpointA == null || w.EndpointB == null) return false;
            if (ReferenceEquals(w.EndpointA.Owner, comp)) return true;
            if (ReferenceEquals(w.EndpointB.Owner, comp)) return true;
            return false;
        }

        // ------------------------------------------------------------------ selection

        public void Select(IElectronicComponent comp)
        {
            _selected = comp;
            SelectionChanged?.Invoke(comp);
            EventBus.Publish(new SelectionEvent(comp));
        }

        // ------------------------------------------------------------------ wiring

        /// <summary>Validates a candidate endpoint. Returns false with a reason when illegal.</summary>
        public bool CanConnect(IConnectable candidate, out string reason)
        {
            reason = null;
            if (candidate == null) { reason = "Nothing selected."; return false; }
            if (candidate.TerminalId < 0) { reason = "Terminal is not registered."; return false; }
            if (_wireStart == null) return true;

            if (ReferenceEquals(candidate, _wireStart)) { reason = "Cannot connect a terminal to itself."; return false; }
            if (_wireStart.TerminalId == candidate.TerminalId) { reason = "Cannot connect a terminal to itself."; return false; }
            if (_graph.FindWire(_wireStart.TerminalId, candidate.TerminalId) != null) { reason = "These terminals are already wired."; return false; }

            // Two outputs shorted together is a real fault, not a convenience.
            if (IsOutput(_wireStart) && IsOutput(candidate))
            {
                reason = "Invalid electrical connection: two outputs cannot be tied together.";
                return false;
            }
            return true;
        }

        private static bool IsOutput(IConnectable c) => c is ComponentPin p && p.Function == PinFunction.DataOutput;
        private static bool IsSupply(IConnectable c) => c is ComponentPin p && p.IsSupply;

        public bool BeginWire(IConnectable from)
        {
            if (from == null) return false;
            _wireStart = from;
            if (_wireTip == null)
                _wireTip = new GameObject("WireTip") { hideFlags = HideFlags.HideAndDontSave };
            _wirePreview = CreateWireObject();
            _wirePreview.SetEndpoints(from, null, Wire.ColorFor(from.TerminalKind, FunctionOf(from), from.DisplayLabel));
            _wirePreview.gameObject.name = "WirePreview";
            return true;
        }

        public void UpdateWirePreview(Vector3 worldPoint)
        {
            if (_wirePreview == null || _wireTip == null) return;
            _wireTip.transform.position = worldPoint;
            _wirePreview.SetPreviewTarget(_wireTip.transform);
        }

        private GameObject _wireTip;

        public bool CompleteWire(IConnectable to)
        {
            if (_wireStart == null) return false;
            if (!CanConnect(to, out string reason))
            {
                EventBus.Publish(new NotificationEvent(reason, NotificationSeverity.Warning));
                CancelWire();
                return false;
            }

            IConnectable a = _wireStart;
            Color color = Wire.ColorFor(a.TerminalKind, FunctionOf(a), a.DisplayLabel);

            DestroyPreview();
            _wireStart = null;

            Wire wire = CreateWireObject();
            wire.name = "Wire";
            wire.SetEndpoints(a, to, color);
            _wires.Add(wire);

            if (!_graph.AddWire(a.TerminalId, to.TerminalId, wire))
            {
                _wires.Remove(wire);
                Destroy(wire.gameObject);
                EventBus.Publish(new NotificationEvent("Could not create that connection.", NotificationSeverity.Error));
                return false;
            }

            EventBus.Publish(new WireChangedEvent(_wires.Count));
            SolveAndRefresh();
            return true;
        }

        public void CancelWire()
        {
            DestroyPreview();
            _wireStart = null;
        }

        private void DestroyPreview()
        {
            if (_wirePreview != null)
            {
                Destroy(_wirePreview.gameObject);
                _wirePreview = null;
            }
            if (_wireTip != null)
            {
                Destroy(_wireTip);
                _wireTip = null;
            }
        }

        private Wire CreateWireObject()
        {
            var go = new GameObject("Wire");
            go.transform.SetParent(labRoot != null ? labRoot : transform, true);
            return go.AddComponent<Wire>();
        }

        private static PinFunction FunctionOf(IConnectable c) => c is ComponentPin p ? p.Function : PinFunction.DataInput;

        public bool DeleteWire(Wire w)
        {
            if (w == null) return false;
            if (!_wires.Remove(w)) return false;
            _graph.RemoveWireByRef(w);
            Destroy(w.gameObject);
            EventBus.Publish(new WireChangedEvent(_wires.Count));
            SolveAndRefresh();
            return true;
        }

        public void ClearWires()
        {
            for (int i = _wires.Count - 1; i >= 0; i--)
            {
                Wire w = _wires[i];
                if (w == null) continue;
                _graph.RemoveWireByRef(w);
                Destroy(w.gameObject);
            }
            _wires.Clear();
            EventBus.Publish(new WireChangedEvent(0));
        }

        // ------------------------------------------------------------------ simulation

        public SimulationOutcome SolveAndRefresh()
        {
            EnsureSimulator();
            SimulationOutcome outcome = _sim.Solve();
            RefreshVisuals();
            return outcome;
        }

        /// <summary>Pushes solved net values onto every pin and indicator.</summary>
        public void RefreshVisuals()
        {
            EnsureSimulator();
            for (int i = 0; i < _components.Count; i++)
            {
                if (_components[i] is LedIndicator led) led.RefreshVisual();
            }

            for (int i = 0; i < _components.Count; i++)
            {
                IElectronicComponent c = _components[i];
                IReadOnlyList<ComponentPin> pins = c.Pins;
                for (int p = 0; p < pins.Count; p++)
                {
                    ComponentPin pin = pins[p];
                    if (pin == null || !pin.IsDataInput) continue;
                    if (!HasActiveWire(pin.TerminalId))
                    {
                        pin.SetLogicState(LogicValue.Undefined, c.IsPowered);
                        continue;
                    }
                    pin.SetLogicState(_sim.ValueOfTerminal(pin.TerminalId), c.IsPowered);
                }
            }
        }

        private bool HasActiveWire(int terminalId)
        {
            Net net = _graph.NetOf(terminalId);
            return net != null && net.Terminals.Count > 1;
        }
        // ------------------------------------------------------------------ power

        public void TogglePower() { if (powerSupply != null) powerSupply.Toggle(); }

        public void ToggleSelectedSwitch()
        {
            if (_selected is InputSwitch s) s.Toggle();
        }

        public void DeleteSelected()
        {
            if (_selected != null) Remove(_selected);
        }

        // ------------------------------------------------------------------ inspector data

        /// <summary>Builds the inspector rows for the current selection.</summary>
        public void FillInspector(List<string> lines)
        {
            lines.Clear();
            if (_selected == null)
            {
                lines.Add("No component selected.");
                return;
            }

            lines.Add(_selected.DisplayName);
            lines.Add("Part: " + _selected.PartNumber);
            if (!string.IsNullOrEmpty(_selected.Description)) lines.Add(_selected.Description);
            lines.Add("Pins: " + _selected.Pins.Count);
            lines.Add("Power: " + (_selected.IsPowered ? "connected" : "NOT connected"));

            if (_selected is IcComponent ic)
            {
                lines.Add(string.Empty);
                lines.Add("Pin mapping");
                ic.CollectPinMap(_pinMapScratch);
                for (int i = 0; i < _pinMapScratch.Count; i++) lines.Add("  " + _pinMapScratch[i]);
                lines.Add(string.Empty);
                lines.Add("Gates");
                for (int i = 0; i < ic.Definition.Gates.Length; i++)
                    lines.Add("  " + ic.Definition.EquationFor(i));
            }
            else if (_selected is LogicGate gate)
            {
                lines.Add(string.Empty);
                lines.Add("Equation: " + gate.DescribeMapping());
            }
            else if (_selected is InputSwitch sw)
            {
                lines.Add(string.Empty);
                lines.Add("Position: " + (sw.Position == LogicValue.High ? "HIGH (1)" : "LOW (0)"));
            }
            else if (_selected is LedIndicator led)
            {
                lines.Add(string.Empty);
                lines.Add("Output: " + led.State + " (" + LogicOps.ToChar(led.State) + ")");
            }

            lines.Add(string.Empty);
            int connections = CountConnections(_selected);
            lines.Add("Connections: " + connections);
        }

        private int CountConnections(IElectronicComponent c)
        {
            int n = 0;
            IReadOnlyList<ComponentPin> pins = c.Pins;
            for (int i = 0; i < pins.Count; i++)
            {
                ComponentPin p = pins[i];
                if (p == null) continue;
                Net net = _graph.NetOf(p.TerminalId);
                if (net == null) continue;
                n += Mathf.Max(0, net.Terminals.Count - 1);
            }
            return n;
        }

        // ------------------------------------------------------------------ reset

        /// <summary>
        /// Full reset: every wire and every placed component is removed and the supply is
        /// switched off, returning the bench to its just-placed state.
        /// </summary>
        public void ResetExperiment()
        {
            CancelWire();
            ClearWires();
            Select(null);

            if (powerSupply != null) powerSupply.SetEnergised(false);

            // Remove everything the student added, keeping the built-in supply.
            for (int i = _components.Count - 1; i >= 0; i--)
            {
                IElectronicComponent c = _components[i];
                if (c == null) continue;
                if (powerSupply != null && ReferenceEquals(c, powerSupply)) continue;
                Remove(c);
            }
            _components.RemoveAll(c => c == null);

            SolveAndRefresh();
            EventBus.Publish(new LabResetEvent());
        }
    }
}
