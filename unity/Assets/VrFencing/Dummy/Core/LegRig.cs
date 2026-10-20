using System;
using System.Numerics;
using VrFencing.Settings;

namespace VrFencing.Dummy
{
    /// <summary>
    /// One leg of a right-handed Dummy in meters (PER-110, PER-111): its en garde joints, bone lengths and knee
    /// direction, and how it is posed for hips that moved and a foot that is moved, lifted and pitched. The knee comes
    /// from <see cref="TwoBoneIk"/>, bending where it bends in en garde, so the rest pose solves back to itself.
    /// </summary>
    public sealed class LegRig
    {
        private readonly Vector3 _kneeDirection;
        private readonly Vector3 _pitchAxis;

        public LegRig(LegPose restAtUnitHeight, float heightMeters)
        {
            Rest = restAtUnitHeight.ScaledForHand(heightMeters, Hand.Right);
            ThighMeters = Vector3.Distance(Rest.Hip, Rest.Knee);
            ShinMeters = Vector3.Distance(Rest.Knee, Rest.Ankle);

            var hipToAnkle = Vector3.Normalize(Rest.Ankle - Rest.Hip);
            var hipToKnee = Rest.Knee - Rest.Hip;
            _kneeDirection = Vector3.Normalize(hipToKnee - (hipToAnkle * Vector3.Dot(hipToKnee, hipToAnkle)));

            // Toe up is a turn about the foot's sideways axis (heel-to-toe crossed with up).
            var heelToToe = Rest.Toe - Rest.Heel;
            _pitchAxis = Vector3.Normalize(Vector3.Cross(new Vector3(heelToToe.X, 0.0f, heelToToe.Z), Vector3.UnitY));
        }

        /// <summary>The en garde leg, right-handed, in meters.</summary>
        public LegPose Rest { get; }

        public float ThighMeters { get; }

        public float ShinMeters { get; }

        /// <summary>Hip to ankle with the knee straight.</summary>
        public float LengthMeters => ThighMeters + ShinMeters;

        /// <summary>
        /// The leg with the hip moved by <paramref name="hipOffset"/> and the ankle by <paramref name="ankleOffset"/>
        /// (x sideways, y up, z forward along the Dummy's +z), the foot turned by <paramref name="pitchRadians"/> about
        /// its sideways axis (positive lifts the toe). A pitched foot is raised so its lowest end is no lower than a flat
        /// one's: it never dips into the floor.
        /// </summary>
        public LegPose Pose(Vector3 hipOffset, Vector3 ankleOffset, float pitchRadians)
        {
            var pitch = Quaternion.CreateFromAxisAngle(_pitchAxis, pitchRadians);
            var heelFromAnkle = Vector3.Transform(Rest.Heel - Rest.Ankle, pitch);
            var toeFromAnkle = Vector3.Transform(Rest.Toe - Rest.Ankle, pitch);

            var restLowest = Math.Min(Rest.Heel.Y, Rest.Toe.Y) - Rest.Ankle.Y;
            var pitchedLowest = Math.Min(heelFromAnkle.Y, toeFromAnkle.Y);
            var raise = Math.Max(0.0f, restLowest - pitchedLowest);

            var hip = Rest.Hip + hipOffset;
            var ankleTarget = Rest.Ankle + ankleOffset + new Vector3(0.0f, raise, 0.0f);
            var solution = TwoBoneIk.Solve(hip, ankleTarget, ThighMeters, ShinMeters, _kneeDirection);
            return new LegPose(hip, solution.Middle, solution.End, solution.End + heelFromAnkle, solution.End + toeFromAnkle);
        }

        /// <summary>
        /// How far forward (+z) of the hip, at most, a foot on the floor can be planted for the hip moved by
        /// <paramref name="hipOffset"/>, with the leg at <paramref name="reachFraction"/> of its length; negative values
        /// are behind the hip. <paramref name="ankleSideMeters"/> is the ankle's sideways offset from its rest place.
        /// </summary>
        public float GetFloorReachMeters(Vector3 hipOffset, float reachFraction, float ankleSideMeters)
        {
            var hip = Rest.Hip + hipOffset;
            var reach = LengthMeters * reachFraction;
            var down = hip.Y - Rest.Ankle.Y;
            var side = (Rest.Ankle.X + ankleSideMeters) - hip.X;
            return (float)Math.Sqrt(Math.Max(0.0f, (reach * reach) - (down * down) - (side * side)));
        }
    }
}
