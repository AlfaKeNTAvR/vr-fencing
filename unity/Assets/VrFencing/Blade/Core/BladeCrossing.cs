using System;
using System.Numerics;

namespace VrFencing.Blade
{
    /// <summary>
    /// Two thin straight pieces of blade (PER-104): where they come closest, whether they lie across each other there
    /// (the spot where their lines pass closest lies inside both pieces), and how far one reaches into the strip behind
    /// the other (<see cref="TryFindSideAtCrossing"/>), where it can only be by passing through it. Past an end, one can go
    /// round the other's end freely. A piece end that joins the next piece of the same blade (a joint, not a blade end)
    /// counts a little past its end as inside, so a crossing right at a joint is not lost between two pieces. Positions
    /// in any one frame, meters.
    /// </summary>
    public static class BladeCrossing
    {
        /// <summary>How far past a joint, as a share of the piece, a crossing still counts as inside it.</summary>
        public const float JointSlack = 0.25f;

        /// <summary>The strip behind a piece runs this share of the piece past a joint, so a crossing at a joint is not lost.</summary>
        public const float StripJointSlack = 0.05f;

        // Below this squared sine of the angle between the pieces they are taken as parallel.
        private const float ParallelSineSquared = 1e-4f;

        private const float DegenerateLengthSquared = 1e-12f;

        /// <summary>
        /// A piece whose direction leans less than this sine (about 6 degrees) across the other's side direction runs
        /// alongside it: it has no one crossing (<see cref="TryFindSideAtCrossing"/>).
        /// </summary>
        public const float AlongsideSine = 0.1f;

        /// <summary>
        /// The closest points of piece A (<paramref name="a0"/> to <paramref name="a1"/>) and piece B. When they lie across
        /// each other (returns true), these are the points where their lines pass closest, so <c>onA - onB</c> points
        /// square to both from B to A and its sign along a fixed side says which side of B the piece A is on. Otherwise
        /// they are the segments' closest points (an end of one, at least).
        /// </summary>
        /// <param name="aJoints">Whether A's start and end join more of its blade (bit 0 the start, bit 1 the end).</param>
        /// <param name="bJoints">The same for B.</param>
        /// <param name="fractionOnA">Where on A the closest point is, 0 at its start to 1 at its end (clamped).</param>
        /// <param name="fractionOnB">The same on B.</param>
        public static bool FindClosest(
            Vector3 a0,
            Vector3 a1,
            int aJoints,
            Vector3 b0,
            Vector3 b1,
            int bJoints,
            out Vector3 onA,
            out Vector3 onB,
            out float fractionOnA,
            out float fractionOnB)
        {
            var directionA = a1 - a0;
            var directionB = b1 - b0;
            var offset = a0 - b0;
            var lengthSquaredA = directionA.LengthSquared();
            var lengthSquaredB = directionB.LengthSquared();
            if (lengthSquaredA < DegenerateLengthSquared || lengthSquaredB < DegenerateLengthSquared)
            {
                return FindSegmentClosest(a0, directionA, b0, directionB, out onA, out onB, out fractionOnA, out fractionOnB);
            }

            var cross = Vector3.Dot(directionA, directionB);
            var projectionA = Vector3.Dot(directionA, offset);
            var projectionB = Vector3.Dot(directionB, offset);
            var denominator = (lengthSquaredA * lengthSquaredB) - (cross * cross);
            if (denominator <= ParallelSineSquared * lengthSquaredA * lengthSquaredB)
            {
                return FindParallelOverlap(a0, directionA, lengthSquaredA, b0, directionB, lengthSquaredB, out onA, out onB, out fractionOnA, out fractionOnB);
            }

            var s = ((cross * projectionB) - (projectionA * lengthSquaredB)) / denominator;
            var t = ((lengthSquaredA * projectionB) - (cross * projectionA)) / denominator;
            if (IsInside(s, aJoints) && IsInside(t, bJoints))
            {
                onA = a0 + (directionA * s);
                onB = b0 + (directionB * t);
                fractionOnA = Clamp01(s);
                fractionOnB = Clamp01(t);
                return true;
            }

            return FindSegmentClosest(a0, directionA, b0, directionB, out onA, out onB, out fractionOnA, out fractionOnB);
        }

