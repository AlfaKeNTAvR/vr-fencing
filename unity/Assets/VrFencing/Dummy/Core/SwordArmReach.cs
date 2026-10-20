using System;
using System.Numerics;
using VrFencing.Settings;

namespace VrFencing.Dummy
{
    /// <summary>
    /// Poses the Armed Dummy's sword arm so the point of the blade in its hand lands on an aim point (PER-40): a
    /// two-link reach, with the upper arm as one link and the forearm, hand and blade (wrist straight) as the other.
    /// The elbow bends in the vertical plane through the shoulder and the aim, forearm up, as in a thrust from a
    /// slightly bent arm.
    ///
    /// Every pose it gives is within the joint limits in <see cref="ArmedDummyTuning"/> (<see cref="IsWithinLimits"/>):
    /// the aim direction is kept within <see cref="ArmedDummyTuning.MaximumAimOffLineDegrees"/> of the in-line
    /// direction, and the reach is kept long enough that neither the elbow nor the shoulder has to turn past its limit.
    ///
    /// Everything is in the right-handed Dummy frame (see <see cref="EnGardePose"/>), as <see cref="SwordArmPose"/>
    /// is: a left-handed Dummy mirrors the aim into it first, and <see cref="SwordArm"/> mirrors the pose back out.
    /// </summary>
    public static class SwordArmReach
    {
        // Keeps the two links from folding flat onto each other at the shortest reach.
        private const float FoldMarginMeters = 0.001f;

        // Slack on the limit check for float rounding.
        private const float LimitToleranceDegrees = 0.05f;

        private const float DegreesToRadians = (float)(Math.PI / 180.0);

        /// <summary>Shoulder to point with the arm and blade straight, for a Dummy of <paramref name="heightMeters"/>.</summary>
        public static float GetFullReachMeters(float heightMeters, float bladeLengthMeters)
        {
            return GetUpperArmMeters(heightMeters) + GetForearmToPointMeters(heightMeters, bladeLengthMeters);
        }

        /// <summary>
        /// Shoulder to point with the arm as bent as the joint limits allow. An aim nearer the shoulder than this gets
        /// the point this far out along the line to it.
        /// </summary>
        public static float GetShortestReachMeters(float heightMeters, float bladeLengthMeters)
        {
            var upperArm = GetUpperArmMeters(heightMeters);
            var forearmToPoint = GetForearmToPointMeters(heightMeters, bladeLengthMeters);

            // The elbow limit: the reach with the elbow bent as far as it may go (law of cosines).
            var elbowInteriorAngle = Math.PI - (ArmedDummyTuning.MaximumElbowBendDegrees * DegreesToRadians);
            var elbowLimitedReach = (float)Math.Sqrt(
                (upperArm * upperArm) + (forearmToPoint * forearmToPoint) - (2.0 * upperArm * forearmToPoint * Math.Cos(elbowInteriorAngle)));

            // The shoulder limit: the upper arm drops below the line to the aim by more as the reach shortens; with the
            // aim up to the off-line limit from the in-line direction, the drop may use what is left of the swing.
            var dropAngle = (ArmedDummyTuning.MaximumShoulderSwingDegrees - ArmedDummyTuning.MaximumAimOffLineDegrees) * DegreesToRadians;
            var sine = upperArm * Math.Sin(dropAngle);
            var shoulderLimitedReach = (float)((upperArm * Math.Cos(dropAngle)) + Math.Sqrt(Math.Max(0.0, (forearmToPoint * forearmToPoint) - (sine * sine))));

            var foldedReach = Math.Abs(forearmToPoint - upperArm) + FoldMarginMeters;
            return Math.Max(foldedReach, Math.Max(elbowLimitedReach, shoulderLimitedReach));
        }

        /// <summary>The sword shoulder in the right-handed Dummy frame.</summary>
        public static Vector3 GetShoulderPosition(float heightMeters)
        {
            return EnGardePose.SwordShoulder * heightMeters;
        }

        /// <summary>
        /// The arm pose that puts the point of a blade of <paramref name="bladeLengthMeters"/> on
        /// <paramref name="aim"/>. An aim beyond the full reach gets the straight arm pointing at it, so the point
        /// falls short; one nearer than <see cref="GetShortestReachMeters"/> gets the point that far out along the line
        /// to it; one too far off the in-line direction gets the nearest direction within
        /// <see cref="ArmedDummyTuning.MaximumAimOffLineDegrees"/>.
        /// </summary>
        /// <param name="aim">Where the point should land, in the right-handed Dummy frame.</param>
        public static SwordArmPose PoseToReach(float heightMeters, Vector3 aim, float bladeLengthMeters)
        {
            return PoseToReach(heightMeters, GetShoulderPosition(heightMeters), aim, bladeLengthMeters);
        }

