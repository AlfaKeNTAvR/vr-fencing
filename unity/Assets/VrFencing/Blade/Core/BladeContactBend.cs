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
    /// later) it is pinned on that near-side surface, and the drawn blade bends as the hand pushes on. The hand is not
    /// stopped (the haptics stand in) and Touch detection still uses the straight blade.
    ///
    /// <list type="bullet">
    /// <item>The drawn blade is one smooth curve of the blade's full length (<see cref="BladeArc"/>) that leaves the
    /// guard along the hand and ends on the pin. It bends in the plane of the push: sideways when the pin is off the
    /// hand's line, bowing out when pushed straight in (toward the given bow direction, then wherever it last bowed).</item>
    /// <item>The point slides along the surface, never through it, when the blade turns more than
    /// <see cref="MaxSidewaysDegrees"/> off the line from the guard to the point (friction gives way).</item>
    /// <item>The curve never bends more than <see cref="BladeArc.MaxCurvaturePerMeter"/> (the bow of
    /// <see cref="MaxPushMeters"/> pushed straight in), never more than <see cref="MaxBendDegrees"/> off the hand's line,
    /// and never through a body. When the hand asks for more, front or side, the drawn hilt stops following
    /// (<see cref="IsDecoupled"/>): it takes the allowed pose closest to the hand on the way to the blade lined up on the
    /// point at the deepest push, or holds where it was; it follows again once the hand is back within reach.</item>
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

        /// <summary>
        /// The most the line from the drawn guard to the point may turn off the hand's blade, when the point cannot slide
        /// (held by the body's shape): past it the blade would hook, so the drawn hilt lags the hand instead.
        /// </summary>
        public const float MaxBendDegrees = 40.0f;

        /// <summary>The pinned point rests this far out of the surface: the drawn point's radius, so it touches without sinking in.</summary>
        public const float PointRadiusMeters = 0.0045f;

        // The point lets go when the body has moved away from it by more than this.
        private const float BodyMovedMeters = 0.02f;

        // The path from the pin to the straight point is clear when every one of these samples stays out of the bodies.
        private const int ClearPathSamples = 10;
        private const int SearchSteps = 24;

        // The bow plane follows the line to the point once that is this far off the blade; nearer the blade, it keeps
        // where it last bowed, so tracking jitter on a straight push does not swing the bow around.
        private const float PlaneSettleMeters = 0.02f;

        // A first guess that bows the blade toward the bow direction when it is pushed straight in.
        private const float StartingBow = 0.5f;

        private readonly Vector3[] _samples = new Vector3[DrawnBlade.SampleCount + 1];
        private readonly Vector3[] _candidate = new Vector3[DrawnBlade.SampleCount + 1];
        private readonly DrawnShape.PoseTest _tryShape;

        private Vector3? _previousTip;
        private Vector3 _pinnedPoint;
        private int _pinnedBody;
        private bool _hasShape;
        private Vector3 _guardDirection = Vector3.UnitZ;
        private Vector3 _fallbackDirection = Vector3.UnitZ;
        private Vector3 _bowDirection = Vector3.UnitY;
        private Vector3 _candidateBowDirection;
        private float _sidewaysAmount;
        private float _bowAmount;
        private float _candidateSideways;
        private float _candidateBow;
        private float _bowMeters;
        private IReadOnlyList<BladeObstacle> _bodies;
        private Vector3[] _drawnBefore;

        public BladeContactBend()
        {
            _tryShape = TryShape;
        }

        /// <summary>True while the drawn point is held at <see cref="PinnedPoint"/>.</summary>
        public bool IsPinned { get; private set; }

        /// <summary>Where the drawn point is held, while <see cref="IsPinned"/>.</summary>
        public Vector3 PinnedPoint => _pinnedPoint;

        /// <summary>How far the hand has pushed past the surface: blade length minus the hand's guard distance to the pin. 0 when not pinned.</summary>
        public float PushMeters { get; private set; }

        /// <summary>True while the hand asks for more bend than the blade has, and the drawn hilt lags it.</summary>
        public bool IsDecoupled { get; private set; }

        /// <summary>Where the drawn guard is, while pinned: the hand's guard, unless <see cref="IsDecoupled"/>.</summary>
        public Vector3 GuardPosition => _samples[0];

        /// <summary>Which way the drawn blade leaves the drawn guard, while pinned: the hand's blade, unless <see cref="IsDecoupled"/>.</summary>
        public Vector3 GuardDirection => _guardDirection;

        /// <summary>The largest sideways offset of the drawn blade from the guard-to-point line, while pinned.</summary>
        public float BowMeters => IsPinned ? _bowMeters : 0.0f;

        /// <summary>Which way the drawn blade bends, perpendicular to it at the guard.</summary>
        public Vector3 BowDirection => _bowDirection;

        /// <summary>Forgets the pin and the point's last position, e.g. after the blade or the Strip jumped.</summary>
        public void Reset()
        {
            Unpin();
            _previousTip = null;
        }

        /// <summary>
        /// Moves to the blade's new straight pose: pins the point where its path from the last pose first meets a body,
        /// or, while pinned, slides it, checks for release and reshapes the drawn blade.
        /// </summary>
        /// <param name="bodies">Every obstacle the blade cannot pass, in the blade's frame, in the same order every step.</param>
        /// <param name="bowDirection">Which way the blade bows when first pushed straight in (the grip's up); made perpendicular to the blade.</param>
        /// <param name="drawnBefore">
        /// The blade as drawn before this step (<see cref="DrawnBlade.SampleCount"/> + 1 points), if any: a new pin is only
        /// taken when the pinned shape can be reached from it without passing through a body.
        /// </param>
        /// <returns>Why the point let go this step, or <see cref="PinRelease.None"/>.</returns>
        public PinRelease Step(BladeSegment straightBlade, IReadOnlyList<BladeObstacle> bodies, Vector3 bowDirection, Vector3[] drawnBefore = null)
        {
            if (bodies == null)
            {
                throw new ArgumentNullException(nameof(bodies));
            }

            var tip = straightBlade.TipPosition;
            var previousTip = _previousTip;
            _previousTip = tip;

            if (IsPinned)
            {
                var release = FindRelease(tip, bodies);
                if (release != PinRelease.None)
                {
                    Unpin();
                    return release;
                }

                SlideIfPastFriction(straightBlade, bodies[_pinnedBody]);
                Reshape(straightBlade, bodies);
                return PinRelease.None;
            }

            if (previousTip.HasValue && TryFindFirstEntry(previousTip.Value, tip, bodies, out var entryPoint, out var body))
            {
                IsPinned = true;
                _pinnedPoint = entryPoint;
                _pinnedBody = body;
                _hasShape = false;
                _fallbackDirection = straightBlade.Direction;
                _bowDirection = FindPerpendicularDirection(bowDirection, straightBlade.Direction);
                _sidewaysAmount = 0.0f;
                _bowAmount = StartingBow;
                _drawnBefore = drawnBefore;
                var isShaped = Reshape(straightBlade, bodies);
                _drawnBefore = null;
                if (!isShaped && drawnBefore != null)
                {
                    // No pinned shape is reachable from the drawn blade (it would cross a body): the point is not taken
                    // here, and the free drawn blade stays held off the body instead.
                    Unpin();
                }
            }

            return PinRelease.None;
        }

        /// <summary>
        /// Where the drawn blade's centerline is at <paramref name="fraction"/> of the way from the drawn guard (0) to the
        /// point (1) while pinned. Not pinned, the straight blade.
        /// </summary>
        public Vector3 GetCenterlinePoint(BladeSegment straightBlade, float fraction)
        {
            var clamped = Math.Max(0.0f, Math.Min(1.0f, fraction));
            if (!IsPinned)
            {
                return Vector3.Lerp(straightBlade.GuardPosition, straightBlade.TipPosition, clamped);
            }

            var scaled = clamped * DrawnBlade.SampleCount;
            var index = Math.Min((int)scaled, DrawnBlade.SampleCount - 1);
            return Vector3.Lerp(_samples[index], _samples[index + 1], scaled - index);
        }

        /// <summary>The drawn centerline point <paramref name="index"/> while pinned, 0 at the drawn guard to <see cref="DrawnBlade.SampleCount"/> at the point.</summary>
        public Vector3 GetSample(int index)
        {
            return _samples[index];
        }

        private void Unpin()
        {
            IsPinned = false;
            IsDecoupled = false;
            PushMeters = 0.0f;
            _bowMeters = 0.0f;
            _hasShape = false;
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

        /// <summary>
        /// Draws the blade from the hand to the pin if that shape is allowed; otherwise from the allowed pose closest to
        /// the hand on the way to the fallback (the blade lined up on the pin, pushed in no further than
        /// <see cref="MaxPushMeters"/>); otherwise it holds the last shape.
        /// </summary>
        /// <returns>False when no allowed shape was found (it held, or drew the fallback on a first frame).</returns>
        private bool Reshape(BladeSegment straightBlade, IReadOnlyList<BladeObstacle> bodies)
        {
            var handChord = _pinnedPoint - straightBlade.GuardPosition;
            var handDistance = handChord.Length();
            PushMeters = BladePose.BladeLengthMeters - handDistance;

            // The fallback lines the blade up on the pin from the hand's side. A hand past the point keeps the last good
            // line, so the drawn blade stays on the near side.
            if (handDistance > 1e-4f && Vector3.Dot(handChord, straightBlade.Direction) > 0.0f)
            {
                _fallbackDirection = handChord / handDistance;
            }

            var fallbackDistance = Math.Max(BladePose.BladeLengthMeters - MaxPushMeters, Math.Min(BladePose.BladeLengthMeters, handDistance));
            var fallbackGuard = _pinnedPoint - (_fallbackDirection * fallbackDistance);
            var fallback = new BladeSegment(fallbackGuard, fallbackGuard + (_fallbackDirection * BladePose.BladeLengthMeters));

            _bodies = bodies;
            var reached = DrawnShape.FindClosestAllowed(straightBlade, fallback, _tryShape);
            _bodies = null;
            if (reached.HasValue)
            {
                Accept(reached.Value > 0.0f ? DrawnShape.Interpolate(straightBlade, fallback, reached.Value) : straightBlade);
            }
            else if (!_hasShape)
            {
                // The first frame of a pin with nothing allowed (the blade already lies in a body): draw the fallback
                // anyway, so the point is at the pin, and hold from there.
                _bodies = Array.Empty<BladeObstacle>();
                TryShape(fallback);
                _bodies = null;
                Accept(fallback);
            }

            IsDecoupled = DrawnShape.IsOffHand(_samples[0], _guardDirection, straightBlade);
            return reached.HasValue;
        }

        private void Accept(BladeSegment pose)
        {
            Array.Copy(_candidate, _samples, _samples.Length);
            _hasShape = true;
            _guardDirection = pose.Direction;
            _bowDirection = _candidateBowDirection;
            _sidewaysAmount = _candidateSideways;
            _bowAmount = _candidateBow;
            _bowMeters = FindBowMeters();
        }

        /// <summary>
        /// Builds the curve from <paramref name="pose"/>'s guard to the pin into the candidate shape and says whether it
        /// is allowed: it reaches the pin, bends within the limits, and is outside every body. If the side it bows to is
        /// blocked, it tries bowing to the other side.
        /// </summary>
        private bool TryShape(BladeSegment pose)
        {
            var direction = pose.Direction;
            var chord = _pinnedPoint - pose.GuardPosition;
            if (SegmentMath.FindAngleDegrees(direction, chord) > MaxBendDegrees)
            {
                return false;
            }

            var across = FindBowPlane(direction, chord);
            var sideways = _sidewaysAmount;
            var bow = _bowAmount;
            if (BuildCandidate(pose.GuardPosition, direction, across, chord, sideways, bow))
            {
                return true;
            }

            return BuildCandidate(pose.GuardPosition, direction, across, chord, 0.0f, _bowAmount >= 0.0f ? -StartingBow : StartingBow);
        }

        private bool BuildCandidate(Vector3 guard, Vector3 direction, Vector3 across, Vector3 chord, float sideways, float bow)
        {
            var along = Vector3.Dot(chord, direction);
            var acrossAmount = Vector3.Dot(chord, across);
            if (!BladeArc.TrySolve(along, acrossAmount, ref sideways, ref bow)
                || BladeArc.FindMaxCurvature(sideways, bow) > BladeArc.MaxCurvaturePerMeter)
            {
                return false;
            }

            BladeArc.FillSamples(guard, direction, across, sideways, bow, _candidate);

            // Put the end exactly on the pin: the small rest (solver tolerance, and the part of the line to the point out
            // of the bow plane) is spread in like a light sideways push, zero at the guard.
            var rest = _pinnedPoint - _candidate[DrawnBlade.SampleCount];
            for (var index = 1; index <= DrawnBlade.SampleCount; index++)
            {
                var fraction = (float)index / DrawnBlade.SampleCount;
                _candidate[index] += rest * (fraction * fraction * (3.0f - fraction) * 0.5f);
            }

            _candidateBowDirection = across;
            _candidateSideways = sideways;
            _candidateBow = bow;
            // The point's own path is the slide's, along the surface; only the rest of the blade is swept here.
            return _bodies.Count == 0
                || DrawnShape.IsClear(_candidate, _hasShape ? _samples : _drawnBefore, _bodies, DrawnBlade.BladeRadiusMeters, 1, sweepsTip: false);
        }

        /// <summary>
        /// The bow plane's sideways direction (perpendicular to the blade at the guard): toward the point when it is well
        /// off the blade's line, else where the blade last bowed; turned on the same side as the last, so a point
        /// crossing the line does not flip the bow over.
        /// </summary>
        private Vector3 FindBowPlane(Vector3 direction, Vector3 chord)
        {
            var last = FindPerpendicularDirection(_bowDirection, direction);
            var offLine = chord - (Vector3.Dot(chord, direction) * direction);
            if (Vector3.Dot(offLine, last) < 0.0f)
            {
                offLine = -offLine;
            }

            var blended = (last * PlaneSettleMeters) + offLine;
            return blended.LengthSquared() > 1e-10f ? Vector3.Normalize(blended) : last;
        }

        private float FindBowMeters()
        {
            var guard = _samples[0];
            var line = _samples[DrawnBlade.SampleCount] - guard;
            var lineLength = line.Length();
            var largest = 0.0f;
            foreach (var sample in _samples)
            {
                var offset = sample - guard;
                var sideways = lineLength > 1e-6f ? offset - (Vector3.Dot(offset, line / lineLength) * (line / lineLength)) : offset;
                largest = Math.Max(largest, sideways.Length());
            }

            return largest;
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