        /// <summary>
        /// Whether piece A reaches into the strip behind piece B: along B within its length, within
        /// <paramref name="halfWidth"/> of B's axis across the side direction, on any depth of <paramref name="side"/>.
        /// That strip is where A can only be by having passed through B, if it was on <paramref name="side"/> of it when
        /// they met. Returns the side coordinate (from B's axis along <paramref name="side"/>) of A where it crosses B's
        /// axis across the side direction, the point there and its fraction along A; false when A misses the strip or
        /// crosses B's axis in the next piece of its blade.
        /// <para>Taken at the crossing, not as the least anywhere in the strip (PER-120). A blade lying against B at a
        /// slant (bent round it, or pressed in at an angle) dips below B's axis level toward the strip's edges while it is
        /// still clear of B, and that must not read as through it: the push out of it was divided by a short lever near
        /// the Dummy's guard and kicked its point 7 to 12 cm a frame. At the crossing the side coordinate runs on
        /// smoothly into the distance apart, so a blade just touching and one just past B's axis need about the same
        /// push. A piece whose crossing is past a joint leaves it to the next piece; a blade end (the point or the guard)
        /// short of the crossing is taken where it ends, so a point that went through still counts. A piece running
        /// alongside B (within <see cref="AlongsideSine"/>) has no clear crossing: its deepest part in the strip.</para>
        /// </summary>
        /// <param name="aJoints">Whether A's start (bit 0) and end (bit 1) join more of its blade.</param>
        /// <param name="side">Unit vector square to B: the side A belongs on.</param>
        /// <param name="bJoints">Whether B's start (bit 0) and end (bit 1) join more of its blade: the strip runs a little past a joint.</param>
        public static bool TryFindSideAtCrossing(
            Vector3 a0,
            Vector3 a1,
            int aJoints,
            Vector3 b0,
            Vector3 b1,
            int bJoints,
            Vector3 side,
            float halfWidth,
            out float sideAtCrossing,
            out Vector3 at,
            out float fractionOnA)
        {
            sideAtCrossing = float.MaxValue;
            at = a0;
            fractionOnA = 0.0f;
            var axis = b1 - b0;
            var length = axis.Length();
            if (length < 1e-6f)
            {
                return false;
            }

            axis /= length;
            var across = Vector3.Cross(axis, side);
            var startOffset = a0 - b0;
            var step = a1 - a0;
            var low = 0.0f;
            var high = 1.0f;
            var alongLow = (bJoints & 1) != 0 ? -StripJointSlack * length : 0.0f;
            var alongHigh = (bJoints & 2) != 0 ? length * (1.0f + StripJointSlack) : length;
            var acrossStart = Vector3.Dot(startOffset, across);
            var acrossStep = Vector3.Dot(step, across);
            if (!ClipToSlab(Vector3.Dot(startOffset, axis), Vector3.Dot(step, axis), alongLow, alongHigh, ref low, ref high)
                || !ClipToSlab(acrossStart, acrossStep, -halfWidth, halfWidth, ref low, ref high))
            {
                return false;
            }

            var sideStart = Vector3.Dot(startOffset, side);
            var sideStep = Vector3.Dot(step, side);
            if (Math.Abs(acrossStep) <= AlongsideSine * step.Length())
            {
                fractionOnA = sideStep >= 0.0f ? low : high;
            }
            else
            {
                var crossing = -acrossStart / acrossStep;
                if ((crossing > 1.0f && (aJoints & 2) != 0) || (crossing < 0.0f && (aJoints & 1) != 0))
                {
                    return false;
                }

                fractionOnA = Math.Clamp(crossing, low, high);
            }

            sideAtCrossing = sideStart + (sideStep * fractionOnA);
            at = a0 + (step * fractionOnA);
            return true;
        }

        /// <summary>Narrows [low, high] (fractions along a line value(t) = start + t * step) to where value is within [min, max].</summary>
        internal static bool ClipToSlab(float start, float step, float min, float max, ref float low, ref float high)
        {
            if (Math.Abs(step) < 1e-9f)
            {
                return start >= min && start <= max && low <= high;
            }

            var first = (min - start) / step;
            var second = (max - start) / step;
            low = Math.Max(low, Math.Min(first, second));
            high = Math.Min(high, Math.Max(first, second));
            return low <= high;
        }

        private static bool IsInside(float fraction, int joints)
        {
            var low = (joints & 1) != 0 ? -JointSlack : 0.0f;
            var high = (joints & 2) != 0 ? 1.0f + JointSlack : 1.0f;
            return fraction >= low && fraction <= high;
        }

        /// <summary>Nearly parallel pieces lie alongside each other where their lengths overlap: the middle of that overlap.</summary>
        private static bool FindParallelOverlap(
            Vector3 a0,
            Vector3 directionA,
            float lengthSquaredA,
            Vector3 b0,
            Vector3 directionB,
            float lengthSquaredB,
            out Vector3 onA,
            out Vector3 onB,
            out float fractionOnA,
            out float fractionOnB)
        {
            var start = Vector3.Dot(b0 - a0, directionA) / lengthSquaredA;
            var end = Vector3.Dot(b0 + directionB - a0, directionA) / lengthSquaredA;
            var low = Math.Max(0.0f, Math.Min(start, end));
            var high = Math.Min(1.0f, Math.Max(start, end));
            if (low > high)
            {
                return FindSegmentClosest(a0, directionA, b0, directionB, out onA, out onB, out fractionOnA, out fractionOnB);
            }

            fractionOnA = (low + high) * 0.5f;
            onA = a0 + (directionA * fractionOnA);
            fractionOnB = Clamp01(Vector3.Dot(onA - b0, directionB) / lengthSquaredB);
            onB = b0 + (directionB * fractionOnB);
            return true;
        }

        private static bool FindSegmentClosest(
            Vector3 a0,
            Vector3 directionA,
            Vector3 b0,
            Vector3 directionB,
            out Vector3 onA,
            out Vector3 onB,
            out float fractionOnA,
            out float fractionOnB)
        {
            SegmentMath.FindDistanceBetweenSegments(a0, a0 + directionA, b0, b0 + directionB, out fractionOnA);
            onA = a0 + (directionA * fractionOnA);
            var lengthSquaredB = directionB.LengthSquared();
            fractionOnB = lengthSquaredB < DegenerateLengthSquared ? 0.0f : Clamp01(Vector3.Dot(onA - b0, directionB) / lengthSquaredB);
            onB = b0 + (directionB * fractionOnB);
            return false;
        }

        private static float Clamp01(float value)
        {
            return Math.Max(0.0f, Math.Min(1.0f, value));
        }
    }
}
