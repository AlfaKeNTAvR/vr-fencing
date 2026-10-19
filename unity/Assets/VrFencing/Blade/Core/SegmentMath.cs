using System;
using System.Numerics;
using VrFencing.Dummy;

namespace VrFencing.Blade
{
    /// <summary>Segment and capsule geometry for the swept blade tests. All positions in one frame, all lengths in meters.</summary>
    public static class SegmentMath
    {
        private const float DegenerateLengthSquared = 1e-12f;

        // Bisection steps for the entry point: 2^-30 of a 20 cm frame step is far below a millimeter.
        private const int EntrySearchSteps = 30;

        /// <summary>The point on the segment closest to <paramref name="point"/>.</summary>
        public static Vector3 FindClosestPointOnSegment(Vector3 point, Vector3 segmentStart, Vector3 segmentEnd)
        {
            var segment = segmentEnd - segmentStart;
            var lengthSquared = segment.LengthSquared();
            if (lengthSquared < DegenerateLengthSquared)
            {
                return segmentStart;
            }

            var fraction = Clamp01(Vector3.Dot(point - segmentStart, segment) / lengthSquared);
            return segmentStart + (fraction * segment);
        }

        public static float FindDistanceFromPointToSegment(Vector3 point, Vector3 segmentStart, Vector3 segmentEnd)
        {
            return Vector3.Distance(point, FindClosestPointOnSegment(point, segmentStart, segmentEnd));
        }

        /// <summary>
        /// Closest points between segment P (p0 to p1) and segment Q (q0 to q1) (Ericson, Real-Time Collision Detection 5.1.9).
        /// Returns the distance; <paramref name="fractionOnP"/> is where the closest point lies on P (0 at p0, 1 at p1).
        /// </summary>
        public static float FindDistanceBetweenSegments(Vector3 p0, Vector3 p1, Vector3 q0, Vector3 q1, out float fractionOnP)
        {
            var directionP = p1 - p0;
            var directionQ = q1 - q0;
            var startOffset = p0 - q0;
            var lengthSquaredP = directionP.LengthSquared();
            var lengthSquaredQ = directionQ.LengthSquared();
            var projectionQ = Vector3.Dot(directionQ, startOffset);
            float s;
            float t;

            if (lengthSquaredP < DegenerateLengthSquared && lengthSquaredQ < DegenerateLengthSquared)
            {
                fractionOnP = 0.0f;
                return Vector3.Distance(p0, q0);
            }

            if (lengthSquaredP < DegenerateLengthSquared)
            {
                s = 0.0f;
                t = Clamp01(projectionQ / lengthSquaredQ);
            }
            else
            {
                var projectionP = Vector3.Dot(directionP, startOffset);
                if (lengthSquaredQ < DegenerateLengthSquared)
                {
                    t = 0.0f;
                    s = Clamp01(-projectionP / lengthSquaredP);
                }
                else
                {
                    var crossTerm = Vector3.Dot(directionP, directionQ);
                    var denominator = (lengthSquaredP * lengthSquaredQ) - (crossTerm * crossTerm);
                    s = denominator > DegenerateLengthSquared
                        ? Clamp01(((crossTerm * projectionQ) - (projectionP * lengthSquaredQ)) / denominator)
                        : 0.0f;
                    t = ((crossTerm * s) + projectionQ) / lengthSquaredQ;
                    if (t < 0.0f)
                    {
                        t = 0.0f;
                        s = Clamp01(-projectionP / lengthSquaredP);
                    }
                    else if (t > 1.0f)
                    {
                        t = 1.0f;
                        s = Clamp01((crossTerm - projectionP) / lengthSquaredP);
                    }
                }
            }

            fractionOnP = s;
            return Vector3.Distance(p0 + (s * directionP), q0 + (t * directionQ));
        }

