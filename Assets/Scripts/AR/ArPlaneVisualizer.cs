using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using ARLab.Core;
using ARLab.Laboratory;

namespace ARLab.AR
{
    /// <summary>
    /// Shows detected horizontal planes so the student can see where a tap will land, then
    /// hides them once the lab is placed. Recounts only when the plane set actually changes.
    /// </summary>
    [RequireComponent(typeof(ARPlaneManager))]
    [DisallowMultipleComponent]
    public sealed class ArPlaneVisualizer : MonoBehaviour
    {
        [SerializeField] private ARPlaneManager planeManager;
        [SerializeField] private GameObject planeVisualPrefab;
        [SerializeField] private bool showPlanesBeforePlacement = true;
        [SerializeField] private bool showPlanesAfterPlacement = false;
        [SerializeField] private Color planeTint = new Color(0.30f, 0.75f, 1.00f);
        [SerializeField, Range(0.05f, 1f)] private float planeAlpha = 0.35f;

        private bool _placed;
        private bool _initialised;
        private int _usablePlaneCount;
        private int _totalPlaneCount;

        private static readonly List<ARPlane> _planeScratch = new List<ARPlane>(16);
        private static readonly List<Renderer> _rendererScratch = new List<Renderer>(16);

        public int UsablePlaneCount => _usablePlaneCount;
        public int TotalPlaneCount => _totalPlaneCount;
        public bool PlanesVisible => _placed ? showPlanesAfterPlacement : showPlanesBeforePlacement;

        private void Awake()
        {
            if (planeManager == null) planeManager = GetComponent<ARPlaneManager>();
        }

        public void Configure(ARPlaneManager mgr)
        {
            planeManager = mgr;
            if (planeManager == null) return;

            planeManager.enabled = true;
            // Horizontal-up only: a bench belongs on a table, and vertical walls are noisy
            // reticle targets for a student trying to place a flat board.
            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
            EnsurePlaneVisual();
            _initialised = false;
        }

        /// <summary>
        /// AR Foundation only creates something to show per plane when <c>planePrefab</c> is
        /// assigned. Left null there are no child renderers at all, so the plane overlay would
        /// silently never appear. The prefab is generated here so the repository needs no
        /// binary art assets.
        /// </summary>
        private void EnsurePlaneVisual()
        {
            if (planeManager.planePrefab != null) { planeVisualPrefab = planeManager.planePrefab; return; }

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "ARPlaneVisual";
            LabMaterials.Discard(quad.GetComponent<Collider>());

            var renderer = quad.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = LabMaterials.TransparentOverlay(planeTint, planeAlpha);
            quad.SetActive(false);

            // Kept out of the active hierarchy: ARPlaneManager instantiates it per plane.
            quad.transform.SetParent(transform, false);
            planeVisualPrefab = quad;
            planeManager.planePrefab = quad;
        }

        private void OnEnable()
        {
            if (planeManager == null) planeManager = GetComponent<ARPlaneManager>();
            planeManager.trackablesChanged.AddListener(OnTrackablesChanged);
            _initialised = true;
            Recount(true);
        }

        private void OnDisable()
        {
            if (planeManager != null) planeManager.trackablesChanged.RemoveListener(OnTrackablesChanged);
        }

        private void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARPlane> _) => Recount(true);

        private void Update()
        {
            Recount(false);
        }

        /// <summary>Updates plane counts and notifies listeners only when something changed.</summary>
        private void Recount(bool force)
        {
            if (planeManager == null) return;

            int usable = ARRaycastController.CountUsableHorizontalPlanes(planeManager, _planeScratch);
            int total = CountPlanes(planeManager);
            bool visibilityChanged = false;
            bool wanted = PlanesVisible;

            if (force || !_initialised)
            {
                visibilityChanged = true;
            }
            else if (usable != _usablePlaneCount || total != _totalPlaneCount)
            {
                visibilityChanged = true;
            }

            if (!visibilityChanged) return;

            _usablePlaneCount = usable;
            _totalPlaneCount = total;
            _initialised = true;
            ApplyVisibility(wanted);
            EventBus.Publish(new PlaneCountEvent(usable, total, wanted));
        }

        public void SetPlaced(bool placed)
        {
            if (_placed == placed) return;
            _placed = placed;
            bool visible = PlanesVisible;
            ApplyVisibility(visible);
            // Publish so the HUD and tutorial see the overlay disappear along with it.
            EventBus.Publish(new PlaneCountEvent(_usablePlaneCount, _totalPlaneCount, visible));
        }

        /// <summary>TrackableCollection has no Count, so enumerate the (very short) plane list.</summary>
        private static int CountPlanes(ARPlaneManager mgr)
        {
            int n = 0;
            foreach (ARPlane _ in mgr.trackables) n++;
            return n;
        }

        private void ApplyVisibility(bool visible)
        {
            if (planeManager == null) return;
            foreach (ARPlane p in planeManager.trackables)
            {
                if (p == null) continue;
                p.GetComponentsInChildren(false, _rendererScratch);
                for (int i = 0; i < _rendererScratch.Count; i++)
                {
                    if (_rendererScratch[i] != null) _rendererScratch[i].enabled = visible;
                }
                _rendererScratch.Clear();
            }
        }
    }
}
