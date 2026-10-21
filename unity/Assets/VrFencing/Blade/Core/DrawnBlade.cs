using System;
using System.Collections.Generic;
using System.Numerics;

namespace VrFencing.Blade
{
    /// <summary>
    /// The blade as drawn (visual only; Touch detection uses the line from the guard to the real point), as
    /// <see cref="SampleCount"/> + 1 centerline points from the guard to the point. It hangs in its rest shape, the set
    /// plus gravity sag (<see cref="BladeDroop"/>, PER-52). It is always one smooth shape, and never passes through a body
    /// (PER-48, PER-49):
    /// <list type="bullet">
    /// <item>the point pins on the near-side surface it meets and the blade bends in one smooth curve as the hand pushes
    /// on (<see cref="BladeContactBend"/>);</item>
    /// <item>otherwise it flexes on fast moves (<see cref="BladeFlex"/>);</item>
    /// <item>its side pressed on a round body bends round it, the hilt still in the hand (<see cref="BladeSideContact"/>,
    /// PER-69), and lets go springing back when the hand takes it off or it slides off the body's end;</item>
    /// <item>a shape that would pass into or through a body (the flat swept into the Dummy's arm) is not taken: the drawn
    /// blade stops at the closest allowed pose on the way back to where it was drawn, or holds there, and lags the hand
    /// (<see cref="IsDecoupled"/>) until the hand's own pose is reachable again: back, or around;</item>
    /// <item>when a pinned point lets go, or the drawn blade catches up with the hand, it springs back ringing a few
    /// decaying cycles, like a real épée;</item>
    /// <item>it never gets stuck (PER-121). Pressed against the Armed Dummy's blade or bell past what it can bend, it
    /// slides off to the near side: the hand's pose moved off the steel (<see cref="BladeObstacle.FindNearSide"/>), when
    /// that lags the hand less than holding. And it is on a leash: once it trails the hand by more than
    /// <see cref="LeashMeters"/> (<see cref="LagMeters"/>) or has lagged for more than <see cref="LeashSeconds"/>, it pops
    /// back to the hand, springing, if the hand's blade is clear of everything where it is and not across the Dummy's
    /// blade on its far side (<see cref="BladeObstacle.IsAcrossOnFarSide"/>). Otherwise it holds: a body and the Dummy's
    /// blade stay solid. Visual only, like all of this: Touches, clashes and Parries are judged on the hand's blade, so a
    /// pop never scores.</item>
    /// </list>
    /// The bodies are <see cref="BladeObstacle"/>s: the Dummy's capsules, the floor and the other blade's bell guard.
    /// The other blade itself is not one (PER-122): the contact solver (<see cref="BladeContactSolver"/>) gives this
    /// blade its chord and rest shape against it, turned in the grip and bowed, the hilt in the hand, so the lag, the
    /// ghost hilt and the leash above only ever come from bodies, the floor and the bell. Positions in any one frame (the
    /// Strip frame in the app), meters, time in seconds.
    /// </summary>
    public sealed class DrawnBlade
    {
        /// <summary>Pieces the drawn blade is made of; the centerline has one more point than this.</summary>
        public const int SampleCount = 24;

        /// <summary>The drawn blade's points keep this far out of a body's surface: about the blade's half-width.</summary>
        public const float BladeRadiusMeters = 0.004f;

        /// <summary>
        /// The leash (PER-121): past this lag (<see cref="LagMeters"/>) the drawn hilt pops back to the hand. 8 cm, or a
        /// 15 degree turn (<see cref="DrawnShape.TurnLeverMeters"/>): a hand that far off is not pressing, it has moved on.
        /// The default of the "Leash distance" Setting.
        /// </summary>
        public const float LeashMeters = 0.08f;

        /// <summary>The leash in time: a drawn hilt lagging the hand this long pops back to it. The "Leash time" Setting's default.</summary>
        public const float LeashSeconds = 0.25f;

        /// <summary>
        /// The ghost hilt shows only past this lag (PER-121): smaller lags (a press at the bend limit, a blade held a
        /// little off a body) flickered it on. The "Ghost from" Setting's default. The logic's own threshold for lagging
        /// stays <see cref="DrawnShape.LagMeters"/>.
        /// </summary>
        public const float GhostLagMeters = 0.03f;

