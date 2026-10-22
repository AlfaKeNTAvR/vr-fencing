using System;
using System.Numerics;
using VrFencing.Settings;

namespace VrFencing.Blade
{
    /// <summary>One blade as the contact solver takes it: where the hand holds it, and how readily it gives (PER-122).</summary>
    public readonly struct BladeHold
    {
        /// <param name="chord">The blade from the hand: its guard to its real (drooped) point.</param>
        /// <param name="droop">Its rest shape on that chord.</param>
        /// <param name="gripStiffness">How hard the grip resists the blade turning in it, N m per radian.</param>
        /// <param name="bendStiffness">The blade's bending stiffness EI, N m^2 (<see cref="BladeContactSolver.GetBendStiffness"/>).</param>
        public BladeHold(BladeSegment chord, BladeDroop droop, float gripStiffness, float bendStiffness)
        {
            if (!(gripStiffness > 0.0f))
            {
                throw new ArgumentOutOfRangeException(nameof(gripStiffness), gripStiffness, "A grip needs some stiffness.");
            }

            if (!(bendStiffness > 0.0f))
            {
                throw new ArgumentOutOfRangeException(nameof(bendStiffness), bendStiffness, "A blade needs some stiffness.");
            }

            Chord = chord;
            Droop = droop;
            GripStiffness = gripStiffness;
            BendStiffness = bendStiffness;
        }

        public BladeSegment Chord { get; }

        public BladeDroop Droop { get; }

        public float GripStiffness { get; }

        public float BendStiffness { get; }
    }

    /// <summary>
    /// What the contact solver gives one blade (PER-122): the blade to draw, turned in the grip about the hand and bowed,
    /// its guard always where the hand holds it. Fed to <see cref="DrawnBlade.Step"/> as its chord and rest shape.
    /// </summary>
    public readonly struct BladeGive
    {
        public BladeGive(BladeSegment chord, BladeDroop droop, float turnDegrees, float bowMeters)
        {
            Chord = chord;
            Droop = droop;
            TurnDegrees = turnDegrees;
            BowMeters = bowMeters;
        }

        /// <summary>The drawn guard (the hand's) to the drawn point.</summary>
        public BladeSegment Chord { get; }

        /// <summary>The rest shape on <see cref="Chord"/>, with the bow on top.</summary>
        public BladeDroop Droop { get; }

        /// <summary>How far the blade turned in the grip, degrees.</summary>
        public float TurnDegrees { get; }

        /// <summary>How far the blade bowed where it is pressed.</summary>
        public float BowMeters { get; }

        /// <summary>No give: the blade as the hand holds it.</summary>
        public static BladeGive None(BladeHold hold)
        {
            return new BladeGive(hold.Chord, hold.Droop, 0.0f, 0.0f);
        }
    }

    /// <summary>
    /// Two blades in contact, solved the same way for both (PER-122, Research 3 on the ticket: Fletcher's spring sword
    /// without the physics engine). The inputs are the two hands' blades and how each gives; the outputs are the two
    /// blades to draw. No hand is ever moved: each blade only turns in its grip about the hand and bows, so its hilt stays
    /// in the hand by construction, and the same rule serves the Armed Dummy now and a real partner later.
    /// <list type="bullet">
    /// <item><b>Side.</b> A small memory of which side of blade B blade A is on (<see cref="Side"/>), from the geometry
    /// while they are apart and held while they touch or nearly touch (<see cref="EngagedMeters"/>). A hand that went
    /// across between two frames, at any speed and also with the blades nearly parallel (a bind, a glide), is pressing,
    /// not through: the overlap is measured along the held side.</item>
    /// <item><b>Split.</b> The overlap at the deepest spot is shared, in one closed-form step, by four give stages in
    /// proportion to each one's compliance there: each grip turning about its hand (a soft angular spring, compliance
    /// r^2 / k, at most <see cref="GripTurnMaxDegrees"/>) and each blade bowing (compliance r^3 / 3EI, at most the blade's
    /// curvature limit). Forte on foible, the foible gives; foible on forte, the foible gives too. A stage at its limit
    /// passes its share on to the others; a second look takes what is left along a long bind.</item>
    /// <item><b>Release.</b> When every stage is at its limit, the weaker blade (the more compliant at the contact)
    /// slides off toward its point: it turns aside in the grip, square to the press, as the press goes on past the
    /// limits, until the other blade is past its point, and any overlap left meanwhile turns it further away. Then the
    /// side follows the hands and the blade swings back to the hand over <see cref="ReleaseSeconds"/>. It never holds and
    /// never jumps through.</item>
    /// </list>
    /// Deterministic: the same inputs and memory give the same outputs, and swapping the two blades mirrors them.
    /// Positions in any one frame (the Strip frame in the app), meters, newtons, seconds.
    /// </summary>
    public sealed class BladeContactSolver
    {
        /// <summary>
        /// The most a blade turns in the grip from a press (Zenner and Kruger 2019: offsets up to about 4.5 degrees go
        /// unnoticed): about 8 cm at the point. The bow carries the rest.
        /// </summary>
        public const float GripTurnMaxDegrees = 5.0f;

        /// <summary>
        /// The Fencer's grip's angular stiffness, N m per radian: the Armed Dummy's "Medium" hand. Soft enough that a
        /// press near the guard turns the blade a little before the forte bows (Fletcher's sword had 2000 for a rigid
        /// 1.8 kg blade; an épée's forte is far stiffer than its foible, so the grip gives first only near the guard).
        /// </summary>
        public const float FencerGripStiffness = 50.0f;

        /// <summary>
        /// An épée blade's bending stiffness EI, N m^2, for the Normal Blade flex: the FIE flexibility test (200 g, 3 cm
        /// from the point, held 70 cm from the point, bends it 4.5 to 5.5 cm) gives about 4.
        /// </summary>
        public const float NormalBendStiffness = 4.0f;

