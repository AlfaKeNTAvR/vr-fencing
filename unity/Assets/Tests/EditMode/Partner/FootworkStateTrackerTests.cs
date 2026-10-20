// The sender's footwork reading (PER-112): from the hips and head at 72 Hz it keeps the feet planted in en garde through
// sway and leans, steps the lead foot first on an advance or retreat, kicks the front foot out in a lunge with the back
// foot planted, brings the feet back into en garde in the recovery, and says how sure it is. Side steps and diagonals
// (PER-118) step the feet across and diagonally the same way.
using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Dummy;
using VrFencing.Partner;
using VrFencing.Settings;

namespace VrFencing.Tests.Partner
{
    public class FootworkStateTrackerTests
    {
        private const double FrameSeconds = 1.0 / 72.0;
        private const float Height = 1.8f;

        // A foot this close to its en garde place counts as in en garde.
        private const float PlaceToleranceMeters = 0.03f;

        // En garde, right-handed, from the Dummy's stance: the hip center and the headset (the eyes) above and ahead of it.
        private static readonly Vector3 HipsAtStart = new Vector3(0.1f, 0.5f * Height, 0.0f);
        private static readonly Vector3 HeadFromHips = new Vector3(0.015f * Height, 0.40f * Height, 0.055f * Height);

        [Test]
        public void SwayAndBreathingMoveNoFoot()
        {
            var frames = Run(3.0, time => new Pose(
                HipsAtStart + new Vector3(0.0f, 0.0f, 0.01f * (float)Math.Sin(time * 2.0)),
                0.005f * (float)Math.Sin(time * 1.8)));

            foreach (var state in frames)
            {
                Assert.That(state.Phase, Is.EqualTo(FootworkPhase.Standing));
                Assert.That(state.Front.IsPlanted && state.Back.IsPlanted, Is.True);
                Assert.That(state.Front.Z, Is.EqualTo(frames[0].Front.Z));
                Assert.That(state.Back.Z, Is.EqualTo(frames[0].Back.Z));
                Assert.That(state.Confidence, Is.EqualTo(RemoteFootworkTuning.TrackedConfidence));
            }
        }

        [Test]
        public void TheFeetStandInEnGardeUnderTheHips()
        {
            var state = Run(0.1, _ => new Pose(HipsAtStart, 0.0f))[0];

            AssertInPlace(state, HipsAtStart, Hand.Right);
        }

        [Test]
        public void ALeanOfTheTorsoMovesNoFoot()
        {
            // The head goes 15 cm forward and back; the hips stay.
            var frames = Run(2.0, time => new Pose(HipsAtStart, 0.0f, 0.15f * (float)Math.Sin(Math.Min(time, 1.0) * Math.PI)));

            foreach (var state in frames)
            {
                Assert.That(state.Phase, Is.EqualTo(FootworkPhase.Standing));
                Assert.That(state.Front.Z, Is.EqualTo(frames[0].Front.Z));
            }
        }

        [Test]
        public void AnAdvanceStepsTheFrontFootFirstAndEndsInEnGarde()
        {
            var stride = 0.18f * Height;
            var frames = Run(1.2, time => new Pose(HipsAtStart + new Vector3(0.0f, 0.0f, stride * Smooth(time - 0.1, 0.45)), 0.0f));

            var frontLifts = FirstFrame(frames, state => state.Front.LiftMeters > 0.0f);
            var backLifts = FirstFrame(frames, state => state.Back.LiftMeters > 0.0f);
            Assert.That(frontLifts, Is.GreaterThanOrEqualTo(0), "The front foot lifts.");
            Assert.That(backLifts, Is.GreaterThan(frontLifts), "The back foot follows.");
            Assert.That(frames[frontLifts].Phase, Is.EqualTo(FootworkPhase.Advancing));
            for (var frame = 0; frame < backLifts; frame++)
            {
                Assert.That(frames[frame].Back.Z, Is.EqualTo(frames[0].Back.Z), $"The back foot stays planted until it lifts (frame {frame}).");
            }

            for (var frame = 1; frame < frames.Count; frame++)
            {
                Assert.That(frames[frame].Front.Z, Is.GreaterThanOrEqualTo(frames[frame - 1].Front.Z - 1e-5f), $"Never backward (frame {frame}).");
                Assert.That(frames[frame].Back.Z, Is.GreaterThanOrEqualTo(frames[frame - 1].Back.Z - 1e-5f), $"Never backward (frame {frame}).");
            }

            var last = frames[frames.Count - 1];
            Assert.That(last.Phase, Is.EqualTo(FootworkPhase.Standing));
            Assert.That(last.Front.IsPlanted && last.Back.IsPlanted, Is.True);
            AssertInPlace(last, HipsAtStart + new Vector3(0.0f, 0.0f, stride), Hand.Right);
        }

