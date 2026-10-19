using System;
using System.Collections.Generic;
using System.Numerics;

namespace VrFencing.Blade
{
    /// <summary>Why a pinned point let go.</summary>
    public enum PinRelease
    {
        /// <summary>Still pinned, or was never pinned.</summary>
        None,

        /// <summary>The hand moved back out or around: the straight point is outside every body and can reach the pin without passing through one.</summary>
        CameOut,

        /// <summary>The body moved away from the point (the Dummy stepped to another distance).</summary>
        BodyMoved,
    }

    /// <summary>
    /// Visual-only non-penetration of the point (PER-48): the drawn blade never passes through a body. Where the point
    /// first meets a body (any <see cref="BladeObstacle"/>: the Dummy's capsules and the floor now, another blade
    /// later) it is pinned on that near-side surface, and the drawn blade bows as the hand pushes on. The hand is not
    /// stopped (the haptics stand in) and Touch detection still uses the straight blade.
    ///
    /// <list type="bullet">
    /// <item>The drawn blade keeps its length (<see cref="BladePose.BladeLengthMeters"/>) and leaves the guard along
    /// the hand's blade direction, as it is clamped in the hand; it bows toward the given bow direction (the épée's own
    /// bend, up from the grip).</item>
    /// <item>The point slides along the surface, never through it, when the blade turns more than
    /// <see cref="MaxSidewaysDegrees"/> off the line from the guard to the point (friction gives way).</item>
    /// <item>Past <see cref="MaxPushMeters"/> of push the blade bends no further: the drawn guard stops on the line
    /// to the hand and lags it (<see cref="IsDecoupled"/>) until the hand comes back.</item>
    /// <item>The point lets go only when the straight point is outside every body and has a clear path to the pin
    /// (the hand came back out or went around), or when the body moved away. It pins again only on a new entry from
    /// outside, found along the point's whole path since the last pose so a fast thrust cannot tunnel.</item>
    /// </list>
    ///
    /// Positions in any one frame (the Strip frame in the app), meters.
    /// </summary>
    public sealed class BladeContactBend
    {
        /// <summary>
        /// The most push the drawn blade bows for: about the deepest bow of a real épée pressed hard (10-15 cm of push,
        /// PER-48). Past it the drawn blade lags the hand.
        /// </summary>
        public const float MaxPushMeters = 0.15f;

        /// <summary>
        /// Friction at the point: the largest angle between the hand's blade and the line from the guard to the pinned
        /// point before the point slides along the surface. 20 degrees is about 30 cm of sideways hand travel.
        /// </summary>
        public const float MaxSidewaysDegrees = 20.0f;

        /// <summary>The pinned point rests this far out of the surface: the drawn point's radius, so it touches without sinking in.</summary>
        public const float PointRadiusMeters = 0.0045f;

        // The point lets go when the body has moved away from it by more than this.
        private const float BodyMovedMeters = 0.02f;

        // The path from the pin to the straight point is clear when every one of these samples stays out of the bodies.
        private const int ClearPathSamples = 10;

        // The drawn length is measured along this many straight pieces.
        private const int LengthSamples = 32;
        private const int SearchSteps = 24;
        private const float MaxBowMeters = 0.45f;

        // s^2 (1 - s) peaks at s = 2/3 with 4/27; scaled so the bow amplitude is the largest sideways offset.
        private const float BowShapeScale = 27.0f / 4.0f;

        private Vector3? _previousTip;
        private Vector3 _pinnedPoint;
        private int _pinnedBody;
        private Vector3 _guard;
        private Vector3 _bladeDirection = Vector3.UnitZ;
        private Vector3 _decoupledChordDirection = Vector3.UnitZ;
        private float _bowMeters;
        private Vector3 _bowDirection = Vector3.UnitY;

        /// <summary>True while the drawn point is held at <see cref="PinnedPoint"/>.</summary>
        public bool IsPinned { get; private set; }

        /// <summary>Where the drawn point is held, while <see cref="IsPinned"/>.</summary>
        public Vector3 PinnedPoint => _pinnedPoint;

        /// <summary>How far the hand has pushed past the surface: blade length minus the hand's guard distance to the pin. 0 when not pinned.</summary>
        public float PushMeters { get; private set; }

        /// <summary>True while the hand has pushed past <see cref="MaxPushMeters"/> and the drawn guard lags it.</summary>
        public bool IsDecoupled => IsPinned && PushMeters > MaxPushMeters;

        /// <summary>Where the drawn guard is: the hand's guard, or while <see cref="IsDecoupled"/> the point where the drawn blade stops.</summary>
        public Vector3 GuardPosition => _guard;

        /// <summary>The largest sideways offset of the drawn blade from the guard-to-point line, while pinned.</summary>
        public float BowMeters => IsPinned ? _bowMeters : 0.0f;

        /// <summary>Forgets the pin and the point's last position, e.g. after the blade or the Strip jumped.</summary>
        public void Reset()
        {
            Unpin();
            _previousTip = null;
        }