        /// <summary>
        /// The most a blade bows where it is pressed, whatever its curvature limit allows: a foible pressed 8 cm aside
        /// already looks like a hard bind (its point then moves up to about 20 cm). Past it the press goes to the grips,
        /// then the weaker blade slides off.
        /// </summary>
        public const float MaxBowMeters = 0.08f;

        /// <summary>Centerline to centerline at which the two drawn blades touch: both half-widths.</summary>
        public const float ContactMeters = 2.0f * DrawnBlade.BladeRadiusMeters;

        /// <summary>The side is held while the blades are this close to touching, or one is behind the other.</summary>
        public const float EngagedMeters = 0.02f;

        /// <summary>
        /// Past every limit, the press needs this much more for the weaker blade to slide all the way off its point; a
        /// press part of the way turns it part of the way.
        /// </summary>
        public const float ReleaseSpanMeters = 0.04f;

        /// <summary>A released blade swings back to the hand over this long (on the side the hands are now on).</summary>
        public const float ReleaseSeconds = 0.2f;

        /// <summary>The most a sliding-off blade turns aside in the grip: as far as lying along the other blade.</summary>
        public const float MaxReleaseDegrees = 90.0f;

        /// <summary>Each blade is taken as this many straight pieces of its drawn curve (a few millimeters off it at most).</summary>
        public const int PieceCount = 12;

        // A second look after the first split takes the overlap left along a long bind.
        private const int Passes = 2;

        // Pieces crossing at more than this sine (about 10 degrees) have one crossing whose normal is the side.
        private const float CrossingSine = 0.17f;

        // The crossing normal is only taken when it agrees with the held side at least this much (as a cosine).
        private const float SideAgreement = 0.3f;

        // A blade found behind the other counts as pushed through it within this share of its depth sideways, so a
        // fast move that also slips sideways is still caught; further sideways it went round.
        private const float BehindSlack = 0.5f;

        // The side is also held within this many frames' worth of the blades' last move against each other: a beat at
        // 3 m/s moves 4 cm a frame, twice the engaged band.
        private const float MotionReach = 1.5f;

        // At most this much wider: a jump (a blade appearing far off) is not a move, and must not keep a stale side.
        private const float MaxMotionBandMeters = 0.07f;

        // Overlap left after the split smaller than this is not looked at again.
        private const float SettledMeters = 0.0005f;

        // A held side follows the blades round each other only while the drawn blades are at least this far apart
        // along it, and the turn is less than 60 degrees in one frame.
        private const float FollowAgreement = 0.5f;

        // Each frame the held side moves this share of the way toward where the drawn blades are apart.
        private const float FollowShare = 0.25f;

        // The turn that clears a sliding blade is found to about a fifth of a degree by halving this many times.
        private const int ReleaseSearchSteps = 9;

        // Two bow loads closer than this share of the blade are one.
        private const float SameLoadFraction = 0.05f;

        private static readonly float GripTurnMaxRadians = GripTurnMaxDegrees * (float)Math.PI / 180.0f;
        private static readonly float MaxReleaseRadians = MaxReleaseDegrees * (float)Math.PI / 180.0f;

        private readonly Vector3[] _shapeA = new Vector3[PieceCount + 1];
        private readonly Vector3[] _shapeB = new Vector3[PieceCount + 1];
        private readonly Vector3[] _turned = new Vector3[PieceCount + 1];
        private readonly Vector3[] _flatOther = new Vector3[PieceCount + 1];
        private readonly Vector3[] _unitA = new Vector3[PieceCount];
        private readonly Vector3[] _unitB = new Vector3[PieceCount];
        private readonly Vector3[] _lastShapeA = new Vector3[PieceCount + 1];
        private readonly Vector3[] _lastShapeB = new Vector3[PieceCount + 1];
        private bool _hasLastShapes;
        private float _engagedMeters = EngagedMeters;
        private Hit _lastHit;
        private readonly Work _a = new Work();
        private readonly Work _b = new Work();
        private Vector3? _side;
        private int _slidingWeaker = -1;
        private int _returningBlade = -1;
        private Vector3 _returningAxis;
        private float _returningRadians;
        private float _returningSeconds;

        /// <summary>What blade A is drawn as after the last <see cref="Solve"/>.</summary>
        public BladeGive GiveA { get; private set; }

        /// <summary>What blade B is drawn as after the last <see cref="Solve"/>.</summary>
        public BladeGive GiveB { get; private set; }

        /// <summary>Which way from blade B blade A is, unit; none until both blades have been seen.</summary>
        public Vector3? Side => _side;

        /// <summary>True while the hands' blades touch or press into each other (the overlap was solved).</summary>
        public bool IsInContact { get; private set; }

        /// <summary>
        /// True while the blades touch, nearly touch (<see cref="EngagedMeters"/>) or one is behind the other: the side is
        /// held.
        /// </summary>
        public bool IsEngaged { get; private set; }

        /// <summary>
        /// How far the hands' blades press into each other at the contact, measured along the side (0 when apart): what
        /// the give took up. How hard the blades press, for the buzz and the sound.
        /// </summary>
        public float PressMeters { get; private set; }

        /// <summary>How close the hands' blades come, centerline to centerline, this frame.</summary>
        public float ClosestMeters { get; private set; } = float.MaxValue;

        /// <summary>
        /// True while the hands' blades are close enough that a whip of either drawn blade's flex
        /// (<see cref="BladeFlex.MaxBendMeters"/>) could carry it into the other, which the contact does not see: the
        /// callers hold both flexes still then. Also while engaged (pressed deep, the hands' blades may be far through).
        /// </summary>
        public bool IsNear => IsEngaged || ClosestMeters < ContactMeters + BladeFlex.MaxBendMeters;

