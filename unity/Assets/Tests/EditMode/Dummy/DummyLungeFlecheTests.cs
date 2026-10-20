// The Dummy's lunge, recovery and fleche (PER-111): a lunge follows the offset someone else times (the Armed Dummy's,
// so Drill 12 keeps its timing and judging) with the front foot leading, the back foot planted (dragging only what the
// leg cannot reach) and the hips dropping, then recovers to where it started; a fleche crosses the back foot over in
// front, runs the front foot past it and ends in en garde a fleche length nearer the Fencer. In every frame the legs
// reach, the feet stay on the floor and out of each other, planted feet stay put, and nothing jumps further than the
// Blade's sweep.
using System;
using System.Diagnostics;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Tests.Dummy
{
    public class DummyLungeFlecheTests
    {
        private const float FrameSeconds = 1.0f / 72.0f;
        private const float Height = 1.8f;
        private const float Start = 3.0f;
        private const float Tolerance = 1e-4f;

        // The Blade restarts its sweep when the Target Zones jump more than 5 cm in a frame (BladeBehaviour.JumpMeters).
        private const float BladeJumpMeters = 0.05f;

        // ---- Lunge ----

        [Test]
        public void ALungeFollowsTheOffsetAndRecoversToEnGardeWhereItStarted()
        {
            var footwork = CreateStill();
            var lunge = ArmedDummyAttack.GetLungeMeters(Height);
            var seen = FootworkStep.Standing;

            RunLunge(footwork, lunge, (offset, frame) =>
            {
                Assert.That(footwork.DistanceMeters, Is.EqualTo(Start - offset).Within(Tolerance), $"Frame {frame}");
                if (footwork.CurrentStep != seen)
                {
                    Assert.That(footwork.CurrentStep, Is.EqualTo(Next(seen)), $"Frame {frame}");
                    seen = footwork.CurrentStep;
                }
            });

            Assert.That(seen, Is.EqualTo(FootworkStep.Standing));
            Assert.That(footwork.DistanceMeters, Is.EqualTo(Start));
            AssertLegsNear(footwork.Legs, DummyLegs.EnGarde(Height, Hand.Right));
        }

        [Test]
        public void TheFrontFootLeadsTheBodyAndLandsAsItArrives()
        {
            var footwork = CreateStill();
            var lunge = ArmedDummyAttack.GetLungeMeters(Height);
            var rest = DummyLegs.EnGarde(Height, Hand.Right);
            var furthestLead = 0.0f;

            for (var frame = 1; frame <= 72; frame++)
            {
                footwork.StepLunge(lunge * MinimumJerk(frame / 72.0f), lunge, FrameSeconds);
                var lead = footwork.Legs.Front.Ankle.Z - rest.Front.Ankle.Z;
                Assert.That(lead, Is.GreaterThanOrEqualTo(-Tolerance), $"Frame {frame}: the front foot fell behind the body.");
                furthestLead = Math.Max(furthestLead, lead);
            }

            Assert.That(furthestLead, Is.GreaterThan(0.05f), "The front foot kicks out ahead.");
            Assert.That(footwork.IsFrontFootPlanted, Is.True, "Landed at full length.");
            Assert.That(footwork.Legs.Front.Ankle.Z, Is.EqualTo(rest.Front.Ankle.Z).Within(1e-3f));
            Assert.That(footwork.Legs.Front.Heel.Y, Is.EqualTo(rest.Front.Heel.Y).Within(1e-3f));
        }

        [Test]
        public void AShortLungeKeepsTheBackFootPlantedThroughout()
        {
            var footwork = CreateStill();
            var lunge = 0.3f * Height;
            Vector3? backSpot = null;

            RunLunge(footwork, lunge, (offset, frame) =>
            {
                Assert.That(footwork.IsBackFootPlanted, Is.True, $"Frame {frame}");
                backSpot = CheckPinned(true, OnStrip(footwork, footwork.Legs.Back.Heel), backSpot, $"Back foot, frame {frame}");
            });
        }

        [TestCase(1.5f)]
        [TestCase(1.8f)]
        [TestCase(2.1f)]
        public void AFullLungeDragsTheBackFootOnlyWhatTheLegCannotReach(float height)
        {
            var footwork = new DummyFootwork(height, Hand.Right, Start, new Random(1), isAlive: false);
            var lunge = ArmedDummyAttack.GetLungeMeters(height);
            var rest = DummyLegs.EnGarde(height, Hand.Right);
            var backLength = Vector3.Distance(rest.Back.Hip, rest.Back.Knee) + Vector3.Distance(rest.Back.Knee, rest.Back.Ankle);
            var startHeel = DummyLayout.DummyToStrip(rest.Back.Heel, Start);
            var drag = 0.0f;
            var plantedUntil = 0.0f;

            RunLunge(footwork, lunge, (offset, frame) =>
            {
                if (footwork.CurrentStep != FootworkStep.Lunging)
                {
                    return;
                }

                var heel = OnStrip(footwork, footwork.Legs.Back.Heel);
                drag = Math.Max(drag, startHeel.Z - heel.Z);
                if (drag < 1e-4f)
                {
                    plantedUntil = offset;
                }

                var reach = Vector3.Distance(footwork.Legs.Back.Hip, footwork.Legs.Back.Ankle);
                Assert.That(reach, Is.LessThanOrEqualTo((backLength * FootworkTuning.LungeBackLegReachFraction) + 1e-3f), $"Frame {frame}");
            });

            TestContext.WriteLine($"{height} m: back foot planted until {plantedUntil:0.00} m of a {lunge:0.00} m lunge, then drags {drag * 100.0f:0} cm.");
            Assert.That(plantedUntil, Is.GreaterThan(0.7f * lunge), "Planted for most of the lunge.");
            Assert.That(drag, Is.LessThan(0.08f * height), "The drag stays short.");
        }

        [Test]
        public void TheHipsDropAndStayBackInALungeAndComeBackAfter()
        {
            var footwork = CreateStill();
            var lunge = ArmedDummyAttack.GetLungeMeters(Height);
            var rest = DummyLegs.EnGarde(Height, Hand.Right);

            for (var frame = 1; frame <= 72; frame++)
            {
                footwork.StepLunge(lunge * frame / 72.0f, lunge, FrameSeconds);
            }

            var drop = rest.Front.Hip.Y - footwork.Legs.Front.Hip.Y;
            var back = rest.Front.Hip.Z - footwork.Legs.Front.Hip.Z;
            Assert.That(drop, Is.EqualTo(FootworkTuning.LungeHipDropPerHeight * Height).Within(1e-3f));
            Assert.That(back, Is.EqualTo(FootworkTuning.LungeHipBackPerHeight * Height).Within(1e-3f));
        }

        // The arm zones are left out: they move with the Armed Dummy's arm and body as before PER-111. The legs' parts may
        // move no more than 5 cm a frame (PER-114 slowed a tall Dummy's recovery to keep the body under that).
        [TestCase(AttackTarget.Chest, AttackSpeed.Real, Hand.Right, 2.1f)]
        [TestCase(AttackTarget.Thigh, AttackSpeed.Real, Hand.Left, 2.1f)]
        [TestCase(AttackTarget.Chest, AttackSpeed.Medium, Hand.Right, 1.5f)]
        [TestCase(AttackTarget.Thigh, AttackSpeed.Real, Hand.Right, 1.5f)]
        public void ADrill12LungeKeepsItsTimingAndNothingJumpsFurtherThanTheBladeSweeps(AttackTarget target, AttackSpeed speed, Hand hand, float height)
        {
            var (attack, distance) = PlanDrill12Lunge(target, speed, hand, height);
            var footwork = new DummyFootwork(height, hand, distance, new Random(3));
            var layout = DummyLayout.Create(height, hand);
            var rest = DummyLegs.EnGarde(height, hand);
            layout.PoseArm(attack.GetPose(0.0));
            layout.PoseLegs(footwork.Legs);
            var before = layout.GetBodyOnStrip(footwork.DistanceMeters);
            var floor = rest.Front.Heel.Y;
            var fastestBody = 0.0f;

            for (var now = 0.0; now <= attack.DoneSeconds + 0.5; now += FrameSeconds)
            {
                var distanceBefore = footwork.DistanceMeters;
                footwork.StepLunge(attack.GetLungeOffsetMeters(now), attack.LungeMeters, FrameSeconds);
                var bodyMove = Math.Abs(footwork.DistanceMeters - distanceBefore);
                fastestBody = Math.Max(fastestBody, bodyMove);
                Assert.That(footwork.DistanceMeters, Is.EqualTo(distance - attack.GetLungeOffsetMeters(now)).Within(Tolerance), "The attack's timing.");
                layout.PoseArm(attack.GetPose(now));
                layout.PoseLegs(footwork.Legs);
                AssertNoJump(
                    before, layout.GetBodyOnStrip(footwork.DistanceMeters), $"t {now:0.000}", (int)TargetZone.Thigh);
                before = layout.GetBodyOnStrip(footwork.DistanceMeters);
                AssertLegsReach(footwork.Legs, rest, $"t {now:0.000}");
                AssertOnOrAboveFloor(footwork.Legs, floor, $"t {now:0.000}");
            }

            TestContext.WriteLine($"The Armed Dummy moves the whole body up to {fastestBody * 100.0f:0.00} cm a frame.");
            Assert.That(footwork.CurrentStep, Is.EqualTo(FootworkStep.Standing));
            Assert.That(footwork.DistanceMeters, Is.EqualTo(distance).Within(Tolerance));
        }

        [Test]
        public void AParriedLungeRecoversFromWhereTheFeetWereWithoutAJump()
        {
            var (attack, distance) = PlanDrill12Lunge(AttackTarget.Chest, AttackSpeed.Real, Hand.Right, Height);
            var footwork = new DummyFootwork(Height, Hand.Right, distance, new Random(2), isAlive: false);
            var layout = DummyLayout.Create(Height, Hand.Right);
            var parryAt = attack.ThrustStartSeconds + (attack.ThrustSeconds * 0.4);
            var isParried = false;
            var before = layout.GetBodyOnStrip(footwork.DistanceMeters);

            for (var now = 0.0; now <= attack.DoneSeconds + 0.5; now += FrameSeconds)
            {
                if (!isParried && now >= parryAt)
                {
                    isParried = attack.ReportParried(now);
                }

                footwork.StepLunge(attack.GetLungeOffsetMeters(now), attack.LungeMeters, FrameSeconds);
                layout.PoseLegs(footwork.Legs);
                AssertNoJump(before, layout.GetBodyOnStrip(footwork.DistanceMeters), $"t {now:0.000}");
                before = layout.GetBodyOnStrip(footwork.DistanceMeters);
            }

            Assert.That(isParried, Is.True);
            Assert.That(footwork.CurrentStep, Is.EqualTo(FootworkStep.Standing));
            AssertLegsNear(footwork.Legs, DummyLegs.EnGarde(Height, Hand.Right));
        }

        [Test]
        public void AStepUnderWayIsFinishedBeforeTheLungeStarts()
        {
            var footwork = CreateStill();
            footwork.Step(Start - 0.2f, FrameSeconds);
            Assert.That(footwork.CurrentStep, Is.EqualTo(FootworkStep.Advancing));

            while (footwork.CurrentStep == FootworkStep.Advancing)
            {
                footwork.StepLunge(0.5f, 1.0f, FrameSeconds);
            }

            Assert.That(footwork.DistanceMeters, Is.EqualTo(Start - 0.2f).Within(Tolerance));
            footwork.StepLunge(0.5f, 1.0f, FrameSeconds);
            Assert.That(footwork.CurrentStep, Is.EqualTo(FootworkStep.Lunging));
            Assert.That(footwork.DistanceMeters, Is.EqualTo(Start - 0.2f - 0.5f).Within(Tolerance));
        }

        [Test]
        public void ALeftHandedLungeMirrorsARightHandedOne()
        {
            var right = new DummyFootwork(Height, Hand.Right, Start, new Random(5));
            var left = new DummyFootwork(Height, Hand.Left, Start, new Random(5));

            for (var frame = 1; frame <= 40; frame++)
            {
                right.StepLunge(0.02f * frame, 1.0f, FrameSeconds);
                left.StepLunge(0.02f * frame, 1.0f, FrameSeconds);
            }

            AssertNear(left.Legs.Front.Knee, HandMirroring.MirrorPosition(right.Legs.Front.Knee));
            AssertNear(left.Legs.Back.Toe, HandMirroring.MirrorPosition(right.Legs.Back.Toe));
        }

        [Test]
        public void ABadLungeLengthIsRejected()
        {
            var footwork = CreateStill();

            Assert.Throws<ArgumentOutOfRangeException>(() => footwork.StepLunge(0.1f, 0.0f, FrameSeconds));
            Assert.Throws<ArgumentOutOfRangeException>(() => footwork.StepLunge(float.NaN, 1.0f, FrameSeconds));
        }

        // ---- Fleche ----

        [TestCase(1.5f)]
        [TestCase(1.8f)]
        [TestCase(2.1f)]
        public void AFlecheEndsInEnGardeAFlecheLengthNearer(float height)
        {
            var footwork = new DummyFootwork(height, Hand.Right, Start, new Random(1), isAlive: false);

            Assert.That(footwork.StartFleche(), Is.True);
            RunFleche(footwork, null);

            Assert.That(footwork.CurrentStep, Is.EqualTo(FootworkStep.Standing));
            Assert.That(footwork.DistanceMeters, Is.EqualTo(Start - footwork.FlecheLengthMeters).Within(Tolerance));
            Assert.That(footwork.FlecheLengthMeters, Is.GreaterThan(0.3f * height));
            AssertLegsNear(footwork.Legs, DummyLegs.EnGarde(height, Hand.Right));
        }

        [Test]
        public void TheBackFootCrossesOverInFrontFirstThenTheFrontFootRunsPastIt()
        {
            var footwork = CreateStill();
            footwork.StartFleche();
            var backLiftedFirst = false;
            var backLandedAhead = false;
            var frontPassed = false;
            var wasBackPlanted = true;

            RunFleche(footwork, frame =>
            {
                if (!footwork.IsBackFootPlanted && footwork.IsFrontFootPlanted && !backLandedAhead)
                {
                    backLiftedFirst = true;
                }

                if (!wasBackPlanted && footwork.IsBackFootPlanted)
                {
                    backLandedAhead = footwork.Legs.Back.Ankle.Z > footwork.Legs.Front.Ankle.Z;
                }

                if (backLandedAhead && footwork.Legs.Front.Ankle.Z > footwork.Legs.Back.Ankle.Z)
                {
                    frontPassed = true;
                }

                wasBackPlanted = footwork.IsBackFootPlanted;
            });

            Assert.That(backLiftedFirst, Is.True, "The back foot goes first.");
            Assert.That(backLandedAhead, Is.True, "It lands in front of the front foot.");
            Assert.That(frontPassed, Is.True, "The front foot runs past it.");
        }

        [TestCase(1.5f, Hand.Right)]
        [TestCase(2.1f, Hand.Right)]
        [TestCase(2.1f, Hand.Left)]
        public void InAFlecheTheLegsReachTheFeetStayApartAndPlantedFeetStayPut(float height, Hand hand)
        {
            var footwork = new DummyFootwork(height, hand, Start, new Random(9));
            var layout = DummyLayout.Create(height, hand);
            var rest = DummyLegs.EnGarde(height, hand);
            var footRadius = EnGardePose.FootRadius * height;
            var before = layout.GetBodyOnStrip(footwork.DistanceMeters);
            Vector3? frontSpot = null;
            Vector3? backSpot = null;
            var closest = float.MaxValue;
            footwork.StartFleche();

            RunFleche(footwork, frame =>
            {
                var message = $"Frame {frame}";
                layout.PoseLegs(footwork.Legs);
                AssertNoJump(before, layout.GetBodyOnStrip(footwork.DistanceMeters), message);
                before = layout.GetBodyOnStrip(footwork.DistanceMeters);
                AssertLegsReach(footwork.Legs, rest, message);
                AssertOnOrAboveFloor(footwork.Legs, rest.Front.Heel.Y, message);
                frontSpot = CheckPinned(footwork.IsFrontFootPlanted, OnStrip(footwork, footwork.Legs.Front.Heel), frontSpot, "Front foot, " + message);
                backSpot = CheckPinned(footwork.IsBackFootPlanted, OnStrip(footwork, footwork.Legs.Back.Heel), backSpot, "Back foot, " + message);
                closest = Math.Min(closest, SegmentDistance(footwork.Legs.Front.Heel, footwork.Legs.Front.Toe, footwork.Legs.Back.Heel, footwork.Legs.Back.Toe));
            });

            TestContext.WriteLine($"Closest the feet come, surface to surface: {(closest - (2.0f * footRadius)) * 100.0f:0.0} cm.");
            Assert.That(closest, Is.GreaterThan(2.0f * footRadius), "A foot went through the other.");
        }

        [Test]
        public void AFlecheStartsOnlyFromEnGardeAndIgnoresTargetsUntilItEnds()
        {
            var footwork = CreateStill();
            footwork.Step(Start - 0.2f, FrameSeconds);

            Assert.That(footwork.StartFleche(), Is.False, "Not mid-step.");

            Run(footwork, Start - 0.2f, 1.0f);
            Assert.That(footwork.StartFleche(), Is.True);
            Assert.That(footwork.StartFleche(), Is.False, "Not twice.");
            Run(footwork, Start + 5.0f, FootworkTuning.FlecheSeconds - FrameSeconds);
            Assert.That(footwork.DistanceMeters, Is.LessThan(Start - 0.2f), "Still running at the Fencer, not retreating.");
        }

        // ---- Cost ----

        [Test]
        public void ALungeOrFlecheFrameCostsMicroseconds()
        {
            var footwork = new DummyFootwork(Height, Hand.Right, Start, new Random(1));
            var lunge = ArmedDummyAttack.GetLungeMeters(Height);
            const int frames = 20000;
            var watch = Stopwatch.StartNew();
            for (var frame = 0; frame < frames; frame++)
            {
                if (frame % 400 < 200)
                {
                    var phase = (frame % 200) / 100.0f;
                    footwork.StepLunge(lunge * (phase < 1.0f ? phase : 2.0f - phase), lunge, FrameSeconds);
                }
                else
                {
                    footwork.StartFleche();
                    footwork.Step(Start, FrameSeconds);
                }
            }

            watch.Stop();
            var microsecondsPerFrame = watch.Elapsed.TotalMilliseconds * 1000.0 / frames;
            TestContext.WriteLine($"DummyFootwork lunge and fleche: {microsecondsPerFrame:0.00} us per frame.");
            Assert.That(microsecondsPerFrame, Is.LessThan(50.0));
        }

        // ---- Helpers ----

        private static DummyFootwork CreateStill()
        {
            return new DummyFootwork(Height, Hand.Right, Start, new Random(1), isAlive: false);
        }

        /// <summary>The minimum-jerk curve from 0 to 1, as the footwork and the Armed Dummy use.</summary>
        private static float MinimumJerk(float fraction)
        {
            var t = Math.Min(Math.Max(fraction, 0.0f), 1.0f);
            return t * t * t * ((t * ((t * 6.0f) - 15.0f)) + 10.0f);
        }

        private static FootworkStep Next(FootworkStep step)
        {
            return step == FootworkStep.Standing ? FootworkStep.Lunging : step == FootworkStep.Lunging ? FootworkStep.Recovering : FootworkStep.Standing;
        }

        /// <summary>A lunge out over 1 s on a minimum-jerk curve, held 0.3 s, back over 0.6 s, then 0.2 s standing.</summary>
        private static void RunLunge(DummyFootwork footwork, float lunge, Action<float, int> onFrame)
        {
            var frame = 0;
            for (var seconds = FrameSeconds; seconds <= 2.1f; seconds += FrameSeconds)
            {
                var offset = seconds < 1.0f
                    ? lunge * MinimumJerk(seconds)
                    : seconds < 1.3f ? lunge : lunge * (1.0f - MinimumJerk((seconds - 1.3f) / 0.6f));
                footwork.StepLunge(offset, lunge, FrameSeconds);
                onFrame(offset, frame++);
            }
        }

        private static void RunFleche(DummyFootwork footwork, Action<int> onFrame)
        {
            var frames = (int)Math.Ceiling(FootworkTuning.FlecheSeconds / FrameSeconds) + 2;
            for (var frame = 0; frame < frames; frame++)
            {
                footwork.Step(footwork.DistanceMeters, FrameSeconds);
                onFrame?.Invoke(frame);
            }
        }

        private static void Run(DummyFootwork footwork, float target, float seconds)
        {
            for (var elapsed = 0.0f; elapsed < seconds; elapsed += FrameSeconds)
            {
                footwork.Step(target, FrameSeconds);
            }
        }

        /// <summary>Drill 12's thrust at <paramref name="target"/> from lunge distance, and that distance.</summary>
        private static (ArmedDummyAttack Attack, float Distance) PlanDrill12Lunge(AttackTarget target, AttackSpeed speed, Hand hand, float height)
        {
            var layout = DummyLayout.Create(height, hand);
            var distance = DistancePresets.GetDistanceMeters(DummyDistance.Lunge, height);
            var aim = AttackAim.GetAimInDummyFrame(target, layout, distance, layout.Arm.GetGuardPosition(), layout.Arm.GetBladeDirection());
            var attack = ArmedDummyAttack.Plan(
                0.0, 1.0f, height, hand, aim, speed, DistancePresets.BladeLengthMeters, ArmedDummyAttack.GetLungeMeters(height));
            return (attack, distance);
        }

        private static Vector3 OnStrip(DummyFootwork footwork, Vector3 pointInDummyFrame)
        {
            return DummyLayout.DummyToStrip(pointInDummyFrame, footwork.DistanceMeters);
        }

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

        /// <summary>
        /// No part from <paramref name="firstPart"/> on (zones in enum order, then the silhouette) moved
        /// <paramref name="limitMeters"/> (the Blade's 5 cm sweep unless given) or more.
        /// </summary>
        private static void AssertNoJump(
            System.Collections.Generic.IReadOnlyList<Capsule> before,
            System.Collections.Generic.IReadOnlyList<Capsule> now,
            string message,
            int firstPart = 0,
            float limitMeters = BladeJumpMeters)
        {
            for (var part = firstPart; part < now.Count; part++)
            {
                Assert.That(Vector3.Distance(now[part].Start, before[part].Start), Is.LessThan(limitMeters), $"Part {part}, {message}");
                Assert.That(Vector3.Distance(now[part].End, before[part].End), Is.LessThan(limitMeters), $"Part {part}, {message}");
            }
        }

        private static void AssertLegsReach(DummyLegs legs, DummyLegs rest, string message)
        {
            foreach (var (leg, restLeg) in new[] { (legs.Front, rest.Front), (legs.Back, rest.Back) })
            {
                var thigh = Vector3.Distance(restLeg.Hip, restLeg.Knee);
                var shin = Vector3.Distance(restLeg.Knee, restLeg.Ankle);
                Assert.That(Vector3.Distance(leg.Hip, leg.Ankle), Is.LessThan((thigh + shin) * 0.999f), message + ": overstretched.");
                Assert.That(Vector3.Distance(leg.Hip, leg.Knee), Is.EqualTo(thigh).Within(1e-3f), message);
                Assert.That(Vector3.Distance(leg.Knee, leg.Ankle), Is.EqualTo(shin).Within(1e-3f), message);
            }
        }

        private static void AssertOnOrAboveFloor(DummyLegs legs, float floor, string message)
        {
            foreach (var point in new[] { legs.Front.Heel, legs.Front.Toe, legs.Back.Heel, legs.Back.Toe })
            {
                Assert.That(point.Y, Is.GreaterThanOrEqualTo(floor - Tolerance), message);
            }
        }

        /// <summary>The closest two segments come, sampled finely enough for feet.</summary>
        private static float SegmentDistance(Vector3 startA, Vector3 endA, Vector3 startB, Vector3 endB)
        {
            const int samples = 24;
            var closest = float.MaxValue;
            for (var i = 0; i <= samples; i++)
            {
                var pointA = Vector3.Lerp(startA, endA, i / (float)samples);
                for (var j = 0; j <= samples; j++)
                {
                    closest = Math.Min(closest, Vector3.Distance(pointA, Vector3.Lerp(startB, endB, j / (float)samples)));
                }
            }

            return closest;
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
