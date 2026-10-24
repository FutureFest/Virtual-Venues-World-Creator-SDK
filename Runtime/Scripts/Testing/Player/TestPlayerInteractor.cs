using System.Collections.Generic;
using StarterAssets;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace VirtualVenues.WorldCreator
{
    /// <summary>
    /// Local, offline stand-in for the core project's interaction runtime so creators can test a world with the
    /// TestPlayer: nearest-Interactable prompt + E, Seats, TriggerEvents, carry-platform SplineMovers and a simple
    /// Vehicle drive (driver look-around + driver/throttle/speed <see cref="VehicleEvents"/>). Lives on the TestPlayer's PlayerArmature (PlayerInput sends OnInteract here).
    /// Runs after SplineMover (-50) so riders read this frame's pose, before ThirdPersonController (0).
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public class TestPlayerInteractor : MonoBehaviour
    {
        private const string InteractKey = "E";
        private const float ProbePadding = 0.2f;
        private const float StandUpHeight = 0.5f;
        private const float VehicleExitDistance = 1.5f;
        private const float CarryBand = 0.5f;
        private const int NoCameraTestLayer = 1;
        private const float VehicleRecenterDegreesPerSecond = 90f;
        private const float VehicleMovingForwardSpeed = 1f;
        private const float LookThreshold = 0.01f;

        private static readonly int AnimSpeed = Animator.StringToHash("Speed");
        private static readonly int AnimGrounded = Animator.StringToHash("Grounded");
        private static readonly int AnimSeated = Animator.StringToHash("Seated");

        private readonly Collider[] _overlaps = new Collider[32];
        private readonly RaycastHit[] _hits = new RaycastHit[16];
        private readonly HashSet<TriggerEvent> _insideTriggers = new HashSet<TriggerEvent>();
        private readonly HashSet<TriggerEvent> _nowInside = new HashSet<TriggerEvent>();
        private readonly Dictionary<TriggerEvent, float> _lastFired = new Dictionary<TriggerEvent, float>();
        private readonly HashSet<CustomControls> _insideZones = new HashSet<CustomControls>();
        private readonly HashSet<CustomControls> _nowInsideZones = new HashSet<CustomControls>();
        private readonly List<Collider> _scopeColliders = new List<Collider>();

        private CharacterController _controller = null;
        private ThirdPersonController _thirdPerson = null;
        private StarterAssetsInputs _input = null;
        private Animator _animator = null;
        private bool _hasSeatedParam = false;
        private float _probeRadius = 0.5f;
        private Vector3 _probeCenter = Vector3.up;

        private Interactable _target = null;
        private Interactable _pinnedTarget = null;
        private Transform _seatPoint = null;
        private Vehicle _vehicle = null;
        private float _vehicleSpeed = 0f;
        private float _vehicleRideHeight = 0f;
        private float _vehicleYawRate = 0f;
        private bool _vehicleThrottling = false;
        private Vector2 _vehicleOrbit = Vector2.zero;
        private CustomControls _seatControls = null;
        private bool _controlsUnlockedCursor = false;
        private bool _savedCursorLocked = true;
        private bool _savedCursorInputForLook = true;

        private Transform _carrier = null;
        private Vector3 _carryLastPos = Vector3.zero;
        private Quaternion _carryLastRot = Quaternion.identity;

        public bool IsSeated => _seatPoint != null;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _thirdPerson = GetComponent<ThirdPersonController>();
            _input = GetComponent<StarterAssetsInputs>();
            _animator = GetComponent<Animator>();
            // A creator may swap in their own controller; only drive "Seated" when it exists.
            if (_animator != null)
            {
                foreach (AnimatorControllerParameter param in _animator.parameters)
                {
                    if (param.nameHash == AnimSeated) { _hasSeatedParam = true; }
                }
            }
            if (_controller != null)
            {
                _probeRadius = _controller.radius + ProbePadding;
                _probeCenter = _controller.center;
            }
        }

        private void OnEnable()
        {
            Vehicle.OnNudge += OnVehicleNudge;
            CollisionScope.onCollisionScopeAdded += ApplyCollisionScope;
            foreach (CollisionScope scope in CollisionScope.Instances) { ApplyCollisionScope(scope); }
        }

        private void OnDisable()
        {
            Vehicle.OnNudge -= OnVehicleNudge;
            CollisionScope.onCollisionScopeAdded -= ApplyCollisionScope;
        }

        /// <summary>
        /// Play-mode stand-in for the app's collision layers, which world projects don't have. Stops-camera colliders
        /// go to Default (the follow camera's obstacle filter); the rest go to built-in layer 1 (TransparentFX),
        /// outside the filter but still solid and counted as ground. The character ignores colliders that don't stop
        /// players. Stops-vehicles is app only.
        /// </summary>
        private void ApplyCollisionScope(CollisionScope scope)
        {
            if (scope == null) { return; }
            int layer = scope.StopsCamera ? 0 : NoCameraTestLayer;
            if (layer == NoCameraTestLayer && _thirdPerson != null) { _thirdPerson.GroundLayers |= 1 << NoCameraTestLayer; }
            scope.GetColliders(_scopeColliders);
            foreach (Collider col in _scopeColliders)
            {
                col.gameObject.layer = layer;
                if (!scope.StopsPlayers && _controller != null) { Physics.IgnoreCollision(_controller, col, true); }
            }
        }

        private void Update()
        {
            ScanSurroundings();
            UpdateControlsCursor();
            if (_vehicle != null) { DriveVehicle(); }
            if (!IsSeated) { CarryOnPlatform(); }
        }

        private void LateUpdate()
        {
            if (!IsSeated) { return; }
            transform.SetPositionAndRotation(_seatPoint.position, _seatPoint.rotation);
            if (_vehicle != null) { OrbitVehicleCamera(); }
        }

#if ENABLE_INPUT_SYSTEM
        public void OnInteract(InputValue value)
        {
            if (!value.isPressed) { return; }
            Interact();
        }
#endif

        public void Interact()
        {
            if (_target == null) { return; }
            Interactable target = _target;
            target.OnLocalInteract();

            if (IsSeated) { Stand(); return; }
            if (target.TryGetComponent(out Seat seat)) { Sit(seat.SeatPoint, target, seat); return; }
            if (target.TryGetComponent(out Vehicle vehicle))
            {
                _vehicle = vehicle;
                _vehicleSpeed = 0f;
                Vector3 start = vehicle.transform.position;
                _vehicleRideHeight = TryGroundHeight(start, vehicle.transform, out float groundY) ? start.y - groundY : 0f;
                _vehicleYawRate = 0f;
                _vehicleThrottling = false;
                _vehicleOrbit = Vector2.zero;
                Sit(vehicle.Seats[0], target, vehicle);
                vehicle.Events.onDriverEnter.Invoke();
            }
        }

        // ---- Detection: poll (not trigger callbacks) so it keeps working while the controller is off when seated.

        private void ScanSurroundings()
        {
            Vector3 center = transform.TransformPoint(_probeCenter);
            int count = Physics.OverlapSphereNonAlloc(center, _probeRadius, _overlaps, ~0, QueryTriggerInteraction.Collide);

            Interactable nearest = null;
            float nearestSqr = float.MaxValue;
            _nowInside.Clear();
            _nowInsideZones.Clear();
            for (int i = 0; i < count; i++)
            {
                Collider col = _overlaps[i];
                if (col.transform.IsChildOf(transform)) { continue; }

                if (col.isTrigger && col.TryGetComponent(out TriggerEvent trigger)) { _nowInside.Add(trigger); }
                if (col.isTrigger && col.TryGetComponent(out CustomControls zone) && zone.ShowWhen == ControlsShowWhen.WhileInZone) { _nowInsideZones.Add(zone); }

                Interactable interactable = col.GetComponentInParent<Interactable>();
                if (interactable == null || !interactable.isActiveAndEnabled) { continue; }
                float sqr = (col.ClosestPoint(center) - center).sqrMagnitude;
                if (sqr < nearestSqr) { nearestSqr = sqr; nearest = interactable; }
            }

            SetTarget(_pinnedTarget != null ? _pinnedTarget : nearest);
            FireEnteredTriggers();
            UpdateZoneControls();
        }

        // ---- CustomControls zones: show while inside the trigger.

        private void UpdateZoneControls()
        {
            foreach (CustomControls zone in _insideZones)
            {
                if (zone != null && !_nowInsideZones.Contains(zone)) { zone.SetShown(false); }
            }
            foreach (CustomControls zone in _nowInsideZones) { zone.SetShown(true); }
            _insideZones.Clear();
            _insideZones.UnionWith(_nowInsideZones);
        }

        // The StarterAssets camera locks the cursor; free it while buttons are on screen so they can be clicked.
        private void UpdateControlsCursor()
        {
            bool want = CustomControls.Current != null;
            if (want == _controlsUnlockedCursor || _input == null) { return; }
            _controlsUnlockedCursor = want;
            if (want)
            {
                _savedCursorLocked = _input.cursorLocked;
                _savedCursorInputForLook = _input.cursorInputForLook;
                _input.cursorLocked = false;
                _input.cursorInputForLook = false;
                _input.look = Vector2.zero;
            }
            else
            {
                _input.cursorLocked = _savedCursorLocked;
                _input.cursorInputForLook = _savedCursorInputForLook;
            }
            bool locked = !want && _input.cursorLocked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private void SetTarget(Interactable target)
        {
            if (target == _target) { return; }
            if (_target != null) { _target.OnUnhighlight?.Invoke(); }
            _target = target;
            if (_target != null) { _target.OnHighlight?.Invoke(); }
        }

        // ---- TriggerEvents: fire on enter; Cooldown/Once enforced locally (the server does it in the core project).

        private void FireEnteredTriggers()
        {
            foreach (TriggerEvent trigger in _nowInside)
            {
                if (_insideTriggers.Contains(trigger)) { continue; }
                bool firedBefore = _lastFired.TryGetValue(trigger, out float last);
                if (firedBefore && trigger.Once) { continue; }
                if (firedBefore && Time.time - last < trigger.Cooldown) { continue; }
                _lastFired[trigger] = Time.time;
                trigger.InvokeLocalPlayerEnter();
                trigger.InvokeEveryone();
            }
            _insideTriggers.Clear();
            _insideTriggers.UnionWith(_nowInside);
        }

        // ---- Seats: copy the seat pose every LateUpdate (no parenting), same as the core project's MountBehaviour.

        private void Sit(Transform seatPoint, Interactable interactable, Component owner)
        {
            _seatPoint = seatPoint;
            _seatControls = CustomControls.FindForSeat(seatPoint, owner);
            if (_seatControls != null) { _seatControls.SetShown(true); }
            _pinnedTarget = interactable;
            _carrier = null;
            SetMovementEnabled(false);
            if (_animator == null) { return; }
            _animator.SetFloat(AnimSpeed, 0f);
            _animator.SetBool(AnimGrounded, true);
            if (_hasSeatedParam) { _animator.SetBool(AnimSeated, true); }
        }

        private void Stand()
        {
            Vector3 exit = _seatPoint.position + Vector3.up * StandUpHeight;
            if (_vehicle != null) { exit += _vehicle.transform.right * VehicleExitDistance; }
            Vector3 forward = Vector3.ProjectOnPlane(_seatPoint.forward, Vector3.up);
            Quaternion facing = forward.sqrMagnitude > 0.001f ? Quaternion.LookRotation(forward) : transform.rotation;

            if (_seatControls != null) { _seatControls.SetShown(false); }
            if (_vehicle != null)
            {
                if (_vehicleThrottling) { _vehicle.Events.onThrottleStop.Invoke(); }
                _vehicle.Events.onDriverExit.Invoke();
                _vehicle.Events.RaiseSpeed(0f); // the arcade drive stops dead when you get out
                _vehicleThrottling = false;
            }
            _seatControls = null;
            _seatPoint = null;
            _pinnedTarget = null;
            _vehicle = null;
            transform.SetPositionAndRotation(exit, facing);
            SetMovementEnabled(true);
            if (_animator != null && _hasSeatedParam) { _animator.SetBool(AnimSeated, false); }
        }

        private void SetMovementEnabled(bool enabled)
        {
            if (_thirdPerson != null) { _thirdPerson.enabled = enabled; }
            if (_controller != null) { _controller.enabled = enabled; }
        }

        // ---- Carry platforms: follow what we stand on when it's a carrier in the core project too
        // (SplineMover / AnimatedObject / KeepUpright with Carry Players on).

        private void CarryOnPlatform()
        {
            Transform carrier = FindCarrierUnderFeet();
            if (carrier != _carrier)
            {
                _carrier = carrier;
                if (carrier != null) { _carryLastPos = carrier.position; _carryLastRot = carrier.rotation; }
                return;
            }
            if (carrier == null) { return; }

            Vector3 local = Quaternion.Inverse(_carryLastRot) * (transform.position - _carryLastPos);
            Vector3 delta = carrier.position + carrier.rotation * local - transform.position;
            float yaw = Mathf.DeltaAngle(_carryLastRot.eulerAngles.y, carrier.rotation.eulerAngles.y);

            // Move the transform, not CharacterController.Move: ThirdPersonController reads controller.velocity as
            // walking speed and would keep "walking" the carry speed along its facing, straight off the platform.
            transform.position += delta;
            transform.Rotate(0f, yaw, 0f, Space.World);
            Physics.SyncTransforms();
            _carryLastPos = carrier.position;
            _carryLastRot = carrier.rotation;
        }

        private Transform FindCarrierUnderFeet()
        {
            if (_controller == null || !_controller.enabled) { return null; }
            Vector3 origin = transform.position + Vector3.up * 0.1f;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 0.4f, ~0, QueryTriggerInteraction.Ignore))
            {
                for (Transform t = hit.collider.transform; t != null; t = t.parent)
                {
                    if (IsCarrier(t)) { return t; }
                }
            }
            // Like the core project's carrier band: stay attached while within the ride + CarryBand above it (small hops).
            return _carrier != null && InCarrierBand(_carrier) ? _carrier : null;
        }

        private bool InCarrierBand(Transform carrier)
        {
            bool any = false;
            Bounds band = new Bounds();
            foreach (Collider c in carrier.GetComponentsInChildren<Collider>())
            {
                if (c.isTrigger || !c.enabled) { continue; }
                if (any) { band.Encapsulate(c.bounds); } else { band = c.bounds; any = true; }
            }
            if (!any) { return false; }
            band.max += Vector3.up * CarryBand;
            return band.Contains(transform.position);
        }

        private static bool IsCarrier(Transform t)
        {
            if (t.TryGetComponent(out SplineMover mover) && mover.CarryPlayers) { return true; }
            if (t.TryGetComponent(out AnimatedObject animated) && animated.CarryPlayers) { return true; }
            return t.TryGetComponent(out KeepUpright upright) && upright.CarryPlayers;
        }

        // ---- Vehicles: arcade drive from the driver seat.
        // ponytail: no physics/wheels/passengers, so handling won't match the core project's Rigidbody vehicle; port its model if creators need to tune handling here.

        private void DriveVehicle()
        {
            if (_input == null) { return; }
            Transform t = _vehicle.transform;
            bool boat = _vehicle.VehicleType == VehicleType.Boat;
            VehicleStats stats = _vehicle.Stats;
            float accel = boat ? _vehicle.BoatStats.enginePower : stats.acceleration;
            float turn = boat ? _vehicle.BoatStats.turnPower : stats.steer;

            _vehicleSpeed = Mathf.MoveTowards(_vehicleSpeed, _input.move.y * stats.topSpeed, accel * Time.deltaTime);
            float turnScale = Mathf.Clamp01(Mathf.Abs(_vehicleSpeed) / 2f) * Mathf.Sign(_vehicleSpeed);
            t.Rotate(0f, (_input.move.x * turn * 20f * turnScale + _vehicleYawRate) * Time.deltaTime, 0f, Space.World);
            _vehicleYawRate = Mathf.MoveTowards(_vehicleYawRate, 0f, 180f * Time.deltaTime);

            Vector3 pos = t.position + t.forward * (_vehicleSpeed * Time.deltaTime);
            if (!boat && TryGroundHeight(pos, t, out float groundY)) { pos.y = groundY + _vehicleRideHeight; }
            t.position = pos;

            bool throttling = _input.move.y > 0f;
            if (throttling != _vehicleThrottling) { (throttling ? _vehicle.Events.onThrottleStart : _vehicle.Events.onThrottleStop).Invoke(); }
            _vehicleThrottling = throttling;
            _vehicle.Events.RaiseSpeed(Mathf.Abs(_vehicleSpeed) / Mathf.Max(0.1f, stats.topSpeed));
        }

        // Driver look-around, same rule as the core project's VehicleCameraLook. PlayerCameraRoot is a child of the
        // armature (snapped to the seat), so its local rotation is the orbit relative to the vehicle.
        // ponytail: mouse only, and the cursor isn't locked while dragging (ThirdPersonController is off while seated).
        private void OrbitVehicleCamera()
        {
            if (_thirdPerson == null || _thirdPerson.CinemachineCameraTarget == null || _input == null) { return; }
            ControlStyle style = _thirdPerson.ControlStyleOverride != null ? _thirdPerson.ControlStyleOverride : ControlStyles.Active;
            LookModule look = style.Look;
            bool canRotate = ControlStyleMath.ShouldRotateCamera(look, LeftHeld, RightHeld, _input.cursorLocked);
            bool looking = canRotate && (look.activation != LookActivation.Locked || _input.look.sqrMagnitude >= LookThreshold);
            bool movingForward = _vehicleSpeed > VehicleMovingForwardSpeed;
            _vehicleOrbit = ControlStyleMath.StepVehicleOrbit(_vehicleOrbit, _input.look, looking, movingForward, look, VehicleRecenterDegreesPerSecond * Time.deltaTime);
            _thirdPerson.CinemachineCameraTarget.transform.localRotation = Quaternion.Euler(_vehicleOrbit.y, _vehicleOrbit.x, 0f);
        }

        private static bool LeftHeld
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Mouse.current != null && Mouse.current.leftButton.isPressed;
#else
                return Input.GetMouseButton(0);