        // Sliding off the steel: the hand's pose is moved off it in steps this long, then the step that got clear is
        // halved this many times (under a millimeter).
        private const float SlipStepMeters = 0.01f;
        private const int SlipSearchSteps = 4;

        // A slide direction square to the hand's blade is only taken when at least this much of it (squared; about 17
        // degrees off the blade) is: nearer along the blade it has no clear way to go.
        private const float SquareMinimumSquared = 0.09f;

        private readonly BladeFlex _flex = new BladeFlex();
        private readonly BladeContactBend _contact = new BladeContactBend();
        private readonly BladeSideContact _side = new BladeSideContact();
        private readonly Vector3[] _samples = new Vector3[SampleCount + 1];
        private readonly Vector3[] _candidate = new Vector3[SampleCount + 1];
        private readonly Vector3[] _allowed = new Vector3[SampleCount + 1];
        private readonly Vector3[] _lifted = new Vector3[SampleCount + 1];
        private readonly DrawnShape.PoseTest _tryFreeShape;
        private bool _hasSamples;
        private bool _isFreeOffHand;
        private bool _isCandidateOnSide;
        private BladeSegment _drawnPose = new BladeSegment(Vector3.Zero, Vector3.UnitZ * BladePose.BladeLengthMeters);
        private IReadOnlyList<BladeObstacle> _bodies;
        private BladeDroop _droop;
        private float _leashMeters = LeashMeters;
        private float _leashSeconds = LeashSeconds;
        private float _ghostLagMeters = GhostLagMeters;
        private float _lagSeconds;

        public DrawnBlade()
        {
            _tryFreeShape = TryFreeShape;
        }

        /// <summary>The fast-move flex; its bend is zero while the point is pinned.</summary>
        public BladeFlex Flex => _flex;

        /// <summary>The pin against a body and the bend.</summary>
        public BladeContactBend Contact => _contact;

        /// <summary>The blade's side pressed on a body, while <see cref="IsOnSide"/>.</summary>
        public BladeSideContact Side => _side;

        /// <summary>
        /// True while the drawn blade is bent round a body its side presses (<see cref="BladeSideContact"/>, PER-69): the
        /// hilt follows the hand, the blade lies against the body. Not a Touch.
        /// </summary>
        public bool IsOnSide { get; private set; }

        /// <summary>True once the blade has been drawn (a <see cref="Step"/> since the last <see cref="Reset"/>).</summary>
        public bool HasShape => _hasSamples;

        /// <summary>Where the drawn guard is: the hand's guard, unless <see cref="IsDecoupled"/>.</summary>
        public Vector3 GuardPosition => _samples[0];

        /// <summary>
        /// The drawn guard's line to the drawn point's rest position: the hand's chord (guard to the drooped point), unless
        /// <see cref="IsDecoupled"/>.
        /// </summary>
        public Vector3 GuardDirection => _drawnPose.Direction;

        /// <summary>Which way the drawn hilt points: along the forte, the rest shape's tangent at the guard (PER-52).</summary>
        public Vector3 HiltDirection => _droop.GetHiltDirection(GuardDirection);

        /// <summary>
        /// The drawn hilt lags the hand: pushed or turned further than the blade can bend, or the blade held off a body
        /// the hand went through. The logic's lag (<see cref="DrawnShape.LagMeters"/>); the ghost hilt shows past
        /// <see cref="IsGhostShown"/>'s larger one.
        /// </summary>
        public bool IsDecoupled => _contact.IsPinned ? _contact.IsDecoupled : _isFreeOffHand;

        /// <summary>
        /// How far the drawn hilt trails the hand after the last <see cref="Step"/> (<see cref="DrawnShape.FindLagMeters"/>),
        /// 0 while it follows: what the ghost hilt, the lag buzz and the leash go by.
        /// </summary>
        public float LagMeters { get; private set; }

        /// <summary>True while the ghost hilt shows the hand: the drawn hilt trails it by more than the ghost's threshold.</summary>
        public bool IsGhostShown => LagMeters > _ghostLagMeters;

        /// <summary>True when the last <see cref="Step"/> popped the drawn blade back to the hand on the leash (felt as a click).</summary>
        public bool HasPopped { get; private set; }

