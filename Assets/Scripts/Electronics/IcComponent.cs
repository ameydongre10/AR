using System;
using System.Collections.Generic;
using UnityEngine;

namespace ARLab.Electronics
{
    /// <summary>
    /// A multi-gate TTL package (7404 / 7408 / 7432). Every gate inside evaluates
    /// independently from its own pins, exactly as the silicon does.
    /// </summary>
    public sealed class IcComponent : ElectronicComponent
    {
        [SerializeField] private string definitionPart = "7408";

        private IcDefinition _def;
        private int[] _inA = Array.Empty<int>();
        private int[] _inB = Array.Empty<int>();
        private int[] _out = Array.Empty<int>();

        public IcDefinition Definition => _def;
        public string DefinitionPart => definitionPart;

        public void Configure(IcDefinition def)
        {
            _def = def;
            definitionPart = def.PartNumber;
            displayName = def.Name;
            partNumber = def.PartNumber;
            description = def.Description;
        }

        public void Configure(string partNumber)
        {
            IcDefinition def = IcDefinitions.ByPartNumber(partNumber);
            if (def != null) Configure(def);
        }

        private void EnsureDefinition()
        {
            if (_def == null || _def.PartNumber != definitionPart)
            {
                _def = IcDefinitions.ByPartNumber(definitionPart) ?? IcDefinitions.Quad7408;
                displayName = _def.Name;
                partNumber = _def.PartNumber;
                description = _def.Description;
            }
        }

        protected override void Awake()
        {
            EnsureDefinition();
            base.Awake();
        }

        protected override void OnRegistered(ConnectionGraph graph)
        {
            EnsureDefinition();
            int n = _def.Gates.Length;
            if (_inA.Length != n) { _inA = new int[n]; _inB = new int[n]; _out = new int[n]; }

            for (int i = 0; i < n; i++)
            {
                GateSpec g = _def.Gates[i];
                _inA[i] = TerminalOf(g.InA);
                _inB[i] = _def.GateKind == LogicGateKind.Not ? _inA[i] : TerminalOf(g.InB);
                _out[i] = TerminalOf(g.Out);
            }
        }

        protected override void OnUnregistered(ConnectionGraph graph)
        {
            Array.Clear(_inA, 0, _inA.Length);
            Array.Clear(_inB, 0, _inB.Length);
            Array.Clear(_out, 0, _out.Length);
        }

        /// <summary>Gates whose output currently reads Undefined while the package is powered.</summary>
        public int UndefinedGateCount(INetReader reader)
        {
            EnsureDefinition();
            if (!IsPowered) return _def.Gates.Length;
            int c = 0;
            for (int i = 0; i < _out.Length; i++)
            {
                if (_out[i] < 0) continue;
                if (reader.ValueOfTerminal(_out[i]) == LogicValue.Undefined) c++;
            }
            return c;
        }

        public override void Evaluate(INetReader reader, IDriverSink sink)
        {
            EnsureDefinition();
            if (!IsPowered)
            {
                for (int i = 0; i < _out.Length; i++)
                    if (_out[i] >= 0) sink.Drive(_out[i], LogicValue.Undefined);
                return;
            }

            for (int i = 0; i < _out.Length; i++)
            {
                if (_out[i] < 0) continue;
                LogicValue a = reader.ValueOfTerminal(_inA[i]);
                LogicValue b = _def.GateKind == LogicGateKind.Not
                    ? a
                    : reader.ValueOfTerminal(_inB[i]);
                sink.Drive(_out[i], EvaluateGate(_def.GateKind, a, b));
            }
        }

        private static LogicValue EvaluateGate(LogicGateKind kind, LogicValue a, LogicValue b)
        {
            switch (kind)
            {
                case LogicGateKind.Not: return LogicOps.Not(a);
                case LogicGateKind.And: return LogicOps.And(a, b);
                default: return LogicOps.Or(a, b);
            }
        }

        /// <summary>Full pin map for the inspector panel, in physical pin order.</summary>
        public void CollectPinMap(List<string> lines)
        {
            EnsureDefinition();
            lines.Clear();
            for (int pin = 1; pin <= 14; pin++)
            {
                string signal = _def.SignalAtPin(pin);
                if (signal == null) continue;
                ComponentPin p = FindPin(signal);
                string role = p == null ? "?" : (p.IsSupply ? "POWER" : p.Direction.ToString().ToUpperInvariant());
                lines.Add($"Pin {pin,2}  {signal,-4}  {role}");
            }
        }
    }
}
