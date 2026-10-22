using System;
using UnityEngine;
using VrFencing.Drills;
using VrFencing.Dummy;
using VrFencing.Partner;
using VrFencing.Settings;
using VrFencing.Strip;
using Random = System.Random;

namespace VrFencing.Blade
{
    /// <summary>
    /// The Armed Dummy (PER-95 prototype), live only while a Drill that uses it runs
    /// (<see cref="DrillDefinition.UsesArmedDummy"/>: the Parry Drills 7 to 9, PER-100, and the development Drill
    /// <see cref="DrillCatalog.ArmedDummyTestDrillId"/>), and in every other Drill too while the "Dummy blade in every
    /// Drill" Setting is on (<see cref="DrillDefinition.HoldsDummyBlade"/>, PER-117): there it only holds its en garde
    /// line, since no cue names an attack, and its blade is as solid as in the attack Drills. The Dummy holds an épée
    /// drawn and bent like the Fencer's (<see cref="DrawnBlade"/>, <see cref="BladeVisual"/>). On every Rep's cue it
    /// plans one straight thrust (<see cref="ArmedDummyAttack"/>) at the target and speed the cue names
    /// (<see cref="RepCue.AttackTarget"/>, <see cref="RepCue.AttackSpeed"/>, from the Drill's <see cref="AttackScript"/>),
    /// standing further back for a thrust at the arm (<see cref="RepCue.DummyStandBackMeters"/>). When the Fencer's blade meets its blade (<see cref="BladeClashDetector"/>) a spark
    /// shows at the contact and moves with the Dummy's blade, the clash buzz plays on the sword hand, and its point is
    /// knocked off line by how hard and where the blades met, a big knock taking the whole arm and turning the torso a
    /// little, held open, then recovered (<see cref="BladeDeflection"/>, PER-116); a knocked Dummy waits until its arm
    /// is back in line before winding up its next attack. The two blades never pass through each other
    /// (<see cref="ArmedDummyBlade"/>, <see cref="BladeContactSolver"/>, PER-122): each turns in its grip and bows, the
    /// Dummy's hand gives way to a press and comes back when let go, and a steel scrape and the bind buzz play while
    /// they press.
    /// A contact during the thrust or its hold is judged (<see cref="ParryJudge"/>, PER-97): once it has turned the point
    /// off the Fencer's body the thrust is cut short and recovers, and the runner gets the Parry. Unparried, its point is
    /// judged against the Fencer's body each frame (<see cref="FencerTarget"/>, PER-98: IOBT when confident, else
    /// capsules from the headset plus the sword arm): when it lands, the thrust stops there, a red flash and the hit buzz
    /// play, and the runner gets the hit with the body that judged it. A thrust that ends its hold without reaching the
    /// Fencer is reported too, with no hit.
    /// Outside any Drill it is armed for Free Practice (PER-123, <see cref="FreePractice"/>), whatever the "Dummy blade
    /// in every Drill" Setting says: it holds its en garde line, or, with <see cref="FreePracticeBehavior.Attacks"/>, thrusts
    /// now and then from the test Drill's script (<see cref="FreeAttackSchedule"/>), judged the same way (the clash, the
    /// Parry, the red flash and hit buzz) but never reported to the runner: no Reps, no score.
    /// All rules are in the Core; this only poses, draws, plays and forwards.
    /// </summary>
    internal sealed partial class ArmedDummyPrototype : IDisposable
    {
        // Why the Dummy holds a blade now: a Drill's run, Free Practice holding its line, Free Practice attacking, or not at all.
        private enum ArmedMode
        {
            Unarmed,
            Drill,
            FreeGuard,
            FreeAttacks,
        }

        private const string MaterialResourcePath = "VrFencingBlade";
        private const float SparkDiameterMeters = 0.03f;
        private const float SparkSeconds = 0.12f;

