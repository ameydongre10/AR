using NUnit.Framework;
using UnityEngine;
using ARLab.Electronics;
using ARLab.Laboratory;
using ARLab.Simulation;

namespace ARLab.Tests
{
    /// <summary>
    /// Netlist and topology tests. These are the ground truth for "the circuit is wired the
    /// way a real breadboard is wired", independent of any Unity scene.
    /// </summary>
    public class ConnectionGraphTests
    {
        [Test]
        public void NewTerminalsStartIsolated()
        {
            var g = new ConnectionGraph();
            int a = g.AddTerminal(TerminalKind.ComponentPin, "A", null);
            int b = g.AddTerminal(TerminalKind.ComponentPin, "B", null);

            Assert.AreNotEqual(a, b);
            Assert.IsFalse(g.AreElectricallySame(a, b));
        }

        [Test]
        public void TieJoinsNets()
        {
            var g = new ConnectionGraph();
            int a = g.AddTerminal(TerminalKind.ComponentPin, "A", null);
            int b = g.AddTerminal(TerminalKind.ComponentPin, "B", null);

            g.AddTie(a, b, ConnectionKind.BreadboardTie);
            Assert.IsTrue(g.AreElectricallySame(a, b));
        }

        [Test]
        public void TieIsTransitive()
        {
            var g = new ConnectionGraph();
            int a = g.AddTerminal(TerminalKind.ComponentPin, "A", null);
            int b = g.AddTerminal(TerminalKind.ComponentPin, "B", null);
            int c = g.AddTerminal(TerminalKind.ComponentPin, "C", null);

            g.AddTie(a, b, ConnectionKind.BreadboardTie);
            g.AddTie(b, c, ConnectionKind.BreadboardTie);

            Assert.IsTrue(g.AreElectricallySame(a, c));
            Assert.AreEqual(1, g.Nets.Count);
        }

        [Test]
        public void WireConnectsAndIsRemovable()
        {
            var g = new ConnectionGraph();
            int a = g.AddTerminal(TerminalKind.ComponentPin, "A", null);
            int b = g.AddTerminal(TerminalKind.ComponentPin, "B", null);
            var wire = new object();

            Assert.IsTrue(g.AddWire(a, b, wire));
            Assert.IsTrue(g.AreElectricallySame(a, b));

            Assert.IsTrue(g.RemoveWireByRef(wire));
            Assert.IsFalse(g.AreElectricallySame(a, b));
        }

        [Test]
        public void DuplicateWireIsRejected()
        {
            var g = new ConnectionGraph();
            int a = g.AddTerminal(TerminalKind.ComponentPin, "A", null);
            int b = g.AddTerminal(TerminalKind.ComponentPin, "B", null);

            Assert.IsTrue(g.AddWire(a, b, new object()));
            Assert.IsFalse(g.AddWire(a, b, new object()), "a second wire between the same pair must be refused");
            Assert.IsFalse(g.AddWire(b, a, new object()), "direction must not matter for duplicate detection");
            Assert.AreEqual(1, g.WireCount);
        }

        [Test]
        public void SelfWireIsRejected()
        {
            var g = new ConnectionGraph();
            int a = g.AddTerminal(TerminalKind.ComponentPin, "A", null);
            Assert.IsFalse(g.AddWire(a, a, new object()));
        }

        [Test]
        public void TiesAreNotUserWires()
        {
            var g = new ConnectionGraph();
            int a = g.AddTerminal(TerminalKind.BreadboardSocket, "s1", null);
            int b = g.AddTerminal(TerminalKind.BreadboardSocket, "s2", null);

            g.AddTie(a, b, ConnectionKind.BreadboardTie);
            Assert.AreEqual(0, g.WireCount, "intrinsic ties must not appear as deletable wires");
        }

        [Test]
        public void BatchedTiesProduceTheSameResultAsUnbatched()
        {
            const int n = 20;

            var batched = new ConnectionGraph();
            batched.BeginBatch();
            var bIds = new int[n];
            for (int i = 0; i < n; i++) bIds[i] = batched.AddTerminal(TerminalKind.BreadboardSocket, "b" + i, null);
            for (int i = 1; i < n; i++) batched.AddTieDeferred(bIds[i], bIds[0], ConnectionKind.BreadboardTie);
            batched.EndBatch();

            var plain = new ConnectionGraph();
            var pIds = new int[n];
            for (int i = 0; i < n; i++) pIds[i] = plain.AddTerminal(TerminalKind.BreadboardSocket, "p" + i, null);
            for (int i = 1; i < n; i++) plain.AddTie(pIds[i], pIds[0], ConnectionKind.BreadboardTie);

            Assert.AreEqual(plain.Nets.Count, batched.Nets.Count);
            Assert.AreEqual(1, batched.Nets.Count);
            for (int i = 0; i < n; i++)
                Assert.IsTrue(batched.AreElectricallySame(bIds[0], bIds[i]));
        }

        [Test]
        public void RevisionAdvancesOnStructuralChange()
        {
            var g = new ConnectionGraph();
            int a = g.AddTerminal(TerminalKind.ComponentPin, "A", null);
            int b = g.AddTerminal(TerminalKind.ComponentPin, "B", null);
            int before = g.Revision;

            g.AddWire(a, b, new object());
            Assert.Greater(g.Revision, before);
        }
    }

    /// <summary>Breadboard connectivity must match the physical part exactly.</summary>
    public class BreadboardGridTests
    {
        [Test]
        public void SameColumnTopHalfIsOneStrip()
        {
            var grid = new BreadboardGrid(10, false);
            var g = new ConnectionGraph();
            grid.Register(g);

            int a = grid.TerminalAt(3, BreadboardRow.A);
            int e = grid.TerminalAt(3, BreadboardRow.E);
            Assert.IsTrue(g.AreElectricallySame(a, e), "A..E in a column are one strip");
        }

