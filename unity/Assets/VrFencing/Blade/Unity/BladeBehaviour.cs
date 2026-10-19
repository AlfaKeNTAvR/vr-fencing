using System.Collections.Generic;
using UnityEngine;
using VrFencing.Drills;
using VrFencing.Dummy;
using VrFencing.Settings;
using VrFencing.Strip;

namespace VrFencing.Blade
{
    /// <summary>
    /// The Fencer's épée on the sword-hand controller (PER-33). Every frame it reads the controller pose, turns it into
    /// the straight blade in the Strip frame, and feeds the Core <see cref="TouchDetector"/> with the Dummy's Target
    /// Zones. Touches and flat contacts go to the Drill runner (<see cref="DrillRunner.ReportContact"/>); glances are
    /// felt and seen but never reported, since only the point scores. Every event plays its haptic pattern, Touches
    /// flash the Dummy's zone. The drawn point stops against any part of the Dummy and the drawn blade bows as the hand
    /// pushes on (<see cref="DrawnBlade"/>, PER-48); the floor stops it the same way (PER-49), but floor contacts are
    /// never Touches and never reach the Drill runner, only the press buzz. All rules and math are in the Blade Core;
    /// this only reads the controller, draws and forwards.
    ///
    /// The blade uses no buttons, so it never competes with the off-hand menu laser or the Strip buttons.
    /// </summary>
    public sealed class BladeBehaviour : MonoBehaviour
    {
        // A Strip re-placed or the zones moved by more than this in one frame is a jump, not motion: the sweep restarts.
        private const float JumpMeters = 0.05f;
        private const float JumpDegrees = 5.0f;

        // Rubbing the flat along a zone would buzz every few frames; one buzz per this long is enough.
        private const float FlatHapticCooldownSeconds = 0.15f;

        [SerializeField]
        [Tooltip("The Practice object's Drill runner; Touches and flat contacts are reported to it.")]
        private DrillRunnerBehaviour _drillRunner;

        [SerializeField]
        [Tooltip("The Dummy whose Target Zones the blade is tested against.")]
        private DummyBehaviour _dummy;

        [SerializeField]
        [Tooltip("The Strip; its En Garde Line is the Strip frame the zones are in.")]
        private StripBehaviour _strip;

        private readonly TouchDetector _detector = new TouchDetector();
        private readonly DrawnBlade _drawnBlade = new DrawnBlade();
        private readonly List<BladeObstacle> _obstacles = new List<BladeObstacle>();

        private TrainerSettings _settings;
        private TouchRules _rules;
        private TrackedPose _mountInController;
        private OVRCameraRig _cameraRig;
        private Transform _mount;
        private BladeVisual _visual;
        private BladeHapticPlayer _haptics;
        private TrackedPose? _lastEnGardeLine;
        private float? _lastDummyDistanceMeters;
        private float _lastFlatHapticAt = float.NegativeInfinity;
        private float _lastPressHapticAt = float.NegativeInfinity;
        // The blade starts drawn; the first untracked frame hides it.
        private bool _isTracked = true;

        /// <summary>Raised for every Touch, glance and flat contact, with positions in the Strip frame.</summary>
        public event System.Action<BladeEvent> Contact;

        /// <summary>Takes changed Settings: sword hand, Touch rule, mount and angles, haptics.</summary>
        public void ApplySettings(TrainerSettings settings)
        {
            _settings = settings.Clone();
            _rules = TouchRules.For(_settings.Blade.TouchRule, _settings.Blade.TouchPressure);
            _drawnBlade.Flex.SetSoftness(_settings.Blade.Flex);
            _mountInController = BladePose.GetMountInController(_settings.Blade, _settings.Hand);
            AttachToSwordHand();
            RestartMotion();
        }

        private void Start()
        {
            _cameraRig = FindFirstObjectByType<OVRCameraRig>();
            if (_cameraRig == null)
            {
                Debug.LogError("[Blade] No OVRCameraRig in the scene; the blade cannot follow the controller.");
                enabled = false;
                return;
            }

            if (_drillRunner == null || _dummy == null || _strip == null)
            {
                Debug.LogWarning("[Blade] Missing the Drill runner, Dummy or Strip reference; Touches are not detected or reported.");
            }

            _mount = new GameObject("Blade Mount").transform;
            _visual = new BladeVisual(_mount);
            _haptics = new BladeHapticPlayer();
            ApplySettings(SettingsFile.Load());
        }