        /// <summary>The force the press stands for (the spring stages' load), newtons; 0 when apart.</summary>
        public float PressNewtons { get; private set; }

        /// <summary>Where the blades meet, between the two drawn blades; meaningful while <see cref="IsInContact"/>.</summary>
        public Vector3 ContactPoint { get; private set; }

        /// <summary>Where on blade A they meet, 0 its guard to 1 its point.</summary>
        public float FractionA { get; private set; }

        /// <summary>Where on blade B they meet, 0 its guard to 1 its point.</summary>
        public float FractionB { get; private set; }

        /// <summary>The way the press pushes blade B (unit, square to the contact): blade A is pushed the other way.</summary>
        public Vector3 PushOnB { get; private set; }

        /// <summary>True while the weaker blade slides off or swings back after sliding off.</summary>
        public bool IsReleasing => _slidingWeaker >= 0 || _returningBlade >= 0;

        /// <summary>For the blade-contact log (PER-136): the blade sliding off now, 0 for A, 1 for B, -1 for none.</summary>
        public int SlidingBlade => _slidingWeaker;

        /// <summary>For the blade-contact log (PER-136): the blade swinging back after sliding off, 0 for A, 1 for B, -1 for none.</summary>
        public int ReturningBlade => _returningBlade;

        /// <summary>
        /// For the blade-contact log (PER-136): the give stages at their limit in this frame's first split, bit 0 A's
        /// grip turn, bit 1 A's bow, bit 2 B's grip turn, bit 3 B's bow; 0 when apart.
        /// </summary>
        public int SaturatedStages { get; private set; }

        private int _splitSaturated;

        /// <summary>The Blade flex Setting's bending stiffness: a stiffer blade sags less (<see cref="BladeDroop.GetSagMeters"/>).</summary>
        public static float GetBendStiffness(BladeFlexSoftness flex)
        {
            return NormalBendStiffness * BladeDroop.NormalSagMeters / BladeDroop.GetSagMeters(flex);
        }

        /// <summary>Forget the side and any release: the blades appeared or jumped.</summary>
        public void Reset()
        {
            _side = null;
            _hasLastShapes = false;
            _slidingWeaker = -1;
            _returningBlade = -1;
            IsInContact = false;
            IsEngaged = false;
            PressMeters = 0.0f;
            PressNewtons = 0.0f;
        }

        /// <summary>Solves this frame's contact of the two hands' blades into <see cref="GiveA"/> and <see cref="GiveB"/>.</summary>
        public void Solve(BladeHold a, BladeHold b, float deltaSeconds)
        {
            _a.Start(a);
            _b.Start(b);
            StepReturn(Math.Max(0.0f, deltaSeconds));

            FillShape(_a, _shapeA, _unitA);
            FillShape(_b, _shapeB, _unitB);

            // Also keeps this frame's hands' blades (the rest shapes) for the side's update.
            _engagedMeters = EngagedMeters + Math.Min(MaxMotionBandMeters, MotionReach * FindRelativeMotion());
            ClosestMeters = float.MaxValue;
            if (!_side.HasValue)
            {
                _side = FindGeometricSide(out var closest);
                ClosestMeters = closest;
            }

            var side = _side.Value;

            IsInContact = false;
            PressMeters = 0.0f;
            PressNewtons = 0.0f;
            SaturatedStages = 0;
            var first = FindDeepest(_shapeA, _shapeB, _unitA, _unitB, side, _engagedMeters);
            _lastHit = first;
            IsEngaged = first.IsFound;
            var isReleased = false;
            if (first.IsFound && first.Overlap > 0.0f)
            {
                IsInContact = true;
                PressMeters = first.Overlap;
                ContactPoint = (first.OnA + first.OnB) * 0.5f;
                FractionA = first.FractionA;
                FractionB = first.FractionB;
                PushOnB = -first.Normal;
                isReleased = SolveContact(first);
            }
            else
            {
                _slidingWeaker = -1;
            }

            GiveA = _a.ToGive();
            GiveB = _b.ToGive();
            UpdateSide(isReleased);
        }

        /// <summary>
        /// How far the two hands' blades moved against each other since the last solve (the most any spot of one moved
        /// relative to the same spot of the other), and keeps this frame's for the next: a fast beat crosses the engaged
        /// band in one frame, so the band widens with it.
        /// </summary>
        private float FindRelativeMotion()
        {
            var most = 0.0f;
            for (var index = 0; index <= PieceCount; index++)
            {
                if (_hasLastShapes)
                {
                    most = Math.Max(most, ((_shapeA[index] - _lastShapeA[index]) - (_shapeB[index] - _lastShapeB[index])).Length());
                }

                _lastShapeA[index] = _shapeA[index];
                _lastShapeB[index] = _shapeB[index];
            }

            _hasLastShapes = true;
            return most;
        }

        /// <summary>
        /// Shares the overlap of <paramref name="first"/> out, then looks again for what is left; past every limit the
        /// weaker blade slides off. True when it slid all the way off this frame.
        /// </summary>
        private bool SolveContact(Hit first)
        {
            var excess = Split(first, out var newtons);
            PressNewtons = newtons;
            SaturatedStages = _splitSaturated;
            var isReleased = false;
            var weaker = -1;
            if (excess > 0.0f)
            {
                weaker = _slidingWeaker >= 0 ? _slidingWeaker : FindWeaker(first);
                _slidingWeaker = weaker;
                isReleased = SlideOff(weaker, first, excess);
            }
            else
            {
                _slidingWeaker = -1;
            }

            for (var pass = 1; pass < Passes; pass++)
            {
                FillShape(_a, _shapeA, _unitA);
                FillShape(_b, _shapeB, _unitB);
                var hit = FindDeepest(_shapeA, _shapeB, _unitA, _unitB, _side.Value, _engagedMeters);
                _lastHit = hit;
                if (!hit.IsFound || hit.Overlap <= SettledMeters)
                {
                    break;
                }

                var left = Split(hit, out _);
                if (left > 0.0f)
                {
                    // Nothing else gives here: the weaker blade turns on past its grip's limit, so it never goes through.
                    TurnAway(weaker >= 0 ? weaker : FindWeaker(hit), hit, left);
                }
            }

            return isReleased;
        }

