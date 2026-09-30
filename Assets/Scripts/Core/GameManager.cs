using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using ARLab.AR;
using ARLab.Core;
using ARLab.Electronics;
using ARLab.Laboratory;
using ARLab.Simulation;
using ARLab.UI;

namespace ARLab
{
    /// <summary>
    /// Single entry point that builds the bench, boots the AR session, and owns the
    /// progress-driven tutorial. Scene references are resolved or created, so a fresh
    /// scene produces a working laboratory without manual setup.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameManager : MonoBehaviour
    {
        [Header("Wiring (assigned by the scene builder, resolved at runtime otherwise)")]
        [SerializeField] private ARSessionController session;
        [SerializeField] private ArPlaneVisualizer planeVisualizer;
        [SerializeField] private ARRaycastController raycasts;
        [SerializeField] private ARPlacementManager placement;
        [SerializeField] private ArInteractionManager interaction;
        [SerializeField] private LaboratoryManager laboratory;
        [SerializeField] private ArStateMachine machine = new ArStateMachine();

        [Header("Tutorial")]
        [SerializeField] private TutorialController tutorial;
        [SerializeField] private LabHud hud;
        [SerializeField] private ExperimentDefinition experiment = ExperimentDefinition.SopXor;

        [Header("Bench geometry (metres)")]
        [SerializeField] private int breadboardColumns = 63;
        [SerializeField] private bool splitRails;

        private ExperimentValidator _validator;
        private Coroutine _boot;
        private GameObject _eventSystem;

        public ArStateMachine Machine => machine;
        public LaboratoryManager Laboratory => laboratory;
        public ARPlacementManager Placement => placement;
        public ExperimentDefinition Experiment => experiment;
        /// <summary>The EventSystem built for UI input, or null when the scene supplies one.</summary>
        public EventSystem EventSystem => _eventSystem != null ? _eventSystem.GetComponent<EventSystem>() : null;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            BuildIfNeeded();
        }

