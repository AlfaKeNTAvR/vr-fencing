// The Fencer's blade meeting the Armed Dummy's (PER-40, prototype PER-95): the swept segment-to-segment contact,
// the clash event it raises (where on each blade, how hard). The Dummy's blade knocked off line is in
// BladeDeflectionTests.
// The clash buzz is in ClashHapticsTests.
using System;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Blade;

namespace VrFencing.Tests.Blade
{
    public class BladeClashTests
    {
        private const double FrameSeconds = 1.0 / 72.0;

        // The Dummy's blade lies along +z at chest height; its guard at z = 1.5, its point at z = 0.6 (toward the Fencer).
        private static readonly BladeSegment Opponent = new BladeSegment(new Vector3(0.0f, 1.0f, 1.5f), new Vector3(0.0f, 1.0f, 0.6f));

        /// <summary>The Fencer's blade along +x, crossing the line x = 0 at <paramref name="z"/>, shifted sideways by <paramref name="x"/>.</summary>
        private static BladeSegment Across(float x, float z = 0.8f, float guardX = -0.3f)
        {
            return new BladeSegment(new Vector3(guardX + x, 1.0f, z), new Vector3(guardX + x + 0.9f, 1.0f, z));
        }

        /// <summary>The Fencer's blade along +x at height <paramref name="y"/>, crossing above the Dummy's at z 0.78, guard 0.27 m to the left.</summary>
        private static BladeSegment Level(float y)
        {
            return new BladeSegment(new Vector3(-0.27f, y, 0.78f), new Vector3(0.63f, y, 0.78f));
        }

        /// <summary>The Fencer's blade pointing up and along the Strip, its guard below the Dummy's blade, moved sideways by <paramref name="x"/>.</summary>
        private static BladeSegment Rising(float x)
        {
            return new BladeSegment(new Vector3(x, 0.7f, 0.2f), new Vector3(x, 1.3f, 1.2f));
        }

        [Test]
        public void CrossingBladesClashOnceUntilTheyPart()
        {
            var detector = new BladeClashDetector();

            Assert.That(detector.Step(0.0, Across(0.0f), Opponent).HasValue, Is.True);
            Assert.That(detector.Step(FrameSeconds, Across(0.0f), Opponent).HasValue, Is.False, "One clash per contact.");
            Assert.That(detector.IsInContact, Is.True);
            Assert.That(detector.Step(2 * FrameSeconds, new BladeSegment(new Vector3(-0.3f, 1.02f, 0.8f), new Vector3(0.6f, 1.02f, 0.8f)), Opponent).HasValue,
                Is.False, "Still within the release distance.");

            Assert.That(detector.Step(3 * FrameSeconds, new BladeSegment(new Vector3(-0.3f, 1.1f, 0.8f), new Vector3(0.6f, 1.1f, 0.8f)), Opponent).HasValue, Is.False);
            Assert.That(detector.IsInContact, Is.False, "Parted.");
            Assert.That(detector.Step(4 * FrameSeconds, Across(0.0f), Opponent).HasValue, Is.True, "A new contact clashes again.");
        }

        [Test]
        public void BladesThatNeverComeCloseDoNotClash()
        {
            var detector = new BladeClashDetector();
            var above = new BladeSegment(new Vector3(-0.3f, 1.05f, 0.8f), new Vector3(0.6f, 1.05f, 0.8f));

            Assert.That(detector.Step(0.0, above, Opponent).HasValue, Is.False);
            Assert.That(detector.Step(FrameSeconds, new BladeSegment(above.GuardPosition + new Vector3(0, 0, 0.2f), above.TipPosition + new Vector3(0, 0, 0.2f)), Opponent).HasValue,
                Is.False, "Moving along, 5 cm above, never meets it.");
        }

        [Test]
        public void AFastBeatThatPassesThroughBetweenFramesIsCaught()
        {
            var detector = new BladeClashDetector();

            // 20 cm sideways in one frame (14 m/s), from 10 cm on one side to 10 cm on the other: neither frame touches.
            Assert.That(detector.Step(0.0, Rising(-0.1f), Opponent).HasValue, Is.False);
            var clash = detector.Step(FrameSeconds, Rising(0.1f), Opponent);

            Assert.That(clash.HasValue, Is.True);
            Assert.That(Math.Abs(clash.Value.ContactPoint.X), Is.LessThan(BladeClashDetector.ContactDistanceMeters));
        }

        [Test]
        public void TheClashSaysWhereOnEachBladeAndHowHard()
        {
            var detector = new BladeClashDetector();
            const float SpeedMetersPerSecond = 2.0f;
            var step = SpeedMetersPerSecond * (float)FrameSeconds;

            // The Fencer's blade, across the Dummy's, beats down onto it: its forte (0.3 from the guard) on the Dummy's foible (z 0.78).
            Assert.That(detector.Step(0.0, Level(1.0f + (2.0f * step)), Opponent).HasValue, Is.False);
            Assert.That(detector.Step(FrameSeconds, Level(1.0f + step), Opponent).HasValue, Is.False);
            var clash = detector.Step(2 * FrameSeconds, Level(1.0f), Opponent);

            Assert.That(clash.HasValue, Is.True);
            Assert.That(clash.Value.FencerBladeFraction, Is.EqualTo(0.3f).Within(0.02f), "Forte.");
            Assert.That(clash.Value.OpponentBladeFraction, Is.EqualTo(0.8f).Within(0.01f), "Foible.");
            Assert.That(clash.Value.RelativeSpeedMetersPerSecond, Is.EqualTo(SpeedMetersPerSecond).Within(0.05f));
            Assert.That(Vector3.Dot(clash.Value.PushDirection, -Vector3.UnitY), Is.GreaterThan(0.999f));
            Assert.That(clash.Value.TimeSeconds, Is.EqualTo(2 * FrameSeconds));
        }

        [Test]
        public void SlidingAlongTheDummysBladeCountsOnlyTheMotionSquareToIt()
        {
            var detector = new BladeClashDetector();

            // 3 cm along the Dummy's blade per frame (2.2 m/s) while closing 8 mm on it (0.6 m/s).
            detector.Step(0.0, new BladeSegment(new Vector3(0.0f, 1.013f, 0.0f), new Vector3(0.0f, 1.013f, 0.9f)), Opponent);
            var clash = detector.Step(FrameSeconds, new BladeSegment(new Vector3(0.0f, 1.005f, 0.03f), new Vector3(0.0f, 1.005f, 0.93f)), Opponent);

            Assert.That(clash.HasValue, Is.True);
            Assert.That(clash.Value.RelativeSpeedMetersPerSecond, Is.EqualTo(0.008f * 72.0f).Within(0.02f));
            Assert.That(Vector3.Dot(clash.Value.PushDirection, -Vector3.UnitY), Is.GreaterThan(0.999f));
        }
    }
}
