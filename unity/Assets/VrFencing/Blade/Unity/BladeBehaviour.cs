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
    /// flash the Dummy's zone and bow the drawn blade. All rules and math are in the Blade Core; this only reads the
    /// controller, draws and forwards.
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
        private readonly BladeFlex _flex = new BladeFlex();

        private TrainerSettings _settings;
        private TouchRules _rules;
        private TrackedPose _mountInController;
        private OVRCameraRig _cameraRig;
        private Transform _mount;
        private BladeVisual _visual;
        private BladeHapticPlayer _haptics;
        private TrackedPose? _lastEnGardeLine;
        private float _lastFlatHapticAt = float.NegativeInfinity;
        // The blade starts drawn; the first untracked frame hides it.
        private bool _isTracked = true;

        /// <summary>Raised for every Touch, glance and flat contact, with positions in the Strip frame.</summary>
        public event System.Action<BladeEvent> Contact;

        /// <summary>Takes changed Settings: sword hand, Touch rule, mount and angles, haptics.</summary>
        public void ApplySettings(TrainerSettings settings)
        {
            _settings = settings.Clone();
            _rules = TouchRules.For(_settings.Blade.TouchRule);
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
            var worldBlade = new BladeSegment(_mount.position.ToNumerics(), _mount.TransformPoint(0.0f, 0.0f, BladePose.BladeLengthMeters).ToNumerics());
            _flex.Step(worldBlade, Time.deltaTime);
            DetectContacts(now, worldBlade);
            _visual.Draw(worldBlade, _flex, (float)now);
        }

        private void DetectContacts(double now, BladeSegment worldBlade)
        {
            if (_dummy == null || _strip == null || _strip.EnGardeLine == null)
            {
                return;
            }

            var enGardeLine = new TrackedPose(_strip.EnGardeLine.position.ToNumerics(), _strip.EnGardeLine.rotation.ToNumerics());
            if (_lastEnGardeLine.HasValue && IsJump(_lastEnGardeLine.Value, enGardeLine))
            {
                _detector.Reset();
            }

            _lastEnGardeLine = enGardeLine;
            var controllerAnchor = _mount.parent;
            var controllerInRoom = new TrackedPose(controllerAnchor.position.ToNumerics(), controllerAnchor.rotation.ToNumerics());
            var controllerInStrip = StripFrame.ToStripFrame(enGardeLine, controllerInRoom);
            var bladeInStrip = BladePose.GetBlade(controllerInStrip, _mountInController);

            IReadOnlyList<ZoneCapsule> zones = _dummy.GetZonesOnStrip();
            foreach (var bladeEvent in _detector.Step(new BladeSample(now, bladeInStrip), zones, _rules))
            {
                HandleContact(bladeEvent, worldBlade, (float)now);
            }
        }

        private void HandleContact(BladeEvent bladeEvent, BladeSegment worldBlade, float now)
        {
            Debug.Log($"[Blade] {bladeEvent.Type} {bladeEvent.Zone} tip {bladeEvent.TipSpeedMetersPerSecond:0.00} m/s " +
                $"approach {bladeEvent.ApproachAngleDegrees:0} deg blade {bladeEvent.BladeAxisAngleDegrees:0} deg " +
                $"from center {bladeEvent.DistanceFromZoneCenterMeters * 100.0f:0.0} cm rule {_settings.Blade.TouchRule}");

            switch (bladeEvent.Type)
            {
                case BladeEventType.Touch:
                    Report(bladeEvent, ContactKind.PointFirst);
                    _dummy.FlashZone(bladeEvent.Zone);
                    // A pressed épée bows up from the grip: the drawn point bows toward the mount's up.
                    _flex.Kick(worldBlade, _mount.up.ToNumerics());
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
            _flex.Reset();
        }

        private static bool IsJump(TrackedPose before, TrackedPose after)
        {
            var moved = System.Numerics.Vector3.Distance(before.Position, after.Position);
            var turned = Quaternion.Angle(before.Rotation.ToUnity(), after.Rotation.ToUnity());
            return moved > JumpMeters || turned > JumpDegrees;
        }
    }
}
