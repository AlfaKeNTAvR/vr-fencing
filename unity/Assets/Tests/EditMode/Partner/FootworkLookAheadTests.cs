// The opponent's steps read with a look-ahead (PER-142): the sender reads its feet from hips 60 ms further on, and the
// receiver pairs those feet with the body from that moment. On advances, retreats and side steps the lead foot now
// starts no later than the hips; a planted foot never slides; the hips always stay between the feet along the Strip;
// lunges, jumps and height changes come out as before.
using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Dummy;
using VrFencing.Partner;
using VrFencing.Session;
using VrFencing.Settings;

namespace VrFencing.Tests.Partner
{
    public class FootworkLookAheadTests
    {
        private const double FrameSeconds = 1.0 / 72.0;
        private const float Height = 1.65f;

        // A planted foot may not move further than this between frames.
        private const float SlideToleranceMeters = 0.001f;

        private static readonly Vector3 HipsAtStart = new Vector3(0.1f, 0.5f * Height, 0.0f);
        private static readonly Vector3 HeadFromHips = new Vector3(0.015f * Height, 0.40f * Height, 0.055f * Height);

        private static IEnumerable<TestCaseData> Steps()
        {
            yield return new TestCaseData(new Vector3(0.0f, 0.0f, 0.18f * Height), 0.45, true).SetName("Advance");
            yield return new TestCaseData(new Vector3(0.0f, 0.0f, -0.18f * Height), 0.45, false).SetName("Retreat");
            yield return new TestCaseData(new Vector3(0.0f, 0.0f, 0.12f * Height), 0.3, true).SetName("QuickAdvance");
            yield return new TestCaseData(new Vector3(0.0f, 0.0f, -0.12f * Height), 0.3, false).SetName("QuickRetreat");
            yield return new TestCaseData(new Vector3(0.22f, 0.0f, 0.0f), 0.45, true).SetName("SideStepRight");
            yield return new TestCaseData(new Vector3(-0.22f, 0.0f, 0.0f), 0.45, true).SetName("SideStepLeft");
        }

        [TestCaseSource(nameof(Steps))]
        public void TheLeadFootStartsNoLaterThanTheHips(Vector3 move, double seconds, bool isFrontLead)
        {
            Pose At(double time) => new Pose(HipsAtStart + (move * Smooth(time - 0.2, seconds)), 0.0f);
            var lookAhead = RunLookAhead(1.5, At);
            var plain = RunPlain(1.5, At);

            // The hips start when they reach the step onset speed over the floor.
            var hipsStart = 0.2 + FindOnset(seconds, move.Length());
            double LeadStart(IReadOnlyList<Shown> shown)
            {
                foreach (var frame in shown)
                {
                    var lead = isFrontLead ? frame.State.Front : frame.State.Back;
                    var start = isFrontLead ? shown[0].State.Front : shown[0].State.Back;
                    if (lead.LiftMeters > 0.0f || new Vector2(lead.X - start.X, lead.Z - start.Z).Length() > 0.001f)
                    {
                        return frame.TimeSeconds;
                    }
                }

                return double.PositiveInfinity;
            }

            var withLookAhead = LeadStart(lookAhead);
            var without = LeadStart(plain);
            TestContext.WriteLine($"Hips start {hipsStart:0.000} s; lead foot starts {withLookAhead:0.000} s with the look-ahead, {without:0.000} s without.");
            Assert.That(withLookAhead, Is.LessThanOrEqualTo(hipsStart + 1e-6), "The lead foot moves no later than the hips.");
            Assert.That(without, Is.GreaterThan(hipsStart), "Before, it moved after them.");
            AssertInPlace(lookAhead[lookAhead.Count - 1].State, HipsAtStart + move);
        }

