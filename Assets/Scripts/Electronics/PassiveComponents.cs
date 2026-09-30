using System;
using UnityEngine;
using ARLab.Core;

namespace ARLab.Electronics
{
    /// <summary>
    /// SPDT input switch. Physically it selects between the +5V rail and the GND rail, so
    /// when the supply is off its output is genuinely Undefined rather than a fake 0.
    /// </summary>
    public sealed class InputSwitch : ElectronicComponent
    {
        [SerializeField] private string signalName = "A";
        [SerializeField] private LogicValue position = LogicValue.Low;

        private int _out = -1;
        private int _vcc = -1;
        private int _gnd = -1;

        public override bool IsDrivenSource => true;
        public string SignalName => signalName;
        public LogicValue Position => position;

        public void Configure(string signal)
        {
            signalName = signal;
            displayName = $"Input {signal}";
            partNumber = "SW-SPST";
            description = "Single-pole input switch. Selects the output between +5V and GND.";
        }

        protected override void Awake()
        {
            Configure(signalName);
            base.Awake();
        }

        protected override void OnRegistered(ConnectionGraph graph)
        {
            _out = TerminalOf("OUT");
            _vcc = TerminalOf("VCC");
            _gnd = TerminalOf("GND");
        }

        protected override void OnUnregistered(ConnectionGraph graph) { _out = _vcc = _gnd = -1; }

        public void SetPosition(LogicValue v)
        {
            LogicValue nv = v == LogicValue.High ? LogicValue.High : LogicValue.Low;
            if (position == nv) return;
            position = nv;
            EventBus.Publish(new InputSwitchChangedEvent(signalName, position));
        }

        public void Toggle() => SetPosition(position == LogicValue.High ? LogicValue.Low : LogicValue.High);

        public override void Evaluate(INetReader reader, IDriverSink sink)
        {
            if (_out < 0) return;
            bool railOk = reader.ValueOfTerminal(_vcc) == LogicValue.High &&
                          reader.ValueOfTerminal(_gnd) == LogicValue.Low;
            sink.Drive(_out, railOk ? position : LogicValue.Undefined);
        }
    }

    /// <summary>Logic output indicator. Lit strictly from the simulated net value.</summary>
    public sealed class LedIndicator : ElectronicComponent
    {
        [SerializeField] private string signalName = "F";
        [SerializeField] private Color onColor = new Color(0.25f, 1f, 0.40f);
        [SerializeField] private Color offColor = new Color(0.16f, 0.18f, 0.20f);

        private int _anode = -1;
        private Renderer _lens;
        private MaterialPropertyBlock _mpb;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        public override bool IsDrivenSource => false;
        public string SignalName => signalName;

        /// <summary>Last simulated state, for the inspector and tests.</summary>
        public LogicValue State { get; private set; } = LogicValue.Undefined;

        public void Configure(string signal)
        {
            signalName = signal;
            displayName = $"LED {signal}";
            partNumber = "LED-5MM";
            description = "Output indicator. Lights on Logic HIGH, dark on LOW, amber when undefined.";
        }

        protected override void Awake()
        {
            Configure(signalName);
            base.Awake();
            _lens = transform.Find("Lens") != null ? transform.Find("Lens").GetComponent<Renderer>() : GetComponentInChildren<Renderer>();
            _mpb = new MaterialPropertyBlock();
        }

        protected override void OnRegistered(ConnectionGraph graph) => _anode = TerminalOf("ANODE");
        protected override void OnUnregistered(ConnectionGraph graph) => _anode = -1;

        public override void Evaluate(INetReader reader, IDriverSink sink)
        {
            if (_anode < 0) return;
            State = reader.ValueOfTerminal(_anode);
            ApplyVisual();
        }

        private void ApplyVisual()
        {
            if (_lens == null) return;
            Color c;
            switch (State)
            {
                case LogicValue.High: c = onColor; break;
                case LogicValue.Low: c = offColor; break;
                default: c = new Color(0.85f, 0.55f, 0.10f); break;
            }
            _lens.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, c);
            _lens.SetPropertyBlock(_mpb);
            if (_lens.material != null && _lens.material.HasProperty(EmissionId))
            {
                var m = _lens.material;
                m.SetColor(EmissionId, State == LogicValue.High ? c * 2.2f : c * 0.15f);
                if (m.HasProperty("_EmissionColor")) m.EnableKeyword("_EMISSION");
            }
        }

        /// <summary>Re-applies the visual after a solve without re-running logic.</summary>
        public void RefreshVisual() => ApplyVisual();
    }

    /// <summary>Test probe: reports the level on any terminal without driving it.</summary>
    public sealed class LogicProbe : ElectronicComponent
    {
        [SerializeField] private string label = "PROBE";

        private int _probe = -1;
        public LogicValue Reading { get; private set; } = LogicValue.Undefined;

        public void Configure(string name)
        {
            label = name;
            displayName = $"Logic Probe {name}";
            partNumber = "LOGIC-PROBE";
            description = "Non-invasive probe. Displays the logic level present on the attached net.";
        }

        protected override void Awake()
        {
            Configure(label);
            base.Awake();
        }

        protected override void OnRegistered(ConnectionGraph graph) => _probe = TerminalOf("PROBE");
        protected override void OnUnregistered(ConnectionGraph graph) => _probe = -1;

        public override void Evaluate(INetReader reader, IDriverSink sink)
        {
            if (_probe < 0) return;
            Reading = reader.ValueOfTerminal(_probe);
        }
    }
}
