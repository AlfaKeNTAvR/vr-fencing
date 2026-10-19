// Dummy distance presets (PER-32): extension, lunge and step-lunge distance, front foot to front foot.
using System;
using NUnit.Framework;
using VrFencing.Dummy;

namespace VrFencing.Tests.Dummy
{
    public class DistancePresetsTests
    {
        private const float Tolerance = 1e-4f;

        [TestCase(1.4f)]
        [TestCase(1.65f)]
        [TestCase(1.8f)]
        [TestCase(2.1f)]
        public void PresetsAreOrderedExtensionLungeStepLunge(float height)
        {
            var extension = DistancePresets.GetDistanceMeters(DummyDistance.Extension, height);
            var lunge = DistancePresets.GetDistanceMeters(DummyDistance.Lunge, height);
            var stepLunge = DistancePresets.GetDistanceMeters(DummyDistance.StepLunge, height);

            Assert.That(extension, Is.GreaterThan(0.0f));
            Assert.That(new[] { extension, lunge, stepLunge }, Is.Ordered.Ascending);
            Assert.That(lunge, Is.Not.EqualTo(extension));
            Assert.That(stepLunge, Is.Not.EqualTo(lunge));
        }

        [TestCase(DummyDistance.Extension)]
        [TestCase(DummyDistance.Lunge)]
        [TestCase(DummyDistance.StepLunge)]
        public void TallerFencersGetALongerDistance(DummyDistance preset)
        {
            Assert.That(
                DistancePresets.GetDistanceMeters(preset, 1.85f),
                Is.GreaterThan(DistancePresets.GetDistanceMeters(preset, 1.55f)));
        }

        [Test]
        public void LungeAddsOneLungeAndStepLungeOneAdvance()
        {
            const float height = 1.65f;
            var extension = DistancePresets.GetDistanceMeters(DummyDistance.Extension, height);
            var lunge = DistancePresets.GetDistanceMeters(DummyDistance.Lunge, height);
            var stepLunge = DistancePresets.GetDistanceMeters(DummyDistance.StepLunge, height);

            Assert.That(lunge - extension, Is.EqualTo(DistancePresets.LungeLengthPerHeight * height).Within(Tolerance));
            Assert.That(stepLunge - lunge, Is.EqualTo(DistancePresets.AdvanceLengthPerHeight * height).Within(Tolerance));
        }

        [Test]
        public void LungeDistanceFallsInTheRangeFromTheResearch()
        {
            // docs/research/epee-rules-for-sim.md: targets at lunge distance sit about 1.5-2.5 m from the front foot.
            var lunge = DistancePresets.GetDistanceMeters(DummyDistance.Lunge, 1.65f);

            Assert.That(lunge, Is.InRange(1.5f, 2.5f));
        }

        [TestCase(0.0f)]
        [TestCase(-1.0f)]
        [TestCase(float.NaN)]
        public void RejectsAHeightThatIsNotPositive(float height)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => DistancePresets.GetDistanceMeters(DummyDistance.Lunge, height));
        }
    }
}
