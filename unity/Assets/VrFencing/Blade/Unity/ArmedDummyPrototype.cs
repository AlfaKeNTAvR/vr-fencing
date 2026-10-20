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
    /// The Armed Dummy (PER-95 prototype), live only while a Drill that uses it runs
    /// (<see cref="DrillDefinition.UsesArmedDummy"/>; today the development Drill
    /// <see cref="DrillCatalog.ArmedDummyTestDrillId"/>), so the MVP Drills never see it. The Dummy holds an épée
    /// drawn and bent like the Fencer's (<see cref="DrawnBlade"/>, <see cref="BladeVisual"/>). On every Rep's cue it
    /// plans one straight thrust (<see cref="ArmedDummyAttack"/>) at the Fencer's hand, forearm, chest or thigh, at slow,
    /// medium and real speed in turn. When the Fencer's blade meets its blade (<see cref="BladeClashDetector"/>) a spark
    /// shows at the contact, the clash buzz plays on the sword hand, and its blade swings away and recovers
    /// (<see cref="BladeSwing"/>). The two blades are solid against each other (<see cref="ArmedDummyBlade"/>,
    /// PER-104): the Fencer's drawn blade bends round the Dummy's, and the Dummy's gives way to a press and springs back.
    /// A contact during the thrust or its hold is judged (<see cref="ParryJudge"/>, PER-97): once it has turned the point
    /// off the Fencer's body the thrust is cut short and recovers, and the runner gets the Parry; a thrust that reaches
    /// the end of its hold unparried is reported as landed.
    /// All rules are in the Core; this only poses, draws, plays and forwards.
    /// </summary>
    internal sealed class ArmedDummyPrototype : IDisposable
    {
        private const string MaterialResourcePath = "VrFencingBlade";
        private const float SparkDiameterMeters = 0.03f;
        private const float SparkSeconds = 0.12f;

        private static readonly Color SparkColor = new Color(1.0f, 0.95f, 0.5f);
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private readonly DummyBehaviour _dummy;
        private readonly DrillRunner _runner;
        private readonly BladeHapticPlayer _haptics;
        private readonly Random _random = new Random();
        private readonly BladeClashDetector _detector = new BladeClashDetector();
        private readonly ArmedDummyBlade _blade = new ArmedDummyBlade();
        private readonly Transform _mount;
        private readonly BladeVisual _visual;
        private readonly Renderer _spark;
        private TrainerSettings _settings = new TrainerSettings();
        private ArmedDummyAttack _attack;
        private ParryJudge _judge;
        private BladeClash? _lastClash;
        private bool _hasAttackClashed;
        private bool _hasReportedLanding;
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
            _blade.Drawn.Flex.SetSoftness(_settings.Blade.Flex);
        }

        /// <summary>The Strip jumped or the blade lost tracking: no sweep across the gap.</summary>
        public void Restart()
        {
            _detector.Reset();
            _blade.Reset();
            _judge?.Reset();
            _lastClash = null;
            _lastFencerBlade = null;
        }

        /// <summary>
        /// One frame, before the Fencer's drawn blade moves: poses the arm, moves the blade (pushed aside by the Fencer's
        /// drawn blade as drawn last frame), tests the clash, draws. Everything in the Strip frame.
        /// </summary>
        public void Step(double nowSeconds, float deltaSeconds, BladeSegment fencerBlade, TrackedPose stripInRoom, DrawnBlade fencerDrawn)
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
            var wasPressed = _blade.IsPressed;
            _blade.Step(forte, gripUp, -System.Numerics.Vector3.UnitY, _settings.Blade, fencerBlade, fencerDrawn, deltaSeconds);
            if (_blade.IsPressed != wasPressed)
            {
                Debug.Log(_blade.IsPressed
                    ? "[ArmedDummy] Blades pressed: the Dummy's blade gives way"
                    : "[ArmedDummy] Blades let go: the Dummy's blade springs back");
            }

            var clash = _detector.Step(nowSeconds, fencerBlade, _blade.Chord);
            if (clash.HasValue)
            {
                OnClash(clash.Value, nowSeconds, stripInRoom);
            }

            JudgeParry(nowSeconds, clash, fencerBlade);
            _visual.Draw(_blade.Drawn, stripInRoom, (float)nowSeconds);
            if (_spark != null && Time.time >= _sparkEndsAt && _spark.enabled)
            {
                _spark.enabled = false;
            }
        }

        /// <summary>Adds the Dummy's blade, as drawn this frame, to the Fencer's drawn blade's obstacles while armed.</summary>
        public void AddBladeObstacles(System.Collections.Generic.ICollection<BladeObstacle> obstacles)
        {
            if (_isActive)
            {
                _blade.AddObstacles(obstacles);
            }
        }

        /// <summary>After the Fencer's drawn blade moved this frame: which side of the Dummy's blade it is on.</summary>
        public void UpdateSide(BladeSegment fencerBlade, DrawnBlade fencerDrawn)
        {
            if (_isActive)
            {
                _blade.UpdateSide(fencerBlade, fencerDrawn);
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
            return _runner.IsRunning && !_runner.IsPaused && _runner.Drill != null && _runner.Drill.UsesArmedDummy;
        }

        /// <summary>
        /// Judges this frame's contact against the attack under way (thrust or hold, not yet parried), forwards a Parry
        /// to the runner and cuts the thrust short, and reports a thrust that got through its hold as landed.
        /// </summary>
        private void JudgeParry(double nowSeconds, BladeClash? clash, BladeSegment fencerBlade)
        {
            if (_attack == null || _judge == null || _attack.IsParried)
            {
                return;
            }

            var phase = _attack.GetPhase(nowSeconds);
            if (phase == AttackPhase.Recover || phase == AttackPhase.Done)
            {
                if (!_hasReportedLanding)
                {
                    _hasReportedLanding = true;
                    Debug.Log("[ArmedDummy] Thrust landed: not parried.");
                    _runner.ReportAttackLanded();
                }

                return;
            }

            if (phase != AttackPhase.Thrust && phase != AttackPhase.Hold)
            {
                return;
            }

            var isInContact = _detector.IsInContact || _blade.IsPressed;
            if (clash.HasValue)
            {
                _hasAttackClashed = true;
                _judge.ReportClash(clash.Value, fencerBlade);
            }
            else if (!_hasAttackClashed && isInContact && _lastClash.HasValue)
            {
                // The Fencer's blade was already on the Dummy's when the thrust began.
                _hasAttackClashed = true;
                _judge.ReportHeldContact(_lastClash.Value, nowSeconds, fencerBlade);
            }

            if (!_judge.IsWatching)
            {
                return;
            }

            var line = AttackLine.OnStrip(_attack.Path, _dummy.DistanceMeters, nowSeconds);
            var body = AttackAim.GetFencerBodyOnStrip(_dummy.Layout, fencerBlade.GuardPosition);
            var parry = _judge.Step(nowSeconds, _blade.Swing.TipOffset, isInContact, line, body);
            if (!parry.HasValue)
            {
                return;
            }

            _attack.ReportParried(nowSeconds);
            var report = parry.Value;
            Debug.Log($"[ArmedDummy] Parry {(int)report.Line} during the {phase}: point off the body by {report.DeflectionMeters * 100.0f:0} cm, " +
                $"Fencer blade at {report.FencerBladeFraction:0.00}, Dummy blade at {report.DummyBladeFraction:0.00}, " +
                $"{(report.IsForteOnFoible ? "forte on foible" : "not forte on foible")}, rule {_settings.Blade.ParryRule}: " +
                (report.CountsAsParry ? "counts." : "does not count."));
            _runner.ReportParry(report);
        }

        private void SetActive(bool isActive)
        {
            _isActive = isActive;
            _attack = null;
            _judge = null;
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

            var layout = _dummy.Layout;
            var speed = (AttackSpeed)(cue.RepIndex % 3);
            var target = AttackAim.DrawTarget(_random);
            var delay = ArmedDummyAttack.DrawDelaySeconds(_random);
            var aim = AttackAim.GetAimInDummyFrame(
                target, layout, _dummy.DistanceMeters, _lastFencerBlade?.GuardPosition, _lastFencerBlade?.Direction ?? System.Numerics.Vector3.UnitZ);
            _attack = ArmedDummyAttack.Plan(Time.timeAsDouble, delay, layout.HeightMeters, layout.Hand, aim, speed, BladePose.BladeLengthMeters);
            _judge = new ParryJudge(_settings.Blade.ParryRule, _settings.Hand);
            _hasAttackClashed = false;
            _hasReportedLanding = false;
            var missMeters = System.Numerics.Vector3.Distance(_attack.Path.AimPoint, aim);
            Debug.Log($"[ArmedDummy] Rep {cue.RepIndex + 1}: thrust at the {target}, {speed} ({ArmedDummyAttack.GetPeakPointSpeed(speed):0.0#} m/s), " +
                $"after {delay:0.0} s, thrust {_attack.ThrustSeconds * 1000.0f:0} ms over {_attack.Path.LengthMeters * 100.0f:0} cm" +
                (missMeters > 0.01f ? $", lands {missMeters * 100.0f:0} cm off the aim (out of the arm's reach)." : "."));
        }

        private void OnClash(BladeClash clash, double nowSeconds, TrackedPose stripInRoom)
        {
            _blade.Swing.Kick(clash);
            _lastClash = clash;
            var phase = _attack?.GetPhase(nowSeconds);
            _haptics?.PlayClash(clash.HapticStrength, _settings.Hand);
            ShowSpark(StripFrame.ToRoom(stripInRoom, clash.ContactPoint).ToUnity());
            Debug.Log($"[ArmedDummy] Clash {clash.RelativeSpeedMetersPerSecond:0.00} m/s, Fencer blade at {clash.FencerBladeFraction:0.00}, " +
                $"Dummy blade at {clash.OpponentBladeFraction:0.00}, swing {BladeSwing.GetPeakMeters(clash.RelativeSpeedMetersPerSecond, clash.OpponentBladeFraction) * 100.0f:0} cm, " +
                $"buzz {clash.HapticStrength:0.00}, during {phase?.ToString() ?? "guard"}");
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
