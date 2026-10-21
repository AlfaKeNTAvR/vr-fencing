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
        /// <summary>
        /// A drawn hilt this far (or turned this far) from the hand's counts as lagging it: the logic's threshold (the
        /// drawn blade stops following and the leash's clock runs), kept tight so a held blade is never taken as
        /// following. What shows (the ghost hilt) and what is felt use their own, larger thresholds on
        /// <see cref="FindLagMeters"/> (PER-121).
        /// </summary>
        public const float LagMeters = 0.002f;

        /// <summary>See <see cref="LagMeters"/>.</summary>
        public const float LagDegrees = 1.0f;

        /// <summary>
        /// A turn of the drawn hilt off the hand's counts as a lag of the turn (in radians) times this, so one distance
        /// stands for both (PER-121): about where the forte leaves the bell, a third of the blade. 8 cm of lag is then
        /// a 15 degree turn, 3 cm a 6 degree one.
        /// </summary>
        public const float TurnLeverMeters = 0.3f;

        // Bisection steps between the hand's pose and the fallback: 2^-11 of the way, under half a millimeter for the
        // longest lags (each step builds a whole shape, so fewer keeps a lagging frame cheap on the Quest).
        private const int SearchSteps = 11;

        // A drawn shape is checked against another blade on its way from the last one every this far of its largest
        // move, at most this many times: about half the strip behind that blade, so a jump cannot hop it.
        private const float BladeStepMeters = 0.02f;
        private const int MaxBladeSteps = 4;

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
        /// point, only when <paramref name="sweepsTip"/>). Another blade (<see cref="BladeObstacle.IsBlade"/>) is thin and
        /// moves: it is tested against every piece between the samples (from the one ending at
        /// <paramref name="firstIndex"/>), and kept to its side where they lie across each other, instead of swept.
        /// </summary>
        public static bool IsClear(Vector3[] samples, Vector3[] previous, IReadOnlyList<BladeObstacle> obstacles, float marginMeters, int firstIndex, bool sweepsTip)
        {
            // Bodies nowhere near the samples (and their paths) are skipped: the same answer, cheaper.
            var boxMin = samples[firstIndex];
            var boxMax = boxMin;
            for (var index = firstIndex; index < samples.Length; index++)
            {
                boxMin = Vector3.Min(boxMin, samples[index]);
                boxMax = Vector3.Max(boxMax, samples[index]);
                if (previous != null)
                {
                    boxMin = Vector3.Min(boxMin, previous[index]);
                    boxMax = Vector3.Max(boxMax, previous[index]);
                }
            }

            foreach (var obstacle in obstacles)
            {
                if (obstacle.IsBlade)
                {
                    if (!IsClearOfBlade(samples, previous, obstacle, marginMeters, Math.Max(0, firstIndex - 1)))
                    {
                        return false;
                    }

                    continue;
                }

                if (!obstacle.IsNearBox(boxMin, boxMax, marginMeters))
                {
                    continue;
                }

                for (var index = firstIndex; index < samples.Length; index++)
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

        /// <summary>
        /// Clear of another blade's piece: the shape itself, and, given <paramref name="previous"/>, the shapes on the way
        /// there every <see cref="BladeStepMeters"/> of the largest move (at most <see cref="MaxBladeSteps"/>), so a jump
        /// of the drawn blade (catching up with the hand) cannot hop over to the far side of it.
        /// </summary>
        private static bool IsClearOfBlade(Vector3[] samples, Vector3[] previous, BladeObstacle blade, float marginMeters, int firstPiece)
        {
            var steps = 1;
            if (previous != null)
            {
                var largestMove = 0.0f;
                for (var index = firstPiece; index < samples.Length; index++)
                {
                    largestMove = Math.Max(largestMove, Vector3.DistanceSquared(samples[index], previous[index]));
                }

                steps = Math.Min(MaxBladeSteps, Math.Max(1, (int)Math.Ceiling(Math.Sqrt(largestMove) / BladeStepMeters)));
            }

            var lastPiece = samples.Length - 2;
            for (var step = steps; step >= 1; step--)
            {
                var fraction = (float)step / steps;
                for (var piece = firstPiece; piece <= lastPiece; piece++)
                {
                    var joints = (piece > 0 ? 1 : 0) | (piece < lastPiece ? 2 : 0);
                    var start = step == steps ? samples[piece] : Vector3.Lerp(previous[piece], samples[piece], fraction);
                    var end = step == steps ? samples[piece + 1] : Vector3.Lerp(previous[piece + 1], samples[piece + 1], fraction);
                    if (!blade.IsPieceClear(start, end, joints, marginMeters))
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

        /// <summary>
        /// How far the drawn hilt at <paramref name="drawnGuard"/> along <paramref name="drawnDirection"/> trails the
        /// hand's: the guards' distance or the turn between them as a distance (<see cref="TurnLeverMeters"/>), whichever
        /// is more.
        /// </summary>
        public static float FindLagMeters(Vector3 drawnGuard, Vector3 drawnDirection, BladeSegment hand)
        {
            var turnRadians = SegmentMath.FindAngleDegrees(drawnDirection, hand.Direction) * (float)(Math.PI / 180.0);
            return Math.Max(Vector3.Distance(drawnGuard, hand.GuardPosition), turnRadians * TurnLeverMeters);
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
