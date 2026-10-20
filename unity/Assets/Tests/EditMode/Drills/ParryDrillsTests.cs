// The Armed Dummy's Parry Drills 7 to 9 (PER-100): Parry 6/4 + Riposte slow, at medium and real speed, and Parry 8/7.
// What each Drill attacks with, how a Rep succeeds and every way it fails, the per-Drill Riposte switch, and the Dummy
// standing further back for a thrust at the arm (Nikita's call: 0.6 to 0.75 m). Fake Parry reports, contacts and clock.
using System;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Drills;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Tests.Drills
{
    public class ParryDrillsTests
    {
        private const float Strip = 2.0f;
        private const double Tolerance = 1e-9;

        private static readonly string[] ParryDrillIds =
        {
            DrillCatalog.SlowParryDrillId, DrillCatalog.FastParryDrillId, DrillCatalog.LowParryDrillId,
        };

        private FakeClock _clock;
        private DrillRunner _runner;
        private RunnerRecorder _recorder;

        [SetUp]
        public void CreateRunner()
        {
            _clock = new FakeClock();
            _runner = new DrillRunner(_clock, new Random(5));
            _recorder = new RunnerRecorder(_runner);
        }

        private static DrillDefinition Build(string id, TrainerSettings settings = null)
        {
            return DrillCatalog.CreateDefault(includeTestDrills: false).BuildAll(settings ?? new TrainerSettings()).Single(drill => drill.Id == id);
        }

        private static ParryReport Parry(ParryLine line = ParryLine.Six, bool countsAsParry = true)
        {
            return new ParryReport(line, isForteOnFoible: countsAsParry, countsAsParry, deflectionMeters: 0.2f, fencerBladeFraction: 0.3f, dummyBladeFraction: 0.7f);
        }

        private static BladeContact Touch(TargetZone zone)
        {
            return new BladeContact(zone, ContactKind.PointFirst, 1.5f, 0.02f);
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

        [Test]
        public void DrillsSevenToNineAreArmedDummyParryDrillsAtExtensionDistanceOnAnyStrip()
        {
            var expected = new[]
            {
                (DrillCatalog.SlowParryDrillId, "Parry 6/4: slow", 7),
                (DrillCatalog.FastParryDrillId, "Parry 6/4: medium, real", 8),
                (DrillCatalog.LowParryDrillId, "Parry 8/7", 9),
            };

            foreach (var (id, name, order) in expected)
            {
                var drill = Build(id, new TrainerSettings { RepsPerDrill = 10 });
                Assert.That(drill.Name, Is.EqualTo(name));
                Assert.That(drill.Order, Is.EqualTo(order));
                Assert.That(drill.RepCount, Is.EqualTo(10), "PER-23: 10 Reps each.");
                Assert.That(drill.SuccessRule, Is.EqualTo(SuccessRule.Parry));
                Assert.That(drill.UsesArmedDummy, Is.True);
                Assert.That(drill.CueType, Is.EqualTo(CueType.ArmedDummyAttack));
                Assert.That(drill.RequiresRiposte, Is.True, "The Riposte is on by default.");
                Assert.That(drill.DummyDistance, Is.EqualTo(DummyDistance.Extension));
                Assert.That(drill.RequiredStripLengthMeters, Is.EqualTo(1.0f), "Parry and Riposte from en garde: no lunge room needed.");
                Assert.That(drill.WaitsAtStartMark(isShrunk: false), Is.False);
                Assert.That(drill.TargetZones, Is.EquivalentTo((TargetZone[])Enum.GetValues(typeof(TargetZone))), "The Riposte may land anywhere.");
            }
        }

        [Test]
        public void SixFourDrillsThrustHighAndEightSevenThrustLowAtTheirSpeeds()
        {
            Assert.That(Build(DrillCatalog.SlowParryDrillId).Attacks.Targets,
                Is.EqualTo(new[] { AttackTarget.Hand, AttackTarget.Forearm, AttackTarget.Chest }));
            Assert.That(Build(DrillCatalog.SlowParryDrillId).Attacks.Speeds, Is.EqualTo(new[] { AttackSpeed.Slow }));
            Assert.That(Build(DrillCatalog.FastParryDrillId).Attacks.Targets,
                Is.EqualTo(new[] { AttackTarget.Hand, AttackTarget.Forearm, AttackTarget.Chest }));
            Assert.That(Build(DrillCatalog.FastParryDrillId).Attacks.Speeds, Is.EqualTo(new[] { AttackSpeed.Medium, AttackSpeed.Real }));
            Assert.That(Build(DrillCatalog.LowParryDrillId).Attacks.Targets, Is.EqualTo(new[] { AttackTarget.Thigh }));
            Assert.That(Build(DrillCatalog.LowParryDrillId).Attacks.Speeds, Is.EqualTo(new[] { AttackSpeed.Slow, AttackSpeed.Medium }));
        }

        [Test]
        public void EachRepCuesTheAttackWithNoZoneAndSpeedsTakeTurnsByRep()
        {
            Start(DrillCatalog.FastParryDrillId, new TrainerSettings { RepsPerDrill = 4, RestBetweenRepsSeconds = 1.0f });

            for (var rep = 0; rep < 4; rep++)
            {
                _runner.ReportParry(Parry());
                _runner.ReportContact(Touch(TargetZone.Body));
                Wait(1.0);
            }

            Assert.That(_recorder.Cues.Select(cue => cue.AttackSpeed),
                Is.EqualTo(new AttackSpeed?[] { AttackSpeed.Medium, AttackSpeed.Real, AttackSpeed.Medium, AttackSpeed.Real }));
            Assert.That(_recorder.Cues.Select(cue => cue.TargetZone), Has.All.Null, "The attack is the cue: nothing named or lit.");
            Assert.That(_recorder.Cues.Select(cue => cue.CueType), Has.All.EqualTo(CueType.ArmedDummyAttack));
            Assert.That(_recorder.Cues.Select(cue => cue.AttackTarget.Value),
                Has.All.AnyOf(AttackTarget.Hand, AttackTarget.Forearm, AttackTarget.Chest));
            Assert.That(_recorder.Judged.Select(result => result.AttackSpeed), Is.EqualTo(_recorder.Cues.Select(cue => cue.AttackSpeed)));
            Assert.That(_recorder.Judged.Select(result => result.AttackTarget), Is.EqualTo(_recorder.Cues.Select(cue => cue.AttackTarget)));
        }

        [Test]
        public void TargetsAreDrawnFromTheDrillsScriptOnly()
        {
            var script = Build(DrillCatalog.SlowParryDrillId).Attacks;
            var random = new Random(3);
            var drawn = Enumerable.Range(0, 300).Select(_ => script.DrawTarget(random)).ToArray();

            Assert.That(drawn.Distinct(), Is.EquivalentTo(script.Targets));
            Assert.That(Enumerable.Range(0, 300).Select(_ => Build(DrillCatalog.LowParryDrillId).Attacks.DrawTarget(random)).Distinct(),
                Is.EqualTo(new[] { AttackTarget.Thigh }));
        }

        [Test]
        public void AParryThenARiposteAnywhereWithinOneSecondSucceedsWithTheLineAndAttack(
            [Values(TargetZone.Body, TargetZone.Hand, TargetZone.Thigh, TargetZone.Foot)] TargetZone riposteZone)
        {
            Start(DrillCatalog.LowParryDrillId);
            Wait(3.0);
            _runner.ReportParry(Parry(ParryLine.Eight));
            Assert.That(_runner.IsAwaitingRiposte, Is.True);
            Wait(0.6);
            _runner.ReportContact(Touch(riposteZone));

            var result = _recorder.Judged.Single();
            Assert.That(result.Outcome, Is.EqualTo(RepOutcome.Success));
            Assert.That(result.ParryLine, Is.EqualTo(ParryLine.Eight));
            Assert.That(result.RiposteSeconds.Value, Is.EqualTo(0.6).Within(Tolerance));
            Assert.That(result.ReactionSeconds.Value, Is.EqualTo(3.0).Within(Tolerance), "Reaction time: cue to the Parry.");
            Assert.That(result.ContactZone, Is.EqualTo(riposteZone));
            Assert.That(result.RequestedZone, Is.Null);
            Assert.That(result.AttackTarget, Is.EqualTo(AttackTarget.Thigh));
            Assert.That(result.FencerHit, Is.Null);
        }

        [Test]
        public void AHitOnTheFencerFailsTheRepWithTheHitSource()
        {
            Start(DrillCatalog.SlowParryDrillId);
            Wait(4.0);
            _runner.ReportAttackLanded(new FencerHit(FencerHitSource.HeadsetCapsules, BodyPart.SwordHand, new Vector3(0.2f, 1.0f, 0.4f)));

            var result = _recorder.Judged.Single();
            Assert.That(result.Outcome, Is.EqualTo(RepOutcome.NotParried));
            Assert.That(result.FencerHit.Value.Source, Is.EqualTo(FencerHitSource.HeadsetCapsules));
            Assert.That(result.FencerHit.Value.Part, Is.EqualTo(BodyPart.SwordHand));
            Assert.That(result.ParryLine, Is.Null);
        }

        [Test]
        public void NoParryFailsTheRepWhenTheThrustEndsShort()
        {
            Start(DrillCatalog.FastParryDrillId);
            _runner.ReportContact(Touch(TargetZone.Hand));
            Assert.That(_recorder.Judged, Is.Empty, "A Touch before the Parry is ignored.");
            Wait(4.0);
            _runner.ReportAttackLanded();

            var result = _recorder.Judged.Single();
            Assert.That(result.Outcome, Is.EqualTo(RepOutcome.NotParried));
            Assert.That(result.FencerHit, Is.Null);
        }

        [Test]
        public void NoRiposteWithinOneSecondOfTheParryFailsTheRep()
        {
            Start(DrillCatalog.SlowParryDrillId);
            Wait(5.0);
            _runner.ReportParry(Parry(ParryLine.Four));
            Wait(1.0);

            var result = _recorder.Judged.Single();
            Assert.That(result.Outcome, Is.EqualTo(RepOutcome.NoRiposte));
            Assert.That(result.ParryLine, Is.EqualTo(ParryLine.Four));
            Assert.That(_runner.State, Is.EqualTo(DrillState.Rest));
        }

        [Test]
        public void AFlatRiposteFailsTheRep()
        {
            Start(DrillCatalog.SlowParryDrillId);
            _runner.ReportParry(Parry());
            _runner.ReportContact(new BladeContact(TargetZone.Body, ContactKind.Flat, 1.0f, 0.0f));

            Assert.That(_recorder.Judged.Single().Outcome, Is.EqualTo(RepOutcome.FlatContact));
        }

        [Test]
        public void ADeflectionTheStrictRuleRejectsFailsTheRep()
        {
            Start(DrillCatalog.LowParryDrillId);
            _runner.ReportParry(Parry(ParryLine.Seven, countsAsParry: false));

            var result = _recorder.Judged.Single();
            Assert.That(result.Outcome, Is.EqualTo(RepOutcome.NotForteOnFoible));
            Assert.That(result.ParryLine, Is.EqualTo(ParryLine.Seven));
        }

        [Test]
        public void WithTheRiposteSwitchedOffTheParryAloneSucceeds(
            [Values(DrillCatalog.SlowParryDrillId, DrillCatalog.FastParryDrillId, DrillCatalog.LowParryDrillId)] string id)
        {
            var settings = new TrainerSettings();
            settings.ArmedDummy.RiposteInSlowParries = false;
            settings.ArmedDummy.RiposteInFastParries = false;
            settings.ArmedDummy.RiposteInLowParries = false;
            Start(id, settings);
            Wait(2.5);
            _runner.ReportParry(Parry(ParryLine.Six));

            var result = _recorder.Judged.Single();
            Assert.That(result.Outcome, Is.EqualTo(RepOutcome.Success));
            Assert.That(result.ParryLine, Is.EqualTo(ParryLine.Six));
            Assert.That(result.RiposteSeconds, Is.Null);
            Assert.That(Build(id, settings).TaskLine, Does.EndWith("No riposte."));
        }

        [Test]
        public void EachRiposteSwitchTurnsOffOnlyItsOwnDrill()
        {
            var switches = new (Action<ArmedDummySettings> TurnOff, string DrillId)[]
            {
                (armed => armed.RiposteInSlowParries = false, DrillCatalog.SlowParryDrillId),
                (armed => armed.RiposteInFastParries = false, DrillCatalog.FastParryDrillId),
                (armed => armed.RiposteInLowParries = false, DrillCatalog.LowParryDrillId),
            };

            foreach (var (turnOff, drillId) in switches)
            {
                var settings = new TrainerSettings();
                turnOff(settings.ArmedDummy);
                foreach (var id in ParryDrillIds)
                {
                    Assert.That(Build(id, settings).RequiresRiposte, Is.EqualTo(id != drillId), $"{drillId} off: {id}");
                }
            }
        }

        [Test]
        public void TaskLinesSayWhatToParryAndWhetherToRiposte()
        {
            Assert.That(Build(DrillCatalog.SlowParryDrillId).TaskLine, Is.EqualTo(DrillCatalog.SlowParryTaskLine));
            Assert.That(Build(DrillCatalog.FastParryDrillId).TaskLine, Is.EqualTo(DrillCatalog.FastParryTaskLine));
            Assert.That(Build(DrillCatalog.LowParryDrillId).TaskLine, Is.EqualTo(DrillCatalog.LowParryTaskLine));
            foreach (var line in new[]
            {
                DrillCatalog.SlowParryTaskLine, DrillCatalog.SlowParryOnlyTaskLine, DrillCatalog.FastParryTaskLine,
                DrillCatalog.FastParryOnlyTaskLine, DrillCatalog.LowParryTaskLine, DrillCatalog.LowParryOnlyTaskLine,
            })
            {
                Assert.That(line.Length, Is.LessThanOrEqualTo(DrillCatalog.MaxTaskLineLength), line);
            }
        }

        [Test]
        public void TheDummyStandsBackForAThrustAtTheArmOnly()
        {
            Assert.That(AttackAim.GetStandBackMeters(AttackTarget.Hand), Is.EqualTo(0.75f));
            Assert.That(AttackAim.GetStandBackMeters(AttackTarget.Forearm), Is.EqualTo(0.6f));
            Assert.That(AttackAim.GetStandBackMeters(AttackTarget.Chest), Is.EqualTo(0.0f));
            Assert.That(AttackAim.GetStandBackMeters(AttackTarget.Thigh), Is.EqualTo(0.0f));

            var cue = new RepCue(0, null, CueType.ArmedDummyAttack, DummyDistance.Extension, attackTarget: AttackTarget.Hand, attackSpeed: AttackSpeed.Slow);
            Assert.That(cue.DummyStandBackMeters, Is.EqualTo(0.75f));
            Assert.That(new RepCue(0, TargetZone.Body, CueType.NamedTargetZone).DummyStandBackMeters, Is.EqualTo(0.0f));
        }

        [Test]
        public void EveryThrustEndsBeforeTheRepWindowDoesAtTheLongestDelay(
            [Values(1.5f, 1.75f, 2.0f)] float height, [Values(Hand.Right, Hand.Left)] Hand hand)
        {
            foreach (var id in ParryDrillIds)
            {
                var drill = Build(id);
                foreach (var target in drill.Attacks.Targets)
                {
                    foreach (var speed in drill.Attacks.Speeds)
                    {
                        var layout = DummyLayout.Create(height, hand);
                        var distance = DistancePresets.GetDistanceMeters(drill.DummyDistance, height) + AttackAim.GetStandBackMeters(target);
                        var aim = AttackAim.GetAimInDummyFrame(target, layout, distance, layout.Arm.GetGuardPosition(), layout.Arm.GetBladeDirection());
                        var attack = ArmedDummyAttack.Plan(
                            0.0, ArmedDummyTuning.MaximumDelaySeconds, height, hand, aim, speed, DistancePresets.BladeLengthMeters);

                        var holdEndsAt = attack.ThrustEndSeconds + ArmedDummyTuning.HoldSeconds;
                        Assert.That(holdEndsAt, Is.LessThan(drill.WindowSeconds), $"{id}, {target}, {speed}, {height} m, {hand}");
                    }
                }
            }
        }

        [Test]
        public void ArmThrustsReachTheirAimFromTheStoodBackDistance(
            [Values(AttackTarget.Hand, AttackTarget.Forearm)] AttackTarget target, [Values(1.5f, 1.75f, 2.0f)] float height)
        {
            var layout = DummyLayout.Create(height, Hand.Right);
            var nearDistance = DistancePresets.GetDistanceMeters(DummyDistance.Extension, height);
            var aimNear = AttackAim.GetAimInDummyFrame(target, layout, nearDistance, layout.Arm.GetGuardPosition(), layout.Arm.GetBladeDirection());
            var aimBack = AttackAim.GetAimInDummyFrame(
                target, layout, nearDistance + AttackAim.GetStandBackMeters(target), layout.Arm.GetGuardPosition(), layout.Arm.GetBladeDirection());

            var fromNear = ArmedDummyAttack.Plan(0.0, 2.0f, height, Hand.Right, aimNear, AttackSpeed.Medium, DistancePresets.BladeLengthMeters);
            var fromBack = ArmedDummyAttack.Plan(0.0, 2.0f, height, Hand.Right, aimBack, AttackSpeed.Medium, DistancePresets.BladeLengthMeters);

            Assert.That(Vector3.Distance(fromBack.Path.AimPoint, aimBack), Is.LessThan(0.02f), "Stood back, the point lands on the arm.");
            Assert.That(Vector3.Distance(fromNear.Path.AimPoint, aimNear), Is.GreaterThan(0.1f), "At extension distance it would end past it (PER-96).");
        }
    }
}