        /// <summary>
        /// As <see cref="PoseToReach(float, Vector3, float)"/>, from the sword shoulder at <paramref name="shoulder"/>
        /// (right-handed Dummy frame) where the torso has carried it, e.g. leaning into a lunge (PER-115).
        /// </summary>
        public static SwordArmPose PoseToReach(float heightMeters, Vector3 shoulder, Vector3 aim, float bladeLengthMeters)
        {
            DummyLayout.RequirePositiveHeight(heightMeters);
            var upperArm = GetUpperArmMeters(heightMeters);
            var forearmToPoint = GetForearmToPointMeters(heightMeters, bladeLengthMeters);
            var shoulderToAim = aim - shoulder;
            var aimDistance = shoulderToAim.Length();
            var towardAim = aimDistance < 1e-6f ? EnGardePose.ArmInLineDirection : KeepNearInLine(shoulderToAim / aimDistance);
            var reach = Math.Clamp(aimDistance, GetShortestReachMeters(heightMeters, bladeLengthMeters), upperArm + forearmToPoint);

            // Law of cosines: the upper arm drops below the line to the aim by the shoulder angle, and the forearm bends
            // back up by the elbow bend, so the point comes back onto that line at the reach.
            var shoulderAngle = Math.Acos(Math.Clamp(
                ((upperArm * upperArm) + (reach * reach) - (forearmToPoint * forearmToPoint)) / (2.0f * upperArm * reach), -1.0f, 1.0f));
            var elbowInteriorAngle = Math.Acos(Math.Clamp(
                ((upperArm * upperArm) + (forearmToPoint * forearmToPoint) - (reach * reach)) / (2.0f * upperArm * forearmToPoint), -1.0f, 1.0f));
            var elbowBend = (float)(Math.PI - elbowInteriorAngle);

            var upInPlane = GetUpSquareTo(towardAim);
            var cosine = (float)Math.Cos(shoulderAngle);
            var sine = (float)Math.Sin(shoulderAngle);
            var upperArmDirection = (towardAim * cosine) - (upInPlane * sine);
            var upperArmUp = (towardAim * sine) + (upInPlane * cosine);
            var upperArmRight = Vector3.Cross(upperArmUp, upperArmDirection);

            var shoulderRotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(
                upperArmRight.X, upperArmRight.Y, upperArmRight.Z, 0.0f,
                upperArmUp.X, upperArmUp.Y, upperArmUp.Z, 0.0f,
                upperArmDirection.X, upperArmDirection.Y, upperArmDirection.Z, 0.0f,
                0.0f, 0.0f, 0.0f, 1.0f)));
            return new SwordArmPose(shoulderRotation, Quaternion.CreateFromAxisAngle(-Vector3.UnitX, elbowBend), Quaternion.Identity);
        }

        /// <summary>The en garde between attacks, the blade held in line (<see cref="ArmedDummyTuning.GuardDrawBackMeters"/>).</summary>
        public static SwordArmPose PoseEnGarde(float heightMeters, float bladeLengthMeters)
        {
            return PoseDrawnBack(heightMeters, bladeLengthMeters, ArmedDummyTuning.GuardDrawBackMeters);
        }

        /// <summary>
        /// The wind-up before a thrust: the point drawn back a further <see cref="ArmedDummyTuning.WindUpDrawBackMeters"/>
        /// from the en garde along the in-line direction, the elbow bending more to do it.
        /// </summary>
        public static SwordArmPose PoseWindUp(float heightMeters, float bladeLengthMeters)
        {
            return PoseDrawnBack(heightMeters, bladeLengthMeters, ArmedDummyTuning.GuardDrawBackMeters + ArmedDummyTuning.WindUpDrawBackMeters);
        }

        /// <summary>The point on the in-line direction, <paramref name="drawBackMeters"/> short of full reach.</summary>
        public static SwordArmPose PoseDrawnBack(float heightMeters, float bladeLengthMeters, float drawBackMeters)
        {
            var reach = GetFullReachMeters(heightMeters, bladeLengthMeters) - drawBackMeters;
            var aim = GetShoulderPosition(heightMeters) + (EnGardePose.ArmInLineDirection * reach);
            return PoseToReach(heightMeters, aim, bladeLengthMeters);
        }

        /// <summary>The point of a blade of <paramref name="bladeLengthMeters"/> in <paramref name="pose"/>, in the right-handed Dummy frame.</summary>
        public static Vector3 GetPoint(float heightMeters, SwordArmPose pose, float bladeLengthMeters)
        {
            return new SwordArm(heightMeters, Hand.Right, pose).GetBladePoint(bladeLengthMeters);
        }

        /// <summary>The point in <paramref name="pose"/> with the shoulder at <paramref name="shoulder"/>, in the right-handed Dummy frame.</summary>
        public static Vector3 GetPoint(float heightMeters, Vector3 shoulder, SwordArmPose pose, float bladeLengthMeters)
        {
            return GetPoint(heightMeters, pose, bladeLengthMeters) + (shoulder - GetShoulderPosition(heightMeters));
        }

        /// <summary>The elbow bend from straight, in degrees: 0 straight, positive with the forearm turned up.</summary>
        public static float GetElbowBendDegrees(SwordArmPose pose)
        {
            var forearm = Vector3.Transform(Vector3.UnitZ, pose.Elbow);
            return (float)(Math.Atan2(forearm.Y, forearm.Z) / DegreesToRadians);
        }

        /// <summary>How far the upper arm points from the in-line direction, in degrees.</summary>
        public static float GetShoulderSwingDegrees(SwordArmPose pose)
        {
            var upperArm = Vector3.Transform(Vector3.UnitZ, pose.Shoulder);
            return (float)(Math.Acos(Math.Clamp(Vector3.Dot(upperArm, EnGardePose.ArmInLineDirection), -1.0f, 1.0f)) / DegreesToRadians);
        }

        /// <summary>How far the wrist bends the hand off the forearm's line, in degrees.</summary>
        public static float GetWristBendDegrees(SwordArmPose pose)
        {
            var hand = Vector3.Transform(Vector3.UnitZ, pose.Wrist);
            return (float)(Math.Acos(Math.Clamp(hand.Z, -1.0f, 1.0f)) / DegreesToRadians);
        }

        /// <summary>Whether every joint of <paramref name="pose"/> is within the limits in <see cref="ArmedDummyTuning"/>.</summary>
        public static bool IsWithinLimits(SwordArmPose pose)
        {
            var elbow = GetElbowBendDegrees(pose);
            return elbow >= -LimitToleranceDegrees
                && elbow <= ArmedDummyTuning.MaximumElbowBendDegrees + LimitToleranceDegrees
                && GetShoulderSwingDegrees(pose) <= ArmedDummyTuning.MaximumShoulderSwingDegrees + LimitToleranceDegrees
                && GetWristBendDegrees(pose) <= ArmedDummyTuning.MaximumWristBendDegrees + LimitToleranceDegrees;
        }

        private static float GetUpperArmMeters(float heightMeters)
        {
            return EnGardePose.UpperArmLength * heightMeters;
        }

        /// <summary>Elbow to point: forearm, hand to the guard, and the blade, in one line with the wrist straight.</summary>
        private static float GetForearmToPointMeters(float heightMeters, float bladeLengthMeters)
        {
            return ((EnGardePose.ForearmLength + EnGardePose.HandLength) * heightMeters) + bladeLengthMeters;
        }

        /// <summary>
        /// <paramref name="direction"/> if it is within <see cref="ArmedDummyTuning.MaximumAimOffLineDegrees"/> of the
        /// in-line direction; otherwise the direction at that limit, turned from the in-line direction toward it.
        /// </summary>
        private static Vector3 KeepNearInLine(Vector3 direction)
        {
            var inLine = EnGardePose.ArmInLineDirection;
            var limit = ArmedDummyTuning.MaximumAimOffLineDegrees * DegreesToRadians;
            var cosine = Vector3.Dot(direction, inLine);
            if (cosine >= Math.Cos(limit))
            {
                return direction;
            }

            var across = direction - (cosine * inLine);
            if (across.LengthSquared() < 1e-12f)
            {
                return inLine;
            }

            return Vector3.Normalize((inLine * (float)Math.Cos(limit)) + (Vector3.Normalize(across) * (float)Math.Sin(limit)));
        }

        /// <summary>The world up made square to <paramref name="direction"/>; for a vertical direction, the forward.</summary>
        private static Vector3 GetUpSquareTo(Vector3 direction)
        {
            var up = Vector3.UnitY - (Vector3.Dot(Vector3.UnitY, direction) * direction);
            if (up.LengthSquared() < 1e-8f)
            {
                up = Vector3.UnitZ - (Vector3.Dot(Vector3.UnitZ, direction) * direction);
            }

            return Vector3.Normalize(up);
        }
    }
}
