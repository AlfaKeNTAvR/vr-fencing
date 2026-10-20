// The Armed Dummy's lunge never moves a Target Zone further in one 72 Hz frame than the Blade sweeps (PER-114): a zone
// that jumps more than 5 cm a frame restarts the Blade's sweep and the Armed Dummy, so a Touch can be missed. A tall
// Dummy's lunge is long, so its recovery takes longer than the usual 0.6 s, just enough to stay under that.
using System;
using NUnit.Framework;
using VrFencing.Drills;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Tests.Dummy
{
    public class ArmedDummyRecoverySpeedTests
    {
        private const double FrameSeconds = 1.0 / 72.0;

        // The Blade restarts its sweep when the Target Zones jump more than 5 cm in a frame (BladeBehaviour.JumpMeters).
        private const float BladeJumpMeters = 0.05f;

        private static readonly float[] Heights = { 1.5f, 1.6f, 1.7f, 1.8f, 1.9f, 2.0f, 2.1f };

        [Test]
        public void NoTargetZoneMovesFurtherInAFrameThanTheBladeSweeps(
            [ValueSource(nameof(Heights))] float height,
            [Values] AttackSpeed speed,
            [Values(AttackTarget.Chest, AttackTarget.Thigh)] AttackTarget target,
            [Values(Hand.Right, Hand.Left)] Hand hand,
            [Values(false, true)] bool isParried)
        {
            var (attack, distance) = PlanDrill12Lunge(target, speed, hand, height);
            var parryAt = attack.ThrustStartSeconds + (attack.ThrustSeconds * 0.6);
            var footwork = new DummyFootwork(height, hand, distance, new Random(5));
            var layout = DummyLayout.Create(height, hand);
            layout.PoseArm(attack.GetPose(0.0));
            layout.PoseLegs(footwork.Legs);
            var before = layout.GetZonesOnStrip(footwork.DistanceMeters);
            var fastest = 0.0f;

            for (var now = FrameSeconds; now <= attack.DoneSeconds + 0.2; now += FrameSeconds)
            {
                if (isParried && !attack.IsParried && now >= parryAt)
                {
                    Assert.That(attack.ReportParried(now), Is.True);
                }

                var distanceBefore = footwork.DistanceMeters;
                footwork.StepLunge(attack.GetLungeOffsetMeters(now), attack.LungeMeters, (float)FrameSeconds);
                Assert.That(
                    Math.Abs(footwork.DistanceMeters - distanceBefore), Is.LessThan(BladeJumpMeters), $"The whole Dummy at t {now:0.000} s ({attack.GetPhase(now)})");
                layout.PoseArm(attack.GetPose(now));
                layout.PoseLegs(footwork.Legs);
                var zones = layout.GetZonesOnStrip(footwork.DistanceMeters);
                for (var index = 0; index < zones.Count; index++)
                {
                    var moved = Math.Max(
                        System.Numerics.Vector3.Distance(zones[index].Shape.Start, before[index].Shape.Start),
                        System.Numerics.Vector3.Distance(zones[index].Shape.End, before[index].Shape.End));
                    fastest = Math.Max(fastest, moved);
                    Assert.That(moved, Is.LessThan(BladeJumpMeters), $"{zones[index].Zone} at t {now:0.000} s ({attack.GetPhase(now)})");
                }

                before = zones;
            }

            TestContext.WriteLine($"Fastest zone: {fastest * 100.0f:0.00} cm a frame.");
            Assert.That(attack.GetLungeOffsetMeters(attack.DoneSeconds), Is.EqualTo(0.0f), "Back where it started.");
        }

        [Test]
        public void EveryDrill12LungeRecoversBeforeTheRepWindowEndsAtTheLongestDelay(
            [ValueSource(nameof(Heights))] float height, [Values(AttackTarget.Chest, AttackTarget.Thigh)] AttackTarget target)
        {
            foreach (var speed in DrillCatalog.LungeSpeeds)
            {
                var attack = PlanDrill12Lunge(target, speed, Hand.Right, height, ArmedDummyTuning.MaximumDelaySeconds).Attack;
                Assert.That(attack.DoneSeconds, Is.LessThan(DrillCatalog.ArmedDummyWindowSeconds), $"{speed}");
            }
        }

        [Test]
        public void AShortDummysRecoveryKeepsItsUsualDuration()
        {
            var (attack, _) = PlanDrill12Lunge(AttackTarget.Chest, AttackSpeed.Real, Hand.Right, 1.5f);

            Assert.That(attack.DoneSeconds - attack.ThrustEndSeconds, Is.EqualTo(ArmedDummyTuning.HoldSeconds + ArmedDummyTuning.RecoverSeconds).Within(1e-6));
        }

        [Test]
        public void ATallDummysRecoveryIsSlowedToItsSpeedLimit()
        {
            var (attack, _) = PlanDrill12Lunge(AttackTarget.Chest, AttackSpeed.Real, Hand.Right, 2.1f);
            var recoverStart = attack.ThrustEndSeconds + ArmedDummyTuning.HoldSeconds;
            var recoverSeconds = attack.DoneSeconds - recoverStart;

            Assert.That(recoverSeconds, Is.GreaterThan(ArmedDummyTuning.RecoverSeconds));
            Assert.That(
                AttackPath.MinimumJerkPeakFactor * attack.LungeMeters / recoverSeconds,
                Is.EqualTo(ArmedDummyTuning.MaximumRecoverLungeSpeedMetersPerSecond).Within(1e-3f));
        }

        [Test]
        public void ARecoveryFromAShortLungeKeepsItsUsualDuration()
        {
            var (attack, _) = PlanDrill12Lunge(AttackTarget.Chest, AttackSpeed.Real, Hand.Right, 2.1f);
            var parryAt = attack.ThrustStartSeconds + (attack.ThrustSeconds * 0.2);

            Assert.That(attack.ReportParried(parryAt), Is.True);
            Assert.That(attack.DoneSeconds - parryAt, Is.EqualTo(ArmedDummyTuning.RecoverSeconds).Within(1e-6));
        }

        /// <summary>Drill 12's thrust at <paramref name="target"/> from lunge distance, and that distance.</summary>
        private static (ArmedDummyAttack Attack, float Distance) PlanDrill12Lunge(
            AttackTarget target, AttackSpeed speed, Hand hand, float height, float delaySeconds = 1.0f)
        {
            var layout = DummyLayout.Create(height, hand);
            var distance = DistancePresets.GetDistanceMeters(DummyDistance.Lunge, height);
            var aim = AttackAim.GetAimInDummyFrame(target, layout, distance, layout.Arm.GetGuardPosition(), layout.Arm.GetBladeDirection());
            var attack = ArmedDummyAttack.Plan(
                0.0, delaySeconds, height, hand, aim, speed, DistancePresets.BladeLengthMeters, ArmedDummyAttack.GetLungeMeters(height));
            return (attack, distance);
        }
    }
}
