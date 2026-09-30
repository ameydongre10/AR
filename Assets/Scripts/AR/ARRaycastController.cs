using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ARLab.AR
{
    /// <summary>
    /// Thin, correct wrapper over AR Foundation's <see cref="ARRaycastManager"/>. Uses
    /// ARRaycastSubsystem only, so hits respect real plane anchors instead of relying on
    /// physics colliders (which ARPlanes do not provide).
    /// </summary>
    [RequireComponent(typeof(ARRaycastManager))]
    public sealed class ARRaycastController : MonoBehaviour
    {
        [SerializeField] private ARRaycastManager raycastManager;
        private static readonly List<ARRaycastHit> _hits = new List<ARRaycastHit>(8);

        public ARRaycastManager Manager => raycastManager;
        public int LastHitCount { get; private set; }

        private void Awake()
        {
            if (raycastManager == null) raycastManager = GetComponent<ARRaycastManager>();
        }

        public void Configure(ARRaycastManager mgr) => raycastManager = mgr;

        private void OnEnable()
        {
            if (raycastManager != null) raycastManager.enabled = true;
        }

        private void OnDisable()
        {
            if (raycastManager != null) raycastManager.enabled = false;
        }

        /// <summary>Raycasts against detected surfaces. Returns false when nothing is hit.</summary>
        public bool TryRaycast(Vector2 screenPoint, out Pose pose, out ARPlane plane)
        {
            pose = default;
            plane = null;
            LastHitCount = 0;

            if (raycastManager == null) return false;

            const TrackableType surfaces = TrackableType.PlaneWithinPolygon | TrackableType.PlaneEstimated;
            if (!raycastManager.Raycast(screenPoint, _hits, surfaces))
                return false;

            if (_hits.Count == 0) return false;
            LastHitCount = _hits.Count;

            // Prefer the nearest *usable* surface to the camera. The first hit is not
            // special: it is commonly a still-initialising plane, and seeding the search
            // with it would stop any usable hit behind it from ever winning.
            Camera cam = Camera.main;
            bool found = false;
            ARRaycastHit best = default;
            float bestDist = 0f;

            for (int i = 0; i < _hits.Count; i++)
            {
                if (!IsPlaneUsable(_hits[i].trackable as ARPlane)) continue;
                float d = cam != null
                    ? Vector3.SqrMagnitude(_hits[i].pose.position - cam.transform.position)
                    : _hits[i].pose.position.sqrMagnitude;
                if (!found || d < bestDist) { found = true; bestDist = d; best = _hits[i]; }
            }

            if (!found) return false;

            pose = best.pose;
            plane = best.trackable as ARPlane;
            return true;
        }

        /// <summary>Rejects hits whose plane is still being tracked badly, e.g. after tracking loss.</summary>
        public static bool IsPlaneUsable(ARPlane plane)
        {
            if (plane == null) return false;   // placement needs a real surface
            return plane.trackingState == TrackingState.Tracking;
        }

        /// <summary>Every currently tracked horizontal plane, for the reticle and debug overlay.</summary>
        public static int CountUsableHorizontalPlanes(ARPlaneManager mgr, List<ARPlane> scratch)
        {
            scratch.Clear();
            if (mgr == null) return 0;
            foreach (ARPlane p in mgr.trackables)
            {
                // HorizontalUp only: a lab bench belongs on a table, not a ceiling.
                if (p.alignment != PlaneAlignment.HorizontalUp) continue;
                if (p.trackingState != TrackingState.Tracking) continue;
                scratch.Add(p);
            }
            return scratch.Count;
        }
    }
}