        /// <summary>
        /// Takes the feel Settings (PER-121): the leash's distance and time, and the lag past which the ghost hilt shows.
        /// The defaults are <see cref="LeashMeters"/>, <see cref="LeashSeconds"/> and <see cref="GhostLagMeters"/>.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The leash is not positive, or the ghost's threshold is negative.</exception>
        public void SetFeel(float leashMeters, float leashSeconds, float ghostLagMeters)
        {
            if (!(leashMeters > 0.0f))
            {
                throw new ArgumentOutOfRangeException(nameof(leashMeters), leashMeters, "The leash needs a length.");
            }

            if (!(leashSeconds > 0.0f))
            {
                throw new ArgumentOutOfRangeException(nameof(leashSeconds), leashSeconds, "The leash needs a time.");
            }

            if (!(ghostLagMeters >= 0.0f))
            {
                throw new ArgumentOutOfRangeException(nameof(ghostLagMeters), ghostLagMeters, "The ghost's threshold cannot be negative.");
            }

            _leashMeters = leashMeters;
            _leashSeconds = leashSeconds;
            _ghostLagMeters = ghostLagMeters;
        }

        /// <summary>Straight again at once, nothing pinned, e.g. after the blade or the Strip jumped.</summary>
        public void Reset()
        {
            _flex.Reset();
            _contact.Reset();
            _side.Reset();
            _hasSamples = false;
            _isFreeOffHand = false;
            IsOnSide = false;
            LagMeters = 0.0f;
            HasPopped = false;
            _lagSeconds = 0.0f;
        }

        /// <summary>The drawn centerline point <paramref name="index"/>, 0 at the guard to <see cref="SampleCount"/> at the point.</summary>
        public Vector3 GetSample(int index)
        {
            return _samples[index];
        }

        /// <summary>Moves to the blade's new pose.</summary>
        /// <param name="straightBlade">
        /// The guard to the real point: the chord of the rest shape (<see cref="BladeDroop.GetChord"/>), or the straight
        /// blade with no droop.
        /// </param>
        /// <param name="bodies">Every obstacle the drawn blade cannot pass, in the blade's frame, in the same order every step.</param>
        /// <param name="gripUp">The grip's up (the thumb's side); with no droop, the blade's set bulges a straight push the other way.</param>
        /// <param name="droop">
        /// The rest shape this step (PER-52): drawn on top of the chord, and the side a straight push bends to. The
        /// default is a straight blade.
        /// </param>
        /// <returns>Why a pinned point let go this step, or <see cref="PinRelease.None"/>.</returns>
        public PinRelease Step(BladeSegment straightBlade, IReadOnlyList<BladeObstacle> bodies, Vector3 gripUp, float deltaSeconds, BladeDroop droop = default)
        {
            if (bodies == null)
            {
                throw new ArgumentNullException(nameof(bodies));
            }

            HasPopped = false;
            var release = Draw(straightBlade, bodies, gripUp, deltaSeconds, droop);
            KeepOnLeash(straightBlade, bodies, deltaSeconds);
            return release;
        }

        /// <summary>Where the drawn centerline is at <paramref name="fraction"/> of the way from the guard (0) to the point (1).</summary>
        public Vector3 GetCenterlinePoint(float fraction)
        {
            var scaled = Math.Max(0.0f, Math.Min(1.0f, fraction)) * SampleCount;
            var index = Math.Min((int)scaled, SampleCount - 1);
            return Vector3.Lerp(_samples[index], _samples[index + 1], scaled - index);
        }

