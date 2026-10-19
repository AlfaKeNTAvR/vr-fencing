// When a lunge started (PER-37), from the head's forward motion along the Strip.
using System.Collections.Generic;
using NUnit.Framework;
using VrFencing.Tracking;
using static VrFencing.Tests.Tracking.TrackingTestData;

namespace VrFencing.Tests.Tracking
{
    public class LungeStartTests
    {
        // The onset is found a frame or few after the burst begins, once the speed passes the onset threshold.
        private const double OnsetTolerance = 0.1;

        [Test]
        public void LungeStartsWhenTheHeadBeginsToDriveForward()
        {
            var track = HeadTrack(2.0, 0.0f, Lunge(1.0));

            var start = LungeStart.FindStartSeconds(track);

            Assert.That(start, Is.Not.Null);
            Assert.That(start.Value, Is.EqualTo(1.0).Within(OnsetTolerance));
            Assert.That(start.Value, Is.GreaterThanOrEqualTo(0.95), "Found before the head moved.");
        }

        [Test]
        public void StandingEnGardeIsNoLunge()
        {
            var track = HeadTrack(2.0, 0.0f);

            Assert.That(LungeStart.FindStartSeconds(track), Is.Null);
        }

        [Test]
        public void LeaningInSlowlyIsNoLunge()
        {
            var track = HeadTrack(2.0, 0.0f, new Burst(0.5, 1.0, 0.5f));

            Assert.That(LungeStart.FindStartSeconds(track), Is.Null);
        }

        [Test]
        public void FastRetreatIsNoLunge()
        {
            var track = HeadTrack(2.0, 0.0f, new Burst(0.5, 0.5, -2.0f));

            Assert.That(LungeStart.FindStartSeconds(track), Is.Null);
        }

        [Test]
        public void StepThenPauseThenLungeStartsAtTheLunge()
        {
            var advance = new Burst(0.3, 0.4, 1.2f);
            var track = HeadTrack(2.5, 0.0f, advance, Lunge(1.4));

            var start = LungeStart.FindStartSeconds(track);

            Assert.That(start.Value, Is.EqualTo(1.4).Within(OnsetTolerance));
        }

        [Test]
        public void StepFlowingStraightIntoTheLungeStartsAtTheStep()
        {
            // Known limit of the rule: with no pause, the step and the lunge are one forward burst.
            var advance = new Burst(0.5, 0.4, 1.2f);
            var track = HeadTrack(2.0, 0.0f, advance, Lunge(0.8));

            var start = LungeStart.FindStartSeconds(track);

            Assert.That(start.Value, Is.EqualTo(0.5).Within(OnsetTolerance));
        }

        [Test]
        public void LungeAlreadyUnderWayAtTheFirstSampleStartsThere()
        {
            var track = HeadTrack(1.0, 0.0f, Lunge(-0.2));

            Assert.That(LungeStart.FindStartSeconds(track), Is.EqualTo(0.0));
        }

        [Test]
        public void TooFewSamplesToMeasureSpeedIsNoLunge()
        {
            Assert.That(LungeStart.FindStartSeconds(new List<ForwardPosition>()), Is.Null);
            Assert.That(LungeStart.FindStartSeconds(new[] { new ForwardPosition(0.0, 0.0f) }), Is.Null);
            Assert.That(LungeStart.FindStartSeconds(new[] { new ForwardPosition(0.0, 0.0f), new ForwardPosition(0.01, 0.5f) }), Is.Null);
        }
    }
}
