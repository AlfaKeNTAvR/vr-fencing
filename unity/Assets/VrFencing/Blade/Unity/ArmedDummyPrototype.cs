using System;
using UnityEngine;
using VrFencing.Drills;
using VrFencing.Dummy;
using VrFencing.Settings;
using VrFencing.Strip;
using Random = System.Random;

namespace VrFencing.Blade
{
    /// <summary>
    /// The Armed Dummy feel-test prototype (PER-95), live only while the development Drill
    /// <see cref="DrillCatalog.ArmedDummyTestDrillId"/> runs, so the MVP Drills never see it. The Dummy holds an épée
    /// drawn and bent like the Fencer's (<see cref="DrawnBlade"/>, <see cref="BladeVisual"/>). On every Rep's cue it
    /// plans one straight thrust (<see cref="ArmedDummyAttack"/>) at the Fencer's hand, forearm, chest or thigh, at slow,
    /// medium and real speed in turn. When the Fencer's blade meets its blade (<see cref="BladeClashDetector"/>) a spark
    /// shows at the contact, the clash buzz plays on the sword hand, its blade swings away and recovers
    /// (<see cref="BladeSwing"/>), and a thrust under way is cut short and recovers. Nothing is judged yet (PER-97).
    /// All rules are in the Core; this only poses, draws and plays.
    /// </summary>
    internal sealed class ArmedDummyPrototype : IDisposable
    {
        private const string MaterialResourcePath = "VrFencingBlade";
        private const float SparkDiameterMeters = 0.03f;
        private const float SparkSeconds = 0.12f;

        // Hand and forearm aims sit behind the Fencer's guard, along their blade: the hand just behind it, the forearm
        // a forearm's middle further back.
        private const float HandBehindGuardMeters = 0.06f;
        private const float ForearmBehindGuardMeters = 0.2f;

        private static readonly Color SparkColor = new Color(1.0f, 0.95f, 0.5f);
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly BladeObstacle[] FloorOnly = { BladeObstacle.Floor };

        private enum AttackTarget
        {
            Hand,
            Forearm,
            Chest,
            Thigh,
        }

        private readonly DummyBehaviour _dummy;
        private readonly DrillRunner _runner;
        private readonly BladeHapticPlayer _haptics;
        private readonly Random _random = new Random();
        private readonly BladeClashDetector _detector = new BladeClashDetector();
        private readonly BladeSwing _swing = new BladeSwing();
        private readonly DrawnBlade _drawnBlade = new DrawnBlade();
        private readonly Transform _mount;
        private readonly BladeVisual _visual;
        private readonly Renderer _spark;
        private TrainerSettings _settings = new TrainerSettings();
        private ArmedDummyAttack _attack;
        private BladeSegment? _lastFencerBlade;
        private bool _isActive;
        private float _sparkEndsAt = float.NegativeInfinity;

        public ArmedDummyPrototype(DummyBehaviour dummy, DrillRunner runner, BladeHapticPlayer haptics)
        {
            _dummy = dummy ?? throw new ArgumentNullException(nameof(dummy));
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _haptics = haptics;

            _mount = new GameObject("Armed Dummy Blade").transform;
            _visual = new BladeVisual(_mount);
            _visual.SetVisible(false);
            _spark = CreateSpark();
            _runner.RepCued += OnRepCued;
        }

        public void ApplySettings(TrainerSettings settings)
        {
            _settings = settings.Clone();
            _drawnBlade.Flex.SetSoftness(_settings.Blade.Flex);
        }

        /// <summary>The Strip jumped or the blade lost tracking: no sweep across the gap.</summary>
        public void Restart()
        {
            _detector.Reset();
            _drawnBlade.Reset();
            _lastFencerBlade = null;
        }