        /// <summary>The drawn blade for this step: pinned and bent, or free.</summary>
        private PinRelease Draw(BladeSegment straightBlade, IReadOnlyList<BladeObstacle> bodies, Vector3 gripUp, float deltaSeconds, BladeDroop droop)
        {
            _droop = droop;
            var wasPinned = _contact.IsPinned;
            var bowBefore = _contact.BowMeters;
            var bowDirectionBefore = _contact.BowDirection;
            var release = _contact.Step(straightBlade, bodies, gripUp, _hasSamples ? _samples : null, droop.TipDeflection);
            if (_contact.IsPinned)
            {
                // The pin holds the point; restart the flex so it does not carry stale motion into the release. The rest
                // curve rides on the pressed shape (zero at the guard and the pin), so the blade leaves the guard along the
                // forte and does not jump as the point lands or lets go.
                _flex.Reset();
                for (var index = 0; index <= SampleCount; index++)
                {
                    _samples[index] = _contact.GetSample(index) + droop.GetChordOffset((float)index / SampleCount);
                }

                _drawnPose = new BladeSegment(_contact.GuardPosition, _contact.GuardPosition + (_contact.GuardDirection * BladePose.BladeLengthMeters));
                _hasSamples = true;
                _isFreeOffHand = false;
                IsOnSide = false;
                return release;
            }

            if (wasPinned)
            {
                // Spring back from where the point was drawn, plus the bow, both limited by the flex.
                var heldOffset = _samples[SampleCount] - straightBlade.TipPosition;
                _flex.SpringBackFrom(straightBlade, heldOffset + (bowDirectionBefore * Math.Min(bowBefore, BladeFlex.MaxBendMeters)));
            }
            else
            {
                _flex.Step(straightBlade, deltaSeconds);
            }

            DrawFree(straightBlade, bodies);
            return release;
        }

        /// <summary>
        /// The free blade (nothing pinned): the hand's flexed blade if it is clear of every body and got there without
        /// crossing one; else the allowed pose closest to the hand on the way back to the last drawn pose, or the hand's
        /// pose slid off the steel it presses if that lags less (<see cref="TryFindSlip"/>); else it holds.
        /// </summary>
        private void DrawFree(BladeSegment straightBlade, IReadOnlyList<BladeObstacle> bodies)
        {
            if (!_hasSamples)
            {
                _drawnPose = straightBlade;
                FillFlexed(straightBlade, _samples);
                _hasSamples = true;
                return;
            }

            _bodies = bodies;
            var reached = DrawnShape.FindClosestAllowed(straightBlade, _drawnPose, _tryFreeShape);
            var isAtHand = reached == 0.0f;
            BladeSegment? pose = null;
            if (reached.HasValue)
            {
                pose = isAtHand ? straightBlade : DrawnShape.Interpolate(straightBlade, _drawnPose, reached.Value);
            }

            if (!isAtHand)
            {
                // The slide search builds its own shapes: the closest allowed one is kept aside in case it finds none.
                Array.Copy(_candidate, _allowed, _allowed.Length);
                var wasCandidateOnSide = _isCandidateOnSide;
                if (TryFindSlip(straightBlade, pose ?? _drawnPose, out var slid))
                {
                    pose = slid;
                }
                else
                {
                    Array.Copy(_allowed, _candidate, _candidate.Length);
                    _isCandidateOnSide = wasCandidateOnSide;
                }
            }

            _bodies = null;
            if (pose.HasValue)
            {
                var isLettingGoOfSide = IsOnSide && !_isCandidateOnSide;
                if ((_isFreeOffHand || isLettingGoOfSide) && isAtHand && !_isCandidateOnSide)
                {
                    // Caught up with the hand, or the side let go of the body: the blade springs from where it was drawn
                    // instead of jumping, if that is clear.
                    Array.Copy(_candidate, _allowed, _allowed.Length);
                    _flex.SpringBackFrom(straightBlade, _samples[SampleCount] - straightBlade.TipPosition);
                    FillFlexed(straightBlade, _candidate);
                    if (!DrawnShape.IsClear(_candidate, _samples, bodies, BladeRadiusMeters, 1, sweepsTip: true))
                    {
                        _flex.Reset();
                        FillFlexed(straightBlade, _candidate);
                        if (!DrawnShape.IsClear(_candidate, _samples, bodies, BladeRadiusMeters, 1, sweepsTip: true))
                        {
                            // Straight is not clear either (another blade in the way): the shape found allowed stands.
                            Array.Copy(_allowed, _candidate, _allowed.Length);
                        }
                    }
                }

                Array.Copy(_candidate, _samples, _samples.Length);
                _drawnPose = pose.Value;
                IsOnSide = _isCandidateOnSide;
            }

            _isFreeOffHand = DrawnShape.IsOffHand(_drawnPose.GuardPosition, _drawnPose.Direction, straightBlade);
        }