        [TestCaseSource(nameof(AllTracks))]
        public void APlantedFootNeverSlides(string name, Func<double, Pose> track)
        {
            var shown = RunLookAhead(3.0, track);
            for (var index = 1; index < shown.Count; index++)
            {
                foreach (var isFront in new[] { true, false })
                {
                    var before = isFront ? shown[index - 1].State.Front : shown[index - 1].State.Back;
                    var now = isFront ? shown[index].State.Front : shown[index].State.Back;
                    if (before.IsPlanted && now.IsPlanted)
                    {
                        var moved = new Vector2(now.X - before.X, now.Z - before.Z).Length();
                        Assert.That(moved, Is.LessThan(SlideToleranceMeters), $"{name}: {(isFront ? "front" : "back")} foot at {shown[index].TimeSeconds:0.000} s.");
                    }
                }
            }
        }

        [TestCaseSource(nameof(AllTracks))]
        public void TheHipsStayBetweenTheFeetAlongTheStrip(string name, Func<double, Pose> track)
        {
            var shown = RunLookAhead(3.0, track);
            foreach (var frame in shown)
            {
                var hips = track(frame.TimeSeconds).Hips;
                Assert.That(frame.State.Front.Z, Is.GreaterThanOrEqualTo(hips.Z), $"{name}: front foot behind the hips at {frame.TimeSeconds:0.000} s.");
                Assert.That(frame.State.Back.Z, Is.LessThanOrEqualTo(hips.Z), $"{name}: back foot ahead of the hips at {frame.TimeSeconds:0.000} s.");
            }
        }

        [Test]
        public void HipsThatLeaveTheFeetSidewaysSlowlyGetAFootUnderThem()
        {
            // 12 cm to the right in 3 s: far too slow for a step.
            Pose At(double time) => new Pose(HipsAtStart + new Vector3(0.12f * (float)Math.Min(time / 3.0, 1.0), 0.0f, 0.0f), 0.0f);
            var shown = RunLookAhead(4.5, At);

            var last = shown[shown.Count - 1].State;
            var hips = At(4.5).Hips;
            var margin = RemoteFootworkTuning.BalanceMarginPerHeight * Height;
            Assert.That(Math.Max(last.Front.X, last.Back.X) + margin, Is.GreaterThanOrEqualTo(hips.X), "A foot has stepped out under the hips.");
        }

        [Test]
        public void ALungeComesOutAsBefore()
        {
            var hipTravel = 0.38f * Height;
            var headDrop = 0.12f * Height;
            Pose At(double time)
            {
                var fraction = Smooth(time - 0.2, 0.35) - Smooth(time - 1.0, 0.5);
                return new Pose(HipsAtStart + new Vector3(0.0f, 0.0f, hipTravel * fraction), -headDrop * fraction);
            }

            var lookAhead = RunLookAhead(2.2, At);
            var plain = RunPlain(2.2, At);

            foreach (var shown in new[] { lookAhead, plain })
            {
                var lunging = shown.FindIndex(frame => frame.State.Phase == FootworkPhase.Lunging);
                var recovering = shown.FindIndex(frame => frame.State.Phase == FootworkPhase.Recovering);
                Assert.That(lunging, Is.GreaterThanOrEqualTo(0));
                Assert.That(recovering, Is.GreaterThan(lunging));
                for (var index = 0; index < recovering; index++)
                {
                    Assert.That(shown[index].State.Back.Z, Is.EqualTo(shown[0].State.Back.Z).Within(1e-4f), "The back foot stays planted.");
                }
            }

            // The front foot lands as far out, at about the same moment, and both end in en garde.
            var landedNew = FrontAtFullLunge(lookAhead);
            var landedOld = FrontAtFullLunge(plain);
            TestContext.WriteLine($"Front foot at full lunge: {landedNew:0.000} m with the look-ahead, {landedOld:0.000} m before.");
            Assert.That(landedNew, Is.EqualTo(landedOld).Within(0.02f));
            Assert.That(lookAhead[lookAhead.Count - 1].State.Phase, Is.EqualTo(FootworkPhase.Standing));
            AssertInPlace(lookAhead[lookAhead.Count - 1].State, HipsAtStart);
        }