        private static readonly Color SparkColor = new Color(1.0f, 0.95f, 0.5f);
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private readonly DummyBehaviour _dummy;
        private readonly DrillRunner _runner;
        private readonly Func<FreePracticeBehavior?> _getFreePractice;
        private readonly FreeAttackSchedule _freeAttacks;
        private readonly BladeHapticPlayer _haptics;
        private readonly Random _random = new Random();
        private readonly BladeClashDetector _detector = new BladeClashDetector();
        private readonly ClashBuzzGate _clashBuzzGate = new ClashBuzzGate();
        private readonly ArmedDummyBlade _blade = new ArmedDummyBlade();
        private readonly Transform _mount;
        private readonly BladeVisual _visual;
        private readonly Renderer _spark;
        private readonly BladeScrapePlayer _scrape;
        private readonly FencerBodyReader _bodyReader;
        private readonly FencerTarget _fencer = new FencerTarget();
        private readonly HitFlash _hitFlash;
        private TrainerSettings _settings = new TrainerSettings();
        private System.Numerics.Vector3? _lastDummyPoint;
        private FencerHitSource? _lastLoggedSource;
        private ArmedDummyAttack _attack;
        private ParryJudge _judge;
        private readonly BeatJudge _beatJudge = new BeatJudge();
        private SwordArmPose? _inLinePose;
        private bool _hasReportedBeat;
        private bool _hasReportedAttackUnderWay;
        private float _attackDistanceMeters;
        private BladeClash? _lastClash;
        private bool _hasAttackClashed;
        private bool _hasReportedLanding;
        private BladeSegment? _lastFencerBlade;
        private bool _isActive;
        private ArmedMode _mode = ArmedMode.Unarmed;
        private float _sparkEndsAt = float.NegativeInfinity;
        private float _sparkBladeFraction;
        private int _loggedPlanCount;
        private bool _hasLoggedWait;

        /// <param name="getFreePractice">What the Dummy does in Free Practice this frame, or null while it is off.</param>
        public ArmedDummyPrototype(
            DummyBehaviour dummy, DrillRunner runner, Func<FreePracticeBehavior?> getFreePractice, BladeHapticPlayer haptics, OVRCameraRig cameraRig)
        {
            _dummy = dummy ?? throw new ArgumentNullException(nameof(dummy));
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _getFreePractice = getFreePractice ?? throw new ArgumentNullException(nameof(getFreePractice));
            _freeAttacks = new FreeAttackSchedule(_random);
            _haptics = haptics;
            _bodyReader = new FencerBodyReader(cameraRig);
            _hitFlash = new HitFlash(cameraRig != null ? cameraRig.centerEyeAnchor : null);

            _mount = new GameObject("Armed Dummy Blade").transform;
            _visual = new BladeVisual(_mount);
            _visual.SetVisible(false);
            _spark = CreateSpark();
            _scrape = BladeScrapePlayer.TryCreate();
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
            _lastDummyPoint = null;
        }

        /// <summary>
        /// One frame, before the Fencer's drawn blade moves: poses the arm, lets the Dummy's hand give way to last frame's
        /// press, solves the two blades' contact (<see cref="ArmedDummyBlade"/>, PER-122), tests the clash, draws.
        /// Everything in the Strip frame.
        /// </summary>
        /// <param name="fencerBlade">The Fencer's blade from the hand: its chord to the real point.</param>
        /// <param name="fencerDroop">The Fencer's blade's rest shape on that chord.</param>
        public void Step(double nowSeconds, float deltaSeconds, BladeSegment fencerBlade, BladeDroop fencerDroop, TrackedPose stripInRoom)
        {
            _lastFencerBlade = fencerBlade;
            _hitFlash.Update();
            var mode = GetArmedMode();
            if (mode != _mode)
            {
                SetMode(mode);
            }

            if (mode == ArmedMode.Unarmed)
            {
                return;
            }

            if (mode == ArmedMode.FreeAttacks)
            {
                PlanFreeAttack(nowSeconds);
            }

            // The upper body turns a little with a knocked arm (PER-116), from the next posing of the legs on.
            _dummy.Layout.TurnTorso(_blade.GetKnockedTorsoTurn(_dummy.Layout));
            if (_attack != null && _attack.LungeMeters > 0.0f)
            {
                // The Dummy lunges (PER-101): the body moves along the Strip with the thrust and back, on the attack's
                // timing; the footwork plants the back foot, kicks out the front foot and drops the hips (PER-111).
                _dummy.Footwork.StepLunge(_attack.GetLungeOffsetMeters(nowSeconds), _attack.LungeMeters, deltaSeconds);
                _dummy.ApplyFootwork();
            }

            WaitWhileKnocked(nowSeconds);
            var scriptPose = _attack?.GetPose(nowSeconds) ?? _inLinePose ?? SwordArmReach.PoseEnGarde(_dummy.Layout.HeightMeters, BladePose.BladeLengthMeters);

            // The script's arm is the blade's line; the knocked arm is drawn and judged (PER-116).
            _dummy.Layout.PoseArm(scriptPose);
            var (forte, gripUp, shoulder) = GetDummyBladeOnStrip();
            var wasPressed = _blade.IsPressed;
            var wasReleasing = _blade.Contact.IsReleasing;
            var fractionsBefore = (_blade.Contact.FractionA, _blade.Contact.FractionB);
            _blade.Step(forte, shoulder, gripUp, -System.Numerics.Vector3.UnitY, _settings.Blade, _settings.ArmedDummy.OpponentHand, fencerBlade, fencerDroop, deltaSeconds);
            _dummy.SetArmPose(_blade.GetKnockedArmPose(scriptPose, _dummy.Layout.Hand));
            LogContact(wasPressed, wasReleasing);
            PlayScrape(stripInRoom, wasPressed, fractionsBefore, deltaSeconds);

            var clash = _detector.Step(nowSeconds, fencerBlade, _blade.Chord);
            if (clash.HasValue)
            {
                OnClash(clash.Value, nowSeconds);
            }

            LogDeflection();

            UpdateFencer(stripInRoom, fencerBlade);
            ReportAttackUnderWay(nowSeconds);
            JudgeBeat(nowSeconds, clash);
            JudgeParry(nowSeconds, clash, fencerBlade);
            JudgeHit(nowSeconds);
            _visual.Draw(_blade.Drawn, stripInRoom, (float)nowSeconds);
            MoveSpark(stripInRoom);
        }

