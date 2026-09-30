using System;
using System.Collections.Generic;
using UnityEngine;
using ARLab.Electronics;

namespace ARLab.Laboratory
{
    /// <summary>Ten contact rows: A-E above the centre trench, F-J below it.</summary>
    public enum BreadboardRow { A, B, C, D, E, F, G, H, I, J }

    /// <summary>
    /// Pure-C# model of a solderless breadboard's connectivity. Sockets in the same column
    /// half share one node, and the power rails are their own nets. This is a logical graph,
    /// not a proximity guess, so two sockets are joined only when the topology says so.
    /// </summary>
    public sealed class BreadboardGrid
    {
        public const float RowPitch = 0.00254f;      // 0.1"
        public const float TrenchGap = 0.00762f;     // 0.3" DIP row span
        public const float RailPitch = 0.00254f;
        public const float RailEdgeGap = 0.0127f;   // 0.5" from outer strip to rail row

        public readonly int Columns;
        public readonly bool SplitRails;

        private readonly int _socketCount;
        private readonly int[] _socketTerminals;
        private readonly int[] _railTerminalsPlusTop, _railTerminalsMinusTop;
        private readonly int[] _railTerminalsPlusBottom, _railTerminalsMinusBottom;

        public int SocketCount => _socketCount;
        public IReadOnlyList<int> SocketTerminals => _socketTerminals;
        public float Width => Columns * RowPitch;
        public float Height => TrenchGap + 2f * (4f * RowPitch + RailPitch) + 2f * RailEdgeGap;

        public BreadboardGrid(int columns = 63, bool splitRails = false)
        {
            Columns = Mathf.Max(2, columns);
            SplitRails = splitRails;
            _socketCount = Columns * 10;
            _socketTerminals = new int[_socketCount];
            _railTerminalsPlusTop = new int[Columns];
            _railTerminalsMinusTop = new int[Columns];
            _railTerminalsPlusBottom = new int[Columns];
            _railTerminalsMinusBottom = new int[Columns];
        }

        // ------------------------------------------------------------------ indexing

        public int SocketIndex(int column, BreadboardRow row)
        {
            if (column < 1) column = 1;
            if (column > Columns) column = Columns;
            return (column - 1) * 10 + (int)row;
        }

        public void DecodeSocket(int index, out int column, out BreadboardRow row)
        {
            column = index / 10 + 1;
            row = (BreadboardRow)(index % 10);
        }

        public int TerminalAt(int column, BreadboardRow row) => _socketTerminals[SocketIndex(column, row)];

        /// <summary>
        /// Terminal of a power rail post. <paramref name="top"/> selects the rail nearest the
        /// A-E half, and <paramref name="plus"/> selects the red (+) row over the black (-) one.
        /// </summary>
        public int RailTerminalAt(int column, bool top, bool plus)
        {
            if (column < 1) column = 1;
            if (column > Columns) column = Columns;
            int i = column - 1;
            if (top) return plus ? _railTerminalsPlusTop[i] : _railTerminalsMinusTop[i];
            return plus ? _railTerminalsPlusBottom[i] : _railTerminalsMinusBottom[i];
        }

        // ------------------------------------------------------------------ layout

        /// <summary>Local Y of a contact row, with the DIP trench centred on 0.</summary>
        public static float RowY(BreadboardRow row)
        {
            bool top = row <= BreadboardRow.E;
            int i = top ? (int)row : (int)row - 5;
            float offset = TrenchGap * 0.5f + (4 - i) * RowPitch;
            return top ? offset : -offset;
        }

        public static float ColumnX(int column) => (column - 1) * RowPitch;

        public float RailY(bool top) => (top ? 1f : -1f) * (4f * RowPitch + TrenchGap * 0.5f + RailPitch + RailEdgeGap);

        // ------------------------------------------------------------------ registration

        /// <summary>Creates every terminal and every intrinsic tie in the netlist.</summary>
        public void Register(ConnectionGraph graph)
        {
            // One rebuild for the whole board instead of one per tie.
            graph.BeginBatch();
            try
            {
                CreateTerminals(graph);
                ApplyTies(graph);
            }
            finally
            {
                graph.EndBatch();
            }
        }