        private void OnDestroy()
        {
            _haptics?.Dispose();
            _visual?.Dispose();
        }

        // OVRCameraRig moves the controller anchors in Update; read them after that.
        private void LateUpdate()
        {
            var controller = _settings.Hand == Hand.Left ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
            var isTracked = OVRInput.GetControllerPositionTracked(controller) || OVRInput.GetControllerOrientationTracked(controller);
            if (isTracked != _isTracked)
            {
                _isTracked = isTracked;
                _visual.SetVisible(isTracked);
                RestartMotion();
            }

            if (!isTracked)
            {
                return;
            }

            var now = Time.timeAsDouble;
            var hasDummy = _dummy != null && _strip != null && _strip.EnGardeLine != null;

            // Everything runs in the Strip frame, where the Dummy's capsules are. Without a Strip the room frame stands in.
            var stripInRoom = hasDummy
                ? new TrackedPose(_strip.EnGardeLine.position.ToNumerics(), _strip.EnGardeLine.rotation.ToNumerics())
                : new TrackedPose(System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity);
            var controllerAnchor = _mount.parent;
            var controllerInRoom = new TrackedPose(controllerAnchor.position.ToNumerics(), controllerAnchor.rotation.ToNumerics());
            var forteInStrip = BladePose.GetBlade(StripFrame.ToStripFrame(stripInRoom, controllerInRoom), _mountInController);

            // The blade hangs (PER-52): its set curves it away from the grip's up (the thumb's side) and gravity sags it.
            // Detection and the drawn blade both use the line from the guard to the real, drooped point.
            var stripFromRoom = System.Numerics.Quaternion.Inverse(stripInRoom.Rotation);
            var gripUp = System.Numerics.Vector3.Transform(_mount.up.ToNumerics(), stripFromRoom);
            var down = System.Numerics.Vector3.Transform(-System.Numerics.Vector3.UnitY, stripFromRoom);
            var droop = BladeDroop.Create(forteInStrip.Direction, gripUp, down, _settings.Blade);
            var bladeInStrip = droop.GetChord(forteInStrip);

            if (hasDummy)
            {
                RestartOnJump(stripInRoom);
                DetectContacts(now, bladeInStrip);
            }

            var wasDecoupled = _drawnBlade.IsDecoupled;
            var release = _drawnBlade.Step(bladeInStrip, GatherObstacles(hasDummy), gripUp, Time.deltaTime, droop);
            if (release != PinRelease.None)
            {
                Debug.Log($"[Blade] Point released: {release}");
            }

            if (_drawnBlade.IsDecoupled != wasDecoupled)
            {
                Debug.Log(_drawnBlade.IsDecoupled
                    ? $"[Blade] Bent as far as it goes (push {_drawnBlade.Contact.PushMeters * 100.0f:0} cm, pinned {_drawnBlade.Contact.IsPinned}): the drawn blade holds and lags the hand"
                    : "[Blade] Hand back within reach: the drawn blade follows it again");
            }

            PlayPressHaptic((float)now);
            _visual.Draw(_drawnBlade, stripInRoom, (float)now);
        }

        /// <summary>
        /// The Dummy's whole body and the floor, in the Strip frame. Without a Strip the room frame stands in, and its
        /// y = 0 is the floor too (floor-level tracking).
        /// </summary>
        private IReadOnlyList<BladeObstacle> GatherObstacles(bool hasDummy)
        {
            _obstacles.Clear();
            if (hasDummy)
            {
                foreach (var capsule in _dummy.GetBodyOnStrip())
                {
                    _obstacles.Add(capsule);
                }
            }

            _obstacles.Add(BladeObstacle.Floor);
            return _obstacles;
        }

        /// <summary>While the point is pinned, a buzz that grows with the push; full while the drawn blade lags the hand.</summary>
        private void PlayPressHaptic(float now)
        {
            var isPressing = _drawnBlade.Contact.IsPinned || _drawnBlade.IsDecoupled;
            if (!isPressing || now - _lastPressHapticAt < BladeHaptics.PressPulseIntervalSeconds)
            {
                return;
            }

            _lastPressHapticAt = now;
            _haptics.PlayPress(BladeHaptics.GetPressStrength(_drawnBlade.Contact.PushMeters, _drawnBlade.IsDecoupled), _settings.Hand);
        }