        [Test]
        public void ARetreatStepsTheBackFootFirst()
        {
            var stride = 0.18f * Height;
            var frames = Run(1.2, time => new Pose(HipsAtStart - new Vector3(0.0f, 0.0f, stride * Smooth(time - 0.1, 0.45)), 0.0f));

            var frontLifts = FirstFrame(frames, state => state.Front.LiftMeters > 0.0f);
            var backLifts = FirstFrame(frames, state => state.Back.LiftMeters > 0.0f);
            Assert.That(backLifts, Is.GreaterThanOrEqualTo(0));
            Assert.That(frontLifts, Is.GreaterThan(backLifts));
            Assert.That(frames[backLifts].Phase, Is.EqualTo(FootworkPhase.Retreating));
            AssertInPlace(frames[frames.Count - 1], HipsAtStart - new Vector3(0.0f, 0.0f, stride), Hand.Right);
        }

        [Test]
        public void ALungeKicksTheFrontFootOutWithTheBackFootPlantedAndRecovers()
        {
            // The hips go 0.38 of height forward in 0.35 s as the head drops 0.12 of height, hold, then come back.
            var hipTravel = 0.38f * Height;
            var headDrop = 0.12f * Height;
            Pose At(double time)
            {
                var outFraction = Smooth(time - 0.2, 0.35);
                var backFraction = Smooth(time - 1.0, 0.5);
                var fraction = outFraction - backFraction;
                return new Pose(HipsAtStart + new Vector3(0.0f, 0.0f, hipTravel * fraction), -headDrop * fraction);
            }

            var frames = Run(2.2, At);

            var lunging = FirstFrame(frames, state => state.Phase == FootworkPhase.Lunging);
            var recovering = FirstFrame(frames, state => state.Phase == FootworkPhase.Recovering);
            Assert.That(lunging, Is.GreaterThanOrEqualTo(0), "Read as a lunge.");
            Assert.That(recovering, Is.GreaterThan(lunging), "Then a recovery.");
            Assert.That(lunging * FrameSeconds, Is.LessThan(0.2 + 0.15), "Read within 150 ms of the start.");

            var start = frames[0];
            for (var frame = 0; frame < recovering; frame++)
            {
                Assert.That(frames[frame].Back.IsPlanted, Is.True, $"Back foot planted (frame {frame}).");
                Assert.That(frames[frame].Back.Z, Is.EqualTo(start.Back.Z).Within(1e-4f), $"Back foot pinned (frame {frame}).");
            }

            var atFullLunge = frames[(int)(0.9 / FrameSeconds)];
            Assert.That(atFullLunge.Front.IsPlanted, Is.True, "The front foot has landed.");
            Assert.That(atFullLunge.Front.Z - start.Front.Z, Is.GreaterThan(0.5f * Height), "Far ahead.");
            Assert.That(MaxOf(frames, state => state.Front.PitchRadians), Is.GreaterThan(0.2f), "Toe up as it kicks out.");

            var last = frames[frames.Count - 1];
            Assert.That(last.Phase, Is.EqualTo(FootworkPhase.Standing));
            AssertInPlace(last, HipsAtStart, Hand.Right);
        }

