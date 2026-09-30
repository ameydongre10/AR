using System.Collections.Generic;
using UnityEngine;

namespace ARLab.Core
{
    /// <summary>
    /// Canonical AR application lifecycle. Every system reads this to decide what
    /// operations are legal, so invalid actions can be rejected instead of failing silently.
    /// </summary>
    public enum ArAppState
    {
        Boot = 0,
        Initializing = 1,
        CameraPermission = 2,
        Scanning = 3,
        SurfaceDetected = 4,
        ReadyToPlace = 5,
        LabPlaced = 6,
        Interacting = 7,
        Simulation = 8,
        ExperimentComplete = 9,
        Unsupported = 10,
        PermissionDenied = 11,
        SessionFailed = 12
    }

    /// <summary>Human-readable status line shown in the AR HUD for the current state.</summary>
    public static class ArStateText
    {
        public static string For(ArAppState s)
        {
            switch (s)
            {
                case ArAppState.Boot: return "Starting up";
                case ArAppState.Initializing: return "Initializing AR session";
                case ArAppState.CameraPermission: return "Camera permission required";
                case ArAppState.Scanning: return "Move your device slowly to detect a surface";
                case ArAppState.SurfaceDetected: return "Surface detected - tap to place laboratory";
                case ArAppState.ReadyToPlace: return "Tap a detected surface to place the laboratory";
                case ArAppState.LabPlaced: return "Laboratory placed";
                case ArAppState.Interacting: return "Place components, wire the circuit, then run";
                case ArAppState.Simulation: return "Simulation running";
                case ArAppState.ExperimentComplete: return "Experiment completed";
                case ArAppState.Unsupported: return "This device does not support required AR features";
                case ArAppState.PermissionDenied: return "Camera permission denied. Enable it in Settings.";
                case ArAppState.SessionFailed: return "AR session failed to initialize";
                default: return s.ToString();
            }
        }
    }

    /// <summary>
    /// Guarded finite state machine. Only transitions declared in <see cref="Rules"/> are
    /// accepted; anything else is rejected and reported.
    /// </summary>
    public sealed class ArStateMachine
    {
        private static readonly Dictionary<ArAppState, ArAppState[]> Rules = new Dictionary<ArAppState, ArAppState[]>
        {
            { ArAppState.Boot,             new[] { ArAppState.Initializing, ArAppState.Unsupported } },
            { ArAppState.Initializing,     new[] { ArAppState.CameraPermission, ArAppState.Scanning, ArAppState.Unsupported, ArAppState.SessionFailed } },
            { ArAppState.CameraPermission, new[] { ArAppState.Scanning, ArAppState.PermissionDenied } },
            { ArAppState.Scanning,         new[] { ArAppState.SurfaceDetected, ArAppState.CameraPermission } },
            { ArAppState.SurfaceDetected,  new[] { ArAppState.ReadyToPlace, ArAppState.Scanning } },
            { ArAppState.ReadyToPlace,     new[] { ArAppState.LabPlaced, ArAppState.Scanning } },
            { ArAppState.LabPlaced,        new[] { ArAppState.Interacting, ArAppState.ReadyToPlace, ArAppState.Scanning } },
            { ArAppState.Interacting,      new[] { ArAppState.Simulation, ArAppState.LabPlaced } },
            { ArAppState.Simulation,       new[] { ArAppState.Interacting, ArAppState.ExperimentComplete } },
            { ArAppState.ExperimentComplete, new[] { ArAppState.Interacting, ArAppState.LabPlaced } },
            { ArAppState.Unsupported,      System.Array.Empty<ArAppState>() },
            { ArAppState.PermissionDenied, new[] { ArAppState.CameraPermission } },
            { ArAppState.SessionFailed,    new[] { ArAppState.Initializing } }
        };

        public ArAppState Current { get; private set; } = ArAppState.Boot;
        public ArAppState Previous { get; private set; } = ArAppState.Boot;
        public event System.Action<ArAppState, ArAppState> StateChanged;

        public bool CanTransition(ArAppState next) => IsAllowed(Current, next);

        public static bool IsAllowed(ArAppState from, ArAppState to)
        {
            if (from == to) return false;
            return Rules.TryGetValue(from, out ArAppState[] allowed) &&
                   System.Array.IndexOf(allowed, to) >= 0;
        }

        /// <summary>Attempts a transition. Returns false (and logs) if the move is not legal.</summary>
        public bool TryTransition(ArAppState next)
        {
            if (!IsAllowed(Current, next))
            {
                Debug.LogWarning($"[ArStateMachine] Rejected illegal transition {Current} -> {next}");
                return false;
            }

            Previous = Current;
            Current = next;
            StateChanged?.Invoke(Previous, Current);
            return true;
        }

        /// <summary>Forces a state without a transition check. Only for recovery paths
        /// (e.g. user re-grants permission after a denial).</summary>
        public void Force(ArAppState next)
        {
            Previous = Current;
            Current = next;
            StateChanged?.Invoke(Previous, Current);
        }

        public string Describe() => $"{Current}  ({ArStateText.For(Current)})";
    }
}
