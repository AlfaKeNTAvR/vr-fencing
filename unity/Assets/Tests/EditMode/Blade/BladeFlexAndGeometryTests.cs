// Visual-only blade flex (bends a few cm on fast moves, springs back) and the segment and capsule geometry under the
// swept tests.
using System.Numerics;
using NUnit.Framework;
using VrFencing.Blade;
using VrFencing.Dummy;

namespace VrFencing.Tests.Blade
{
    public class BladeFlexTests
    {
        private const float FrameSeconds = 1.0f / 72.0f;

        [Test]
        public void AStillBladeStaysStraight()
        {
            var flex = new BladeFlex();
            var blade = new BladeSegment(Vector3.Zero, new Vector3(0.0f, 0.0f, 0.9f));

            for (var frame = 0; frame < 100; frame++)
            {
                flex.Step(blade, FrameSeconds);
            }

            Assert.That(flex.Bend.Length(), Is.LessThan(1e-6f));
        }

        [Test]
        public void ASharpSideMoveBendsTheTipAFewCentimetersAndItSpringsBack()
        {
            // The blade points along +z and sweeps sideways at 4 m/s for 0.15 s, then stops dead.
            var flex = new BladeFlex();
            var maxBend = 0.0f;
            var x = 0.0f;
            for (var frame = 0; frame < 11; frame++)
            {
                flex.Step(AlongZAt(x), FrameSeconds);
                maxBend = System.Math.Max(maxBend, flex.Bend.Length());
                x += 4.0f * FrameSeconds;
            }

            for (var frame = 0; frame < 15; frame++)
            {
                flex.Step(AlongZAt(x), FrameSeconds);
                maxBend = System.Math.Max(maxBend, flex.Bend.Length());
            }

            Assert.That(maxBend, Is.InRange(0.02f, BladeFlex.MaxBendMeters));
            Assert.That(System.Math.Abs(flex.Bend.Z), Is.LessThan(1e-4f), "The bend stays perpendicular to the blade.");

            for (var frame = 0; frame < 72; frame++)
            {
                flex.Step(AlongZAt(x), FrameSeconds);
            }

            Assert.That(flex.Bend.Length(), Is.LessThan(0.002f));
        }

        [Test]
        public void ATouchBowsTheBladeAndTheGuardStaysPut()
        {
            var flex = new BladeFlex();
            var blade = AlongZAt(0.0f);
            flex.Step(blade, FrameSeconds);

            flex.Kick(blade, Vector3.UnitY);
            var maxBend = 0.0f;
            for (var frame = 0; frame < 10; frame++)
            {
                flex.Step(blade, FrameSeconds);
                maxBend = System.Math.Max(maxBend, flex.Bend.Y);
            }

            Assert.That(maxBend, Is.InRange(0.03f, BladeFlex.MaxBendMeters));
            Assert.That(flex.GetCenterlinePoint(blade, 0.0f), Is.EqualTo(blade.GuardPosition));
            Assert.That(Vector3.Distance(flex.GetCenterlinePoint(blade, 1.0f), blade.TipPosition + flex.Bend), Is.LessThan(1e-6f));
        }

        private static BladeSegment AlongZAt(float x)
        {
            return new BladeSegment(new Vector3(x, 1.0f, 0.0f), new Vector3(x, 1.0f, 0.9f));
        }
    }

    public class SegmentMathTests
    {
        [Test]
        public void CapsuleEntryIsWhereThePathFirstReachesTheSurface()
        {
            var capsule = new Capsule(new Vector3(-0.2f, 0.0f, 1.0f), new Vector3(0.2f, 0.0f, 1.0f), 0.05f);

            var found = SegmentMath.TryFindCapsuleEntry(Vector3.Zero, new Vector3(0.0f, 0.0f, 2.0f), capsule, 0.0f, out var fraction);

            Assert.That(found, Is.True);
            Assert.That(fraction * 2.0f, Is.EqualTo(0.95f).Within(1e-4f));
        }

        [Test]
        public void CapsuleEntryFindsTheRoundedEnd()
        {
            var capsule = new Capsule(new Vector3(0.0f, 0.0f, 1.0f), new Vector3(0.0f, 0.0f, 1.5f), 0.05f);

            var found = SegmentMath.TryFindCapsuleEntry(Vector3.Zero, new Vector3(0.0f, 0.0f, 2.0f), capsule, 0.004f, out var fraction);

            Assert.That(found, Is.True);
            Assert.That(fraction * 2.0f, Is.EqualTo(1.0f - 0.054f).Within(1e-4f));
        }

        [Test]
        public void NoEntryWhenThePathMissesOrStartsInside()
        {
            var capsule = new Capsule(new Vector3(0.0f, 0.0f, 1.0f), new Vector3(0.0f, 0.0f, 1.0f), 0.05f);

            Assert.That(SegmentMath.TryFindCapsuleEntry(new Vector3(0.1f, 0.0f, 0.0f), new Vector3(0.1f, 0.0f, 2.0f), capsule, 0.0f, out _), Is.False);
            Assert.That(SegmentMath.TryFindCapsuleEntry(new Vector3(0.0f, 0.0f, 1.0f), new Vector3(0.0f, 0.0f, 2.0f), capsule, 0.0f, out _), Is.False);
            Assert.That(SegmentMath.TryFindCapsuleEntry(Vector3.Zero, new Vector3(0.0f, 0.0f, 0.9f), capsule, 0.0f, out _), Is.False);
        }

        [Test]
        public void DistanceBetweenCrossingAndParallelSegments()
        {
            var crossing = SegmentMath.FindDistanceBetweenSegments(
                new Vector3(-1.0f, 0.0f, 0.0f), new Vector3(1.0f, 0.0f, 0.0f), new Vector3(0.0f, -1.0f, 0.3f), new Vector3(0.0f, 1.0f, 0.3f), out var crossingFraction);
            var parallel = SegmentMath.FindDistanceBetweenSegments(
                Vector3.Zero, new Vector3(1.0f, 0.0f, 0.0f), new Vector3(2.0f, 0.2f, 0.0f), new Vector3(3.0f, 0.2f, 0.0f), out var parallelFraction);

            Assert.That(crossing, Is.EqualTo(0.3f).Within(1e-5f));
            Assert.That(crossingFraction, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(parallel, Is.EqualTo(new Vector2(1.0f, 0.2f).Length()).Within(1e-5f));
            Assert.That(parallelFraction, Is.EqualTo(1.0f));
        }
    }
}
