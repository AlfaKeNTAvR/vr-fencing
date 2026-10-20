// The Armed Dummy's attack script (PER-96, from the PER-95 prototype): a thrust on each target line reaches its aim
// point at the set speed along a straight planned path, the delay stays within 1 to 3 s, a wind-up comes before every
// thrust, the arm stays within its joint limits throughout, the feet stay planted, and a Parry cuts the thrust short.
using System;
using System.Collections.Generic;
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

        private static readonly AttackTarget[] Targets = { AttackTarget.Hand, AttackTarget.Forearm, AttackTarget.Chest, AttackTarget.Thigh };
        private static readonly AttackSpeed[] Speeds = { AttackSpeed.Slow, AttackSpeed.Medium, AttackSpeed.Real };
        private static readonly Hand[] Hands = { Hand.Right, Hand.Left };
        private static readonly float[] Heights = { 1.5f, 1.75f, 2.0f };

        private static Vector3 Shoulder => SwordArmReach.GetShoulderPosition(Height);

        private static float FullReach => SwordArmReach.GetFullReachMeters(Height, BladeLength);

        /// <summary>A chest-high aim a little inside full reach, down and in from the shoulder, as at extension distance.</summary>
        private static Vector3 ReachableAim => Shoulder + (Vector3.Normalize(new Vector3(-0.1f, -0.2f, 1.0f)) * (FullReach - 0.08f));

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

            Assert.That(drawnBack, Is.EqualTo(ArmedDummyTuning.WindUpDrawBackMeters).Within(0.002f));
            Assert.That(SwordArmReach.GetElbowBendDegrees(windUp), Is.GreaterThan(20.0f), "The elbow visibly bends.");
        }

        [Test]
        public void EveryTargetLineReachesItsAimAtTheSetSpeed(
            [ValueSource(nameof(Targets))] AttackTarget target, [ValueSource(nameof(Speeds))] AttackSpeed speed, [ValueSource(nameof(Hands))] Hand hand)
        {
            var (attack, aim) = PlanAt(target, speed, hand, Height);

            var landing = PointOf(attack, attack.ThrustEndSeconds, hand, Height);
            var peak = FindPeakPointSpeed(attack, attack.ThrustStartSeconds, attack.ThrustEndSeconds, hand, Height);

            Assert.That(Vector3.Distance(landing, aim), Is.LessThan(0.002f), "The point lands on the aim.");
            Assert.That(Vector3.Distance(attack.Path.AimPoint, aim), Is.LessThan(0.002f), "The planned path ends on the aim.");
            Assert.That(peak, Is.EqualTo(ArmedDummyAttack.GetPeakPointSpeed(speed)).Within(ArmedDummyAttack.GetPeakPointSpeed(speed) * 0.02f));
            Assert.That(attack.Path.PeakSpeedMetersPerSecond, Is.EqualTo(ArmedDummyAttack.GetPeakPointSpeed(speed)).Within(1e-3f));
        }

        [Test]
        public void ThePointFollowsThePlannedStraightPath([ValueSource(nameof(Targets))] AttackTarget target, [ValueSource(nameof(Hands))] Hand hand)
        {
            var (attack, _) = PlanAt(target, AttackSpeed.Medium, hand, Height);
            var path = attack.Path;

            for (var time = path.StartSeconds; time <= path.EndSeconds; time += 0.01)
            {
                var point = PointOf(attack, time, hand, Height);
                Assert.That(Vector3.Distance(point, path.GetPoint(time)), Is.LessThan(0.002f), $"At {time - path.StartSeconds:0.00} s into the thrust.");
            }

            Assert.That(path.StartSeconds, Is.EqualTo(attack.ThrustStartSeconds));
            Assert.That(path.EndSeconds, Is.EqualTo(attack.ThrustEndSeconds).Within(1e-9));
            Assert.That(Vector3.Distance(path.StartPoint, PointOf(attack, attack.ThrustStartSeconds, hand, Height)), Is.LessThan(0.002f));
        }

        [Test]
        public void SpeedLevelsAreSlowMediumAndReal()
        {
            Assert.That(ArmedDummyAttack.GetPeakPointSpeed(AttackSpeed.Slow), Is.EqualTo(0.5f));
            Assert.That(ArmedDummyAttack.GetPeakPointSpeed(AttackSpeed.Medium), Is.EqualTo(1.5f));
            Assert.That(ArmedDummyAttack.GetPeakPointSpeed(AttackSpeed.Real), Is.InRange(2.5f, 3.0f));
        }

        [Test]
        public void AWindUpComesBeforeEveryThrust(
            [ValueSource(nameof(Targets))] AttackTarget target, [ValueSource(nameof(Speeds))] AttackSpeed speed)
        {
            var (attack, _) = PlanAt(target, speed, Hand.Right, Height);
            var guardReach = Vector3.Distance(PointOf(attack, Start, Hand.Right, Height), Shoulder);
            var windUpReach = Vector3.Distance(PointOf(attack, attack.ThrustStartSeconds, Hand.Right, Height), Shoulder);

            Assert.That(attack.GetPhase(attack.ThrustStartSeconds - 0.01), Is.EqualTo(AttackPhase.WindUp));
            Assert.That(attack.ThrustStartSeconds - attack.WindUpStartSeconds, Is.EqualTo(ArmedDummyTuning.WindUpSeconds).Within(1e-6));
            Assert.That(guardReach - windUpReach, Is.EqualTo(ArmedDummyTuning.WindUpDrawBackMeters).Within(0.002f), "The point visibly draws back.");
        }

        [Test]
        public void PhasesRunDelayWindUpThrustHoldRecoverInOrder()
        {
            var attack = PlanSimple();

            Assert.That(attack.GetPhase(Start), Is.EqualTo(AttackPhase.Waiting));
            Assert.That(attack.GetPhase(Start + Delay - 0.01), Is.EqualTo(AttackPhase.Waiting));
            Assert.That(attack.GetPhase(attack.WindUpStartSeconds + 0.01), Is.EqualTo(AttackPhase.WindUp));
            Assert.That(attack.GetPhase(attack.ThrustStartSeconds + 0.01), Is.EqualTo(AttackPhase.Thrust));
            Assert.That(attack.GetPhase(attack.ThrustEndSeconds + 0.01), Is.EqualTo(AttackPhase.Hold));
            Assert.That(attack.GetPhase(attack.ThrustEndSeconds + ArmedDummyTuning.HoldSeconds + 0.01), Is.EqualTo(AttackPhase.Recover));
            Assert.That(attack.GetPhase(attack.DoneSeconds), Is.EqualTo(AttackPhase.Done));
            Assert.That(attack.WindUpStartSeconds - Start, Is.EqualTo(Delay).Within(1e-6));
            Assert.That(attack.DoneSeconds - attack.ThrustEndSeconds,
                Is.EqualTo(ArmedDummyTuning.HoldSeconds + ArmedDummyTuning.RecoverSeconds).Within(1e-6));
        }

        [Test]
        public void ArmIsEnGardeInLineBeforeAndAfter()
        {
            var attack = PlanSimple();
            var guardPoint = PointOf(SwordArmReach.PoseEnGarde(Height, BladeLength));

            Assert.That(Vector3.Distance(PointOf(attack.GetPose(Start)), guardPoint), Is.LessThan(1e-4f));
            Assert.That(Vector3.Distance(PointOf(attack.GetPose(attack.ThrustEndSeconds + 0.1)), ReachableAim), Is.LessThan(0.002f), "Held at the aim.");
            Assert.That(Vector3.Distance(PointOf(attack.GetPose(attack.DoneSeconds + 1.0)), guardPoint), Is.LessThan(1e-4f));
        }

        [Test]
        public void ThePointNeverJumpsThroughTheWholeAttack([ValueSource(nameof(Targets))] AttackTarget target)
        {
            var (attack, _) = PlanAt(target, AttackSpeed.Real, Hand.Right, Height);

            var peak = FindPeakPointSpeed(attack, Start, attack.DoneSeconds + 0.1, Hand.Right, Height);

            Assert.That(peak, Is.LessThan(ArmedDummyAttack.GetPeakPointSpeed(AttackSpeed.Real) * 1.05f));
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

            Assert.That(shortest, Is.GreaterThanOrEqualTo(1.0f).And.LessThan(1.1f));
            Assert.That(longest, Is.LessThanOrEqualTo(3.0f).And.GreaterThan(2.9f));
        }

        [TestCase(0.99f)]
        [TestCase(3.01f)]
        [TestCase(-1.0f)]
        [TestCase(float.NaN)]
        public void APlanRefusesADelayOutsideOneToThreeSeconds(float delaySeconds)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                ArmedDummyAttack.Plan(Start, delaySeconds, Height, Hand.Right, ReachableAim, AttackSpeed.Medium, BladeLength));
        }

        [Test]
        public void TheArmStaysWithinItsJointLimitsThroughEveryAttack(
            [ValueSource(nameof(Targets))] AttackTarget target, [ValueSource(nameof(Hands))] Hand hand, [ValueSource(nameof(Heights))] float height)
        {
            var (attack, _) = PlanAt(target, AttackSpeed.Medium, hand, height);

            foreach (var pose in SamplePoses(attack, Start, attack.DoneSeconds + 0.1))
            {
                Assert.That(SwordArmReach.IsWithinLimits(pose), Is.True, DescribeJoints(pose));
            }
        }

        [Test]
        public void ParriedMidThrustTheArmStaysWithinItsJointLimits([ValueSource(nameof(Targets))] AttackTarget target)
        {
            var (attack, _) = PlanAt(target, AttackSpeed.Real, Hand.Right, Height);
            var parryAt = attack.ThrustStartSeconds + (attack.ThrustSeconds * 0.4);
            attack.ReportParried(parryAt);

            foreach (var pose in SamplePoses(attack, parryAt, attack.DoneSeconds + 0.1))
            {
                Assert.That(SwordArmReach.IsWithinLimits(pose), Is.True, DescribeJoints(pose));
            }
        }

        [Test]
        public void ReachKeepsTheArmWithinItsLimitsForAnyAim()
        {
            var random = new Random(3);
            for (var sample = 0; sample < 2000; sample++)
            {
                var aim = Shoulder + new Vector3(
                    ((float)random.NextDouble() * 6.0f) - 3.0f, ((float)random.NextDouble() * 6.0f) - 3.0f, ((float)random.NextDouble() * 6.0f) - 3.0f);

                var pose = SwordArmReach.PoseToReach(Height, aim, BladeLength);

                Assert.That(SwordArmReach.IsWithinLimits(pose), Is.True, $"Aim {aim}: {DescribeJoints(pose)}");
            }
        }

        [Test]
        public void TheFeetStayPlantedWhileTheArmAttacks()
        {
            var layout = DummyLayout.Create(Height, Hand.Right);
            var stanceBefore = new[] { layout.GetZone(TargetZone.Foot), layout.GetZone(TargetZone.Thigh), layout.GetZone(TargetZone.Body) };
            var (attack, _) = PlanAt(AttackTarget.Thigh, AttackSpeed.Real, Hand.Right, Height);

            layout.PoseArm(attack.GetPose(attack.ThrustEndSeconds));

            Assert.That(layout.GetZone(TargetZone.Foot), Is.EqualTo(stanceBefore[0]));
            Assert.That(layout.GetZone(TargetZone.Thigh), Is.EqualTo(stanceBefore[1]));
            Assert.That(layout.GetZone(TargetZone.Body), Is.EqualTo(stanceBefore[2]));
            Assert.That(layout.SilhouetteParts, Is.EqualTo(DummyLayout.Create(Height, Hand.Right).SilhouetteParts));
        }

        [Test]
        public void PosingTheArmInPlaceMatchesANewLayout([ValueSource(nameof(Hands))] Hand hand)
        {
            var pose = SwordArmReach.PoseWindUp(Height, BladeLength);
            var layout = DummyLayout.Create(Height, hand);

            layout.PoseArm(pose);
            var expected = DummyLayout.Create(Height, hand, pose);

            Assert.That(layout.Arm.Pose.Shoulder, Is.EqualTo(pose.Shoulder));
            for (var index = 0; index < expected.Zones.Count; index++)
            {
                Assert.That(layout.Zones[index].Shape, Is.EqualTo(expected.Zones[index].Shape), expected.Zones[index].Zone.ToString());
            }

            Assert.That(layout.Arm.GetBladePoint(BladeLength), Is.EqualTo(expected.Arm.GetBladePoint(BladeLength)));
        }

        [Test]
        public void ParryMidThrustRecoversFromWhereTheArmIs()
        {
            var attack = PlanSimple(AttackSpeed.Slow);
            var parryAt = (attack.ThrustStartSeconds + attack.ThrustEndSeconds) * 0.5;
            var pointBefore = PointOf(attack.GetPose(parryAt));

            Assert.That(attack.ReportParried(parryAt), Is.True);

            Assert.That(attack.IsParried, Is.True);
            Assert.That(attack.GetPhase(parryAt + 0.01), Is.EqualTo(AttackPhase.Recover));
            Assert.That(Vector3.Distance(PointOf(attack.GetPose(parryAt)), pointBefore), Is.LessThan(1e-4f), "No jump at the parry.");
            Assert.That(attack.DoneSeconds, Is.EqualTo(parryAt + ArmedDummyTuning.RecoverSeconds).Within(1e-6));
            Assert.That(attack.ReportParried(parryAt + 0.05), Is.False, "Parried once.");
        }

        [Test]
        public void ContactBeforeTheThrustDoesNotParry()
        {
            var attack = PlanSimple();

            Assert.That(attack.ReportParried(Start + 0.5), Is.False);
            Assert.That(attack.ReportParried(attack.WindUpStartSeconds + 0.1), Is.False);
            Assert.That(attack.IsParried, Is.False);
            Assert.That(attack.GetPhase(attack.ThrustStartSeconds + 0.01), Is.EqualTo(AttackPhase.Thrust));
        }

        [Test]
        public void HandAndForearmFallBackToTheChestWithoutTheFencersBlade()
        {
            var layout = DummyLayout.Create(Height, Hand.Right);
            var distance = DistancePresets.GetDistanceMeters(DummyDistance.Extension, Height);
            var chest = AttackAim.GetAimInDummyFrame(AttackTarget.Chest, layout, distance, null, Vector3.UnitZ);

            Assert.That(AttackAim.GetAimInDummyFrame(AttackTarget.Hand, layout, distance, null, Vector3.UnitZ), Is.EqualTo(chest));
            Assert.That(AttackAim.GetAimInDummyFrame(AttackTarget.Forearm, layout, distance, null, Vector3.UnitZ), Is.EqualTo(chest));
        }

        [Test]
        public void TargetsAreDrawnEvenly()
        {
            var random = new Random(11);
            var counts = new Dictionary<AttackTarget, int>();
            for (var draw = 0; draw < 4000; draw++)
            {
                var target = AttackAim.DrawTarget(random);
                counts[target] = counts.TryGetValue(target, out var count) ? count + 1 : 1;
            }

            Assert.That(counts.Keys, Is.EquivalentTo(Targets));
            Assert.That(counts.Values, Has.All.InRange(850, 1150));
        }

        [Test]
        public void AnAimNearerThanTheArmCanFoldGetsThePointAsNearAsItReaches()
        {
            var closeAim = Shoulder + (Vector3.Normalize(new Vector3(-0.1f, -0.1f, 1.0f)) * 0.8f);
            var shortest = SwordArmReach.GetShortestReachMeters(Height, BladeLength);

            var attack = ArmedDummyAttack.Plan(Start, Delay, Height, Hand.Right, closeAim, AttackSpeed.Medium, BladeLength);
            var landing = PointOf(attack.GetPose(attack.ThrustEndSeconds));

            Assert.That(shortest, Is.GreaterThan(0.8f));
            Assert.That(Vector3.Distance(landing, Shoulder), Is.EqualTo(shortest).Within(0.002f));
            Assert.That(Vector3.Dot(Vector3.Normalize(landing - Shoulder), Vector3.Normalize(closeAim - Shoulder)), Is.GreaterThan(0.9999f));
            Assert.That(Vector3.Distance(attack.Path.AimPoint, landing), Is.LessThan(0.002f), "The planned path says where the point really lands.");
        }

        /// <summary>
        /// A thrust at <paramref name="target"/> for a Fencer of the Dummy's height and hand standing en garde at the
        /// En Garde Line; the Fencer's guard is where the Dummy's own guard would be with the arm in line, seen from the
        /// Strip frame. Chest and thigh are attacked from extension distance. The hand and forearm are nearer than the
        /// arm and a straight-wristed 90 cm blade can fold at that distance, so they are attacked from further out
        /// (<see cref="AttackAim.GetStandBackMeters"/>, PER-100), as a real fencer attacks the hand from further away.
        /// </summary>
        private static (ArmedDummyAttack Attack, Vector3 Aim) PlanAt(AttackTarget target, AttackSpeed speed, Hand hand, float height)
        {
            var layout = DummyLayout.Create(height, hand);
            var distance = DistancePresets.GetDistanceMeters(DummyDistance.Extension, height) + AttackAim.GetStandBackMeters(target);
            var fencerGuard = layout.Arm.GetGuardPosition();
            var fencerBladeDirection = layout.Arm.GetBladeDirection();
            var aim = AttackAim.GetAimInDummyFrame(target, layout, distance, fencerGuard, fencerBladeDirection);
            return (ArmedDummyAttack.Plan(Start, Delay, height, hand, aim, speed, BladeLength), aim);
        }

        private static ArmedDummyAttack PlanSimple(AttackSpeed speed = AttackSpeed.Medium)
        {
            return ArmedDummyAttack.Plan(Start, Delay, Height, Hand.Right, ReachableAim, speed, BladeLength);
        }

        private static Vector3 PointOf(SwordArmPose pose)
        {
            return new SwordArm(Height, Hand.Right, pose).GetBladePoint(BladeLength);
        }

        /// <summary>The point in the Dummy's own frame at <paramref name="nowSeconds"/>, as the Dummy draws it.</summary>
        private static Vector3 PointOf(ArmedDummyAttack attack, double nowSeconds, Hand hand, float height)
        {
            return new SwordArm(height, hand, attack.GetPose(nowSeconds)).GetBladePoint(BladeLength);
        }

        [Test]
        public void AKnockedDummyWaitsUntilItsArmIsBackBeforeWindingUp()
        {
            var attack = ArmedDummyAttack.Plan(Start, Delay, Height, Hand.Right, ReachableAim, AttackSpeed.Medium, BladeLength);
            var plannedThrust = attack.ThrustStartSeconds;

            Assert.That(attack.PostponeWindUp(Start + 1.0, Start + Delay + 0.5), Is.True, "Knocked while waiting: the plan moves.");

            Assert.That(attack.WindUpStartSeconds, Is.EqualTo(Start + Delay + 0.5).Within(1e-9));
            Assert.That(attack.ThrustStartSeconds, Is.EqualTo(plannedThrust + 0.5).Within(1e-9));
            Assert.That(attack.Path.StartSeconds, Is.EqualTo(attack.ThrustStartSeconds).Within(1e-9), "The judged path moves with it.");
            Assert.That(attack.GetPhase(Start + Delay + 0.4), Is.EqualTo(AttackPhase.Waiting));
            Assert.That(attack.GetPhase(attack.ThrustEndSeconds + (ArmedDummyTuning.HoldSeconds * 0.5)), Is.EqualTo(AttackPhase.Hold), "Hold and recovery move too.");
            Assert.That(attack.PostponeWindUp(Start + 1.0, Start + 1.5), Is.False, "Ready before the wind-up: nothing changes.");
            Assert.That(attack.PostponeWindUp(attack.WindUpStartSeconds + 0.01, attack.WindUpStartSeconds + 1.0), Is.False, "Once wound up, the attack goes on.");
        }

        private static IEnumerable<SwordArmPose> SamplePoses(ArmedDummyAttack attack, double from, double to)
        {
            for (var time = from; time <= to; time += 0.005)
            {
                yield return attack.GetPose(time);
            }
        }

        private static string DescribeJoints(SwordArmPose pose)
        {
            return $"elbow {SwordArmReach.GetElbowBendDegrees(pose):0.0} deg, shoulder {SwordArmReach.GetShoulderSwingDegrees(pose):0.0} deg, " +
                $"wrist {SwordArmReach.GetWristBendDegrees(pose):0.0} deg";
        }

        private static float FindPeakPointSpeed(ArmedDummyAttack attack, double from, double to, Hand hand, float height)
        {
            var peak = 0.0f;
            var previous = PointOf(attack, from, hand, height);
            for (var time = from + SampleSeconds; time <= to; time += SampleSeconds)
            {
                var point = PointOf(attack, time, hand, height);
                peak = Math.Max(peak, Vector3.Distance(previous, point) / (float)SampleSeconds);
                previous = point;
            }

            return peak;
        }
    }
}