        private void CreateTerminals(ConnectionGraph graph)
        {
            for (int c = 1; c <= Columns; c++)
            {
                for (int r = 0; r < 10; r++)
                {
                    BreadboardRow row = (BreadboardRow)r;
                    int idx = SocketIndex(c, row);
                    _socketTerminals[idx] = graph.AddTerminal(
                        TerminalKind.BreadboardSocket,
                        $"BB.c{c}{row}",
                        this);
                }

                _railTerminalsPlusTop[c - 1] = graph.AddTerminal(TerminalKind.PowerRail, $"BB.rail+T.top.c{c}", this);
                _railTerminalsMinusTop[c - 1] = graph.AddTerminal(TerminalKind.PowerRail, $"BB.rail-T.top.c{c}", this);
                _railTerminalsPlusBottom[c - 1] = graph.AddTerminal(TerminalKind.PowerRail, $"BB.rail+T.bot.c{c}", this);
                _railTerminalsMinusBottom[c - 1] = graph.AddTerminal(TerminalKind.PowerRail, $"BB.rail-T.bot.c{c}", this);
            }
        }

        /// <summary>
        /// Wires up the strips and rails. Upper rows A-E in a column form one node, lower rows
        /// F-J form another, and each rail is continuous (or split at the midpoint).
        /// </summary>
        public void ApplyTies(ConnectionGraph graph)
        {
            for (int c = 1; c <= Columns; c++)
            {
                for (int r = 1; r < 5; r++)
                {
                    graph.AddTieDeferred(_socketTerminals[SocketIndex(c, (BreadboardRow)r)],
                                        _socketTerminals[SocketIndex(c, (BreadboardRow)0)], ConnectionKind.BreadboardTie, this);
                    graph.AddTieDeferred(_socketTerminals[SocketIndex(c, (BreadboardRow)(r + 5))],
                                        _socketTerminals[SocketIndex(c, (BreadboardRow)5)], ConnectionKind.BreadboardTie, this);
                }
            }

            TieRail(graph, _railTerminalsPlusTop);
            TieRail(graph, _railTerminalsMinusTop);
            TieRail(graph, _railTerminalsPlusBottom);
            TieRail(graph, _railTerminalsMinusBottom);
        }

        private void TieRail(ConnectionGraph graph, int[] rail)
        {
            // A real split board breaks each rail at the midpoint into two independent
            // segments; a continuous board is a single run.
            int segment = (!SplitRails || Columns < 4) ? rail.Length : Columns / 2;

            for (int start = 0; start < rail.Length; start += segment)
            {
                int end = Mathf.Min(start + segment, rail.Length);
                for (int i = start + 1; i < end; i++)
                    graph.AddTieDeferred(rail[i], rail[i - 1], ConnectionKind.BreadboardTie, this);
            }
        }

        // ------------------------------------------------------------------ picking

        /// <summary>
        /// Maps a world-space point on the board surface to a socket. Lets a single collider
        /// stand in for hundreds of holes, which is what keeps this cheap on mobile.
        /// </summary>
        public bool TrySocketAtLocal(Vector3 local, out int column, out BreadboardRow row)
        {
            column = 0;
            row = BreadboardRow.A;
            if (local.y > 0f)
            {
                float y = local.y;
                int i = Mathf.Clamp(Mathf.RoundToInt((4f * RowPitch + TrenchGap * 0.5f - y) / RowPitch), 0, 4);
                row = (BreadboardRow)i;
            }
            else
            {
                float y = -local.y;
                int i = Mathf.Clamp(Mathf.RoundToInt((4f * RowPitch + TrenchGap * 0.5f - y) / RowPitch), 0, 4);
                row = (BreadboardRow)(i + 5);
            }

            int c = Mathf.RoundToInt(local.x / RowPitch) + 1;
            if (c < 1 || c > Columns) return false;
            column = c;
            return true;
        }

        /// <summary>Centre point of a socket in board-local space.</summary>
        public Vector3 LocalPositionOf(int column, BreadboardRow row)
            => new Vector3(ColumnX(column), RowY(row), 0f);

        /// <summary>
        /// Where a 14-pin DIP straddling the trench lands: pins 1-7 on the lower strip
        /// left to right, pins 8-14 on the upper strip right to left, pin 14 above pin 1.
        /// </summary>
        public void IcsPinPlacement(int startColumn, int pinNumber, out int column, out BreadboardRow row)
        {
            if (pinNumber < 1) pinNumber = 1;
            if (pinNumber > 14) pinNumber = 14;
            if (pinNumber <= 7)
            {
                column = startColumn + pinNumber - 1;
                row = BreadboardRow.F;
            }
            else
            {
                column = startColumn + (14 - pinNumber);
                row = BreadboardRow.A;
            }
        }
    }
}
