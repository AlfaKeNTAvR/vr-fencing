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
        public void ASpringBackStartsFromTheHeldBendAndSettles()
        {
            var flex = new BladeFlex();
            var blade = AlongZAt(0.0f);

            flex.SpringBackFrom(blade, new Vector3(0.0f, 0.05f, 0.03f));

            Assert.That(flex.Bend.Y, Is.EqualTo(0.05f).Within(1e-6f));
            Assert.That(flex.Bend.Z, Is.EqualTo(0.0f).Within(1e-6f), "Only the part across the blade is kept.");
            Assert.That(flex.GetCenterlinePoint(blade, 0.0f), Is.EqualTo(blade.GuardPosition));
            Assert.That(Vector3.Distance(flex.GetCenterlinePoint(blade, 1.0f), blade.TipPosition + flex.Bend), Is.LessThan(1e-6f));

            var crossedStraight = false;
            for (var frame = 0; frame < 72; frame++)
            {
                flex.Step(blade, FrameSeconds);
                crossedStraight |= flex.Bend.Y < 0.0f;
            }

            Assert.That(crossedStraight, Is.True, "A released blade springs past straight once.");
            Assert.That(flex.Bend.Length(), Is.LessThan(0.002f));
        }

        [Test]
        public void ASpringBackNeverBendsMoreThanTheMaximum()
        {
            var flex = new BladeFlex();

            flex.SpringBackFrom(AlongZAt(0.0f), new Vector3(0.3f, 0.0f, 0.0f));

            Assert.That(flex.Bend.Length(), Is.EqualTo(BladeFlex.MaxBendMeters).Within(1e-6f));
        }

        [Test]
        public void AStillHandWithTrackingJitterKeepsThePointStill()
        {
            // About 7 mm of tip jitter (PER-48): every frame the tracked blade is off by up to 3.5 mm on each axis, plus
            // an 8 Hz hand tremor of 3.5 mm. The old lightly damped spring bent about as much as the jitter.
            var flex = new BladeFlex();
            var random = new System.Random(48);
            var maxBend = 0.0f;
            for (var frame = 0; frame < 5 * 72; frame++)
            {
                var tremor = 0.0035f * (float)System.Math.Sin(2.0 * System.Math.PI * 8.0 * frame / 72.0);
                var jitter = new Vector3(NextJitter(random), NextJitter(random), NextJitter(random)) + new Vector3(tremor, tremor, 0.0f);
                var blade = new BladeSegment(new Vector3(0.0f, 1.0f, 0.0f) + (jitter * 0.3f), new Vector3(0.0f, 1.0f, 0.9f) + jitter);
                flex.Step(blade, FrameSeconds);
                maxBend = System.Math.Max(maxBend, flex.Bend.Length());
            }

            Assert.That(maxBend, Is.LessThan(0.0005f));
        }

        [Test]
        public void SlowAimingDoesNotBendTheBlade()
        {
            // Moving the point 10 cm at 0.2 m/s and stopping: aiming, not a fast move.
            var flex = new BladeFlex();
            var maxBend = 0.0f;
            var x = 0.0f;
            for (var frame = 0; frame < 72; frame++)
            {
                flex.Step(AlongZAt(System.Math.Min(x, 0.1f)), FrameSeconds);
                maxBend = System.Math.Max(maxBend, flex.Bend.Length());
                x += 0.2f * FrameSeconds;
            }

            Assert.That(maxBend, Is.LessThan(0.0005f));
        }

        private static float NextJitter(System.Random random)
        {
            return (float)((random.NextDouble() * 2.0) - 1.0) * 0.0035f;
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