        /// <summary>
        /// Slides off the steel (PER-121): the drawn blade lying on the Dummy's blade or bell and unable to follow the
        /// hand takes the hand's pose moved off the steel, square to the hand's blade: to the steel's near side
        /// (<see cref="BladeObstacle.FindNearSide"/>), or, for a point pushed square onto it (the near side along the
        /// blade), round it, over or under (the side the drawn blade is on first). The steel is the nearest within the
        /// leash of the drawn blade. The least move that is clear and gets there clear, flexed, and not across the
        /// Dummy's blade on its far side. Only when that lags the hand less than <paramref name="held"/> and no more than
        /// the leash. Leaves its shape in the candidate.
        /// </summary>
        private bool TryFindSlip(BladeSegment hand, BladeSegment held, out BladeSegment slid)
        {
            slid = hand;
            if (!TryFindNearSteel(out var steel, out var drawnNear))
            {
                return false;
            }

            var most = Math.Min(_leashMeters, DrawnShape.FindLagMeters(held.GuardPosition, held.Direction, hand));
            var direction = hand.Direction;
            var nearSide = MakeSquare(steel.FindNearSide(drawnNear), direction);
            if (nearSide != Vector3.Zero)
            {
                // Across the steel: off it to the near side only. Over or under it would carry a beat or a press past
                // the Dummy's blade, which it has to meet.
                return TrySlipAlong(hand, nearSide, most, out slid);
            }

            // The point pushed square onto the steel (its near side is along the blade): round it, over or under.
            var round = MakeSquare(Vector3.Cross(direction, steel.FindSteelAxis()), direction);
            if (round == Vector3.Zero)
            {
                return false;
            }

            // The drawn blade's side of the hand's first: it slides on round the way it already is.
            var drawnMiddle = _samples[SampleCount / 2] - Vector3.Lerp(hand.GuardPosition, hand.TipPosition, 0.5f);
            var first = Vector3.Dot(round, drawnMiddle) >= 0.0f ? round : -round;
            return TrySlipAlong(hand, first, most, out slid) || TrySlipAlong(hand, -first, most, out slid);
        }

        /// <summary>
        /// The least move of <paramref name="hand"/> along <paramref name="direction"/>, short of <paramref name="most"/>,
        /// whose flexed shape is clear and got there clear (stepped, then bisected): <see cref="TryFindSlip"/>'s search.
        /// </summary>
        private bool TrySlipAlong(BladeSegment hand, Vector3 direction, float most, out BladeSegment slid)
        {
            slid = hand;
            var clearAt = -1.0f;
            var blockedAt = 0.0f;
            for (var offset = SlipStepMeters; offset < most; offset += SlipStepMeters)
            {
                var shifted = Shift(hand, direction, offset);
                if (IsAcrossSteelOnFarSide(shifted, _bodies))
                {
                    // Off the steel this way is through the Dummy's blade: no slide.
                    return false;
                }

                if (TrySlidShape(shifted, direction, offset))
                {
                    clearAt = offset;
                    break;
                }

                blockedAt = offset;
            }

            if (clearAt < 0.0f)
            {
                return false;
            }

            for (var step = 0; step < SlipSearchSteps; step++)
            {
                var middle = (blockedAt + clearAt) * 0.5f;
                if (TrySlidShape(Shift(hand, direction, middle), direction, middle))
                {
                    clearAt = middle;
                }
                else
                {
                    blockedAt = middle;
                }
            }

            slid = Shift(hand, direction, clearAt);
            TrySlidShape(slid, direction, clearAt);
            return true;
        }

        /// <summary>
        /// The flexed blade in <paramref name="pose"/> (the hand's moved <paramref name="offset"/> along
        /// <paramref name="direction"/>), built into the candidate: true when it is clear and got there clear, straight
        /// or in two legs: off the steel first (the drawn blade moved the same way), then on to it. A blade slides round
        /// the other's edge, it does not cut the corner through it.
        /// </summary>
        private bool TrySlidShape(BladeSegment pose, Vector3 direction, float offset)
        {
            if (TryFlexedShape(pose))
            {
                return true;
            }

            var lift = direction * offset;
            for (var index = 0; index <= SampleCount; index++)
            {
                _lifted[index] = _samples[index] + lift;
            }

            return DrawnShape.IsClear(_lifted, _samples, _bodies, BladeRadiusMeters, 1, sweepsTip: true)
                && DrawnShape.IsClear(_candidate, _lifted, _bodies, BladeRadiusMeters, 1, sweepsTip: true);
        }

