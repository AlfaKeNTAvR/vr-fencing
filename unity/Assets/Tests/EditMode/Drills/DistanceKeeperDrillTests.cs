// The moving Dummy's dev Drill (PER-105): "Test: Dummy keeps distance" opts in to the distance keeper at Easy, Medium
// and Hard in turn by Rep; every other Drill, the Armed Dummy's included, keeps its Dummy standing still. The Rep's
// difficulty rides on its cue and result into the rep log.
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VrFencing.Drills;
using VrFencing.Dummy;
using VrFencing.Session;
using VrFencing.Settings;
using VrFencing.Tests.Session;

namespace VrFencing.Tests.Drills
{
    public class DistanceKeeperDrillTests
    {
        private static DrillDefinition BuildTestDrill(TrainerSettings settings = null)
        {
            return DrillCatalog.CreateDefault(includeTestDrills: true)
                .BuildAll(settings ?? new TrainerSettings())
                .Single(drill => drill.Id == DrillCatalog.DistanceKeeperTestDrillId);
        }

        private static DrillDefinition CreateDrill(
            SuccessRule rule = SuccessRule.PointFirstTouchOnTargetZone,
            CueType cue = CueType.NamedTargetZone,
            DummyDistance[] repDistances = null,
            DummyDifficulty[] difficulties = null)
        {
            return new DrillDefinition(
                "keeper",
                "Keeper",
                order: 1,
                requiredStripLengthMeters: 3.0f,
                shrunkStripLengthMeters: null,
                repCount: 3,
                cueSeconds: 0.0f,
                windowSeconds: 2.0f,
                restSeconds: 1.0f,
                cue,
                rule,
                new[] { TargetZone.Body },
                repDummyDistances: repDistances,
                dummyDifficulties: difficulties ?? new[] { DummyDifficulty.Hard });
        }

        [Test]
        public void TheDevDrillKeepsLungeDistanceAtEasyMediumAndHardInTurn()
        {
            var drill = BuildTestDrill(new TrainerSettings { RepsPerDrill = 5 });

            Assert.That(drill.Name, Is.EqualTo("Test: Dummy keeps distance"));
            Assert.That(drill.KeepsDistance, Is.True);
            Assert.That(drill.DummyDistance, Is.EqualTo(DummyDistance.Lunge));
            Assert.That(drill.UsesArmedDummy, Is.False);
            Assert.That(drill.RepCount, Is.EqualTo(5));
            Assert.That(drill.WaitsAtStartMark(isShrunk: false), Is.True, "The Fencer moves; the Dummy follows them back too.");
            Assert.That(
                Enumerable.Range(0, 5).Select(rep => drill.GetDummyDifficulty(rep)),
                Is.EqualTo(new DummyDifficulty?[] { DummyDifficulty.Easy, DummyDifficulty.Medium, DummyDifficulty.Hard, DummyDifficulty.Easy, DummyDifficulty.Medium }));
            Assert.That(drill.TaskLine.Length, Is.LessThanOrEqualTo(DrillCatalog.MaxTaskLineLength));
            Assert.That(drill.LightsNamedZone, Is.True, "The named zone is lit, to find it on a moving Dummy.");
        }

        [Test]
        public void TheDevDrillIsOnlyInDevelopmentBuildsAndSortsWithTheTestDrills()
        {
            var release = DrillCatalog.CreateDefault(includeTestDrills: false).BuildAll(new TrainerSettings());
            var development = DrillCatalog.CreateDefault(includeTestDrills: true).BuildAll(new TrainerSettings());

            Assert.That(release.Select(drill => drill.Id), Does.Not.Contain(DrillCatalog.DistanceKeeperTestDrillId));
            Assert.That(development.Select(drill => drill.Id), Does.Contain(DrillCatalog.DistanceKeeperTestDrillId));
            Assert.That(BuildTestDrill().Order, Is.GreaterThan(development.Where(drill => !drill.Name.StartsWith("Test:")).Max(drill => drill.Order)));
        }