        /// <summary>The Dummy is hidden or has no Strip: puts the blade away until a <see cref="Step"/> arms it again.</summary>
        public void Disarm()
        {
            if (_mode != ArmedMode.Unarmed)
            {
                SetMode(ArmedMode.Unarmed);
            }
        }

        /// <summary>Adds the Dummy's bell guard, as drawn this frame, to the Fencer's drawn blade's obstacles while armed (PER-120).</summary>
        public void AddBladeObstacles(System.Collections.Generic.ICollection<BladeObstacle> obstacles)
        {
            if (_isActive)
            {
                _blade.AddObstacles(obstacles);
            }
        }

        /// <summary>
        /// What the Fencer's drawn blade takes this frame while armed: its blade turned and bowed by the contact with the
        /// Dummy's (PER-122). False while unarmed: the Fencer's blade is drawn as the hand holds it.
        /// </summary>
        public bool TryGetFencerGive(out BladeGive give)
        {
            give = _blade.FencerGive;
            return _isActive;
        }

        /// <summary>How far the blades press into each other this frame (0 while apart or unarmed): the bind buzz's strength.</summary>
        public float PressMeters => _isActive ? _blade.Contact.PressMeters : 0.0f;

        /// <summary>
        /// True while armed and the blades are near enough that a flex whip could carry the Fencer's drawn blade into the
        /// Dummy's (<see cref="BladeContactSolver.IsNear"/>): its flex is held still then.
        /// </summary>
        public bool IsBladeNear => _isActive && _blade.Contact.IsNear;

        public void Dispose()
        {
            _runner.RepCued -= OnRepCued;
            _visual.Dispose();
            _hitFlash.Dispose();
            _scrape?.Dispose();
            if (_spark != null)
            {
                UnityEngine.Object.Destroy(_spark.gameObject);
            }
        }

        // Armed in the Drills where it attacks, and in every other Drill with the "Dummy blade in every Drill" Setting on
        // (PER-117). There no cue names an attack or a blade in line, so the blade only holds its en garde line, still
        // solid against the Fencer's (PER-104).
        private bool IsTestDrillRunning()
        {
            return _runner.IsRunning && !_runner.IsPaused && _runner.Drill != null
                && _runner.Drill.HoldsDummyBlade(_settings.ArmedDummy.BladeInEveryDrill);
        }

