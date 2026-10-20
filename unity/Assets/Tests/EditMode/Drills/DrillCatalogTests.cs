// Drills 1 to 3 (PER-38): point control, lunge to target and reaction, as registered in the Drill catalog,
// plus the runner pieces they rely on (the Started event, reaction time from the lit cue).
using System;
using System.Linq;
using NUnit.Framework;
using VrFencing.Drills;
using VrFencing.Dummy;
using VrFencing.Settings;
using VrFencing.Strip;

namespace VrFencing.Tests.Drills
{
    public class DrillCatalogTests
    {
        private static readonly string[] DrillOneToThreeIds =
        {
            DrillCatalog.PointControlDrillId, DrillCatalog.LungeToTargetDrillId, DrillCatalog.ReactionDrillId,
        };

        private static readonly string[] AllRealDrillIds = DrillOneToThreeIds
            .Concat(new[] { DrillCatalog.DistanceChoiceDrillId, DrillCatalog.CompoundDrillId, DrillCatalog.FootworkCallsDrillId })
            .Concat(new[] { DrillCatalog.SlowParryDrillId, DrillCatalog.FastParryDrillId, DrillCatalog.LowParryDrillId })
            .ToArray();

        private static DrillDefinition Build(string id, TrainerSettings settings = null)
        {
            return DrillCatalog.CreateDefault(includeTestDrills: false).BuildAll(settings ?? new TrainerSettings()).Single(drill => drill.Id == id);
        }

        // The shortest Strip anyone can place: 1 m from the En Garde Line, the smallest Fencer, the smallest Warning Zone.
        private static float ShortestStripLengthMeters()
        {
            var warningZone = new WarningZoneSettings { MinimumLengthMeters = 0.2f };
            return StripGeometry.Create(StripGeometry.MinimumPlacedLengthMeters, fencerHeightMeters: 1.2f, warningZone).LengthMeters;
        }

        [Test]
        public void ReleaseBuildsListDrillsOneToNineEasyToHard()
        {
            var drills = DrillCatalog.CreateDefault(includeTestDrills: false).BuildAll(new TrainerSettings());

            Assert.That(drills.OrderBy(drill => drill.Order).Select(drill => drill.Id), Is.EqualTo(AllRealDrillIds));
            Assert.That(drills.Select(drill => drill.Order), Is.EqualTo(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }));
        }

        [Test]
        public void TestDrillsSortAfterTheRealDrills()
        {
            var drills = DrillCatalog.CreateDefault(includeTestDrills: true).BuildAll(new TrainerSettings());

            var lastReal = drills.Where(drill => AllRealDrillIds.Contains(drill.Id)).Max(drill => drill.Order);
            var firstTest = drills.Where(drill => !AllRealDrillIds.Contains(drill.Id)).Min(drill => drill.Order);
            Assert.That(firstTest, Is.GreaterThan(lastReal));
        }

        [Test]
        public void DrillsOneToThreeTakeRepsAndRestFromTheSettingsAndCueEveryTargetZone()
        {
            var settings = new TrainerSettings { RepsPerDrill = 7, RestBetweenRepsSeconds = 4.5f };
            var allZones = (TargetZone[])Enum.GetValues(typeof(TargetZone));

            foreach (var id in DrillOneToThreeIds)
            {
                var drill = Build(id, settings);
                Assert.That(drill.RepCount, Is.EqualTo(7), id);
                Assert.That(drill.RestSeconds, Is.EqualTo(4.5f), id);
                Assert.That(drill.TargetZones, Is.EquivalentTo(allZones), id);
                Assert.That(drill.SuccessRule, Is.EqualTo(SuccessRule.PointFirstTouchOnTargetZone), id);
            }
        }

        [Test]
        public void DrillsOneToThreeOpenTheWindowWithTheCue()
        {
            foreach (var id in DrillOneToThreeIds)
            {
                Assert.That(Build(id).CueSeconds, Is.EqualTo(0.0f), $"{id}: reaction time must run from the cue.");
            }
        }

        [Test]
        public void PointControlNamesAZoneAtExtensionDistanceOnTheShortestStrip()
        {
            var drill = Build(DrillCatalog.PointControlDrillId);

            Assert.That(drill.CueType, Is.EqualTo(CueType.NamedTargetZone));
            Assert.That(drill.DummyDistance, Is.EqualTo(DummyDistance.Extension));
            Assert.That(drill.WindowSeconds, Is.EqualTo(DrillCatalog.NamedZoneWindowSeconds));
            var availability = DrillAvailability.Check(drill, ShortestStripLengthMeters(), shrinkDrillsToFit: false);
            Assert.That(availability.IsAvailable, Is.True);
            Assert.That(availability.IsShrunk, Is.False);
        }

