// The footwork classifier (PER-39) on synthetic head traces: 72 Hz frames along the Strip, with smooth forward
// and backward bursts (see TrackingTestData) for a 1.65 m Fencer, so a step is about 0.33 m and a lunge's head
// travel about 0.5 to 0.6 m.
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Drills;
using VrFencing.Tracking;
using static VrFencing.Tests.Tracking.TrackingTestData;

namespace VrFencing.Tests.Tracking
{
    public class FootworkClassifierTests
    {
        private const float Height = 1.65f;
        private const float HeadHeight = 1.45f;
        private const double CueSeconds = 0.0;

        // A burst of distance d over t seconds peaks at 2d/t (the sin^2 profile averages half its peak).
        private static Burst Move(double startSeconds, double durationSeconds, float meters)
        {
            return new Burst(startSeconds, durationSeconds, (float)(2.0 * meters / durationSeconds));
        }

        private static Burst Advance(double startSeconds) => Move(startSeconds, 0.45, 0.33f);

        private static Burst Retreat(double startSeconds) => Move(startSeconds, 0.45, -0.33f);

        private static Burst LungeOf(double startSeconds, float meters = 0.55f) => Move(startSeconds, 0.45, meters);

        private static Burst Recovery(double startSeconds, float meters = 0.55f) => Move(startSeconds, 0.6, -meters);

        // Head samples in the Strip frame: forward motion from the bursts, standing at the En Garde Line's height.
        private static List<HeadSample> Trace(double durationSeconds, params Burst[] bursts)
        {
            return HeadTrack(durationSeconds, -0.25f, bursts)
                .Select(position => new HeadSample(position.TimeSeconds, new Vector3(0.0f, HeadHeight, position.ForwardMeters)))
                .ToList();
        }

        private static FootworkAction Read(List<HeadSample> trace, float height = Height)
        {
            return FootworkClassifier.Classify(trace, CueSeconds, height).Action;
        }

        [Test]
        public void StandingEnGardeIsNoFootwork()
        {
            var reading = FootworkClassifier.Classify(Trace(2.0), CueSeconds, Height);

            Assert.That(reading.Action, Is.EqualTo(FootworkAction.None));
            Assert.That(reading.OnsetSecondsAfterCue, Is.Null, "The 1 cm sway is not a move.");
        }

        [Test]
        public void LeaningIntoAnExtensionIsNoFootwork()
        {
            Assert.That(Read(Trace(2.0, Move(0.3, 0.4, 0.08f))), Is.EqualTo(FootworkAction.None));
        }

        [Test]
        public void OneStepForwardHeldIsAnAdvance()
        {
            Assert.That(Read(Trace(2.0, Advance(0.4))), Is.EqualTo(FootworkAction.Advance));
        }

        [Test]
        public void AFastAdvanceIsStillAnAdvanceNotALunge()
        {
            // Fast enough for a lunge (peaks at 2 m/s) but only a step long.
            Assert.That(Read(Trace(2.0, Move(0.4, 0.3, 0.3f))), Is.EqualTo(FootworkAction.Advance));
        }

        [Test]
        public void OneStepBackIsARetreat()
        {
            Assert.That(Read(Trace(2.0, Retreat(0.4))), Is.EqualTo(FootworkAction.Retreat));
        }

        [Test]
        public void ALungeIsALunge()
        {
            Assert.That(Read(Trace(2.0, LungeOf(0.3))), Is.EqualTo(FootworkAction.Lunge));
        }

        [Test]
        public void ALungeStaysALungeAfterTheRecovery()
        {
            Assert.That(Read(Trace(2.0, LungeOf(0.3), Recovery(1.0))), Is.EqualTo(FootworkAction.Lunge));
        }

        [Test]
        public void ASlowLongWalkForwardIsNotALunge()
        {
            // 0.6 m in 1.5 s peaks at 0.8 m/s: too slow for a lunge.
            Assert.That(Read(Trace(2.0, Move(0.2, 1.5, 0.6f))), Is.EqualTo(FootworkAction.Advance));
        }

        [Test]
        public void AnAdvanceFlowingIntoALungeIsAStepLunge()
        {
            Assert.That(Read(Trace(2.0, Advance(0.2), LungeOf(0.6, 0.6f))), Is.EqualTo(FootworkAction.StepLunge));
        }

        [Test]
        public void AnAdvanceThenALungeAfterAPauseIsAStepLunge()
        {
            Assert.That(Read(Trace(2.0, Advance(0.1), LungeOf(0.9, 0.6f))), Is.EqualTo(FootworkAction.StepLunge));
        }

        [Test]
        public void AHalfStepForwardAndBackIsACheck()
        {
            Assert.That(Read(Trace(2.0, Move(0.3, 0.3, 0.12f), Move(0.7, 0.3, -0.12f))), Is.EqualTo(FootworkAction.Check));
        }