        /// <summary>
        /// Moves to the blade's new straight pose: pins the point where its path from the last pose first meets a body,
        /// or, while pinned, slides it, checks for release and reshapes the bow.
        /// </summary>
        /// <param name="bodies">Every obstacle the blade cannot pass, in the blade's frame, in the same order every step.</param>
        /// <param name="bowDirection">Which way the blade bows when pushed straight in (the grip's up); made perpendicular to the blade.</param>
        /// <returns>Why the point let go this step, or <see cref="PinRelease.None"/>.</returns>
        public PinRelease Step(BladeSegment straightBlade, IReadOnlyList<BladeObstacle> bodies, Vector3 bowDirection)
        {
            if (bodies == null)
            {
                throw new ArgumentNullException(nameof(bodies));
            }

            var tip = straightBlade.TipPosition;
            var previousTip = _previousTip;
            _previousTip = tip;
            _guard = straightBlade.GuardPosition;
            _bladeDirection = straightBlade.Direction;

            if (IsPinned)
            {
                var release = FindRelease(tip, bodies);
                if (release != PinRelease.None)
                {
                    Unpin();
                    return release;
                }

                SlideIfPastFriction(straightBlade, bodies[_pinnedBody]);
                Reshape(straightBlade, bowDirection);
                return PinRelease.None;
            }

            if (previousTip.HasValue && TryFindFirstEntry(previousTip.Value, tip, bodies, out var entryPoint, out var body))
            {
                IsPinned = true;
                _pinnedPoint = entryPoint;
                _pinnedBody = body;
                _decoupledChordDirection = _bladeDirection;
                Reshape(straightBlade, bowDirection);
            }

            return PinRelease.None;
        }

        /// <summary>
        /// Where the drawn blade's centerline is at <paramref name="fraction"/> of the way from the guard (0) to the point
        /// (1) while pinned: from <see cref="GuardPosition"/> along the hand's blade direction, bowing out, to the pinned
        /// point. Not pinned, the straight blade.
        /// </summary>
        public Vector3 GetCenterlinePoint(BladeSegment straightBlade, float fraction)
        {
            var clamped = Math.Max(0.0f, Math.Min(1.0f, fraction));
            if (!IsPinned)
            {
                return Vector3.Lerp(straightBlade.GuardPosition, straightBlade.TipPosition, clamped);
            }

            return GetBowedPoint(_guard, _bladeDirection, _pinnedPoint, _bowDirection, _bowMeters, clamped);
        }

        /// <summary>
        /// The bowed centerline: the chord from the guard to the point, a term that turns the start onto the hand's
        /// blade (s (1 - s)^2, zero at both ends, slope 1 at the guard), and the bow (s^2 (1 - s), zero at both ends and
        /// flat at the guard, so the blade still leaves the guard straight).
        /// </summary>
        private static Vector3 GetBowedPoint(Vector3 guard, Vector3 bladeDirection, Vector3 point, Vector3 bowDirection, float bowMeters, float s)
        {
            var chord = point - guard;
            var offAxis = chord - (Vector3.Dot(chord, bladeDirection) * bladeDirection);
            var oneMinus = 1.0f - s;
            return guard + (s * chord) - (s * oneMinus * oneMinus * offAxis) + (bowMeters * BowShapeScale * s * s * oneMinus * bowDirection);
        }

        private void Unpin()
        {
            IsPinned = false;
            PushMeters = 0.0f;
            _bowMeters = 0.0f;
        }

        private PinRelease FindRelease(Vector3 tip, IReadOnlyList<BladeObstacle> bodies)
        {
            if (_pinnedBody >= bodies.Count
                || bodies[_pinnedBody].FindDistance(_pinnedPoint) > PointRadiusMeters + BodyMovedMeters)
            {
                return PinRelease.BodyMoved;
            }

            // Out along a path that passes through nothing: the point simply leaves the surface.
            for (var sample = 1; sample <= ClearPathSamples; sample++)
            {
                var point = Vector3.Lerp(_pinnedPoint, tip, (float)sample / ClearPathSamples);
                if (FindDistanceToNearestSurface(point, bodies) < PointRadiusMeters * 0.5f)
                {
                    return PinRelease.None;
                }
            }

            return FindDistanceToNearestSurface(tip, bodies) >= PointRadiusMeters ? PinRelease.CameOut : PinRelease.None;
        }