        [Test]
        public void JumpsAndHeightChangesMoveNoFootAsBefore()
        {
            // A crouch, a jump and standing up: only the height changes.
            Pose At(double time)
            {
                var height = (float)(-0.08 * Smooth(time - 0.2, 0.4) + (0.15 * Math.Max(0.0, Math.Sin(Math.Max(0.0, time - 1.0) * Math.PI / 0.4)) * (time < 1.4 ? 1.0 : 0.0)));
                return new Pose(HipsAtStart + new Vector3(0.0f, height, 0.0f), height);
            }

            var lookAhead = RunLookAhead(2.5, At);
            var plain = RunPlain(2.5, At);
            for (var index = 0; index < lookAhead.Count; index++)
            {
                Assert.That(lookAhead[index].State.Front.IsPlanted && lookAhead[index].State.Back.IsPlanted, Is.True);
                Assert.That(lookAhead[index].State.Front.Z, Is.EqualTo(plain[0].State.Front.Z));
                Assert.That(lookAhead[index].State.Back.Z, Is.EqualTo(plain[0].State.Back.Z));
            }

            foreach (var frame in plain)
            {
                Assert.That(frame.State.Front.IsPlanted && frame.State.Back.IsPlanted, Is.True);
            }
        }

        [Test]
        public void TheReceiverTakesTheFeetFromTheLookAheadLater()
        {
            // Each body carries feet whose front z says which body time they belong to: the sender time less the look-ahead.
            var stream = new BodyStream();
            for (var sample = 0; sample < 12; sample++)
            {
                var time = sample / 30.0;
                var feetTime = (float)(time - FootworkLookAhead.Seconds);
                var feet = new FootworkState(FootworkPhase.Standing, new FootPlacement(0.0f, feetTime, 0.0f, 0.0f), default, 1.0f);
                stream.Add(new BodySample(time, PartnerTestData.Body(BodyTrackingFidelity.High, new Vector3(0.0f, 0.0f, (float)time)).WithFootwork(feet)));
            }

            var drawnAt = 0.2;
            var body = stream.Sample(drawnAt + BodyStream.BufferSeconds);

            Assert.That(body.Footwork.Value.Front.Z, Is.EqualTo((float)drawnAt).Within(1e-4f), "The feet for the drawn moment.");
            Assert.That(body.Get(SkeletonJoint.HipCenter).Value.Z - PartnerTestData.Body(BodyTrackingFidelity.High, Vector3.Zero).Get(SkeletonJoint.HipCenter).Value.Z, Is.EqualTo((float)drawnAt).Within(1e-4f));
        }

