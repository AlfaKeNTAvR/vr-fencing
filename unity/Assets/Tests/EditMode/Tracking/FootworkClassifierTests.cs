// The footwork classifier (PER-39, PER-88) on synthetic head traces: 72 Hz frames along the Strip, with smooth
// forward and backward bursts (see TrackingTestData) for a 1.65 m Fencer, so a step is about 0.33 m and a lunge's head
// travel about 0.5 to 0.6 m, lowering the head about 12 cm.
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

        // The head going down (or, with a negative drop, up) over the same time as a forward burst.
        private static Burst Lowering(Burst during, float meters = 0.12f) => Move(during.StartSeconds, during.DurationSeconds, -meters);

        // Head samples in the Strip frame: forward motion from the bursts, standing at the En Garde Line's height.
        private static List<HeadSample> Trace(double durationSeconds, params Burst[] bursts)
        {
            return HeadTrack(durationSeconds, -0.25f, bursts)
                .Select(position => new HeadSample(position.TimeSeconds, new Vector3(0.0f, HeadHeight, position.ForwardMeters)))
                .ToList();
        }

        // As above, with the head's height moving with the vertical bursts (plus the same 1 cm sway).
        private static List<HeadSample> Trace(double durationSeconds, Burst[] bursts, Burst[] verticalBursts)
        {
            var heights = HeadTrack(durationSeconds, HeadHeight, verticalBursts);
            return HeadTrack(durationSeconds, -0.25f, bursts)
                .Select((position, i) => new HeadSample(position.TimeSeconds, new Vector3(0.0f, heights[i].ForwardMeters, position.ForwardMeters)))
                .ToList();
        }

        // A trace of steps and lunges, each lunge lowering the head 12 cm.
        private static List<HeadSample> Lunging(Burst[] steps, params Burst[] lunges)
        {
            return Trace(2.0, steps.Concat(lunges).ToArray(), lunges.Select(lunge => Lowering(lunge)).ToArray());
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
            Assert.That(Read(Lunging(new Burst[0], LungeOf(0.3))), Is.EqualTo(FootworkAction.Lunge));
        }

        [Test]
        public void ALongFastStepWithTheHeadUpIsAnAdvanceNotALunge()
        {
            // A lunge's length and speed, but the head stays up: speed and travel alone no longer make a lunge (PER-88).
            Assert.That(Read(Trace(2.0, LungeOf(0.3))), Is.EqualTo(FootworkAction.Advance));
        }

        [Test]
        public void ALungeStaysALungeAfterTheRecovery()
        {
            var lunge = LungeOf(0.3);
            var recovery = Recovery(1.0);

            var trace = Trace(2.0, new[] { lunge, recovery }, new[] { Lowering(lunge), Lowering(recovery, -0.12f) });

            Assert.That(Read(trace), Is.EqualTo(FootworkAction.Lunge));
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
            Assert.That(Read(Lunging(new[] { Advance(0.2) }, LungeOf(0.6, 0.6f))), Is.EqualTo(FootworkAction.StepLunge));
        }

        [Test]
        public void AnAdvanceThenALungeAfterAPauseIsAStepLunge()
        {
            Assert.That(Read(Lunging(new[] { Advance(0.1) }, LungeOf(0.9, 0.6f))), Is.EqualTo(FootworkAction.StepLunge));
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

        // ---- The 2026-10-19 headset log (PER-88) ----
        // Four Advance calls read 1.0 to 1.4 m forward at 1.5 to 1.8 m/s peak, with the head 0.12 to 0.24 m down, and
        // were judged step-lunges. One advance moves the head about 0.33 m, so 1.0 to 1.4 m in the 2 s window is three or
        // four quick advances; each step's peak (1.5 to 1.8 m/s) is a brisk advance, and the head drop came from sinking
        // into en garde, the cue having come while the Fencer was still standing up from the last Rep.

        [Test]
        public void ThreeQuickAdvancesFromTheLogAreAdvancesNotAStepLunge()
        {
            // 3 x 0.33 m, each in 0.44 s (1.5 m/s peak), the head sinking 0.12 m over the first second.
            var steps = new[] { Move(0.25, 0.44, 0.33f), Move(0.75, 0.44, 0.33f), Move(1.25, 0.44, 0.33f) };
            var trace = Trace(2.0, steps, new[] { Move(0.25, 1.0, -0.12f) });

            var reading = FootworkClassifier.Classify(trace, CueSeconds, Height);

            Assert.That(reading.ForwardTravelMeters, Is.EqualTo(1.0f).Within(0.03f));
            Assert.That(reading.PeakForwardSpeedMetersPerSecond, Is.EqualTo(1.5f).Within(0.1f));
            Assert.That(reading.HeadDropMeters, Is.EqualTo(0.12f).Within(0.02f));
            Assert.That(reading.Action, Is.EqualTo(FootworkAction.Advance));
            Assert.That(reading.StepCount, Is.EqualTo(3));
        }

        [Test]
        public void FourQuickAdvancesFromTheLogAreAdvancesNotAStepLunge()
        {
            // 4 x 0.35 m, each in 0.39 s (1.8 m/s peak), the head sinking 0.24 m.
            var steps = new[] { Move(0.1, 0.39, 0.35f), Move(0.45, 0.39, 0.35f), Move(0.8, 0.39, 0.35f), Move(1.15, 0.39, 0.35f) };
            var trace = Trace(2.0, steps, new[] { Move(0.1, 1.4, -0.24f) });

            var reading = FootworkClassifier.Classify(trace, CueSeconds, Height);

            Assert.That(reading.ForwardTravelMeters, Is.EqualTo(1.4f).Within(0.03f));
            Assert.That(reading.PeakForwardSpeedMetersPerSecond, Is.EqualTo(1.8f).Within(0.1f));
            Assert.That(reading.Action, Is.EqualTo(FootworkAction.Advance));
            Assert.That(reading.StepCount, Is.EqualTo(4));
        }

        [Test]
        public void AdvancesFlowingIntoEachOtherStillCountOneByOne()
        {
            // Each step starts before the last one ends, so the head never stops: its speed only dips to about a third.
            var steps = new[] { Move(0.2, 0.44, 0.33f), Move(0.52, 0.44, 0.33f), Move(0.84, 0.44, 0.33f) };
            var trace = Trace(2.0, steps, new[] { Move(0.2, 1.0, -0.12f) });

            var reading = FootworkClassifier.Classify(trace, CueSeconds, Height);

            Assert.That(reading.Action, Is.EqualTo(FootworkAction.Advance));
            Assert.That(reading.StepCount, Is.EqualTo(3));
        }

        [Test]
        public void AStepLungeWithAFastLowLungeIsStillAStepLunge()
        {
            // The real thing: an advance, then a 0.6 m lunge peaking at 2.7 m/s with the head 0.15 m down.
            var lunge = LungeOf(0.75, 0.6f);
            var trace = Trace(2.0, new[] { Advance(0.2), lunge }, new[] { Lowering(lunge, 0.15f) });

            Assert.That(Read(trace), Is.EqualTo(FootworkAction.StepLunge));
        }

        [Test]
        public void ASmallCheckFromTheLogRegisters()
        {
            // 6 cm forward and back: under the old 7 cm threshold, which read the logged checks as no step.
            Assert.That(Read(Trace(2.0, Move(0.3, 0.3, 0.06f), Move(0.8, 0.3, -0.06f))), Is.EqualTo(FootworkAction.Check));
        }

        [Test]
        public void TwoRetreatsCountAsARetreatOfTwoSteps()
        {
            var reading = FootworkClassifier.Classify(Trace(2.0, Retreat(0.2), Retreat(0.8)), CueSeconds, Height);

            Assert.That(reading.Action, Is.EqualTo(FootworkAction.Retreat));
            Assert.That(reading.StepCount, Is.EqualTo(2));
        }

        [Test]
        public void OnlyAdvancesAndRetreatsCountSteps()
        {
            var reading = FootworkClassifier.Classify(Lunging(new Burst[0], LungeOf(0.3)), CueSeconds, Height);

            Assert.That(reading.StepCount, Is.Zero);
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