        /// <summary>
        /// Past the friction angle, the point slides over the pinned body's surface toward the straight point, just far
        /// enough to come back inside the angle (or the whole way this step). It moves along the surface, so never
        /// through the body.
        /// </summary>
        private void SlideIfPastFriction(BladeSegment straightBlade, BladeObstacle body)
        {
            if (FindChordAngleDegrees(straightBlade, _pinnedPoint) <= MaxSidewaysDegrees)
            {
                return;
            }

            var normal = body.FindOutwardNormal(_pinnedPoint);
            var towardTip = straightBlade.TipPosition - _pinnedPoint;
            var tangential = towardTip - (Vector3.Dot(towardTip, normal) * normal);
            var fullSlide = body.ProjectOntoSurface(_pinnedPoint + tangential, PointRadiusMeters);
            if (FindChordAngleDegrees(straightBlade, fullSlide) > MaxSidewaysDegrees)
            {
                _pinnedPoint = fullSlide;
                return;
            }

            var low = 0.0f;
            var high = 1.0f;
            for (var step = 0; step < SearchSteps; step++)
            {
                var middle = (low + high) * 0.5f;
                if (FindChordAngleDegrees(straightBlade, body.ProjectOntoSurface(_pinnedPoint + (middle * tangential), PointRadiusMeters)) > MaxSidewaysDegrees)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }

            _pinnedPoint = body.ProjectOntoSurface(_pinnedPoint + (high * tangential), PointRadiusMeters);
        }

        private static float FindChordAngleDegrees(BladeSegment straightBlade, Vector3 point)
        {
            return SegmentMath.FindAngleDegrees(straightBlade.Direction, point - straightBlade.GuardPosition);
        }

        /// <summary>Works out the push, the drawn guard and the bow that keeps the drawn blade at its full length.</summary>
        private void Reshape(BladeSegment straightBlade, Vector3 bowDirection)
        {
            var handChord = _pinnedPoint - straightBlade.GuardPosition;
            PushMeters = BladePose.BladeLengthMeters - handChord.Length();
            _bowDirection = FindPerpendicularDirection(bowDirection, _bladeDirection);

            if (PushMeters > MaxPushMeters)
            {
                // The drawn guard stops on the line from the point to the hand; if the hand is past the point, it
                // keeps the last good line, so the drawn blade stays on the near side.
                if (Vector3.Dot(handChord, _bladeDirection) > 0.0f)
                {
                    _decoupledChordDirection = Vector3.Normalize(handChord);
                }

                _guard = _pinnedPoint - (_decoupledChordDirection * (BladePose.BladeLengthMeters - MaxPushMeters));
            }
            else
            {
                _guard = straightBlade.GuardPosition;
                if (handChord.LengthSquared() > 1e-8f)
                {
                    _decoupledChordDirection = Vector3.Normalize(handChord);
                }
            }

            _bowMeters = SolveBow();
        }

        /// <summary>Finds the bow that keeps the drawn blade at its full length, by bisection: the drawn length only grows with the bow.</summary>
        private float SolveBow()
        {
            if (MeasureLength(0.0f) >= BladePose.BladeLengthMeters)
            {
                return 0.0f;
            }

            var low = 0.0f;
            var high = MaxBowMeters;
            for (var step = 0; step < SearchSteps; step++)
            {
                var middle = (low + high) * 0.5f;
                if (MeasureLength(middle) < BladePose.BladeLengthMeters)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }

            return (low + high) * 0.5f;
        }

        private float MeasureLength(float bowMeters)
        {
            var length = 0.0f;
            var previous = _guard;
            for (var sample = 1; sample <= LengthSamples; sample++)
            {
                var point = GetBowedPoint(_guard, _bladeDirection, _pinnedPoint, _bowDirection, bowMeters, (float)sample / LengthSamples);
                length += Vector3.Distance(previous, point);
                previous = point;
            }

            return length;
        }

        /// <summary>The earliest point along the path where the point's sphere meets a body, resting on its surface.</summary>
        private static bool TryFindFirstEntry(Vector3 previousTip, Vector3 tip, IReadOnlyList<BladeObstacle> bodies, out Vector3 entryPoint, out int entryBody)
        {
            entryPoint = Vector3.Zero;
            entryBody = -1;
            var earliest = float.MaxValue;
            for (var index = 0; index < bodies.Count; index++)
            {
                if (bodies[index].TryFindEntry(previousTip, tip, PointRadiusMeters, out var fraction) && fraction < earliest)
                {
                    earliest = fraction;
                    entryPoint = Vector3.Lerp(previousTip, tip, fraction);
                    entryBody = index;
                }
            }

            return entryBody >= 0;
        }

        private static float FindDistanceToNearestSurface(Vector3 point, IReadOnlyList<BladeObstacle> bodies)
        {
            var nearest = float.MaxValue;
            foreach (var body in bodies)
            {
                nearest = Math.Min(nearest, body.FindDistance(point));
            }

            return nearest;
        }

        /// <summary><paramref name="preferred"/> made perpendicular to the blade; any perpendicular if it is along the blade.</summary>
        private static Vector3 FindPerpendicularDirection(Vector3 preferred, Vector3 bladeDirection)
        {
            var perpendicular = preferred - (Vector3.Dot(preferred, bladeDirection) * bladeDirection);
            if (perpendicular.LengthSquared() < 1e-8f)
            {
                perpendicular = Vector3.Cross(bladeDirection, Vector3.UnitX);
                if (perpendicular.LengthSquared() < 1e-8f)
                {
                    perpendicular = Vector3.Cross(bladeDirection, Vector3.UnitY);
                }
            }

            return Vector3.Normalize(perpendicular);
        }
    }
}