        [Test]
        public void OnTheRecordingTheDrawnFeetNeverSlideAndStraddleTheHips()
        {
            var replay = MirrorRecordingReplay.Run();
            var worstSlide = 0.0f;
            var worstStraddle = float.MaxValue;
            for (var index = 1; index < replay.Frames.Count; index++)
            {
                var before = replay.Frames[index - 1].View.Body;
                var now = replay.Frames[index].View.Body;
                foreach (var isFront in new[] { true, false })
                {
                    var wasPlanted = (isFront ? before.Footwork?.Front : before.Footwork?.Back)?.IsPlanted == true;
                    var isPlanted = (isFront ? now.Footwork?.Front : now.Footwork?.Back)?.IsPlanted == true;
                    var joint = isFront ? SkeletonJoint.FrontAnkle : SkeletonJoint.BackAnkle;
                    // On the floor: a leg too short for hips standing taller than en garde hangs its foot just above it.
                    var onFloor = EnGardePose.FrontAnkle.Y * replay.HeightMeters + 0.005f;
                    if (wasPlanted && isPlanted && before.Get(joint).Value.Y <= onFloor && now.Get(joint).Value.Y <= onFloor)
                    {
                        var moved = new Vector2(now.Get(joint).Value.X - before.Get(joint).Value.X, now.Get(joint).Value.Z - before.Get(joint).Value.Z).Length();
                        worstSlide = Math.Max(worstSlide, moved);
                    }
                }

                // The opponent faces -z: its front foot has the lower z.
                var hips = now.Get(SkeletonJoint.HipCenter).Value.Z;
                worstStraddle = Math.Min(worstStraddle, Math.Min(hips - now.Get(SkeletonJoint.FrontAnkle).Value.Z, now.Get(SkeletonJoint.BackAnkle).Value.Z - hips));
            }

            // As many steps read as before: the look-ahead and the balance rule add no steps on their own.
            var plain = new FootworkStateTracker();
            var stepsBefore = 0;
            var stepsNow = 0;
            var wasStepping = false;
            var wasSteppingNow = false;
            foreach (var frame in replay.Frames)
            {
                plain.Update(frame.TimeSeconds, frame.Recorded.Head?.Position, frame.Sent.Get(SkeletonJoint.HipCenter), replay.HeightMeters, replay.Hand);
                var isStepping = plain.State.Phase == FootworkPhase.Advancing || plain.State.Phase == FootworkPhase.Retreating;
                var isSteppingNow = frame.SentFootwork.Phase == FootworkPhase.Advancing || frame.SentFootwork.Phase == FootworkPhase.Retreating;
                stepsBefore += isStepping && !wasStepping ? 1 : 0;
                stepsNow += isSteppingNow && !wasSteppingNow ? 1 : 0;
                wasStepping = isStepping;
                wasSteppingNow = isSteppingNow;
            }

            TestContext.WriteLine($"Recording: {stepsNow} steps read ({stepsBefore} before); a planted foot moved at most {worstSlide * 1000.0f:0.0} mm in a frame; the hips came at most {-Math.Min(0.0f, worstStraddle) * 1000.0f:0.0} mm outside the feet.");
            Assert.That(stepsNow, Is.LessThanOrEqualTo(stepsBefore + 3));
            Assert.That(worstSlide, Is.LessThan(SlideToleranceMeters));
            Assert.That(worstStraddle, Is.GreaterThanOrEqualTo(0.0f));
        }

        private static IEnumerable<TestCaseData> AllTracks()
        {
            foreach (var step in Steps())
            {
                var move = (Vector3)step.Arguments[0];
                var seconds = (double)step.Arguments[1];
                yield return new TestCaseData(step.TestName, (Func<double, Pose>)(time => new Pose(HipsAtStart + (move * Smooth(time - 0.2, seconds)), 0.0f))).SetName("Slide " + step.TestName);
            }

            yield return new TestCaseData("two advances", (Func<double, Pose>)(time => new Pose(HipsAtStart + new Vector3(0.0f, 0.0f, 0.2f * (Smooth(time - 0.2, 0.4) + Smooth(time - 0.8, 0.4))), 0.0f))).SetName("Slide TwoAdvances");
            yield return new TestCaseData("advance retreat", (Func<double, Pose>)(time => new Pose(HipsAtStart + new Vector3(0.0f, 0.0f, 0.25f * (Smooth(time - 0.2, 0.4) - Smooth(time - 0.9, 0.4))), 0.0f))).SetName("Slide AdvanceRetreat");
            yield return new TestCaseData("long slow advance", (Func<double, Pose>)(time => new Pose(HipsAtStart + new Vector3(0.0f, 0.0f, 0.35f * Smooth(time - 0.2, 2.0)), 0.0f))).SetName("Slide LongSlowAdvance");
            yield return new TestCaseData("slow drift back", (Func<double, Pose>)(time => new Pose(HipsAtStart + new Vector3(0.0f, 0.0f, -0.25f * (float)Math.Min(Math.Max(time - 0.2, 0.0) / 2.5, 1.0)), 0.0f))).SetName("Slide SlowDriftBack");
            yield return new TestCaseData("diagonal", (Func<double, Pose>)(time => new Pose(HipsAtStart + new Vector3(0.15f, 0.0f, 0.25f * Smooth(time - 0.2, 0.45)), 0.0f))).SetName("Slide Diagonal");
            yield return new TestCaseData("sway", (Func<double, Pose>)(time => new Pose(HipsAtStart + new Vector3(0.05f * (float)Math.Sin(time * 3.0), 0.0f, 0.04f * (float)Math.Sin(time * 2.0)), 0.0f))).SetName("Slide Sway");
        }