        [Test]
        public void AFastAdvanceWithTheHeadLevelIsNoLunge()
        {
            var frames = Run(1.0, time => new Pose(HipsAtStart + new Vector3(0.0f, 0.0f, 0.18f * Height * Smooth(time - 0.1, 0.2)), 0.0f));

            Assert.That(FirstFrame(frames, state => state.Phase == FootworkPhase.Lunging), Is.EqualTo(-1));
            Assert.That(FirstFrame(frames, state => state.Phase == FootworkPhase.Advancing), Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void HipsThatDriftSlowlyGetTheFeetToStepAfterThem()
        {
            // 25 cm in 3 s: too slow for a step, but the feet cannot stay behind.
            var frames = Run(4.0, time => new Pose(HipsAtStart + new Vector3(0.0f, 0.0f, 0.25f * (float)Math.Min(time / 3.0, 1.0)), 0.0f));

            Assert.That(FirstFrame(frames, state => state.Phase == FootworkPhase.Advancing && (state.Front.LiftMeters > 0.0f || state.Back.LiftMeters > 0.0f)), Is.GreaterThanOrEqualTo(0));
            AssertInPlace(frames[frames.Count - 1], HipsAtStart + new Vector3(0.0f, 0.0f, 0.25f), Hand.Right, RemoteFootworkTuning.FootDriftPerHeight * Height);
        }

        [Test]
        public void WithoutTrackedHipsTheHeadStandsInLessSurely()
        {
            var stride = 0.18f * Height;
            var frames = Run(1.2, time => new Pose(HipsAtStart + new Vector3(0.0f, 0.0f, stride * Smooth(time - 0.1, 0.45)), 0.0f, hasHips: false));

            Assert.That(frames[frames.Count - 1].Confidence, Is.EqualTo(RemoteFootworkTuning.HeadOnlyConfidence));
            Assert.That(FirstFrame(frames, state => state.Phase == FootworkPhase.Advancing), Is.GreaterThanOrEqualTo(0));
            AssertInPlace(frames[frames.Count - 1], HipsAtStart + new Vector3(0.0f, 0.0f, stride), Hand.Right, 0.05f);
        }

        [Test]
        public void WithoutTheHeadNothingIsSure()
        {
            var tracker = new FootworkStateTracker();
            tracker.Update(1.0, null, HipsAtStart, Height, Hand.Right);

            Assert.That(tracker.State.Confidence, Is.EqualTo(RemoteFootworkTuning.NoConfidence));
        }

        [Test]
        public void WalkingAcrossTheStripIsNotSure()
        {
            var frames = Run(1.0, time => new Pose(HipsAtStart + new Vector3(1.4f * (float)time, 0.0f, 0.0f), 0.0f));

            Assert.That(frames[frames.Count - 1].Confidence, Is.EqualTo(RemoteFootworkTuning.SidewaysConfidence));
        }

        [TestCase(0.22f)]
        [TestCase(-0.22f)]
        public void ASideStepStepsTheFrontFootFirstAcrossAndEndsInEnGarde(float side)
        {
            var frames = Run(1.2, time => new Pose(HipsAtStart + new Vector3(side * Smooth(time - 0.1, 0.45), 0.0f, 0.0f), 0.0f));

            var frontLifts = FirstFrame(frames, state => state.Front.LiftMeters > 0.0f);
            var backLifts = FirstFrame(frames, state => state.Back.LiftMeters > 0.0f);
            Assert.That(frontLifts, Is.GreaterThanOrEqualTo(0), "The front foot lifts.");
            Assert.That(backLifts, Is.GreaterThan(frontLifts), "The back foot follows.");
            for (var frame = 0; frame < backLifts; frame++)
            {
                Assert.That(frames[frame].Back.X, Is.EqualTo(frames[0].Back.X), $"The back foot stays planted until it lifts (frame {frame}).");
            }

            var frontLands = frontLifts + FirstFrame(frames.GetRange(frontLifts, frames.Count - frontLifts), state => state.Front.IsPlanted);
            Assert.That(frontLands, Is.GreaterThan(frontLifts), "The front foot lands.");
            Assert.That(Math.Abs(frames[frontLands].Front.X - frames[0].Front.X), Is.GreaterThan(0.5f * Math.Abs(side)), "The front foot stepped across, not along.");
            Assert.That(MaxOf(frames, state => Math.Abs(state.Front.Z - frames[0].Front.Z)), Is.LessThan(0.02f));
            foreach (var state in frames)
            {
                Assert.That(state.Confidence, Is.EqualTo(RemoteFootworkTuning.TrackedConfidence), "A side step is footwork, not walking.");
            }

            var last = frames[frames.Count - 1];
            Assert.That(last.Phase, Is.EqualTo(FootworkPhase.Standing));
            AssertInPlace(last, HipsAtStart + new Vector3(side, 0.0f, 0.0f), Hand.Right);
        }

        [TestCase(1.0f, FootworkPhase.Advancing)]
        [TestCase(-1.0f, FootworkPhase.Retreating)]
        public void ADiagonalStepMovesEachFootDiagonallyInOneStep(float along, FootworkPhase phase)
        {
            var move = new Vector3(0.15f, 0.0f, along * 0.25f);
            var frames = Run(1.2, time => new Pose(HipsAtStart + (move * Smooth(time - 0.1, 0.45)), 0.0f));

            var lead = phase == FootworkPhase.Advancing ? (Func<FootworkState, FootPlacement>)(state => state.Front) : state => state.Back;
            var leadLifts = FirstFrame(frames, state => lead(state).LiftMeters > 0.0f);
            Assert.That(leadLifts, Is.GreaterThanOrEqualTo(0), "The lead foot lifts.");
            Assert.That(frames[leadLifts].Phase, Is.EqualTo(phase));

            // Midway through the lead foot's swing it has gone both along and across.
            var midSwing = FirstFrame(frames, state => lead(state).LiftMeters >= 0.9f * FootworkTuning.FootLiftPerHeight * Height);
            Assert.That(midSwing, Is.GreaterThanOrEqualTo(0));
            Assert.That(lead(frames[midSwing]).X - lead(frames[0]).X, Is.GreaterThan(0.02f), "Across.");
            Assert.That((lead(frames[midSwing]).Z - lead(frames[0]).Z) * along, Is.GreaterThan(0.03f), "Along.");

            AssertInPlace(frames[frames.Count - 1], HipsAtStart + move, Hand.Right);
        }

        [TestCase(0.22f, 0.0f, 0.45)]
        [TestCase(-0.22f, 0.0f, 0.45)]
        [TestCase(0.15f, 0.2f, 0.45)]
        [TestCase(0.15f, -0.2f, 0.45)]
        [TestCase(0.0f, 0.324f, 0.45)]
        [TestCase(0.0f, -0.324f, 0.45)]
        [TestCase(0.2f, 0.25f, 0.3)]
        public void NoFootMovesFurtherInAFrameThanTheBladeSweeps(float across, float along, double seconds)
        {
            // Side steps, diagonals and full advances and retreats at a brisk pace, and a fast diagonal.
            var move = new Vector3(across, 0.0f, along);
            var frames = Run(1.5, time => new Pose(HipsAtStart + (move * Smooth(time - 0.1, seconds)), 0.0f));

            AssertInPlace(frames[frames.Count - 1], HipsAtStart + move, Hand.Right);
            for (var frame = 1; frame < frames.Count; frame++)
            {
                foreach (var (now, before) in new[] { (frames[frame].Front, frames[frame - 1].Front), (frames[frame].Back, frames[frame - 1].Back) })
                {
                    var jump = new Vector3(now.X - before.X, now.LiftMeters - before.LiftMeters, now.Z - before.Z).Length();
                    Assert.That(jump, Is.LessThan(0.05f), $"Frame {frame}");
                }
            }
        }

        [Test]
        public void ATrackingJumpStandsTheFeetAnew()
        {
            var jumped = HipsAtStart + new Vector3(0.0f, 0.0f, 1.0f);
            var frames = Run(0.5, time => new Pose(time < 0.25 ? HipsAtStart : jumped, 0.0f));

            var last = frames[frames.Count - 1];
            Assert.That(last.Phase, Is.EqualTo(FootworkPhase.Standing));
            AssertInPlace(last, jumped, Hand.Right);
        }

        [Test]
        public void ALeftHandersFeetAreMirrored()
        {
            var tracker = new FootworkStateTracker();
            tracker.Update(1.0, HipsAtStart + HeadFromHips, HipsAtStart, Height, Hand.Left);

            AssertInPlace(tracker.State, HipsAtStart, Hand.Left);
            var right = DummyLegs.EnGarde(Height, Hand.Right);
            var rightFrontAcross = right.Front.Ankle.X - ((right.Front.Hip.X + right.Back.Hip.X) * 0.5f);
            Assert.That(tracker.State.Front.X - HipsAtStart.X, Is.EqualTo(-rightFrontAcross).Within(1e-4f));
        }

        [Test]
        public void TimeThatDoesNotMoveOnChangesNothing()
        {
            var tracker = new FootworkStateTracker();
            tracker.Update(1.0, HipsAtStart + HeadFromHips, HipsAtStart, Height, Hand.Right);
            var before = tracker.State;

            tracker.Update(1.0, HipsAtStart + HeadFromHips + Vector3.UnitZ, HipsAtStart + Vector3.UnitZ, Height, Hand.Right);

            Assert.That(tracker.State.Front.Z, Is.EqualTo(before.Front.Z));
        }

        /// <summary>A min-jerk ramp from 0 to 1 over <paramref name="seconds"/>, starting at time 0.</summary>
        private static float Smooth(double time, double seconds)
        {
            return DummyFootwork.MinimumJerk((float)(time / seconds));
        }

        private static List<FootworkState> Run(double seconds, Func<double, Pose> poseAt)
        {
            var tracker = new FootworkStateTracker();
            var states = new List<FootworkState>();
            for (var frame = 0; frame * FrameSeconds <= seconds; frame++)
            {
                var time = frame * FrameSeconds;
                var pose = poseAt(time);
                var head = pose.Hips + HeadFromHips + new Vector3(0.0f, pose.HeadDrop, pose.HeadLean);
                tracker.Update(time, head, pose.HasHips ? pose.Hips : (Vector3?)null, Height, Hand.Right);
                states.Add(tracker.State);
            }

            return states;
        }

        private static int FirstFrame(IReadOnlyList<FootworkState> frames, Func<FootworkState, bool> isWanted)
        {
            for (var frame = 0; frame < frames.Count; frame++)
            {
                if (isWanted(frames[frame]))
                {
                    return frame;
                }
            }

            return -1;
        }

        private static float MaxOf(IReadOnlyList<FootworkState> frames, Func<FootworkState, float> read)
        {
            var max = float.MinValue;
            foreach (var state in frames)
            {
                max = Math.Max(max, read(state));
            }

            return max;
        }

        private static void AssertInPlace(FootworkState state, Vector3 hips, Hand hand, float tolerance = PlaceToleranceMeters)
        {
            var legs = DummyLegs.EnGarde(Height, hand);
            var center = (legs.Front.Hip + legs.Back.Hip) * 0.5f;
            var front = hips + legs.Front.Ankle - center;
            var back = hips + legs.Back.Ankle - center;
            Assert.That(new Vector2(state.Front.X - front.X, state.Front.Z - front.Z).Length(), Is.LessThan(tolerance), "Front foot in its en garde place.");
            Assert.That(new Vector2(state.Back.X - back.X, state.Back.Z - back.Z).Length(), Is.LessThan(tolerance), "Back foot in its en garde place.");
        }

        /// <summary>One frame of the test Fencer: the hips, and the head lowered and leaned forward from its en garde place.</summary>
        private readonly struct Pose
        {
            public Pose(Vector3 hips, float headDrop, float headLean = 0.0f, bool hasHips = true)
            {
                Hips = hips;
                HeadDrop = headDrop;
                HeadLean = headLean;
                HasHips = hasHips;
            }

            public Vector3 Hips { get; }

            /// <summary>Added to the head's height (negative lowers it).</summary>
            public float HeadDrop { get; }

            public float HeadLean { get; }

            public bool HasHips { get; }
        }
    }
}
