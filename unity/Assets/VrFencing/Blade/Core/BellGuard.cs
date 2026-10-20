using System;
using System.Numerics;

namespace VrFencing.Blade
{
    /// <summary>
    /// An épée's bell guard as a solid (PER-120): a flat round cylinder <see cref="RadiusMeters"/> across its middle and
    /// <see cref="DepthMeters"/> deep, its front face on the guard and its axis along the blade, as
    /// <c>BladeVisual</c> draws it. The Armed Dummy's bell is an obstacle the Fencer's drawn blade cannot pass
    /// (<see cref="BladeObstacle.FromBell"/>), so a press near its guard meets the bell, and the hand behind it stays
    /// covered. Positions in any one frame (the Strip frame in the app), meters.
    /// </summary>
    public static class BellGuard
    {
        /// <summary>FIE m.17.1: at most 13.5 cm across.</summary>
        public const float RadiusMeters = BladePose.GuardDiameterMeters * 0.5f;

        /// <summary>FIE m.17.1: 3 to 5.5 cm deep.</summary>
        public const float DepthMeters = BladePose.GuardDepthMeters;

        private const float HalfDepthMeters = DepthMeters * 0.5f;

        private const float DegenerateLengthSquared = 1e-12f;

        /// <summary>The bell's center for a guard at <paramref name="guard"/> with the blade along <paramref name="bladeDirection"/> (unit).</summary>
        public static Vector3 FindCenter(Vector3 guard, Vector3 bladeDirection)
        {
            return guard - (bladeDirection * HalfDepthMeters);
        }

        /// <summary>
        /// The signed distance from <paramref name="point"/> to the bell's surface (negative inside) and the unit vector
        /// out of the surface nearest it: off a face along the axis, off the rim straight out, off the edge from it.
        /// </summary>
        /// <param name="center">The bell's center (<see cref="FindCenter"/>).</param>
        /// <param name="axis">The blade's direction through it, unit: the front face is on that side.</param>
        public static float FindDistance(Vector3 point, Vector3 center, Vector3 axis, out Vector3 outward)
        {
            var offset = point - center;
            var along = Vector3.Dot(offset, axis);
            var radial = offset - (along * axis);
            var radius = radial.Length();
            var radialDirection = radius * radius > DegenerateLengthSquared ? radial / radius : FindAnySquare(axis);
            var faceNormal = along >= 0.0f ? axis : -axis;
            var outOfRim = radius - RadiusMeters;
            var outOfFace = Math.Abs(along) - HalfDepthMeters;
            if (outOfRim > 0.0f && outOfFace > 0.0f)
            {
                // Past the edge: straight from it.
                var edge = center + (radialDirection * RadiusMeters) + (faceNormal * HalfDepthMeters);
                var fromEdge = point - edge;
                var distance = fromEdge.Length();
                outward = fromEdge / distance;
                return distance;
            }

            outward = outOfRim > outOfFace ? radialDirection : faceNormal;
            return Math.Max(outOfRim, outOfFace);
        }

        /// <summary>
        /// Where a point moving in a straight line from <paramref name="start"/> to <paramref name="end"/> first comes
        /// within <paramref name="marginMeters"/> of the bell (its edges taken as square, a little generous): false when the
        /// start is already that close or the path never gets that close.
        /// </summary>
        /// <param name="entryFraction">0 at start, 1 at end.</param>
        public static bool TryFindEntry(Vector3 start, Vector3 end, Vector3 center, Vector3 axis, float marginMeters, out float entryFraction)
        {
            entryFraction = 0.0f;
            if (FindDistance(start, center, axis, out _) <= marginMeters)
            {
                return false;
            }

            var low = 0.0f;
            var high = 1.0f;
            var offset = start - center;
            var step = end - start;
            var halfDepth = HalfDepthMeters + marginMeters;
            if (!BladeCrossing.ClipToSlab(Vector3.Dot(offset, axis), Vector3.Dot(step, axis), -halfDepth, halfDepth, ref low, ref high))
            {
                return false;
            }

            // Inside the round wall: |radial(t)| at most the radius, a quadratic in t.
            var radialStart = offset - (Vector3.Dot(offset, axis) * axis);
            var radialStep = step - (Vector3.Dot(step, axis) * axis);
            var radius = RadiusMeters + marginMeters;
            var a = radialStep.LengthSquared();
            var b = Vector3.Dot(radialStart, radialStep);
            var c = radialStart.LengthSquared() - (radius * radius);
            if (a < DegenerateLengthSquared)
            {
                if (c > 0.0f)
                {
                    return false;
                }
            }
            else
            {
                var discriminant = (b * b) - (a * c);
                if (discriminant < 0.0f)
                {
                    return false;
                }

                var root = (float)Math.Sqrt(discriminant);
                low = Math.Max(low, (-b - root) / a);
                high = Math.Min(high, (-b + root) / a);
            }

            if (low > high)
            {
                return false;
            }

            entryFraction = low;
            return true;
        }

        /// <summary>Any unit vector square to <paramref name="axis"/>.</summary>
        private static Vector3 FindAnySquare(Vector3 axis)
        {
            var helper = Math.Abs(axis.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
            return Vector3.Normalize(Vector3.Cross(axis, helper));
        }
    }
}