        /// <summary>One frame: poses the arm, tests the blades, draws. Everything in the Strip frame.</summary>
        public void Step(double nowSeconds, float deltaSeconds, BladeSegment fencerBlade, TrackedPose stripInRoom)
        {
            _lastFencerBlade = fencerBlade;
            var isActive = IsTestDrillRunning() && _dummy.Layout != null;
            if (isActive != _isActive)
            {
                SetActive(isActive);
            }

            if (!isActive)
            {
                return;
            }

            _dummy.SetArmPose(_attack?.GetPose(nowSeconds) ?? SwordArmReach.PoseEnGarde(_dummy.Layout.HeightMeters, BladePose.BladeLengthMeters));
            var (forte, gripUp) = GetDummyBladeOnStrip();
            _swing.Step(deltaSeconds);
            var swung = _swing.Apply(forte);
            var droop = BladeDroop.Create(swung.Direction, gripUp, -System.Numerics.Vector3.UnitY, _settings.Blade);
            var chord = droop.GetChord(swung);

            var clash = _detector.Step(nowSeconds, fencerBlade, chord);
            if (clash.HasValue)
            {
                OnClash(clash.Value, nowSeconds, stripInRoom);
            }

            _drawnBlade.Step(chord, FloorOnly, gripUp, deltaSeconds, droop);
            _visual.Draw(_drawnBlade, stripInRoom, (float)nowSeconds);
            if (_spark != null && Time.time >= _sparkEndsAt && _spark.enabled)
            {
                _spark.enabled = false;
            }
        }

        public void Dispose()
        {
            _runner.RepCued -= OnRepCued;
            _visual.Dispose();
            if (_spark != null)
            {
                UnityEngine.Object.Destroy(_spark.gameObject);
            }
        }

        private bool IsTestDrillRunning()
        {
            return _runner.IsRunning && !_runner.IsPaused && _runner.Drill?.Id == DrillCatalog.ArmedDummyTestDrillId;
        }

        private void SetActive(bool isActive)
        {
            _isActive = isActive;
            _attack = null;
            _swing.Reset();
            Restart();
            _visual.SetVisible(isActive);
            if (!isActive && _dummy.Layout != null)
            {
                _dummy.SetArmPose(SwordArmPose.ArmInLine);
            }

            Debug.Log(isActive ? "[ArmedDummy] Armed: the Dummy holds a blade." : "[ArmedDummy] Disarmed.");
        }

        private void OnRepCued(RepCue cue)
        {
            if (!IsTestDrillRunning() || _dummy.Layout == null)
            {
                return;
            }

            var speed = (AttackSpeed)(cue.RepIndex % 3);
            var target = (AttackTarget)_random.Next(4);
            var delay = ArmedDummyAttack.DrawDelaySeconds(_random);
            var aim = GetAimInRightHandedDummyFrame(target);
            _attack = ArmedDummyAttack.Plan(Time.timeAsDouble, delay, _dummy.Layout.HeightMeters, aim, speed, BladePose.BladeLengthMeters);
            Debug.Log($"[ArmedDummy] Rep {cue.RepIndex + 1}: thrust at the {target}, {speed} ({ArmedDummyAttack.GetPeakPointSpeed(speed):0.0#} m/s), " +
                $"after {delay:0.0} s, thrust {_attack.ThrustSeconds * 1000.0f:0} ms.");
        }

        private void OnClash(BladeClash clash, double nowSeconds, TrackedPose stripInRoom)
        {
            _swing.Kick(clash);
            var phase = _attack?.GetPhase(nowSeconds);
            var isParried = _attack != null && _attack.ReportParried(nowSeconds);
            _haptics?.PlayClash(clash.HapticStrength, _settings.Hand);
            ShowSpark(StripFrame.ToRoom(stripInRoom, clash.ContactPoint).ToUnity());
            Debug.Log($"[ArmedDummy] Clash {clash.RelativeSpeedMetersPerSecond:0.00} m/s, Fencer blade at {clash.FencerBladeFraction:0.00}, " +
                $"Dummy blade at {clash.OpponentBladeFraction:0.00}, swing {BladeSwing.GetPeakMeters(clash.RelativeSpeedMetersPerSecond, clash.OpponentBladeFraction) * 100.0f:0} cm, " +
                $"buzz {clash.HapticStrength:0.00}, during {phase?.ToString() ?? "guard"}{(isParried ? ": thrust parried" : string.Empty)}");
        }

