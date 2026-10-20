using System;
using System.Numerics;

namespace VrFencing.Dummy
{
    /// <summary>Where a two-bone chain's middle joint and end land (see <see cref="TwoBoneIk.Solve"/>).</summary>
    public readonly struct TwoBoneSolution
    {
        public TwoBoneSolution(Vector3 middle, Vector3 end, bool isReached)
        {
            Middle = middle;
            End = end;
            IsReached = isReached;
        }

        /// <summary>The middle joint (a knee or an elbow).</summary>
        public Vector3 Middle { get; }

        /// <summary>The chain's end: the target, or the nearest point to it the chain can reach.</summary>
        public Vector3 End { get; }

        /// <summary>Whether <see cref="End"/> is on the target (false: the chain is fully stretched or folded toward it).</summary>
        public bool IsReached { get; }
    }

    /// <summary>
    /// Analytic two-bone IK (PER-110): a root joint (hip), two bones of fixed length (thigh, shin) and a target for the
    /// end (ankle). The middle joint (knee) lies in the plane through root, target and a pole direction, on the pole's
    /// side, where the law of cosines puts it. Plain math, no iteration, no allocation.
    /// </summary>
    public static class TwoBoneIk
    {
        // A chain is never solved fully straight or fully folded: there the bend direction is undefined and jitters.
        private const float ReachMarginFraction = 1e-4f;

        /// <param name="root">The fixed end of the chain (the hip).</param>
        /// <param name="target">Where the end (the ankle) should be.</param>
        /// <param name="upperLength">Root to middle joint (thigh).</param>
        /// <param name="lowerLength">Middle joint to end (shin).</param>
        /// <param name="pole">Which way the middle joint bends (a knee: forward over the toes); need not be unit or perpendicular.</param>
        /// <exception cref="ArgumentOutOfRangeException">A bone length is not a positive number.</exception>
        public static TwoBoneSolution Solve(Vector3 root, Vector3 target, float upperLength, float lowerLength, Vector3 pole)
        {
            if (!(upperLength > 0.0f) || !(lowerLength > 0.0f) || float.IsInfinity(upperLength) || float.IsInfinity(lowerLength))
            {
                throw new ArgumentOutOfRangeException(nameof(upperLength), "Bone lengths must be positive numbers.");
            }

            var toTarget = target - root;
            var targetDistance = toTarget.Length();
            var longest = (upperLength + lowerLength) * (1.0f - ReachMarginFraction);
            var shortest = Math.Abs(upperLength - lowerLength) + ((upperLength + lowerLength) * ReachMarginFraction);
            var distance = Math.Min(Math.Max(targetDistance, shortest), longest);
            var isReached = targetDistance <= longest && targetDistance >= shortest;

            var along = targetDistance > 1e-6f ? toTarget / targetDistance : -Vector3.UnitY;
            var bend = GetBendDirection(along, pole);

            // Law of cosines: the middle joint is 'alongLength' along the root-to-end line and 'outLength' off it.
            var alongLength = ((upperLength * upperLength) - (lowerLength * lowerLength) + (distance * distance)) / (2.0f * distance);
            var outLength = (float)Math.Sqrt(Math.Max(0.0f, (upperLength * upperLength) - (alongLength * alongLength)));
            var middle = root + (along * alongLength) + (bend * outLength);
            var end = isReached ? target : root + (along * distance);
            return new TwoBoneSolution(middle, end, isReached);
        }

        /// <summary>The part of <paramref name="pole"/> across <paramref name="along"/>, as a unit vector; any perpendicular if it has none.</summary>
        private static Vector3 GetBendDirection(Vector3 along, Vector3 pole)
        {
            var across = pole - (along * Vector3.Dot(pole, along));
            if (across.LengthSquared() > 1e-10f)
            {
                return Vector3.Normalize(across);
            }

            var fallback = Math.Abs(along.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX;
            return Vector3.Normalize(fallback - (along * Vector3.Dot(fallback, along)));
        }
    }
}