        /// <summary>
        /// Shares <paramref name="hit"/>'s overlap out over the four stages by compliance, each up to its limit (a stage
        /// at its limit hands its share on). Returns the overlap no stage could take; <paramref name="newtons"/> is the
        /// stages' load.
        /// </summary>
        private float Split(Hit hit, out float newtons)
        {
            Span<Stage> stages = stackalloc Stage[4];
            stages[0] = _a.FindTurnStage(hit.OnA, hit.FractionA, hit.Normal);
            stages[1] = _a.FindBowStage(hit.FractionA, hit.Normal);
            stages[2] = _b.FindTurnStage(hit.OnB, hit.FractionB, -hit.Normal);
            stages[3] = _b.FindBowStage(hit.FractionB, -hit.Normal);

            // Water-filling: the load rises until each stage takes compliance times load or its limit.
            var remaining = hit.Overlap;
            Span<bool> isFull = stackalloc bool[4];
            Span<float> taken = stackalloc float[4];
            newtons = 0.0f;
            for (var round = 0; round < stages.Length; round++)
            {
                var compliance = 0.0f;
                for (var index = 0; index < stages.Length; index++)
                {
                    compliance += isFull[index] ? 0.0f : stages[index].Compliance;
                }

                if (compliance <= 0.0f)
                {
                    break;
                }

                var load = remaining / compliance;
                var isAnyFull = false;
                for (var index = 0; index < stages.Length; index++)
                {
                    if (!isFull[index] && stages[index].Compliance * load >= stages[index].Limit)
                    {
                        isFull[index] = true;
                        taken[index] = stages[index].Limit;
                        remaining -= stages[index].Limit;
                        newtons = Math.Max(newtons, stages[index].Compliance > 0.0f ? stages[index].Limit / stages[index].Compliance : 0.0f);
                        isAnyFull = true;
                    }
                }

                if (isAnyFull)
                {
                    continue;
                }

                for (var index = 0; index < stages.Length; index++)
                {
                    taken[index] = isFull[index] ? taken[index] : stages[index].Compliance * load;
                }

                newtons = Math.Max(newtons, load);
                remaining = 0.0f;
                break;
            }

            _splitSaturated = (isFull[0] ? 1 : 0) | (isFull[1] ? 2 : 0) | (isFull[2] ? 4 : 0) | (isFull[3] ? 8 : 0);
            _a.Turn(hit.OnA, hit.Normal, taken[0]);
            _a.AddBow(hit.FractionA, hit.Normal, taken[1]);
            _b.Turn(hit.OnB, -hit.Normal, taken[2]);
            _b.AddBow(hit.FractionB, -hit.Normal, taken[3]);
            return Math.Max(0.0f, remaining);
        }

        /// <summary>
        /// Past every limit by <paramref name="excess"/>: the weaker blade turns aside, square to the press, toward the
        /// other blade's line, so the contact slides toward its point, by the share of the turn that clears it that the
        /// excess is of <see cref="ReleaseSpanMeters"/>. True once it is all the way off: the release then swings it back.
        /// </summary>
        private bool SlideOff(int weaker, Hit hit, float excess)
        {
            FillShape(_a, _shapeA, _unitA);
            FillShape(_b, _shapeB, _unitB);
            var normal = hit.Normal;
            var sliding = weaker == 0 ? _shapeA : _shapeB;
            var other = weaker == 0 ? _shapeB : _shapeA;
            var work = weaker == 0 ? _a : _b;
            var otherPiece = weaker == 0 ? hit.PieceB : hit.PieceA;
            var axis = FindSlideAxis(sliding, other, otherPiece, normal);
            var clearRadians = FindClearingTurn(sliding, other, work.Guard, axis, normal);
            var share = Math.Min(1.0f, excess / ReleaseSpanMeters);
            var radians = clearRadians * share * share * (3.0f - (2.0f * share));
            work.TurnAbout(axis, radians);
            if (share < 1.0f)
            {
                return false;
            }

            _returningBlade = weaker;
            _returningAxis = axis;
            _returningRadians = radians;
            _returningSeconds = 0.0f;
            _slidingWeaker = -1;
            return true;
        }

        /// <summary>The left-over overlap of <paramref name="hit"/> taken by the blade turning on in its grip past its limit.</summary>
        private void TurnAway(int blade, Hit hit, float overlap)
        {
            if (blade == 0)
            {
                _a.Turn(hit.OnA, hit.Normal, overlap, isLimited: false);
            }
            else
            {
                _b.Turn(hit.OnB, -hit.Normal, overlap, isLimited: false);
            }
        }

        /// <summary>The blade more compliant where they meet, the one that gives first: 0 for A, 1 for B (B on a tie).</summary>
        private int FindWeaker(Hit hit)
        {
            var a = _a.FindTurnStage(hit.OnA, hit.FractionA, hit.Normal).FreeCompliance + _a.FindBowStage(hit.FractionA, hit.Normal).FreeCompliance;
            var b = _b.FindTurnStage(hit.OnB, hit.FractionB, -hit.Normal).FreeCompliance + _b.FindBowStage(hit.FractionB, -hit.Normal).FreeCompliance;
            return a > b ? 0 : 1;
        }

