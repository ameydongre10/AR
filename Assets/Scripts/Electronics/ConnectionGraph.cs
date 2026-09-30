using System;
using System.Collections.Generic;

namespace ARLab.Electronics
{
    public enum TerminalKind
    {
        ComponentPin,
        BreadboardSocket,
        PowerRail
    }

    /// <summary>Why two terminals are electrically joined.</summary>
    public enum ConnectionKind
    {
        /// <summary>User-created jumper wire. Visible and deletable.</summary>
        Wire,
        /// <summary>Intrinsic breadboard strip / rail contact. Invisible, not deletable.</summary>
        BreadboardTie,
        /// <summary>Component internally bonded its pins (e.g. a switch's two lugs).</summary>
        InternalTie
    }

    /// <summary>An addressable electrical contact: one IC pin, one breadboard hole, one rail post.</summary>
    public sealed class Terminal
    {
        public int Id;
        public TerminalKind Kind;
        public string Label;
        /// <summary>Component that owns this terminal, when Kind == ComponentPin.</summary>
        public object Owner;

        public Terminal(int id, TerminalKind kind, string label, object owner)
        {
            Id = id;
            Kind = kind;
            Label = label;
            Owner = owner;
        }

        public override string ToString() => $"#{Id}:{Label}";
    }

    /// <summary>One electrical node: every terminal that is shorted together.</summary>
    public sealed class Net
    {
        /// <summary>Stable key = lowest terminal id in the group.</summary>
        public int Id;
        public readonly List<int> Terminals = new List<int>();

        /// <summary>Terminals that actively source a value (switch outputs, gate outputs, rails).</summary>
        public readonly List<int> Drivers = new List<int>();
        /// <summary>Terminals that consume a value (gate inputs, LED anodes, probes).</summary>
        public readonly List<int> Readers = new List<int>();

        public bool IsDriven => Drivers.Count > 0;
        public bool IsContended => Drivers.Count > 1;
        public bool IsFloating => Drivers.Count == 0 && Readers.Count > 0;
        public bool IsPowerRail => Kind == TerminalKind.PowerRail;

        public TerminalKind Kind = TerminalKind.BreadboardSocket;
        public LogicValue Value = LogicValue.Undefined;
        /// <summary>True for +5V / GND rails so the validator can require them by name.</summary>
        public bool IsSupply;

        public override string ToString() => $"Net{Id} v={LogicOps.ToChar(Value)} terms={Terminals.Count} drv={Drivers.Count} rdr={Readers.Count}";
    }

    /// <summary>
    /// The authoritative electrical netlist. Breadboard ties and user wires are stored
    /// separately and the union-find structure is recomputed on change (never per frame),
    /// which keeps grouping exactly correct and allocation-light.
    /// </summary>
    public sealed class ConnectionGraph
    {
        private readonly List<Terminal> _terminals = new List<Terminal>();
        private readonly Dictionary<int, int> _parent = new Dictionary<int, int>(64);
        private readonly Dictionary<int, int> _rank = new Dictionary<int, int>(64);

        private readonly List<Edge> _ties = new List<Edge>();   // intrinsic (breadboard / internal)
        private readonly List<Edge> _wires = new List<Edge>();  // user-created

        private readonly Dictionary<int, Net> _nets = new Dictionary<int, Net>(32);
        private readonly List<Net> _netList = new List<Net>(32);
        private readonly Dictionary<int, int> _terminalToNet = new Dictionary<int, int>(64);

        /// <summary>Bumped on every structural change so consumers can cheaply detect staleness.</summary>
        public int Revision { get; private set; }

        public IReadOnlyList<Terminal> Terminals => _terminals;
        public IReadOnlyList<Net> Nets => _netList;
        public IReadOnlyList<WireLink> Wires => _wireLinks;
        public int WireCount => _wires.Count;

        public event Action GraphChanged;

