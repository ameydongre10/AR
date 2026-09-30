using System.Collections.Generic;

namespace ARLab.Electronics
{
    /// <summary>One physical gate inside a quad/hex package.</summary>
    public sealed class GateSpec
    {
        /// <summary>Output signal name, e.g. "1Y".</summary>
        public string Out;
        /// <summary>First input signal name, e.g. "1A".</summary>
        public string InA;
        /// <summary>Second input signal name. Equals <see cref="InA"/> for inverters.</summary>
        public string InB;
        /// <summary>Zero-based gate index within the package.</summary>
        public int Index;

        public GateSpec(int index, string inA, string inB, string output)
        {
            Index = index;
            InA = inA;
            InB = inB;
            Out = output;
        }
    }

    /// <summary>Full physical pinout of a 14-pin TTL package.</summary>
    public sealed class IcDefinition
    {
        public string PartNumber;
        public string Name;
        public string Description;
        public LogicGateKind GateKind;
        public GateSpec[] Gates;

        /// <summary>Signal name ("1A", "GND", "VCC") to physical DIP pin number (1-14).</summary>
        public readonly Dictionary<string, int> PinNumber = new Dictionary<string, int>();

        public int VccPin => PinNumber.TryGetValue("VCC", out int v) ? v : 14;
        public int GndPin => PinNumber.TryGetValue("GND", out int g) ? g : 7;

        /// <summary>All data-pin signal names, in DIP pin order.</summary>
        public readonly List<string> SignalOrder = new List<string>();

        public int PinOf(string signal) => PinNumber.TryGetValue(signal, out int p) ? p : -1;

        public string EquationFor(int gateIndex)
        {
            GateSpec g = Gates[gateIndex];
            switch (GateKind)
            {
                case LogicGateKind.Not: return $"Y{g.Index + 1} = NOT A{g.Index + 1}";
                case LogicGateKind.And: return $"Y{g.Index + 1} = A{g.Index + 1} AND B{g.Index + 1}";
                default: return $"Y{g.Index + 1} = A{g.Index + 1} OR B{g.Index + 1}";
            }
        }

        /// <summary>Signal occupying a given physical pin, or null.</summary>
        public string SignalAtPin(int pin)
        {
            foreach (KeyValuePair<string, int> kv in PinNumber)
                if (kv.Value == pin) return kv.Key;
            return null;
        }
    }

    /// <summary>
    /// Authoritative TTL pinouts. These are the real SN7404/7408/7432 assignments; getting
    /// them wrong would silently produce wrong logic, so they live in one audited place.
    /// </summary>
    public static class IcDefinitions
    {
        // 7404 hex inverter: 1A/1Y, 2A/2Y, 3A/3Y on pins 1-6; gate 4 is mirrored onto 8/9,
        // 5 onto 10/11 and 6 onto 12/13. GND is pin 7, VCC is pin 14.
        private static readonly GateSpec[] InverterGates =
        {
            new GateSpec(0, "1A", "1A", "1Y"),
            new GateSpec(1, "2A", "2A", "2Y"),
            new GateSpec(2, "3A", "3A", "3Y"),
            new GateSpec(3, "4A", "4A", "4Y"),
            new GateSpec(4, "5A", "5A", "5Y"),
            new GateSpec(5, "6A", "6A", "6Y")
        };

        // 7408 / 7432 quads share an identical 14-pin assignment.
        private static readonly GateSpec[] QuadGates =
        {
            new GateSpec(0, "1A", "1B", "1Y"),
            new GateSpec(1, "2A", "2B", "2Y"),
            new GateSpec(2, "3A", "3B", "3Y"),
            new GateSpec(3, "4A", "4B", "4Y")
        };

        // Declared above every field that reads it: static initialisers run in textual order,
        // so a pin map defined after its use is still null at that point and the type
        // initializer throws before any pinout is usable.
        private static readonly (string, int)[] QuadPinMap =
        {
            ("1A",1), ("1B",2), ("1Y",3), ("2A",4), ("2B",5), ("2Y",6),
            ("GND",7), ("3Y",8), ("3A",9), ("3B",10), ("4Y",11), ("4A",12),
            ("4B",13), ("VCC",14)
        };

        public static readonly IcDefinition Hex7404 = Build(new IcDefinition
        {
            PartNumber = "7404",
            Name = "Hex Inverter (NOT)",
            Description = "Six independent inverters in a 14-pin DIP. Y = NOT A.",
            GateKind = LogicGateKind.Not,
            Gates = InverterGates
        }, new (string, int)[]
        {
            ("1A",1), ("1Y",2), ("2A",3), ("2Y",4), ("3A",5), ("3Y",6),
            ("GND",7), ("4Y",8), ("4A",9), ("5Y",10), ("5A",11), ("6Y",12),
            ("6A",13), ("VCC",14)
        });

        public static readonly IcDefinition Quad7408 = Build(new IcDefinition
        {
            PartNumber = "7408",
            Name = "Quad 2-input AND",
            Description = "Four independent 2-input AND gates in a 14-pin DIP. Y = A AND B.",
            GateKind = LogicGateKind.And,
            Gates = QuadGates
        }, QuadPinMap);

        public static readonly IcDefinition Quad7432 = Build(new IcDefinition
        {
            PartNumber = "7432",
            Name = "Quad 2-input OR",
            Description = "Four independent 2-input OR gates in a 14-pin DIP. Y = A OR B.",
            GateKind = LogicGateKind.Or,
            Gates = QuadGates
        }, QuadPinMap);

        private static IcDefinition Build(IcDefinition def, (string signal, int pin)[] map)
        {
            for (int i = 0; i < map.Length; i++)
            {
                def.PinNumber[map[i].signal] = map[i].pin;
                def.SignalOrder.Add(map[i].signal);
            }
            return def;
        }

        public static readonly IcDefinition[] All = { Hex7404, Quad7408, Quad7432 };

        public static IcDefinition ByPartNumber(string part)
        {
            if (string.IsNullOrEmpty(part)) return null;
            for (int i = 0; i < All.Length; i++)
                if (All[i].PartNumber == part) return All[i];
            return null;
        }
    }
}
