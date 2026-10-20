// The Dummy's side and diagonal footwork (PER-118): it side-steps across the Strip front foot first and steps
// diagonally in one step within a stride, on planted feet that never slide, with legs of fixed length that always
// reach and nothing moving further in a frame than the Blade's sweep; the distance keeper follows the Fencer's side a
// little, one reaction late and inside the Strip's corridor; and a standing Dummy sways a little from side to side.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Tests.Dummy
{
    public class DummySideFootworkTests
    {
        private const float FrameSeconds = 1.0f / 72.0f;
        private const float Height = 1.8f;
        private const float Start = 2.0f;
        private const float Gap = 1.9f;
        private const float Tolerance = 1e-3f;

        // The Blade restarts its sweep when the Target Zones jump more than 5 cm in a frame (BladeBehaviour.JumpMeters).
        private const float BladeJumpMeters = 0.05f;

        [TestCase(Hand.Right, 0.2f)]
        [TestCase(Hand.Right, -0.2f)]
        [TestCase(Hand.Left, 0.2f)]
        public void ASideStepCarriesTheWholeBodyAcrossFrontFootFirst(Hand hand, float side)
        {
            var footwork = new DummyFootwork(Height, hand, Start, new Random(1), isAlive: false);
            var restHips = HipCenterOnStrip(footwork);
            var restFrontAnkle = OnStrip(footwork, footwork.Legs.Front.Ankle);

            footwork.Step(Start, side, FrameSeconds);
            Assert.That(footwork.CurrentStep, Is.EqualTo(FootworkStep.SteppingSideways));
            Assert.That(footwork.IsFrontFootPlanted, Is.False, "The front foot leads.");
            Assert.That(footwork.IsBackFootPlanted, Is.True);
            Run(footwork, Start, side, 2.0f, null);

            Assert.That(footwork.CurrentStep, Is.EqualTo(FootworkStep.Standing));
            Assert.That(footwork.SideMeters, Is.EqualTo(side).Within(Tolerance));
            Assert.That(footwork.DistanceMeters, Is.EqualTo(Start).Within(Tolerance), "No move along the Strip.");
            Assert.That(footwork.StepCount, Is.EqualTo(1));
            AssertNear(HipCenterOnStrip(footwork), restHips + new Vector3(side, 0.0f, 0.0f), "The hips moved across by the side.");
            AssertNear(OnStrip(footwork, footwork.Legs.Front.Ankle), restFrontAnkle + new Vector3(side, 0.0f, 0.0f), "The front foot too.");
        }

        [Test]
        public void ALongMoveAcrossIsSeveralSideStepsOfAtMostASideStride()
        {
            var footwork = new DummyFootwork(Height, Hand.Right, Start, new Random(2), isAlive: false);
            var maxSideStride = FootworkTuning.MaxSideStridePerHeight * Height;
            var previousSide = footwork.SideMeters;
            var stepSide = footwork.SideMeters;
            var steps = 0;

            Run(footwork, Start, 0.5f, 5.0f, _ =>
            {
                if (footwork.StepCount != steps)
                {
                    steps = footwork.StepCount;
                    stepSide = previousSide;
                }

                Assert.That(Math.Abs(footwork.SideMeters - stepSide), Is.LessThanOrEqualTo(maxSideStride + Tolerance));
                previousSide = footwork.SideMeters;
            });

            Assert.That(footwork.SideMeters, Is.EqualTo(0.5f).Within(Tolerance));
            Assert.That(footwork.StepCount, Is.EqualTo((int)Math.Ceiling(0.5f / maxSideStride)));
        }

        [Test]
        public void ADiagonalWithinAStrideIsOneStepThatArrivesBothWays()
        {
            var footwork = new DummyFootwork(Height, Hand.Right, Start, new Random(3), isAlive: false);

            footwork.Step(Start - 0.2f, 0.15f, FrameSeconds);
            Assert.That(footwork.CurrentStep, Is.EqualTo(FootworkStep.Advancing), "Mostly along the Strip: an advance.");
            Run(footwork, Start - 0.2f, 0.15f, 2.0f, null);

            Assert.That(footwork.StepCount, Is.EqualTo(1));
            Assert.That(footwork.DistanceMeters, Is.EqualTo(Start - 0.2f).Within(Tolerance));
            Assert.That(footwork.SideMeters, Is.EqualTo(0.15f).Within(Tolerance));
        }

        [Test]
        public void ADiagonalIsNeverLongerThanAStraightStride()
        {
            var footwork = new DummyFootwork(Height, Hand.Right, Start, new Random(4), isAlive: false);
            var maxStride = FootworkTuning.MaxStridePerHeight * Height;

            footwork.Step(Start + 1.0f, 1.0f, FrameSeconds);
            var stepFrom = new Vector2(Start, 0.0f);
            while (footwork.StepCount == 1)
            {
                Assert.That(Vector2.Distance(new Vector2(footwork.DistanceMeters, footwork.SideMeters), stepFrom), Is.LessThanOrEqualTo(maxStride + Tolerance));
                footwork.Step(Start + 1.0f, 1.0f, FrameSeconds);
            }
        }

        [TestCase(Hand.Right, -0.6f, 0.4f)]
        [TestCase(Hand.Right, 0.6f, -0.4f)]
        [TestCase(Hand.Left, 0.0f, 0.5f)]
        [TestCase(Hand.Left, -0.5f, -0.3f)]
        public void PlantedFeetNeverSlideAndTheLegsKeepTheirLengths(Hand hand, float move, float side)
        {
            var footwork = new DummyFootwork(Height, hand, Start, new Random(7), isAlive: false);
            var rest = DummyLegs.EnGarde(Height, hand);
            Vector3? frontSpot = null;
            Vector3? backSpot = null;

            Run(footwork, Start + move, side, 5.0f, frame =>
            {
                frontSpot = CheckPinned(footwork.IsFrontFootPlanted, OnStrip(footwork, footwork.Legs.Front.Heel), frontSpot, $"Front foot, frame {frame}");
                backSpot = CheckPinned(footwork.IsBackFootPlanted, OnStrip(footwork, footwork.Legs.Back.Heel), backSpot, $"Back foot, frame {frame}");
                AssertLegLengths(footwork.Legs.Front, rest.Front, $"Front leg, frame {frame}");
                AssertLegLengths(footwork.Legs.Back, rest.Back, $"Back leg, frame {frame}");
            });

            Assert.That(footwork.SideMeters, Is.EqualTo(side).Within(Tolerance));
        }

        [TestCase(1.5f, Hand.Right)]
        [TestCase(2.1f, Hand.Right)]
        [TestCase(2.1f, Hand.Left)]
        public void NothingJumpsFurtherInAFrameThanTheBladeSweeps(float height, Hand hand)
        {
            var footwork = new DummyFootwork(height, hand, Start, new Random(11));
            var layout = DummyLayout.Create(height, hand);
            var before = layout.GetBodyOnStrip(footwork.DistanceMeters);
            var targets = new[] { (Start - 0.6f, 0.3f), (Start, -0.3f), (Start + 0.5f, 0.0f), (Start, 0.0f) };

            foreach (var (distance, side) in targets)
            {
                Run(footwork, distance, side, 4.0f, frame =>
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
        }

        [Test]
        public void PlacingTheDummyStandsItBackOnTheCenterLine()
        {
            var footwork = new DummyFootwork(Height, Hand.Right, Start, new Random(5), isAlive: false);
            Run(footwork, Start, 0.2f, 2.0f, null);

            footwork.PlaceAt(Start);

            Assert.That(footwork.SideMeters, Is.EqualTo(0.0f));
            AssertNear(footwork.Legs.Front.Ankle, DummyLegs.EnGarde(Height, Hand.Right).Front.Ankle, "En garde on the center line.");
        }

        [Test]
        public void ALungeFromASideStaysOnThatSide()
        {
            var footwork = new DummyFootwork(Height, Hand.Right, Start, new Random(6), isAlive: false);
            Run(footwork, Start, 0.2f, 2.0f, null);
            var lunge = ArmedDummyAttack.GetLungeMeters(Height);
            var hipsX = HipCenterOnStrip(footwork).X;

            for (var frame = 0; frame < 72; frame++)
            {
                footwork.StepLunge(lunge * DummyFootwork.MinimumJerk(frame / 72.0f), lunge, FrameSeconds);
                Assert.That(HipCenterOnStrip(footwork).X, Is.EqualTo(hipsX).Within(Tolerance), $"Frame {frame}");
            }

            Assert.That(footwork.SideMeters, Is.EqualTo(0.2f).Within(Tolerance));
        }

        [Test]
        public void TheKeeperFollowsTheFencersSideALittleOneReactionLate()
        {
            var keeper = CreateKeeper();
            var now = Feed(keeper, 0.0, 0.5, 0.0f, 0.0f);
            Assert.That(keeper.TargetSideMeters, Is.EqualTo(0.0f));

            now = Feed(keeper, now, 0.39, 0.0f, 0.3f);
            Assert.That(keeper.State, Is.EqualTo(DistanceKeeperState.Reacting));
            Assert.That(keeper.TargetSideMeters, Is.EqualTo(0.0f), "Nothing moves before the 400 ms reaction.");

            Feed(keeper, now, 1.0, 0.0f, 0.3f);
            Assert.That(keeper.State, Is.EqualTo(DistanceKeeperState.Holding));
            Assert.That(keeper.TargetSideMeters, Is.EqualTo(0.3f * DistanceKeeperTuning.SideFollowFraction).Within(1e-4f));
            Assert.That(keeper.TargetFrontFootZ, Is.EqualTo(Gap), "The distance is unchanged.");
        }

        [Test]
        public void TheKeeperStaysInsideTheStripsCorridor()
        {
            var keeper = CreateKeeper();

            Feed(keeper, 0.0, 2.0, 0.0f, -1.5f);

            Assert.That(keeper.TargetSideMeters, Is.EqualTo(-DistanceKeeperTuning.MaxSideMeters).Within(1e-4f));
        }

        [Test]
        public void SwayAndASmallDodgeAsideNeverMoveIt()
        {
            var keeper = CreateKeeper();
            var now = 0.0;
            for (var frame = 0; frame < 300; frame++)
            {
                now += FrameSeconds;
                var side = (float)Math.Sin(frame * 0.05) * (DistanceKeeperTuning.SideToleranceMeters / DistanceKeeperTuning.SideFollowFraction * 0.9f);
                keeper.Step(now, 0.0f, side);
            }

            Assert.That(keeper.TargetSideMeters, Is.EqualTo(0.0f));
            Assert.That(keeper.State, Is.EqualTo(DistanceKeeperState.Holding));
        }

        [Test]
        public void ADiagonalAdvanceByTheFencerIsAnsweredWithADiagonalRetreat()
        {
            var keeper = CreateKeeper();
            var footwork = new DummyFootwork(Height, Hand.Right, keeper.TargetFrontFootZ, new Random(1), isAlive: false);
            var now = 0.0;
            for (var frame = 0; frame < 72 * 4; frame++)
            {
                now += FrameSeconds;

                // The Fencer steps 25 cm forward and 30 cm to their right over 0.4 s, then stands.
                var fraction = (float)Math.Min(1.0, now / 0.4);
                keeper.Step(now, 0.25f * fraction, 0.3f * fraction);
                footwork.Step(keeper.TargetFrontFootZ, keeper.TargetSideMeters, FrameSeconds);
            }

            Assert.That(footwork.DistanceMeters, Is.EqualTo(Gap + 0.25f).Within(FootworkTuning.MinStepMeters));
            Assert.That(footwork.SideMeters, Is.EqualTo(0.15f).Within(FootworkTuning.MinStepMeters), "Half the Fencer's side.");
        }

        [Test]
        public void WithoutTheFencersSideTheKeeperKeepsItsSide()
        {
            var keeper = CreateKeeper();

            Feed(keeper, 0.0, 2.0, 0.3f, null);

            Assert.That(keeper.TargetSideMeters, Is.EqualTo(0.0f));
            Assert.That(keeper.TargetFrontFootZ, Is.EqualTo(Gap + 0.3f).Within(1e-4f));
        }

        [Test]
        public void AStandingDummySwaysALittleFromSideToSideOnPlantedFeet()
        {
            var footwork = new DummyFootwork(Height, Hand.Right, Start, new Random(4));
            var rest = DummyLegs.EnGarde(Height, Hand.Right);
            var hipsX = new List<float>();

            for (var frame = 0; frame < 72 * 30; frame++)
            {
                footwork.Hold(FrameSeconds);
                Assert.That(Vector3.Distance(footwork.Legs.Front.Heel, rest.Front.Heel), Is.LessThan(1e-3f), $"Frame {frame}: the front foot moved.");
                Assert.That(Vector3.Distance(footwork.Legs.Back.Toe, rest.Back.Toe), Is.LessThan(1e-3f), $"Frame {frame}: the back foot moved.");
                hipsX.Add(((footwork.Legs.Front.Hip + footwork.Legs.Back.Hip) * 0.5f).X - ((rest.Front.Hip + rest.Back.Hip) * 0.5f).X);
            }

            var sway = FootworkTuning.SideSwayPerHeight * Height;
            Assert.That(hipsX.Max() - hipsX.Min(), Is.GreaterThan(sway), "It sways from side to side.");
            Assert.That(hipsX.Select(Math.Abs).Max(), Is.LessThanOrEqualTo(sway + 1e-4f), "A little.");
            Assert.That(footwork.SideMeters, Is.EqualTo(0.0f), "Swaying does not move the Dummy.");
        }

        private static DistanceKeeper CreateKeeper()
        {
            return new DistanceKeeper(Gap, Gap, DummyStripLimits.Unlimited, DummyDifficulty.Medium, new MidRandom());
        }

        private static double Feed(DistanceKeeper keeper, double fromSeconds, double seconds, float frontFootZ, float? side)
        {
            var now = fromSeconds;
            var end = fromSeconds + seconds;
            while (now < end - 1e-9)
            {
                now += FrameSeconds;
                keeper.Step(now, frontFootZ, side);
            }

            return now;
        }

        private static void Run(DummyFootwork footwork, float distance, float side, float seconds, Action<int> onFrame)
        {
            var frames = (int)(seconds / FrameSeconds);
            for (var frame = 0; frame < frames; frame++)
            {
                footwork.Step(distance, side, FrameSeconds);
                onFrame?.Invoke(frame);
            }
        }

        private static Vector3 OnStrip(DummyFootwork footwork, Vector3 pointInDummyFrame)
        {
            return DummyLayout.DummyToStrip(pointInDummyFrame, footwork.DistanceMeters);
        }

        private static Vector3 HipCenterOnStrip(DummyFootwork footwork)
        {
            return OnStrip(footwork, (footwork.Legs.Front.Hip + footwork.Legs.Back.Hip) * 0.5f);
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

        private static void AssertLegLengths(LegPose leg, LegPose rest, string message)
        {
            var thigh = Vector3.Distance(rest.Hip, rest.Knee);
            var shin = Vector3.Distance(rest.Knee, rest.Ankle);
            Assert.That(Vector3.Distance(leg.Hip, leg.Ankle), Is.LessThan((thigh + shin) * 0.999f), message + ": the leg is overstretched.");
            Assert.That(Vector3.Distance(leg.Hip, leg.Knee), Is.EqualTo(thigh).Within(1e-3f), message);
            Assert.That(Vector3.Distance(leg.Knee, leg.Ankle), Is.EqualTo(shin).Within(1e-3f), message);
        }

        private static void AssertNear(Vector3 actual, Vector3 expected, string message)
        {
            Assert.That(Vector3.Distance(actual, expected), Is.LessThan(Tolerance), $"{message} {actual} vs {expected}");
        }

        /// <summary>A random source whose draws all sit mid-range, so every reaction comes exactly at the mean delay.</summary>
        private sealed class MidRandom : Random
        {
            public override double NextDouble()
            {
                return 0.5;
            }
        }
    }
}