        /// <summary>
        /// The axis (along <paramref name="normal"/>) a sliding blade turns about so it swings toward the other blade's
        /// line where they meet, which moves the contact out toward its point.
        /// </summary>
        private static Vector3 FindSlideAxis(Vector3[] sliding, Vector3[] other, int otherPiece, Vector3 normal)
        {
            var slidingDirection = Flatten(sliding[PieceCount] - sliding[0], normal);
            var otherDirection = Flatten(other[otherPiece + 1] - other[otherPiece], normal);
            if (Vector3.Dot(slidingDirection, otherDirection) < 0.0f)
            {
                otherDirection = -otherDirection;
            }

            var cross = Vector3.Cross(slidingDirection, otherDirection);
            var sign = Vector3.Dot(cross, normal);
            return sign < 0.0f ? -normal : normal;
        }

        /// <summary>
        /// The least turn of <paramref name="sliding"/> about <paramref name="axis"/> through <paramref name="guard"/> that
        /// puts it clear of <paramref name="other"/> seen along the press (no piece over another), at most
        /// <see cref="MaxReleaseDegrees"/>.
        /// </summary>
        private float FindClearingTurn(Vector3[] sliding, Vector3[] other, Vector3 guard, Vector3 axis, Vector3 normal)
        {
            if (IsClearSeenAlong(sliding, other, normal, guard, axis, 0.0f))
            {
                return 0.0f;
            }

            if (!IsClearSeenAlong(sliding, other, normal, guard, axis, MaxReleaseRadians))
            {
                return MaxReleaseRadians;
            }

            var blocked = 0.0f;
            var clear = MaxReleaseRadians;
            for (var step = 0; step < ReleaseSearchSteps; step++)
            {
                var middle = (blocked + clear) * 0.5f;
                if (IsClearSeenAlong(sliding, other, normal, guard, axis, middle))
                {
                    clear = middle;
                }
                else
                {
                    blocked = middle;
                }
            }

            return clear;
        }

