using System.Collections;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.XR.CoreUtils;
using ARLab.Core;

namespace ARLab.AR
{
    /// <summary>
    /// Owns the AR session lifecycle: support check, camera permission, session start and
    /// tracking-state reporting. Every failure path surfaces a real message instead of
    /// silently doing nothing.
    /// </summary>
    [RequireComponent(typeof(ARSession))]
    public sealed class ARSessionController : MonoBehaviour
    {
        [SerializeField] private ARSession arSession;
        [SerializeField] private XROrigin origin;
        [SerializeField] private bool verboseLogging;

        public ARSession Session => arSession;

        /// <summary>
        /// The XR origin, named "Origin" since ARSessionOrigin is deprecated in ARF 6.
        /// Resolved lazily because a rig assembled at runtime creates the origin after this
        /// component's Awake has already run.
        /// </summary>
        public XROrigin Origin
        {
            get
            {
                if (origin == null) origin = FindAnyObjectByType<XROrigin>();
                return origin;
            }
        }
        // In AR Foundation 6.x ARSession.state and ARSession.notTrackingReason are static,
        // because a process hosts a single session.
        public bool IsSessionRunning => arSession != null && arSession.enabled &&
                                        ARSession.state == ARSessionState.SessionTracking;
        public string DeviceModel => SystemInfo.deviceModel;

        private bool _permissionGranted;
        private bool _permissionPending;

        private void Awake()
        {
            if (arSession == null) arSession = GetComponent<ARSession>();
            if (origin == null) origin = FindAnyObjectByType<XROrigin>();
        }

        /// <summary>
        /// True when an XR loader is active. In the editor the default "None" loader means no
        /// session subsystem is ever created, so the AR layer has to degrade instead of wait.
        /// </summary>
        public static bool HasActiveLoader()
        {
            var loader = LoaderUtility.GetActiveLoader();
            return loader != null;
        }

        /// <summary>True when the loaded XR provider reports the session as usable.</summary>
        public bool IsArSupported()
        {
            if (arSession == null) return false;
            switch (ARSession.state)
            {
                case ARSessionState.Ready:
                case ARSessionState.SessionInitializing:
                case ARSessionState.SessionTracking:
                    return true;
                default:
                    // In the editor no session subsystem runs, so AR code paths stay reachable.
                    return Application.isEditor;
            }
        }

        /// <summary>
        /// Walks the app from Initializing to Scanning, handling permission and unsupported
        /// hardware as first-class states. Returns once the session is running.
        /// </summary>
        public IEnumerator BootSequence(ArStateMachine machine, System.Action<string> onStatus)
        {
            machine.TryTransition(ArAppState.Initializing);
            onStatus?.Invoke(ArStateText.For(ArAppState.Initializing));

            if (!IsArSupported())
            {
                Debug.LogError("[AR] Device does not support ARCore.");
                onStatus?.Invoke("This device does not support required AR features");
                machine.TryTransition(ArAppState.Unsupported);
                yield break;
            }

            yield return null;

            machine.TryTransition(ArAppState.CameraPermission);
            onStatus?.Invoke(ArStateText.For(ArAppState.CameraPermission));

            _permissionGranted = false;
            _permissionPending = true;
            RequestCameraPermission(onStatus);
            while (_permissionPending) yield return null;

            if (!_permissionGranted)
            {
                Debug.LogError("[AR] Camera permission denied.");
                machine.TryTransition(ArAppState.PermissionDenied);
                yield break;
            }

            arSession.enabled = true;

            // Wait for the session subsystem to actually come up rather than assuming it.
            // With no XR loader active (the normal editor default) no session subsystem is ever
            // created and the state stays None forever, so that case is treated as a simulated
            // session instead of stalling for the full timeout and failing the boot.
            bool simulated = !HasActiveLoader();
            float deadline = Time.realtimeSinceStartup + 20f;
            while (!simulated && ARSession.state != ARSessionState.SessionTracking)
            {
                if (ARSession.state == ARSessionState.Unsupported ||
                    ARSession.state == ARSessionState.NeedsInstall)
                {
                    Debug.LogError($"[AR] Session unavailable: {ARSession.state}");
                    onStatus?.Invoke(ARSession.state == ARSessionState.NeedsInstall
                        ? "ARCore services need installing on this device"
                        : "This device does not support required AR features");
                    machine.TryTransition(ArAppState.Unsupported);
                    yield break;
                }
                if (Time.realtimeSinceStartup > deadline)
                {
                    Debug.LogError($"[AR] Session stuck in {ARSession.state} after 20s.");
                    onStatus?.Invoke("AR session failed to initialize");
                    machine.TryTransition(ArAppState.SessionFailed);
                    yield break;
                }
                yield return null;
            }

            if (simulated)
            {
                if (verboseLogging) Debug.Log("[AR] No XR loader active; running the lab in simulation mode.");
                onStatus?.Invoke("Simulated AR mode - place the bench with the Place button");
            }
            else if (verboseLogging) Debug.Log("[AR] Session running.");

            // Warm up light estimation for a few frames so placement is not black-on-black.
            for (int i = 0; i < 3; i++) yield return null;

            machine.TryTransition(ArAppState.Scanning);
            onStatus?.Invoke(ArStateText.For(ArAppState.Scanning));
        }

