using System;
using System.Collections.Generic;
using UnityEngine;
using ARLab.Electronics;

namespace ARLab.Laboratory
{
    /// <summary>
    /// The solderless breadboard: one collider, a procedurally drawn hole/rail texture, and
    /// the logical <see cref="BreadboardGrid"/> that defines which sockets share a node.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Breadboard : MonoBehaviour
    {
        [SerializeField] private int columns = 63;
        [SerializeField] private bool splitRails = false;
        [SerializeField] private Color bodyColor = new Color(0.93f, 0.93f, 0.90f);
        [SerializeField] private Color holeColor = new Color(0.16f, 0.17f, 0.19f);
        [SerializeField] private bool generateTexture = true;

        private BreadboardGrid _grid;
        private BoxCollider _collider;
        private Transform _deck;
        private bool _registered;

        public BreadboardGrid Grid => _grid;
        public int Columns => columns;
        public bool SplitRails => splitRails;

        private void Awake()
        {
            EnsureBuilt();
        }

        private void EnsureBuilt()
        {
            if (_grid == null) _grid = new BreadboardGrid(columns, splitRails);
            if (_collider == null) _collider = GetComponent<BoxCollider>();
            if (_collider == null) _collider = gameObject.AddComponent<BoxCollider>();
            _collider.isTrigger = true;   // picking only; must never push other bodies
            BuildDeck();
        }

        /// <summary>
        /// Rebuilds the board at a different size. Safe to call after Awake, which is how the
        /// scene builder sizes the board without hand-editing prefab defaults. Always ends with
        /// a built board: Awake does not run for AddComponent in the editor, so a Configure
        /// that short-circuits on unchanged defaults would leave the deck missing entirely.
        /// </summary>
        public void Configure(int boardColumns, bool splitPowerRails)
        {
            if (boardColumns < 2) boardColumns = 2;
            bool changed = boardColumns != columns || splitPowerRails != splitRails;

            columns = boardColumns;
            splitRails = splitPowerRails;

            if (changed)
            {
                _grid = new BreadboardGrid(columns, splitRails);
                if (_deck != null)
                {
                    LabMaterials.Discard(_deck.gameObject);
                    _deck = null;
                }
            }

            EnsureBuilt();
        }

        public void Register(ConnectionGraph graph)
        {
            if (_registered) return;
            _registered = true;
            _grid.Register(graph);
        }

        public void Unregister()
        {
            if (!_registered) return;
            _registered = false;
        }

        private void BuildDeck()
        {
            float w = _grid.Width;
            float h = _grid.Height;

            // Thin slab: the trigger is for picking only, so it must not occlude pins.
            _collider.size = new Vector3(w, 0.004f, h);
            _collider.center = new Vector3(w * 0.5f, 0.001f, 0f);

            if (_deck != null) return;

            var deck = new GameObject("Deck");
            deck.transform.SetParent(transform, false);
            deck.transform.localPosition = new Vector3(0f, 0.0005f, 0f);
            _deck = deck.transform;

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(deck.transform, false);
            body.transform.localPosition = new Vector3(w * 0.5f, -0.002f, 0f);
            body.transform.localScale = new Vector3(w, 0.004f, h);
            LabMaterials.Discard(body.GetComponent<Collider>());
            ApplyColor(body, bodyColor);

            var face = GameObject.CreatePrimitive(PrimitiveType.Quad);
            face.name = "Face";
            face.transform.SetParent(deck.transform, false);
            face.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            face.transform.localScale = new Vector3(w, h, 1f);
            face.transform.localPosition = new Vector3(w * 0.5f, 0.0002f, 0f);
            LabMaterials.Discard(face.GetComponent<Collider>());

            var mat = new Material(LabMaterials.UnlitTransparent());
            mat.name = "BreadboardFace";
            if (generateTexture)
            {
                Texture2D tex = BuildFaceTexture();
                mat.mainTexture = tex;
            }
            face.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private static void ApplyColor(GameObject go, Color c)
        {
            var r = go.GetComponent<MeshRenderer>();
            if (r == null) return;
            var m = new Material(LabMaterials.Standard());
            m.SetColor("_BaseColor", c);
            r.sharedMaterial = m;
        }

        /// <summary>Draws the socket grid, the centre trench, the rails and column numbers once.</summary>
        private Texture2D BuildFaceTexture()
        {
            int pxPerPitch = 12;
            int w = Mathf.Max(64, (int)(_grid.Width * 1000f * pxPerPitch / 2.54f));
            int h = Mathf.Max(64, (int)(_grid.Height * 1000f * pxPerPitch / 2.54f));
            w = Mathf.Min(w, 4096);
            h = Mathf.Min(h, 1024);

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "BreadboardFaceTex", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = bodyColor;

            float pitchX = w / (float)_grid.Width;
            float pitchZ = h / (float)_grid.Height;

            // Centre trench
            float trenchHalf = BreadboardGrid.TrenchGap * 0.5f * 1000f * pxPerPitch / 2.54f;
            float centreZ = h * 0.5f;
            FillRect(px, w, h, 0, Mathf.RoundToInt(centreZ - trenchHalf), w, Mathf.RoundToInt(trenchHalf * 2f), new Color32(200, 200, 198, 255));

            // Socket holes
            for (int c = 1; c <= _grid.Columns; c++)
            {
                for (int r = 0; r < 10; r++)
                {
                    BreadboardRow row = (BreadboardRow)r;
                    float lx = BreadboardGrid.ColumnX(c) * 1000f * pxPerPitch / 2.54f;
                    float lz = (_grid.Height * 0.5f - (RowZ(row) * 1000f * pxPerPitch / 2.54f));
                    int cx = Mathf.RoundToInt(lx);
                    int cz = Mathf.RoundToInt(lz);
                    int rad = Mathf.Max(2, Mathf.RoundToInt(pitchX * 0.22f));
                    FillDisc(px, w, h, cx, cz, rad, holeColor);
                }
            }

            // Power rails
            Color plus = new Color(0.86f, 0.28f, 0.28f);
            Color minus = new Color(0.22f, 0.22f, 0.24f);
            DrawRail(px, w, h, _grid.RailY(true), -BreadboardGrid.RailPitch, plus, minus, true);
            DrawRail(px, w, h, _grid.RailY(false), BreadboardGrid.RailPitch, plus, minus, false);

            tex.SetPixels32(px);
            tex.Apply(true, false);
            return tex;
        }

        private float RowZ(BreadboardRow row) => BreadboardGrid.RowY(row);

        private void DrawRail(Color32[] px, int w, int h, float railY, float zStep, Color plus, Color minus, bool top)
        {
            float scale = 1000f * 12f / 2.54f;
            int zPlus = Mathf.RoundToInt(h * 0.5f - (railY - BreadboardGrid.RailPitch) * scale);
            int zMinus = Mathf.RoundToInt(h * 0.5f - (railY + BreadboardGrid.RailPitch) * scale);
            float pitchX = w / (float)_grid.Width;
            int rad = Mathf.Max(2, Mathf.RoundToInt(pitchX * 0.22f));

            for (int c = 1; c <= _grid.Columns; c++)
            {
                int cx = Mathf.RoundToInt(BreadboardGrid.ColumnX(c) * scale);
                FillDisc(px, w, h, cx, zPlus, rad, minus);
                FillDisc(px, w, h, cx, zMinus, rad, plus);
            }
        }

        private static void FillRect(Color32[] px, int w, int h, int x0, int z0, int rw, int rh, Color32 c)
        {
            for (int z = Mathf.Max(0, z0); z < Mathf.Min(h, z0 + rh); z++)
                for (int x = Mathf.Max(0, x0); x < Mathf.Min(w, x0 + rw); x++)
                    px[z * w + x] = c;
        }

        private static void FillDisc(Color32[] px, int w, int h, int cx, int cz, int r, Color c)
        {
            Color32 c32 = c;
            for (int z = cz - r; z <= cz + r; z++)
            {
                if (z < 0 || z >= h) continue;
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (x < 0 || x >= w) continue;
                    int dx = x - cx, dz = z - cz;
                    if (dx * dx + dz * dz <= r * r) px[z * w + x] = c32;
                }
            }
        }

        // ------------------------------------------------------------------ picking

        /// <summary>Converts a world hit point on the board to a socket terminal id.</summary>
        public bool TrySocketAtWorld(Vector3 worldPoint, out int terminalId, out int column, out BreadboardRow row)
        {
            terminalId = -1;
            column = 0;
            row = BreadboardRow.A;
            if (_grid == null) return false;

            Vector3 local = transform.InverseTransformPoint(worldPoint);
            if (!_grid.TrySocketAtLocal(local, out column, out row)) return false;

            terminalId = _grid.TerminalAt(column, row);
            return terminalId >= 0;
        }

        public Vector3 SocketWorldPosition(int column, BreadboardRow row)
            => transform.TransformPoint(_grid.LocalPositionOf(column, row)) + Vector3.up * 0.002f;

        /// <summary>World placement for a DIP straddling the trench at a given start column.</summary>
        public Vector3 IcBodyPosition(int startColumn)
        {
            float x = BreadboardGrid.ColumnX(startColumn) + BreadboardGrid.RowPitch * 3f;
            return transform.TransformPoint(new Vector3(x, 0.0028f, 0f));
        }

        /// <summary>Where each DIP pin lands, used to build the chip's pin colliders.</summary>
        public Vector3 IcPinWorldPosition(int startColumn, int pinNumber, out BreadboardRow row)
        {
            int col;
            _grid.IcsPinPlacement(startColumn, pinNumber, out col, out row);
            return SocketWorldPosition(col, row);
        }
    }
}
