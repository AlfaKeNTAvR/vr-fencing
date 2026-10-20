// The Armed Dummy's harder Drills 10 to 12 (PER-101): Beat then thrust (the out-of-line threshold and the 1 s window),
// Stop Hit to the arm (before the point lands, or a double inside 50 ms), and the Dummy lunging (its whole body moving
// with the thrust) for a Parry and Riposte. Fake reports, contacts and clock; the Dummy Core's attack for the lunge path.
using System;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Blade;
using VrFencing.Drills;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Tests.Drills
{
    public class ArmedDummyDrillsTenToTwelveTests
    {
        private const float Strip = 3.0f;
        private const double Tolerance = 1e-9;

        private FakeClock _clock;
        private DrillRunner _runner;
        private RunnerRecorder _recorder;

        [SetUp]
        public void CreateRunner()
        {
            _clock = new FakeClock();
            _runner = new DrillRunner(_clock, new Random(9));
            _recorder = new RunnerRecorder(_runner);
        }

        private static DrillDefinition Build(string id, TrainerSettings settings = null)
        {
            return DrillCatalog.CreateDefault(includeTestDrills: false).BuildAll(settings ?? new TrainerSettings()).Single(drill => drill.Id == id);
        }

        private static BladeContact Touch(TargetZone zone, ContactKind kind = ContactKind.PointFirst)
        {
            return new BladeContact(zone, kind, 1.5f, 0.02f);
        }

        private static BeatReport Beat()
        {
            return new BeatReport(0.22f, fencerBladeFraction: 0.6f, dummyBladeFraction: 0.8f);
        }

        private static FencerHit Hit()
        {
            return new FencerHit(FencerHitSource.Iobt, BodyPart.Torso, new Vector3(0.0f, 1.2f, 0.1f));
        }

        private static BladeClash Clash(double atSeconds, float relativeSpeed, float dummyFraction = 1.0f)
        {
            return new BladeClash(atSeconds, Vector3.Zero, 0.6f, dummyFraction, relativeSpeed, Vector3.UnitX);
        }

        private void Start(string id, TrainerSettings settings = null)
        {
            Assert.That(_runner.Start(Build(id, settings), Strip, shrinkDrillsToFit: false).IsAvailable, Is.True);
            Assert.That(_runner.State, Is.EqualTo(DrillState.Window));
        }

        private void Wait(double seconds)
        {
            _clock.Advance(seconds);
            _runner.Tick();
        }

        // ---- The catalog ----

        [Test]
        public void DrillsTenToTwelveAreRegisteredAfterTheParryDrills()
        {
            var beat = Build(DrillCatalog.BeatDrillId, new TrainerSettings { RepsPerDrill = 10 });
            Assert.That((beat.Name, beat.Order, beat.RepCount), Is.EqualTo(("Beat, then thrust", 10, 10)));
            Assert.That(beat.SuccessRule, Is.EqualTo(SuccessRule.BeatThenTouch));
            Assert.That(beat.CueType, Is.EqualTo(CueType.BladeInLine));
            Assert.That(beat.DummyDistance, Is.EqualTo(DummyDistance.Lunge));
            Assert.That(beat.RequiredStripLengthMeters, Is.EqualTo(DrillCatalog.LungeStripLengthMeters), "The Fencer lunges after the Beat.");
            Assert.That(beat.Attacks, Is.Null, "The Dummy only holds its blade in line.");
            Assert.That(beat.UsesArmedDummy, Is.True);

            var stopHit = Build(DrillCatalog.StopHitDrillId);
            Assert.That((stopHit.Name, stopHit.Order), Is.EqualTo(("Stop hit to the arm", 11)));
            Assert.That(stopHit.SuccessRule, Is.EqualTo(SuccessRule.StopHit));
            Assert.That(stopHit.TargetZones, Is.EqualTo(new[] { TargetZone.Hand, TargetZone.Wrist, TargetZone.Forearm, TargetZone.UpperArm }));
            Assert.That(stopHit.Attacks.Targets, Is.EqualTo(new[] { AttackTarget.Chest, AttackTarget.Thigh }));
            Assert.That(stopHit.Attacks.Lunges, Is.False);
            Assert.That(stopHit.DummyDistance, Is.EqualTo(DummyDistance.Extension));
            Assert.That(stopHit.RequiresRiposte, Is.False);

            var lunge = Build(DrillCatalog.LungeParryDrillId);
            Assert.That((lunge.Name, lunge.Order), Is.EqualTo(("Dummy lunges", 12)));
            Assert.That(lunge.SuccessRule, Is.EqualTo(SuccessRule.Parry));
            Assert.That(lunge.RequiresRiposte, Is.True);
            Assert.That(lunge.Attacks.Lunges, Is.True);
            Assert.That(lunge.Attacks.Speeds, Is.EqualTo(new[] { AttackSpeed.Medium, AttackSpeed.Real }));
            Assert.That(lunge.DummyDistance, Is.EqualTo(DummyDistance.Lunge));
        }

        [Test]
        public void TheLungeDrillsRiposteSwitchTurnsOffOnlyIt()
        {
            var settings = new TrainerSettings();
            settings.ArmedDummy.RiposteInLungeParries = false;

            Assert.That(Build(DrillCatalog.LungeParryDrillId, settings).RequiresRiposte, Is.False);
            Assert.That(Build(DrillCatalog.LungeParryDrillId, settings).TaskLine, Is.EqualTo(DrillCatalog.LungeParryOnlyTaskLine));
            Assert.That(Build(DrillCatalog.SlowParryDrillId, settings).RequiresRiposte, Is.True);
        }

        [Test]
        public void TaskLinesFitThePicker()
        {
            foreach (var line in new[]
            {
                DrillCatalog.BeatTaskLine, DrillCatalog.StopHitTaskLine, DrillCatalog.LungeParryTaskLine, DrillCatalog.LungeParryOnlyTaskLine,
            })
            {
                Assert.That(line.Length, Is.LessThanOrEqualTo(DrillCatalog.MaxTaskLineLength), line);
            }
        }

        [Test]
        public void OnlyAStopHitOrParryDrillHasAnAttackAndOnlyABeatDrillHoldsTheBladeInLine()
        {
            var script = new AttackScript(new[] { AttackTarget.Chest }, new[] { AttackSpeed.Slow });
            Assert.Throws<ArgumentException>(() => new DrillDefinition(
                "s", "S", 1, 1.0f, null, 1, 0.0f, 1.0f, 1.0f, CueType.ArmedDummyAttack, SuccessRule.StopHit, new[] { TargetZone.Hand }));
            Assert.Throws<ArgumentException>(() => new DrillDefinition(
                "b", "B", 1, 1.0f, null, 1, 0.0f, 1.0f, 1.0f, CueType.BladeInLine, SuccessRule.BeatThenTouch, new[] { TargetZone.Body },
                attacks: script));
            Assert.Throws<ArgumentException>(() => new DrillDefinition(
                "b", "B", 1, 1.0f, null, 1, 0.0f, 1.0f, 1.0f, CueType.NamedTargetZone, SuccessRule.BeatThenTouch, new[] { TargetZone.Body }));
        }

        // ---- Beat then thrust ----

        [Test]
        public void ABeatThenATouchAnywhereWithinOneSecondSucceeds()
        {
            Start(DrillCatalog.BeatDrillId);
            Assert.That(_recorder.Cues.Single().TargetZone, Is.Null, "The blade in line is the cue.");
            Wait(1.5);
            _runner.ReportBeat(Beat());
            Assert.That(_runner.IsAwaitingTouchAfterBeat, Is.True);
            Wait(0.7);
            _runner.ReportContact(Touch(TargetZone.Thigh));

            var result = _recorder.Judged.Single();
            Assert.That(result.Outcome, Is.EqualTo(RepOutcome.Success));
            Assert.That(result.TouchAfterBeatSeconds.Value, Is.EqualTo(0.7).Within(Tolerance));
            Assert.That(result.BeatOutOfLineMeters, Is.EqualTo(0.22f));
            Assert.That(result.ReactionSeconds.Value, Is.EqualTo(1.5).Within(Tolerance), "Reaction time: cue to the Beat.");
        }

        [Test]
        public void ATouchBeforeTheBeatFailsButABladeSweepingTheDummyDoesNot()
        {
            Start(DrillCatalog.BeatDrillId);
            _runner.ReportContact(Touch(TargetZone.Forearm, ContactKind.Flat));
            Assert.That(_recorder.Judged, Is.Empty, "A flat sweep while beating is ignored.");
            _runner.ReportContact(Touch(TargetZone.Body));

            Assert.That(_recorder.Judged.Single().Outcome, Is.EqualTo(RepOutcome.NoBeat));
        }

        [Test]
        public void NoTouchWithinOneSecondOfTheBeatFails()
        {
            Start(DrillCatalog.BeatDrillId);
            _runner.ReportBeat(Beat());
            Wait(DrillDefinition.BeatTouchWindowSeconds);

            var result = _recorder.Judged.Single();
            Assert.That(result.Outcome, Is.EqualTo(RepOutcome.NoTouchAfterBeat));
            Assert.That(result.BeatOutOfLineMeters, Is.EqualTo(0.22f));
        }

        [Test]
        public void NoBeatInTheWindowIsATimeoutAndOnlyTheFirstBeatCounts()
        {
            Start(DrillCatalog.BeatDrillId);
            Wait(DrillCatalog.BeatWindowSeconds);
            Assert.That(_recorder.Judged.Single().Outcome, Is.EqualTo(RepOutcome.Timeout));

            Wait(new TrainerSettings().RestBetweenRepsSeconds);
            Assert.That(_runner.State, Is.EqualTo(DrillState.Window).Or.EqualTo(DrillState.Return));
        }

        [Test]
        public void TheBeatWindowRunsFromTheBeatNotFromTheSecondBeat()
        {
            Start(DrillCatalog.BeatDrillId);
            _runner.ReportBeat(Beat());
            Wait(0.8);
            _runner.ReportBeat(new BeatReport(0.3f, 0.5f, 0.5f));
            Wait(0.3);

            Assert.That(_recorder.Judged.Single().Outcome, Is.EqualTo(RepOutcome.NoTouchAfterBeat));
        }

        [Test]
        public void ASharpClashKnocksTheBladeOutOfLineAndALightOneDoesNot()
        {
            var judge = new BeatJudge();

            judge.ReportClash(Clash(1.0, relativeSpeed: 0.5f));
            Assert.That(judge.Step(1.0, Vector3.Zero, isInContact: false), Is.Null, "0.5 m/s is a light touch: at most 5 cm.");
            Assert.That(judge.Step(1.0 + ParryJudge.WatchSeconds + 0.01, Vector3.Zero, isInContact: false), Is.Null);
            Assert.That(judge.IsWatching, Is.False, "The watch closed without a Beat.");

            judge.ReportClash(Clash(2.0, relativeSpeed: 2.0f));
            var beat = judge.Step(2.0, Vector3.Zero, isInContact: true);
            Assert.That(beat.HasValue, Is.True);
            Assert.That(beat.Value.OutOfLineMeters, Is.GreaterThanOrEqualTo(BeatJudge.OutOfLineMeters));
            Assert.That(beat.Value.DummyBladeFraction, Is.EqualTo(1.0f));
        }

        [Test]
        public void APressThatPushesThePointFarEnoughIsABeat()
        {
            var judge = new BeatJudge();
            judge.ReportClash(Clash(1.0, relativeSpeed: 0.2f));

            Assert.That(judge.Step(1.1, new Vector3(0.1f, 0.0f, 0.0f), isInContact: true), Is.Null);
            Assert.That(judge.Step(1.5, new Vector3(0.16f, 0.0f, 0.0f), isInContact: true).HasValue, Is.True,
                "Held in contact, the watch stays open past its time, and the pushed point counts.");
        }

        // ---- Stop Hit to the arm ----

        [Test]
        public void ATouchOnTheArmBeforeThePointLandsIsAStopHitOnceTheDoubleWindowPasses(
            [Values(TargetZone.Hand, TargetZone.Wrist, TargetZone.Forearm, TargetZone.UpperArm)] TargetZone zone)
        {
            Start(DrillCatalog.StopHitDrillId);
            Wait(2.0);
            _runner.ReportAttackUnderWay();
            Wait(0.4);
            _runner.ReportContact(Touch(zone));
            Assert.That(_recorder.Judged, Is.Empty, "Judged only once the double window has passed.");
            Wait(DrillDefinition.StopHitDoubleWindowSeconds);

            var result = _recorder.Judged.Single();
            Assert.That(result.Outcome, Is.EqualTo(RepOutcome.Success));
            Assert.That(result.ContactZone, Is.EqualTo(zone));
            Assert.That(result.ReactionSeconds.Value, Is.EqualTo(2.4).Within(Tolerance));
            Assert.That(result.AttackTarget, Is.Not.Null);

            _runner.ReportAttackLanded(Hit());
            Assert.That(_recorder.Judged, Has.Count.EqualTo(1), "A landing after the double window changes nothing.");
        }

        [Test]
        public void ThePointLandingWithinTheDoubleWindowIsADouble()
        {
            Start(DrillCatalog.StopHitDrillId);
            _runner.ReportAttackUnderWay();
            _runner.ReportContact(Touch(TargetZone.Forearm));
            Wait(0.03);
            _runner.ReportAttackLanded(Hit());

            var result = _recorder.Judged.Single();
            Assert.That(result.Outcome, Is.EqualTo(RepOutcome.Double));
            Assert.That(result.ContactZone, Is.EqualTo(TargetZone.Forearm));
            Assert.That(result.FencerHit.Value.Part, Is.EqualTo(BodyPart.Torso));
        }

        [Test]
        public void ThePointLandingFirstFailsAndAnAttackEndingShortWithoutAStopHitFails()
        {
            Start(DrillCatalog.StopHitDrillId);
            _runner.ReportAttackUnderWay();
            Wait(1.0);
            _runner.ReportAttackLanded(Hit());
            Assert.That(_recorder.Judged.Last().Outcome, Is.EqualTo(RepOutcome.HitFirst));
            Assert.That(_recorder.Judged.Last().FencerHit.HasValue, Is.True);

            Wait(new TrainerSettings().RestBetweenRepsSeconds);
            _runner.ReportAttackUnderWay();
            _runner.ReportAttackLanded();
            Assert.That(_recorder.Judged.Last().Outcome, Is.EqualTo(RepOutcome.NoStopHit));
        }

        [Test]
        public void TouchesBeforeTheAttackAreIgnoredAndTheBodyIsTheWrongZone()
        {
            Start(DrillCatalog.StopHitDrillId);
            _runner.ReportContact(Touch(TargetZone.Hand));
            Assert.That(_recorder.Judged, Is.Empty, "The Dummy only stands en garde: no Stop Hit yet.");

            _runner.ReportAttackUnderWay();
            _runner.ReportContact(Touch(TargetZone.Body));
            Assert.That(_recorder.Judged.Single().Outcome, Is.EqualTo(RepOutcome.WrongZone));
        }

        [Test]
        public void AFlatContactOnTheArmIsNoStopHitAndParriesAreIgnored()
        {
            Start(DrillCatalog.StopHitDrillId);
            _runner.ReportAttackUnderWay();
            _runner.ReportParry(new ParryReport(ParryLine.Six, true, true, 0.2f, 0.3f, 0.7f));
            Assert.That(_recorder.Judged, Is.Empty);
            _runner.ReportContact(Touch(TargetZone.Forearm, ContactKind.Flat));

            Assert.That(_recorder.Judged.Single().Outcome, Is.EqualTo(RepOutcome.FlatContact));
        }

        // ---- The Dummy lunges ----

        [Test]
        public void ALungingThrustMovesTheWholeDummyForwardAndBack()
        {
            const float height = 1.75f;
            var lunge = ArmedDummyAttack.GetLungeMeters(height);
            var (attack, aim) = PlanLunge(AttackTarget.Chest, AttackSpeed.Medium, Hand.Right, height);

            Assert.That(lunge, Is.EqualTo(0.6f * height).Within(1e-5f));
            Assert.That(attack.GetLungeOffsetMeters(attack.WindUpStartSeconds - 0.1), Is.EqualTo(0.0f), "Feet planted until the thrust.");
            Assert.That(attack.GetLungeOffsetMeters(attack.ThrustEndSeconds), Is.EqualTo(lunge).Within(1e-4f));
            Assert.That(attack.GetLungeOffsetMeters(attack.ThrustEndSeconds + 0.1), Is.EqualTo(lunge).Within(1e-4f), "Held in the lunge.");
            Assert.That(attack.GetLungeOffsetMeters(attack.DoneSeconds), Is.EqualTo(0.0f), "Back where it started.");
            Assert.That(Vector3.Distance(attack.Path.AimPoint, aim), Is.LessThan(0.02f), "The lunge carries the point onto the chest.");

            var landing = PointOnFloorFrame(attack, attack.ThrustEndSeconds, Hand.Right, height);
            Assert.That(Vector3.Distance(landing, attack.Path.AimPoint), Is.LessThan(0.002f), "Arm and lunge together put the point on the path's end.");
        }

        [Test]
        public void ALungeFromLungeDistanceReachesTheChestAndThighWithoutTheLunge(
            [Values(AttackTarget.Chest, AttackTarget.Thigh)] AttackTarget target, [Values(1.5f, 1.75f, 2.0f)] float height)
        {
            var (planted, plantedAim) = PlanLunge(target, AttackSpeed.Medium, Hand.Right, height, lunges: false);
            var (lunging, lungingAim) = PlanLunge(target, AttackSpeed.Medium, Hand.Right, height);

            Assert.That(Vector3.Distance(planted.Path.AimPoint, plantedAim), Is.GreaterThan(0.3f), "Feet planted, the Dummy falls well short.");
            Assert.That(Vector3.Distance(lunging.Path.AimPoint, lungingAim), Is.LessThan(0.02f));
        }

        [Test]
        public void TheLungingPointsPeakSpeedOverTheFloorIsTheLevelsAndTheArmStaysInItsLimits(
            [Values(AttackSpeed.Medium, AttackSpeed.Real)] AttackSpeed speed, [Values(Hand.Right, Hand.Left)] Hand hand)
        {
            const float height = 1.75f;
            var (attack, _) = PlanLunge(AttackTarget.Chest, speed, hand, height);

            var peak = 0.0f;
            const double step = 0.0005;
            var previous = PointOnFloorFrame(attack, attack.ThrustStartSeconds, hand, height);
            for (var time = attack.ThrustStartSeconds + step; time <= attack.ThrustEndSeconds; time += step)
            {
                var point = PointOnFloorFrame(attack, time, hand, height);
                peak = Math.Max(peak, Vector3.Distance(previous, point) / (float)step);
                previous = point;
                Assert.That(SwordArmReach.IsWithinLimits(attack.GetPose(time)), Is.True);
            }

            Assert.That(peak, Is.EqualTo(ArmedDummyAttack.GetPeakPointSpeed(speed)).Within(0.03f * ArmedDummyAttack.GetPeakPointSpeed(speed)));
        }

        [Test]
        public void AParriedLungeRecoversFromWhereTheDummyWas()
        {
            var (attack, _) = PlanLunge(AttackTarget.Thigh, AttackSpeed.Medium, Hand.Right, 1.75f);
            var parryAt = attack.ThrustStartSeconds + (attack.ThrustSeconds * 0.5);
            var offsetAtParry = attack.GetLungeOffsetMeters(parryAt);

            Assert.That(attack.ReportParried(parryAt), Is.True);
            Assert.That(attack.GetLungeOffsetMeters(parryAt + 0.001), Is.EqualTo(offsetAtParry).Within(0.01f), "No jump at the Parry.");
            Assert.That(attack.GetLungeOffsetMeters(parryAt + ArmedDummyTuning.RecoverSeconds), Is.EqualTo(0.0f));
        }

        [Test]
        public void EveryLungeEndsBeforeTheRepWindowDoesAtTheLongestDelay([Values(1.5f, 1.75f, 2.0f)] float height)
        {
            var drill = Build(DrillCatalog.LungeParryDrillId);
            foreach (var target in drill.Attacks.Targets)
            {
                foreach (var speed in drill.Attacks.Speeds)
                {
                    var (attack, _) = PlanLunge(target, speed, Hand.Right, height, delaySeconds: ArmedDummyTuning.MaximumDelaySeconds);
                    Assert.That(attack.ThrustEndSeconds + ArmedDummyTuning.HoldSeconds, Is.LessThan(drill.WindowSeconds), $"{target}, {speed}, {height} m");
                }
            }
        }

        [Test]
        public void ANegativeLungeIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                ArmedDummyAttack.Plan(0.0, 2.0f, 1.75f, Hand.Right, Vector3.UnitZ, AttackSpeed.Medium, DistancePresets.BladeLengthMeters, -0.1f));
        }

        /// <summary>
        /// A thrust from lunge distance at <paramref name="target"/> of a Fencer of the Dummy's size en garde at the En Garde
        /// Line, lunging unless told not to.
        /// </summary>
        private static (ArmedDummyAttack Attack, Vector3 Aim) PlanLunge(
            AttackTarget target, AttackSpeed speed, Hand hand, float height, bool lunges = true, float delaySeconds = 2.0f)
        {
            var layout = DummyLayout.Create(height, hand);
            var distance = DistancePresets.GetDistanceMeters(DummyDistance.Lunge, height);
            var aim = AttackAim.GetAimInDummyFrame(target, layout, distance, layout.Arm.GetGuardPosition(), layout.Arm.GetBladeDirection());
            var lunge = lunges ? ArmedDummyAttack.GetLungeMeters(height) : 0.0f;
            return (ArmedDummyAttack.Plan(0.0, delaySeconds, height, hand, aim, speed, DistancePresets.BladeLengthMeters, lunge), aim);
        }

        /// <summary>
        /// The point at <paramref name="nowSeconds"/> in the Dummy frame where the attack started: the arm's point from the
        /// shoulder where the lunging torso has it (PER-115), plus the lunge.
        /// </summary>
        private static Vector3 PointOnFloorFrame(ArmedDummyAttack attack, double nowSeconds, Hand hand, float height)
        {
            var offset = attack.GetLungeOffsetMeters(nowSeconds);
            var hipOffset = attack.LungeMeters > 0.0f ? DummyFootwork.GetLungeHipOffset(height, offset, attack.LungeMeters) : Vector3.Zero;
            var shoulder = DummyTorso.GetSwordShoulder(height, hipOffset);
            var point = SwordArmReach.GetPoint(height, shoulder, attack.GetPose(nowSeconds), DistancePresets.BladeLengthMeters);
            var pointForHand = hand == Hand.Left ? HandMirroring.MirrorPosition(point) : point;
            return pointForHand + new Vector3(0.0f, 0.0f, offset);
        }
    }
}
