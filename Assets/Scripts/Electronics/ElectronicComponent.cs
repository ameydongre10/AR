using System;
using System.Collections.Generic;
using UnityEngine;

namespace ARLab.Electronics
{
    /// <summary>Read-only view of net values handed to components during evaluation.</summary>
    public interface INetReader
    {
        LogicValue ValueOfTerminal(int terminalId);
        LogicValue ValueOfNet(int netId);
        bool TryGetNet(int terminalId, out Net net);
    }

    /// <summary>Collects what each component wants to drive, so contention can be detected centrally.</summary>
    public interface IDriverSink
    {
        void Drive(int terminalId, LogicValue value);
    }

    public interface IElectronicComponent
    {
        string DisplayName { get; }
        string PartNumber { get; }
        string Description { get; }
        IReadOnlyList<ComponentPin> Pins { get; }
        bool IsPowered { get; }
        bool IsDrivenSource { get; }
        /// <summary>Exposed so placement code can move any part through the interface.</summary>
        Transform Transform { get; }
        GameObject GameObject { get; }
        void Register(ConnectionGraph graph);
        void Unregister(ConnectionGraph graph);
        void RefreshPower(INetReader reader);
        void Evaluate(INetReader reader, IDriverSink sink);
    }

    /// <summary>
    /// Base for every placeable part. Owns its pins, registers one netlist terminal per pin,
    /// and caches the supply terminals so power checks cost no allocations.
    /// </summary>
    public abstract class ElectronicComponent : MonoBehaviour, IElectronicComponent
    {
        [SerializeField] protected string displayName = "Component";
        [SerializeField] protected string partNumber = "—";
        [SerializeField, TextArea] protected string description = "";
        [SerializeField] protected ComponentPin[] pinArray = Array.Empty<ComponentPin>();

        private readonly List<ComponentPin> _pins = new List<ComponentPin>(16);
        private int _vccTerminal = -1;
        private int _gndTerminal = -1;
        private bool _registered;

        public bool IsPowered { get; private set; }
        public virtual bool IsDrivenSource => false;

        Transform IElectronicComponent.Transform => transform;
        GameObject IElectronicComponent.GameObject => gameObject;

        public string DisplayName => displayName;
        public string PartNumber => partNumber;
        public string Description => description;
        public IReadOnlyList<ComponentPin> Pins => _pins;
        public IReadOnlyList<ComponentPin> PinList => _pins;
        public int VccTerminal => _vccTerminal;
        public int GndTerminal => _gndTerminal;

        /// <summary>Set by the component library; drives the lab palette.</summary>
        public Color Tint { get; private set; } = new Color(0.82f, 0.84f, 0.88f);

        protected virtual void Awake()
        {
            _pins.Clear();
            if (pinArray != null)
            {
                for (int i = 0; i < pinArray.Length; i++)
                    if (pinArray[i] != null) _pins.Add(pinArray[i]);
            }
            if (_pins.Count == 0) _pins.AddRange(GetComponentsInChildren<ComponentPin>(true));
        }

        public void SetTint(Color c) => Tint = c;

        /// <summary>Explicitly assigns pins (used by the procedural factory and the editor builder).</summary>
        public void AssignPins(ComponentPin[] pins)
        {
            pinArray = pins ?? Array.Empty<ComponentPin>();
            _pins.Clear();
            for (int i = 0; i < pinArray.Length; i++)
                if (pinArray[i] != null) _pins.Add(pinArray[i]);
        }

        public virtual void Register(ConnectionGraph graph)
        {
            if (_registered) return;
            _registered = true;
            for (int i = 0; i < _pins.Count; i++)
            {
                ComponentPin p = _pins[i];
                if (p == null) continue;
                string label = $"{partNumber}.{p.SignalName}(pin{p.PinNumber})";
                int tid = graph.AddTerminal(TerminalKind.ComponentPin, label, this);
                p.Bind(tid);
                if (p.Function == PinFunction.Vcc) _vccTerminal = tid;
                else if (p.Function == PinFunction.Gnd) _gndTerminal = tid;
            }
            OnRegistered(graph);
            graph.Rebuild();
        }

        public virtual void Unregister(ConnectionGraph graph)
        {
            if (!_registered) return;
            _registered = false;
            for (int i = 0; i < _pins.Count; i++)
                if (_pins[i] != null) _pins[i].Bind(-1);
            _vccTerminal = -1;
            _gndTerminal = -1;
            OnUnregistered(graph);
        }

        protected virtual void OnRegistered(ConnectionGraph graph) { }
        protected virtual void OnUnregistered(ConnectionGraph graph) { }

        public virtual void RefreshPower(INetReader reader)
        {
            if (_vccTerminal < 0 || _gndTerminal < 0)
            {
                IsPowered = false;
                return;
            }
            bool vccHigh = reader.ValueOfTerminal(_vccTerminal) == LogicValue.High;
            bool gndLow = reader.ValueOfTerminal(_gndTerminal) == LogicValue.Low;
            IsPowered = vccHigh && gndLow;
        }

        public abstract void Evaluate(INetReader reader, IDriverSink sink);

        protected ComponentPin FindPin(string signal)
        {
            for (int i = 0; i < _pins.Count; i++)
                if (_pins[i] != null && _pins[i].SignalName == signal) return _pins[i];
            return null;
        }

        protected int TerminalOf(string signal)
        {
            ComponentPin p = FindPin(signal);
            return p != null ? p.TerminalId : -1;
        }

        public void SetPoweredExternal(bool value) => IsPowered = value;
    }
}