        // Free Practice (PER-123) arms the Dummy outside any Drill, whatever the "Dummy blade in every Drill" Setting says.
        private ArmedMode GetArmedMode()
        {
            if (_dummy.Layout == null)
            {
                return ArmedMode.Unarmed;
            }

            if (IsTestDrillRunning())
            {
                return ArmedMode.Drill;
            }

            var freePractice = _getFreePractice();
            if (!freePractice.HasValue)
            {
                return ArmedMode.Unarmed;
            }

            return freePractice.Value == FreePracticeBehavior.Attacks ? ArmedMode.FreeAttacks : ArmedMode.FreeGuard;
        }

        // Free Practice: nothing is reported to the runner (no Reps, no score), only played and logged.
        private bool IsFreePractice => _mode == ArmedMode.FreeGuard || _mode == ArmedMode.FreeAttacks;

        /// <summary>
        /// Free Practice attacks (PER-123): once the last thrust is over and the rest after it too, plans the next one from
        /// the test Drill's script, standing at extension distance (further back for the arm) as that Drill does.
        /// </summary>
        private void PlanFreeAttack(double nowSeconds)
        {
            var isAttackUnderWay = _attack != null && _attack.GetPhase(nowSeconds) != AttackPhase.Done;
            var next = _freeAttacks.Step(nowSeconds, isAttackUnderWay);
            if (next.HasValue)
            {
                PlanAttack(next.Value.Target, next.Value.Speed, FreePractice.AttackDistance, lungeMeters: 0.0f, "Free Practice");
            }
        }

        /// <summary>
        /// Judges this frame's contact against the attack under way (thrust or hold, not yet parried), forwards a Parry
        /// to the runner and cuts the thrust short, and reports a thrust that got through its hold as landed.
        /// </summary>
        private void JudgeParry(double nowSeconds, BladeClash? clash, BladeSegment fencerBlade)
        {
            if (_attack == null || _judge == null || _attack.IsParried || _attack.HasLanded)
            {
                return;
            }

            var phase = _attack.GetPhase(nowSeconds);
            if (phase == AttackPhase.Recover || phase == AttackPhase.Done)
            {
                if (!_hasReportedLanding)
                {
                    _hasReportedLanding = true;
                    Debug.Log("[ArmedDummy] Thrust ended short of the Fencer: not parried, no hit.");
                    if (!IsFreePractice)
                    {
                        _runner.ReportAttackLanded();
                    }
                }

                return;
            }

            if (phase != AttackPhase.Thrust && phase != AttackPhase.Hold)
            {
                return;
            }

            // A Stop Hit Drill (PER-101) asks for a Touch, not a Parry: the thrust carries on through blade contact.
            // Free Practice judges every attack's Parry, as the test Drill does.
            if (!IsFreePractice && _runner.Drill.SuccessRule != SuccessRule.Parry)
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

            // The path was planned where the attack started, before any lunge.
            var line = AttackLine.OnStrip(_attack.Path, _attackDistanceMeters, nowSeconds);
            // Without a headset pose there is no tracked Fencer: the stand-in at the En Garde Line keeps the judging fair.
            var body = _fencer.HasBody ? _fencer.Capsules : AttackAim.GetFencerBodyOnStrip(_dummy.Layout, fencerBlade.GuardPosition);
            var parry = _judge.Step(nowSeconds, _blade.JudgedTipOffset, isInContact, line, body);
            if (!parry.HasValue)
            {
                return;
            }

            _attack.ReportParried(nowSeconds);
            var report = parry.Value;
            Debug.Log($"[ArmedDummy] Parry {(int)report.Line} during the {phase}: point off the body by {report.DeflectionMeters * 100.0f:0} cm, " +
                $"Fencer blade at {report.FencerBladeFraction:0.00}, Dummy blade at {report.DummyBladeFraction:0.00}, " +
                $"{(report.IsForteOnFoible ? "forte on foible" : "not forte on foible")}, rule {_settings.Blade.ParryRule}: " +
                (report.CountsAsParry ? "counts." : "does not count.") + (IsFreePractice ? " Free Practice: not scored." : string.Empty));
            if (!IsFreePractice)
            {
                _runner.ReportParry(report);
            }
        }

