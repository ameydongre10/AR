namespace ARLab.Electronics
{
    /// <summary>
    /// Three-valued digital logic level. <see cref="Undefined"/> represents a floating /
    /// undriven net, which is deliberately distinct from <see cref="Low"/> so the UI can
    /// warn the student instead of silently reading 0.
    /// </summary>
    public enum LogicValue
    {
        Low = 0,
        High = 1,
        Undefined = 2
    }

    public static class LogicOps
    {
        public static LogicValue Not(LogicValue a)
        {
            switch (a)
            {
                case LogicValue.Low: return LogicValue.High;
                case LogicValue.High: return LogicValue.Low;
                default: return LogicValue.Undefined;
            }
        }

        /// <summary>Dominant Low: any Low forces Low, else any Undefined forces Undefined.</summary>
        public static LogicValue And(LogicValue a, LogicValue b)
        {
            if (a == LogicValue.Low || b == LogicValue.Low) return LogicValue.Low;
            if (a == LogicValue.Undefined || b == LogicValue.Undefined) return LogicValue.Undefined;
            return LogicValue.High;
        }

        /// <summary>Dominant High: any High forces High, else any Undefined forces Undefined.</summary>
        public static LogicValue Or(LogicValue a, LogicValue b)
        {
            if (a == LogicValue.High || b == LogicValue.High) return LogicValue.High;
            if (a == LogicValue.Undefined || b == LogicValue.Undefined) return LogicValue.Undefined;
            return LogicValue.Low;
        }

        public static LogicValue And(LogicValue a, LogicValue b, LogicValue c)
            => And(And(a, b), c);

        public static LogicValue Or(LogicValue a, LogicValue b, LogicValue c)
            => Or(Or(a, b), c);

        public static char ToChar(LogicValue v)
        {
            switch (v)
            {
                case LogicValue.Low: return '0';
                case LogicValue.High: return '1';
                default: return 'x';
            }
        }

        public static bool IsDriven(LogicValue v) => v != LogicValue.Undefined;
    }
}
