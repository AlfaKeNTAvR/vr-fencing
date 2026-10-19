// The Armed Dummy's scripted thrust (PER-40, prototype PER-95): the arm reaches its aim, the timing curve runs
// delay, wind-up, thrust, hold and recovery in order, and the point peaks at the speed level's speed.
using System;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Tests.Dummy
{
    public class ArmedDummyAttackTests
    {
        private const float Height = 1.75f;
        private const float BladeLength = DistancePresets.BladeLengthMeters;
        private const double Start = 10.0;
        private const float Delay = 2.0f;
        private const double SampleSeconds = 0.0005;

        private static Vector3 Shoulder => SwordArmReach.GetShoulderPosition(Height);

        private static float FullReach => SwordArmReach.GetFullReachMeters(Height, BladeLength);

        /// <summary>A chest-high aim a little inside full reach, down and in from the shoulder, as at extension distance.</summary>
        private static Vector3 ReachableAim => Shoulder + (Vector3.Normalize(new Vector3(-0.1f, -0.2f, 1.0f)) * (FullReach - 0.08f));

        private static Vector3 PointOf(SwordArmPose pose)
        {
            return new SwordArm(Height, Hand.Right, pose).GetBladePoint(BladeLength);
        }

        private static ArmedDummyAttack Plan(AttackSpeed speed = AttackSpeed.Medium)
        {
            return ArmedDummyAttack.Plan(Start, Delay, Height, ReachableAim, speed, BladeLength);
        }

        [Test]
        public void ReachPutsThePointOnAnAimWithinReach()
        {
            var pose = SwordArmReach.PoseToReach(Height, ReachableAim, BladeLength);

            Assert.That(Vector3.Distance(PointOf(pose), ReachableAim), Is.LessThan(0.002f));
        }

        [Test]
        public void ReachPointsAStraightArmAtAnAimOutOfReach()
        {
            var farAim = Shoulder + (new Vector3(0.0f, -0.3f, 1.0f) * 3.0f);

            var point = PointOf(SwordArmReach.PoseToReach(Height, farAim, BladeLength));

            Assert.That(Vector3.Distance(point, Shoulder), Is.EqualTo(FullReach).Within(0.002f), "The arm and blade are straight.");
            Assert.That(Vector3.Dot(Vector3.Normalize(point - Shoulder), Vector3.Normalize(farAim - Shoulder)), Is.GreaterThan(0.9999f));
        }

        [Test]
        public void WindUpDrawsThePointBackWithABentElbow()
        {
            var guardPoint = PointOf(SwordArmReach.PoseEnGarde(Height, BladeLength));
            var windUp = SwordArmReach.PoseWindUp(Height, BladeLength);

            var drawnBack = Vector3.Distance(guardPoint, Shoulder) - Vector3.Distance(PointOf(windUp), Shoulder);

            Assert.That(drawnBack, Is.EqualTo(SwordArmReach.WindUpDrawBackMeters).Within(0.002f));
            Assert.That(2.0 * Math.Acos(Math.Min(1.0f, Math.Abs(windUp.Elbow.W))), Is.GreaterThan(0.3), "The elbow visibly bends.");
        }

        [Test]
        public void PhasesRunDelayWindUpThrustHoldRecoverInOrder()
        {
            var attack = Plan();

            Assert.That(attack.GetPhase(Start), Is.EqualTo(AttackPhase.Waiting));
            Assert.That(attack.GetPhase(Start + Delay - 0.01), Is.EqualTo(AttackPhase.Waiting));
            Assert.That(attack.GetPhase(attack.WindUpStartSeconds + 0.01), Is.EqualTo(AttackPhase.WindUp));
            Assert.That(attack.GetPhase(attack.ThrustStartSeconds + 0.01), Is.EqualTo(AttackPhase.Thrust));
            Assert.That(attack.GetPhase(attack.ThrustEndSeconds + 0.01), Is.EqualTo(AttackPhase.Hold));
            Assert.That(attack.GetPhase(attack.ThrustEndSeconds + ArmedDummyAttack.HoldSeconds + 0.01), Is.EqualTo(AttackPhase.Recover));
            Assert.That(attack.GetPhase(attack.DoneSeconds), Is.EqualTo(AttackPhase.Done));
            Assert.That(attack.ThrustStartSeconds - attack.WindUpStartSeconds, Is.EqualTo(ArmedDummyAttack.WindUpSeconds).Within(1e-6));
            Assert.That(attack.DoneSeconds - attack.ThrustEndSeconds,
                Is.EqualTo(ArmedDummyAttack.HoldSeconds + ArmedDummyAttack.RecoverSeconds).Within(1e-6));
        }

        [Test]
        public void ArmIsEnGardeBeforeAndAfterAndThePointLandsOnTheAim()
        {
            var attack = Plan();
            var inLinePoint = PointOf(SwordArmReach.PoseEnGarde(Height, BladeLength));

            Assert.That(Vector3.Distance(PointOf(attack.GetPose(Start)), inLinePoint), Is.LessThan(1e-4f));
            Assert.That(Vector3.Distance(PointOf(attack.GetPose(attack.ThrustEndSeconds)), ReachableAim), Is.LessThan(0.002f));
            Assert.That(Vector3.Distance(PointOf(attack.GetPose(attack.ThrustEndSeconds + 0.1)), ReachableAim), Is.LessThan(0.002f), "Held at the aim.");
            Assert.That(Vector3.Distance(PointOf(attack.GetPose(attack.DoneSeconds + 1.0)), inLinePoint), Is.LessThan(1e-4f));
        }

        [TestCase(AttackSpeed.Slow, 0.5f)]
        [TestCase(AttackSpeed.Medium, 1.5f)]
        [TestCase(AttackSpeed.Real, 2.75f)]
        public void ThePointPeaksAtTheSpeedLevelsSpeed(AttackSpeed speed, float expectedMetersPerSecond)
        {
            var attack = Plan(speed);

            var peak = FindPeakPointSpeed(attack, attack.ThrustStartSeconds, attack.ThrustEndSeconds);

            Assert.That(ArmedDummyAttack.GetPeakPointSpeed(speed), Is.EqualTo(expectedMetersPerSecond));
            Assert.That(peak, Is.EqualTo(expectedMetersPerSecond).Within(expectedMetersPerSecond * 0.1f));
        }

        [Test]
        public void ThePointNeverJumpsThroughTheWholeAttack()
        {
            var attack = Plan(AttackSpeed.Real);

            var peak = FindPeakPointSpeed(attack, Start, attack.DoneSeconds + 0.1);

            Assert.That(peak, Is.LessThan(ArmedDummyAttack.GetPeakPointSpeed(AttackSpeed.Real) * 1.2f));
        }

        [Test]
        public void ParryMidThrustRecoversFromWhereTheArmIs()
        {
            var attack = Plan(AttackSpeed.Slow);
            var parryAt = (attack.ThrustStartSeconds + attack.ThrustEndSeconds) * 0.5;
            var pointBefore = PointOf(attack.GetPose(parryAt));

            Assert.That(attack.ReportParried(parryAt), Is.True);

            Assert.That(attack.IsParried, Is.True);
            Assert.That(attack.GetPhase(parryAt + 0.01), Is.EqualTo(AttackPhase.Recover));
            Assert.That(Vector3.Distance(PointOf(attack.GetPose(parryAt)), pointBefore), Is.LessThan(1e-4f), "No jump at the parry.");
            Assert.That(attack.DoneSeconds, Is.EqualTo(parryAt + ArmedDummyAttack.RecoverSeconds).Within(1e-6));
            Assert.That(attack.ReportParried(parryAt + 0.05), Is.False, "Parried once.");
        }

        [Test]
        public void ContactBeforeTheThrustDoesNotParry()
        {
            var attack = Plan();

            Assert.That(attack.ReportParried(Start + 0.5), Is.False);
            Assert.That(attack.ReportParried(attack.WindUpStartSeconds + 0.1), Is.False);
            Assert.That(attack.IsParried, Is.False);
            Assert.That(attack.GetPhase(attack.ThrustStartSeconds + 0.01), Is.EqualTo(AttackPhase.Thrust));
        }

        [Test]
        public void DelayIsDrawnFromOneToThreeSeconds()
        {
            var random = new Random(7);
            var shortest = float.MaxValue;
            var longest = float.MinValue;
            for (var draw = 0; draw < 1000; draw++)
            {
                var delay = ArmedDummyAttack.DrawDelaySeconds(random);
                shortest = Math.Min(shortest, delay);
                longest = Math.Max(longest, delay);
            }

            Assert.That(shortest, Is.GreaterThanOrEqualTo(ArmedDummyAttack.MinimumDelaySeconds).And.LessThan(1.1f));
            Assert.That(longest, Is.LessThanOrEqualTo(ArmedDummyAttack.MaximumDelaySeconds).And.GreaterThan(2.9f));
        }

        [Test]
        public void AnAttackNeedsAThrustThatTakesTime()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ArmedDummyAttack(
                Start, Delay, SwordArmPose.ArmInLine, SwordArmPose.ArmInLine, SwordArmPose.ArmInLine, 0.0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ArmedDummyAttack(
                Start, -1.0f, SwordArmPose.ArmInLine, SwordArmPose.ArmInLine, SwordArmPose.ArmInLine, 0.5f));
        }

        private static float FindPeakPointSpeed(ArmedDummyAttack attack, double from, double to)
        {
            var peak = 0.0f;
            var previous = PointOf(attack.GetPose(from));
            for (var time = from + SampleSeconds; time <= to; time += SampleSeconds)
            {
                var point = PointOf(attack.GetPose(time));
                peak = Math.Max(peak, Vector3.Distance(previous, point) / (float)SampleSeconds);
                previous = point;
            }

            return peak;
        }
    }
}