        /// <summary>
        /// Requests the Android camera runtime permission and waits for the user's answer.
        /// Outside Android there is nothing to request, so this succeeds immediately.
        /// </summary>
        private void RequestCameraPermission(System.Action<string> onStatus)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
            {
                _permissionGranted = true;
                _permissionPending = false;
                return;
            }

            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera);
            // Android answers on a worker thread, so poll the authoritative flag rather than
            // relying on a callback.
            StartCoroutine(PollCameraPermission(onStatus));
#else
            _permissionGranted = true;
            _permissionPending = false;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private IEnumerator PollCameraPermission(System.Action<string> onStatus)
        {
            float deadline = Time.realtimeSinceStartup + 30f;
            while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
            {
                if (UnityEngine.Android.Permission.didDenyPermissionThisFrame(UnityEngine.Android.Permission.Camera))
                {
                    onStatus?.Invoke("Camera permission denied. Enable it in Settings.");
                    _permissionGranted = false;
                    _permissionPending = false;
                    yield break;
                }
                if (Time.realtimeSinceStartup > deadline)
                {
                    onStatus?.Invoke("Camera permission request timed out");
                    _permissionGranted = false;
                    _permissionPending = false;
                    yield break;
                }
                yield return null;
            }
            _permissionGranted = true;
            _permissionPending = false;
        }
#endif

        /// <summary>Describes the current session state for the HUD.</summary>
        public static string TrackingLabel(ARSession session)
        {
            if (session == null) return "No AR session";
            switch (ARSession.state)
            {
                case ARSessionState.None: return "Session not started";
                case ARSessionState.Unsupported: return "AR not supported";
                case ARSessionState.CheckingAvailability: return "Checking availability";
                case ARSessionState.NeedsInstall: return "ARCore needs installing";
                case ARSessionState.Installing: return "Installing ARCore";
                case ARSessionState.Ready: return "Ready";
                case ARSessionState.SessionInitializing: return "Preparing camera";
                case ARSessionState.SessionTracking: return "Tracking";
                default: return ARSession.state.ToString();
            }
        }

        /// <summary>Explains why the camera is not tracking well, driving the "move slowly" hints.</summary>
        public static string TrackingReason(ARSession session)
        {
            if (session == null) return "No AR session";
            switch (ARSession.notTrackingReason)
            {
                case NotTrackingReason.None: return string.Empty;
                case NotTrackingReason.Initializing: return "Initialising";
                case NotTrackingReason.Relocalizing: return "Relocalising - keep scanning";
                case NotTrackingReason.ExcessiveMotion: return "Move the device more slowly";
                case NotTrackingReason.InsufficientFeatures: return "Not enough visual detail - point at a textured surface";
                case NotTrackingReason.InsufficientLight: return "Too dark - find brighter lighting";
                case NotTrackingReason.Unsupported: return "AR is unsupported on this device";
                default: return "Tracking is limited";
            }
        }
    }
}
