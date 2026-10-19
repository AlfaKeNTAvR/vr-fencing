// The "arm extended" rule (PER-37): a straight elbow and the wrist reaching forward of the shoulder,
// measured in the Strip frame (+z toward the Dummy).
using System;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Tracking;

namespace VrFencing.Tests.Tracking
{
    public class ArmExtensionTests
    {
        private const float UpperArmMeters = 0.30f;
        private const float ForearmMeters = 0.25f;
        private static readonly Vector3 Shoulder = new Vector3(0.2f, 1.4f, 0.0f);

        /// <summary>
        /// An arm with the upper arm straight forward (+z) and the forearm bent upward so the elbow angle is
        /// <paramref name="elbowAngleDegrees"/> (180 straight, 90 a right angle).
        /// </summary>
        public static (Vector3 Shoulder, Vector3 Elbow, Vector3 Wrist) ArmPointingForward(float elbowAngleDegrees)
        {
            var radians = elbowAngleDegrees * MathF.PI / 180.0f;
            var elbow = Shoulder + new Vector3(0.0f, 0.0f, UpperArmMeters);
            var forearmDirection = new Vector3(0.0f, MathF.Sin(radians), -MathF.Cos(radians));
            return (Shoulder, elbow, elbow + (forearmDirection * ForearmMeters));
        }

        [Test]
        public void StraightArmReachingForwardIsExtended()
        {
            var arm = ArmPointingForward(180.0f);

            Assert.That(ArmExtension.ElbowAngleDegrees(arm.Shoulder, arm.Elbow, arm.Wrist), Is.EqualTo(180.0f).Within(0.1f));
            Assert.That(ArmExtension.IsExtended(arm.Shoulder, arm.Elbow, arm.Wrist), Is.True);
        }

        [Test]
        public void RelaxedExtensionJustOverTheThresholdCounts()
        {
            var arm = ArmPointingForward(ArmExtension.MinExtendedElbowAngleDegrees + 5.0f);

            Assert.That(ArmExtension.IsExtended(arm.Shoulder, arm.Elbow, arm.Wrist), Is.True);
        }

        [Test]
        public void ElbowBentPastTheThresholdIsNotExtended()
        {
            var arm = ArmPointingForward(ArmExtension.MinExtendedElbowAngleDegrees - 5.0f);

            Assert.That(ArmExtension.IsExtended(arm.Shoulder, arm.Elbow, arm.Wrist), Is.False);
        }

        [Test]
        public void EnGardeArmBentAtTheElbowIsNotExtended()
        {
            var arm = ArmPointingForward(100.0f);

            Assert.That(ArmExtension.ElbowAngleDegrees(arm.Shoulder, arm.Elbow, arm.Wrist), Is.EqualTo(100.0f).Within(0.1f));
            Assert.That(ArmExtension.IsExtended(arm.Shoulder, arm.Elbow, arm.Wrist), Is.False);
        }

        [Test]
        public void StraightArmHangingDownIsNotExtended()
        {
            var elbow = Shoulder + new Vector3(0.0f, -UpperArmMeters, 0.0f);
            var wrist = elbow + new Vector3(0.0f, -ForearmMeters, 0.0f);

            Assert.That(ArmExtension.IsExtended(Shoulder, elbow, wrist), Is.False);
        }

        [Test]
        public void StraightArmHeldOutToTheSideIsNotExtended()
        {
            var elbow = Shoulder + new Vector3(UpperArmMeters, 0.0f, 0.0f);
            var wrist = elbow + new Vector3(ForearmMeters, 0.0f, 0.0f);

            Assert.That(ArmExtension.IsExtended(Shoulder, elbow, wrist), Is.False);
        }

        [Test]
        public void StraightArmAimedLowAtTheThighStillCounts()
        {
            // About 35 degrees below horizontal: forward reach is cos(35) = 0.82 of the arm's length.
            var direction = Vector3.Normalize(new Vector3(0.0f, -0.7f, 1.0f));
            var elbow = Shoulder + (direction * UpperArmMeters);
            var wrist = elbow + (direction * ForearmMeters);

            Assert.That(ArmExtension.IsExtended(Shoulder, elbow, wrist), Is.True);
        }

        [Test]
        public void MirroredLeftArmGivesTheSameAnswer()
        {
            var arm = ArmPointingForward(170.0f);
            Vector3 Mirror(Vector3 point) => new Vector3(-point.X, point.Y, point.Z);

            Assert.That(
                ArmExtension.IsExtended(Mirror(arm.Shoulder), Mirror(arm.Elbow), Mirror(arm.Wrist)),
                Is.EqualTo(ArmExtension.IsExtended(arm.Shoulder, arm.Elbow, arm.Wrist)));
            Assert.That(
                ArmExtension.ElbowAngleDegrees(Mirror(arm.Shoulder), Mirror(arm.Elbow), Mirror(arm.Wrist)),
                Is.EqualTo(170.0f).Within(0.1f));
        }

        [Test]
        public void CollapsedJointsAreNotExtended()
        {
            Assert.That(ArmExtension.ElbowAngleDegrees(Shoulder, Shoulder, Shoulder), Is.EqualTo(0.0f));
            Assert.That(ArmExtension.IsExtended(Shoulder, Shoulder, Shoulder), Is.False);
        }
    }
}
