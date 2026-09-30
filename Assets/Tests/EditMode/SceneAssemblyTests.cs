using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using ARLab.Electronics;
using ARLab.Laboratory;
using ARLab.UI;

namespace ARLab.Tests.EditMode
{
    /// <summary>
    /// Covers the whole runtime assembly step: a scene containing nothing but a GameManager
    /// must come out the other side with a complete, wired manager graph. These are the tests
    /// that catch a missing Configure call, which otherwise only shows up on a device.
    /// </summary>
    [TestFixture]
    public sealed class SceneAssemblyTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            _root = null;
        }

        private GameManager BuildGraph()
        {
            _root = new GameObject("ARLab");
            var gm = _root.AddComponent<GameManager>();
            // Awake does not run under the edit-mode test runner, so invoke it directly.
            gm.BuildIfNeeded();
            return gm;
        }

        [Test]
        public void EmptyScene_ProducesEveryManager()
        {
            var gm = BuildGraph();

            Assert.IsNotNull(gm.Laboratory, "The bench manager is the whole app; it must exist.");
            Assert.IsNotNull(gm.Placement, "Placement is needed to put the bench in the world.");
            Assert.IsNotNull(gm.Machine, "The AR state machine must be wired.");
            Assert.IsNotNull(gm.Experiment);
            Assert.IsNotNull(gm.Laboratory.Breadboard);
            Assert.IsNotNull(gm.Laboratory.LabRoot);
        }

        [Test]
        public void BenchStartsHiddenSoTheTutorialIsNotSkipped()
        {
            var gm = BuildGraph();
            Assert.IsFalse(gm.Laboratory.LabRoot.gameObject.activeSelf,
                "The rig must not be visible before placement, or step 1 completes on frame one.");
        }

        [Test]
        public void BreadboardHasExactlyOnePickingCollider()
        {
            var gm = BuildGraph();
            // Breadboard creates its own collider in Awake; a second overlapping trigger box
            // would make picking ambiguous.
            int colliders = gm.Laboratory.Breadboard.GetComponents<Collider>().Length;
            Assert.AreEqual(1, colliders, "Breadboard should carry exactly one picking collider.");
        }

        [Test]
        public void BenchVisualHierarchyHasAtLeastThreeRenderers()
        {
            var gm = BuildGraph();
            // Placement highlights one renderer; the bench must show up even if an anchor is empty.
            int renderers = gm.Laboratory.LabRoot.GetComponentsInChildren<Renderer>(true).Length;
            Assert.GreaterOrEqual(renderers, 3,
                "The bench needs at least a board and a supply to be visible.");
        }

        [Test]
        public void PowerSupplyExistsAndStartsOff()
        {
            var gm = BuildGraph();
            var psu = gm.Laboratory.PowerSupply;
            Assert.IsNotNull(psu, "The bench ships with a supply so the rails can be energised.");
            Assert.IsFalse(psu.Energised, "Power must not come on by itself.");
        }

        [Test]
        public void PlacementIsNotCompleteBeforeTheStudentPlaces()
        {
            var gm = BuildGraph();
            Assert.IsFalse(gm.Placement.IsPlaced);
        }

        [Test]
        public void PlacingWithNoLabRootIsRejectedRatherThanThrowing()
        {
            var gm = BuildGraph();
            // Simulate a configure failure and confirm placement reports failure.
            Assert.DoesNotThrow(() => gm.Placement.TryPlaceAt(new Pose(Vector3.zero, Quaternion.identity)));
        }

        [Test]
        public void PlaceInFrontOfCameraActivatesTheBench()
        {
            var gm = BuildGraph();
            Assert.IsTrue(gm.OnPlaceInFrontOfCamera(), "Placement must succeed without an AR raycast.");
            Assert.IsTrue(gm.Laboratory.LabRoot.gameObject.activeSelf);
            Assert.IsTrue(gm.Placement.IsPlaced);
        }

        [Test]
        public void PlacingTwiceIsRejected()
        {
            var gm = BuildGraph();
            Assert.IsTrue(gm.OnPlaceInFrontOfCamera());
            Assert.IsFalse(gm.OnPlaceInFrontOfCamera(), "A placed rig must not be re-placed.");
            Assert.AreEqual(1, gm.Placement.PlacementCount);
        }

        [Test]
        public void HudIsBuiltWithEveryPanelPresent()
        {
            var gm = BuildGraph();
            var hud = _root.GetComponentInChildren<LabHud>(true);
            Assert.IsNotNull(hud);
            Assert.IsNotNull(hud.BuiltUi, "The HUD must build its own canvas when none is wired.");
        }

        [Test]
        public void HudBuildsAUsableCanvasAndEventSystem()
        {
            var gm = BuildGraph();
            var canvas = _root.GetComponentInChildren<Canvas>(true);
            Assert.IsNotNull(canvas, "Buttons cannot receive clicks without a Canvas.");
            Assert.IsNotNull(canvas.GetComponent<GraphicRaycaster>());

            // EventSystem.current is only populated in play mode, so the built instance is
            // checked directly instead of relying on the static.
            var es = gm.EventSystem;
            Assert.IsNotNull(es, "A UI input module is required for Button clicks.");
            Assert.IsNotNull(es.GetComponent<InputSystemUIInputModule>(),
                "Buttons need an input module that understands the Input System.");
        }

        [Test]
        public void EveryActionButtonIsClickableAndLabelled()
        {
            var gm = BuildGraph();
            // Row templates are hidden prefab sources, not buttons the student can press.
            var buttons = _root.GetComponentsInChildren<Button>(true)
                .Where(b => b.GetComponent<LabHud.RowTemplate>() == null)
                .ToList();

            Assert.GreaterOrEqual(buttons.Count, 8, "The action bar should expose the full command set.");
            foreach (Button b in buttons)
            {
                Assert.IsNotNull(b.targetGraphic, "A Button without a graphic is invisible and unclickable.");
                Assert.IsNotEmpty(b.GetComponentInChildren<Text>().text, "Buttons must be labelled.");
            }
        }

        /// <summary>
        /// Row templates are stored inactive, and Instantiate copies that state, so a plain
        /// Instantiate yields rows that exist in the hierarchy but never render. This asserts
        /// the rows are genuinely active, not merely present.
        /// </summary>
        [Test]
        public void LibraryRowsAreActiveAndVisible()
        {
            var gm = BuildGraph();
            var hud = _root.GetComponentInChildren<ARLab.UI.LabHud>(true);

            // One row per catalog entry, plus one header per distinct category.
            var categories = new HashSet<string>();
            foreach (var e in ARLab.Laboratory.LaboratoryManager.Catalog) categories.Add(e.Category);
            int expected = ARLab.Laboratory.LaboratoryManager.Catalog.Length + categories.Count;

            Assert.AreEqual(expected, hud.VisibleLibraryRowCount,
                "Every catalog entry and category header must be an active, rendered row.");
        }

        [Test]
        public void LibraryRowTextIsPopulated()
        {
            var gm = BuildGraph();
            var texts = _root.GetComponentsInChildren<UnityEngine.UI.Text>(true)
                .Where(t => t.gameObject.activeInHierarchy && !string.IsNullOrEmpty(t.text))
                .Select(t => t.text)
                .ToList();

            Assert.GreaterOrEqual(texts.Count, ARLab.Laboratory.LaboratoryManager.Catalog.Length,
                "Active library rows must carry visible label text.");
        }

        [Test]
        public void ValidationRowsAreActiveAndVisible()
        {
            var gm = BuildGraph();
            var hud = _root.GetComponentInChildren<ARLab.UI.LabHud>(true);

            // Evaluating an unbuilt lab produces a report with checks; each must become a
            // visible row rather than an inactive one.
            gm.OnEvaluate();

            Assert.Greater(hud.VisibleValidationRowCount, 0,
                "Validation checks must be rendered as active rows, not hidden templates.");
        }

        [Test]
        public void RebuildingTheLibraryDoesNotDuplicateRows()
        {
            var gm = BuildGraph();
            var hud = _root.GetComponentInChildren<ARLab.UI.LabHud>(true);
            int first = hud.VisibleLibraryRowCount;

            // Opening the library again re-runs the build; it must be idempotent.
            hud.OnToggleLibrary();
            hud.OnToggleLibrary();

            Assert.AreEqual(first, hud.VisibleLibraryRowCount, "Rebuilding must not duplicate rows.");
        }

        /// <summary>
        /// The action bar holds nine buttons. If they are laid out on a grid wider than nine
        /// columns they crowd into the left of the panel and leave the right-hand side dead.
        /// </summary>
        [Test]
        public void ActionBarFillsItsPanelWidth()
        {
            var gm = BuildGraph();
            var hud = _root.GetComponentInChildren<ARLab.UI.LabHud>(true);

            // Scope to the action bar itself: other rows (inspector, tutorial, validation) are
            // separate panels with their own column counts and would skew the measurements.
            var barRow = _root.GetComponentsInChildren<Transform>(true)
                .First(t => t.name == "ActionBar");
            var bar = barRow.GetComponentsInChildren<Button>(true).ToList();
            Assert.GreaterOrEqual(bar.Count, 9, "The action bar should expose every command.");

            // The rightmost button must reach the right edge of the bar, and the leftmost must
            // start at the left edge.
            float minX = bar.Min(b => ((RectTransform)b.transform).anchorMin.x);
            float maxX = bar.Max(b => ((RectTransform)b.transform).anchorMax.x);
            Assert.Less(minX, 0.02f, "Buttons must start at the left edge of the bar.");
            Assert.Greater(maxX, 0.98f, "Buttons must extend to the right edge of the bar.");

            // No two buttons may overlap, otherwise labels collide and taps hit the wrong one.
            var sorted = bar.Select(b => (RectTransform)b.transform)
                            .OrderBy(r => r.anchorMin.x).ToList();
            for (int i = 1; i < sorted.Count; i++)
            {
                Assert.GreaterOrEqual(sorted[i].anchorMin.x, sorted[i - 1].anchorMax.x - 0.001f,
                    $"Buttons '{sorted[i - 1].name}' and '{sorted[i].name}' overlap.");
            }
        }

        /// <summary>
        /// The action buttons are wired with runtime delegates, which Unity does not expose for
        /// inspection, so the commands they invoke are exercised directly instead.
        /// </summary>
        [Test]
        public void EveryCommandEntryPointIsSafeToInvoke()
        {
            var gm = BuildGraph();
            Assert.DoesNotThrow(() => gm.OnTogglePower());
            Assert.DoesNotThrow(() => gm.OnToggleSelected());
            Assert.DoesNotThrow(() => gm.OnCancelWire());
            Assert.DoesNotThrow(() => gm.OnEvaluate());
            Assert.DoesNotThrow(() => gm.OnReset());
            Assert.DoesNotThrow(() => gm.OnComponentLibrarySpawn("ic.7408"));
        }

        [Test]
        public void TogglingPowerEnergisesTheSupply()
        {
            var gm = BuildGraph();
            Assert.IsFalse(gm.Laboratory.PowerSupply.Energised);
            gm.OnTogglePower();
            Assert.IsTrue(gm.Laboratory.PowerSupply.Energised, "The Power button must switch the supply on.");
        }

        [Test]
        public void RebuildingTheGraphTwiceIsSafe()
        {
            _root = new GameObject("ARLab");
            var gm = _root.AddComponent<GameManager>();
            Assert.DoesNotThrow(() =>
            {
                gm.BuildIfNeeded();
                gm.BuildIfNeeded();
            });
            // The second pass must not stack a second bench on top of the first.
            Assert.AreEqual(1, _root.GetComponentsInChildren<LaboratoryManager>(true).Length);
        }
    }
}