        /// <summary>Distance from a point to a capsule's surface: negative inside.</summary>
        public static float FindDistanceToCapsule(Vector3 point, Capsule capsule)
        {
            return FindDistanceFromPointToSegment(point, capsule.Start, capsule.End) - capsule.RadiusMeters;
        }

        /// <summary>
        /// Where a point moving in a straight line from <paramref name="start"/> to <paramref name="end"/> first comes
        /// within <paramref name="extraRadiusMeters"/> of the capsule. False when the start is already that close (not a
        /// new entry) or the path never gets that close. The whole path is tested, so a fast point that crosses the capsule
        /// inside one step cannot tunnel through it.
        ///
        /// The distance from a point moving in a line to a segment is convex along the line, so it only falls until the
        /// closest approach: the entry is found by bisection between the start and that closest approach.
        /// </summary>
        /// <param name="entryFraction">0 at start, 1 at end.</param>
        public static bool TryFindCapsuleEntry(Vector3 start, Vector3 end, Capsule capsule, float extraRadiusMeters, out float entryFraction)
        {
            entryFraction = 0.0f;
            var reach = capsule.RadiusMeters + extraRadiusMeters;
            if (FindDistanceFromPointToSegment(start, capsule.Start, capsule.End) <= reach)
            {
                return false;
            }

            var closestDistance = FindDistanceBetweenSegments(start, end, capsule.Start, capsule.End, out var closestFraction);
            if (closestDistance > reach)
            {
                return false;
            }

            var outside = 0.0f;
            var inside = closestFraction;
            for (var step = 0; step < EntrySearchSteps; step++)
            {
                var middle = (outside + inside) * 0.5f;
                var point = Vector3.Lerp(start, end, middle);
                if (FindDistanceFromPointToSegment(point, capsule.Start, capsule.End) <= reach)
                {
                    inside = middle;
                }
                else
                {
                    outside = middle;
                }
            }

            entryFraction = inside;
            return true;
        }

        /// <summary>Unit vector from the capsule's axis out through <paramref name="point"/>; any perpendicular if the point is on the axis.</summary>
        public static Vector3 FindOutwardNormal(Vector3 point, Capsule capsule)
        {
            var offset = point - FindClosestPointOnSegment(point, capsule.Start, capsule.End);
            if (offset.LengthSquared() > DegenerateLengthSquared)
            {
                return Vector3.Normalize(offset);
            }

            var axis = capsule.End - capsule.Start;
            var perpendicular = Vector3.Cross(axis, Vector3.UnitX);
            if (perpendicular.LengthSquared() < DegenerateLengthSquared)
            {
                perpendicular = Vector3.Cross(axis, Vector3.UnitY);
            }

            return perpendicular.LengthSquared() < DegenerateLengthSquared ? Vector3.UnitY : Vector3.Normalize(perpendicular);
        }

        /// <summary>Angle between two vectors in degrees, 0 to 180; 180 if either is zero.</summary>
        public static float FindAngleDegrees(Vector3 from, Vector3 to)
        {
            var lengths = from.Length() * to.Length();
            if (lengths < 1e-9f)
            {
                return 180.0f;
            }

            var cosine = Math.Max(-1.0f, Math.Min(1.0f, Vector3.Dot(from, to) / lengths));
            return (float)(Math.Acos(cosine) * 180.0 / Math.PI);
        }

        /// <summary>The shortest rotation that turns unit vector <paramref name="from"/> onto unit vector <paramref name="to"/>; none if they are parallel.</summary>
        public static Quaternion FindRotationBetween(Vector3 from, Vector3 to)
        {
            var axis = Vector3.Cross(from, to);
            var sine = axis.Length();
            if (sine < 1e-7f)
            {
                return Quaternion.Identity;
            }

            return Quaternion.CreateFromAxisAngle(axis / sine, (float)Math.Atan2(sine, Vector3.Dot(from, to)));
        }

        private static float Clamp01(float value)
        {
            return value < 0.0f ? 0.0f : (value > 1.0f ? 1.0f : value);
        }
    }
}
