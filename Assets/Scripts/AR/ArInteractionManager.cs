using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using ARLab.Core;
using ARLab.Electronics;
using ARLab.Laboratory;

namespace ARLab.AR
{
    /// <summary>
    /// A breadboard hole presented as a wire endpoint. Created on demand for whichever socket
    /// the student tapped, so the board needs no per-hole GameObjects.
    /// </summary>
    public sealed class SocketTerminal : IConnectable
    {
        private readonly LaboratoryManager _lab;
        private readonly int _column;
        private readonly BreadboardRow _row;

        public SocketTerminal(LaboratoryManager lab, int terminalId, int column, BreadboardRow row)
        {
            _lab = lab;
            _column = column;
            _row = row;
            TerminalId = terminalId;
        }

        public int TerminalId { get; }
        public TerminalKind TerminalKind => TerminalKind.BreadboardSocket;
        public IElectronicComponent Owner => null;
        public string DisplayLabel => $"socket {_column}{_row}";
        public Vector3 AnchorWorld => _lab != null && _lab.Breadboard != null
            ? _lab.Breadboard.SocketWorldPosition(_column, _row)
            : Vector3.zero;
    }

    /// <summary>
    /// Routes new-Input-System touches to placement, rig manipulation, selection and wiring.
    /// A touch that lands on a UI panel is ignored here, so a tap on a button never also pokes
    /// the bench.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArInteractionManager : MonoBehaviour
    {
        [SerializeField] private ARPlacementManager placement;
        [SerializeField] private LaboratoryManager laboratory;
        [SerializeField] private ArStateMachine machine;
        [SerializeField] private Camera arCamera;

        [Header("Gesture tuning")]
        [SerializeField] private float tapMaxMovementPixels = 24f;
        [SerializeField] private float tapMaxDurationSeconds = 0.6f;
        [SerializeField] private float pickRange = 100f;

        private static readonly PointerEventData _pointerData = new PointerEventData(null);

        private Vector2 _pressPoint;
        private float _pressTime;
        private bool _pressed;
        private bool _dragged;
        private bool _dragging;
        private bool _twoFinger;

        public void Configure(ARPlacementManager p, Camera cam, LaboratoryManager lab, ArStateMachine sm)
        {
            placement = p;
            arCamera = cam != null ? cam : Camera.main;
            laboratory = lab;
            machine = sm;
        }

        private void Update()
        {
            if (placement == null || laboratory == null) return;
            if (arCamera == null) arCamera = Camera.main;

            var touch = Touchscreen.current;
            if (touch != null && (HasTwoFingers(touch) || touch.primaryTouch.press.isPressed || _touchActive))
            {
                _touchActive = touch.primaryTouch.press.isPressed;
                ReadTouch(touch);
                return;
            }

            _touchActive = false;
            var mouse = Mouse.current;
            if (mouse != null) ReadMouse(mouse);
        }

        private bool _touchActive;

        /// <summary>Two simultaneous contacts means pinch / twist, not a tap.</summary>
        private static bool HasTwoFingers(Touchscreen touch)
        {
            if (touch == null) return false;
            int active = 0;
            foreach (var t in touch.touches)
                if (t.press.isPressed) active++;
            return active >= 2;
        }

        // ------------------------------------------------------------------ input read

        private void ReadTouch(Touchscreen touch)
        {
            if (HasTwoFingers(touch))
            {
                ReadTwoFingers(touch);
                return;
            }

            var primary = touch.primaryTouch;
            Vector2 p = primary.position.ReadValue();

            if (primary.press.wasPressedThisFrame) { BeginPress(p); return; }
            if (primary.press.isPressed) MovePress(p);
            else if (primary.press.wasReleasedThisFrame) EndPress(p);
        }

        private void ReadMouse(Mouse mouse)
        {
            Vector2 p = mouse.position.ReadValue();
            if (mouse.leftButton.wasPressedThisFrame) { BeginPress(p); return; }
            if (mouse.leftButton.isPressed) MovePress(p);
            else if (mouse.leftButton.wasReleasedThisFrame) EndPress(p);
        }

        private void ReadTwoFingers(Touchscreen touch)
        {
            _pressed = false;
            _dragged = false;

            // Collect the two live contacts. Tracked by touch id rather than array index,
            // because indices shift as fingers lift and a replaced finger must restart the
            // gesture baseline instead of jumping the rig.
            _pointA = default;
            _pointB = default;
            int n = 0;
            foreach (var t in touch.touches)
            {
                if (!t.press.isPressed) continue;
                if (n == 0) { _pointA = t.position.ReadValue(); _idA = t.touchId.ReadValue(); }
                else if (n == 1) { _pointB = t.position.ReadValue(); _idB = t.touchId.ReadValue(); }
                n++;
                if (n == 2) break;
            }

            if (n < 2)
            {
                // Both fingers have to leave; until then the gesture stays live.
                if (_twoFinger) EndTwoFinger();
                return;
            }

            if (_twoFinger && (_idA != _touchA || _idB != _touchB))
            {
                // The pair changed, so rebase rather than interpreting it as a twist.
                EndTwoFinger();
            }

            if (!_twoFinger)
            {
                _twoFinger = true;
                _touchA = _idA;
                _touchB = _idB;
                if (placement.IsPlaced) placement.BeginTwoFinger(_pointA, _pointB);
                return;
            }
            if (placement.IsPlaced) placement.UpdateTwoFinger(_pointA, _pointB);
        }

        private void EndTwoFinger()
        {
            _twoFinger = false;
            if (placement != null) placement.EndTwoFinger();
        }

        private Vector2 _pointA, _pointB;
        private int _idA, _idB, _touchA, _touchB;

        // ------------------------------------------------------------------ press lifecycle

        private void BeginPress(Vector2 point)
        {
            _pressPoint = point;
            _pressTime = Time.unscaledTime;
            _pressed = true;
            _dragged = false;
            _dragging = false;
            placement.SetScreenPoint(point);
        }

        private void MovePress(Vector2 point)
        {
            if (!_pressed) return;
            Vector2 delta = point - _pressPoint;
            placement.SetScreenPoint(point);

            if (delta.sqrMagnitude > tapMaxMovementPixels * tapMaxMovementPixels) _dragged = true;

            if (placement.IsPlaced)
            {
                // Enter drag mode once, so the wire preview is not started by a still finger.
                if (!_dragging) { _dragging = true; placement.BeginDrag(); }
                placement.UpdateDrag(point, delta);
            }
            else if (_dragged)
            {
                // Dragging before placement just nudges the reticle along the surface.
                placement.UpdateReticleAt(point);
            }
        }

        private void EndPress(Vector2 point)
        {
            if (!_pressed) return;
            _pressed = false;
            if (placement.IsPlaced) placement.EndDrag();
            _dragging = false;

            bool isTap = !_dragged && (Time.unscaledTime - _pressTime) <= tapMaxDurationSeconds;
            if (!isTap) return;
            if (IsPointerOverUi(point)) return;

            if (!placement.IsPlaced)
            {
                placement.TryPlace();
                return;
            }

            HandleBenchTap(point);
        }

        // ------------------------------------------------------------------ bench taps

        private void HandleBenchTap(Vector2 point)
        {
            // Mid-wire: the next terminal touched completes the connection, anything else cancels.
            if (laboratory.IsWiring)
            {
                if (TryResolveTerminal(point, out IConnectable t) && t.TerminalId != laboratory.WireStart.TerminalId)
                    laboratory.CompleteWire(t);
                else
                    laboratory.CancelWire();
                return;
            }

            if (TryResolveTerminal(point, out IConnectable target))
            {
                IElectronicComponent owner = target.Owner;
                laboratory.Select(owner);

                if (laboratory.CanConnect(target, out _)) laboratory.BeginWire(target);
                return;
            }

            laboratory.Select(null);
        }

        /// <summary>
        /// Resolves a screen point to a terminal. Component pins win over the board surface
        /// because the pins sit above it, and rails are checked last.
        /// </summary>
        public bool TryResolveTerminal(Vector2 screenPoint, out IConnectable terminal)
        {
            terminal = null;
            if (arCamera == null) arCamera = Camera.main;
            if (arCamera == null) return false;

            Ray ray = arCamera.ScreenPointToRay(screenPoint);
            int mask = Physics.DefaultRaycastLayers;

            if (Physics.Raycast(ray, out RaycastHit hit, pickRange, mask, QueryTriggerInteraction.Collide))
            {
                ComponentPin pin = hit.collider.GetComponent<ComponentPin>();
                if (pin != null && pin.IsConnectable)
                {
                    terminal = pin;
                    return true;
                }
            }

            Breadboard board = laboratory.Breadboard;
            if (board != null && Physics.Raycast(ray, out RaycastHit boardHit, pickRange, mask, QueryTriggerInteraction.Collide))
            {
                if (boardHit.collider.transform.IsChildOf(board.transform) &&
                    board.TrySocketAtWorld(boardHit.point, out int id, out int col, out BreadboardRow row))
                {
                    terminal = new SocketTerminal(laboratory, id, col, row);
                    return true;
                }
            }

            return false;
        }

        private static bool IsPointerOverUi(Vector2 screenPoint)
        {
            var es = EventSystem.current;
            if (es == null) return false;
            _pointerData.Reset();
            _pointerData.position = screenPoint;
            return es.IsPointerOverGameObject(0);
        }
    }
}
