// The moving Dummy's rigid glide (PER-105): the whole Dummy slides to the distance keeper's target, starting and
// stopping like a step, never faster than the top speed, and never past the target.
using System;
using NUnit.Framework;
using VrFencing.Dummy;

namespace VrFencing.Tests.Dummy
{
    public class DummyGlideTests
    {
        private const float FrameSeconds = 1.0f / 72.0f;

        // The Blade restarts its sweep when the Target Zones jump more than 5 cm in a frame (BladeBehaviour.JumpMeters):
        // a glide must stay below that so the Touch detector sweeps the moving zones.
        private const float BladeJumpMeters = 0.05f;

        [Test]
        public void ItArrivesOnTheTargetAndStops()
        {
            var glide = new DummyGlide(1.9f);

            for (var frame = 0; frame < 144; frame++)
            {
                glide.Step(2.23f, FrameSeconds);
            }

            Assert.That(glide.FrontFootZ, Is.EqualTo(2.23f));
            Assert.That(glide.VelocityMetersPerSecond, Is.EqualTo(0.0f));
        }

        [TestCase(0.05f)]
        [TestCase(0.33f)]
        [TestCase(-1.0f)]
        public void ItNeverPassesTheTarget(float move)
        {
            var glide = new DummyGlide(1.9f);
            var target = 1.9f + move;
            var previousError = Math.Abs(move);

            for (var frame = 0; frame < 300; frame++)
            {
                glide.Step(target, FrameSeconds);
                var error = (target - glide.FrontFootZ) * Math.Sign(move);
                Assert.That(error, Is.GreaterThanOrEqualTo(0.0f), $"Frame {frame}: past the target.");
                Assert.That(error, Is.LessThanOrEqualTo(previousError + 1e-6f), $"Frame {frame}: moved away from it.");
                previousError = error;
            }
        }

        [Test]
        public void ItSpeedsUpAndSlowsDownGentlyAndKeepsUnderTheTopSpeed()
        {
            var glide = new DummyGlide(0.0f);
            var previousVelocity = 0.0f;
            var maximumChange = DistanceKeeperTuning.GlideAccelerationMetersPerSecondSquared * FrameSeconds;

            for (var frame = 0; frame < 300; frame++)
            {
                var before = glide.FrontFootZ;
                glide.Step(2.0f, FrameSeconds);
                Assert.That(Math.Abs(glide.VelocityMetersPerSecond), Is.LessThanOrEqualTo(DistanceKeeperTuning.GlideTopSpeedMetersPerSecond + 1e-6f));
                // The frame it lands on the target it stops from a crawl: under two speed steps.
                var allowedChange = glide.FrontFootZ == 2.0f ? 2.0f * maximumChange : maximumChange;
                Assert.That(Math.Abs(glide.VelocityMetersPerSecond - previousVelocity), Is.LessThanOrEqualTo(allowedChange + 1e-5f), $"Frame {frame}");
                Assert.That(Math.Abs(glide.FrontFootZ - before), Is.LessThan(BladeJumpMeters));
                previousVelocity = glide.VelocityMetersPerSecond;
            }

            Assert.That(glide.FrontFootZ, Is.EqualTo(2.0f));
        }

        [Test]
        public void ALongGlideReachesTheTopSpeed()
        {
            var glide = new DummyGlide(0.0f);
            var fastest = 0.0f;

            for (var frame = 0; frame < 144; frame++)
            {
                glide.Step(3.0f, FrameSeconds);
                fastest = Math.Max(fastest, glide.VelocityMetersPerSecond);
            }

            Assert.That(fastest, Is.EqualTo(DistanceKeeperTuning.GlideTopSpeedMetersPerSecond).Within(1e-5f));
        }

        [Test]
        public void ATargetThatTurnsBackIsFollowedWithoutAJump()
        {
            var glide = new DummyGlide(1.9f);
            for (var frame = 0; frame < 20; frame++)
            {
                glide.Step(2.5f, FrameSeconds);
            }

            Assert.That(glide.VelocityMetersPerSecond, Is.GreaterThan(0.0f));
            for (var frame = 0; frame < 300; frame++)
            {
                var before = glide.FrontFootZ;
                glide.Step(1.6f, FrameSeconds);
                Assert.That(Math.Abs(glide.FrontFootZ - before), Is.LessThan(BladeJumpMeters));
            }

            Assert.That(glide.FrontFootZ, Is.EqualTo(1.6f));
        }

        [Test]
        public void NoTimeNoMove()
        {
            var glide = new DummyGlide(1.0f);

            glide.Step(2.0f, 0.0f);

            Assert.That(glide.FrontFootZ, Is.EqualTo(1.0f));
        }

        [Test]
        public void ANonFiniteTargetIsRejected()
        {
            var glide = new DummyGlide(1.0f);

            Assert.Throws<ArgumentOutOfRangeException>(() => glide.Step(float.NaN, FrameSeconds));
        }
    }
}
