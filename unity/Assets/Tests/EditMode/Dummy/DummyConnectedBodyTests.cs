// The Dummy moves as one connected body (PER-115): the upper body rides on the hips and leans with them, so breathing,
// steps and lunges move the shoulders, head and both arms, every bone keeping its length; the Armed Dummy plans its
// thrust from where the leaning torso carries the shoulder, so its real point stays on the planned path the Parry
// judging uses; and posing the whole body every frame stays cheap.
using System;
using System.Diagnostics;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Tests.Dummy
{
    public class DummyConnectedBodyTests
    {
        private const float FrameSeconds = 1.0f / 72.0f;
        private const float Height = 1.8f;
        private const float BladeLengthMeters = 0.9f;

        // The breathing and weight shift move the shoulder a centimeter or two that the attack's plan does not know about.
        private const float LifeLayerMeters = 0.03f;

        [Test]
        public void ArmedDummysRealPointStaysOnItsPlannedPathThroughALunge(
            [Values(1.5f, 1.8f, 2.1f)] float height,
            [Values(AttackTarget.Chest, AttackTarget.Thigh)] AttackTarget target,
            [Values(Hand.Right, Hand.Left)] Hand hand)
        {
            var distance = DistancePresets.GetDistanceMeters(DummyDistance.Lunge, height);
            var layout = DummyLayout.Create(height, hand);
            var aim = AttackAim.GetAimInDummyFrame(target, layout, distance, layout.Arm.GetGuardPosition(), layout.Arm.GetBladeDirection());
            var attack = ArmedDummyAttack.Plan(
                0.0, 1.0f, height, hand, aim, AttackSpeed.Medium, BladeLengthMeters, ArmedDummyAttack.GetLungeMeters(height));
            var footwork = new DummyFootwork(height, hand, distance, new Random(7));
            var farthest = 0.0f;

            for (var now = attack.ThrustStartSeconds; now <= attack.ThrustEndSeconds + ArmedDummyTuning.HoldSeconds; now += FrameSeconds)
            {
                footwork.StepLunge(attack.GetLungeOffsetMeters(now), attack.LungeMeters, FrameSeconds);
                layout.PoseLegs(footwork.Legs);
                layout.PoseArm(attack.GetPose(now));

                // The path is in the Dummy frame where the attack started: the lunge has carried the frame forward since.
                var point = layout.Arm.GetBladePoint(BladeLengthMeters) + new Vector3(0.0f, 0.0f, distance - footwork.DistanceMeters);
                farthest = Math.Max(farthest, Vector3.Distance(point, attack.Path.GetPoint(now)));
            }

            TestContext.WriteLine($"The point strays up to {farthest * 100.0f:0.0} cm from the planned path.");
            Assert.That(farthest, Is.LessThan(LifeLayerMeters));
            Assert.That(Vector3.Distance(attack.Path.AimPoint, aim), Is.LessThan(0.03f), "The leaning lunge still reaches the aim.");
        }

        [Test]
        public void AStandingDummyBreathesWithItsWholeBodyAndNoBoneStretches()
        {
            var footwork = new DummyFootwork(Height, Hand.Right, 3.0f, new Random(2));
            var layout = DummyLayout.Create(Height, Hand.Right);
            var spine = GetSpineMeters(layout);
            var lowest = float.MaxValue;
            var highest = float.MinValue;

            for (var frame = 0; frame < 72 * 8; frame++)
            {
                footwork.Hold(FrameSeconds);
                layout.PoseLegs(footwork.Legs);
                var head = DummySkeleton.GetJointPosition(layout, SkeletonJoint.HeadCenter).Y;
                lowest = Math.Min(lowest, head);
                highest = Math.Max(highest, head);
                Assert.That(GetSpineMeters(layout), Is.EqualTo(spine).Within(1e-4f));
            }

            Assert.That(highest - lowest, Is.GreaterThan(0.004f), "The head rises and settles with the breathing.");
            Assert.That(highest - lowest, Is.LessThan(0.03f), "Without bobbing.");
        }

        [Test]
        public void HipsShiftedForwardStraightenTheTorsoButNeverArchIt()
        {
            var forward = DummyTorso.GetLeanRadians(new Vector3(0.0f, 0.0f, 0.3f * Height), Height) * 180.0f / MathF.PI;
            var back = DummyTorso.GetLeanRadians(new Vector3(0.0f, -0.5f * Height, -0.5f * Height), Height) * 180.0f / MathF.PI;

            Assert.That(forward, Is.EqualTo(-FootworkTuning.MaximumTorsoLeanBackDegrees).Within(1e-3f));
            Assert.That(back, Is.EqualTo(FootworkTuning.MaximumTorsoLeanDegrees).Within(1e-3f));
            Assert.That(DummyTorso.GetLeanRadians(Vector3.Zero, Height), Is.EqualTo(0.0f), "Upright in en garde.");
        }

        [TestCase(Hand.Right)]
        [TestCase(Hand.Left)]
        public void AnUprightTorsoIsTheEnGardeStance(Hand hand)
        {
            var torso = DummyTorso.EnGarde(Height, hand);
            var mirror = hand == Hand.Left ? -1.0f : 1.0f;

            AssertNear(torso.HeadCenter, Scale(EnGardePose.HeadCenter, mirror));
            AssertNear(torso.SwordShoulder, Scale(EnGardePose.SwordShoulder, mirror));
            AssertNear(torso.BackHand, Scale(EnGardePose.BackHand, mirror));
            AssertNear(torso.Body.Start, Scale(EnGardePose.TorsoBottom, mirror));
            AssertNear(torso.Body.End, Scale(EnGardePose.TorsoTop, mirror));
        }

        [Test]
        public void PosingTheWholeBodyEveryFrameCostsMicroseconds()
        {
            var footwork = new DummyFootwork(Height, Hand.Right, 3.0f, new Random(1));
            var layout = DummyLayout.Create(Height, Hand.Right);
            var lunge = ArmedDummyAttack.GetLungeMeters(Height);
            var pose = SwordArmReach.PoseEnGarde(Height, BladeLengthMeters);
            const int frames = 20000;
            var watch = Stopwatch.StartNew();
            for (var frame = 0; frame < frames; frame++)
            {
                var phase = (frame % 200) / 100.0f;
                footwork.StepLunge(lunge * (phase < 1.0f ? phase : 2.0f - phase), lunge, FrameSeconds);
                layout.PoseLegs(footwork.Legs);
                layout.PoseArm(pose);
            }

            watch.Stop();
            var microsecondsPerFrame = watch.Elapsed.TotalMilliseconds * 1000.0 / frames;
            TestContext.WriteLine($"Footwork, torso, legs and arm: {microsecondsPerFrame:0.00} us per frame.");
            Assert.That(microsecondsPerFrame, Is.LessThan(50.0));
        }

        private static float GetSpineMeters(DummyLayout layout)
        {
            return Vector3.Distance(
                DummySkeleton.GetJointPosition(layout, SkeletonJoint.HipCenter), DummySkeleton.GetJointPosition(layout, SkeletonJoint.ShoulderCenter));
        }

        private static Vector3 Scale(Vector3 pointAtUnitHeight, float mirror)
        {
            return new Vector3(pointAtUnitHeight.X * mirror * Height, pointAtUnitHeight.Y * Height, pointAtUnitHeight.Z * Height);
        }

        private static void AssertNear(Vector3 actual, Vector3 expected)
        {
            Assert.That(Vector3.Distance(actual, expected), Is.LessThan(1e-4f), $"{actual} vs {expected}");
        }
    }
}