        /// <summary>This frame's Fencer body (IOBT or the headset capsules), logging when the source changes.</summary>
        private void UpdateFencer(TrackedPose stripInRoom, BladeSegment fencerBlade)
        {
            var layout = _dummy.Layout;
            var headset = _bodyReader.ReadHeadset(stripInRoom);
            var trackedBody = _bodyReader.ReadBody(stripInRoom, headset, layout.Hand, layout.HeightMeters);
            _fencer.Update(trackedBody, headset, fencerBlade.GuardPosition, layout);
            if (_fencer.Source != _lastLoggedSource)
            {
                _lastLoggedSource = _fencer.Source;
                Debug.Log($"[ArmedDummy] Hits on the Fencer judged by {_fencer.Source?.ToString() ?? "nothing (no headset pose)"}" +
                    $" (body tracking fidelity {trackedBody.Fidelity?.ToString() ?? "none"}).");
            }
        }

        /// <summary>
        /// The unparried point's path this frame against the Fencer's body: on the first contact the thrust stops there,
        /// the red flash and the hit buzz play, and the runner gets the hit.
        /// </summary>
        private void JudgeHit(double nowSeconds)
        {
            var pointBefore = _lastDummyPoint;
            var pointNow = _blade.Chord.TipPosition;
            _lastDummyPoint = pointNow;
            if (_attack == null || !pointBefore.HasValue || _attack.IsParried || _attack.HasLanded || _hasReportedLanding)
            {
                return;
            }

            var phase = _attack.GetPhase(nowSeconds);
            if (phase != AttackPhase.Thrust && phase != AttackPhase.Hold)
            {
                return;
            }

            var hit = _fencer.FindHit(pointBefore.Value, pointNow);
            if (!hit.HasValue)
            {
                return;
            }

            _attack.ReportLanded(nowSeconds);
            _hasReportedLanding = true;
            var landed = hit.Value;
            if (!IsFreePractice && _runner.State != DrillState.Window)
            {
                // The Rep was already judged (a Stop Hit that landed clear of the double window): like the épée's
                // lock-out, the late point lights nothing.
                Debug.Log($"[ArmedDummy] Point landed on the Fencer's {landed.Part} after the Rep was judged: locked out.");
                return;
            }

            _hitFlash.Show();
            _haptics?.PlayArmedDummyHit();
            Debug.Log($"[ArmedDummy] Hit on the Fencer: {landed.Part}, judged by {landed.Source}, during the {phase}: not parried." +
                (IsFreePractice ? " Free Practice: not scored." : string.Empty));
            if (!IsFreePractice)
            {
                _runner.ReportAttackLanded(landed);
            }
        }

        /// <summary>Arms or disarms the Dummy, or changes why it is armed: every change starts the blade and the attacks over.</summary>
        private void SetMode(ArmedMode mode)
        {
            _mode = mode;
            var isActive = mode != ArmedMode.Unarmed;
            _isActive = isActive;
            _attack = null;
            _judge = null;
            _inLinePose = null;
            _freeAttacks.Reset();
            Restart();
            _clashBuzzGate.Reset();
            _scrape?.Silence();
            _visual.SetVisible(isActive);
            if (!isActive && _dummy.Layout != null)
            {
                _dummy.Layout.TurnTorso(System.Numerics.Quaternion.Identity);
                _dummy.SetArmPose(SwordArmPose.ArmInLine);
            }

            Debug.Log(DescribeMode(mode));
        }

        private string DescribeMode(ArmedMode mode)
        {
            switch (mode)
            {
                case ArmedMode.Drill:
                    return _runner.Drill.UsesArmedDummy
                        ? "[ArmedDummy] Armed: the Dummy holds a blade."
                        : "[ArmedDummy] Armed: the Dummy holds its blade in line, no attacks (Dummy blade in every Drill).";
                case ArmedMode.FreeGuard:
                    return "[ArmedDummy] Armed for Free Practice: the Dummy holds its blade in line, no attacks.";
                case ArmedMode.FreeAttacks:
                    return "[ArmedDummy] Armed for Free Practice: the Dummy attacks now and then, not scored.";
                default:
                    return "[ArmedDummy] Disarmed.";
            }
        }

        private void OnRepCued(RepCue cue)
        {
            if (!IsTestDrillRunning() || _dummy.Layout == null)
            {
                return;
            }

            if (cue.CueType == CueType.BladeInLine)
            {
                HoldBladeInLine(cue);
                return;
            }

            if (!cue.AttackTarget.HasValue || !cue.AttackSpeed.HasValue)
            {
                return;
            }

            var lungeMeters = _runner.Drill.Attacks.Lunges ? ArmedDummyAttack.GetLungeMeters(_dummy.Layout.HeightMeters) : 0.0f;
            PlanAttack(cue.AttackTarget.Value, cue.AttackSpeed.Value, cue.DummyDistance, lungeMeters, $"Rep {cue.RepIndex + 1}");
        }

