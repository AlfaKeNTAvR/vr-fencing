// The Start Mark watch (PER-89): is the Fencer's head back at its en garde place at the mark, and still? 72 Hz frames
// for a 1.65 m Fencer, whose head is 0.18 m behind the mark in en garde.
using System.Numerics;
using NUnit.Framework;
using VrFencing.Tracking;

namespace VrFencing.Tests.Tracking
{
    public class StartMarkTests
    {
        private const float Height = 1.65f;
        private const float HeadHeight = 1.45f;
        private const double FrameSeconds = 1.0 / 72.0;

        // The Setting's default (PER-117): 45 cm either side of the Strip's center line.
        private const float SideTolerance = 0.45f;

        private static readonly float HeadAtMarkZ = StartMark.GetHeadZ(Height);

        // Feeds the watch frames from startSeconds for durationSeconds, the head moving along the Strip at speed from startZ,
        // sideX across the Strip from its center line.
        private static void Feed(StartMarkWatch watch, double startSeconds, double durationSeconds, float startZ, float speed = 0.0f, float sideX = 0.0f)
        {
            for (var t = 0.0; t <= durationSeconds; t += FrameSeconds)
            {
                watch.AddSample(new HeadSample(startSeconds + t, new Vector3(sideX, HeadHeight, startZ + (float)(speed * t))));
            }
        }

        [Test]
        public void StandingStillOffTheCenterLineWithinTheSideToleranceIsBack()
        {
            var left = new StartMarkWatch();
            Feed(left, 0.0, 0.5, HeadAtMarkZ, sideX: -0.44f);
            var right = new StartMarkWatch();
            Feed(right, 0.0, 0.5, HeadAtMarkZ, sideX: 0.44f);

            Assert.That(left.IsBackAndStill(Height, SideTolerance), Is.True);
            Assert.That(right.IsBackAndStill(Height, SideTolerance), Is.True);
        }

        [Test]
        public void StandingStillFartherToTheSideIsNotBack()
        {
            // Near the Strip's left edge (75 cm from the center line), as in Nikita's 11:13 video at 36 to 42 s.
            var watch = new StartMarkWatch();
            Feed(watch, 0.0, 0.5, HeadAtMarkZ, sideX: -0.6f);

            Assert.That(watch.IsBackAndStill(Height, SideTolerance), Is.False);
            Assert.That(watch.IsBackAndStill(Height, 0.65f), Is.True, "A wider side tolerance takes it.");
        }

        [Test]
        public void TheSideToleranceDoesNotLoosenTheCheckAlongTheStrip()
        {
            var watch = new StartMarkWatch();
            Feed(watch, 0.0, 0.5, HeadAtMarkZ + 0.16f);

            Assert.That(watch.IsBackAndStill(Height, 0.75f), Is.False);
        }

        [Test]
        public void ANegativeSideToleranceIsRefused()
        {
            var watch = new StartMarkWatch();

            Assert.That(() => watch.IsBackAndStill(Height, -0.1f), Throws.TypeOf<System.ArgumentOutOfRangeException>());
        }

        [Test]
        public void TheSideOffsetIsFromTheCenterLine()
        {
            var watch = new StartMarkWatch();
            Assert.That(watch.MeasureSideOffsetMeters(), Is.Null);

            Feed(watch, 0.0, 0.1, HeadAtMarkZ, sideX: -0.3f);

            Assert.That(watch.MeasureSideOffsetMeters(), Is.EqualTo(-0.3f).Within(1e-4f));
        }

        [Test]
        public void TheHeadSitsBehindTheMarkInEnGarde()
        {
            Assert.That(HeadAtMarkZ, Is.EqualTo(-0.18f).Within(0.01f));
        }

        [Test]
        public void StandingStillAtTheMarkIsBack()
        {
            var watch = new StartMarkWatch();
            Feed(watch, 0.0, 0.5, HeadAtMarkZ);

            Assert.That(watch.IsBackAndStill(Height, SideTolerance), Is.True);
        }

        [Test]
        public void StandingStillWithinFifteenCentimetersIsBack()
        {
            var watch = new StartMarkWatch();
            Feed(watch, 0.0, 0.5, HeadAtMarkZ + 0.14f);

            Assert.That(watch.IsBackAndStill(Height, SideTolerance), Is.True);
        }

        [Test]
        public void StandingStillFartherAwayIsNotBack()
        {
            var ahead = new StartMarkWatch();
            Feed(ahead, 0.0, 0.5, HeadAtMarkZ + 0.16f);
            var behind = new StartMarkWatch();
            Feed(behind, 0.0, 0.5, HeadAtMarkZ - 0.16f);

            Assert.That(ahead.IsBackAndStill(Height, SideTolerance), Is.False);
            Assert.That(behind.IsBackAndStill(Height, SideTolerance), Is.False);
        }

        [Test]
        public void WalkingBackThroughTheMarkIsNotBackUntilStill()
        {
            // A recovery moving back at 0.5 m/s passes the mark; only once it stops there does it count.
            var watch = new StartMarkWatch();
            Feed(watch, 0.0, 0.4, HeadAtMarkZ + 0.1f, speed: -0.5f);
            Assert.That(watch.IsBackAndStill(Height, SideTolerance), Is.False);

            Feed(watch, 0.42, 0.2, HeadAtMarkZ - 0.1f);
            Assert.That(watch.IsBackAndStill(Height, SideTolerance), Is.False, "Still for only 0.2 s.");

            Feed(watch, 0.64, 0.3, HeadAtMarkZ - 0.1f);
            Assert.That(watch.IsBackAndStill(Height, SideTolerance), Is.True);
        }

        [Test]
        public void TooFewSamplesAreNotBack()
        {
            var watch = new StartMarkWatch();
            Feed(watch, 0.0, 0.1, HeadAtMarkZ);

            Assert.That(watch.IsBackAndStill(Height, SideTolerance), Is.False);
        }

        [Test]
        public void ClearingForgetsTheHead()
        {
            var watch = new StartMarkWatch();
            Feed(watch, 0.0, 0.5, HeadAtMarkZ);

            watch.Clear();

            Assert.That(watch.IsBackAndStill(Height, SideTolerance), Is.False);
            Assert.That(watch.MeasureOffsetMeters(Height), Is.Null);
        }

        [Test]
        public void TheOffsetIsFromTheHeadsPlaceAtTheMark()
        {
            var watch = new StartMarkWatch();
            Feed(watch, 0.0, 0.1, HeadAtMarkZ + 0.4f);

            Assert.That(watch.MeasureOffsetMeters(Height), Is.EqualTo(0.4f).Within(1e-4f));
        }
    }
}