        private bool IsClearSeenAlong(Vector3[] sliding, Vector3[] other, Vector3 normal, Vector3 guard, Vector3 axis, float radians)
        {
            var turn = Quaternion.CreateFromAxisAngle(axis, radians);
            for (var index = 0; index <= PieceCount; index++)
            {
                _turned[index] = Flatten(guard + Vector3.Transform(sliding[index] - guard, turn), normal);
                _flatOther[index] = Flatten(other[index], normal);
            }

            for (var piece = 0; piece < PieceCount; piece++)
            {
                var middle = (_turned[piece] + _turned[piece + 1]) * 0.5f;
                var halfLength = Vector3.Distance(_turned[piece], _turned[piece + 1]) * 0.5f;
                for (var otherPiece = 0; otherPiece < PieceCount; otherPiece++)
                {
                    // Too far apart to touch whatever their ends do: no need to look closer.
                    var otherMiddle = (_flatOther[otherPiece] + _flatOther[otherPiece + 1]) * 0.5f;
                    var reach = halfLength + (Vector3.Distance(_flatOther[otherPiece], _flatOther[otherPiece + 1]) * 0.5f) + ContactMeters;
                    if (Vector3.DistanceSquared(middle, otherMiddle) > reach * reach)
                    {
                        continue;
                    }

                    FindClosest(_turned[piece], _turned[piece + 1], _flatOther[otherPiece], _flatOther[otherPiece + 1], out var onSliding, out var onOther, out _, out _);
                    if (Vector3.DistanceSquared(onSliding, onOther) < ContactMeters * ContactMeters)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>A blade that slid off swings back to the hand: the turn left eases out over <see cref="ReleaseSeconds"/>.</summary>
        private void StepReturn(float deltaSeconds)
        {
            if (_returningBlade < 0)
            {
                return;
            }

            _returningSeconds += deltaSeconds;
            var fraction = Math.Min(1.0f, _returningSeconds / ReleaseSeconds);
            if (fraction >= 1.0f)
            {
                _returningBlade = -1;
                return;
            }

            var radians = _returningRadians * (1.0f - (fraction * fraction * (3.0f - (2.0f * fraction))));
            (_returningBlade == 0 ? _a : _b).TurnAbout(_returningAxis, radians);
        }

        /// <summary>
        /// After the solve: the side is held while engaged (following the drawn blades as they roll round each other),
        /// and taken from the hands while apart or once a blade slid off.
        /// </summary>
        private void UpdateSide(bool isReleased)
        {
            if (isReleased || !IsEngaged)
            {
                _side = FindGeometricSide(out var closest);
                ClosestMeters = closest;
                return;
            }

            // The last look at the blades this frame (after the give, or the hands' blades with none).
            var hit = _lastHit;
            if (!hit.IsFound)
            {
                return;
            }

            var apart = hit.OnA - hit.OnB;
            var distance = apart.Length();
            if (hit.IsCrossing && !hit.IsAtEnd && distance > ContactMeters * 0.5f && Vector3.Dot(apart, _side.Value) > FollowAgreement * distance)
            {
                // Crossing blades turn against each other: the side eases toward their crossing's normal, so a stray spot
                // does not swing it. Alongside (a bind) it holds as it is.
                _side = Vector3.Normalize(Vector3.Lerp(_side.Value, hit.Normal, FollowShare));
            }
        }

        /// <summary>
        /// The deepest overlap of the two shapes along <paramref name="side"/>: for each pair of pieces, the side there
        /// (the crossing's normal turned to <paramref name="side"/>, or <paramref name="side"/> square to the pieces when
        /// they lie alongside) and how far A is from B along it. A pair counts when A lies over B seen along that side,
        /// or behind it (pushed through); pieces lying alongside each other count anywhere behind, unless the closest
        /// spot is a blade's end (then they went round it). Found when some pair is within
        /// <paramref name="engagedMeters"/> of touching.
        /// </summary>
        private static Hit FindDeepest(Vector3[] shapeA, Vector3[] shapeB, Vector3[] unitA, Vector3[] unitB, Vector3 side, float engagedMeters)
        {
            var best = default(Hit);
            best.Overlap = -float.MaxValue;
            for (var pieceA = 0; pieceA < PieceCount; pieceA++)
            {
                for (var pieceB = 0; pieceB < PieceCount; pieceB++)
                {
                    var a0 = shapeA[pieceA];
                    var a1 = shapeA[pieceA + 1];
                    var b0 = shapeB[pieceB];
                    var b1 = shapeB[pieceB + 1];
                    FindClosest(a0, a1, b0, b1, out var onA, out var onB, out var alongA, out var alongB);
                    var apart = onA - onB;

                    // Nothing here can beat the deepest found so far, even pushed right through.
                    if (ContactMeters + apart.Length() <= best.Overlap)
                    {
                        continue;
                    }

                    var normal = FindPairNormal(unitA[pieceA], unitB[pieceB], side, out var isCrossing);
                    var signed = Vector3.Dot(apart, normal);

                    // On its own side, how far into each other they really are; behind, how far back it has to go.
                    var overlap = ContactMeters - (signed >= 0.0f ? apart.Length() : signed);
                    if (overlap < -engagedMeters || overlap <= best.Overlap)
                    {
                        continue;
                    }

                    // Seen along the side, the pieces must lie over each other (or within the slack of a push through).
                    // Alongside each other, a blade found behind the other went through it, wherever, unless they meet
                    // at a blade's end.
                    var isAtEnd = (pieceA == 0 && alongA <= 0.0f) || (pieceA == PieceCount - 1 && alongA >= 1.0f)
                        || (pieceB == 0 && alongB <= 0.0f) || (pieceB == PieceCount - 1 && alongB >= 1.0f);
                    var along = apart - (signed * normal);
                    var meanDirection = Vector3.Dot(unitA[pieceA], unitB[pieceB]) >= 0.0f ? unitA[pieceA] + unitB[pieceB] : unitA[pieceA] - unitB[pieceB];
                    meanDirection = meanDirection.LengthSquared() > 1e-12f ? Vector3.Normalize(meanDirection) : unitA[pieceA];
                    var isBehindAlongside = !isCrossing && signed < 0.0f && !isAtEnd && Math.Abs(Vector3.Dot(along, meanDirection)) <= ContactMeters;
                    FindClosest(Flatten(a0, normal), Flatten(a1, normal), Flatten(b0, normal), Flatten(b1, normal), out var flatA, out var flatB, out _, out _);
                    var sideways = Vector3.Distance(flatA, flatB);
                    if (!isBehindAlongside && sideways > ContactMeters + (BehindSlack * Math.Max(0.0f, -signed)))
                    {
                        continue;
                    }

                    best = new Hit
                    {
                        IsFound = true,
                        IsCrossing = isCrossing,
                        IsAtEnd = isAtEnd,
                        Overlap = overlap,
                        OnA = onA,
                        OnB = onB,
                        Normal = normal,
                        PieceA = pieceA,
                        PieceB = pieceB,
                        FractionA = (pieceA + alongA) / PieceCount,
                        FractionB = (pieceB + alongB) / PieceCount,
                    };
                }
            }

            return best;
        }

        /// <summary>
        /// The side at a pair of pieces: the normal of their crossing, turned to agree with <paramref name="side"/>, when
        /// they cross at a clear angle and it agrees well enough; else <paramref name="side"/> made square to both
        /// (to their mean direction).
        /// </summary>
        private static Vector3 FindPairNormal(Vector3 unitA, Vector3 unitB, Vector3 side, out bool isCrossing)
        {
            var cross = Vector3.Cross(unitA, unitB);
            var sine = cross.Length();
            isCrossing = false;
            if (sine > CrossingSine)
            {
                var crossing = cross / sine;
                var agreement = Vector3.Dot(crossing, side);
                if (Math.Abs(agreement) >= SideAgreement)
                {
                    isCrossing = true;
                    return agreement >= 0.0f ? crossing : -crossing;
                }
            }

            var mean = Vector3.Dot(unitA, unitB) >= 0.0f ? unitA + unitB : unitA - unitB;
            var square = MakeSquare(side, mean);
            return square == Vector3.Zero ? side : square;
        }

        /// <summary>
        /// Which way A is from B with no memory, from this frame's hands' blades (kept by <see cref="FindRelativeMotion"/>):
        /// where they come closest, the crossing's normal turned toward A, or the way from B to A.
        /// </summary>
        private Vector3 FindGeometricSide(out float closest)
        {
            var shapeA = _lastShapeA;
            var shapeB = _lastShapeB;
            closest = float.MaxValue;
            var side = Vector3.UnitY;
            for (var pieceA = 0; pieceA < PieceCount; pieceA++)
            {
                for (var pieceB = 0; pieceB < PieceCount; pieceB++)
                {
                    FindClosest(shapeA[pieceA], shapeA[pieceA + 1], shapeB[pieceB], shapeB[pieceB + 1], out var onA, out var onB, out _, out _);
                    var apart = onA - onB;
                    var distance = apart.Length();
                    if (distance >= closest)
                    {
                        continue;
                    }

                    closest = distance;
                    var cross = Vector3.Cross(Vector3.Normalize(shapeA[pieceA + 1] - shapeA[pieceA]), Vector3.Normalize(shapeB[pieceB + 1] - shapeB[pieceB]));
                    var sine = cross.Length();
                    if (sine > CrossingSine)
                    {
                        var crossing = cross / sine;
                        side = Vector3.Dot(crossing, apart) >= 0.0f ? crossing : -crossing;
                    }
                    else if (distance > 1e-6f)
                    {
                        side = apart / distance;
                    }
                }
            }

            return side;
        }

        /// <summary>The blade as given so far, as <see cref="PieceCount"/> + 1 points, and each piece's unit direction.</summary>
        private static void FillShape(Work work, Vector3[] shape, Vector3[] units)
        {
            var give = work.ToGive();
            for (var index = 0; index <= PieceCount; index++)
            {
                var fraction = (float)index / PieceCount;
                shape[index] = Vector3.Lerp(give.Chord.GuardPosition, give.Chord.TipPosition, fraction) + give.Droop.GetChordOffset(fraction);
            }

            for (var piece = 0; piece < PieceCount; piece++)
            {
                var along = shape[piece + 1] - shape[piece];
                units[piece] = along.LengthSquared() > 1e-12f ? Vector3.Normalize(along) : Vector3.UnitZ;
            }
        }

        /// <summary><paramref name="point"/> with its part along <paramref name="normal"/> taken away: seen along it.</summary>
        private static Vector3 Flatten(Vector3 point, Vector3 normal)
        {
            return point - (Vector3.Dot(point, normal) * normal);
        }

        /// <summary><paramref name="vector"/> made square to <paramref name="axis"/> and unit length; zero if it lies along it.</summary>
        private static Vector3 MakeSquare(Vector3 vector, Vector3 axis)
        {
            var axisLengthSquared = axis.LengthSquared();
            var square = axisLengthSquared > 1e-12f ? vector - (Vector3.Dot(vector, axis) / axisLengthSquared * axis) : vector;
            return square.LengthSquared() > 1e-12f ? Vector3.Normalize(square) : Vector3.Zero;
        }

        /// <summary>
        /// The closest points of segments A and B. Nearly parallel overlapping segments take the middle of their overlap,
        /// so the answer does not depend on which one is A.
        /// </summary>
        private static void FindClosest(Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1, out Vector3 onA, out Vector3 onB, out float alongA, out float alongB)
        {
            var directionA = a1 - a0;
            var directionB = b1 - b0;
            var offset = a0 - b0;
            var lengthSquaredA = Vector3.Dot(directionA, directionA);
            var lengthSquaredB = Vector3.Dot(directionB, directionB);
            var alongBOfOffset = Vector3.Dot(directionB, offset);
            if (lengthSquaredA < 1e-12f || lengthSquaredB < 1e-12f)
            {
                alongA = 0.0f;
                alongB = lengthSquaredB < 1e-12f ? 0.0f : Clamp01(alongBOfOffset / lengthSquaredB);
                onA = a0;
                onB = b0 + (directionB * alongB);
                return;
            }

            var alongAOfOffset = Vector3.Dot(directionA, offset);
            var mutual = Vector3.Dot(directionA, directionB);
            var denominator = (lengthSquaredA * lengthSquaredB) - (mutual * mutual);
            if (denominator < 1e-6f * lengthSquaredA * lengthSquaredB)
            {
                // Parallel: the middle of where B's ends fall on A, clamped to A.
                var start = Clamp01(-alongAOfOffset / lengthSquaredA);
                var end = Clamp01((mutual - alongAOfOffset) / lengthSquaredA);
                alongA = (start + end) * 0.5f;
            }
            else
            {
                alongA = Clamp01(((mutual * alongBOfOffset) - (alongAOfOffset * lengthSquaredB)) / denominator);
            }

            alongB = ((mutual * alongA) + alongBOfOffset) / lengthSquaredB;
            if (alongB < 0.0f || alongB > 1.0f)
            {
                alongB = Clamp01(alongB);
                alongA = Clamp01(((mutual * alongB) - alongAOfOffset) / lengthSquaredA);
            }

            onA = a0 + (directionA * alongA);
            onB = b0 + (directionB * alongB);
        }

        private static float Clamp01(float value)
        {
            return value < 0.0f ? 0.0f : (value > 1.0f ? 1.0f : value);
        }

        /// <summary>The deepest spot found: where, along which side, and how far into each other.</summary>
        private struct Hit
        {
            public bool IsFound;
            public bool IsCrossing;
            public bool IsAtEnd;
            public float Overlap;
            public Vector3 OnA;
            public Vector3 OnB;
            public Vector3 Normal;
            public int PieceA;
            public int PieceB;
            public float FractionA;
            public float FractionB;
        }

        /// <summary>One give stage at the contact: how far it moves the contact per newton, and how far it can.</summary>
        private struct Stage
        {
            public float Compliance;
            public float Limit;
            public float FreeCompliance;
        }

        /// <summary>One blade being solved: its hold and the turn and bow given so far.</summary>
        private sealed class Work
        {
            private BladeHold _hold;
            private Quaternion _turn = Quaternion.Identity;
            private float _limitedTurnRadians;
            private Vector3 _firstBow;
            private float _firstBowFraction = 1.0f;
            private Vector3 _secondBow;
            private float _secondBowFraction = 1.0f;
            private BladeGive _give;
            private bool _isGiveStale = true;

            /// <summary>Starts a frame's solve of this blade from its hold, with no give.</summary>
            public void Start(BladeHold hold)
            {
                _hold = hold;
                _turn = Quaternion.Identity;
                _limitedTurnRadians = 0.0f;
                _firstBow = Vector3.Zero;
                _firstBowFraction = 1.0f;
                _secondBow = Vector3.Zero;
                _secondBowFraction = 1.0f;
                _isGiveStale = true;
            }

            public Vector3 Guard => _hold.Chord.GuardPosition;

            private float Length => Math.Max(1e-3f, Vector3.Distance(_hold.Chord.GuardPosition, _hold.Chord.TipPosition));

            /// <summary>The grip's turn: lever square to the push, squared, over its stiffness; up to the turn left.</summary>
            public Stage FindTurnStage(Vector3 contact, float fraction, Vector3 push)
            {
                var lever = Vector3.Cross(contact - Guard, push).Length();
                var compliance = lever * lever / _hold.GripStiffness;
                return new Stage
                {
                    Compliance = compliance,
                    FreeCompliance = compliance,
                    Limit = lever * Math.Max(0.0f, GripTurnMaxRadians - _limitedTurnRadians),
                };
            }

            /// <summary>
            /// The blade's bow: a cantilever loaded at the contact, a^3 / 3EI, for the push's part square to the blade; up
            /// to the bow at which the forte reaches the curvature limit (3 d / a^2).
            /// </summary>
            public Stage FindBowStage(float fraction, Vector3 push)
            {
                var direction = Vector3.Normalize(ToGive().Chord.TipPosition - Guard);
                var along = Vector3.Dot(direction, push);
                var square = (float)Math.Sqrt(Math.Max(0.0f, 1.0f - (along * along)));
                var reach = Math.Max(fraction, 0.02f) * Length;
                var compliance = reach * reach * reach / (3.0f * _hold.BendStiffness) * square * square;
                var most = Math.Min(MaxBowMeters, BladeArc.MaxCurvaturePerMeter * reach * reach / 3.0f);
                var has = new ContactBow(_firstBow, _firstBowFraction, _secondBow, _secondBowFraction).GetDeflection(fraction).Length();
                return new Stage
                {
                    Compliance = compliance,
                    FreeCompliance = compliance,
                    Limit = Math.Max(0.0f, most - has) * square,
                };
            }

            /// <summary>
            /// Turns the blade about the hand so <paramref name="contact"/> moves <paramref name="meters"/> along
            /// <paramref name="push"/>; counted against the grip's limit unless <paramref name="isLimited"/> is false.
            /// </summary>
            public void Turn(Vector3 contact, Vector3 push, float meters, bool isLimited = true)
            {
                var lever = contact - Guard;
                var axis = Vector3.Cross(lever, push);
                var leverSquare = axis.Length();
                if (meters <= 0.0f || leverSquare < 1e-6f)
                {
                    return;
                }

                var radians = meters / leverSquare;
                if (isLimited)
                {
                    _limitedTurnRadians += radians;
                }

                TurnAbout(axis / leverSquare, radians);
            }

            public void TurnAbout(Vector3 axis, float radians)
            {
                if (radians == 0.0f)
                {
                    return;
                }

                _turn = Quaternion.Normalize(Quaternion.Concatenate(_turn, Quaternion.CreateFromAxisAngle(axis, radians)));
                _isGiveStale = true;
            }

            /// <summary>Bows the blade at <paramref name="fraction"/> so that spot moves <paramref name="meters"/> along <paramref name="push"/>.</summary>
            public void AddBow(float fraction, Vector3 push, float meters)
            {
                if (meters <= 0.0f)
                {
                    return;
                }

                var direction = Vector3.Normalize(ToGive().Chord.TipPosition - Guard);
                var square = push - (Vector3.Dot(push, direction) * direction);
                var squareLength = square.Length();
                if (squareLength < 1e-3f)
                {
                    return;
                }

                var bow = square / squareLength * (meters / squareLength);
                _isGiveStale = true;
                if (_firstBow == Vector3.Zero || Math.Abs(fraction - _firstBowFraction) < SameLoadFraction)
                {
                    _firstBowFraction = _firstBow == Vector3.Zero ? fraction : _firstBowFraction;
                    _firstBow += bow;
                }
                else if (_secondBow == Vector3.Zero || Math.Abs(fraction - _secondBowFraction) < SameLoadFraction)
                {
                    _secondBowFraction = _secondBow == Vector3.Zero ? fraction : _secondBowFraction;
                    _secondBow += bow;
                }
                else
                {
                    // Already two loads: the nearer takes it.
                    if (Math.Abs(fraction - _firstBowFraction) <= Math.Abs(fraction - _secondBowFraction))
                    {
                        _firstBow += bow;
                    }
                    else
                    {
                        _secondBow += bow;
                    }
                }
            }

            /// <summary>The blade to draw: the hand's chord turned about the guard and bowed, a blade's length long.</summary>
            public BladeGive ToGive()
            {
                if (_isGiveStale)
                {
                    _give = BuildGive();
                    _isGiveStale = false;
                }

                return _give;
            }

            private BladeGive BuildGive()
            {
                var guard = _hold.Chord.GuardPosition;
                var chord = _hold.Chord.TipPosition - guard;
                var length = chord.Length();
                if (_turn.IsIdentity && _firstBow == Vector3.Zero && _secondBow == Vector3.Zero)
                {
                    return BladeGive.None(_hold);
                }

                var bow = new ContactBow(_firstBow, _firstBowFraction, _secondBow, _secondBowFraction);
                var turned = Vector3.Transform(chord, _turn);
                var tip = guard + (Vector3.Normalize(turned + bow.TipDeflection) * length);
                var droop = _hold.Droop.WithContact(_turn, chord, tip - guard, bow);
                var turnDegrees = 2.0f * (float)Math.Acos(Math.Min(1.0f, Math.Abs(_turn.W))) * 180.0f / (float)Math.PI;
                return new BladeGive(new BladeSegment(guard, tip), droop, turnDegrees, Math.Max(_firstBow.Length(), _secondBow.Length()));
            }
        }
    }
}
