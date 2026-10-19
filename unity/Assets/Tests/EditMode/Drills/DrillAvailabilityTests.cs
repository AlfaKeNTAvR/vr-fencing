// Strip-length gating and shrink mode (PER-34, Strip decision PER-24): a Drill longer than the Strip is
// unavailable with the required length reported; in shrink mode its shortened variant runs instead.
using NUnit.Framework;
using VrFencing.Drills;

namespace VrFencing.Tests.Drills
{
    public class DrillAvailabilityTests
    {
        [Test]
        public void LongEnoughStripRunsTheFullDrill()
        {
            var availability = DrillAvailability.Check(TestDrills.Create(), stripLengthMeters: 4.0f, shrinkDrillsToFit: false);

            Assert.That(availability.IsAvailable, Is.True);
            Assert.That(availability.IsShrunk, Is.False);
        }

        [Test]
        public void StripOfExactlyTheRequiredLengthIsEnough()
        {
            var availability = DrillAvailability.Check(TestDrills.Create(), TestDrills.RequiredLength, shrinkDrillsToFit: false);

            Assert.That(availability.IsAvailable, Is.True);
        }

        [Test]
        public void LongEnoughStripRunsTheFullDrillEvenInShrinkMode()
        {
            var availability = DrillAvailability.Check(TestDrills.Create(), stripLengthMeters: 4.0f, shrinkDrillsToFit: true);

            Assert.That(availability.IsAvailable, Is.True);
            Assert.That(availability.IsShrunk, Is.False);
        }

        [Test]
        public void ShortStripMakesTheDrillUnavailableAndReportsTheRequiredLength()
        {
            var availability = DrillAvailability.Check(TestDrills.Create(), stripLengthMeters: 2.0f, shrinkDrillsToFit: false);

            Assert.That(availability.IsAvailable, Is.False);
            Assert.That(availability.RequiredStripLengthMeters, Is.EqualTo(TestDrills.RequiredLength));
        }

        [Test]
        public void ShrinkModeRunsTheShortenedVariantOnAShortStrip()
        {
            var availability = DrillAvailability.Check(TestDrills.Create(), stripLengthMeters: 2.0f, shrinkDrillsToFit: true);

            Assert.That(availability.IsAvailable, Is.True);
            Assert.That(availability.IsShrunk, Is.True);
        }

        [Test]
        public void ShrinkModeStillRefusesAStripShorterThanTheShortenedVariant()
        {
            var availability = DrillAvailability.Check(TestDrills.Create(), stripLengthMeters: 1.0f, shrinkDrillsToFit: true);

            Assert.That(availability.IsAvailable, Is.False);
            Assert.That(availability.RequiredStripLengthMeters, Is.EqualTo(TestDrills.ShrunkLength));
        }

        [Test]
        public void ShrinkModeCannotHelpADrillWithoutAShortenedVariant()
        {
            var drill = TestDrills.Create(shrunkLength: null);

            var availability = DrillAvailability.Check(drill, stripLengthMeters: 2.0f, shrinkDrillsToFit: true);

            Assert.That(availability.IsAvailable, Is.False);
            Assert.That(availability.RequiredStripLengthMeters, Is.EqualTo(TestDrills.RequiredLength));
        }
    }
}
