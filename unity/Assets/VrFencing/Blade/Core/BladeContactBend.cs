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
    /// <item>The drawn blade is the elastica of a blade clamped at the guard and pinned at the point (<see cref="BladeArc"/>,
    /// PER-50): one smooth curve of the blade's full length that leaves the guard along the hand, ends on the pin, and
    /// has no curvature at the point. It bends in the plane of the hand's blade and the line to the point, and bulges to
    /// the side the hand's blade points to from that line, so it curves over toward the point. Pushed straight in, the
    /// side is the way the blade hangs: its set (toward the grip's palm side, turning with the wrist) plus gravity sag
    /// (<see cref="BladeDroop"/>, PER-52), unless the hand is moving sideways
    /// (it bulges the way the hand moves); a side that leans into the surface (a shallow press on the floor, a glancing
    /// one on the Dummy) is mirrored out of it, so the blade starts on its free side (PER-69). The side holds through a
    /// press (hysteresis) and changes only when the point is clearly on the other side of the hand's line
    /// (<see cref="FlipMeters"/>) and that side is free. A blocked side never makes it snap over to the other: the drawn
    /// hilt lags instead, and the side only changes over where the blade is nearly straight, so nothing jumps.</item>
    /// <item>The point slides along the surface, never through it, when the blade's push on it leans out of the friction
    /// cone round the surface normal (<see cref="BladeObstacle.FrictionCoefficient"/>: the floor is slippery, the
    /// Dummy's jacket grips): just far enough that the push of the blade in its new shape is back on the cone's
    /// edge, and no faster than the hand moves (or <see cref="SlipMeters"/> a step). A steep press pins; a shallow one
    /// skids along with the hand, keeping its bend. Pulled back, the point is dragged after the hand.</item>
    /// <item>The curve never bends more than <see cref="BladeArc.MaxCurvaturePerMeter"/> (the bow of
    /// <see cref="MaxPushMeters"/> pushed straight in), never more than <see cref="MaxBendDegrees"/> off the hand's line,
    /// never curls its point back past perpendicular to a surface it meets at a slant (<see cref="SlantDegrees"/>), and
    /// never through a body. When the hand asks for more, front or side, the drawn hilt stops following
    /// (<see cref="IsDecoupled"/>): it takes the allowed pose closest to the hand on the way to the blade lined up on the
    /// point at the deepest push, or holds where it was; it follows again once the hand is back within reach.</item>
    /// <item>On steel (the Armed Dummy's blade or bell, <see cref="BladeObstacle.IsSteel"/>) pressed past those limits,
    /// the point does not wedge (PER-121): it slips with no friction the way the push leans (or the hand's point is off
    /// the pin), off to the side or round the end, and lets go there.</item>
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
        /// The most push the drawn blade bows for: a real épée on a hard Touch bows in one big arc (PER-50 reference
        /// images); 20 cm of push straight in bulges it about 24 cm. Past it the drawn blade lags the hand.
        /// </summary>
        public const float MaxPushMeters = 0.20f;

        /// <summary>
        /// The bulge changes side during a press only once the point is this far past the hand's line on the bulge's
        /// side: closer, tracking jitter or a small wobble would flip it back and forth.
        /// </summary>
        public const float FlipMeters = 0.03f;

        /// <summary>
        /// A line from the guard to the point more than this off the surface normal meets the surface at a slant: the
        /// point may then not curl back past perpendicular to the surface (toward the hand), which would hook the blade.
        /// Nearer the normal there is no back to curl toward.
        /// </summary>
        public const float SlantDegrees = 20.0f;

        /// <summary>
        /// The blade changes over to its other side only while both shapes bulge less than this: nearly straight, so the
        /// change does not show as a jump.
        /// </summary>
        public const float SideChangeBowMeters = 0.01f;

        // A new press's side is mirrored out of the surface when it leans into it more than this (the sine of 30
        // degrees): a straight push into the Dummy leans its set along the surface and keeps it.
        private const float MirrorLean = 0.5f;

        /// <summary>
        /// The most the line from the drawn guard to the point may turn off the hand's blade, when the point cannot slide
        /// (held by the body's shape): past it the blade would hook, so the drawn hilt lags the hand instead.
        /// </summary>
        public const float MaxBendDegrees = 40.0f;

        /// <summary>The pinned point rests this far out of the surface: the drawn point's radius, so it touches without sinking in.</summary>
        public const float PointRadiusMeters = 0.0045f;

        // The point lets go when the body has moved away from it by more than this.
        private const float BodyMovedMeters = 0.02f;

        /// <summary>
        /// A point slipping on its own (the hand still, the push leaning out of the friction cone) moves at most this far
        /// in one step, about 0.7 m/s at 72 Hz: a slip to a far spot plays out over a few frames instead of a jump.
        /// </summary>
        public const float SlipMeters = 0.01f;

        // The path from the pin to the straight point is clear when every one of these samples stays out of the bodies.
        private const int ClearPathSamples = 10;

        // How far the point slides is found by stepping out 1 cm, then 2 cm at a time (the shape followed from step to
        // step), then bisecting the last step 7 times (a tenth of a millimeter or finer).
        private const float FirstSlideStepMeters = 0.01f;
        private const float MaxSlideStepMeters = 0.02f;
        private const int SlideSearchSteps = 7;

        // The blade keeps at least this much push on its point: pulled back further, the point is dragged after the hand
        // (a blade cannot pull), and a slide stops this far short of where the blade could not be drawn.
        private const float SlideMarginMeters = 0.002f;

        // A push on the steel leaning less than this (squared, of a unit push: about 1 degree) off its normal gives no
        // way to slip: the hand's point decides it.
        private const float SlipLeanSquared = 3e-4f;

        private static readonly float SlantSine = (float)Math.Sin(SlantDegrees * Math.PI / 180.0);

        // Past the band round the hand's line, the bow plane turns toward the point gradually: it weighs the last plane
        // as this much more offset, so it does not swing round at once.
        private const float PlaneSettleMeters = 0.02f;

        // The blade's set: a real épée carries a slight bend so it buckles the same way every time. Ours bulges toward
        // the grip's palm side (away from the thumb, which pushes on the top of a French or pistol grip), weighted as
        // this much sideways point motion in one frame: a point moving sideways faster than about 0.4 m/s at 72 Hz as
        // it lands decides instead (the blade drags behind the hand).
        private const float SetBiasMeters = 0.005f;

        private readonly Vector3[] _samples = new Vector3[DrawnBlade.SampleCount + 1];
        private readonly Vector3[] _candidate = new Vector3[DrawnBlade.SampleCount + 1];
        private readonly DrawnShape.PoseTest _tryShape;

        private Vector3? _previousTip;
        private Vector3 _pinnedPoint;
        private Vector3 _pinnedNormal = Vector3.UnitY;
        private int _pinnedBody;
        private bool _hasShape;
        private Vector3 _guardDirection = Vector3.UnitZ;
        private Vector3 _fallbackDirection = Vector3.UnitZ;
        private Vector3 _bowDirection = -Vector3.UnitY;
        private Vector3 _candidateBowDirection;
        private ElasticaShape _shape;
        private ElasticaShape _warmShape;
        private bool _isFollowingShape;
        private ElasticaShape _candidateShape;
        private Vector3 _lastGripUp = Vector3.UnitY;
        private Vector3 _lastBladeDirection = Vector3.UnitZ;
        private float _bowMeters;
        private IReadOnlyList<BladeObstacle> _bodies;
        private Vector3[] _drawnBefore;
        private float _slideFriction;
        private bool _isSlipping;

        public BladeContactBend()
        {
            _tryShape = TryShape;
        }

        /// <summary>True while the drawn point is held at <see cref="PinnedPoint"/>.</summary>
        public bool IsPinned { get; private set; }

        /// <summary>
        /// True when the point was last pinned on a plane (the floor) rather than a body: while <see cref="IsPinned"/>,
        /// the point is on the floor. The "Floor buzz" Setting silences the press buzz then.
        /// </summary>
        public bool IsPinnedOnPlane { get; private set; }

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

        /// <summary>
        /// Lets the point go at once, the hand's point now at <paramref name="tip"/>: the drawn blade popped back to the
        /// hand (<see cref="DrawnBlade.LeashMeters"/>, PER-121). A new pin needs a new entry from there.
        /// </summary>
        public void LetGo(Vector3 tip)
        {
            Unpin();
            _previousTip = tip;
        }

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
        /// <param name="gripUp">The grip's up (the thumb's side): the blade's set bulges it the other way when pushed straight in.</param>
        /// <param name="drawnBefore">
        /// The blade as drawn before this step (<see cref="DrawnBlade.SampleCount"/> + 1 points), if any: a new pin is only
        /// taken when the pinned shape can be reached from it without passing through a body.
        /// </param>
        /// <param name="restBend">
        /// Where the blade's rest shape hangs its point (<see cref="BladeDroop.TipDeflection"/>: the set plus gravity sag,
        /// PER-52): a straight push bends the blade further that way. Zero for a straight blade: the set alone, away from
        /// <paramref name="gripUp"/>.
        /// </param>
        /// <returns>Why the point let go this step, or <see cref="PinRelease.None"/>.</returns>
        public PinRelease Step(BladeSegment straightBlade, IReadOnlyList<BladeObstacle> bodies, Vector3 gripUp, Vector3[] drawnBefore = null, Vector3 restBend = default)
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

                // The blade is clamped in the hand: rolling the wrist turns the bulge with it.
                _bowDirection = TurnWithGrip(_bowDirection, straightBlade.Direction, gripUp);
                _bodies = bodies;
                SlideByFriction(straightBlade, bodies[_pinnedBody], previousTip.HasValue ? tip - previousTip.Value : Vector3.Zero);
                _bodies = null;
                Reshape(straightBlade, bodies);
                return PinRelease.None;
            }

            if (previousTip.HasValue && TryFindFirstEntry(previousTip.Value, tip, bodies, out var entryPoint, out var body))
            {
                IsPinned = true;
                _pinnedPoint = entryPoint;
                _pinnedBody = body;
                IsPinnedOnPlane = bodies[body].IsPlane;
                _hasShape = false;
                _fallbackDirection = straightBlade.Direction;
                var normal = bodies[body].FindOutwardNormal(entryPoint);
                _bowDirection = FindStartingBow(straightBlade.Direction, gripUp, restBend, tip - previousTip.Value, normal);
                _lastBladeDirection = straightBlade.Direction;
                _lastGripUp = gripUp;
                _shape = default;
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
        /// Friction at the point (PER-69). The blade bent from the hand to the pin pushes on the pin along its load line
        /// (<see cref="BladeArc.FindLoadDirection"/>), not along the hand. While that push leans off the surface normal
        /// by less than the friction allows, the point holds. Past it, the point slides over the surface the way the push
        /// leans, to the first spot where the blade bent to it pushes inside the friction cone again (the cone's edge),
        /// and moves along the surface, so never through the body. A skid goes at most as far in one step as the straight
        /// point moved along the surface, or <see cref="SlipMeters"/> if that is more: where the cone's edge cannot be
        /// reached (a shallow press on the slippery floor), the point skids along with the hand and the blade keeps its
        /// bend; a slip to a far edge plays out over a few frames. It stops short of where no bent blade from the hand
        /// reaches. Pulled back, the point is dragged after the hand instead.
        /// </summary>
        /// <param name="tipStep">How far the straight point moved since the last step.</param>
        private void SlideByFriction(BladeSegment straightBlade, BladeObstacle body, Vector3 tipStep)
        {
            var start = _pinnedPoint;
            var normal = body.FindOutwardNormal(start);
            _warmShape = _shape;

            // Pressed past the bend limit on the Dummy's blade or bell (steel, PER-121), the point does not wedge: it
            // slips the way the push leans, off to the side it is on or round the end.
            _isSlipping = IsDecoupled && body.IsSteel;
            _slideFriction = _isSlipping ? 0.0f : body.FrictionCoefficient;
            if (!IsInReach(straightBlade, start))
            {
                DragTowardStraightPoint(straightBlade, body, start, normal);
                return;
            }

            // The push follows the shape the blade had: an elastica can have more than one shape to a point, and a jump
            // to another would turn the push round. The push is the hand's through the blade even where the drawn blade
            // cannot follow it (bent past its limits, the drawn hilt lagging): friction answers the real push.
            _isFollowingShape = true;
            var hasPush = TryFindPush(straightBlade, start, out var push);
            _isFollowingShape = false;
            if (!hasPush)
            {
                // The blade's shape does not carry on to the pin from the hand (it would have to change side, or run
                // through a body): the point holds, and the drawn hilt lags if no shape reaches it either.
                return;
            }

            if (IsInsideFrictionCone(push, normal, _slideFriction))
            {
                // Held: the drawn shape is solved again from this one, already on the pin.
                _shape = _candidateShape;
                return;
            }

            var along = push - (Vector3.Dot(push, normal) * normal);
            if (_isSlipping && along.LengthSquared() < SlipLeanSquared)
            {
                // Pushed square onto the steel: it slips the way the hand's point is off the pin along the surface.
                var towardTip = straightBlade.TipPosition - start;
                along = towardTip - (Vector3.Dot(towardTip, normal) * normal);
            }

            if (along.LengthSquared() < 1e-12f)
            {
                return;
            }

            _warmShape = _candidateShape;
            _isFollowingShape = true;
            var slideDirection = Vector3.Normalize(along);
            var tipAlong = tipStep - (Vector3.Dot(tipStep, normal) * normal);
            var skid = Math.Max(tipAlong.Length(), SlipMeters);
            var reach = Math.Min(skid, FindReachAlong(straightBlade.GuardPosition, start, slideDirection));
            var stop = FindWhereSlideStops(straightBlade, body, start, slideDirection, reach, SlideTest.Friction, out var end);
            _isFollowingShape = false;

            // Not drawable further on: it stops a little short of that, so a small move of the hand does not put the pin
            // out of reach at once.
            MovePin(body, start, slideDirection, end == SlideState.OutOfReach ? Math.Max(0.0f, stop - SlideMarginMeters) : stop);
        }

        /// <summary>
        /// The hand pulled back until its blade no longer pushes on the pin (a blade cannot pull): the point is dragged
        /// over the surface toward the straight point (or toward the hand, pulled straight back), to the nearest spot
        /// the blade pushes on again.
        /// </summary>
        private void DragTowardStraightPoint(BladeSegment straightBlade, BladeObstacle body, Vector3 start, Vector3 normal)
        {
            var towardTip = straightBlade.TipPosition - start;
            var along = towardTip - (Vector3.Dot(towardTip, normal) * normal);
            var length = along.Length();
            if (length < SlipMeters)
            {
                // The straight point is about under the pin: pulled straight back, the point follows the hand.
                var towardGuard = straightBlade.GuardPosition - start;
                along = towardGuard - (Vector3.Dot(towardGuard, normal) * normal);
                length = Math.Min(along.Length(), MaxPushMeters);
                if (length < 1e-6f)
                {
                    return;
                }

                along = Vector3.Normalize(along) * length;
            }

            var direction = along / length;
            var reached = FindWhereSlideStops(straightBlade, body, start, direction, length, SlideTest.Reach, out var end);
            if (end != SlideState.Sliding)
            {
                MovePin(body, start, direction, reached);
            }
        }

        /// <summary>
        /// True when the hand's blade still pushes on <paramref name="point"/> by at least <see cref="SlideMarginMeters"/>:
        /// a blade pulled back to less cannot hold its point, which follows.
        /// </summary>
        private static bool IsInReach(BladeSegment hand, Vector3 point)
        {
            return Vector3.Distance(point, hand.GuardPosition) <= BladePose.BladeLengthMeters - SlideMarginMeters;
        }

        private void MovePin(BladeObstacle body, Vector3 start, Vector3 direction, float distance)
        {
            _pinnedPoint = body.ProjectOntoSurface(start + (direction * distance), PointRadiusMeters);
        }

        /// <summary>
        /// Walks the point out from <paramref name="start"/> along <paramref name="direction"/> in steps of
        /// <see cref="FirstSlideStepMeters"/>, then up to <see cref="MaxSlideStepMeters"/>, until the test stops saying it
        /// slides on (or <paramref name="maxDistance"/>), then bisects the last step: the nearest spot where it stops.
        /// Each test starts the shape from the last spot it slid through, so the walk follows one shape. Returns that
        /// distance; with <paramref name="end"/> <see cref="SlideState.Sliding"/> it never stopped. For
        /// <see cref="SlideState.OutOfReach"/> the distance is the last spot a bent blade still reaches.
        /// </summary>
        private float FindWhereSlideStops(BladeSegment hand, BladeObstacle body, Vector3 start, Vector3 direction, float maxDistance, SlideTest test, out SlideState end)
        {
            var moving = 0.0f;
            var stepMeters = FirstSlideStepMeters;
            end = SlideState.Sliding;
            while (moving < maxDistance)
            {
                var next = Math.Min(maxDistance, moving + stepMeters);
                if (TestSlide(hand, body, start, direction, next, test) != SlideState.Sliding)
                {
                    var stopped = next;
                    for (var step = 0; step < SlideSearchSteps; step++)
                    {
                        var middle = (moving + stopped) * 0.5f;
                        if (TestSlide(hand, body, start, direction, middle, test) == SlideState.Sliding)
                        {
                            moving = middle;
                            FollowSlidShape(test);
                        }
                        else
                        {
                            stopped = middle;
                        }
                    }

                    end = TestSlide(hand, body, start, direction, stopped, test);
                    return end == SlideState.OutOfReach ? moving : stopped;
                }

                moving = next;
                FollowSlidShape(test);
                stepMeters = Math.Min(MaxSlideStepMeters, stepMeters * 2.0f);
            }

            return maxDistance;
        }

        /// <summary>A friction slide carries on from the shape at the spot it just slid through; a drag has none there.</summary>
        private void FollowSlidShape(SlideTest test)
        {
            if (test == SlideTest.Friction)
            {
                _warmShape = _candidateShape;
            }
        }

        private SlideState TestSlide(BladeSegment hand, BladeObstacle body, Vector3 start, Vector3 direction, float distance, SlideTest test)
        {
            var point = body.ProjectOntoSurface(start + (direction * distance), PointRadiusMeters);
            if (test == SlideTest.Reach)
            {
                return IsInReach(hand, point) && TryFindPush(hand, point, out _) ? SlideState.Held : SlideState.Sliding;
            }

            if (!TryFindPush(hand, point, out var push))
            {
                return SlideState.OutOfReach;
            }

            // A push that now leans back against the slide has passed through the cone on the way: held there.
            var normal = body.FindOutwardNormal(point);
            // Slipping off steel past the bend limit, it goes on until it is held on the normal or out of reach.
            var isLeaningBack = !_isSlipping && Vector3.Dot(push, direction) - (Vector3.Dot(push, normal) * Vector3.Dot(normal, direction)) <= 0.0f;
            return isLeaningBack || IsInsideFrictionCone(push, normal, _slideFriction) ? SlideState.Held : SlideState.Sliding;
        }

        /// <summary>True when the push leans off the surface's inward normal by no more than the friction allows.</summary>
        private static bool IsInsideFrictionCone(Vector3 push, Vector3 outwardNormal, float friction)
        {
            var into = -Vector3.Dot(push, outwardNormal);
            if (into <= 0.0f)
            {
                return false;
            }

            var along = push + (into * outwardNormal);
            return along.Length() <= friction * into;
        }

        /// <summary>
        /// How far the point can go from <paramref name="point"/> along <paramref name="direction"/> before the push is
        /// down to <see cref="SlideMarginMeters"/> (a blade with no push left cannot bend at all); 0 when it already is.
        /// </summary>
        private static float FindReachAlong(Vector3 guard, Vector3 point, Vector3 direction)
        {
            var fromGuard = point - guard;
            var halfLinear = Vector3.Dot(direction, fromGuard);
            var reach = BladePose.BladeLengthMeters - SlideMarginMeters;
            var constant = fromGuard.LengthSquared() - (reach * reach);
            if (constant >= 0.0f)
            {
                return 0.0f;
            }

            return -halfLinear + (float)Math.Sqrt((halfLinear * halfLinear) - constant);
        }

        /// <summary>
        /// The push of the blade, bent from the hand's pose to <paramref name="point"/> by the side rules and clear of every
        /// body, on that point (unit length, along the load line). The bend limits are not checked: this is the hand's
        /// push even where the drawn hilt will lag. False when no such shape reaches the point.
        /// </summary>
        private bool TryFindPush(BladeSegment hand, Vector3 point, out Vector3 push)
        {
            push = Vector3.Zero;
            if (!TryShapeTo(hand, point, checksLimits: false))
            {
                return false;
            }

            var load = BladeArc.FindLoadDirection(_candidateShape);
            push = (load.X * hand.Direction) + (load.Y * _candidateBowDirection);
            return true;
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
            _pinnedNormal = bodies[_pinnedBody].FindOutwardNormal(_pinnedPoint);

            // The fallback lines the blade up on the pin from the hand's side. A hand past the point keeps the last good
            // line, so the drawn blade stays on the near side.
            if (handDistance > 1e-4f && Vector3.Dot(handChord, straightBlade.Direction) > 0.0f)
            {
                _fallbackDirection = handChord / handDistance;
            }

            var fallbackDistance = Math.Max(BladePose.BladeLengthMeters - MaxPushMeters, Math.Min(BladePose.BladeLengthMeters, handDistance));
            var fallback = LineUpOnPin(fallbackDistance);

            _bodies = bodies;
            _warmShape = _shape;
            if (!TryShape(fallback))
            {
                // On a slanted surface the most push lined up on the pin can curl the point back past perpendicular: the
                // fallback pushes in only as far as the point stays short of that.
                var allowed = BladePose.BladeLengthMeters - SlideMarginMeters;
                var curled = fallbackDistance;
                for (var step = 0; step < SlideSearchSteps; step++)
                {
                    var middle = (allowed + curled) * 0.5f;
                    if (TryShape(LineUpOnPin(middle)))
                    {
                        allowed = middle;
                    }
                    else
                    {
                        curled = middle;
                    }
                }

                fallback = LineUpOnPin(allowed);
            }

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

        /// <summary>The blade lined up on the pin from the hand's side (<c>_fallbackDirection</c>), its guard this far from it.</summary>
        private BladeSegment LineUpOnPin(float guardDistance)
        {
            var guard = _pinnedPoint - (_fallbackDirection * guardDistance);
            return new BladeSegment(guard, guard + (_fallbackDirection * BladePose.BladeLengthMeters));
        }

        private void Accept(BladeSegment pose)
        {
            Array.Copy(_candidate, _samples, _samples.Length);
            _hasShape = true;
            _guardDirection = pose.Direction;
            _bowDirection = _candidateBowDirection;
            _shape = _candidateShape;
            _bowMeters = FindBowMeters(_samples);
        }

        /// <summary>
        /// Builds the elastica from <paramref name="pose"/>'s guard to the pin into the candidate shape and says whether
        /// it is allowed: it reaches the pin, bends within the limits, and is outside every body.
        /// </summary>
        private bool TryShape(BladeSegment pose)
        {
            return TryShapeTo(pose, _pinnedPoint, checksLimits: true);
        }

        /// <summary>
        /// Builds the elastica from <paramref name="pose"/>'s guard to <paramref name="point"/> into the candidate shape by
        /// the side rules (PER-69), and says whether it reaches the point outside every body (and, with
        /// <paramref name="checksLimits"/>, within the bend limits):
        /// <list type="number">
        /// <item>the plane and side <see cref="FindBowPlane"/> asks for;</item>
        /// <item>if that is blocked or out of reach, the plane and side the blade has, without turning toward the point:
        /// a blocked natural side never blends the plane round (that flickered with the wrist rolled 90 degrees);</item>
        /// <item>the other side only on a press's first frame, or while both shapes are nearly straight
        /// (<see cref="SideChangeBowMeters"/>): a blocked side never snaps the blade over to a shape far from the one
        /// drawn. Otherwise the drawn hilt lags instead.</item>
        /// </list>
        /// </summary>
        private bool TryShapeTo(BladeSegment pose, Vector3 point, bool checksLimits)
        {
            var direction = pose.Direction;
            var chord = point - pose.GuardPosition;
            if (checksLimits && SegmentMath.FindAngleDegrees(direction, chord) > MaxBendDegrees)
            {
                return false;
            }

            var current = FindPerpendicularDirection(_bowDirection, direction);
            var asked = FindBowPlane(direction, chord);
            if (IsLeaningIntoSurface(asked) && !IsLeaningIntoSurface(current))
            {
                // Never turn over to bulge into the surface the point is on (PER-69).
                asked = current;
            }

            if (BuildCandidate(pose.GuardPosition, direction, asked, point, _warmShape, checksLimits))
            {
                return true;
            }

            if (Vector3.Dot(asked, current) < 0.9999f && BuildCandidate(pose.GuardPosition, direction, current, point, _warmShape, checksLimits))
            {
                return true;
            }

            if (_hasShape && (_bowMeters > SideChangeBowMeters || IsLeaningIntoSurface(-current)))
            {
                return false;
            }

            return BuildCandidate(pose.GuardPosition, direction, -current, point, default, checksLimits)
                && (!_hasShape || FindBowMeters(_candidate) <= SideChangeBowMeters);
        }

        private bool IsLeaningIntoSurface(Vector3 bow)
        {
            return -Vector3.Dot(bow, _pinnedNormal) > MirrorLean;
        }

        private bool BuildCandidate(Vector3 guard, Vector3 direction, Vector3 across, Vector3 point, ElasticaShape start, bool checksLimits)
        {
            var chord = point - guard;
            var shape = start;
            var along = Vector3.Dot(chord, direction);
            var acrossAmount = Vector3.Dot(chord, across);
            var isSolved = _isFollowingShape
                ? BladeArc.TrySolveNear(along, acrossAmount, ref shape)
                : BladeArc.TrySolve(along, acrossAmount, ref shape);
            if (!isSolved
                || (checksLimits && BladeArc.FindMaxCurvature(shape) > BladeArc.MaxCurvaturePerMeter))
            {
                return false;
            }

            BladeArc.FillSamples(guard, direction, across, shape, _candidate);

            // Put the end exactly on the point: the small rest (solver tolerance, and the part of the line to the point
            // out of the bend plane while the bulge holds its side) is spread in like a light sideways push: zero at the
            // guard, and no curvature at the point.
            var rest = point - _candidate[DrawnBlade.SampleCount];
            for (var index = 1; index <= DrawnBlade.SampleCount; index++)
            {
                var fraction = (float)index / DrawnBlade.SampleCount;
                _candidate[index] += rest * (fraction * fraction * (3.0f - fraction) * 0.5f);
            }

            _candidateBowDirection = across;
            _candidateShape = shape;
            if (checksLimits && IsCurledBack(chord))
            {
                return false;
            }

            // The point's own path is the slide's, along the surface; only the rest of the blade is swept here.
            return _bodies.Count == 0
                || DrawnShape.IsClear(_candidate, _hasShape ? _samples : _drawnBefore, _bodies, DrawnBlade.BladeRadiusMeters, 1, sweepsTip: false);
        }

        /// <summary>
        /// True when the candidate's point curls back past perpendicular to the pinned surface: on a surface met at a
        /// slant (<see cref="SlantDegrees"/>), its last piece leans back toward the hand's side along the surface. That
        /// is the hook of the floor press (PER-69); the point slides or the drawn hilt lags instead.
        /// </summary>
        private bool IsCurledBack(Vector3 chord)
        {
            var chordAlong = chord - (Vector3.Dot(chord, _pinnedNormal) * _pinnedNormal);
            if (chordAlong.Length() < chord.Length() * SlantSine)
            {
                return false;
            }

            var lastPiece = _candidate[DrawnBlade.SampleCount] - _candidate[DrawnBlade.SampleCount - 1];
            return Vector3.Dot(lastPiece, chordAlong) < 0.0f;
        }

        /// <summary>
        /// The side the blade bulges to (perpendicular to the blade at the guard). Naturally, away from where the point is
        /// off the hand's line, so the blade curves over toward the point; within <see cref="FlipMeters"/> of the line it
        /// keeps the plane and side it had (the set, or where it last bulged). It changes side only once the point is
        /// <see cref="FlipMeters"/> past the hand's line on the bulge's side.
        /// </summary>
        private Vector3 FindBowPlane(Vector3 direction, Vector3 chord)
        {
            var last = FindPerpendicularDirection(_bowDirection, direction);
            var natural = -(chord - (Vector3.Dot(chord, direction) * direction));
            var agreement = Vector3.Dot(natural, last);
            if (agreement < -FlipMeters)
            {
                return Vector3.Normalize(natural);
            }

            // Within the band round the hand's line, jitter must not turn the plane: keep it. Past the band, follow the
            // point round the blade, keeping the side.
            var offLine = natural.Length();
            if (offLine <= FlipMeters)
            {
                return last;
            }

            var toward = (natural - (Math.Min(0.0f, agreement) * last)) * ((offLine - FlipMeters) / offLine);
            var blended = (last * PlaneSettleMeters) + toward;
            return blended.LengthSquared() > 1e-10f ? Vector3.Normalize(blended) : last;
        }

        /// <summary>
        /// The side a new press bulges to: the way the blade already hangs (its set plus gravity sag, PER-52; the set alone,
        /// away from the grip's up, for a straight blade), unless the point was moving sideways as it landed: then the
        /// blade drags behind the hand and bulges the way it moved. An axially loaded slender blade buckles toward the
        /// curve it already has. If that side leans into the surface the point lands on (more than
        /// <see cref="MirrorLean"/>: a shallow press on the floor, a glancing one on the Dummy), it is mirrored out of it,
        /// so the blade starts on its free side instead of bulging into the surface and snapping over (PER-69).
        /// </summary>
        private static Vector3 FindStartingBow(Vector3 bladeDirection, Vector3 gripUp, Vector3 restBend, Vector3 tipStep, Vector3 surfaceNormal)
        {
            var restAcross = restBend - (Vector3.Dot(restBend, bladeDirection) * bladeDirection);
            var set = restAcross.LengthSquared() > 1e-8f
                ? Vector3.Normalize(restAcross)
                : -FindPerpendicularDirection(gripUp, bladeDirection);
            var sideways = tipStep - (Vector3.Dot(tipStep, bladeDirection) * bladeDirection);
            var bow = FindPerpendicularDirection((set * SetBiasMeters) + sideways, bladeDirection);

            var outward = surfaceNormal - (Vector3.Dot(surfaceNormal, bladeDirection) * bladeDirection);
            var into = -Vector3.Dot(bow, surfaceNormal);
            if (into > MirrorLean && outward.LengthSquared() > 1e-8f)
            {
                var mirrorAxis = Vector3.Normalize(outward);
                bow -= 2.0f * Vector3.Dot(bow, mirrorAxis) * mirrorAxis;
            }

            return bow;
        }

        /// <summary>
        /// <paramref name="bow"/> turned with the hand since the last step: the same direction relative to the blade and
        /// the grip's up, as the blade is clamped in the grip.
        /// </summary>
        private Vector3 TurnWithGrip(Vector3 bow, Vector3 bladeDirection, Vector3 gripUp)
        {
            var lastUp = FindPerpendicularDirection(_lastGripUp, _lastBladeDirection);
            var lastSide = Vector3.Cross(_lastBladeDirection, lastUp);
            var up = FindPerpendicularDirection(gripUp, bladeDirection);
            var side = Vector3.Cross(bladeDirection, up);
            _lastBladeDirection = bladeDirection;
            _lastGripUp = gripUp;
            return FindPerpendicularDirection((Vector3.Dot(bow, lastUp) * up) + (Vector3.Dot(bow, lastSide) * side), bladeDirection);
        }

        /// <summary>The largest sideways offset of a centerline from the line between its ends.</summary>
        private static float FindBowMeters(Vector3[] samples)
        {
            var guard = samples[0];
            var line = samples[DrawnBlade.SampleCount] - guard;
            var lineLength = line.Length();
            var largest = 0.0f;
            foreach (var sample in samples)
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

        /// <summary>What a slide of the point is tested for.</summary>
        private enum SlideTest
        {
            /// <summary>Friction: it stops where the push is held, or short of where the blade cannot be drawn.</summary>
            Friction,

            /// <summary>A drag: it stops at the first spot a bent blade from the hand reaches.</summary>
            Reach,
        }

        /// <summary>What the friction finds for the point slid to one spot.</summary>
        private enum SlideState
        {
            /// <summary>The push still leans out of the friction cone: it slides on.</summary>
            Sliding,

            /// <summary>The push is inside the friction cone: the point stops.</summary>
            Held,

            /// <summary>No bent blade from the hand reaches this spot (on its side, clear of the bodies).</summary>
            OutOfReach,
        }
    }
}
