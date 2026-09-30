using System.Collections.Generic;
using NUnit.Framework;
using ARLab.Electronics;

namespace ARLab.Tests
{
    /// <summary>
    /// Pinout tests. A wrong pin number here would produce a circuit that looks right in the
    /// inspector and computes the wrong function, so the real datasheet assignments are
    /// written out explicitly.
    /// </summary>
    public class IcDefinitionTests
    {
        [Test]
        public void AllThreePartsAreDefined()
        {
            Assert.IsNotNull(IcDefinitions.Hex7404);
            Assert.IsNotNull(IcDefinitions.Quad7408);
            Assert.IsNotNull(IcDefinitions.Quad7432);
            Assert.AreEqual(3, IcDefinitions.All.Length);
        }

        [Test]
        public void LookupByPartNumber()
        {
            Assert.AreEqual("7404", IcDefinitions.ByPartNumber("7404").PartNumber);
            Assert.AreEqual("7408", IcDefinitions.ByPartNumber("7408").PartNumber);
            Assert.AreEqual("7432", IcDefinitions.ByPartNumber("7432").PartNumber);
            Assert.IsNull(IcDefinitions.ByPartNumber("9999"));
            Assert.IsNull(IcDefinitions.ByPartNumber(null));
        }

        [Test]
        public void SupplyPinsAreSevenAndFourteen()
        {
            foreach (IcDefinition def in IcDefinitions.All)
            {
                Assert.AreEqual(7, def.GndPin, def.PartNumber + " GND pin");
                Assert.AreEqual(14, def.VccPin, def.PartNumber + " VCC pin");
            }
        }

        [Test]
        public void EverySignalResolvesToExactlyOnePin()
        {
            foreach (IcDefinition def in IcDefinitions.All)
            {
                var seen = new HashSet<int>();
                foreach (string signal in def.SignalOrder)
                {
                    int pin = def.PinOf(signal);
                    Assert.Greater(pin, 0, $"{def.PartNumber} {signal} has no pin");
                    Assert.LessOrEqual(pin, 14, $"{def.PartNumber} {signal} pin out of range");
                    Assert.IsTrue(seen.Add(pin), $"{def.PartNumber} pin {pin} is assigned twice");
                }
            }
        }

        [Test]
        public void SignalAtPinIsTheInverseOfPinOf()
        {
            foreach (IcDefinition def in IcDefinitions.All)
            {
                for (int pin = 1; pin <= 14; pin++)
                {
                    string signal = def.SignalAtPin(pin);
                    if (signal == null) continue;
                    Assert.AreEqual(pin, def.PinOf(signal), $"{def.PartNumber} pin {pin}");
                }
            }
        }

        // ---- 7404 hex inverter

        [Test]
        public void Hex7404Pinout()
        {
            IcDefinition d = IcDefinitions.Hex7404;
            Assert.AreEqual(1, d.PinOf("1A"));
            Assert.AreEqual(2, d.PinOf("1Y"));
            Assert.AreEqual(3, d.PinOf("2A"));
            Assert.AreEqual(4, d.PinOf("2Y"));
            Assert.AreEqual(5, d.PinOf("3A"));
            Assert.AreEqual(6, d.PinOf("3Y"));
            Assert.AreEqual(7, d.PinOf("GND"));
            Assert.AreEqual(8, d.PinOf("4Y"));
            Assert.AreEqual(9, d.PinOf("4A"));
            Assert.AreEqual(10, d.PinOf("5Y"));
            Assert.AreEqual(11, d.PinOf("5A"));
            Assert.AreEqual(12, d.PinOf("6Y"));
            Assert.AreEqual(13, d.PinOf("6A"));
            Assert.AreEqual(14, d.PinOf("VCC"));
        }

        [Test]
        public void Hex7404HasSixInverters()
        {
            Assert.AreEqual(6, IcDefinitions.Hex7404.Gates.Length);
            foreach (GateSpec g in IcDefinitions.Hex7404.Gates)
            {
                Assert.AreEqual(g.InA, g.InB, "an inverter has one input");
                Assert.IsTrue(g.Out.EndsWith("Y"));
            }
        }

        // ---- 7408 / 7432 quads

        [Test]
        public void QuadPinoutIsSharedBy7408And7432()
        {
            IcDefinition a = IcDefinitions.Quad7408;
            IcDefinition b = IcDefinitions.Quad7432;
            foreach (string signal in a.SignalOrder)
                Assert.AreEqual(a.PinOf(signal), b.PinOf(signal), signal + " differs between 7408 and 7432");
        }

        [Test]
        public void Quad7408Pinout()
        {
            IcDefinition d = IcDefinitions.Quad7408;
            Assert.AreEqual(1, d.PinOf("1A"));
            Assert.AreEqual(2, d.PinOf("1B"));
            Assert.AreEqual(3, d.PinOf("1Y"));
            Assert.AreEqual(4, d.PinOf("2A"));
            Assert.AreEqual(5, d.PinOf("2B"));
            Assert.AreEqual(6, d.PinOf("2Y"));
            Assert.AreEqual(7, d.PinOf("GND"));
            Assert.AreEqual(8, d.PinOf("3Y"));
            Assert.AreEqual(9, d.PinOf("3A"));
            Assert.AreEqual(10, d.PinOf("3B"));
            Assert.AreEqual(11, d.PinOf("4Y"));
            Assert.AreEqual(12, d.PinOf("4A"));
            Assert.AreEqual(13, d.PinOf("4B"));
            Assert.AreEqual(14, d.PinOf("VCC"));
        }

        [Test]
        public void QuadsHaveFourGates()
        {
            Assert.AreEqual(4, IcDefinitions.Quad7408.Gates.Length);
            Assert.AreEqual(4, IcDefinitions.Quad7432.Gates.Length);
            Assert.AreEqual(LogicGateKind.And, IcDefinitions.Quad7408.GateKind);
            Assert.AreEqual(LogicGateKind.Or, IcDefinitions.Quad7432.GateKind);
        }

        [Test]
        public void EquationsAreDescribed()
        {
            Assert.IsTrue(IcDefinitions.Hex7404.EquationFor(0).Contains("NOT"));
            Assert.IsTrue(IcDefinitions.Quad7408.EquationFor(0).Contains("AND"));
            Assert.IsTrue(IcDefinitions.Quad7432.EquationFor(2).Contains("OR"));
        }

        [Test]
        public void AllGatesAreDistinct()
        {
            foreach (IcDefinition def in IcDefinitions.All)
            {
                var outs = new HashSet<string>();
                foreach (GateSpec g in def.Gates)
                    Assert.IsTrue(outs.Add(g.Out), def.PartNumber + " reuses output " + g.Out);
            }
        }
    }
}
