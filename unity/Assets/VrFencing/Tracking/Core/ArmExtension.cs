using System;
using System.Numerics;

namespace VrFencing.Tracking
{
    /// <summary>
    /// Whether the blade arm is extended: straight at the elbow and reaching toward the Dummy. In épée the arm
    /// extends first and the lunge follows, so this is checked at the moment the lunge starts.
    /// <para>
    /// Positions are in the Strip frame (+x the Fencer's right, +y up, +z toward the Dummy). The rule only uses
    /// the elbow angle and forward (+z) distances, which a left-hand mirror leaves unchanged, so it holds for
    /// either hand without mirroring.
    /// </para>
    /// </summary>
    public static class ArmExtension
    {
        /// <summary>
        /// Elbow angle (between the upper arm and the forearm) from which the arm counts as straight. 180 is fully
        /// straight; en garde holds the elbow at roughly 90 to 120, and a fencer's "straight" arm is rarely
        /// locked, so 150 leaves room for a relaxed extension and for IOBT noise. Tune on the headset.
        /// </summary>
        public const float MinExtendedElbowAngleDegrees = 150.0f;

        /// <summary>
        /// How far forward of the shoulder the wrist must be, as a fraction of the arm's length (upper arm plus
        /// forearm, measured from the same sample, so it scales with the Fencer). A straight arm pointing at the
        /// Dummy's chest reaches about 0.9; 0.6 still accepts a point aimed low or high, and rejects a straight
        /// arm hanging down or held out to the side.
        /// </summary>
        public const float MinWristForwardFractionOfArmLength = 0.6f;

        /// <summary>Elbow angle in degrees: 180 for a straight arm, smaller as it bends. 0 when a segment has no length.</summary>
        public static float ElbowAngleDegrees(Vector3 shoulder, Vector3 elbow, Vector3 wrist)
        {
            var toShoulder = shoulder - elbow;
            var toWrist = wrist - elbow;
            var lengths = toShoulder.Length() * toWrist.Length();
            if (lengths < 1e-8f)
            {
                return 0.0f;
            }

            var cosine = Math.Max(-1.0f, Math.Min(1.0f, Vector3.Dot(toShoulder, toWrist) / lengths));
            return (float)(Math.Acos(cosine) * 180.0 / Math.PI);
        }

        /// <summary>
        /// True when the elbow is at least <see cref="MinExtendedElbowAngleDegrees"/> straight and the wrist is at
        /// least <see cref="MinWristForwardFractionOfArmLength"/> of the arm's length forward (+z) of the shoulder.
        /// </summary>
        public static bool IsExtended(Vector3 shoulder, Vector3 elbow, Vector3 wrist)
        {
            if (ElbowAngleDegrees(shoulder, elbow, wrist) < MinExtendedElbowAngleDegrees)
            {
                return false;
            }

            var armLength = Vector3.Distance(shoulder, elbow) + Vector3.Distance(elbow, wrist);
            var wristForward = wrist.Z - shoulder.Z;
            return wristForward >= MinWristForwardFractionOfArmLength * armLength;
        }
    }
}
