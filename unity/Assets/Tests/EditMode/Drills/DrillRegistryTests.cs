// The Drill registry the picker lists from (PER-36), and the catalog's development-only test Drills.
using System;
using System.Linq;
using NUnit.Framework;
using VrFencing.Drills;
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
            Assert.That(withoutTests, Does.Not.Contain(DrillCatalog.FullPisteTestDrillId));
            Assert.That(withTests.Length, Is.EqualTo(withoutTests.Length + 1), "Only the full-piste test Drill is dev-only.");
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
