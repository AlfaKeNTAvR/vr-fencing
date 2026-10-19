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

        private static readonly float HeadAtMarkZ = StartMark.GetHeadZ(Height);

        // Feeds the watch frames from startSeconds for durationSeconds, the head moving along the Strip at speed from startZ.
        private static void Feed(StartMarkWatch watch, double startSeconds, double durationSeconds, float startZ, float speed = 0.0f)
        {
            for (var t = 0.0; t <= durationSeconds; t += FrameSeconds)
            {
                watch.AddSample(new HeadSample(startSeconds + t, new Vector3(0.0f, HeadHeight, startZ + (float)(speed * t))));
            }
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

            Assert.That(watch.IsBackAndStill(Height), Is.True);
        }

        [Test]
        public void StandingStillWithinFifteenCentimetersIsBack()
        {
            var watch = new StartMarkWatch();
            Feed(watch, 0.0, 0.5, HeadAtMarkZ + 0.14f);

            Assert.That(watch.IsBackAndStill(Height), Is.True);
        }

        [Test]
        public void StandingStillFartherAwayIsNotBack()
        {
            var ahead = new StartMarkWatch();
            Feed(ahead, 0.0, 0.5, HeadAtMarkZ + 0.16f);
            var behind = new StartMarkWatch();
            Feed(behind, 0.0, 0.5, HeadAtMarkZ - 0.16f);

            Assert.That(ahead.IsBackAndStill(Height), Is.False);
            Assert.That(behind.IsBackAndStill(Height), Is.False);
        }

        [Test]
        public void WalkingBackThroughTheMarkIsNotBackUntilStill()
        {
            // A recovery moving back at 0.5 m/s passes the mark; only once it stops there does it count.
            var watch = new StartMarkWatch();
            Feed(watch, 0.0, 0.4, HeadAtMarkZ + 0.1f, speed: -0.5f);
            Assert.That(watch.IsBackAndStill(Height), Is.False);

            Feed(watch, 0.42, 0.2, HeadAtMarkZ - 0.1f);
            Assert.That(watch.IsBackAndStill(Height), Is.False, "Still for only 0.2 s.");

            Feed(watch, 0.64, 0.3, HeadAtMarkZ - 0.1f);
            Assert.That(watch.IsBackAndStill(Height), Is.True);
        }

        [Test]
        public void TooFewSamplesAreNotBack()
        {
            var watch = new StartMarkWatch();
            Feed(watch, 0.0, 0.1, HeadAtMarkZ);

            Assert.That(watch.IsBackAndStill(Height), Is.False);
        }

        [Test]
        public void ClearingForgetsTheHead()
        {
            var watch = new StartMarkWatch();
            Feed(watch, 0.0, 0.5, HeadAtMarkZ);

            watch.Clear();

            Assert.That(watch.IsBackAndStill(Height), Is.False);
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
