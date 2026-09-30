using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using ARLab.Core;
using ARLab.Laboratory;

namespace ARLab.AR
{
    /// <summary>
    /// Drives placement of the laboratory rig: a reticle that follows a real AR raycast,
    /// tap-to-place, then drag / two-finger rotate / pinch-scale on the placed root.
    /// World coordinates are never hard-coded.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ARPlacementManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private ARRaycastController raycasts;
        [SerializeField] private GameObject reticle;
        [SerializeField] private Transform labRoot;
        [SerializeField] private Renderer labRootRenderer;

        [Header("Transform limits")]
        [SerializeField] private float minScale = 0.25f;
        [SerializeField] private float maxScale = 3f;
        [SerializeField] private float dragSpeed = 1f;
        [SerializeField] private float rotateSpeed = 1f;
        [SerializeField] private float minVerticalAngle = 10f;
        [SerializeField] private float maxVerticalAngle = 80f;

        [Header("Behaviour")]
        [SerializeField] private bool allowVerticalDrag = false;

        public bool IsPlaced { get; private set; }
        public Vector3 LastPlacementPosition { get; private set; }
        public int PlacementCount { get; private set; }
        public bool HasValidHit { get; private set; }

        private ArStateMachine _machine;
        private Vector2 _lastScreenPoint;
        private bool _dragging;
        private float _pinchStartDistance;
        private float _pinchStartScale;
        private float _twistStartAngle;
        private Quaternion _twistStartRotation;
        private bool _gestureActive;
        private float _planeIdleTime;

        public void Configure(ARRaycastController rc, GameObject reticlePrefab, Transform root, Renderer highlight)
        {
            raycasts = rc;
            labRoot = root;
            labRootRenderer = highlight;
            if (reticlePrefab != null)
            {
                // Called twice when the graph is rebuilt, so an existing instance is replaced
                // rather than leaked. Mode-aware because editor tooling runs this too.
                if (reticle != null) LabMaterials.Discard(reticle);
                reticle = Instantiate(reticlePrefab);
                reticle.name = "PlacementReticle";
                // Parented here rather than left at the scene root, so the instance's lifetime
                // matches the manager that owns it.
                reticle.transform.SetParent(transform, true);
                reticle.SetActive(false);
            }
        }

        public void Attach(ArStateMachine machine) => _machine = machine;

        private void Update()
        {
            if (_machine == null) return;

            if (!IsPlaced)
                UpdateReticle();
            else
                UpdateManipulation();

            if (reticle != null) reticle.SetActive(!IsPlaced && HasValidHit);
        }

        private void UpdateReticle()
        {
            if (raycasts == null) return;
            if (_machine.Current != ArAppState.Scanning && _machine.Current != ArAppState.SurfaceDetected) return;

            if (raycasts.TryRaycast(_lastScreenPoint, out Pose pose, out ARPlane plane))
            {
                HasValidHit = true;
                _planeIdleTime = 0f;
                if (reticle != null)
                {
                    reticle.transform.SetPositionAndRotation(pose.position, pose.rotation);
                    reticle.SetActive(true);
                }
                if (_machine.Current == ArAppState.Scanning)
                    _machine.TryTransition(ArAppState.SurfaceDetected);
            }
            else
            {
                HasValidHit = false;
                if (reticle != null) reticle.SetActive(false);
                // Drop back to "scanning" if the surface was lost (tracking dropout).
                if (_machine.Current == ArAppState.SurfaceDetected)
                {
                    _planeIdleTime += Time.deltaTime;
                    if (_planeIdleTime > 0.75f) _machine.TryTransition(ArAppState.Scanning);
                }
            }
        }

        // ------------------------------------------------------------------ placement

        public bool TryPlace()
        {
            if (IsPlaced) return false;
            if (labRoot == null) return false;
            if (!raycasts.TryRaycast(_lastScreenPoint, out Pose pose, out _))
            {
                EventBus.Publish(new NotificationEvent("No surface detected. Aim at a flat, well-lit area.",
                    NotificationSeverity.Warning));
                return false;
            }
            return TryPlaceAt(pose);
        }

        /// <summary>
        /// Places the rig at a caller-supplied pose. This is the single place placement is
        /// actually committed, so a raycast tap, a keyboard shortcut and a simulated editor
        /// session all end up doing exactly the same thing.
        /// </summary>
        public bool TryPlaceAt(Pose pose)
        {
            if (IsPlaced || labRoot == null) return false;

            // The rig is authored flat in the XZ plane, so world-up placement keeps it on the
            // surface regardless of the surface's own roll and pitch.
            Quaternion upright = Quaternion.LookRotation(pose.forward, Vector3.up);
            labRoot.SetPositionAndRotation(pose.position, upright);
            labRoot.gameObject.SetActive(true);
            labRoot.localScale = Vector3.one;
            IsPlaced = true;
            LastPlacementPosition = pose.position;
            PlacementCount++;

            if (_machine != null)
            {
                _machine.TryTransition(ArAppState.LabPlaced);
                _machine.TryTransition(ArAppState.Interacting);
            }

            EventBus.Publish(new LabPlacedEvent(pose.position, upright));
            EventBus.Publish(new NotificationEvent("Laboratory placed.", NotificationSeverity.Success));
            return true;
        }