        /// <summary>The steel (the Dummy's blade or bell) the drawn blade lies on, if any, and the drawn blade's point nearest it.</summary>
        private bool TryFindNearSteel(out BladeObstacle steel, out Vector3 drawnNear)
        {
            steel = default;
            drawnNear = Vector3.Zero;
            // Steel within the leash: the drawn blade slid round it stays off it by up to that much.
            var nearest = _leashMeters;
            var isFound = false;
            foreach (var body in _bodies)
            {
                if (!body.IsSteel)
                {
                    continue;
                }

                foreach (var sample in _samples)
                {
                    var distance = body.FindDistance(sample);
                    if (distance < nearest)
                    {
                        nearest = distance;
                        steel = body;
                        drawnNear = sample;
                        isFound = true;
                    }
                }
            }

            return isFound;
        }

        /// <summary><paramref name="vector"/> made square to <paramref name="axis"/> (unit) and unit length; zero when it lies (nearly) along it.</summary>
        private static Vector3 MakeSquare(Vector3 vector, Vector3 axis)
        {
            var square = vector - (Vector3.Dot(vector, axis) * axis);
            return square.LengthSquared() > SquareMinimumSquared ? Vector3.Normalize(square) : Vector3.Zero;
        }

        /// <summary>The flexed blade in <paramref name="pose"/>, built into the candidate: true when it is clear and got there clear.</summary>
        private bool TryFlexedShape(BladeSegment pose)
        {
            FillFlexed(pose, _candidate);
            _isCandidateOnSide = false;
            return DrawnShape.IsClear(_candidate, _samples, _bodies, BladeRadiusMeters, 1, sweepsTip: true);
        }

        private static BladeSegment Shift(BladeSegment pose, Vector3 direction, float meters)
        {
            var offset = direction * meters;
            return new BladeSegment(pose.GuardPosition + offset, pose.TipPosition + offset);
        }

        /// <summary>
        /// The leash (PER-121): measures the lag and, past <see cref="LeashMeters"/> or after <see cref="LeashSeconds"/> of
        /// lagging, pops the drawn blade back to the hand if it can (<see cref="TryPopToHand"/>).
        /// </summary>
        private void KeepOnLeash(BladeSegment hand, IReadOnlyList<BladeObstacle> bodies, float deltaSeconds)
        {
            if (!IsDecoupled)
            {
                LagMeters = 0.0f;
                _lagSeconds = 0.0f;
                return;
            }

            LagMeters = DrawnShape.FindLagMeters(GuardPosition, GuardDirection, hand);
            _lagSeconds += Math.Max(0.0f, deltaSeconds);
            if ((LagMeters <= _leashMeters && _lagSeconds <= _leashSeconds) || !TryPopToHand(hand, bodies))
            {
                return;
            }

            HasPopped = true;
            LagMeters = 0.0f;
            _lagSeconds = 0.0f;
        }

        /// <summary>
        /// Pops the drawn blade to the hand, springing from where it was drawn (as when it catches up): the hand's blade
        /// as it hangs, or bent round a body its side presses, where it is (no path: the path is what was blocked), clear
        /// of every body and not across the Dummy's blade on its far side. A pinned point lets go. False, and nothing
        /// changes, when there is no such shape: the hand is in a body or through the Dummy's blade, which stay solid.
        /// </summary>
        private bool TryPopToHand(BladeSegment hand, IReadOnlyList<BladeObstacle> bodies)
        {
            if (IsAcrossSteelOnFarSide(hand, bodies))
            {
                return false;
            }

            // The hand's blade as it hangs with no flex; else bent round a body its side presses.
            for (var index = 0; index <= SampleCount; index++)
            {
                var fraction = (float)index / SampleCount;
                _candidate[index] = Vector3.Lerp(hand.GuardPosition, hand.TipPosition, fraction) + _droop.GetChordOffset(fraction);
            }

            var isOnSide = false;
            if (!IsPoppable(_candidate, bodies))
            {
                isOnSide = _side.TryShape(hand, bodies, _samples, Vector3.Zero, _droop, _candidate) && IsPoppable(_candidate, bodies);
                if (!isOnSide)
                {
                    return false;
                }
            }

            var heldTip = _samples[SampleCount];
            _contact.LetGo(hand.TipPosition);
            _flex.Reset();
            if (!isOnSide)
            {
                // Springs from where the point was drawn, if that ringing shape is clear too; else straight at the hand.
                _side.Reset();
                _flex.SpringBackFrom(hand, heldTip - hand.TipPosition);
                FillFlexed(hand, _allowed);
                if (DrawnShape.IsClear(_allowed, null, bodies, BladeRadiusMeters, 1, sweepsTip: false))
                {
                    Array.Copy(_allowed, _candidate, _candidate.Length);
                }
                else
                {
                    _flex.Reset();
                }
            }

            Array.Copy(_candidate, _samples, _samples.Length);
            _drawnPose = hand;
            _hasSamples = true;
            _isFreeOffHand = false;
            IsOnSide = isOnSide;
            return true;
        }

