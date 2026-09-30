using NUnit.Framework;
using UnityEngine;
using ARLab.Electronics;
using ARLab.Laboratory;
using ARLab.Simulation;

namespace ARLab.Tests
{
    /// <summary>
    /// End-to-end tests that build a real circuit out of real components through
    /// <see cref="ComponentFactory"/> and solve it through the netlist. These are the tests
    /// that prove the simulation is electrical rather than cosmetic.
    /// </summary>
    public class DigitalLogicSimulatorTests
    {
        private GameObject _root;
        private ConnectionGraph _graph;
        private DigitalLogicSimulator _sim;
        private BreadboardGrid _grid;

        [SetUp]
        public void SetUp()
        {
            EventBusReset();
            _root = new GameObject("TestRig");
            _graph = new ConnectionGraph();
            _sim = new DigitalLogicSimulator(_graph);
            _grid = new BreadboardGrid(30, false);
            _grid.Register(_graph);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        private static void EventBusReset() => ARLab.Core.EventBus.Clear();

        private PowerSupply AddSupply()
        {
            PowerSupply psu = ComponentFactory.CreatePowerSupply();
            psu.transform.SetParent(_root.transform, false);
            psu.Register(_graph);
            _sim.Register(psu);
            psu.SetEnergised(true);
            return psu;
        }

        private IcComponent AddIc(IcDefinition def)
        {
            IcComponent ic = ComponentFactory.CreateIc(def);
            ic.transform.SetParent(_root.transform, false);
            ic.Register(_graph);
            _sim.Register(ic);
            return ic;
        }

        private InputSwitch AddSwitch(string signal)
        {
            InputSwitch sw = ComponentFactory.CreateSwitch(signal);
            sw.transform.SetParent(_root.transform, false);
            sw.Register(_graph);
            _sim.Register(sw);
            return sw;
        }

        private LedIndicator AddLed()
        {
            LedIndicator led = ComponentFactory.CreateLed("F");
            led.transform.SetParent(_root.transform, false);
            led.Register(_graph);
            _sim.Register(led);
            return led;
        }

        private int TerminalOf(IElectronicComponent c, string signal)
        {
            for (int i = 0; i < c.Pins.Count; i++)
                if (c.Pins[i] != null && c.Pins[i].SignalName == signal) return c.Pins[i].TerminalId;
            Assert.Fail($"{c.DisplayName} has no pin {signal}");
            return -1;
        }

        private void Wire(IElectronicComponent a, string aPin, IElectronicComponent b, string bPin)
        {
            Assert.IsTrue(_graph.AddWire(TerminalOf(a, aPin), TerminalOf(b, bPin), new object()),
                $"could not wire {a.DisplayName}.{aPin} -> {b.DisplayName}.{bPin}");
        }

        /// <summary>Connects a component's supply pins straight to the supply posts.</summary>
        private void PowerPart(IElectronicComponent part, PowerSupply psu)
        {
            Wire(part, "VCC", psu, "VCC_OUT");
            Wire(part, "GND", psu, "GND_OUT");
        }

        // ------------------------------------------------------------------ basic

        [Test]
        public void EmptyBenchReportsNoComponents()
        {
            SimulationOutcome o = _sim.Solve();
            Assert.AreEqual(SolveStatus.NoComponents, o.Status);
        }

        [Test]
        public void UnpoweredPartOutputsUndefined()
        {
            AddIc(IcDefinitions.Quad7408);
            _sim.Solve();

            IcComponent ic = null;
            for (int i = 0; i < _sim.Components.Count; i++)
                if (_sim.Components[i] is IcComponent x) ic = x;

            Assert.IsFalse(ic.IsPowered, "with no supply attached the IC must not be powered");
            Assert.AreEqual(LogicValue.Undefined, _sim.ValueOfTerminal(TerminalOf(ic, "1Y")));
        }

        [Test]
        public void SupplyOffMeansUndefinedNotZero()
        {
            PowerSupply psu = ComponentFactory.CreatePowerSupply();
            psu.transform.SetParent(_root.transform, false);
            psu.Register(_graph);
            _sim.Register(psu);
            psu.SetEnergised(false);

            Assert.AreEqual(LogicValue.Undefined, _sim.ValueOfTerminal(TerminalOf(psu, "VCC_OUT")),
                "an off supply must drive Undefined, not Low");
        }

        [Test]
        public void PoweredInverterInverts()
        {
            PowerSupply psu = AddSupply();
            InputSwitch sw = AddSwitch("A");
            IcComponent ic = AddIc(IcDefinitions.Hex7404);

            PowerPart(sw, psu);
            PowerPart(ic, psu);
            Wire(sw, "OUT", ic, "1A");

            sw.SetPosition(LogicValue.Low);
            _sim.Solve();
            Assert.IsTrue(ic.IsPowered, "7408/7404 need VCC and GND to be considered powered");
            Assert.AreEqual(LogicValue.High, _sim.ValueOfTerminal(TerminalOf(ic, "1Y")));

            sw.SetPosition(LogicValue.High);
            _sim.Solve();
            Assert.AreEqual(LogicValue.Low, _sim.ValueOfTerminal(TerminalOf(ic, "1Y")));
        }

        [Test]
        public void AndGateRespectsBothInputs()
        {
            PowerSupply psu = AddSupply();
            InputSwitch a = AddSwitch("A");
            InputSwitch b = AddSwitch("B");
            IcComponent ic = AddIc(IcDefinitions.Quad7408);

            PowerPart(a, psu); PowerPart(b, psu); PowerPart(ic, psu);
            Wire(a, "OUT", ic, "1A");
            Wire(b, "OUT", ic, "1B");

            a.SetPosition(LogicValue.High); b.SetPosition(LogicValue.High);
            _sim.Solve();
            Assert.AreEqual(LogicValue.High, _sim.ValueOfTerminal(TerminalOf(ic, "1Y")));

            a.SetPosition(LogicValue.High); b.SetPosition(LogicValue.Low);
            _sim.Solve();
            Assert.AreEqual(LogicValue.Low, _sim.ValueOfTerminal(TerminalOf(ic, "1Y")));

            a.SetPosition(LogicValue.Low); b.SetPosition(LogicValue.High);
            _sim.Solve();
            Assert.AreEqual(LogicValue.Low, _sim.ValueOfTerminal(TerminalOf(ic, "1Y")));

            a.SetPosition(LogicValue.Low); b.SetPosition(LogicValue.Low);
            _sim.Solve();
            Assert.AreEqual(LogicValue.Low, _sim.ValueOfTerminal(TerminalOf(ic, "1Y")));
        }

        [Test]
        public void FloatingGateInputYieldsUndefined()
        {
            PowerSupply psu = AddSupply();
            IcComponent ic = AddIc(IcDefinitions.Quad7408);
            PowerPart(ic, psu);

            _sim.Solve();
            Assert.AreEqual(LogicValue.Undefined, _sim.ValueOfTerminal(TerminalOf(ic, "1Y")),
                "an AND with no driven input is not Low, it is Undefined");
        }

        [Test]
        public void IcsAreIndependentWithinAPackage()
        {
            PowerSupply psu = AddSupply();
            InputSwitch a = AddSwitch("A");
            InputSwitch b = AddSwitch("B");
            IcComponent ic = AddIc(IcDefinitions.Quad7408);

            PowerPart(a, psu); PowerPart(b, psu); PowerPart(ic, psu);
            Wire(a, "OUT", ic, "1A");
            Wire(a, "OUT", ic, "1B");
            // Gate 2 is deliberately left floating.

            a.SetPosition(LogicValue.High);
            _sim.Solve();

            Assert.AreEqual(LogicValue.High, _sim.ValueOfTerminal(TerminalOf(ic, "1Y")));
            Assert.AreEqual(LogicValue.Undefined, _sim.ValueOfTerminal(TerminalOf(ic, "2Y")),
                "one unwired gate must not affect the others in the same package");
        }

        [Test]
        public void OutputContentionProducesUndefined()
        {
            PowerSupply psu = AddSupply();
            IcComponent ic1 = AddIc(IcDefinitions.Hex7404);
            IcComponent ic2 = AddIc(IcDefinitions.Hex7404);
            PowerPart(ic1, psu); PowerPart(ic2, psu);

            // Tie the two inverter outputs together with inputs held at different levels.
            InputSwitch a = AddSwitch("A");
            InputSwitch b = AddSwitch("B");
            PowerPart(a, psu); PowerPart(b, psu);
            Wire(a, "OUT", ic1, "1A");
            Wire(b, "OUT", ic2, "1A");
            Wire(ic1, "1Y", ic2, "1Y");

            a.SetPosition(LogicValue.High);
            b.SetPosition(LogicValue.Low);

            SimulationOutcome o = _sim.Solve();
            Assert.AreEqual(LogicValue.Undefined, _sim.ValueOfTerminal(TerminalOf(ic1, "1Y")),
                "two disagreeing outputs shorted together must read Undefined");
            Assert.GreaterOrEqual(o.ContendedNets, 1);
        }

        [Test]
        public void LedReflectsTheSimulatedNet()
        {
            PowerSupply psu = AddSupply();
            InputSwitch a = AddSwitch("A");
            IcComponent ic = AddIc(IcDefinitions.Hex7404);
            LedIndicator led = AddLed();

            PowerPart(a, psu); PowerPart(ic, psu);
            Wire(a, "OUT", ic, "1A");
            Wire(ic, "1Y", led, "ANODE");

            a.SetPosition(LogicValue.Low);
            _sim.Solve();
            Assert.AreEqual(LogicValue.High, led.State);

            a.SetPosition(LogicValue.High);
            _sim.Solve();
            Assert.AreEqual(LogicValue.Low, led.State);
        }

        [Test]
        public void BreadboardStripsCarrySignals()
        {
            // A switch output landed in a socket, and a gate input sharing that column strip.
            PowerSupply psu = AddSupply();
            InputSwitch a = AddSwitch("A");
            IcComponent ic = AddIc(IcDefinitions.Hex7404);
            PowerPart(a, psu); PowerPart(ic, psu);

            int swOut = TerminalOf(a, "OUT");
            int gateIn = TerminalOf(ic, "1A");
            int stripA = _grid.TerminalAt(10, BreadboardRow.C);
            int stripB = _grid.TerminalAt(10, BreadboardRow.D);

            _graph.AddWire(swOut, stripA, new object());
            _graph.AddWire(gateIn, stripB, new object());

            a.SetPosition(LogicValue.High);
            _sim.Solve();
            Assert.AreEqual(LogicValue.High, _sim.ValueOfTerminal(TerminalOf(ic, "1A")),
                "a value on one socket must reach its strip partner");
        }

        [Test]
        public void ProbeReadsWithoutDriving()
        {
            PowerSupply psu = AddSupply();
            InputSwitch a = AddSwitch("A");
            LogicProbe probe = ComponentFactory.CreateProbe("P1");
            probe.transform.SetParent(_root.transform, false);
            probe.Register(_graph);
            _sim.Register(probe);

            PowerPart(a, psu);
            Wire(a, "OUT", probe, "PROBE");

            a.SetPosition(LogicValue.High);
            _sim.Solve();
            Assert.AreEqual(LogicValue.High, probe.Reading);
        }

        // ------------------------------------------------------------------ the lab experiment

        /// <summary>
        /// Builds F = A'B + AB' out of a real 7404, 7408 and 7432 and checks all four rows
        /// against the parsed expression. This is the end-to-end proof the lab works.
        /// </summary>
        [Test]
        public void SopXorBuiltFromRealIcsMatchesTheExpression()
        {
            PowerSupply psu = AddSupply();
            InputSwitch a = AddSwitch("A");
            InputSwitch b = AddSwitch("B");

            IcComponent inv = AddIc(IcDefinitions.Hex7404);   // A' on gate 1, B' on gate 2
            IcComponent and1 = AddIc(IcDefinitions.Quad7408); // A' AND B
            IcComponent and2 = AddIc(IcDefinitions.Quad7408); // A AND B'
            IcComponent or1 = AddIc(IcDefinitions.Quad7432);  // combine
            LedIndicator led = AddLed();

            PowerPart(a, psu); PowerPart(b, psu);
            PowerPart(inv, psu); PowerPart(and1, psu); PowerPart(and2, psu); PowerPart(or1, psu);

            // Inverters.
            Wire(a, "OUT", inv, "1A");
            Wire(b, "OUT", inv, "2A");

            // AND1: A' AND B
            Wire(inv, "1Y", and1, "1A");
            Wire(b, "OUT", and1, "1B");

            // AND2: A AND B'
            Wire(a, "OUT", and2, "1A");
            Wire(inv, "2Y", and2, "1B");

            // OR
            Wire(and1, "1Y", or1, "1A");
            Wire(and2, "1Y", or1, "1B");

            // Output
            Wire(or1, "1Y", led, "ANODE");

            var expr = ARLab.Simulation.BooleanExpression.Parse("A'B + AB'");
            Assert.IsTrue(expr.IsValid);
            var evaluator = new TruthTableEvaluator(_sim);
            TruthTableResult result = evaluator.Evaluate(expr, () => _sim.ValueOfTerminal(TerminalOf(led, "ANODE")));

            Assert.AreEqual(4, result.Rows.Count);
            Assert.IsTrue(result.AllCorrect, result.Summary);
        }

        [Test]
        public void ExperimentValidationFlagsAnUnwiredBench()
        {
            AddSupply();
            var validator = new ExperimentValidator(_sim, null);
            ExperimentReport report = validator.Validate(ExperimentDefinition.SopXor, () => LogicValue.Undefined);

            Assert.IsFalse(report.IsComplete, "a bench with no ICs cannot be complete");
            Assert.Greater(report.Checks.Count, 0);
        }

        [Test]
        public void ValidationListsEveryRequiredPart()
        {
            AddSupply();
            var validator = new ExperimentValidator(_sim, null);
            ExperimentReport report = validator.Validate(ExperimentDefinition.SopXor, () => LogicValue.Undefined);

            var ids = new System.Collections.Generic.List<string>();
            for (int i = 0; i < report.Checks.Count; i++) ids.Add(report.Checks[i].Id);
            CollectionAssert.Contains(ids, "ic_7404");
            CollectionAssert.Contains(ids, "ic_7408");
            CollectionAssert.Contains(ids, "ic_7432");
        }
    }
}
