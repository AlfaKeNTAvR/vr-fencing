// The joints the rigged Dummy body is posed on (PER-78): they sit on the Target Zones' capsules, follow the sword
// arm, scale with height and mirror for a left-handed Dummy, so the drawn body wraps the zones that are judged.
using System;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Tests.Dummy
{
    public class DummySkeletonTests
    {
        private const float Tolerance = 1e-4f;
        private const float Height = 1.65f;

        private static readonly SkeletonJoint[] AllJoints = Enum.GetValues(typeof(SkeletonJoint)).Cast<SkeletonJoint>().ToArray();

        [TestCase(Hand.Right)]
        [TestCase(Hand.Left)]
        public void SwordArmJointsAreTheArmsJoints(Hand hand)
        {
            var layout = DummyLayout.Create(Height, hand, SwordArmPose.ArmInLine.WithElbowBend(0.6f));

            AssertNear(DummySkeleton.GetJointPosition(layout, SkeletonJoint.SwordShoulder), layout.Arm.GetPose(ArmJoint.Shoulder).Position);
            AssertNear(DummySkeleton.GetJointPosition(layout, SkeletonJoint.SwordElbow), layout.Arm.GetPose(ArmJoint.Elbow).Position);
            AssertNear(DummySkeleton.GetJointPosition(layout, SkeletonJoint.SwordWrist), layout.Arm.GetPose(ArmJoint.Wrist).Position);
        }

        [TestCase(Hand.Right)]
        [TestCase(Hand.Left)]
        public void ArmZonesLieOnTheArmBones(Hand hand)
        {
            var layout = DummyLayout.Create(Height, hand, SwordArmPose.ArmInLine.WithElbowBend(0.6f));

            AssertZoneOnBone(layout, TargetZone.UpperArm, SkeletonJoint.SwordShoulder, SkeletonJoint.SwordElbow);
            AssertZoneOnBone(layout, TargetZone.Forearm, SkeletonJoint.SwordElbow, SkeletonJoint.SwordWrist);
            AssertZoneOnBone(layout, TargetZone.Wrist, SkeletonJoint.SwordElbow, SkeletonJoint.SwordHandEnd);
            AssertZoneOnBone(layout, TargetZone.Hand, SkeletonJoint.SwordWrist, SkeletonJoint.SwordHandEnd);
        }

        [TestCase(Hand.Right)]
        [TestCase(Hand.Left)]
        public void LegZonesLieOnTheFrontLeg(Hand hand)
        {
            var layout = DummyLayout.Create(Height, hand);

            AssertZoneOnBone(layout, TargetZone.Thigh, SkeletonJoint.FrontKnee, SkeletonJoint.FrontHip);

            // The foot zone runs heel to toe under the ankle, so it ends at the toe and passes under the ankle.
            var foot = layout.GetZone(TargetZone.Foot);
            var toe = DummySkeleton.GetJointPosition(layout, SkeletonJoint.FrontToe);
            var ankle = DummySkeleton.GetJointPosition(layout, SkeletonJoint.FrontAnkle);
            Assert.That(Vector3.Distance(foot.End, toe), Is.LessThan(Tolerance));
            Assert.That(DistanceToSegment(ankle, foot.Start, foot.End), Is.LessThan(foot.RadiusMeters));
        }

        [TestCase(Hand.Right)]
        [TestCase(Hand.Left)]
        public void TheBodyZoneRunsUpTheSpine(Hand hand)
        {
            var layout = DummyLayout.Create(Height, hand);
            var body = layout.GetZone(TargetZone.Body);
            var hipCenter = DummySkeleton.GetJointPosition(layout, SkeletonJoint.HipCenter);
            var shoulderCenter = DummySkeleton.GetJointPosition(layout, SkeletonJoint.ShoulderCenter);

            // The trunk capsule is the drawn torso, so its axis stays well inside it along the spine.
            Assert.That(DistanceToSegment(body.Start, hipCenter, shoulderCenter), Is.LessThan(body.RadiusMeters * 0.5f));
            Assert.That(DistanceToSegment(body.End, hipCenter, shoulderCenter), Is.LessThan(body.RadiusMeters * 0.5f));
        }

        [Test]
        public void ALeftHandedSkeletonIsTheRightHandedOneMirrored()
        {
            var right = DummyLayout.Create(Height, Hand.Right);
            var left = DummyLayout.Create(Height, Hand.Left);

            foreach (var joint in AllJoints)
            {
                var mirrored = DummySkeleton.GetJointPosition(right, joint);
                AssertNear(DummySkeleton.GetJointPosition(left, joint), new Vector3(-mirrored.X, mirrored.Y, mirrored.Z), joint.ToString());
            }
        }

        [Test]
        public void JointsScaleWithHeight()
        {
            var tall = DummyLayout.Create(2.0f, Hand.Right);
            var unit = DummyLayout.Create(1.0f, Hand.Right);

            foreach (var joint in AllJoints)
            {
                AssertNear(DummySkeleton.GetJointPosition(tall, joint), DummySkeleton.GetJointPosition(unit, joint) * 2.0f, joint.ToString());
            }
        }

        [Test]
        public void TheSwordSideIsTowardTheFencer()
        {
            var layout = DummyLayout.Create(Height, Hand.Right);

            Assert.That(DummySkeleton.GetJointPosition(layout, SkeletonJoint.SwordShoulder).Z, Is.GreaterThan(DummySkeleton.GetJointPosition(layout, SkeletonJoint.BackShoulder).Z));
            Assert.That(DummySkeleton.GetJointPosition(layout, SkeletonJoint.FrontAnkle).Z, Is.GreaterThan(DummySkeleton.GetJointPosition(layout, SkeletonJoint.BackAnkle).Z));
            Assert.That(DummySkeleton.GetJointPosition(layout, SkeletonJoint.SwordHandEnd).Z, Is.GreaterThan(DummySkeleton.GetJointPosition(layout, SkeletonJoint.SwordWrist).Z));
        }

        private static void AssertZoneOnBone(DummyLayout layout, TargetZone zone, SkeletonJoint from, SkeletonJoint to)
        {
            var capsule = layout.GetZone(zone);
            var start = DummySkeleton.GetJointPosition(layout, from);
            var end = DummySkeleton.GetJointPosition(layout, to);
            Assert.That(DistanceToSegment(capsule.Start, start, end), Is.LessThan(Tolerance), $"{zone} start");
            Assert.That(DistanceToSegment(capsule.End, start, end), Is.LessThan(Tolerance), $"{zone} end");
        }

        private static float DistanceToSegment(Vector3 point, Vector3 start, Vector3 end)
        {
            var segment = end - start;
            var along = Math.Clamp(Vector3.Dot(point - start, segment) / segment.LengthSquared(), 0.0f, 1.0f);
            return Vector3.Distance(point, start + (segment * along));
        }

        private static void AssertNear(Vector3 actual, Vector3 expected, string message = null)
        {
            Assert.That(Vector3.Distance(actual, expected), Is.LessThan(Tolerance), message);
        }
    }
}
