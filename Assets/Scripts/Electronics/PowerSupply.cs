using UnityEngine;
using ARLab.Core;

namespace ARLab.Electronics
{
    /// <summary>
    /// Bench DC supply. It is the root of the electrical model: while OFF both rails are
    /// Undefined, which is what makes every downstream gate read Undefined instead of 0.
    /// </summary>
    public sealed class PowerSupply : ElectronicComponent
    {
        public const float NominalVolts = 5f;

        [SerializeField] private bool energised = false;
        [SerializeField, Range(0f, 8f)] private float volts = NominalVolts;
        [SerializeField] private bool showReadout = true;

        private int _vccOut = -1;
        private int _gndOut = -1;
        private Transform _indicator;
        private TextMesh _readout;
        private Renderer _indicatorRenderer;
        private MaterialPropertyBlock _mpb;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        public override bool IsDrivenSource => true;
        public bool Energised => energised;
        public float Volts => energised ? volts : 0f;
        /// <summary>Terminal the supply drives +5V onto, distinct from its own Vcc input pin.</summary>
        public int VccOutputTerminal => _vccOut;
        /// <summary>Terminal the supply drives 0V onto.</summary>
        public int GndOutputTerminal => _gndOut;

        protected override void Awake()
        {
            displayName = "DC Power Supply";
            partNumber = "PSU-5V";
            description = "Bench supply providing +5V and GND. Outputs are undefined while switched off.";
            base.Awake();

            _indicator = transform.Find("Indicator");
            if (_indicator != null) _indicatorRenderer = _indicator.GetComponent<Renderer>();
            var readoutTf = transform.Find("Readout");
            if (readoutTf != null) _readout = readoutTf.GetComponent<TextMesh>();
            _mpb = new MaterialPropertyBlock();
        }

        protected override void OnRegistered(ConnectionGraph graph)
        {
            _vccOut = TerminalOf("VCC_OUT");
            _gndOut = TerminalOf("GND_OUT");
        }

        protected override void OnUnregistered(ConnectionGraph graph) { _vccOut = _gndOut = -1; }

        public void SetEnergised(bool on)
        {
            if (energised == on) return;
            energised = on;
            ApplyVisual();
            EventBus.Publish(new PowerChangedEvent(on, Volts));
        }

        public void Toggle() => SetEnergised(!energised);

        public override void Evaluate(INetReader reader, IDriverSink sink)
        {
            if (energised)
            {
                if (_vccOut >= 0) sink.Drive(_vccOut, LogicValue.High);
                if (_gndOut >= 0) sink.Drive(_gndOut, LogicValue.Low);
            }
            else
            {
                if (_vccOut >= 0) sink.Drive(_vccOut, LogicValue.Undefined);
                if (_gndOut >= 0) sink.Drive(_gndOut, LogicValue.Undefined);
            }
        }

        public void ApplyVisual()
        {
            if (_indicatorRenderer != null)
            {
                Color c = energised ? new Color(0.25f, 1f, 0.45f) : new Color(0.20f, 0.21f, 0.23f);
                _indicatorRenderer.GetPropertyBlock(_mpb);
                _mpb.SetColor(BaseColorId, c);
                _indicatorRenderer.SetPropertyBlock(_mpb);
                if (_indicatorRenderer.material != null && _indicatorRenderer.material.HasProperty(EmissionId))
                {
                    _indicatorRenderer.material.SetColor(EmissionId, energised ? c * 2.4f : Color.black);
                }
            }

            if (showReadout && _readout != null)
            {
                _readout.text = energised ? $"{volts:0.0} V" : "0.0 V  OFF";
                _readout.color = energised ? new Color(0.35f, 1f, 0.55f) : new Color(0.75f, 0.35f, 0.35f);
            }
        }

        /// <summary>TTL parts in this lab are specified at 5V; surfaced by the validator.</summary>
        public bool IsSufficientVoltage => volts >= NominalVolts - 0.35f;
    }
}