        /// <summary>The time from a min-jerk move's start at which its speed reaches the step onset speed.</summary>
        private static double FindOnset(double seconds, float distance)
        {
            for (var time = 0.0; time < seconds; time += 0.0005)
            {
                var speed = (Smooth(time + 0.0005, seconds) - Smooth(time, seconds)) * distance / 0.0005;
                if (speed >= RemoteFootworkTuning.StepOnsetMetersPerSecond)
                {
                    return time;
                }
            }

            return seconds;
        }

        private static float FrontAtFullLunge(List<Shown> shown)
        {
            return shown.Find(frame => frame.TimeSeconds >= 0.9).State.Front.Z;
        }

        private static float Smooth(double time, double seconds)
        {
            return DummyFootwork.MinimumJerk((float)(time / seconds));
        }

        /// <summary>The sender with the look-ahead: each state is shown with the body from <see cref="FootworkLookAhead.Seconds"/> earlier.</summary>
        private static List<Shown> RunLookAhead(double seconds, Func<double, Pose> poseAt)
        {
            var lookAhead = new FootworkLookAhead();
            var shown = new List<Shown>();
            for (var frame = 0; frame * FrameSeconds <= seconds + FootworkLookAhead.Seconds; frame++)
            {
                var time = frame * FrameSeconds;
                var pose = poseAt(time);
                lookAhead.Update(time, pose.Hips + HeadFromHips + new Vector3(0.0f, pose.HeadDrop, 0.0f), pose.Hips, Height, Hand.Right);
                var shownAt = time - FootworkLookAhead.Seconds;
                if (shownAt >= 0.0 && lookAhead.State.Confidence > 0.0f)
                {
                    shown.Add(new Shown(shownAt, lookAhead.State));
                }
            }

            return shown;
        }

        /// <summary>The sender as before: each state shown with the body of the same frame.</summary>
        private static List<Shown> RunPlain(double seconds, Func<double, Pose> poseAt)
        {
            var tracker = new FootworkStateTracker();
            var shown = new List<Shown>();
            for (var frame = 0; frame * FrameSeconds <= seconds; frame++)
            {
                var time = frame * FrameSeconds;
                var pose = poseAt(time);
                tracker.Update(time, pose.Hips + HeadFromHips + new Vector3(0.0f, pose.HeadDrop, 0.0f), pose.Hips, Height, Hand.Right);
                shown.Add(new Shown(time, tracker.State));
            }

            return shown;
        }

        private static void AssertInPlace(FootworkState state, Vector3 hips)
        {
            var legs = DummyLegs.EnGarde(Height, Hand.Right);
            var center = (legs.Front.Hip + legs.Back.Hip) * 0.5f;
            var front = hips + legs.Front.Ankle - center;
            var back = hips + legs.Back.Ankle - center;
            Assert.That(new Vector2(state.Front.X - front.X, state.Front.Z - front.Z).Length(), Is.LessThan(0.03f), "Front foot in its en garde place.");
            Assert.That(new Vector2(state.Back.X - back.X, state.Back.Z - back.Z).Length(), Is.LessThan(0.03f), "Back foot in its en garde place.");
        }

        public readonly struct Pose
        {
            public Pose(Vector3 hips, float headDrop)
            {
                Hips = hips;
                HeadDrop = headDrop;
            }

            public Vector3 Hips { get; }

            public float HeadDrop { get; }
        }

        private readonly struct Shown
        {
            public Shown(double timeSeconds, FootworkState state)
            {
                TimeSeconds = timeSeconds;
                State = state;
            }

            /// <summary>The body time these feet are drawn with.</summary>
            public double TimeSeconds { get; }

            public FootworkState State { get; }
        }
    }
}