        /// <summary>
        /// Buttons only receive clicks when an EventSystem with a UI input module exists.
        /// Built rather than authored so a fresh scene is runnable, and marked persistent only
        /// in play mode because DontDestroyOnLoad throws in the editor.
        /// </summary>
        private void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;

            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
            if (Application.isPlaying) DontDestroyOnLoad(go);
            _eventSystem = go;
        }

        private void Start()
        {
            _boot = StartCoroutine(Boot());
        }

        // ------------------------------------------------------------------ assembly

        /// <summary>
        /// Creates the XR rig, the bench and the manager graph if the scene is empty.
        /// Public and idempotent so the editor tooling can run the exact same assembly that
        /// play mode runs, instead of maintaining a second scene-building path.
        /// </summary>
        public void BuildIfNeeded()
        {
            if (session == null) session = GetComponentInChildren<ARSessionController>();
            if (session == null) session = BuildSessionRig();

            if (laboratory == null) laboratory = GetComponentInChildren<LaboratoryManager>();
            if (laboratory == null) laboratory = BuildBench();

            if (planeVisualizer == null) planeVisualizer = session.GetComponentInChildren<ArPlaneVisualizer>(true);
            if (raycasts == null) raycasts = session.GetComponentInChildren<ARRaycastController>(true);
            if (planeVisualizer == null) planeVisualizer = session.gameObject.AddComponent<ArPlaneVisualizer>();
            if (raycasts == null) raycasts = session.gameObject.AddComponent<ARRaycastController>();

            var cam = session.GetComponentInChildren<Camera>(true);

            if (placement == null) placement = GetComponentInChildren<ARPlacementManager>();
            if (placement == null)
            {
                var go = new GameObject("ARPlacement");
                go.transform.SetParent(transform, false);
                placement = go.AddComponent<ARPlacementManager>();
            }

            if (interaction == null) interaction = GetComponentInChildren<ArInteractionManager>();
            if (interaction == null)
            {
                var go = new GameObject("ARInteraction");
                go.transform.SetParent(transform, false);
                interaction = go.AddComponent<ArInteractionManager>();
            }

            if (tutorial == null) tutorial = GetComponentInChildren<TutorialController>();
            if (tutorial == null)
            {
                var tutGo = new GameObject("Tutorial");
                tutGo.transform.SetParent(transform, false);
                tutorial = tutGo.AddComponent<TutorialController>();
            }

            if (hud == null) hud = GetComponentInChildren<LabHud>();
            if (hud == null)
            {
                EnsureEventSystem();
                var canvasGo = new GameObject("HudCanvas");
                canvasGo.transform.SetParent(transform, false);
                hud = canvasGo.AddComponent<LabHud>();
            }

            // Explicit wiring: no component goes looking for another at runtime.
            planeVisualizer.Configure(planeVisualizer.GetComponent<ARPlaneManager>());
            raycasts.Configure(session.GetComponent<ARRaycastManager>());
            placement.Attach(machine);

            // Placement is configured, not merely attached: without the lab root and raycast
            // controller it silently does nothing, and without a reticle the student has no
            // idea where a tap will land.
            placement.Configure(raycasts, BuildReticle(), laboratory.LabRoot, laboratory.LabRoot.GetComponentInChildren<Renderer>());

            interaction.Configure(placement, cam, laboratory, machine);
            hud.Configure(this, laboratory, tutorial);
            tutorial.Configure(laboratory);

            _validator = new ExperimentValidator(laboratory.Simulator, laboratory.Breadboard);
        }

        /// <summary>
        /// Ring-and-cross reticle built from primitives so the repository carries no binary art.
        /// Its own transform is driven by the placement raycast, so the pivot is its base.
        /// </summary>
        private GameObject BuildReticle()
        {
            // Parented under this manager so the template is never left stranded at the scene
            // root, where it would be saved into the scene and instantiated again at runtime.
            var root = new GameObject("ReticlePrefab");
            root.transform.SetParent(transform, false);
            Material ring = LabMaterials.TransparentOverlay(new Color(0.25f, 0.85f, 1f), 0.75f);
            Material line = LabMaterials.TransparentOverlay(new Color(1f, 1f, 1f), 0.55f);

            var ringGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ringGo.name = "Ring";
            ringGo.transform.SetParent(root.transform, false);
            ringGo.transform.localScale = new Vector3(0.06f, 0.06f, 1f);
            Discard(ringGo.GetComponent<Collider>());
            ringGo.GetComponent<MeshRenderer>().sharedMaterial = ring;

            for (int i = 0; i < 4; i++)
            {
                var tickGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
                tickGo.name = "Tick" + i;
                tickGo.transform.SetParent(root.transform, false);
                float a = i * 90f * Mathf.Deg2Rad;
                tickGo.transform.localPosition = new Vector3(Mathf.Cos(a) * 0.042f, Mathf.Sin(a) * 0.042f, 0f);
                tickGo.transform.localRotation = Quaternion.Euler(0f, 0f, i * 90f);
                tickGo.transform.localScale = new Vector3(0.018f, 0.004f, 1f);
                Discard(tickGo.GetComponent<Collider>());
                tickGo.GetComponent<MeshRenderer>().sharedMaterial = line;
            }

            // ARPlacementManager instantiates this and hides the template.
            root.SetActive(false);
            return root;
        }

        /// <summary>Mode-aware removal, since the reticle is also built by editor tooling.</summary>
        private static void Discard(Object o) => LabMaterials.Discard(o);

        private ARSessionController BuildSessionRig()
        {
            var arSessionGo = new GameObject("ARSession");
            arSessionGo.transform.SetParent(transform, false);
            var arSession = arSessionGo.AddComponent<ARSession>();
            arSessionGo.AddComponent<ARPlaneManager>();
            arSessionGo.AddComponent<ARRaycastManager>();
            arSessionGo.AddComponent<ARSessionController>();

            var originGo = new GameObject("Origin");
            originGo.transform.SetParent(transform, false);
            var origin = originGo.AddComponent<Unity.XR.CoreUtils.XROrigin>();
            // Nested enum: Unity.XR.CoreUtils.XROrigin.TrackingOriginMode.
            origin.RequestedTrackingOriginMode = Unity.XR.CoreUtils.XROrigin.TrackingOriginMode.Device;

            var camGo = new GameObject("AR Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(originGo.transform, false);
            var cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            cam.nearClipPlane = 0.01f;
            camGo.AddComponent<ARCameraManager>();
            // XROrigin drives its child cameras itself, so no extra pose driver is needed.

            origin.CameraFloorOffsetObject = camGo;
            return arSessionGo.GetComponent<ARSessionController>();
        }

        private LaboratoryManager BuildBench()
        {
            var rootGo = new GameObject("LabRoot");
            rootGo.transform.SetParent(transform, false);
            // The bench must not exist until it is placed, otherwise the tutorial's first step
            // is satisfied on frame one and the rig is visible floating in mid-air.
            rootGo.SetActive(false);

            var anchorGo = new GameObject("ComponentAnchor");
            anchorGo.transform.SetParent(rootGo.transform, false);

            var boardGo = new GameObject("Breadboard");
            boardGo.transform.SetParent(rootGo.transform, false);
            var board = boardGo.AddComponent<Breadboard>();
            // AddComponent runs Awake immediately, so size the board explicitly afterwards.
            // Breadboard already creates its own picking collider in Awake; adding another here
            // would leave two overlapping trigger boxes on the same object.
            board.Configure(breadboardColumns, splitRails);

            var psu = ComponentFactory.CreatePowerSupply();
            psu.transform.SetParent(rootGo.transform, false);
            // Sit the supply to the left of the board on the same surface.
            psu.transform.localPosition = new Vector3(-0.06f, 0f, 0f);
            // The reticle uses only this renderer to highlight the rig, so it must be the
            // one the test expects. Explicitly setting enabled makes it resilient.
            var psuR = psu.GetComponentInChildren<Renderer>(true);
            if (psuR != null) psuR.enabled = true;

            var labGo = new GameObject("Laboratory");
            labGo.transform.SetParent(transform, false);
            var lab = labGo.AddComponent<LaboratoryManager>();
            lab.Configure(rootGo.transform, board, psu, anchorGo.transform);
            return lab;
        }

        // ------------------------------------------------------------------ boot

        private IEnumerator Boot()
        {
            if (session == null)
            {
                Debug.LogError("[GameManager] No AR session controller; cannot start.");
                yield break;
            }

            yield return session.BootSequence(machine, OnStatus);

            // Everything below the AR layer is usable in the editor too, so the simulator
            // can be driven and tested without a headset.
            laboratory.SolveAndRefresh();
            EventBus.Publish(new NotificationEvent("Ready. Tap a surface to place the bench.", NotificationSeverity.Info));
            if (tutorial != null) tutorial.Begin(experiment);
        }

        private void OnStatus(string text) => EventBus.Publish(new NotificationEvent(text, NotificationSeverity.Info));

        private void OnEnable()
        {
            EventBus.Subscribe<LabPlacedEvent>(OnLabPlaced);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<LabPlacedEvent>(OnLabPlaced);
        }

        /// <summary>The plane overlay is only useful while choosing a surface, so hide it once placed.</summary>
        private void OnLabPlaced(LabPlacedEvent _)
        {
            if (planeVisualizer != null) planeVisualizer.SetPlaced(true);
        }

        // ------------------------------------------------------------------ commands (UI entry points)

        public void OnComponentLibrarySpawn(string catalogKey)
        {
            if (laboratory == null) return;
            if (!placement.IsPlaced)
            {
                EventBus.Publish(new NotificationEvent("Place the laboratory first.", NotificationSeverity.Warning));
                return;
            }

            // New parts land on the board, just in front of its centre, on the deck surface.
            Transform board = laboratory.Breadboard != null ? laboratory.Breadboard.transform : laboratory.LabRoot;
            Vector3 p = board.position + board.forward * 0.03f + Vector3.up * 0.005f;

            IElectronicComponent c = laboratory.Spawn(catalogKey, p);
            if (c != null) laboratory.Select(c);
        }

        /// <summary>
        /// Places the rig on a real raycast hit when one exists, otherwise on a clear spot in
        /// front of the camera. The fallback is what makes the app usable on a device that has
        /// not yet found a plane, and in the editor where no AR raycast ever succeeds.
        /// </summary>
        public bool OnPlaceInFrontOfCamera()
        {
            if (placement == null || placement.IsPlaced) return false;

            Camera cam = session != null ? session.GetComponentInChildren<Camera>(true) : Camera.main;
            Vector3 center = Vector3.zero;
            Quaternion facing = Quaternion.identity;
            bool have = false;

            if (cam != null)
            {
                // A screen-centre hit is the honest choice: it is the surface the reticle showed.
                if (raycasts != null && raycasts.TryRaycast(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f),
                        out Pose hit, out _))
                {
                    center = hit.position;
                    facing = hit.rotation;
                    have = true;
                }
                else if (Physics.Raycast(cam.transform.position, cam.transform.forward,
                             out RaycastHit phys, 12f))
                {
                    center = phys.point;
                    facing = Quaternion.FromToRotation(Vector3.forward, phys.normal);
                    have = true;
                }
                else
                {
                    center = cam.transform.position + cam.transform.forward * 0.6f;
                    facing = Quaternion.Euler(0f, cam.transform.eulerAngles.y, 0f);
                }
            }

            // Sitting exactly on the camera's own plane would clip the near geometry, so lift
            // the rig by a fraction of its own height.
            Vector3 lift = Vector3.up * 0.002f;
            return placement.TryPlaceAt(new Pose(center + lift, facing));
        }

        public void OnTogglePower() => laboratory.TogglePower();
        public void OnToggleSelected() => laboratory.ToggleSelectedSwitch();
        public void OnDeleteSelected() => laboratory.DeleteSelected();
        public void OnReset() => laboratory.ResetExperiment();
        public void OnCancelWire() => laboratory.CancelWire();

        public void OnEvaluate()
        {
            if (_validator == null) return;
            ExperimentReport report = _validator.Validate(experiment, ReadExperimentOutput);
            EventBus.Publish(new ValidationResultEvent(report));
            EventBus.Publish(new NotificationEvent(report.Headline,
                report.IsComplete ? NotificationSeverity.Success
                    : report.FailCount > 0 ? NotificationSeverity.Error : NotificationSeverity.Warning));
            if (report.IsComplete && machine.TryTransition(ArAppState.ExperimentComplete))
                EventBus.Publish(new TutorialCompletedEvent());
        }

        /// <summary>Reads the experiment's output net, defaulting to the first LED anode.</summary>
        private LogicValue ReadExperimentOutput()
        {
            ConnectionGraph g = laboratory.Graph;
            IReadOnlyList<IElectronicComponent> all = laboratory.Components;
            for (int i = 0; i < all.Count; i++)
            {
                if (!(all[i] is LedIndicator led)) continue;
                IReadOnlyList<ComponentPin> pins = led.Pins;
                for (int p = 0; p < pins.Count; p++)
                {
                    if (pins[p] == null || !pins[p].IsDataInput) continue;
                    return laboratory.Simulator.ValueOfTerminal(pins[p].TerminalId);
                }
            }
            return LogicValue.Undefined;
        }

        private void OnDestroy()
        {
            if (_boot != null) StopCoroutine(_boot);
        }
    }
}