        [Test]
        public void EveryOtherDrillKeepsItsDummyStill()
        {
            var drills = DrillCatalog.CreateDefault(includeTestDrills: true).BuildAll(new TrainerSettings())
                .Where(drill => drill.Id != DrillCatalog.DistanceKeeperTestDrillId);

            foreach (var drill in drills)
            {
                Assert.That(drill.KeepsDistance, Is.False, drill.Id);
                Assert.That(drill.GetDummyDifficulty(0), Is.Null, drill.Id);
            }
        }

        [Test]
        public void EachCueAndResultCarriesItsRepsDifficulty()
        {
            var clock = new FakeClock();
            var runner = new DrillRunner(clock, new Random(4));
            var recorder = new RunnerRecorder(runner);
            var drill = BuildTestDrill(new TrainerSettings { RepsPerDrill = 4, RestBetweenRepsSeconds = 0.5f });

            Assert.That(runner.Start(drill, stripLengthMeters: 3.5f, shrinkDrillsToFit: false).IsAvailable, Is.True);
            runner.ReportAtStartMark(true);
            for (var rep = 0; rep < 4; rep++)
            {
                clock.Advance(DrillCatalog.DistanceKeeperTestWindowSeconds + 0.1);
                runner.Tick();
                clock.Advance(1.0);
                runner.Tick();
            }

            Assert.That(recorder.Cues.Select(cue => cue.DummyDifficulty),
                Is.EqualTo(new DummyDifficulty?[] { DummyDifficulty.Easy, DummyDifficulty.Medium, DummyDifficulty.Hard, DummyDifficulty.Easy }));
            Assert.That(recorder.Judged.Select(result => result.DummyDifficulty), Is.EqualTo(recorder.Cues.Select(cue => cue.DummyDifficulty)));
        }

        [Test]
        public void AStillDummysCueHasNoDifficulty()
        {
            var runner = new DrillRunner(new FakeClock(), new Random(4));
            var recorder = new RunnerRecorder(runner);

            runner.Start(TestDrills.Create(), stripLengthMeters: 3.0f, shrinkDrillsToFit: false);

            Assert.That(recorder.Cues.Single().DummyDifficulty, Is.Null);
        }

        [Test]
        public void TheArmedDummyCannotKeepItsDistanceYet()
        {
            Assert.Throws<ArgumentException>(() => CreateDrill(SuccessRule.Parry, CueType.ArmedDummyAttack));
            Assert.Throws<ArgumentException>(() => CreateDrill(SuccessRule.BeatThenTouch, CueType.BladeInLine));
        }

        [Test]
        public void ADummyMovedPerRepCannotAlsoKeepItsDistance()
        {
            Assert.Throws<ArgumentException>(() => CreateDrill(repDistances: new[] { DummyDistance.Extension, DummyDistance.Lunge }));
        }

        [Test]
        public void KeepingDistanceNeedsADifficulty()
        {
            Assert.Throws<ArgumentException>(() => CreateDrill(difficulties: Array.Empty<DummyDifficulty>()));
            Assert.That(CreateDrill().KeepsDistance, Is.True);
        }

        [Test]
        public void TheRepLogRecordsTheDifficulty()
        {
            using var folder = new ScratchFolder();
            var path = folder.File(RepLogFile.FileName);
            var result = new RepResult(
                DrillCatalog.DistanceKeeperTestDrillId, 2, TargetZone.Body, RepOutcome.Success, TargetZone.Body, reactionSeconds: 3.1,
                tipSpeedMetersPerSecond: 2.0f, distanceFromZoneCenterMeters: 0.03f, isShrunk: false, dummyDistance: DummyDistance.Lunge,
                dummyDifficulty: DummyDifficulty.Hard);

            RepLogFile.Append(path, RepLogEntry.FromRepResult(result, SessionTestData.Noon, SessionTestData.SessionId, flagged: false, armPose: null));
            var entry = RepLogFile.Read(path).Items.Single();

            Assert.That(entry.DummyDifficulty, Is.EqualTo(DummyDifficulty.Hard));
            Assert.That(entry.WithFlagged(true).DummyDifficulty, Is.EqualTo(DummyDifficulty.Hard), "The flag keeps it.");
            Assert.That(File.ReadAllText(path), Does.Contain("\"dummyDifficulty\":\"Hard\""));
        }
    }
}
