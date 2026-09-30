using NUnit.Framework;
using ARLab.Electronics;

namespace ARLab.Tests
{
    /// <summary>
    /// Three-valued logic algebra. These rules are what make an unpowered or floating wire
    /// visibly different from a real 0, so they are pinned down explicitly.
    /// </summary>
    public class LogicValueTests
    {
        [Test]
        public void NotInvertsLowAndHigh()
        {
            Assert.AreEqual(LogicValue.High, LogicOps.Not(LogicValue.Low));
            Assert.AreEqual(LogicValue.Low, LogicOps.Not(LogicValue.High));
        }

        [Test]
        public void NotStaysUndefined()
        {
            Assert.AreEqual(LogicValue.Undefined, LogicOps.Not(LogicValue.Undefined));
        }

        [Test]
        public void AndIsTrueOnlyWhenBothHigh()
        {
            Assert.AreEqual(LogicValue.High, LogicOps.And(LogicValue.High, LogicValue.High));
            Assert.AreEqual(LogicValue.Low, LogicOps.And(LogicValue.High, LogicValue.Low));
            Assert.AreEqual(LogicValue.Low, LogicOps.And(LogicValue.Low, LogicValue.High));
            Assert.AreEqual(LogicValue.Low, LogicOps.And(LogicValue.Low, LogicValue.Low));
        }

        [Test]
        public void AndIsDominantLow()
        {
            Assert.AreEqual(LogicValue.Low, LogicOps.And(LogicValue.Low, LogicValue.Undefined));
            Assert.AreEqual(LogicValue.Low, LogicOps.And(LogicValue.Undefined, LogicValue.Low));
        }

        [Test]
        public void AndPropagatesUndefinedWhenNoLow()
        {
            Assert.AreEqual(LogicValue.Undefined, LogicOps.And(LogicValue.High, LogicValue.Undefined));
        }

        [Test]
        public void OrIsTrueWhenEitherHigh()
        {
            Assert.AreEqual(LogicValue.High, LogicOps.Or(LogicValue.High, LogicValue.Low));
            Assert.AreEqual(LogicValue.High, LogicOps.Or(LogicValue.Low, LogicValue.High));
            Assert.AreEqual(LogicValue.High, LogicOps.Or(LogicValue.High, LogicValue.High));
            Assert.AreEqual(LogicValue.Low, LogicOps.Or(LogicValue.Low, LogicValue.Low));
        }

        [Test]
        public void OrIsDominantHigh()
        {
            Assert.AreEqual(LogicValue.High, LogicOps.Or(LogicValue.High, LogicValue.Undefined));
            Assert.AreEqual(LogicValue.High, LogicOps.Or(LogicValue.Undefined, LogicValue.High));
        }

        [Test]
        public void OrPropagatesUndefinedWhenNoHigh()
        {
            Assert.AreEqual(LogicValue.Undefined, LogicOps.Or(LogicValue.Low, LogicValue.Undefined));
        }

        [Test]
        public void IdentityHolds()
        {
            // A AND 1 = A, and A OR 0 = A.
            Assert.AreEqual(LogicValue.High, LogicOps.And(LogicValue.High, LogicValue.High));
            Assert.AreEqual(LogicValue.Low, LogicOps.And(LogicValue.Low, LogicValue.High));
            Assert.AreEqual(LogicValue.Undefined, LogicOps.And(LogicValue.Undefined, LogicValue.High));

            Assert.AreEqual(LogicValue.High, LogicOps.Or(LogicValue.High, LogicValue.Low));
            Assert.AreEqual(LogicValue.Low, LogicOps.Or(LogicValue.Low, LogicValue.Low));
            Assert.AreEqual(LogicValue.Undefined, LogicOps.Or(LogicValue.Undefined, LogicValue.Low));
        }

        [Test]
        public void DeMorganHolds()
        {
            // NOT(A AND B) == NOT A OR NOT B, extended to three values.
            for (int a = 0; a < 3; a++)
                for (int b = 0; b < 3; b++)
                {
                    LogicValue va = (LogicValue)a, vb = (LogicValue)b;
                    Assert.AreEqual(LogicOps.Or(LogicOps.Not(va), LogicOps.Not(vb)),
                                    LogicOps.Not(LogicOps.And(va, vb)), $"A={va} B={vb}");
                }
        }