        /// <summary>The Dummy's straight blade (guard to the undrooped point) and its grip's up, in the Strip frame.</summary>
        private (BladeSegment Forte, System.Numerics.Vector3 GripUp) GetDummyBladeOnStrip()
        {
            var arm = _dummy.Layout.Arm;
            var distance = _dummy.DistanceMeters;
            var guard = DummyLayout.DummyToStrip(arm.GetGuardPosition(), distance);
            var point = DummyLayout.DummyToStrip(arm.GetBladePoint(BladePose.BladeLengthMeters), distance);
            var wristUp = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitY, arm.GetPose(ArmJoint.Wrist).Rotation);
            return (new BladeSegment(guard, point), DirectionToStrip(wristUp));
        }

        /// <summary>
        /// The aim in the right-handed Dummy frame. The Fencer is taken to stand en garde at the En Garde Line in the
        /// Dummy's own stance and size (the Dummy is the Fencer's height), so chest and thigh are its body and thigh zones
        /// as seen from the Strip frame; hand and forearm follow the Fencer's live blade.
        /// </summary>
        private System.Numerics.Vector3 GetAimInRightHandedDummyFrame(AttackTarget target)
        {
            var layout = _dummy.Layout;
            System.Numerics.Vector3 aimOnStrip;
            switch (target)
            {
                case AttackTarget.Hand when _lastFencerBlade.HasValue:
                    aimOnStrip = _lastFencerBlade.Value.GuardPosition - (_lastFencerBlade.Value.Direction * HandBehindGuardMeters);
                    break;
                case AttackTarget.Forearm when _lastFencerBlade.HasValue:
                    aimOnStrip = _lastFencerBlade.Value.GuardPosition - (_lastFencerBlade.Value.Direction * ForearmBehindGuardMeters);
                    break;
                case AttackTarget.Thigh:
                    aimOnStrip = FrontOf(layout.GetZone(TargetZone.Thigh));
                    break;
                default:
                    aimOnStrip = FrontOf(layout.GetZone(TargetZone.Body));
                    break;
            }

            // The Dummy frame is the Strip frame turned half a turn about the vertical and moved along the Strip.
            var aim = DummyLayout.DummyToStrip(aimOnStrip, _dummy.DistanceMeters);
            return layout.Hand == Hand.Left ? HandMirroring.MirrorPosition(aim) : aim;
        }

        /// <summary>The front of a Fencer zone, toward the Dummy (+z in the Strip frame).</summary>
        private static System.Numerics.Vector3 FrontOf(Capsule zone)
        {
            return zone.Center + new System.Numerics.Vector3(0.0f, 0.0f, zone.RadiusMeters);
        }

        private static System.Numerics.Vector3 DirectionToStrip(System.Numerics.Vector3 directionInDummyFrame)
        {
            return new System.Numerics.Vector3(-directionInDummyFrame.X, directionInDummyFrame.Y, -directionInDummyFrame.Z);
        }

        private void ShowSpark(Vector3 worldPosition)
        {
            if (_spark == null)
            {
                return;
            }

            _spark.transform.position = worldPosition;
            _spark.enabled = true;
            _sparkEndsAt = Time.time + SparkSeconds;
        }

        private static Renderer CreateSpark()
        {
            var material = Resources.Load<Material>(MaterialResourcePath);
            if (material == null)
            {
                Debug.LogError($"[ArmedDummy] Material 'Resources/{MaterialResourcePath}' is missing; the clash spark is not drawn.");
                return null;
            }

            var spark = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            spark.name = "Armed Dummy Clash Spark";
            UnityEngine.Object.Destroy(spark.GetComponent<Collider>());
            spark.transform.localScale = Vector3.one * SparkDiameterMeters;
            var sparkRenderer = spark.GetComponent<MeshRenderer>();
            sparkRenderer.sharedMaterial = material;
            sparkRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sparkRenderer.receiveShadows = false;
            var propertyBlock = new MaterialPropertyBlock();
            propertyBlock.SetColor(BaseColorId, SparkColor);
            sparkRenderer.SetPropertyBlock(propertyBlock);
            sparkRenderer.enabled = false;
            return sparkRenderer;
        }
    }
}
