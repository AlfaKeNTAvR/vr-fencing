// The Drill registry the picker lists from (PER-36), and the catalog's development-only test Drills.
using System;
using System.Linq;
using NUnit.Framework;
using VrFencing.Drills;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Tests.Drills
{
    public class DrillRegistryTests
    {
        [Test]
        public void BuildsEveryDrillForTheGivenSettings()
        {
            var registry = new DrillRegistry();
            registry.Register("lunge-to-target", settings => TestDrills.Create(repCount: settings.RepsPerDrill));

            var drills = registry.BuildAll(new TrainerSettings { RepsPerDrill = 7 });

            Assert.That(drills, Has.Count.EqualTo(1));
            Assert.That(drills[0].RepCount, Is.EqualTo(7));
        }

        [Test]
        public void RegisteringAnIdTwiceThrows()
        {
            var registry = new DrillRegistry();
            registry.Register("lunge-to-target", _ => TestDrills.Create());

            Assert.Throws<ArgumentException>(() => registry.Register("lunge-to-target", _ => TestDrills.Create()));
        }

        [Test]
        public void ABuilderThatBuildsAnotherIdThrows()
        {
            var registry = new DrillRegistry();
            registry.Register("something-else", _ => TestDrills.Create());

            Assert.Throws<InvalidOperationException>(() => registry.BuildAll(new TrainerSettings()));
        }

        [Test]
        public void TestDrillsAreListedOnlyWhenAskedFor()
        {
            var settings = new TrainerSettings();
            var withTests = DrillCatalog.CreateDefault(includeTestDrills: true).BuildAll(settings).Select(drill => drill.Id).ToArray();
            var withoutTests = DrillCatalog.CreateDefault(includeTestDrills: false).BuildAll(settings).Select(drill => drill.Id).ToArray();

            Assert.That(withTests, Does.Contain(DrillCatalog.FullPisteTestDrillId));
            Assert.That(withTests, Does.Contain(DrillCatalog.ArmedDummyTestDrillId));
            Assert.That(withoutTests, Does.Not.Contain(DrillCatalog.FullPisteTestDrillId));
            Assert.That(withoutTests, Does.Not.Contain(DrillCatalog.ArmedDummyTestDrillId));
            Assert.That(withTests, Does.Contain(DrillCatalog.DistanceKeeperTestDrillId));
            Assert.That(withoutTests, Does.Not.Contain(DrillCatalog.DistanceKeeperTestDrillId));
            Assert.That(withTests.Length, Is.EqualTo(withoutTests.Length + 3), "Only the full-piste, Armed Dummy and distance keeper test Drills are dev-only.");
        }

        [Test]
        public void TheArmedDummyTestDrillFitsEveryStripAndCuesTheBodyAtExtensionDistance()
        {
            var drill = DrillCatalog.CreateArmedDummyTestDrill(new TrainerSettings { RepsPerDrill = 6 });

            Assert.That(DrillAvailability.Check(drill, 1.0f, shrinkDrillsToFit: false).IsAvailable, Is.True);
            Assert.That(drill.DummyDistance, Is.EqualTo(DummyDistance.Extension));
            Assert.That(drill.TargetZones, Is.EqualTo(new[] { TargetZone.Body }));
            Assert.That(drill.RepCount, Is.EqualTo(6));
            Assert.That(drill.WindowSeconds, Is.GreaterThanOrEqualTo(6.0f), "Delay, wind-up, a slow thrust and the riposte must fit.");
            Assert.That(drill.WaitsAtStartMark(isShrunk: false), Is.False);
        }

        [Test]
        public void TheFullPisteTestDrillDoesNotFitAShortStrip()
        {
            var drill = DrillCatalog.CreateFullPisteTestDrill(new TrainerSettings());

            Assert.That(DrillAvailability.Check(drill, 5.0f, shrinkDrillsToFit: true).IsAvailable, Is.False);
            Assert.That(DrillAvailability.Check(drill, 14.0f, shrinkDrillsToFit: false).IsAvailable, Is.True);
        }
    }
}