        /// <summary>
        /// Plans one thrust at <paramref name="target"/> and <paramref name="speed"/> after a random delay, standing the
        /// Dummy at <paramref name="distance"/> first (further back for a thrust at the arm). <paramref name="label"/>
        /// names the Rep, or Free Practice, in the log.
        /// </summary>
        private void PlanAttack(AttackTarget target, AttackSpeed speed, DummyDistance distance, float lungeMeters, string label)
        {
            var layout = _dummy.Layout;
            var standBackMeters = AttackAim.GetStandBackMeters(target);
            StandForAttack(distance, standBackMeters);
            var delay = ArmedDummyAttack.DrawDelaySeconds(_random);
            // Chest and thigh aim at the Fencer's body as tracked (the Dummy frame is the Strip frame turned and moved, its
            // own inverse); hand and forearm, or no body, by the stand-in.
            var aim = _fencer.TryGetAim(target, out var aimOnStrip)
                ? DummyLayout.DummyToStrip(aimOnStrip, _dummy.DistanceMeters)
                : AttackAim.GetAimInDummyFrame(
                    target, layout, _dummy.DistanceMeters, _lastFencerBlade?.GuardPosition, _lastFencerBlade?.Direction ?? System.Numerics.Vector3.UnitZ);
            _attack = ArmedDummyAttack.Plan(
                Time.timeAsDouble, delay, layout.HeightMeters, layout.Hand, aim, speed, BladePose.BladeLengthMeters, lungeMeters);
            _hasReportedAttackUnderWay = false;
            _hasLoggedWait = false;
            _judge = new ParryJudge(_settings.Blade.ParryRule, _settings.Hand);
            _hasAttackClashed = false;
            _hasReportedLanding = false;
            var missMeters = System.Numerics.Vector3.Distance(_attack.Path.AimPoint, aim);
            Debug.Log($"[ArmedDummy] {label}: thrust at the {target}, {speed} ({ArmedDummyAttack.GetPeakPointSpeed(speed):0.0#} m/s), " +
                $"Dummy at {_dummy.DistanceMeters:0.00} m ({standBackMeters:0.00} m back, lunge {lungeMeters:0.00} m), " +
                $"after {delay:0.0} s, thrust {_attack.ThrustSeconds * 1000.0f:0} ms over {_attack.Path.LengthMeters * 100.0f:0} cm" +
                (missMeters > 0.01f ? $", lands {missMeters * 100.0f:0} cm off the aim (out of the arm's reach)." : "."));
        }

        /// <summary>
        /// Stands the Dummy at the Rep's Dummy Distance, further back by <paramref name="standBackMeters"/> for a thrust at
        /// the arm (PER-100, <see cref="RepCue.DummyStandBackMeters"/>), so every Rep starts from its own distance. The
        /// Blade restarts its sweeps on the jump.
        /// </summary>
        private void StandForAttack(DummyDistance distance, float standBackMeters)
        {
            _dummy.SetDistance(distance);
            if (standBackMeters > 0.0f)
            {
                _dummy.SetDistanceMeters(_dummy.DistanceMeters + standBackMeters);
            }

            _attackDistanceMeters = _dummy.DistanceMeters;
            Restart();
        }

        /// <summary>
        /// Beat Drills (PER-101): the Dummy stands at the Rep's distance with its arm straight and its blade in line at the
        /// Fencer's chest (as near as the arm reaches), until the Fencer beats it.
        /// </summary>
        private void HoldBladeInLine(RepCue cue)
        {
            StandForAttack(cue.DummyDistance, cue.DummyStandBackMeters);
            _attack = null;
            _judge = null;
            _beatJudge.Reset();
            _hasReportedBeat = false;
            var layout = _dummy.Layout;
            var chest = AttackAim.GetAimInDummyFrame(AttackTarget.Chest, layout, _dummy.DistanceMeters, null, System.Numerics.Vector3.UnitZ);
            var rightHandedChest = layout.Hand == Hand.Left ? HandMirroring.MirrorPosition(chest) : chest;
            _inLinePose = SwordArmReach.PoseToReach(layout.HeightMeters, rightHandedChest, BladePose.BladeLengthMeters);
            Debug.Log($"[ArmedDummy] Rep {cue.RepIndex + 1}: blade in line, Dummy at {_dummy.DistanceMeters:0.00} m.");
        }

