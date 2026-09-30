using System;
using UnityEngine;

namespace ARLab.Electronics
{
    public enum LogicGateKind { Not, And, Or }

    /// <summary>
    /// A single discrete logic gate. Evaluates from net values; never from UI state.
    /// An unpowered gate drives <see cref="LogicValue.Undefined"/>, which is what makes the
    /// "power supply is OFF" case observable instead of silently reading 0.
    /// </summary>
    public sealed class LogicGate : ElectronicComponent
    {
        [SerializeField] private LogicGateKind kind = LogicGateKind.And;

        private int _inA = -1, _inB = -1, _out = -1;

        public LogicGateKind Kind => kind;
        public int InputATerminal => _inA;
        public int InputBTerminal => _inB;
        public int OutputTerminal => _out;

        public void Configure(LogicGateKind k, string label, string part)
        {
            kind = k;
            displayName = label;
            partNumber = part;
            _inA = _inB = _out = -1;
        }

        protected override void OnRegistered(ConnectionGraph graph)
        {
            _inA = TerminalOf("A");
            _inB = kind == LogicGateKind.Not ? _inA : TerminalOf("B");
            _out = TerminalOf("Y");
        }

        protected override void OnUnregistered(ConnectionGraph graph)
        {
            _inA = _inB = _out = -1;
        }

        /// <summary>Pure combinational function, exposed for unit tests and the truth-table sweep.</summary>
        public static LogicValue Compute(LogicGateKind kind, LogicValue a, LogicValue b)
        {
            switch (kind)
            {
                case LogicGateKind.Not: return LogicOps.Not(a);
                case LogicGateKind.And: return LogicOps.And(a, b);
                case LogicGateKind.Or: return LogicOps.Or(a, b);
                default: return LogicValue.Undefined;
            }
        }

        public override void Evaluate(INetReader reader, IDriverSink sink)
        {
            if (_out < 0) return;

            if (!IsPowered)
            {
                sink.Drive(_out, LogicValue.Undefined);
                return;
            }

            LogicValue a = reader.ValueOfTerminal(_inA);
            LogicValue b = kind == LogicGateKind.Not ? LogicValue.Low : reader.ValueOfTerminal(_inB);
            sink.Drive(_out, Compute(kind, a, b));
        }

        /// <summary>Human-readable wiring summary used by the inspector panel.</summary>
        public string DescribeMapping()
        {
            switch (kind)
            {
                case LogicGateKind.Not: return "Y = NOT A";
                case LogicGateKind.And: return "Y = A AND B";
                default: return "Y = A OR B";
            }
        }
    }
}