        [Test]
        public void LungeToTargetNamesAZoneAtLungeDistanceAndNeedsLungeRoom()
        {
            var drill = Build(DrillCatalog.LungeToTargetDrillId);

            Assert.That(drill.CueType, Is.EqualTo(CueType.NamedTargetZone));
            Assert.That(drill.DummyDistance, Is.EqualTo(DummyDistance.Lunge));
            Assert.That(drill.WindowSeconds, Is.EqualTo(DrillCatalog.NamedZoneWindowSeconds));
            Assert.That(drill.RequiredStripLengthMeters, Is.EqualTo(2.5f));
            Assert.That(DrillAvailability.Check(drill, stripLengthMeters: 2.0f, shrinkDrillsToFit: true).IsAvailable, Is.False,
                "A lunge needs its room; there is no shortened variant.");
            Assert.That(DrillAvailability.Check(drill, stripLengthMeters: 2.5f, shrinkDrillsToFit: false).IsAvailable, Is.True);
        }

        [Test]
        public void ReactionLightsAZoneForTheReactionWindowAtLungeDistance()
        {
            var settings = new TrainerSettings();
            settings.TimeWindows.ReactionSeconds = 1.2f;

            var drill = Build(DrillCatalog.ReactionDrillId, settings);

            Assert.That(drill.CueType, Is.EqualTo(CueType.LitTargetZone));
            Assert.That(drill.WindowSeconds, Is.EqualTo(1.2f));
            Assert.That(drill.RequiredStripLengthMeters, Is.EqualTo(2.5f));
            Assert.That(drill.GetDummyDistance(isShrunk: false), Is.EqualTo(DummyDistance.Lunge));
        }

        [Test]
        public void ShortenedReactionStandsTheDummyAtExtensionDistanceOnTheShortestStrip()
        {
            var drill = Build(DrillCatalog.ReactionDrillId);

            var availability = DrillAvailability.Check(drill, ShortestStripLengthMeters(), shrinkDrillsToFit: true);

            Assert.That(availability.IsAvailable, Is.True);
            Assert.That(availability.IsShrunk, Is.True);
            Assert.That(drill.GetDummyDistance(availability.IsShrunk), Is.EqualTo(DummyDistance.Extension));
            Assert.That(DrillAvailability.Check(drill, ShortestStripLengthMeters(), shrinkDrillsToFit: false).IsAvailable, Is.False);
        }

        [Test]
        public void ReactionTimeRunsFromTheLitCueToTheTouch()
        {
            var clock = new FakeClock();
            var runner = new DrillRunner(clock, new Random(7));
            var recorder = new RunnerRecorder(runner);
            runner.Start(Build(DrillCatalog.ReactionDrillId), stripLengthMeters: 4.0f, shrinkDrillsToFit: false);

            clock.Advance(0.8);
            runner.Tick();
            runner.ReportContact(TestDrills.PointOn(runner.CurrentTargetZone.Value));

            Assert.That(recorder.Cues.Single().CueType, Is.EqualTo(CueType.LitTargetZone));
            Assert.That(recorder.Judged.Single().Outcome, Is.EqualTo(RepOutcome.Success));
            Assert.That(recorder.Judged.Single().ReactionSeconds, Is.EqualTo(0.8).Within(1e-9));
        }

        [Test]
        public void TheLitZoneGoesDarkAfterTheReactionWindowAsATimeout()
        {
            var clock = new FakeClock();
            var runner = new DrillRunner(clock, new Random(7));
            var recorder = new RunnerRecorder(runner);
            runner.Start(Build(DrillCatalog.ReactionDrillId), stripLengthMeters: 4.0f, shrinkDrillsToFit: false);

            clock.Advance(1.49);
            runner.Tick();
            Assert.That(runner.State, Is.EqualTo(DrillState.Window));

            clock.Advance(0.02);
            runner.Tick();
            Assert.That(recorder.Judged.Single().Outcome, Is.EqualTo(RepOutcome.Timeout));
            Assert.That(runner.State, Is.EqualTo(DrillState.Rest));
        }

        [Test]
        public void StartedIsRaisedBeforeTheFirstCueWithTheVariantKnown()
        {
            var runner = new DrillRunner(new FakeClock(), new Random(7));
            var recorder = new RunnerRecorder(runner);
            int? cuesAtStart = null;
            bool? wasShrunkAtStart = null;
            string drillAtStart = null;
            runner.Started += () =>
            {
                cuesAtStart = recorder.Cues.Count;
                wasShrunkAtStart = runner.IsShrunk;
                drillAtStart = runner.Drill.Id;
            };

            runner.Start(Build(DrillCatalog.ReactionDrillId), ShortestStripLengthMeters(), shrinkDrillsToFit: true);

            Assert.That(cuesAtStart, Is.EqualTo(0));
            Assert.That(recorder.Cues, Has.Count.EqualTo(1));
            Assert.That(wasShrunkAtStart, Is.True);
            Assert.That(drillAtStart, Is.EqualTo(DrillCatalog.ReactionDrillId));
        }

        [Test]
        public void StartedIsNotRaisedForADrillThatDoesNotFit()
        {
            var runner = new DrillRunner(new FakeClock(), new Random(7));
            var starts = 0;
            runner.Started += () => starts++;

            runner.Start(Build(DrillCatalog.LungeToTargetDrillId), stripLengthMeters: 2.0f, shrinkDrillsToFit: true);

            Assert.That(starts, Is.EqualTo(0));
        }
    }
}