        /// <summary>A Strip re-placed or the Dummy stood at another distance moves every zone at once: a jump, not motion.</summary>
        private void RestartOnJump(TrackedPose enGardeLine)
        {
            if (_lastEnGardeLine.HasValue && IsJump(_lastEnGardeLine.Value, enGardeLine))
            {
                RestartMotion();
            }

            _lastEnGardeLine = enGardeLine;

            var dummyDistanceMeters = _dummy.DistanceMeters;
            if (_lastDummyDistanceMeters.HasValue && Mathf.Abs(dummyDistanceMeters - _lastDummyDistanceMeters.Value) > JumpMeters)
            {
                RestartMotion();
            }

            _lastDummyDistanceMeters = dummyDistanceMeters;
        }

        private void DetectContacts(double now, BladeSegment bladeInStrip)
        {
            IReadOnlyList<ZoneCapsule> zones = _dummy.GetZonesOnStrip();
            foreach (var bladeEvent in _detector.Step(new BladeSample(now, bladeInStrip), zones, _rules))
            {
                HandleContact(bladeEvent, (float)now);
            }
        }

        private void HandleContact(BladeEvent bladeEvent, float now)
        {
            Debug.Log($"[Blade] {bladeEvent.Type} {bladeEvent.Zone} tip {bladeEvent.TipSpeedMetersPerSecond:0.00} m/s " +
                $"approach {bladeEvent.ApproachAngleDegrees:0} deg blade {bladeEvent.BladeAxisAngleDegrees:0} deg " +
                $"from center {bladeEvent.DistanceFromZoneCenterMeters * 100.0f:0.0} cm rule {_settings.Blade.TouchRule} " +
                $"pressure {_settings.Blade.TouchPressure} pressed {(bladeEvent.TimeSeconds - bladeEvent.FirstContactSeconds) * 1000.0:0} ms after contact");

            switch (bladeEvent.Type)
            {
                case BladeEventType.Touch:
                    Report(bladeEvent, ContactKind.PointFirst);
                    _dummy.FlashZone(bladeEvent.Zone);
                    PlayHaptic(bladeEvent);
                    break;
                case BladeEventType.FlatContact:
                    Report(bladeEvent, ContactKind.Flat);
                    if (now - _lastFlatHapticAt >= FlatHapticCooldownSeconds)
                    {
                        PlayHaptic(bladeEvent);
                        _lastFlatHapticAt = now;
                    }

                    break;
                case BladeEventType.TipGlance:
                    PlayHaptic(bladeEvent);
                    break;
            }

            _visual.Flash(bladeEvent.Type, now);
            Contact?.Invoke(bladeEvent);
        }

        private void Report(BladeEvent bladeEvent, ContactKind kind)
        {
            _drillRunner?.Runner?.ReportContact(new BladeContact(
                bladeEvent.Zone, kind, bladeEvent.TipSpeedMetersPerSecond, bladeEvent.DistanceFromZoneCenterMeters));
        }

        private void PlayHaptic(BladeEvent bladeEvent)
        {
            var pattern = BladeHaptics.GetChosenPattern(_settings.Blade, bladeEvent.Type);
            var strength = BladeHaptics.GetStrength(_settings.Blade, bladeEvent.TipSpeedMetersPerSecond);
            _haptics.Play(bladeEvent.Type, pattern, strength, _settings.Hand);
        }

        private void AttachToSwordHand()
        {
            var anchor = _settings.Hand == Hand.Left ? _cameraRig.leftControllerAnchor : _cameraRig.rightControllerAnchor;
            _mount.SetParent(anchor, false);
            _mount.SetLocalPositionAndRotation(_mountInController.Position.ToUnity(), _mountInController.Rotation.ToUnity());
        }

        private void RestartMotion()
        {
            _detector.Reset();
            _drawnBlade.Reset();
        }

        private static bool IsJump(TrackedPose before, TrackedPose after)
        {
            var moved = System.Numerics.Vector3.Distance(before.Position, after.Position);
            var turned = Quaternion.Angle(before.Rotation.ToUnity(), after.Rotation.ToUnity());
            return moved > JumpMeters || turned > JumpDegrees;
        }
    }
}