        [Test]
        public void SameColumnBottomHalfIsOneStrip()
        {
            var grid = new BreadboardGrid(10, false);
            var g = new ConnectionGraph();
            grid.Register(g);

            int f = grid.TerminalAt(4, BreadboardRow.F);
            int j = grid.TerminalAt(4, BreadboardRow.J);
            Assert.IsTrue(g.AreElectricallySame(f, j), "F..J in a column are one strip");
        }

        [Test]
        public void TrenchSeparatesTheTwoHalves()
        {
            var grid = new BreadboardGrid(10, false);
            var g = new ConnectionGraph();
            grid.Register(g);

            int e = grid.TerminalAt(5, BreadboardRow.E);
            int f = grid.TerminalAt(5, BreadboardRow.F);
            Assert.IsFalse(g.AreElectricallySame(e, f), "the centre trench isolates row E from row F");
        }

        [Test]
        public void AdjacentColumnsAreIsolated()
        {
            var grid = new BreadboardGrid(10, false);
            var g = new ConnectionGraph();
            grid.Register(g);

            int c1 = grid.TerminalAt(2, BreadboardRow.C);
            int c2 = grid.TerminalAt(3, BreadboardRow.C);
            Assert.IsFalse(g.AreElectricallySame(c1, c2), "neighbouring columns must not short together");
        }

        [Test]
        public void RailsAreContinuousAndSeparate()
        {
            var grid = new BreadboardGrid(12, false);
            var g = new ConnectionGraph();
            grid.Register(g);

            int first = grid.RailTerminalAt(1, true, true);
            int last = grid.RailTerminalAt(12, true, true);
            Assert.IsTrue(g.AreElectricallySame(first, last), "the + rail is continuous");

            int plus = grid.RailTerminalAt(5, true, true);
            int minus = grid.RailTerminalAt(5, true, false);
            Assert.IsFalse(g.AreElectricallySame(plus, minus), "+ and - rails are separate nets");

            int socket = grid.TerminalAt(1, BreadboardRow.A);
            Assert.IsFalse(g.AreElectricallySame(socket, grid.RailTerminalAt(1, true, true)),
                "a rail post is not bonded to the strip in the same column");
        }

        [Test]
        public void SplitRailsBreakAtTheMidpoint()
        {
            var grid = new BreadboardGrid(12, true);
            var g = new ConnectionGraph();
            grid.Register(g);

            // 12 columns split into two independent halves: 1-6 and 7-12.
            int first = grid.RailTerminalAt(1, true, true);
            int last = grid.RailTerminalAt(12, true, true);
            Assert.IsFalse(g.AreElectricallySame(first, last), "split rails must be two separate nets");

            Assert.IsTrue(g.AreElectricallySame(grid.RailTerminalAt(1, true, true), grid.RailTerminalAt(6, true, true)),
                "the left half stays internally connected");
            Assert.IsTrue(g.AreElectricallySame(grid.RailTerminalAt(7, true, true), grid.RailTerminalAt(12, true, true)),
                "the right half stays internally connected");
            Assert.IsFalse(g.AreElectricallySame(grid.RailTerminalAt(6, true, true), grid.RailTerminalAt(7, true, true)),
                "the break sits between the two halves");
        }

        [Test]
        public void RegistrationCreatesTheExpectedTerminalCount()
        {
            var grid = new BreadboardGrid(10, false);
            var g = new ConnectionGraph();
            grid.Register(g);

            // 10 sockets + 4 rails per column.
            Assert.AreEqual(10 * 14, g.Terminals.Count);
        }

        [Test]
        public void IcPinPlacementStraddlesTheTrench()
        {
            var grid = new BreadboardGrid(20, false);

            grid.IcsPinPlacement(5, 1, out int col1, out BreadboardRow row1);
            grid.IcsPinPlacement(5, 7, out int col7, out BreadboardRow row7);
            grid.IcsPinPlacement(5, 8, out int col8, out BreadboardRow row8);
            grid.IcsPinPlacement(5, 14, out int col14, out BreadboardRow row14);

            Assert.AreEqual(BreadboardRow.F, row1);
            Assert.AreEqual(BreadboardRow.F, row7);
            Assert.AreEqual(BreadboardRow.A, row8);
            Assert.AreEqual(BreadboardRow.A, row14);

            Assert.Less(col1, col7, "pins 1-7 run left to right along the bottom");
            Assert.Greater(col8, col14, "pins 8-14 run right to left along the top");
            Assert.AreEqual(col1, col14, "pin 14 sits directly above pin 1");
            Assert.AreEqual(col7, col8, "pin 8 sits directly above pin 7");
        }

        [Test]
        public void SocketPickingStaysInBounds()
        {
            var grid = new BreadboardGrid(10, false);

            Assert.IsTrue(grid.TrySocketAtLocal(new Vector3(BreadboardGrid.ColumnX(5), BreadboardGrid.RowY(BreadboardRow.C), 0f), out int col, out BreadboardRow row));
            Assert.AreEqual(5, col);
            Assert.AreEqual(BreadboardRow.C, row);

            Assert.IsFalse(grid.TrySocketAtLocal(new Vector3(-1f, 0f, 0f), out _, out _), "off the left edge");
            Assert.IsFalse(grid.TrySocketAtLocal(new Vector3(100f, 0f, 0f), out _, out _), "off the right edge");
        }

    }
}
