using System;
using System.Numerics;

namespace VrFencing.Dummy
{
    /// <summary>
    /// Poses the Armed Dummy's sword arm so the point of the blade in its hand lands on an aim point (PER-40): a
    /// two-link reach, with the upper arm as one link and the forearm, hand and blade (wrist straight) as the other.
    /// The elbow bends in the vertical plane through the shoulder and the aim, forearm up, as in a thrust from a
    /// slightly bent arm.
    ///
    /// Everything is in the right-handed Dummy frame (see <see cref="EnGardePose"/>), as <see cref="SwordArmPose"/>
    /// is: a left-handed Dummy mirrors the aim into it first, and <see cref="SwordArm"/> mirrors the pose back out.
    /// </summary>
    public static class SwordArmReach
    {
        /// <summary>
        /// How far the wind-up draws the point back from the in-line arm's full reach, along the in-line direction: a
        /// short, visible bend of the elbow before a thrust (PER-23: "a short visible wind-up").
        /// </summary>
        public const float WindUpDrawBackMeters = 0.12f;

        // Keeps the two links from folding flat onto each other at the shortest reach.
        private const float FoldMarginMeters = 0.001f;

        /// <summary>Shoulder to point with the arm and blade straight, for a Dummy of <paramref name="heightMeters"/>.</summary>
        public static float GetFullReachMeters(float heightMeters, float bladeLengthMeters)
        {
            return GetUpperArmMeters(heightMeters) + GetForearmToPointMeters(heightMeters, bladeLengthMeters);
        }

        /// <summary>The sword shoulder in the right-handed Dummy frame.</summary>
        public static Vector3 GetShoulderPosition(float heightMeters)
        {
            return EnGardePose.SwordShoulder * heightMeters;
        }

        /// <summary>
        /// The arm pose that puts the point of a blade of <paramref name="bladeLengthMeters"/> on
        /// <paramref name="aim"/>. An aim beyond the full reach gets the straight arm pointing at it, so the point
        /// falls short; one nearer than the arm can fold gets the most bent arm pointing at it.
        /// </summary>
        /// <param name="aim">Where the point should land, in the right-handed Dummy frame.</param>
        public static SwordArmPose PoseToReach(float heightMeters, Vector3 aim, float bladeLengthMeters)
        {
            DummyLayout.RequirePositiveHeight(heightMeters);
            var upperArm = GetUpperArmMeters(heightMeters);
            var forearmToPoint = GetForearmToPointMeters(heightMeters, bladeLengthMeters);
            var shoulderToAim = aim - GetShoulderPosition(heightMeters);
            var aimDistance = shoulderToAim.Length();
            if (aimDistance < 1e-6f)
            {
                return SwordArmPose.ArmInLine;
            }

            var towardAim = shoulderToAim / aimDistance;
            var reach = Math.Clamp(aimDistance, Math.Abs(forearmToPoint - upperArm) + FoldMarginMeters, upperArm + forearmToPoint);

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

            var shoulder = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(
                upperArmRight.X, upperArmRight.Y, upperArmRight.Z, 0.0f,
                upperArmUp.X, upperArmUp.Y, upperArmUp.Z, 0.0f,
                upperArmDirection.X, upperArmDirection.Y, upperArmDirection.Z, 0.0f,
                0.0f, 0.0f, 0.0f, 1.0f)));
            return new SwordArmPose(shoulder, Quaternion.CreateFromAxisAngle(-Vector3.UnitX, elbowBend), Quaternion.Identity);
        }

        /// <summary>
        /// The Armed Dummy's en garde between attacks (PER-95): the point toward the Fencer on the in-line direction but
        /// drawn back this far from full reach by a bent elbow. At extension distance the straight arm in line already
        /// reaches the Fencer's body, so a real en garde keeps the arm bent and the thrust straightens it.
        /// </summary>
        public const float GuardDrawBackMeters = 0.15f;

        /// <summary>The en garde between attacks: the point drawn back <see cref="GuardDrawBackMeters"/> along the in-line direction.</summary>
        public static SwordArmPose PoseEnGarde(float heightMeters, float bladeLengthMeters)
        {
            return PoseDrawnBack(heightMeters, bladeLengthMeters, GuardDrawBackMeters);
        }

        /// <summary>
        /// The wind-up before a thrust: the point drawn back a further <see cref="WindUpDrawBackMeters"/> from the en
        /// garde along the in-line direction, the elbow bending more to do it.
        /// </summary>
        public static SwordArmPose PoseWindUp(float heightMeters, float bladeLengthMeters)
        {
            return PoseDrawnBack(heightMeters, bladeLengthMeters, GuardDrawBackMeters + WindUpDrawBackMeters);
        }

        /// <summary>The point on the in-line direction, <paramref name="drawBackMeters"/> short of full reach.</summary>
        public static SwordArmPose PoseDrawnBack(float heightMeters, float bladeLengthMeters, float drawBackMeters)
        {
            var reach = GetFullReachMeters(heightMeters, bladeLengthMeters) - drawBackMeters;
            var aim = GetShoulderPosition(heightMeters) + (EnGardePose.ArmInLineDirection * reach);
            return PoseToReach(heightMeters, aim, bladeLengthMeters);
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