        /// <summary>
        /// A shape the drawn blade may pop to: clear of everything where it is. Its way there is not looked at (that is
        /// what held it): it may pass a body the hand went right through (the flat through the Dummy's arm), which only
        /// shows, never scores. The steel it never passes (<see cref="IsAcrossSteelOnFarSide"/>, checked first).
        /// </summary>
        private static bool IsPoppable(Vector3[] shape, IReadOnlyList<BladeObstacle> bodies)
        {
            return DrawnShape.IsClear(shape, null, bodies, BladeRadiusMeters, 1, sweepsTip: false);
        }

        /// <summary>
        /// True when <paramref name="pose"/> lies across the Dummy's blade or bell on its far side from the drawn blade
        /// (<see cref="BladeObstacle.IsAcrossOnFarSide"/>): the drawn blade cannot be put there, the steel stays solid.
        /// </summary>
        private bool IsAcrossSteelOnFarSide(BladeSegment pose, IReadOnlyList<BladeObstacle> bodies)
        {
            foreach (var body in bodies)
            {
                if (!body.IsSteel)
                {
                    continue;
                }

                var drawnNear = _samples[0];
                var nearest = float.MaxValue;
                foreach (var sample in _samples)
                {
                    var distance = body.FindDistance(sample);
                    if (distance < nearest)
                    {
                        nearest = distance;
                        drawnNear = sample;
                    }
                }

                if (body.IsAcrossOnFarSide(pose.GuardPosition, pose.TipPosition, drawnNear))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The free blade in <paramref name="pose"/>: flexed if that is clear of every body; else bent round the body its
        /// side presses (<see cref="BladeSideContact"/>) if that is clear. Both must get there without crossing a body.
        /// </summary>
        private bool TryFreeShape(BladeSegment pose)
        {
            FillFlexed(pose, _candidate);
            _isCandidateOnSide = false;
            if (DrawnShape.IsClear(_candidate, _samples, _bodies, BladeRadiusMeters, 1, sweepsTip: true))
            {
                return true;
            }

            if (IsSlidingOffEnd(_candidate))
            {
                return true;
            }

            _isCandidateOnSide = _side.TryShape(pose, _bodies, _samples, _flex.Bend, _droop, _candidate);
            return _isCandidateOnSide && DrawnShape.IsClear(_candidate, _samples, _bodies, BladeRadiusMeters, 1, sweepsTip: true);
        }

        /// <summary>
        /// The side was pressed near the point and the hand went on past the body's end: the point went round the end,
        /// not through the body, so the blade may spring to the flexed <paramref name="handShape"/> though the straight
        /// path there crosses the body, if that shape is clear and the point's own path enters nothing.
        /// </summary>
        private bool IsSlidingOffEnd(Vector3[] handShape)
        {
            if (!IsOnSide || _side.ContactMeters < BladePose.BladeLengthMeters - BladeSideContact.SlideOffMeters)
            {
                return false;
            }

            foreach (var body in _bodies)
            {
                if (body.TryFindEntry(_samples[SampleCount], handShape[SampleCount], BladeRadiusMeters * 0.5f, out _))
                {
                    return false;
                }
            }

            return DrawnShape.IsClear(handShape, null, _bodies, BladeRadiusMeters, 1, sweepsTip: true);
        }

        private void FillFlexed(BladeSegment pose, Vector3[] samples)
        {
            for (var index = 0; index <= SampleCount; index++)
            {
                var fraction = (float)index / SampleCount;
                samples[index] = _flex.GetCenterlinePoint(pose, fraction) + _droop.GetChordOffset(fraction);
            }
        }
    }
}