        /// <summary>Stop Hit Drills (PER-101): tells the runner once the attack's wind-up starts, so a Touch on the arm now counts.</summary>
        private void ReportAttackUnderWay(double nowSeconds)
        {
            if (IsFreePractice || _attack == null || _hasReportedAttackUnderWay || _attack.GetPhase(nowSeconds) == AttackPhase.Waiting)
            {
                return;
            }

            _hasReportedAttackUnderWay = true;
            _runner.ReportAttackUnderWay();
        }

        /// <summary>
        /// Beat Drills (PER-101): judges this frame's contact on the blade in line (<see cref="BeatJudge"/>) and forwards the
        /// first Beat that knocks it out of line.
        /// </summary>
        private void JudgeBeat(double nowSeconds, BladeClash? clash)
        {
            if (_inLinePose == null || _hasReportedBeat)
            {
                return;
            }

            if (clash.HasValue)
            {
                _beatJudge.ReportClash(clash.Value);
            }

            var beat = _beatJudge.Step(nowSeconds, _blade.JudgedTipOffset, _detector.IsInContact || _blade.IsPressed);
            if (!beat.HasValue)
            {
                return;
            }

            _hasReportedBeat = true;
            var report = beat.Value;
            Debug.Log($"[ArmedDummy] Beat: point knocked {report.OutOfLineMeters * 100.0f:0} cm out of line, Fencer blade at " +
                $"{report.FencerBladeFraction:0.00}, Dummy blade at {report.DummyBladeFraction:0.00}.");
            _runner.ReportBeat(report);
        }

        /// <summary>
        /// A Dummy whose arm is knocked off line does not start its next wind-up until the arm is back (PER-116): the
        /// attack's plan waits for the deflection's recovery.
        /// </summary>
        private void WaitWhileKnocked(double nowSeconds)
        {
            var deflection = _blade.Deflection;
            if (_attack == null || !deflection.IsOffLine || !_attack.PostponeWindUp(nowSeconds, nowSeconds + deflection.SecondsUntilInLine))
            {
                return;
            }

            if (!_hasLoggedWait)
            {
                _hasLoggedWait = true;
                Debug.Log($"[ArmedDummy] Knocked off line: the next attack waits until the arm is back ({deflection.SecondsUntilInLine:0.00} s).");
            }
        }

        /// <summary>Logs each knock or release that takes the point well off line.</summary>
        private void LogContact(bool wasPressed, bool wasReleasing)
        {
            var contact = _blade.Contact;
            if (contact.IsInContact != wasPressed)
            {
                Debug.Log(contact.IsInContact
                    ? $"[ArmedDummy] Blades pressed at {contact.FractionA:0.00} of the Fencer's blade, {contact.FractionB:0.00} of the Dummy's"
                    : "[ArmedDummy] Blades let go");
            }

            if (contact.IsReleasing != wasReleasing)
            {
                Debug.Log(contact.IsReleasing
                    ? $"[ArmedDummy] Pressed past every give ({contact.PressMeters * 100.0f:0} cm): the weaker blade slides off toward its point"
                    : "[ArmedDummy] Slide-off over");
            }
        }

        /// <summary>The steel scrape (PER-122): louder the harder the blades press, higher the faster the contact slides.</summary>
        private void PlayScrape(TrackedPose stripInRoom, bool wasPressed, (float A, float B) fractionsBefore, float deltaSeconds)
        {
            if (_scrape == null)
            {
                return;
            }

            var contact = _blade.Contact;
            if (!_settings.Blade.ContactSound || !contact.IsInContact)
            {
                _scrape.Silence();
                return;
            }

            var slide = wasPressed
                ? ScrapeSound.FindSlideSpeed(fractionsBefore.A, contact.FractionA, fractionsBefore.B, contact.FractionB, BladePose.BladeLengthMeters, deltaSeconds)
                : 0.0f;
            _scrape.Play(
                StripFrame.ToRoom(stripInRoom, contact.ContactPoint).ToUnity(),
                ScrapeSound.GetVolume(contact.PressMeters, slide),
                ScrapeSound.GetPitch(slide));
        }

