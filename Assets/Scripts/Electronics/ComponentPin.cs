using System;
using UnityEngine;

namespace ARLab.Electronics
{
    public enum PinDirection { Input, Output, Power }

    /// <summary>What a physical pin does on its device, for the inspector and validator.</summary>
    public enum PinFunction
    {
        DataInput,
        DataOutput,
        Vcc,
        Gnd,
        /// <summary>Physically present but unconnected inside the device (e.g. DIP pin 5 on 7408).</summary>
        NotConnected
    }

    /// <summary>
    /// A single physical lead on a component. Owns one graph terminal, so wiring a pin
    /// is really joining two terminals in the netlist — never a proximity guess.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ComponentPin : MonoBehaviour, IConnectable
    {
        [SerializeField] private int pinNumber;
        [SerializeField] private string signalName = "P";
        [SerializeField] private PinDirection direction = PinDirection.Input;
        [SerializeField] private PinFunction function = PinFunction.DataInput;
        [SerializeField, HideInInspector] private int terminalId = -1;

        private Collider _collider;
        private Renderer _renderer;
        private MaterialPropertyBlock _mpb;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        public int PinNumber => pinNumber;
        public string SignalName => signalName;
        public PinDirection Direction => direction;
        public PinFunction Function => function;
        public int TerminalId => terminalId;

        // IConnectable
        public TerminalKind TerminalKind => TerminalKind.ComponentPin;
        public Vector3 AnchorWorld => transform.position;
        public string DisplayLabel => $"{signalName} (pin {pinNumber})";
        public IElectronicComponent Owner => GetComponentInParent<ElectronicComponent>();

        public bool IsDataInput => function == PinFunction.DataInput;
        public bool IsDataOutput => function == PinFunction.DataOutput;
        public bool IsSupply => function == PinFunction.Vcc || function == PinFunction.Gnd;

        /// <summary>False for a pin the simulation is not allowed to drive or read, e.g. NC.</summary>
        public bool IsConnectable => function != PinFunction.NotConnected;

        /// <summary>Set by the owner when the component registers with the netlist.</summary>
        public void Bind(int terminalId) => this.terminalId = terminalId;

        public void Configure(int number, string signal, PinDirection dir, PinFunction fn)
        {
            pinNumber = number;
            signalName = signal;
            direction = dir;
            function = fn;
        }

        private void Awake()
        {
            _collider = GetComponent<Collider>();
            _renderer = GetComponentInChildren<Renderer>();
            _mpb = new MaterialPropertyBlock();
        }

        /// <summary>
        /// Interaction feedback. Green = legal target, red = illegal, amber = preview.
        /// Driven by the wire builder so the student always knows what a tap will do.
        /// </summary>
        public void SetHighlight(HighlightKind kind)
        {
            if (_renderer == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();

            Color c;
            switch (kind)
            {
                case HighlightKind.Valid: c = new Color(0.20f, 0.95f, 0.45f); break;
                case HighlightKind.Invalid: c = new Color(1.00f, 0.30f, 0.30f); break;
                case HighlightKind.Preview: c = new Color(1.00f, 0.78f, 0.25f); break;
                default: c = new Color(0.55f, 0.60f, 0.68f); break;
            }

            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, c);
            _mpb.SetColor(EmissionColorId, c * 0.85f);
            _renderer.SetPropertyBlock(_mpb);
        }

        /// <summary>Live drive indicator: HIGH glows green, LOW dims blue, Undefined pulses amber.</summary>
        public void SetLogicState(LogicValue value, bool powered)
        {
            if (_renderer == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();

            Color c;
            if (!powered) c = new Color(0.22f, 0.22f, 0.24f);
            else if (value == LogicValue.High) c = new Color(0.25f, 1.00f, 0.40f);
            else if (value == LogicValue.Low) c = new Color(0.20f, 0.35f, 0.85f);
            else c = new Color(1.00f, 0.62f, 0.10f);

            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, c);
            _mpb.SetColor(EmissionColorId, c);
            _renderer.SetPropertyBlock(_mpb);
        }

        public bool HasCollider => _collider != null;
    }

    public enum HighlightKind { None, Valid, Invalid, Preview }
}
