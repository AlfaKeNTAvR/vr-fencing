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
    public sealed partial class BladeBehaviour : MonoBehaviour
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
        private ArmedDummyPrototype _armedDummy;
        private PartnerBlade _partnerBlade;
        private IPartnerBladeSource _partnerSource;
        private TrackedPose? _lastEnGardeLine;
        private float? _lastDummyDistanceMeters;
        private float _lastFlatHapticAt = float.NegativeInfinity;
        private float _lastPressHapticAt = float.NegativeInfinity;
        // The blade starts drawn; the first untracked frame hides it.
        private bool _isTracked = true;

        private IReadOnlyList<BladeObstacle> _extraObstacles = System.Array.Empty<BladeObstacle>();

        /// <summary>Raised for every Touch, glance and flat contact, with positions in the Strip frame.</summary>
        public event System.Action<BladeEvent> Contact;

        /// <summary>
        /// The blade a bout judges this Fencer's Touches with this frame, in the Strip frame (PER-145): the hand's chord
        /// as the contact with the partner's blade leaves it (turned, bowed, slid off), as drawn before any body bends
        /// it; the hand's chord with no partner blade. Null while the controller is not tracked.
        /// </summary>
        public BladeSegment? JudgedBladeOnStrip { get; private set; }

        /// <summary>
        /// This headset's own view of the contact with the partner's blade (PER-145), sent with the hand so the partner
        /// can log where their views disagree; null with no partner blade.
        /// </summary>
        public VrFencing.Partner.BladeContactReport? PartnerContactReport => _partnerBlade?.Report;

        /// <summary>
        /// The partner view that hands this Blade the partner's blade each frame (PER-145), or null for none. It must
        /// sample the partner before this behaviour's frame (it runs earlier) and draws their blade when called back.
        /// </summary>
        public void SetPartnerBladeSource(IPartnerBladeSource source)
        {
            _partnerSource = source;
        }

        /// <summary>
        /// More bodies the drawn blade cannot pass besides the Dummy and the floor, in the Strip frame: the opponent's
        /// body (PER-81). Visual only, never Touches. Keep the count fixed while shown (the drawn blade remembers its pin
        /// by its place in the list); a new count restarts the drawn blade. Empty for none.
        /// </summary>
        public void SetExtraObstacles(IReadOnlyList<BladeObstacle> obstacles)
        {
            var next = obstacles ?? System.Array.Empty<BladeObstacle>();
            if (next.Count != _extraObstacles.Count)
            {
                RestartMotion();
            }

            _extraObstacles = next;
        }

        /// <summary>Takes changed Settings: sword hand, Touch rule, mount and angles, haptics.</summary>
        public void ApplySettings(TrainerSettings settings)
        {
            _settings = settings.Clone();
            _rules = TouchRules.For(_settings.Blade.TouchRule, _settings.Blade.TouchPressure);
            _drawnBlade.Flex.SetSoftness(_settings.Blade.Flex);
            _armedDummy?.ApplySettings(_settings);
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
            if (_dummy != null && _drillRunner != null && _drillRunner.Runner != null)
            {
                // The Armed Dummy (PER-95): live in the Drills that arm it and in Free Practice outside any Drill (PER-123).
                var drillRunner = _drillRunner;
                _armedDummy = new ArmedDummyPrototype(_dummy, drillRunner.Runner, () => drillRunner.FreePracticeNow, _haptics, _cameraRig);
            }

            // A partner's blade (PER-145), live while a Partner session or "Mirror me" hands one in.
            _partnerBlade = new PartnerBlade(_haptics);

            ApplySettings(SettingsFile.Load());
        }

        private void OnDestroy()
        {
            _armedDummy?.Dispose();
            _partnerBlade?.Dispose();
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

            var now = Time.timeAsDouble;
            var hasDummy = _dummy != null && _strip != null && _strip.EnGardeLine != null;

            // The Dummy is hidden while in a Partner session (PER-80): nothing to touch or bend on then. A partner's blade
            // (PER-145) is sampled by the partner view before this frame, in its Strip frame.
            var isDummyShown = hasDummy && _dummy.gameObject.activeInHierarchy;
            System.Collections.Generic.IReadOnlyList<BladeObstacle> partnerObstacles = null;
            var partner = default(HeldBlade);
            var isPartnerShown = !isDummyShown && _partnerSource != null && _partnerSource.TryGetPartnerBlade(out partner, out partnerObstacles);

            // Everything runs in the Strip frame, where the Dummy's capsules are. Without a Strip the room frame stands in.
            // Against a partner, the frame the partner is drawn in (the placed Strip's).
            var stripInRoom = isPartnerShown
                ? _partnerSource.StripInRoom
                : hasDummy
                    ? new TrackedPose(_strip.EnGardeLine.position.ToNumerics(), _strip.EnGardeLine.rotation.ToNumerics())
                    : new TrackedPose(System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity);
            if (!isPartnerShown)
            {
                _partnerBlade?.Stop();
            }

            if (!isTracked)
            {
                JudgedBladeOnStrip = null;
                if (isPartnerShown && _partnerBlade != null)
                {
                    // No blade of this Fencer's: the partner's is drawn as their hand holds it.
                    _partnerBlade.StepAlone(partner, partnerObstacles, Time.deltaTime);
                    _partnerSource.DrawPartnerBlade(_partnerBlade.Contact, stripInRoom);
                }

                return;
            }
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
            _handBladeOnStrip = bladeInStrip;
            if (hasDummy)
            {
                RestartOnJump(stripInRoom);
            }

            if (isDummyShown)
            {
                // The Armed Dummy poses its arm first, so the Touch test sees this frame's arm zones; it also solves the
                // two blades' contact (PER-122).
                _armedDummy?.Step(now, Time.deltaTime, bladeInStrip, droop, stripInRoom);
                DetectContacts(now, bladeInStrip);
            }
            else
            {
                // A hidden Dummy (Partner session) or no Strip: no blade either, even in Free Practice (PER-123).
                _armedDummy?.Disarm();
            }

            // Against the Dummy's blade the drawn blade turns in the grip and bows as the contact solver gives it; the
            // hilt stays in the hand (PER-122).
            var give = new BladeGive(bladeInStrip, droop, 0.0f, 0.0f);
            if (isDummyShown && _armedDummy != null && _armedDummy.TryGetFencerGive(out var fencerGive))
            {
                give = fencerGive;
                if (_armedDummy.IsBladeNear)
                {
                    // Near the Dummy's blade it does not whip, so a whip never carries it through: drawn as the contact leaves it.
                    _drawnBlade.Flex.Reset();
                }
            }
            else if (isPartnerShown && _partnerBlade != null)
            {
                // Against the partner's blade the same way (PER-145): one solve here draws both blades, the partner being
                // just another Fencer; haptics and the scrape come from this solve only.
                var mine = new HeldBlade(bladeInStrip, droop, gripUp, _settings.Blade.Flex);
                _partnerBlade.Step(now, Time.deltaTime, mine, partner, partnerObstacles, stripInRoom, _settings);
                give = _partnerBlade.Contact.MyGive;
                if (_partnerBlade.IsBladeNear)
                {
                    _drawnBlade.Flex.Reset();
                }
            }

            // A bout judges this Fencer's point on the blade as the contact leaves it (PER-145).
            JudgedBladeOnStrip = give.Chord;

            var wasDecoupled = _drawnBlade.IsDecoupled;
            var wasOnSide = _drawnBlade.IsOnSide;
            var release = _drawnBlade.Step(give.Chord, GatherObstacles(isDummyShown), gripUp, Time.deltaTime, give.Droop);
            if (release != PinRelease.None)
            {
                Debug.Log($"[Blade] Point released: {release}");
            }

            if (_drawnBlade.HasPopped)
            {
                // Only a body, the floor or a bell holds the drawn blade now (PER-122): the leash brings it back, silently.
                Debug.Log("[Blade] Leash: the drawn blade popped back to the hand");
            }

            if (_drawnBlade.IsOnSide != wasOnSide)
            {
                Debug.Log(_drawnBlade.IsOnSide
                    ? $"[Blade] Side pressed on the body {_drawnBlade.Side.ContactMeters * 100.0f:0} cm from the guard: the drawn blade bends round it"
                    : "[Blade] Side let go of the body: the drawn blade springs back");
            }

            if (_drawnBlade.IsDecoupled != wasDecoupled)
            {
                Debug.Log(_drawnBlade.IsDecoupled
                    ? $"[Blade] Bent as far as it goes on a body (push {_drawnBlade.Contact.PushMeters * 100.0f:0} cm, pinned {_drawnBlade.Contact.IsPinned}): the drawn blade holds and lags the hand"
                    : "[Blade] Hand back within reach: the drawn blade follows it again");
            }

            PlayPressHaptic((float)now);
            _visual.Draw(_drawnBlade, stripInRoom, (float)now);
            if (isPartnerShown && _partnerBlade != null)
            {
                _partnerSource.DrawPartnerBlade(_partnerBlade.Contact, stripInRoom);
            }
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
                    _obstacles.Add(BladeObstacle.FromCapsule(capsule));
                }
            }

            // The Armed Dummy's blade and its hilt (bell guard and grip) are the contact solver's (PER-122), not obstacles.
            _obstacles.Add(BladeObstacle.Floor);

            // The opponent's body (PER-81), last, so its place in the list stays put.
            foreach (var obstacle in _extraObstacles)
            {
                _obstacles.Add(obstacle);
            }

            return _obstacles;
        }

        /// <summary>
        /// While the blades press on each other, the bind buzz (PER-122), a steady low hum that grows with the press; else
        /// while the point is pinned, a buzz that grows with the push (full while a body holds the drawn blade off the hand).
        /// </summary>
        private void PlayPressHaptic(float now)
        {
            var pressMeters = Mathf.Max(_armedDummy?.PressMeters ?? 0.0f, _partnerBlade?.PressMeters ?? 0.0f);
            if (pressMeters > 0.0f)
            {
                if (now - _lastPressHapticAt < BladeHaptics.BindPulseIntervalSeconds)
                {
                    return;
                }

                _lastPressHapticAt = now;
                _haptics.PlayBind(BladeHaptics.GetBindStrength(pressMeters), _settings.Hand);
                return;
            }

            if (!_drawnBlade.Contact.IsPinned || now - _lastPressHapticAt < BladeHaptics.PressPulseIntervalSeconds)
            {
                return;
            }

            _lastPressHapticAt = now;
            var strength = BladeHaptics.GetPressStrength(
                _drawnBlade.Contact.PushMeters, _drawnBlade.IsDecoupled, _drawnBlade.Contact.IsPinnedOnPlane, _settings.Blade.FloorBuzz);
            if (strength > 0.0f)
            {
                _haptics.PlayPress(strength, _settings.Hand);
            }
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
                    break;
                case BladeEventType.FlatContact:
                    Report(bladeEvent, ContactKind.Flat);
                    break;
            }

            PlayFeedback(bladeEvent, now);
            Contact?.Invoke(bladeEvent);
        }

        /// <summary>
        /// A contact of this blade with the opponent's body in a bout (PER-82), judged by the bout: the same haptic and
        /// point flash as on the Dummy, never reported to the Drill runner.
        /// </summary>
        public void PlayOpponentContact(BladeEvent bladeEvent)
        {
            if (_visual != null)
            {
                PlayFeedback(bladeEvent, Time.time);
            }
        }

        /// <summary>The opponent's Touch lit in a bout (PER-82): a distinct short buzz on both controllers.</summary>
        public void PlayBeingTouched()
        {
            _haptics?.PlayBeingTouched();
        }

        /// <summary>This Fencer's Touch was ruled Parried in a bout (PER-145): two soft knocks on the sword hand.</summary>
        public void PlayMyTouchParried()
        {
            if (_settings != null)
            {
                _haptics?.PlayParried(_settings.Hand);
            }
        }

        /// <summary>The event's haptic (a flat contact at most once per cooldown) and the point's flash.</summary>
        private void PlayFeedback(BladeEvent bladeEvent, float now)
        {
            if (bladeEvent.Type != BladeEventType.FlatContact)
            {
                PlayHaptic(bladeEvent);
            }
            else if (now - _lastFlatHapticAt >= FlatHapticCooldownSeconds)
            {
                PlayHaptic(bladeEvent);
                _lastFlatHapticAt = now;
            }

            _visual.Flash(bladeEvent.Type, now);
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
            _armedDummy?.Restart();
            _partnerBlade?.Restart();
        }

        private static bool IsJump(TrackedPose before, TrackedPose after)
        {
            var moved = System.Numerics.Vector3.Distance(before.Position, after.Position);
            var turned = Quaternion.Angle(before.Rotation.ToUnity(), after.Rotation.ToUnity());
            return moved > JumpMeters || turned > JumpDegrees;
        }
    }
}