        private void LogDeflection()
        {
            var deflection = _blade.Deflection;
            if (deflection.PlanCount == _loggedPlanCount)
            {
                return;
            }

            _loggedPlanCount = deflection.PlanCount;
            var plan = deflection.LastPlan;
            Debug.Log($"[ArmedDummy] Deflect {plan.Meters * 100.0f:0} cm (wrist {plan.WristMeters * 100.0f:0}, arm {plan.ArmMeters * 100.0f:0}), " +
                $"open {plan.OpenSeconds:0.00} s, recover {plan.RecoverSeconds:0.00} s");
        }

        private void OnClash(BladeClash clash, double nowSeconds)
        {
            _blade.Deflection.Kick(clash);
            _lastClash = clash;
            var phase = _attack?.GetPhase(nowSeconds);
            var buzzStrength = ClashHaptics.GetStrength(_settings.Blade.ClashHapticFloor, clash.RelativeSpeedMetersPerSecond);
            var isBuzzed = _clashBuzzGate.TryPass(nowSeconds);
            if (isBuzzed)
            {
                _haptics?.PlayClash(_settings.Blade.ClashHaptic, buzzStrength, _settings.Hand);
            }

            ShowSpark(clash.OpponentBladeFraction);
            Debug.Log($"[ArmedDummy] Clash {clash.RelativeSpeedMetersPerSecond:0.00} m/s, Fencer blade at {clash.FencerBladeFraction:0.00}, " +
                $"Dummy blade at {clash.OpponentBladeFraction:0.00}, knock {BladeDeflection.GetKickMeters(clash.RelativeSpeedMetersPerSecond, clash.OpponentBladeFraction) * 100.0f:0} cm, " +
                $"buzz {(isBuzzed ? buzzStrength.ToString("0.00") : "skipped (rate limit)")}, during {phase?.ToString() ?? "guard"}");
        }

        /// <summary>
        /// The Dummy's straight blade as its arm holds it now (guard to the undrooped point), its grip's up and its sword
        /// shoulder, in the Strip frame.
        /// </summary>
        private (BladeSegment Forte, System.Numerics.Vector3 GripUp, System.Numerics.Vector3 Shoulder) GetDummyBladeOnStrip()
        {
            var arm = _dummy.Layout.Arm;
            var distance = _dummy.DistanceMeters;
            var guard = DummyLayout.DummyToStrip(arm.GetGuardPosition(), distance);
            var point = DummyLayout.DummyToStrip(arm.GetBladePoint(BladePose.BladeLengthMeters), distance);
            var shoulder = DummyLayout.DummyToStrip(arm.GetPose(ArmJoint.Shoulder).Position, distance);
            var wristUp = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitY, arm.GetPose(ArmJoint.Wrist).Rotation);
            return (new BladeSegment(guard, point), DirectionToStrip(wristUp), shoulder);
        }

        private static System.Numerics.Vector3 DirectionToStrip(System.Numerics.Vector3 directionInDummyFrame)
        {
            return new System.Numerics.Vector3(-directionInDummyFrame.X, directionInDummyFrame.Y, -directionInDummyFrame.Z);
        }

        /// <summary>Shows the spark at <paramref name="dummyBladeFraction"/> along the Dummy's blade, where the blades met.</summary>
        private void ShowSpark(float dummyBladeFraction)
        {
            if (_spark == null)
            {
                return;
            }

            _sparkBladeFraction = dummyBladeFraction;
            _spark.enabled = true;
            _sparkEndsAt = Time.time + SparkSeconds;
        }

        /// <summary>
        /// Keeps the spark on its spot of the Dummy's blade as drawn this frame, so it never hangs in the air; hides it
        /// when its time is up.
        /// </summary>
        private void MoveSpark(TrackedPose stripInRoom)
        {
            if (_spark == null || !_spark.enabled)
            {
                return;
            }

            if (Time.time >= _sparkEndsAt)
            {
                _spark.enabled = false;
                return;
            }

            _spark.transform.position = StripFrame.ToRoom(stripInRoom, _blade.GetPointAt(_sparkBladeFraction)).ToUnity();
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