        public void ClearPlacement()
        {
            if (!IsPlaced) return;
            IsPlaced = false;
            HasValidHit = false;
            if (labRoot != null) labRoot.gameObject.SetActive(false);
            if (_machine != null) _machine.TryTransition(ArAppState.Scanning);
        }

        public void SetScreenPoint(Vector2 p) => _lastScreenPoint = p;

        /// <summary>Lets the drag handler steer the reticle before the lab exists.</summary>
        public void UpdateReticleAt(Vector2 p) => _lastScreenPoint = p;

        // ------------------------------------------------------------------ manipulation

        /// <summary>One finger: move the rig across the tracked surface.</summary>
        public void BeginDrag() { _dragging = true; _gestureActive = true; }

        public void UpdateDrag(Vector2 screenPoint, Vector2 delta)
        {
            if (!_dragging || labRoot == null) return;

            if (raycasts != null && raycasts.TryRaycast(screenPoint, out Pose pose, out _))
            {
                Vector3 p = pose.position;
                if (!allowVerticalDrag)
                {
                    // Constrain to the plane's orientation so the rig never floats or sinks.
                    p = labRoot.position + Vector3.ProjectOnPlane(pose.position - labRoot.position, pose.up);
                }
                labRoot.position = Vector3.Lerp(labRoot.position, p, 1f - Mathf.Exp(-12f * Time.deltaTime * dragSpeed));
            }
        }

        public void EndDrag() { _dragging = false; _gestureActive = false; }

        public void BeginTwoFinger(Vector2 a, Vector2 b)
        {
            _pinchStartDistance = Vector2.Distance(a, b);
            _pinchStartScale = labRoot != null ? labRoot.localScale.x : 1f;
            _twistStartAngle = AngleBetween(a, b);
            _twistStartRotation = labRoot != null ? labRoot.rotation : Quaternion.identity;
            _gestureActive = true;
        }

        /// <summary>Two fingers: pinch scales and twist rotates simultaneously.</summary>
        public void UpdateTwoFinger(Vector2 a, Vector2 b)
        {
            if (labRoot == null) return;

            float dist = Vector2.Distance(a, b);
            if (_pinchStartDistance > 1f)
            {
                float target = _pinchStartScale * (dist / _pinchStartDistance);
                target = Mathf.Clamp(target, minScale, maxScale);
                labRoot.localScale = Vector3.one * target;
            }

            float angle = AngleBetween(a, b);
            float delta = Mathf.DeltaAngle(_twistStartAngle, angle);
            labRoot.rotation = Quaternion.AngleAxis(delta * rotateSpeed, Vector3.up) * _twistStartRotation;
            ClampTilt();
        }

        public void EndTwoFinger() { _gestureActive = false; }

        private void UpdateManipulation()
        {
            // Visual feedback only; actual gesture handling lives in ArInteractionManager.
        }

        private void ClampTilt()
        {
            if (labRoot == null) return;
            Vector3 e = labRoot.eulerAngles;
            if (e.x > minVerticalAngle && e.x < maxVerticalAngle) return;
            if (e.x > 180f) return;   // already beyond the limit, leave it alone
            labRoot.rotation = Quaternion.Euler(minVerticalAngle, e.y, e.z);
        }

        private static float AngleBetween(Vector2 a, Vector2 b)
        {
            Vector2 d = b - a;
            return Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        }

        public void ResetManipulation()
        {
            if (labRoot == null) return;
            labRoot.localScale = Vector3.one;
            labRoot.rotation = Quaternion.identity;
        }

        /// <summary>Highlights the rig so the student can see what a tap will grab.</summary>
        public void SetHighlight(bool on)
        {
            if (labRootRenderer == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();

            // URP Lit uses _BaseColor; the alpha channel drives the emissive rim.
            _mpb.Clear();
            _mpb.SetColor(HighlightColorId, on ? HighlightTint : new Color(0f, 0f, 0f, 0f));
            labRootRenderer.SetPropertyBlock(_mpb);
        }

        private static readonly int HighlightColorId = Shader.PropertyToID("_BaseColor");
        private static readonly Color HighlightTint = new Color(0.30f, 0.85f, 1f, 0.35f);
        private MaterialPropertyBlock _mpb;

        public float MinScale => minScale;
        public float MaxScale => maxScale;
        public bool IsDragging => _dragging;
        public bool IsTwoFingerActive => _gestureActive;
    }
}
