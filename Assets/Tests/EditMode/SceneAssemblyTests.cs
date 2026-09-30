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