        /// <summary>Public view of one user wire, used by the UI to list and delete connections.</summary>
        public readonly struct WireLink
        {
            public readonly int A;
            public readonly int B;
            public readonly ConnectionKind Kind;
            public readonly object Ref;
            public WireLink(int a, int b, ConnectionKind kind, object reference) { A = a; B = b; Kind = kind; Ref = reference; }
        }

        private readonly List<WireLink> _wireLinks = new List<WireLink>(16);

        private int _batching;
        private bool _rebuildPending;

        /// <summary>True when at least one buffered edit is still waiting for its rebuild.</summary>
        public bool HasPendingChanges => _rebuildPending;

        private struct Edge
        {
            public int A;
            public int B;
            public ConnectionKind Kind;
            public object Ref;
        }

        // ---------------------------------------------------------------- terminals

        public int AddTerminal(TerminalKind kind, string label, object owner)
        {
            int id = _terminals.Count;
            _terminals.Add(new Terminal(id, kind, label, owner));
            _parent[id] = id;
            _rank[id] = 0;
            return id;
        }

        public Terminal GetTerminal(int id) => (id >= 0 && id < _terminals.Count) ? _terminals[id] : null;

        // ---------------------------------------------------------------- connectivity

        /// <summary>Adds a permanent (non-deletable) electrical tie such as a breadboard strip.</summary>
        public void AddTie(int a, int b, ConnectionKind kind, object reference = null)
        {
            if (a == b || !Valid(a) || !Valid(b)) return;
            _ties.Add(new Edge { A = a, B = b, Kind = kind, Ref = reference });
            Rebuild();
        }

        /// <summary>
        /// Structural edits are buffered so a batch of them costs exactly one rebuild. The
        /// breadboard pushes roughly 900 ties at start-up; rebuilding per tie would be
        /// quadratic and would leave nets stale in between.
        /// </summary>
        public void BeginBatch() => _batching++;

        public void EndBatch()
        {
            if (_batching == 0) return;
            _batching--;
            if (_batching == 0 && _rebuildPending) Rebuild();
        }

        /// <summary>Adds a tie inside a batch. Prefer this over <see cref="AddTie"/> in bulk paths.</summary>
        public void AddTieDeferred(int a, int b, ConnectionKind kind, object reference = null)
        {
            if (a == b || !Valid(a) || !Valid(b)) return;
            _ties.Add(new Edge { A = a, B = b, Kind = kind, Ref = reference });
            _rebuildPending = true;
        }

        public void ClearTies()
        {
            if (_ties.Count == 0) return;
            _ties.Clear();
            Rebuild();
        }

        /// <summary>Creates a user wire. Returns false for self-connection or duplicates.</summary>
        public bool AddWire(int a, int b, object wireRef = null)
        {
            if (a == b) return false;
            if (!Valid(a) || !Valid(b)) return false;
            if (FindWire(a, b) != null) return false;
            _wires.Add(new Edge { A = a, B = b, Kind = ConnectionKind.Wire, Ref = wireRef });
            if (_batching > 0) _rebuildPending = true;
            else Rebuild();
            return true;
        }

        /// <summary>Validates and records a user wire without rebuilding (inside a batch).</summary>
        public bool CanAddWire(int a, int b)
        {
            if (a == b) return false;
            if (!Valid(a) || !Valid(b)) return false;
            return FindWire(a, b) == null;
        }

        public bool RemoveWire(int a, int b)
        {
            for (int i = 0; i < _wires.Count; i++)
            {
                Edge e = _wires[i];
                if ((e.A == a && e.B == b) || (e.A == b && e.B == a))
                {
                    _wires.RemoveAt(i);
                    if (_batching > 0) _rebuildPending = true;
                    else Rebuild();
                    return true;
                }
            }
            return false;
        }
        public bool RemoveWireByRef(object wireRef)
        {
            for (int i = 0; i < _wires.Count; i++)
            {
                if (ReferenceEquals(_wires[i].Ref, wireRef))
                {
                    _wires.RemoveAt(i);
                    if (_batching > 0) _rebuildPending = true;
                    else Rebuild();
                    return true;
                }
            }
            return false;
        }

