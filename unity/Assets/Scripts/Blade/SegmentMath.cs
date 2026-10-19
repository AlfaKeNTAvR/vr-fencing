// Segment geometry for swept blade tests. All positions are in one shared
// frame (world space in the app), all lengths in metres.
using UnityEngine;

namespace VrFencing.Blade
{
    public static class SegmentMath
    {
        private const float DegenerateLengthSquared = 1e-12f;

        /// <summary>
        /// Finds where a point moving in a straight line from <paramref name="start"/> to
        /// <paramref name="end"/> first enters a sphere. Returns false when the start is
        /// already inside (not a new entry), when the path misses, or when it would
        /// only enter after <paramref name="end"/>. Catches a path that crosses the
        /// whole sphere within one step, so a fast point cannot tunnel through.
        /// </summary>
        /// <param name="entryFraction">0 at start, 1 at end.</param>
        public static bool TryFindSphereEntry(Vector3 start, Vector3 end, Vector3 center, float radius,
            out float entryFraction)
        {
            entryFraction = 0f;
            var step = end - start;
            var centerToStart = start - center;
            var startOutsideAmount = centerToStart.sqrMagnitude - radius * radius;
            if (startOutsideAmount <= 0f)
                return false;

            var stepLengthSquared = step.sqrMagnitude;
            if (stepLengthSquared < DegenerateLengthSquared)
                return false;

            // Solve |centerToStart + t * step| = radius for the smaller root t.
            var halfB = Vector3.Dot(centerToStart, step);
            if (halfB >= 0f)
                return false; // Moving away from the centre.

            var discriminant = halfB * halfB - stepLengthSquared * startOutsideAmount;
            if (discriminant < 0f)
                return false;

            var fraction = (-halfB - Mathf.Sqrt(discriminant)) / stepLengthSquared;
            if (fraction > 1f)
                return false;

            entryFraction = fraction;
            return true;
        }

        public static float FindDistanceFromPointToSegment(Vector3 point, Vector3 segmentStart, Vector3 segmentEnd)
        {
            var segment = segmentEnd - segmentStart;
            var lengthSquared = segment.sqrMagnitude;
            if (lengthSquared < DegenerateLengthSquared)
                return Vector3.Distance(point, segmentStart);

            var fraction = Mathf.Clamp01(Vector3.Dot(point - segmentStart, segment) / lengthSquared);
            return Vector3.Distance(point, segmentStart + fraction * segment);
        }
    }
}