        [Test]
        public void ThreeInputVariantsMatchRepeatedBinary()
        {
            var values = new[] { LogicValue.Low, LogicValue.High, LogicValue.Undefined };
            foreach (LogicValue a in values)
                foreach (LogicValue b in values)
                    foreach (LogicValue c in values)
                    {
                        Assert.AreEqual(LogicOps.And(LogicOps.And(a, b), c), LogicOps.And(a, b, c));
                        Assert.AreEqual(LogicOps.Or(LogicOps.Or(a, b), c), LogicOps.Or(a, b, c));
                    }
        }

        [Test]
        public void CommutativityHolds()
        {
            var values = new[] { LogicValue.Low, LogicValue.High, LogicValue.Undefined };
            foreach (LogicValue a in values)
                foreach (LogicValue b in values)
                {
                    Assert.AreEqual(LogicOps.And(a, b), LogicOps.And(b, a), $"AND A={a} B={b}");
                    Assert.AreEqual(LogicOps.Or(a, b), LogicOps.Or(b, a), $"OR A={a} B={b}");
                }
        }

        [Test]
        public void ToCharUsesXForUndefined()
        {
            Assert.AreEqual('0', LogicOps.ToChar(LogicValue.Low));
            Assert.AreEqual('1', LogicOps.ToChar(LogicValue.High));
            Assert.AreEqual('x', LogicOps.ToChar(LogicValue.Undefined));
        }

        [Test]
        public void IsDrivenRejectsUndefined()
        {
            Assert.IsTrue(LogicOps.IsDriven(LogicValue.Low));
            Assert.IsTrue(LogicOps.IsDriven(LogicValue.High));
            Assert.IsFalse(LogicOps.IsDriven(LogicValue.Undefined));
        }

        // ---- gate-level truth

        [Test]
        public void NotGateTruth()
        {
            Assert.AreEqual(LogicValue.High, LogicGate.Compute(LogicGateKind.Not, LogicValue.Low, LogicValue.Low));
            Assert.AreEqual(LogicValue.Low, LogicGate.Compute(LogicGateKind.Not, LogicValue.High, LogicValue.Low));
        }

        [Test]
        public void AndGateTruth()
        {
            Assert.AreEqual(LogicValue.High, LogicGate.Compute(LogicGateKind.And, LogicValue.High, LogicValue.High));
            Assert.AreEqual(LogicValue.Low, LogicGate.Compute(LogicGateKind.And, LogicValue.High, LogicValue.Low));
            Assert.AreEqual(LogicValue.Low, LogicGate.Compute(LogicGateKind.And, LogicValue.Low, LogicValue.Low));
        }

        [Test]
        public void OrGateTruth()
        {
            Assert.AreEqual(LogicValue.High, LogicGate.Compute(LogicGateKind.Or, LogicValue.Low, LogicValue.High));
            Assert.AreEqual(LogicValue.Low, LogicGate.Compute(LogicGateKind.Or, LogicValue.Low, LogicValue.Low));
        }

        [Test]
        public void InverterAndAndGateMakeXor()
        {
            // This is the experiment's core identity, checked through the gate primitives.
            for (int mask = 0; mask < 4; mask++)
            {
                LogicValue a = (mask & 1) != 0 ? LogicValue.High : LogicValue.Low;
                LogicValue b = (mask & 2) != 0 ? LogicValue.High : LogicValue.Low;

                LogicValue notA = LogicGate.Compute(LogicGateKind.Not, a, LogicValue.Low);
                LogicValue notB = LogicGate.Compute(LogicGateKind.Not, b, LogicValue.Low);
                LogicValue t1 = LogicGate.Compute(LogicGateKind.And, notA, b);
                LogicValue t2 = LogicGate.Compute(LogicGateKind.And, a, notB);
                LogicValue f = LogicGate.Compute(LogicGateKind.Or, t1, t2);

                bool expected = a != b;
                Assert.AreEqual(expected ? LogicValue.High : LogicValue.Low, f, $"row {mask}");
            }
        }
    }
}