        /// <summary>Finds an existing wire between the two terminals, or null.</summary>
        public object FindWire(int a, int b)
        {
            for (int i = 0; i < _wires.Count; i++)
            {
                Edge e = _wires[i];
                if ((e.A == a && e.B == b) || (e.A == b && e.B == a)) return e.Ref;
            }
            return null;
        }

        public bool AreDirectlyConnected(int a, int b) => a == b || Find(a) == Find(b);

        /// <summary>True when the two terminals are shorted, whether by wire or by breadboard rail.</summary>
        public bool AreElectricallySame(int a, int b) => AreDirectlyConnected(a, b);

        // ---------------------------------------------------------------- nets

        public Net NetOf(int terminalId)
        {
            if (_terminalToNet.TryGetValue(terminalId, out int nid) && _nets.TryGetValue(nid, out Net n)) return n;
            return null;
        }

        public Net NetById(int netId) => _nets.TryGetValue(netId, out Net n) ? n : null;

        /// <summary>All terminals in the same net as <paramref name="terminalId"/>.</summary>
        public void PeersOf(int terminalId, List<int> results)
        {
            results.Clear();
            Net n = NetOf(terminalId);
            if (n == null) return;
            results.AddRange(n.Terminals);
        }

        // ---------------------------------------------------------------- union-find

        private bool Valid(int t) => t >= 0 && t < _terminals.Count;

        private int Find(int x)
        {
            if (!_parent.TryGetValue(x, out int p)) return x;
            int root = p;
            while (_parent.TryGetValue(root, out int next) && next != root) root = next;
            // path compression
            int cur = x;
            while (_parent.TryGetValue(cur, out int c) && c != root)
            {
                _parent[cur] = root;
                cur = c;
            }
            return root;
        }

        private void Union(int a, int b)
        {
            int ra = Find(a), rb = Find(b);
            if (ra == rb) return;
            int rankA = _rank.TryGetValue(ra, out int va) ? va : 0;
            int rankB = _rank.TryGetValue(rb, out int vb) ? vb : 0;
            if (rankA < rankB) { _parent[ra] = rb; }
            else if (rankA > rankB) { _parent[rb] = ra; }
            else { _parent[rb] = ra; _rank[ra] = rankA + 1; }
        }

        /// <summary>Recomputes every net from scratch. Called on structural change only.</summary>
        public void Rebuild()
        {
            for (int i = 0; i < _terminals.Count; i++) { _parent[i] = i; _rank[i] = 0; }

            for (int i = 0; i < _ties.Count; i++) Union(_ties[i].A, _ties[i].B);
            for (int i = 0; i < _wires.Count; i++) Union(_wires[i].A, _wires[i].B);

            _nets.Clear();
            _netList.Clear();
            _terminalToNet.Clear();
            _wireLinks.Clear();
            for (int i = 0; i < _wires.Count; i++)
            {
                Edge e = _wires[i];
                _wireLinks.Add(new WireLink(e.A, e.B, e.Kind, e.Ref));
            }

            // group terminals by root
            var groups = new Dictionary<int, List<int>>(32);
            for (int i = 0; i < _terminals.Count; i++)
            {
                int r = Find(i);
                if (!groups.TryGetValue(r, out List<int> list))
                {
                    list = new List<int>(4);
                    groups[r] = list;
                }
                list.Add(i);
            }

            foreach (KeyValuePair<int, List<int>> kv in groups)
            {
                List<int> members = kv.Value;
                int minId = int.MaxValue;
                for (int i = 0; i < members.Count; i++) if (members[i] < minId) minId = members[i];

                Net net = new Net { Id = minId };
                net.Terminals.AddRange(members);
                net.Kind = _terminals[minId].Kind;
                _nets[minId] = net;
                _netList.Add(net);

                for (int i = 0; i < members.Count; i++) _terminalToNet[members[i]] = minId;
            }

            Revision++;
            GraphChanged?.Invoke();
        }
    }
}
