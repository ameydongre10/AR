using UnityEngine;
using ARLab.Electronics;
using ARLab.Simulation;

namespace ARLab.Core
{
    /// <summary>All cross-system notifications. Keeps AR / Lab / UI layers decoupled.</summary>
    public readonly struct LabPlacedEvent { public readonly Vector3 Position; public readonly Quaternion Rotation; public LabPlacedEvent(Vector3 p, Quaternion r) { Position = p; Rotation = r; } }

    public readonly struct LabResetEvent { }

    public readonly struct ComponentAddedEvent { public readonly IElectronicComponent Component; public ComponentAddedEvent(IElectronicComponent c) { Component = c; } }
    public readonly struct ComponentRemovedEvent { public readonly IElectronicComponent Component; public ComponentRemovedEvent(IElectronicComponent c) { Component = c; } }

    public readonly struct WireChangedEvent { public readonly int WireCount; public WireChangedEvent(int c) { WireCount = c; } }
    public readonly struct NetChangedEvent { public readonly int NetCount; public NetChangedEvent(int c) { NetCount = c; } }

    /// <summary>Emitted after every successful simulation settle.</summary>
    public readonly struct SimulationResultEvent
    {
        public readonly bool Converged;
        public readonly bool Powered;
        public readonly int IterationCount;
        public readonly string Diagnostic;
        public SimulationResultEvent(bool converged, bool powered, int iters, string diag)
        { Converged = converged; Powered = powered; IterationCount = iters; Diagnostic = diag; }
    }

    public readonly struct PowerChangedEvent { public readonly bool IsOn; public readonly float Volts; public PowerChangedEvent(bool on, float v) { IsOn = on; Volts = v; } }

    public readonly struct InputSwitchChangedEvent { public readonly string SwitchId; public readonly LogicValue Value; public InputSwitchChangedEvent(string id, LogicValue v) { SwitchId = id; Value = v; } }

    public readonly struct ValidationResultEvent { public readonly ExperimentReport Report; public ValidationResultEvent(ExperimentReport r) { Report = r; } }

    public readonly struct TutorialAdvancedEvent { public readonly int StepIndex; public TutorialAdvancedEvent(int i) { StepIndex = i; } }
    public readonly struct TutorialCompletedEvent { }

    /// <summary>Transient user-facing message.</summary>
    public readonly struct NotificationEvent
    {
        public readonly string Message;
        public readonly NotificationSeverity Severity;
        public NotificationEvent(string m, NotificationSeverity s) { Message = m; Severity = s; }
    }

    public enum NotificationSeverity { Info, Success, Warning, Error }

    /// <summary>Raised by a node when its value settles, so visuals can subscribe directly.</summary>
    public readonly struct NetValueEvent { public readonly int NetId; public readonly LogicValue Value; public NetValueEvent(int id, LogicValue v) { NetId = id; Value = v; } }

    public readonly struct SelectionEvent { public readonly IElectronicComponent Component; public SelectionEvent(IElectronicComponent c) { Component = c; } }

    /// <summary>Emitted when the set of tracked planes changes, for HUD and reticle logic.</summary>
    public readonly struct PlaneCountEvent
    {
        public readonly int Usable;
        public readonly int Total;
        public readonly bool Visible;
        public PlaneCountEvent(int usable, int total, bool visible) { Usable = usable; Total = total; Visible = visible; }
    }
}