        [Test]
        public void AnAdvanceTakenBackIsACheck()
        {
            Assert.That(Read(Trace(2.0, Advance(0.2), Retreat(0.9))), Is.EqualTo(FootworkAction.Check));
        }

        [Test]
        public void ARetreatThatComesBackIsStillARetreat()
        {
            Assert.That(Read(Trace(2.0, Retreat(0.2), Advance(1.0))), Is.EqualTo(FootworkAction.Retreat));
        }

        [Test]
        public void DistancesScaleWithTheFencersHeight()
        {
            // 0.18 m is an advance for a 1.5 m Fencer (step threshold 0.12 m) but not for a 2.6 m one (0.21 m).
            var trace = Trace(2.0, Move(0.4, 0.45, 0.18f));

            Assert.That(Read(trace, height: 1.5f), Is.EqualTo(FootworkAction.Advance));
            Assert.That(Read(trace, height: 2.6f), Is.EqualTo(FootworkAction.None));
        }

        [Test]
        public void TheOnsetIsWhenTheHeadFirstMovesOffItsPlace()
        {
            var reading = FootworkClassifier.Classify(Trace(2.0, Advance(0.5)), CueSeconds, Height);

            Assert.That(reading.OnsetSecondsAfterCue, Is.Not.Null);
            Assert.That(reading.OnsetSecondsAfterCue.Value, Is.GreaterThan(0.5).And.LessThan(0.7));
        }

        [Test]
        public void TheOnsetCountsFromTheCueNotFromTheFirstSample()
        {
            var trace = Trace(2.0, Advance(0.5));
            var lateCue = -0.2;

            var reading = FootworkClassifier.Classify(trace, lateCue, Height);

            Assert.That(reading.OnsetSecondsAfterCue.Value, Is.GreaterThan(0.7).And.LessThan(0.9));
        }

        [Test]
        public void TheReadingCarriesTravelSpeedAndHeadDrop()
        {
            var trace = Trace(2.0, LungeOf(0.3), Recovery(1.0, 0.7f));
            trace[60] = new HeadSample(trace[60].TimeSeconds, trace[60].HeadInStrip - new Vector3(0.0f, 0.15f, 0.0f));

            var reading = FootworkClassifier.Classify(trace, CueSeconds, Height);

            Assert.That(reading.ForwardTravelMeters, Is.EqualTo(0.55f).Within(0.03f));
            Assert.That(reading.BackwardTravelMeters, Is.EqualTo(0.15f).Within(0.03f));
            Assert.That(reading.PeakForwardSpeedMetersPerSecond, Is.EqualTo(2.0f * 0.55f / 0.45f).Within(0.15f));
            Assert.That(reading.HeadDropMeters, Is.EqualTo(0.15f).Within(1e-4f));
        }

        [Test]
        public void AnEmptyTraceIsNoFootwork()
        {
            var reading = FootworkClassifier.Classify(new List<HeadSample>(), CueSeconds, Height);

            Assert.That(reading.Action, Is.EqualTo(FootworkAction.None));
            Assert.That(reading.ForwardTravelMeters, Is.Zero);
        }

        [Test]
        public void ANonPositiveHeightIsRejected()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => FootworkClassifier.Classify(Trace(1.0), CueSeconds, 0.0f));
        }

        [Test]
        public void TheRepTrackerReadsOnlyTheCurrentRepFromItsCue()
        {
            var tracker = new RepFootworkTracker();
            var firstRep = Trace(2.0, Advance(0.2));
            tracker.BeginRep(0.0);
            firstRep.ForEach(tracker.AddSample);
            Assert.That(tracker.Read(Height).Action, Is.EqualTo(FootworkAction.Advance));

            tracker.BeginRep(5.0);
            tracker.AddSample(new HeadSample(4.9, new Vector3(0.0f, HeadHeight, 0.0f)));
            foreach (var sample in Trace(2.0))
            {
                tracker.AddSample(new HeadSample(5.0 + sample.TimeSeconds, sample.HeadInStrip + new Vector3(0.0f, 0.0f, 0.33f)));
            }

            Assert.That(tracker.Read(Height).Action, Is.EqualTo(FootworkAction.None), "The advance before this cue does not count.");
            Assert.That(tracker.SampleCount, Is.EqualTo(Trace(2.0).Count), "A sample from before the cue is ignored.");
        }

        [Test]
        public void TheRepTrackerIgnoresOutOfOrderSamples()
        {
            var tracker = new RepFootworkTracker();
            tracker.BeginRep(0.0);
            tracker.AddSample(new HeadSample(1.0, Vector3.Zero));
            tracker.AddSample(new HeadSample(0.5, new Vector3(0.0f, 0.0f, 1.0f)));
            tracker.AddSample(new HeadSample(1.0, new Vector3(0.0f, 0.0f, 1.0f)));

            Assert.That(tracker.SampleCount, Is.EqualTo(1));
        }
    }
}
