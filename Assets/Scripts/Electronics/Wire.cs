using System;
using UnityEngine;
using ARLab.Core;

namespace ARLab.Electronics
{
    /// <summary>Anything a wire can terminate on: an IC pin, a breadboard hole, or a rail post.</summary>
    public interface IConnectable
    {
        int TerminalId { get; }
        Vector3 AnchorWorld { get; }
        string DisplayLabel { get; }
        TerminalKind TerminalKind { get; }
        /// <summary>The component that owns this terminal, or null for breadboard sockets.</summary>
        IElectronicComponent Owner { get; }
    }

    /// <summary>
    /// A user-created jumper wire. Owns a procedural tube mesh, follows its endpoints as
    /// components are moved, and is fully removable from the netlist.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Wire : MonoBehaviour, IConnectable
    {
        [SerializeField] private int terminalA = -1;
        [SerializeField] private int terminalB = -1;
        [SerializeField] private Color color = new Color(0.95f, 0.82f, 0.25f);
        [SerializeField] private float radius = 0.0045f;
        [SerializeField] private float sag = 0.05f;
        [SerializeField] private int segments = 18;
        [SerializeField] private int sides = 6;

        private IConnectable _a;
        private IConnectable _b;
        private Transform _followB;
        private Vector3 _lastA, _lastB;
        private bool _dirty = true;
        private bool _preview;

        private MeshFilter _filter;
        private MeshRenderer _renderer;
        private Mesh _mesh;
        private readonly System.Collections.Generic.List<Vector3> _v = new System.Collections.Generic.List<Vector3>(128);
        private readonly System.Collections.Generic.List<Vector3> _n = new System.Collections.Generic.List<Vector3>(128);
        private readonly System.Collections.Generic.List<Color> _c = new System.Collections.Generic.List<Color>(128);
        private readonly System.Collections.Generic.List<int> _t = new System.Collections.Generic.List<int>(256);

        public int TerminalA => terminalA;
        public int TerminalB => terminalB;
        public int TerminalId => terminalA;
        public Vector3 AnchorWorld => transform.position;
        public string DisplayLabel => "Wire";
        public TerminalKind TerminalKind => TerminalKind.ComponentPin;
        public IElectronicComponent Owner => null;
        public Color Color => color;
        public bool IsPreview => _preview;

        public IConnectable EndpointA => _a;
        public IConnectable EndpointB => _b;

        private void Awake()
        {
            _filter = GetComponent<MeshFilter>();
            _renderer = GetComponent<MeshRenderer>();
            if (_filter == null) _filter = gameObject.AddComponent<MeshFilter>();
            if (_renderer == null) _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _lastA = _lastB = Vector3.zero;
            _dirty = true;
        }

        public void SetEndpoints(IConnectable a, IConnectable b, Color wireColor)
        {
            _a = a;
            _b = b;
            terminalA = a != null ? a.TerminalId : -1;
            terminalB = b != null ? b.TerminalId : -1;
            color = wireColor;
            _dirty = true;
        }

        /// <summary>Used by the in-progress wire: B trails a moving transform.</summary>
        public void SetPreviewTarget(Transform target)
        {
            _followB = target;
            _preview = true;
            _dirty = true;
        }

        public void ClearPreview()
        {
            _followB = null;
            _preview = false;
            _dirty = true;
        }

        /// <summary>Standard patch-cord colour for the two roles, so wiring reads at a glance.</summary>
        public static Color ColorFor(TerminalKind kind, PinFunction fn, string label)
        {
            if (fn == PinFunction.Vcc) return new Color(0.90f, 0.22f, 0.22f);
            if (fn == PinFunction.Gnd) return new Color(0.13f, 0.13f, 0.15f);
            if (kind == TerminalKind.PowerRail) return new Color(0.55f, 0.60f, 0.68f);

            // Stable hue per signal name so A, A', F etc. are visually distinct.
            if (!string.IsNullOrEmpty(label))
            {
                int h = Mathf.Abs(label.GetHashCode() % 360);
                return Color.HSVToRGB(h / 360f, 0.55f, 0.95f);
            }
            return new Color(0.30f, 0.70f, 0.95f);
        }

        private void LateUpdate()
        {
            if (_a == null && _b == null && _followB == null) return;

            Vector3 pa = _a != null ? _a.AnchorWorld : transform.position;
            Vector3 pb = _b != null ? _b.AnchorWorld : (_followB != null ? _followB.position : transform.position);

            if ((pa - _lastA).sqrMagnitude > 1e-8f || (pb - _lastB).sqrMagnitude > 1e-8f)
            {
                _lastA = pa;
                _lastB = pb;
                _dirty = true;
            }

            if (_dirty) Rebuild(pa, pb);
        }

        private void Rebuild(Vector3 a, Vector3 b)
        {
            _dirty = false;
            if (_mesh != null) Destroy(_mesh);
            _mesh = WireMeshBuilder.Build(a, b, sag, radius, segments, sides, _v, _n, _c, _t);
            _filter.sharedMesh = _mesh;
            if (_renderer != null && _renderer.sharedMaterial != null)
            {
                _renderer.material.color = color;
            }
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }
    }
}
