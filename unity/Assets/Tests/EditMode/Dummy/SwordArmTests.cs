// The Dummy's sword arm as a joint chain (PER-32): the arm Target Zones derive from the joint poses, so an armed
// Dummy can later move its arm and the zones follow.
using System;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Tests.Dummy
{
    public class SwordArmTests
    {
        private const float Tolerance = 1e-4f;
        private const float Height = 1.65f;
        private const float ElbowBendRadians = 0.6f;

        [Test]
        public void ArmInLineIsStraightFromShoulderToHand()
        {
            var arm = new SwordArm(Height, Hand.Right, SwordArmPose.ArmInLine);

            var shoulder = arm.GetPose(ArmJoint.Shoulder).Position;
            var upperArm = Vector3.Normalize(arm.GetPose(ArmJoint.Elbow).Position - shoulder);
            var wholeArm = Vector3.Normalize(arm.GetZone(TargetZone.Hand).Center - shoulder);

            Assert.That(Vector3.Dot(upperArm, wholeArm), Is.EqualTo(1.0f).Within(Tolerance));
            Assert.That(upperArm.Z, Is.GreaterThan(0.9f), "The arm in line points at the Fencer (+z).");
        }

        [Test]
        public void JointsAreOneSegmentLengthApart()
        {
            var arm = new SwordArm(Height, Hand.Right, SwordArmPose.ArmInLine.WithElbowBend(ElbowBendRadians));

            var upperArm = Vector3.Distance(arm.GetPose(ArmJoint.Shoulder).Position, arm.GetPose(ArmJoint.Elbow).Position);
            var forearm = Vector3.Distance(arm.GetPose(ArmJoint.Elbow).Position, arm.GetPose(ArmJoint.Wrist).Position);

            Assert.That(upperArm, Is.EqualTo(0.186f * Height).Within(Tolerance));
            Assert.That(forearm, Is.EqualTo(0.146f * Height).Within(Tolerance));
        }

        [Test]
        public void BendingTheElbowMovesTheForearmAndHandZonesButNotTheUpperArm()
        {
            var straight = DummyLayout.Create(Height, Hand.Right);
            var bent = straight.WithArmPose(SwordArmPose.ArmInLine.WithElbowBend(ElbowBendRadians));

            AssertSame(bent.GetZone(TargetZone.UpperArm), straight.GetZone(TargetZone.UpperArm));
            Assert.That(Vector3.Distance(bent.GetZone(TargetZone.Forearm).Center, straight.GetZone(TargetZone.Forearm).Center), Is.GreaterThan(0.02f));
            Assert.That(Vector3.Distance(bent.GetZone(TargetZone.Hand).Center, straight.GetZone(TargetZone.Hand).Center), Is.GreaterThan(0.05f));
            Assert.That(bent.GetZone(TargetZone.Hand).Center.Y, Is.GreaterThan(straight.GetZone(TargetZone.Hand).Center.Y), "A positive bend raises the hand.");

            // The stance zones do not move with the arm.
            AssertSame(bent.GetZone(TargetZone.Body), straight.GetZone(TargetZone.Body));
            AssertSame(bent.GetZone(TargetZone.Thigh), straight.GetZone(TargetZone.Thigh));
        }

        [Test]
        public void TheForearmTurnsByTheElbowAngle()
        {
            var arm = new SwordArm(Height, Hand.Right, SwordArmPose.ArmInLine.WithElbowBend(ElbowBendRadians));

            var upperArm = Vector3.Normalize(arm.GetPose(ArmJoint.Elbow).Position - arm.GetPose(ArmJoint.Shoulder).Position);
            var forearm = Vector3.Normalize(arm.GetPose(ArmJoint.Wrist).Position - arm.GetPose(ArmJoint.Elbow).Position);

            Assert.That(Vector3.Dot(upperArm, forearm), Is.EqualTo((float)Math.Cos(ElbowBendRadians)).Within(Tolerance));
        }

        [Test]
        public void ArmZonesFollowTheirJoint()
        {
            var arm = new SwordArm(Height, Hand.Right, SwordArmPose.ArmInLine.WithElbowBend(ElbowBendRadians));

            foreach (var zone in new[] { TargetZone.Hand, TargetZone.Wrist, TargetZone.Forearm, TargetZone.UpperArm })
            {
                var joint = arm.GetPose(SwordArm.GetAttachedJoint(zone));
                var local = arm.GetZoneInJointFrame(zone);
                var expectedCenter = joint.Position + Vector3.Transform(local.Center, joint.Rotation);
                Assert.That(Vector3.Distance(arm.GetZone(zone).Center, expectedCenter), Is.LessThan(Tolerance), zone.ToString());
            }

            Assert.That(Vector3.Distance(arm.GetZone(TargetZone.Wrist).Center, arm.GetPose(ArmJoint.Wrist).Position), Is.LessThan(Tolerance));
        }

        [Test]
        public void LeftHandedArmIsTheRightHandedOneMirroredInAnyPose()
        {
            var pose = SwordArmPose.ArmInLine.WithElbowBend(ElbowBendRadians);
            var right = DummyLayout.Create(Height, Hand.Right, pose);
            var left = DummyLayout.Create(Height, Hand.Left, pose);

            foreach (var zone in right.Zones)
            {
                var mirrored = HandMirroring.MirrorPosition(zone.Shape.Center);
                Assert.That(Vector3.Distance(left.GetZone(zone.Zone).Center, mirrored), Is.LessThan(Tolerance), zone.Zone.ToString());
            }
        }

        [Test]
        public void RejectsAnUnsetPose()
        {
            Assert.Throws<ArgumentException>(() => new SwordArm(Height, Hand.Right, default(SwordArmPose)));
        }

        private static void AssertSame(Capsule actual, Capsule expected)
        {
            Assert.That(Vector3.Distance(actual.Start, expected.Start), Is.LessThan(Tolerance));
            Assert.That(Vector3.Distance(actual.End, expected.End), Is.LessThan(Tolerance));
        }
    }
}
