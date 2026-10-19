// The Dummy's distance from the Fencer's measured reach (PER-92): Calibrate grip measures shoulder to point, and the
// Extension distance (and the presets built on it) follow it; until then, or for a bad measurement, height decides.
using NUnit.Framework;
using VrFencing.Dummy;

namespace VrFencing.Tests.Dummy
{
    public class MeasuredReachTests
    {
        private const float Tolerance = 1e-4f;
        private const float Height = 1.65f;

        [TestCase(DummyDistance.Extension)]
        [TestCase(DummyDistance.Lunge)]
        [TestCase(DummyDistance.StepLunge)]
        public void WithoutAMeasuredReachTheHeightBasedReachIsUsed(DummyDistance preset)
        {
            var heightBased = DistancePresets.GetHeightBasedReachMeters(Height);

            Assert.That(
                DistancePresets.GetDistanceMeters(preset, Height),
                Is.EqualTo(DistancePresets.GetDistanceMeters(preset, Height, heightBased)).Within(Tolerance));
        }

        [TestCase(DummyDistance.Extension)]
        [TestCase(DummyDistance.Lunge)]
        [TestCase(DummyDistance.StepLunge)]
        public void ALongerReachStandsTheDummyFartherByTheSameAmount(DummyDistance preset)
        {
            var heightBased = DistancePresets.GetHeightBasedReachMeters(Height);
            const float longer = 0.12f;

            var fromHeight = DistancePresets.GetDistanceMeters(preset, Height);
            var fromReach = DistancePresets.GetDistanceMeters(preset, Height, heightBased + longer);

            Assert.That(fromReach - fromHeight, Is.EqualTo(longer).Within(Tolerance));
        }

        [Test]
        public void TheHeightBasedReachIsTheArmToTheGripPlusTheBlade()
        {
            // Upper arm 0.186, forearm 0.146 and half the hand 0.054 of height, then a 90 cm blade.
            Assert.That(DistancePresets.GetHeightBasedReachMeters(Height), Is.EqualTo((0.386f * Height) + 0.90f).Within(Tolerance));
        }

        [TestCase(0.5f)]
        [TestCase(3.0f)]
        [TestCase(float.NaN)]
        public void AnImplausibleReachIsIgnored(float reach)
        {
            Assert.That(DistancePresets.IsPlausibleReach(reach), Is.False);
            Assert.That(
                DistancePresets.GetDistanceMeters(DummyDistance.Extension, Height, reach),
                Is.EqualTo(DistancePresets.GetDistanceMeters(DummyDistance.Extension, Height)).Within(Tolerance));
        }

        [TestCase(1.2f)]
        [TestCase(2.2f)]
        public void EveryHeightTheSettingsAllowHasAPlausibleHeightBasedReach(float height)
        {
            Assert.That(DistancePresets.IsPlausibleReach(DistancePresets.GetHeightBasedReachMeters(height)), Is.True);
        }
    }
}
