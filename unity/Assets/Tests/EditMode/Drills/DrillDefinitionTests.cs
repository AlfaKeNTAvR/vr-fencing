// Drill definitions as data (PER-34): impossible Drills are rejected up front, valid ones keep their values.
using System;
using NUnit.Framework;
using VrFencing.Drills;
using VrFencing.Dummy;

namespace VrFencing.Tests.Drills
{
    public class DrillDefinitionTests
    {
        [Test]
        public void ValidDefinitionKeepsItsValues()
        {
            var drill = TestDrills.Create(repCount: 10, zones: new[] { TargetZone.Hand, TargetZone.Body });

            Assert.That(drill.Id, Is.EqualTo("lunge-to-target"));
            Assert.That(drill.Order, Is.EqualTo(2));
            Assert.That(drill.RequiredStripLengthMeters, Is.EqualTo(TestDrills.RequiredLength));
            Assert.That(drill.ShrunkStripLengthMeters, Is.EqualTo(TestDrills.ShrunkLength));
            Assert.That(drill.RepCount, Is.EqualTo(10));
            Assert.That(drill.WindowSeconds, Is.EqualTo(TestDrills.WindowSeconds));
            Assert.That(drill.TargetZones, Is.EqualTo(new[] { TargetZone.Hand, TargetZone.Body }));
        }

        [Test]
        public void TargetZonesAreCopiedSoTheCallerCannotChangeThemLater()
        {
            var zones = new[] { TargetZone.Hand };
            var drill = TestDrills.Create(zones: zones);

            zones[0] = TargetZone.Foot;

            Assert.That(drill.TargetZones[0], Is.EqualTo(TargetZone.Hand));
        }

        [Test]
        public void RejectsZeroReps()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => TestDrills.Create(repCount: 0));
        }

        [Test]
        public void RejectsNoTargetZones()
        {
            Assert.Throws<ArgumentException>(() => TestDrills.Create(zones: new TargetZone[0]));
        }

        [Test]
        public void RejectsNegativeCueOrRest()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => TestDrills.Create(cueSeconds: -0.5f));
            Assert.Throws<ArgumentOutOfRangeException>(() => TestDrills.Create(restSeconds: -1.0f));
        }

        [Test]
        public void RejectsAShrunkLengthThatIsNotShorterThanTheFullLength()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => TestDrills.Create(shrunkLength: TestDrills.RequiredLength));
            Assert.Throws<ArgumentOutOfRangeException>(() => TestDrills.Create(shrunkLength: 0.0f));
        }

        [Test]
        public void RejectsNonPositiveWindowAndStripLength()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new DrillDefinition(
                "d", "Drill", 1, 1.0f, null, 1, 0.0f, windowSeconds: 0.0f, 0.0f,
                CueType.NamedTargetZone, SuccessRule.PointFirstTouchOnTargetZone, new[] { TargetZone.Body }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DrillDefinition(
                "d", "Drill", 1, requiredStripLengthMeters: 0.0f, null, 1, 0.0f, 1.0f, 0.0f,
                CueType.NamedTargetZone, SuccessRule.PointFirstTouchOnTargetZone, new[] { TargetZone.Body }));
        }

        [Test]
        public void TheShortenedVariantKeepsTheDummyDistanceUnlessItSetsItsOwn()
        {
            var sameDistance = new DrillDefinition(
                "d", "Drill", 1, 2.5f, shrunkStripLengthMeters: 1.0f, 1, 0.0f, 1.0f, 0.0f,
                CueType.LitTargetZone, SuccessRule.PointFirstTouchOnTargetZone, new[] { TargetZone.Body },
                DummyDistance.StepLunge);
            var nearer = new DrillDefinition(
                "d", "Drill", 1, 2.5f, shrunkStripLengthMeters: 1.0f, 1, 0.0f, 1.0f, 0.0f,
                CueType.LitTargetZone, SuccessRule.PointFirstTouchOnTargetZone, new[] { TargetZone.Body },
                DummyDistance.Lunge, shrunkDummyDistance: DummyDistance.Extension);

            Assert.That(sameDistance.GetDummyDistance(isShrunk: false), Is.EqualTo(DummyDistance.StepLunge));
            Assert.That(sameDistance.GetDummyDistance(isShrunk: true), Is.EqualTo(DummyDistance.StepLunge));
            Assert.That(nearer.GetDummyDistance(isShrunk: false), Is.EqualTo(DummyDistance.Lunge));
            Assert.That(nearer.GetDummyDistance(isShrunk: true), Is.EqualTo(DummyDistance.Extension));
        }

        [Test]
        public void DefaultsToLungeDistanceAsTheSceneDoes()
        {
            Assert.That(TestDrills.Create().DummyDistance, Is.EqualTo(DummyDistance.Lunge));
        }

        [Test]
        public void RejectsAShortenedDummyDistanceWithoutAShortenedVariant()
        {
            Assert.Throws<ArgumentException>(() => new DrillDefinition(
                "d", "Drill", 1, 2.5f, shrunkStripLengthMeters: null, 1, 0.0f, 1.0f, 0.0f,
                CueType.LitTargetZone, SuccessRule.PointFirstTouchOnTargetZone, new[] { TargetZone.Body },
                DummyDistance.Lunge, shrunkDummyDistance: DummyDistance.Extension));
        }
    }
}
