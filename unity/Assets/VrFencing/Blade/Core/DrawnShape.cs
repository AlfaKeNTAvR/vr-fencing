using System;
using System.Collections.Generic;
using System.Numerics;

namespace VrFencing.Blade
{
    /// <summary>
    /// The rule shared by the pinned and the free drawn blade (PER-48, PER-49): a drawn shape is only taken when it is
    /// outside every obstacle and got there without passing through one. When the shape the hand asks for is not
    /// allowed (bent past the most it can bend, or through a body), the drawn blade takes the allowed pose closest to
    /// the hand on the way to a fallback pose, or else holds where it is: it lags the hand, and a ghost hilt shows the
    /// hand. Positions in any one frame, meters.
    /// </summary>
    public static class DrawnShape
    {
        /// <summary>A drawn hilt this far (or turned this far) from the hand's counts as lagging it.</summary>
        public const float LagMeters = 0.002f;

        /// <summary>See <see cref="LagMeters"/>.</summary>
        public const float LagDegrees = 1.0f;

        // Bisection steps between the hand's pose and the fallback: 2^-11 of the way, under half a millimeter for the
        // longest lags (each step builds a whole shape, so fewer keeps a lagging frame cheap on the Quest).
        private const int SearchSteps = 11;

        /// <summary>Tests one pose: builds its shape and says whether it is allowed.</summary>
        public delegate bool PoseTest(BladeSegment pose);

        /// <summary>
        /// The fraction of the way from <paramref name="hand"/> to <paramref name="fallback"/> of the allowed pose closest to
        /// the hand: 0 when the hand's own pose is allowed, null when not even the fallback is. Assumes that once allowed
        /// on the way, the rest of the way is too (true for the bend limit; near enough for bodies). The test is left
        /// run on the returned pose, so the caller's last built shape is that pose's.
        /// </summary>
        public static float? FindClosestAllowed(BladeSegment hand, BladeSegment fallback, PoseTest isAllowed)
        {
            if (isAllowed(hand))
            {
                return 0.0f;
            }

            if (!isAllowed(fallback))
            {
                return null;
            }

            var low = 0.0f;
            var high = 1.0f;
            for (var step = 0; step < SearchSteps; step++)
            {
                var middle = (low + high) * 0.5f;
                if (isAllowed(Interpolate(hand, fallback, middle)))
                {
                    high = middle;
                }
                else
                {
                    low = middle;
                }
            }

            isAllowed(Interpolate(hand, fallback, high));
            return high;
        }

        /// <summary>The guard moved in a straight line and the direction turned at an even rate, full blade length.</summary>
        public static BladeSegment Interpolate(BladeSegment from, BladeSegment to, float fraction)
        {
            var guard = Vector3.Lerp(from.GuardPosition, to.GuardPosition, fraction);
            var direction = TurnToward(from.Direction, to.Direction, fraction);
            return new BladeSegment(guard, guard + (direction * BladePose.BladeLengthMeters));
        }

        /// <summary>
        /// True when every sample from <paramref name="firstIndex"/> on is at least <paramref name="marginMeters"/> out of
        /// every obstacle and, given <paramref name="previous"/>, moved there without entering one (the last sample, the
        /// point, only when <paramref name="sweepsTip"/>).
        /// </summary>
        public static bool IsClear(Vector3[] samples, Vector3[] previous, IReadOnlyList<BladeObstacle> obstacles, float marginMeters, int firstIndex, bool sweepsTip)
        {
            for (var index = firstIndex; index < samples.Length; index++)
            {
                foreach (var obstacle in obstacles)
                {
                    if (obstacle.FindDistance(samples[index]) < marginMeters - 1e-5f)
                    {
                        return false;
                    }

                    var isSwept = previous != null && (sweepsTip || index < samples.Length - 1);
                    if (isSwept && obstacle.TryFindEntry(previous[index], samples[index], marginMeters * 0.5f, out _))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>The largest angle, in degrees, between two neighbouring pieces of a drawn centerline: how sharp its sharpest kink is.</summary>
        public static float FindSharpestTurnDegrees(IReadOnlyList<Vector3> samples)
        {
            var sharpest = 0.0f;
            for (var index = 1; index < samples.Count - 1; index++)
            {
                var before = samples[index] - samples[index - 1];
                var after = samples[index + 1] - samples[index];
                if (before.LengthSquared() > 1e-12f && after.LengthSquared() > 1e-12f)
                {
                    sharpest = Math.Max(sharpest, SegmentMath.FindAngleDegrees(before, after));
                }
            }

            return sharpest;
        }

        /// <summary>True when the drawn hilt at <paramref name="drawnGuard"/> along <paramref name="drawnDirection"/> lags the hand's.</summary>
        public static bool IsOffHand(Vector3 drawnGuard, Vector3 drawnDirection, BladeSegment hand)
        {
            return Vector3.Distance(drawnGuard, hand.GuardPosition) > LagMeters
                || SegmentMath.FindAngleDegrees(drawnDirection, hand.Direction) > LagDegrees;
        }

        private static Vector3 TurnToward(Vector3 from, Vector3 to, float fraction)
        {
            var cosine = Math.Max(-1.0f, Math.Min(1.0f, Vector3.Dot(from, to)));
            var angle = (float)Math.Acos(cosine);
            if (angle < 1e-5f)
            {
                return Vector3.Normalize(Vector3.Lerp(from, to, fraction));
            }

            var sine = (float)Math.Sin(angle);
            if (sine < 1e-4f)
            {
                // Opposite directions: no defined way round; step through a lerp (a jump, but never NaN).
                var lerped = Vector3.Lerp(from, to, fraction);
                return lerped.LengthSquared() > 1e-10f ? Vector3.Normalize(lerped) : from;
            }

            var weightFrom = (float)Math.Sin((1.0f - fraction) * angle) / sine;
            var weightTo = (float)Math.Sin(fraction * angle) / sine;
            return Vector3.Normalize((from * weightFrom) + (to * weightTo));
        }
    }
}
