// The Dummy's stepping footwork (PER-110): it carries the Dummy to its target in real advances and retreats (front
// foot first on an advance, back foot first on a retreat), with planted feet that never slide, knees that bend as in
// en garde, legs that always reach, strides within the limit, and no jump the Blade's sweep could miss.
using System;
using System.Diagnostics;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Tests.Dummy
{
    public class DummyFootworkTests
    {
        private const float FrameSeconds = 1.0f / 72.0f;
        private const float Height = 1.8f;
        private const float Start = 2.0f;
        private const float Tolerance = 1e-4f;

        // The Blade restarts its sweep when the Target Zones jump more than 5 cm in a frame (BladeBehaviour.JumpMeters).
        private const float BladeJumpMeters = 0.05f;

        private static float MaxStride(float height) => FootworkTuning.MaxStridePerHeight * height;

        [Test]
        public void ItStandsInEnGardeUntilItHasSomewhereToGo()
        {
            var footwork = Create();

            footwork.Step(Start + (FootworkTuning.MinStepMeters * 0.5f), FrameSeconds);

            Assert.That(footwork.CurrentStep, Is.EqualTo(FootworkStep.Standing));
            Assert.That(footwork.DistanceMeters, Is.EqualTo(Start));
            AssertLegsNear(footwork.Legs, DummyLegs.EnGarde(Height, Hand.Right));
        }

        [TestCase(-0.3f)]
        [TestCase(0.3f)]
        [TestCase(-1.0f)]
        [TestCase(1.0f)]
        public void ItArrivesOnTheTargetInStepsNoLongerThanAStride(float move)
        {
            var footwork = Create();
            var target = Start + move;

            Run(footwork, target, 5.0f, null);

            Assert.That(footwork.DistanceMeters, Is.EqualTo(target).Within(Tolerance));
            Assert.That(footwork.CurrentStep, Is.EqualTo(FootworkStep.Standing));
            Assert.That(footwork.StepCount, Is.EqualTo((int)Math.Ceiling(Math.Abs(move) / MaxStride(Height) - 1e-4)));
            AssertLegsNear(footwork.Legs, DummyLegs.EnGarde(Height, Hand.Right));
        }

        [Test]
        public void AnAdvanceLeadsWithTheFrontFoot()
        {
            var footwork = Create();

            footwork.Step(Start - 0.3f, FrameSeconds);

            Assert.That(footwork.CurrentStep, Is.EqualTo(FootworkStep.Advancing));
            Assert.That(footwork.IsFrontFootPlanted, Is.False);
            Assert.That(footwork.IsBackFootPlanted, Is.True);
            var frontLanded = RunUntil(footwork, Start - 0.3f, () => footwork.IsFrontFootPlanted);
            Assert.That(frontLanded, Is.True);
            Assert.That(footwork.CurrentStep, Is.EqualTo(FootworkStep.Advancing), "The back foot follows after the front foot lands.");
        }

        [Test]
        public void ARetreatLeadsWithTheBackFoot()
        {
            var footwork = Create();

            footwork.Step(Start + 0.3f, FrameSeconds);

            Assert.That(footwork.CurrentStep, Is.EqualTo(FootworkStep.Retreating));
            Assert.That(footwork.IsBackFootPlanted, Is.False);
            Assert.That(footwork.IsFrontFootPlanted, Is.True);
            var backLanded = RunUntil(footwork, Start + 0.3f, () => footwork.IsBackFootPlanted);
            Assert.That(backLanded, Is.True);
            Assert.That(footwork.CurrentStep, Is.EqualTo(FootworkStep.Retreating), "The front foot follows after the back foot lands.");
        }

        [TestCase(-1.0f, Hand.Right)]
        [TestCase(1.0f, Hand.Right)]
        [TestCase(-1.0f, Hand.Left)]
        [TestCase(1.0f, Hand.Left)]
        public void APlantedFootNeverSlides(float move, Hand hand)
        {
            var footwork = new DummyFootwork(Height, hand, Start, new Random(7));
            Vector3? frontSpot = null;
            Vector3? backSpot = null;

            Run(footwork, Start + move, 5.0f, frame =>
            {
                frontSpot = CheckPinned(footwork.IsFrontFootPlanted, OnStrip(footwork, footwork.Legs.Front.Heel), frontSpot, $"Front foot, frame {frame}");
                backSpot = CheckPinned(footwork.IsBackFootPlanted, OnStrip(footwork, footwork.Legs.Back.Heel), backSpot, $"Back foot, frame {frame}");
            });
        }

        [TestCase(-1.0f, 1.5f)]
        [TestCase(1.0f, 1.5f)]
        [TestCase(-1.0f, 2.1f)]
        [TestCase(1.0f, 2.1f)]
        public void TheLegsAlwaysReachAndTheKneesBendForward(float move, float height)
        {
            var footwork = new DummyFootwork(height, Hand.Right, Start, new Random(3));
            var rest = DummyLegs.EnGarde(height, Hand.Right);

            Run(footwork, Start + move, 5.0f, frame =>
            {
                AssertLegReachesAndBends(footwork.Legs.Front, rest.Front, $"Front leg, frame {frame}");
                AssertLegReachesAndBends(footwork.Legs.Back, rest.Back, $"Back leg, frame {frame}");
            });
        }

        [TestCase(-1.0f)]
        [TestCase(1.0f)]
        public void FeetStayOnOrAboveTheFloorAndLiftOnlyALittle(float move)
        {
            var footwork = Create();
            var floor = DummyLegs.EnGarde(Height, Hand.Right).Front.Heel.Y;
            var highest = 0.0f;

            Run(footwork, Start + move, 5.0f, frame =>
            {
                foreach (var point in new[] { footwork.Legs.Front.Heel, footwork.Legs.Front.Toe, footwork.Legs.Back.Heel, footwork.Legs.Back.Toe })
                {
                    Assert.That(point.Y, Is.GreaterThanOrEqualTo(floor - Tolerance), $"Frame {frame}");
                    highest = Math.Max(highest, point.Y - floor);
                }
            });

            Assert.That(highest, Is.GreaterThan(0.02f), "The swinging foot lifts.");
            Assert.That(highest, Is.LessThan(0.15f), "Fencing steps stay low.");
        }

        [TestCase(-1.0f)]
        [TestCase(1.0f)]
        public void TheStanceNeverNarrowsAndWidensByAtMostAStride(float move)
        {
            var footwork = Create();
            var rest = DummyLegs.EnGarde(Height, Hand.Right);
            var restWidth = rest.Front.Ankle.Z - rest.Back.Ankle.Z;

            Run(footwork, Start + move, 5.0f, frame =>
            {
                var width = footwork.Legs.Front.Ankle.Z - footwork.Legs.Back.Ankle.Z;
                Assert.That(width, Is.GreaterThanOrEqualTo(restWidth - 1e-3f), $"Frame {frame}");
                Assert.That(width, Is.LessThanOrEqualTo(restWidth + MaxStride(Height) + 1e-3f), $"Frame {frame}");
            });
        }

        [TestCase(-1.0f, 2.1f)]
        [TestCase(1.0f, 2.1f)]
        [TestCase(-1.0f, 1.5f)]
        public void NothingJumpsFurtherInAFrameThanTheBladeSweeps(float move, float height)
        {
            var footwork = new DummyFootwork(height, Hand.Right, Start, new Random(11));
            var layout = DummyLayout.Create(height, Hand.Right);
            var before = layout.GetBodyOnStrip(footwork.DistanceMeters);

            Run(footwork, Start + move, 5.0f, frame =>
            {
                layout.PoseLegs(footwork.Legs);
                var now = layout.GetBodyOnStrip(footwork.DistanceMeters);
                for (var part = 0; part < now.Count; part++)
                {
                    Assert.That(Vector3.Distance(now[part].Start, before[part].Start), Is.LessThan(BladeJumpMeters), $"Part {part}, frame {frame}");
                    Assert.That(Vector3.Distance(now[part].End, before[part].End), Is.LessThan(BladeJumpMeters), $"Part {part}, frame {frame}");
                }

                before = now;
            });
        }

        [Test]
        public void TheBodyStartsAndStopsWithoutAJolt()
        {
            var footwork = Create();
            var previous = footwork.DistanceMeters;

            footwork.Step(Start - 0.3f, FrameSeconds);
            var firstFrameMove = Math.Abs(footwork.DistanceMeters - previous);
            float lastFrameMove = 0.0f;
            Run(footwork, Start - 0.3f, 2.0f, frame =>
            {
                if (footwork.CurrentStep != FootworkStep.Standing)
                {
                    lastFrameMove = Math.Abs(footwork.DistanceMeters - previous);
                }

                previous = footwork.DistanceMeters;
            });

            Assert.That(firstFrameMove, Is.LessThan(0.002f));
            Assert.That(lastFrameMove, Is.LessThan(0.002f));
        }

        [Test]
        public void AStepIsFinishedBeforeTurningBack()
        {
            var footwork = Create();
            for (var frame = 0; frame < 5; frame++)
            {
                footwork.Step(Start - 0.3f, FrameSeconds);
            }

            var turnedBackAt = footwork.StepCount;
            var previous = footwork.DistanceMeters;
            while (footwork.StepCount == turnedBackAt)
            {
                footwork.Step(Start + 0.3f, FrameSeconds);
                if (footwork.StepCount == turnedBackAt)
                {
                    Assert.That(footwork.DistanceMeters, Is.LessThanOrEqualTo(previous + Tolerance), "Still advancing.");
                    previous = footwork.DistanceMeters;
                }
            }

            Assert.That(footwork.CurrentStep, Is.EqualTo(FootworkStep.Retreating));
            Run(footwork, Start + 0.3f, 3.0f, null);
            Assert.That(footwork.DistanceMeters, Is.EqualTo(Start + 0.3f).Within(Tolerance));
        }

        [Test]
        public void ALeftHandedDummysLegsMirrorARightHandedOnes()
        {
            var right = new DummyFootwork(Height, Hand.Right, Start, new Random(5));
            var left = new DummyFootwork(Height, Hand.Left, Start, new Random(5));

            for (var frame = 0; frame < 20; frame++)
            {
                right.Step(Start - 0.3f, FrameSeconds);
                left.Step(Start - 0.3f, FrameSeconds);
            }

            AssertNear(left.Legs.Front.Knee, HandMirroring.MirrorPosition(right.Legs.Front.Knee));
            AssertNear(left.Legs.Back.Toe, HandMirroring.MirrorPosition(right.Legs.Back.Toe));
            Assert.That(left.DistanceMeters, Is.EqualTo(right.DistanceMeters));
        }

        [Test]
        public void StepsVaryALittleButRepeatForTheSameSeed()
        {
            var frames = new int[3];
            var repeatFrames = new int[3];
            CountStepFrames(new Random(42), frames);
            CountStepFrames(new Random(42), repeatFrames);

            Assert.That(repeatFrames, Is.EqualTo(frames));
            Assert.That(frames[0] != frames[1] || frames[1] != frames[2], Is.True, "Three equal steps should not all take the same time.");
        }

        [Test]
        public void ItRetreatsOnTheDistanceKeepersIntentWhenTheFencerAdvances()
        {
            var keeper = new DistanceKeeper(3.0f, 3.0f, DummyStripLimits.Unlimited, DummyDifficulty.Hard, new Random(1));
            var footwork = new DummyFootwork(Height, Hand.Right, keeper.TargetFrontFootZ, new Random(1));
            var now = 0.0;

            for (var frame = 0; frame < 72 * 4; frame++)
            {
                now += FrameSeconds;

                // The Fencer advances 33 cm over the first 0.4 s, then stands.
                var fencer = (float)Math.Min(0.33, now / 0.4 * 0.33);
                keeper.Step(now, fencer);
                footwork.Step(keeper.TargetFrontFootZ, FrameSeconds);
            }

            Assert.That(keeper.State, Is.EqualTo(DistanceKeeperState.Holding));
            Assert.That(footwork.DistanceMeters, Is.EqualTo(keeper.TargetFrontFootZ).Within(FootworkTuning.MinStepMeters));
            Assert.That(footwork.DistanceMeters, Is.GreaterThan(3.0f + 0.25f), "It retreated to keep the distance.");
            Assert.That(footwork.StepCount, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void NoTimeNoMove()
        {
            var footwork = Create();

            footwork.Step(Start + 1.0f, 0.0f);

            Assert.That(footwork.DistanceMeters, Is.EqualTo(Start));
            Assert.That(footwork.CurrentStep, Is.EqualTo(FootworkStep.Standing));
        }

        [Test]
        public void ANonFiniteTargetIsRejected()
        {
            var footwork = Create();

            Assert.Throws<ArgumentOutOfRangeException>(() => footwork.Step(float.NaN, FrameSeconds));
        }

        [Test]
        public void AFrameCostsMicroseconds()
        {
            var footwork = Create();
            const int frames = 20000;
            var watch = Stopwatch.StartNew();
            for (var frame = 0; frame < frames; frame++)
            {
                // Back and forth a full stride each way, so nearly every frame is mid-step.
                var target = (frame / 40) % 2 == 0 ? Start - 0.5f : Start + 0.5f;
                footwork.Step(target, FrameSeconds);
            }

            watch.Stop();
            var microsecondsPerFrame = watch.Elapsed.TotalMilliseconds * 1000.0 / frames;
            TestContext.WriteLine($"DummyFootwork.Step: {microsecondsPerFrame:0.00} us per frame.");

            // 72 Hz gives 13.9 ms a frame; this must be a sliver of it even on the Quest's slower CPU.
            Assert.That(microsecondsPerFrame, Is.LessThan(50.0));
        }

        private static DummyFootwork Create()
        {
            return new DummyFootwork(Height, Hand.Right, Start, new Random(1));
        }

        private static void Run(DummyFootwork footwork, float target, float seconds, Action<int> onFrame)
        {
            var frames = (int)(seconds / FrameSeconds);
            for (var frame = 0; frame < frames; frame++)
            {
                footwork.Step(target, FrameSeconds);
                onFrame?.Invoke(frame);
            }
        }

        private static bool RunUntil(DummyFootwork footwork, float target, Func<bool> condition)
        {
            for (var frame = 0; frame < 144; frame++)
            {
                footwork.Step(target, FrameSeconds);
                if (condition())
                {
                    return true;
                }
            }

            return false;
        }

        private static void CountStepFrames(Random random, int[] frames)
        {
            var footwork = new DummyFootwork(Height, Hand.Right, Start, random);
            var target = Start - (3.0f * MaxStride(Height));
            for (var frame = 0; frame < 72 * 5 && footwork.StepCount <= frames.Length; frame++)
            {
                footwork.Step(target, FrameSeconds);
                if (footwork.CurrentStep != FootworkStep.Standing && footwork.StepCount <= frames.Length)
                {
                    frames[footwork.StepCount - 1]++;
                }
            }
        }

        private static Vector3 OnStrip(DummyFootwork footwork, Vector3 pointInDummyFrame)
        {
            return DummyLayout.DummyToStrip(pointInDummyFrame, footwork.DistanceMeters);
        }

        /// <summary>A planted foot's spot: kept from the frame it planted, and checked unchanged every frame after.</summary>
        private static Vector3? CheckPinned(bool isPlanted, Vector3 heelOnStrip, Vector3? spot, string message)
        {
            if (!isPlanted)
            {
                return null;
            }

            if (spot.HasValue)
            {
                Assert.That(Vector3.Distance(heelOnStrip, spot.Value), Is.LessThan(1e-3f), message);
                return spot;
            }

            return heelOnStrip;
        }

        private static void AssertLegReachesAndBends(LegPose leg, LegPose rest, string message)
        {
            var thigh = Vector3.Distance(rest.Hip, rest.Knee);
            var shin = Vector3.Distance(rest.Knee, rest.Ankle);
            Assert.That(Vector3.Distance(leg.Hip, leg.Ankle), Is.LessThan((thigh + shin) * 0.999f), message + ": the leg is overstretched.");
            Assert.That(Vector3.Distance(leg.Hip, leg.Knee), Is.EqualTo(thigh).Within(1e-3f), message);
            Assert.That(Vector3.Distance(leg.Knee, leg.Ankle), Is.EqualTo(shin).Within(1e-3f), message);

            // The knee stays on the side of the hip-to-ankle line it is on in en garde: forward (and out for the back leg).
            Assert.That(Vector3.Dot(KneeOffLine(leg), KneeOffLine(rest)), Is.GreaterThan(0.0f), message + ": the knee bends the wrong way.");
        }

        private static Vector3 KneeOffLine(LegPose leg)
        {
            var along = Vector3.Normalize(leg.Ankle - leg.Hip);
            var toKnee = leg.Knee - leg.Hip;
            return toKnee - (along * Vector3.Dot(toKnee, along));
        }

        private static void AssertLegsNear(DummyLegs actual, DummyLegs expected)
        {
            foreach (var (got, want) in new[] { (actual.Front, expected.Front), (actual.Back, expected.Back) })
            {
                AssertNear(got.Hip, want.Hip);
                AssertNear(got.Knee, want.Knee);
                AssertNear(got.Ankle, want.Ankle);
                AssertNear(got.Heel, want.Heel);
                AssertNear(got.Toe, want.Toe);
            }
        }

        private static void AssertNear(Vector3 actual, Vector3 expected)
        {
            Assert.That(Vector3.Distance(actual, expected), Is.LessThan(Tolerance), $"{actual} vs {expected}");
        }
    }
}