#endif
            }
        }

        private static bool RightHeld
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Mouse.current != null && Mouse.current.rightButton.isPressed;
#else
                return Input.GetMouseButton(1);
#endif
            }
        }

        // ponytail: arcade stand-in for the runtime's VelocityChange impulse; the push decays at the drive's
        // acceleration and the spin at 180°/s², so it feels shorter than the Rigidbody version.
        private void OnVehicleNudge(Vehicle vehicle, float metersPerSecond, float degreesPerSecond)
        {
            if (vehicle == null || vehicle != _vehicle) { return; }
            _vehicleSpeed += metersPerSecond;
            _vehicleYawRate += degreesPerSecond;
        }

        private bool TryGroundHeight(Vector3 pos, Transform vehicle, out float groundY)
        {
            groundY = pos.y;
            int count = Physics.RaycastNonAlloc(pos + Vector3.up * 2f, Vector3.down, _hits, 6f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (_hits[i].transform.IsChildOf(vehicle) || _hits[i].transform.IsChildOf(transform)) { continue; }
                if (_hits[i].distance < best) { best = _hits[i].distance; groundY = _hits[i].point.y; }
            }
            return best < float.MaxValue;
        }

        // ---- Prompt

        private void OnGUI()
        {
            DrawCustomControls();
            if (_target == null) { return; }
            string text = !IsSeated ? SafeFormat(_target.InteractionDisplayText)
                : _vehicle != null ? $"Press {InteractKey} to get out" : $"Press {InteractKey} to stand up";
            GUIStyle style = new GUIStyle(GUI.skin.box) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
            Vector2 size = style.CalcSize(new GUIContent(text)) + new Vector2(24f, 12f);
            GUI.Box(new Rect((UnityEngine.Screen.width - size.x) * 0.5f, UnityEngine.Screen.height - size.y - 60f, size.x, size.y), text, style);
        }

        private static void DrawCustomControls()
        {
            CustomControls controls = CustomControls.Current;
            if (controls == null || controls.Controls == null) { return; }
            const float width = 140f, height = 48f, gap = 8f;
            int count = controls.Controls.Length;
            float x = (UnityEngine.Screen.width - (count * width + (count - 1) * gap)) * 0.5f;
            float y = UnityEngine.Screen.height - 130f - height;
            GUIStyle style = new GUIStyle(GUI.skin.button) { fontSize = 16 };
            for (int i = 0; i < count; i++)
            {
                CustomControl control = controls.Controls[i];
                if (control == null) { continue; }
                GUIContent content = control.icon != null ? new GUIContent(control.label, control.icon.texture) : new GUIContent(control.label);
                if (GUI.Button(new Rect(x + i * (width + gap), y, width, height), content, style)) { controls.Press(i); }
            }
        }

        private static string SafeFormat(string format)
        {
            if (string.IsNullOrEmpty(format)) { return $"Press {InteractKey} to interact"; }
            try { return string.Format(format, InteractKey); }
            catch (System.FormatException) { return format; }
        }
    }
}
