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
    /// not through: the overlap is measured along the held side. Fast hits (PER-138): the hands' move between frames is
    /// swept, so only spots that really went through each other count as pressed (a point swept over the other's forte
    /// went round it), a disengage round a point changes the side at once, and after a fast hit the side stays latched
    /// for <see cref="LatchSeconds"/> once apart, so a hand that swept past and comes straight back is still on its
    /// side.</item>
    /// <item><b>Split.</b> The overlap at the deepest spot is shared, in one closed-form step, by four give stages in
    /// proportion to each one's compliance there: each grip turning about its hand (a soft angular spring, compliance
    /// r^2 / k, at most <see cref="GripTurnMaxDegrees"/>) and each blade bowing (compliance r^3 / 3EI, at most the blade's
    /// curvature limit). Forte on foible, the foible gives; foible on forte, the foible gives too. A stage at its limit
    /// passes its share on to the others; two more looks take what is left along a long bind.</item>
    /// <item><b>Release.</b> When every stage is at its limit, the weaker blade (the more compliant at the contact)
    /// slides off toward its point: it turns aside in the grip, square to the press, as the press goes on past the
    /// limits (its point no faster than a flick, PER-147), until the other blade is past its point, and any overlap left
    /// meanwhile turns it further away. Then the side follows the hands and both blades swing back to their hands: their
    /// bows spring out, then their turns ease out over <see cref="ReleaseSeconds"/>. It never holds and never jumps.
    /// Within <see cref="LatchSeconds"/> of a fast hit it is not let go: it stays slid off while the hands stay pressed
    /// past, and swings back once they are not.</item>
    /// <item><b>Through (PER-147).</b> Hands pushed past every give, the slide and <see cref="ThroughMarginMeters"/> are
    /// through each other, latched or not: the press and the latch are dropped, the side is read from the hands, and both
    /// blades swing back to their hands from where they were drawn, visibly passing through each other. No turn in the
    /// grip ever passes <see cref="MaxReleaseDegrees"/>, and the press stays on the same spot of the blades unless another
    /// is clearly deeper, so the give does not swing to and fro between frames.</item>
    /// <item><b>Hilts.</b> Each blade's hilt, its bell guard (<see cref="BellGuard"/>) and the grip behind it
    /// (<see cref="GripRadiusMeters"/>), is solid for the other blade, held in its hand as the blade is (PER-122 fix):
    /// the other blade meets it by the same rules. A side memory per hilt, the overlap taken by that blade's grip turn
    /// and bow up to their limits, and past them it slides off round the bell's rim or the grip's end; a point pressed
    /// straight onto the bell's face skids along it to the rim. Neither hilt ever leaves its hand for this.</item>
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

        /// <summary>
        /// The through depth (PER-147): past all the give at the contact (the most this press has had: both grips' 5
        /// degrees and both bows) and the slide's span (<see cref="ReleaseSpanMeters"/>), the hands pressing this much
        /// further are through each other, latched or not: the press and the side latch are dropped, the side is read from
        /// the hands again, and both drawn blades swing back to their hands (<see cref="ReleaseSeconds"/>). Pressed square
        /// at the same spot of both blades, that is about 10 cm of press a tenth of the way from the guards, 20 cm at a
        /// fifth, 27 to 30 cm in the middle and 33 to 36 cm toward the points. A slow press slides off before it.
        /// </summary>
        public const float ThroughMarginMeters = 0.02f;

        /// <summary>
        /// A released blade swings back to the hand over this long (on the side the hands are now on): since PER-147 its
        /// bows spring out first (a few hundredths of a second) and then its turn in the grip eases out over this long,
        /// longer only for a turn so large its point would move faster than a sliding blade's.
        /// </summary>
        public const float ReleaseSeconds = 0.2f;

        /// <summary>
        /// A blade let go springs out of its bows in at least this long (PER-147): an épée's foible springs back in a few
        /// hundredths of a second, well before the turn in the grip has eased out, so a blade bowed toward the other never
        /// swings back through it while still bowed.
        /// </summary>
        private const float BowReleaseSeconds = 0.06f;

        /// <summary>
        /// ... and with its point moving at most this fast (PER-147: under 5 cm a frame at 72 Hz), so a bow pressed near
        /// the guard, whose point stands out up to five times as far, springs back over a little longer, up to
        /// <see cref="ReleaseSeconds"/>.
        /// </summary>
        private const float ReturnTipMetersPerSecond = 3.5f;

        /// <summary>
        /// A blade sliding off turns aside with its point moving at most this fast (PER-147: about 8 cm a frame at 72 Hz).
        /// Over the slide's 4 cm of press the turn moves the point about 13 times as far, so a hand pressing at 1 m/s would
        /// flick it 20 cm a frame; the press left over meanwhile turns it only as far as keeps the blades apart.
        /// </summary>
        private const float SlideTipMetersPerSecond = 7.0f;

        /// <summary>The most a sliding-off blade turns aside in the grip: as far as lying along the other blade.</summary>
        public const float MaxReleaseDegrees = 90.0f;

        /// <summary>Each blade is taken as this many straight pieces of its drawn curve (a few millimeters off it at most).</summary>
        public const int PieceCount = 12;

        /// <summary>
        /// The grip behind the bell, solid for the other blade: as thick as the drawn pommel (3.2 cm across; the grip
        /// itself is 2.4 cm), from the bell's back face to past the pommel.
        /// </summary>
        public const float GripRadiusMeters = 0.016f;

        /// <summary>The grip's length behind the bell's back face, the pommel included.</summary>
        public const float GripLengthMeters = 0.136f;

        // Each piece of a blade is looked at in this many steps against a hilt (about a centimeter each).
        private const int HiltStepsPerPiece = 8;

        // Overlap with a hilt left after its give smaller than this is not looked at again.
        private const float HiltSettledMeters = 0.0005f;

        // A sliding blade's way off a hilt is taken across the blade only when its line, seen along the press, is at
        // least this long a share of it: nearer end-on (a point on the bell's face), the way out is straight from the
        // hilt's axis.
        private const float EndOnShare = 0.3f;

        // A side within this cosine of the hilt's axis is the bell's face (front or back); nearer square, its rim.
        private const float BackShare = 0.5f;

        // Two more looks after the first split take the overlap left along a long bind.
        private const int Passes = 3;

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

        // Another spot of the hands' blades must press this much deeper than last frame's to be taken as the press.
        private const float StickyMeters = 0.005f;

        // A held side follows the blades round each other only while the drawn blades are at least this far apart
        // along it, and the turn is less than 60 degrees in one frame.
        private const float FollowAgreement = 0.5f;

        // Each frame the held side moves this share of the way toward where the drawn blades are apart.
        private const float FollowShare = 0.25f;

        // The turn that clears a sliding blade is found to about a fifth of a degree by halving this many times.
        private const int ReleaseSearchSteps = 9;

        // Two bow loads closer than this share of the blade are one.
        private const float SameLoadFraction = 0.05f;

        // A closest spot this far along a blade's last piece is its point.
        private const float AtPointShare = 0.999f;

        /// <summary>
        /// After a fast hit (<see cref="LatchSpeedMetersPerSecond"/>), the side stays latched this long from the hit
        /// (PER-138; since PER-147 only the hit, the first fast frame of an engagement, starts it, not every fast frame
        /// after it): a hand that swept past the other blade's reach and comes straight back is still on the side it hit
        /// from. Only a disengage round a blade's point, or a push through (<see cref="ThroughMarginMeters"/>), changes it
        /// sooner. About a pull-back's length after a beat (0.1 to 0.2 s).
        /// </summary>
        public const float LatchSeconds = 0.3f;

        /// <summary>
        /// Blades touching while they move against each other at least this fast are a hit (a beat, a fast parry), whose
        /// side is latched after they come apart (<see cref="LatchSeconds"/>); slower, they part where they are and the
        /// side follows the hands at once. A press or a glide is well under it.
        /// </summary>
        public const float LatchSpeedMetersPerSecond = 5.0f;

        /// <summary>
        /// The hands' move between two frames is looked at in steps of at most this (a blade's width) when the side is
        /// to change: through the other blade, or round its point.
        /// </summary>
        public const float SweepStepMeters = 0.01f;

        /// <summary>At most this many steps: a 0.32 m move between two frames (a 23 m/s relative speed at 72 Hz).</summary>
        public const int MaxSweepSteps = 32;

        /// <summary>
        /// At most this many steps when the side itself is swept (each step looks at every pair of pieces): a disengage
        /// is told from a move through at a few centimeters' steps.
        /// </summary>
        public const int MaxSideSweepSteps = 8;

        private static readonly float GripTurnMaxRadians = GripTurnMaxDegrees * (float)Math.PI / 180.0f;
        private static readonly float MaxReleaseRadians = MaxReleaseDegrees * (float)Math.PI / 180.0f;

        // The cosine of half the most turn: a turn's quaternion with a smaller |w| turns further.
        private static readonly float MaxReleaseHalfCosine = (float)Math.Cos(MaxReleaseRadians * 0.5f);

        private readonly Vector3[] _shapeA = new Vector3[PieceCount + 1];
        private readonly Vector3[] _shapeB = new Vector3[PieceCount + 1];
        private readonly Vector3[] _turned = new Vector3[PieceCount + 1];
        private readonly Vector3[] _flatOther = new Vector3[PieceCount + 1];
        private readonly Vector3[] _unitA = new Vector3[PieceCount];
        private readonly Vector3[] _unitB = new Vector3[PieceCount];
        private readonly Vector3[] _lastShapeA = new Vector3[PieceCount + 1];
        private readonly Vector3[] _lastShapeB = new Vector3[PieceCount + 1];
        private readonly Vector3[] _priorShapeA = new Vector3[PieceCount + 1];
        private readonly Vector3[] _priorShapeB = new Vector3[PieceCount + 1];
        private readonly Vector3[] _sweepA = new Vector3[PieceCount + 1];
        private readonly Vector3[] _sweepB = new Vector3[PieceCount + 1];
        private bool _hasLastShapes;
        private bool _hasPriorShapes;
        private float _relativeMotion;
        private float _deltaSeconds;
        private float _sinceHitSeconds = LatchSeconds;

        // This engagement's hit already started the side latch (PER-147: only the hit does, not every fast frame after it).
        private bool _hasLatchedHit;

        // The hands' blades were on the other side of each other from the side held, last frame.
        private bool _areHandsAcross;
        private float _engagedMeters = EngagedMeters;
        private Hit _lastHit;

        // Which pairs of pieces of the hands' blades are through each other (behind, gone in by a swept crossing), this
        // frame and last frame.
        private bool[] _isBehind = new bool[PieceCount * PieceCount];
        private bool[] _wasBehind = new bool[PieceCount * PieceCount];
        private readonly Work _a = new Work();
        private readonly Work _b = new Work();
        private Vector3? _side;
        private int _slidingWeaker = -1;
        private int _returningBlade = -1;
        private Vector3 _slideAxis;
        private float _slideRadians;

        // What each blade swings back to its hand from (PER-147: both blades' whole give after a release or a push
        // through, only the slide's turn after a slide-off that stopped short), eased out over ReleaseSeconds.
        private bool _isReturning;
        private GiveMemory _returnA;
        private GiveMemory _returnB;
        private float _returningSeconds;
        private float _bowReturnSeconds = BowReleaseSeconds;
        private float _turnReturnSeconds = ReleaseSeconds;

        // The pieces of the hands' blades pressed last frame (-2 for none): the press stays there (StickyMeters).
        private int _lastPieceA = -2;
        private int _lastPieceB = -2;

        // The most give the stages had at the contact during this press (PER-147): the through depth is past it.
        private float _pressGiveMeters;

        // Each blade's give as last drawn: where a push through starts swinging back from.
        private GiveMemory _lastGiveA;
        private GiveMemory _lastGiveB;
        private readonly HiltMemory _onHiltA = new HiltMemory();
        private readonly HiltMemory _onHiltB = new HiltMemory();
        private readonly Vector3[] _otherPoints = new Vector3[PieceCount + 1];
        private Vector3 _otherBladeSide;

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
        public bool IsNear => IsEngaged || IsNearHilt || ClosestMeters < ContactMeters + BladeFlex.MaxBendMeters;

        /// <summary>
        /// True while a blade touches or nearly touches the other's hilt (bell or grip): the hilts are not obstacles of
        /// the drawn blades, so a flex whip must not carry a blade into one either.
        /// </summary>
        public bool IsNearHilt => _onHiltA.IsEngaged || _onHiltB.IsEngaged;

        /// <summary>True while blade B presses on blade A's hilt (bell or grip) this frame.</summary>
        public bool IsBOnHiltA => _onHiltA.PressMeters > 0.0f;

        /// <summary>True while blade A presses on blade B's hilt (bell or grip) this frame.</summary>
        public bool IsAOnHiltB => _onHiltB.PressMeters > 0.0f;

        /// <summary>How far a blade presses into the other's hilt (the deeper of the two), 0 when neither touches.</summary>
        public float HiltPressMeters => Math.Max(_onHiltA.PressMeters, _onHiltB.PressMeters);

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

        /// <summary>True while the weaker blade slides off, or the blades swing back after sliding off or a push through.</summary>
        public bool IsReleasing => _slidingWeaker >= 0 || _isReturning;

        /// <summary>
        /// True on the frame the hands pushed the blades through each other (PER-147: past every give, the slide's span
        /// and <see cref="ThroughMarginMeters"/>): the press and the side latch were dropped and both blades swing back.
        /// </summary>
        public bool IsThrough { get; private set; }

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
            _hasPriorShapes = false;
            _relativeMotion = 0.0f;
            _sinceHitSeconds = LatchSeconds;
            _hasLatchedHit = false;
            _pressGiveMeters = 0.0f;
            _lastPieceA = -2;
            _lastPieceB = -2;
            _areHandsAcross = false;
            Array.Clear(_isBehind, 0, _isBehind.Length);
            Array.Clear(_wasBehind, 0, _wasBehind.Length);
            _slidingWeaker = -1;
            _returningBlade = -1;
            _isReturning = false;
            _lastGiveA = GiveMemory.None;
            _lastGiveB = GiveMemory.None;
            IsThrough = false;
            IsInContact = false;
            IsEngaged = false;
            PressMeters = 0.0f;
            PressNewtons = 0.0f;
            _onHiltA.Reset();
            _onHiltB.Reset();
        }

        /// <summary>Solves this frame's contact of the two hands' blades into <see cref="GiveA"/> and <see cref="GiveB"/>.</summary>
        public void Solve(BladeHold a, BladeHold b, float deltaSeconds)
        {
            _deltaSeconds = Math.Max(0.0f, deltaSeconds);
            _a.Start(a);
            _b.Start(b);
            FillShape(_a, _shapeA, _unitA);
            FillShape(_b, _shapeB, _unitB);

            // Also keeps this frame's hands' blades (the rest shapes) for the side's update. Measured before a blade
            // swinging back is turned (PER-147): its swing is not the hands moving, and must not count as a hit.
            _relativeMotion = FindRelativeMotion();
            if (StepReturn(Math.Max(0.0f, deltaSeconds)))
            {
                FillShape(_a, _shapeA, _unitA);
                FillShape(_b, _shapeB, _unitB);
            }

            _sinceHitSeconds += Math.Max(0.0f, deltaSeconds);
            _engagedMeters = EngagedMeters + Math.Min(MaxMotionBandMeters, MotionReach * _relativeMotion);
            ClosestMeters = float.MaxValue;
            if (!_side.HasValue)
            {
                _side = FindGeometricSide(_lastShapeA, _lastShapeB, out var closest, out _);
                ClosestMeters = closest;
            }
            else if (!IsEngaged || _relativeMotion > EngagedMeters)
            {
                // Engaged and moving slowly (a press, a bind), nothing goes round in a frame: not looked at, to save the time.
                FollowRoundCrossing();
            }

            var side = _side.Value;

            IsInContact = false;
            IsThrough = false;
            PressMeters = 0.0f;
            PressNewtons = 0.0f;
            SaturatedStages = 0;
            (_wasBehind, _isBehind) = (_isBehind, _wasBehind);
            var first = FindDeepest(_shapeA, _shapeB, _unitA, _unitB, side, _engagedMeters, isHands: true);
            _lastHit = first;
            IsEngaged = first.IsFound;
            if (!IsEngaged)
            {
                _hasLatchedHit = false;
            }
            else if (!_hasLatchedHit && deltaSeconds > 0.0f && _relativeMotion >= LatchSpeedMetersPerSecond * deltaSeconds)
            {
                // The hit (PER-147): the first fast frame of this engagement starts the latch, and nothing after it
                // restarts it until the blades part, so a fast slide along the blades never postpones a release.
                _sinceHitSeconds = 0.0f;
                _hasLatchedHit = true;
            }

            var outcome = ContactOutcome.Held;
            _lastPieceA = first.IsFound && first.Overlap > 0.0f ? first.PieceA : -2;
            _lastPieceB = first.IsFound && first.Overlap > 0.0f ? first.PieceB : -2;
            if (first.IsFound && first.Overlap > 0.0f)
            {
                IsInContact = true;
                PressMeters = first.Overlap;
                ContactPoint = (first.OnA + first.OnB) * 0.5f;
                FractionA = first.FractionA;
                FractionB = first.FractionB;
                PushOnB = -first.Normal;
                outcome = SolveContact(first);
            }
            else
            {
                EndSlide();
            }

            if (outcome != ContactOutcome.Held || !IsInContact)
            {
                _pressGiveMeters = 0.0f;
            }

            if (outcome == ContactOutcome.Through)
            {
                LetGoThrough(first);
            }

            // Then each blade against the other's hilt, slid off the side it is on of the other blade.
            SolveHilt(_b, _shapeB, _a, new Hilt(_a.ToGive()), _onHiltA, -side, IsEngaged);
            SolveHilt(_a, _shapeA, _b, new Hilt(_b.ToGive()), _onHiltB, side, IsEngaged);
            if (HiltPressMeters > 0.0f && IsEngaged)
            {
                // A blade pressed between the other's blade and its bell (the wedge): the hilt's push must not have
                // carried it into or through that blade. One more look at the blades, which come first.
                LookAgainAtBlades();
            }

            if (outcome == ContactOutcome.Released)
            {
                // Slid all the way off: both blades swing back to their hands from all their give, not only the slide's
                // turn: no bow is dropped in one frame (PER-147).
                StartReturn(_a.Remember(), _b.Remember(), _returningBlade);
            }

            GiveA = _a.ToGive();
            GiveB = _b.ToGive();
            _lastGiveA = _a.Remember();
            _lastGiveB = _b.Remember();
            UpdateSide(outcome != ContactOutcome.Held);
        }

        /// <summary>
        /// The hands pushed the blades through each other (PER-147): the press and the side latch are dropped, nothing of
        /// this frame's give is kept, and both blades swing back to their hands over <see cref="ReleaseSeconds"/> from
        /// where they were last drawn, so neither jumps. The side is then read from the hands (<see cref="UpdateSide"/>),
        /// and no pair is remembered as through, so nothing carries the old side on.
        /// </summary>
        private void LetGoThrough(Hit first)
        {
            var returning = _slidingWeaker >= 0 ? _slidingWeaker : FindWeaker(first);
            _a.Restart();
            _b.Restart();
            StartReturn(_lastGiveA, _lastGiveB, returning);
            _a.Apply(_returnA, 1.0f, 1.0f);
            _b.Apply(_returnB, 1.0f, 1.0f);
            _slidingWeaker = -1;
            _slideRadians = 0.0f;
            _sinceHitSeconds = LatchSeconds;
            _areHandsAcross = false;
            Array.Clear(_isBehind, 0, _isBehind.Length);
            IsThrough = true;
            IsInContact = false;
            IsEngaged = false;
            PressMeters = 0.0f;
            PressNewtons = 0.0f;
            SaturatedStages = 0;
        }

        /// <summary>Both blades swing back to their hands from <paramref name="a"/> and <paramref name="b"/>, starting this frame.</summary>
        private void StartReturn(GiveMemory a, GiveMemory b, int returningBlade)
        {
            _isReturning = !a.IsNone || !b.IsNone;
            _returnA = a;
            _returnB = b;
            _returningSeconds = 0.0f;
            var mostTip = Math.Max(a.TipBowMeters, b.TipBowMeters);
            _bowReturnSeconds = mostTip > 0.0f ? Math.Clamp(mostTip / ReturnTipMetersPerSecond, BowReleaseSeconds, ReleaseSeconds) : 0.0f;

            // The turn eases out over ReleaseSeconds, longer for a turn so large its point would pass the slide's pace
            // (an eased turn's point moves at most 1.5 times its mean pace).
            var mostTurnTip = Math.Max(a.TurnRadians * _a.Length, b.TurnRadians * _b.Length);
            _turnReturnSeconds = Math.Max(ReleaseSeconds, 1.5f * mostTurnTip / SlideTipMetersPerSecond);
            _returningBlade = _isReturning ? returningBlade : -1;
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

                // Last frame's hands' blades are kept too, for the sweep when the side is to change.
                _priorShapeA[index] = _lastShapeA[index];
                _priorShapeB[index] = _lastShapeB[index];
                _lastShapeA[index] = _shapeA[index];
                _lastShapeB[index] = _shapeB[index];
            }

            _hasPriorShapes = _hasLastShapes;
            _hasLastShapes = true;
            return most;
        }

        /// <summary>
        /// Shares the overlap of <paramref name="first"/> out, then looks again for what is left; past every limit the
        /// weaker blade slides off. <see cref="ContactOutcome.Released"/> when it slid all the way off this frame and is
        /// let go; <see cref="ContactOutcome.Through"/> when the hands are past even that by
        /// <see cref="ThroughMarginMeters"/> (PER-147), whatever the latch: nothing of this frame's give is kept then.
        /// </summary>
        private ContactOutcome SolveContact(Hit first)
        {
            var isReleased = false;
            var excess = Split(first, out var newtons, out var giveMeters);
            PressNewtons = newtons;
            SaturatedStages = _splitSaturated;

            // The through depth is taken from the most give seen at the contact during this press: the deepest spot may
            // move to a stiffer part of a blade (toward its guard) as the other slides, and that is no push through.
            _pressGiveMeters = Math.Max(_pressGiveMeters, giveMeters);
            if (first.Overlap > _pressGiveMeters + ReleaseSpanMeters + ThroughMarginMeters)
            {
                return ContactOutcome.Through;
            }

            var weaker = -1;
            if (excess > 0.0f || (_slidingWeaker >= 0 && _slideRadians > 0.0f))
            {
                // Past every limit, or sliding and still pressed (PER-147): a press that fits the give again for a frame
                // (the deepest spot moved to a softer part of a blade) eases the slide back at its own pace rather than
                // ending it, so it never stops and starts again from frame to frame.
                var wasSliding = _slidingWeaker >= 0;
                weaker = wasSliding ? _slidingWeaker : FindWeaker(first);
                _slidingWeaker = weaker;
                isReleased = SlideOff(weaker, first, excess, wasSliding);
            }
            else
            {
                EndSlide();
            }

            for (var pass = 1; pass < Passes; pass++)
            {
                FillShape(_a, _shapeA, _unitA);
                FillShape(_b, _shapeB, _unitB);
                var hit = FindDeepest(_shapeA, _shapeB, _unitA, _unitB, _side.Value, _engagedMeters, isHands: false);
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

            return isReleased ? ContactOutcome.Released : ContactOutcome.Held;
        }

        /// <summary>
        /// The blades as given so far looked at once more along the held side: any overlap is shared out again, and what
        /// is left turns the weaker blade on past its grip's limit, so it never ends a frame through the other.
        /// </summary>
        private void LookAgainAtBlades()
        {
            FillShape(_a, _shapeA, _unitA);
            FillShape(_b, _shapeB, _unitB);
            var hit = FindDeepest(_shapeA, _shapeB, _unitA, _unitB, _side.Value, _engagedMeters, isHands: false);
            if (!hit.IsFound || hit.Overlap <= SettledMeters)
            {
                return;
            }

            var left = Split(hit, out _);
            if (left > 0.0f)
            {
                TurnAway(_slidingWeaker >= 0 ? _slidingWeaker : FindWeaker(hit), hit, left);
            }
        }

        /// <summary>
        /// Shares <paramref name="hit"/>'s overlap out over the four stages by compliance, each up to its limit (a stage
        /// at its limit hands its share on). Returns the overlap no stage could take; <paramref name="newtons"/> is the
        /// stages' load.
        /// </summary>
        private float Split(Hit hit, out float newtons)
        {
            return Split(hit, out newtons, out _);
        }

        /// <summary>As <see cref="Split(Hit, out float)"/>; <paramref name="giveMeters"/> is all the four stages could take there.</summary>
        private float Split(Hit hit, out float newtons, out float giveMeters)
        {
            Span<Stage> stages = stackalloc Stage[4];
            stages[0] = _a.FindTurnStage(hit.OnA, hit.FractionA, hit.Normal);
            stages[1] = _a.FindBowStage(hit.FractionA, hit.Normal);
            stages[2] = _b.FindTurnStage(hit.OnB, hit.FractionB, -hit.Normal);
            stages[3] = _b.FindBowStage(hit.FractionB, -hit.Normal);
            giveMeters = stages[0].Limit + stages[1].Limit + stages[2].Limit + stages[3].Limit;
            Span<float> taken = stackalloc float[4];
            var remaining = ShareOut(stages, hit.Overlap, taken, out newtons, out _splitSaturated);

            _a.Turn(hit.OnA, hit.Normal, taken[0]);
            _a.AddBow(hit.FractionA, hit.Normal, taken[1]);
            _b.Turn(hit.OnB, -hit.Normal, taken[2]);
            _b.AddBow(hit.FractionB, -hit.Normal, taken[3]);
            return remaining;
        }

        /// <summary>
        /// Water-filling: the load rises until each stage takes compliance times load or its limit, together
        /// <paramref name="overlap"/>. Fills <paramref name="taken"/>; returns what no stage could take.
        /// </summary>
        private static float ShareOut(Span<Stage> stages, float overlap, Span<float> taken, out float newtons, out int fullStages)
        {
            var remaining = overlap;
            Span<bool> isFull = stackalloc bool[stages.Length];
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

            fullStages = 0;
            for (var index = 0; index < stages.Length; index++)
            {
                fullStages |= isFull[index] ? 1 << index : 0;
            }

            return Math.Max(0.0f, remaining);
        }

        /// <summary>
        /// Past every limit by <paramref name="excess"/>: the weaker blade turns aside, square to the press, toward the
        /// other blade's line, so the contact slides toward its point, by the share of the turn that clears it that the
        /// excess is of <see cref="ReleaseSpanMeters"/>. True once it is all the way off: it is let go, the side follows
        /// the hands and the release swings it back. Within <see cref="LatchSeconds"/> of a fast hit it is not let go
        /// (PER-138): the hand swept through in the hit, the side holds, and swinging back to the hand would carry it
        /// through the other blade; it stays off while pressed past and swings back once the press is gone
        /// (<see cref="EndSlide"/>), or until the hands push through (<see cref="ThroughMarginMeters"/>). A blade that was
        /// already sliding goes on the way it was going (PER-147): the way round is not chosen afresh each frame, which
        /// let it swing to and fro; and it turns with its point no faster than <see cref="SlideTipMetersPerSecond"/>, so it
        /// is let go only once it has turned all the way.
        /// </summary>
        private bool SlideOff(int weaker, Hit hit, float excess, bool wasSliding)
        {
            FillShape(_a, _shapeA, _unitA);
            FillShape(_b, _shapeB, _unitB);
            var normal = hit.Normal;
            var sliding = weaker == 0 ? _shapeA : _shapeB;
            var other = weaker == 0 ? _shapeB : _shapeA;
            var work = weaker == 0 ? _a : _b;
            var otherPiece = weaker == 0 ? hit.PieceB : hit.PieceA;
            var axis = FindSlideAxis(sliding, other, otherPiece, normal);
            if (wasSliding && Vector3.Dot(axis, _slideAxis) < 0.0f)
            {
                axis = -axis;
            }

            // Easing back (the press fits the give again): no turn to look for.
            var share = Math.Min(1.0f, excess / ReleaseSpanMeters);
            var clearRadians = share > 0.0f ? FindClearingTurn(sliding, other, work.Guard, axis, normal) : 0.0f;
            var target = clearRadians * SmoothStep(share);
            var radians = target;
            if (_deltaSeconds > 0.0f)
            {
                var step = SlideTipMetersPerSecond * _deltaSeconds / work.Length;
                var from = wasSliding ? _slideRadians : 0.0f;
                radians = Math.Clamp(target, from - step, from + step);
            }

            work.TurnAbout(axis, radians);
            _slideAxis = axis;
            _slideRadians = radians;
            if (radians <= 0.0f && excess <= 0.0f)
            {
                // Eased all the way back while still pressed: no longer sliding.
                _slidingWeaker = -1;
                return false;
            }

            if (share < 1.0f || radians < target || _sinceHitSeconds < LatchSeconds)
            {
                return false;
            }

            // Let go: both blades swing back from all their give once the frame is solved (Solve).
            _returningBlade = weaker;
            _slidingWeaker = -1;
            _slideRadians = 0.0f;
            return true;
        }

        /// <summary>
        /// No longer pressed past every limit: a blade that was sliding off swings back to the hand from where it slid to,
        /// over <see cref="ReleaseSeconds"/> (it starts there this frame, so it never jumps back), and it never holds.
        /// Only the slide's turn swings back: the press still bends both blades.
        /// </summary>
        private void EndSlide()
        {
            if (_slidingWeaker >= 0 && _slideRadians > 0.0f)
            {
                var slide = GiveMemory.FromTurn(Quaternion.CreateFromAxisAngle(_slideAxis, _slideRadians));
                StartReturn(_slidingWeaker == 0 ? slide : GiveMemory.None, _slidingWeaker == 0 ? GiveMemory.None : slide, _slidingWeaker);
                (_slidingWeaker == 0 ? _a : _b).TurnAbout(_slideAxis, _slideRadians);
            }

            _slidingWeaker = -1;
            _slideRadians = 0.0f;
        }

        /// <summary>
        /// The left-over overlap of <paramref name="hit"/> taken by the blade turning on in its grip past its limit; its
        /// whole turn stays within <see cref="MaxReleaseDegrees"/> (PER-147), whatever is left over.
        /// </summary>
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

        /// <summary>
        /// Blades let go swing back to their hands: the give they were let go with eases out over
        /// <see cref="ReleaseSeconds"/>. True when some give was put on this frame.
        /// </summary>
        private bool StepReturn(float deltaSeconds)
        {
            if (!_isReturning)
            {
                return false;
            }

            _returningSeconds += deltaSeconds;
            if (_returningSeconds >= _bowReturnSeconds + _turnReturnSeconds)
            {
                _isReturning = false;
                _returningBlade = -1;
                return false;
            }

            // The bows spring straight out at a steady pace first, the turns held meanwhile: a blade let go clear of the
            // other (slid off past its point, or turned aside) stays clear while it unbends, and only then swings back.
            var bowLeft = _bowReturnSeconds > 0.0f ? 1.0f - Math.Min(1.0f, _returningSeconds / _bowReturnSeconds) : 0.0f;
            var turnLeft = 1.0f - SmoothStep(Math.Clamp((_returningSeconds - _bowReturnSeconds) / _turnReturnSeconds, 0.0f, 1.0f));
            _a.Apply(_returnA, turnLeft, bowLeft);
            _b.Apply(_returnB, turnLeft, bowLeft);
            return true;
        }

        private static float SmoothStep(float fraction)
        {
            return fraction * fraction * (3.0f - (2.0f * fraction));
        }

        /// <summary>
        /// After the solve: the side latched at first contact (from the last frame the blades were apart) is held while
        /// engaged, following the drawn blades as they roll round each other, also when a blade slid off (PER-138: the
        /// hands are through each other then, not round). Apart, it is taken from the hands, but within
        /// <see cref="LatchSeconds"/> of a fast hit (<see cref="LatchSpeedMetersPerSecond"/>) it stays: a hand that swept
        /// past in the hit and comes straight back is still on the side it hit from. Only a disengage round a blade's
        /// point changes it sooner (<see cref="FollowRoundCrossing"/>).
        /// </summary>
        private void UpdateSide(bool isLetGo)
        {
            if (isLetGo)
            {
                // Slid all the way off its point, out of a slow press (it went round), or pushed through (PER-147): the
                // side follows the hands.
                _side = FindGeometricSide(_lastShapeA, _lastShapeB, out var releasedClosest, out _);
                ClosestMeters = releasedClosest;
                return;
            }

            if (!IsEngaged)
            {
                var geometric = FindGeometricSide(_lastShapeA, _lastShapeB, out var closest, out _);
                ClosestMeters = closest;
                if (_sinceHitSeconds >= LatchSeconds)
                {
                    _side = geometric;
                }

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
        /// <paramref name="engagedMeters"/> of touching. A pair behind counts only when the hands' pieces went through
        /// each other there (PER-138): they were behind last frame too (a press going on), or since last frame they came
        /// within touching on the way (swept, <see cref="HasSweptThrough"/>). One that went past the other further off
        /// than that (a point swept over the other's forte in a fast hit) went round it, not through.
        /// </summary>
        /// <param name="isHands">The shapes are this frame's hands' blades (no give yet): the pairs found behind are kept for the next frame.</param>
        private Hit FindDeepest(Vector3[] shapeA, Vector3[] shapeB, Vector3[] unitA, Vector3[] unitB, Vector3 side, float engagedMeters, bool isHands)
        {
            var best = default(Hit);
            best.Overlap = -float.MaxValue;
            var bestRank = -float.MaxValue;
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
                    var wasBehind = WasThroughNear(pieceA, pieceB);
                    if (isHands)
                    {
                        _isBehind[(pieceA * PieceCount) + pieceB] = false;
                    }

                    // Nothing here can beat the deepest found so far, even pushed right through (the hands' blades are
                    // all looked at, to keep which pairs are through).
                    if (!isHands && ContactMeters + apart.Length() <= best.Overlap)
                    {
                        continue;
                    }

                    var normal = FindPairNormal(unitA[pieceA], unitB[pieceB], side, out var isCrossing);
                    var signed = Vector3.Dot(apart, normal);

                    // On its own side, how far into each other they really are; behind, how far back it has to go.
                    var overlap = ContactMeters - (signed >= 0.0f ? apart.Length() : signed);
                    if (overlap < -engagedMeters || (!isHands && overlap <= best.Overlap))
                    {
                        continue;
                    }

                    // Seen along the side, the pieces must lie over each other (or within the slack of a push through).
                    // Alongside each other, a blade found behind the other went through it, wherever, unless they meet
                    // at a blade's point. Never round its guard end: the bell and the hand are there (the hilt, below).
                    var isAtEnd = (pieceA == PieceCount - 1 && alongA >= 1.0f) || (pieceB == PieceCount - 1 && alongB >= 1.0f);
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

                    if (signed < 0.0f && !wasBehind && !HasSweptThrough(pieceA, pieceB))
                    {
                        // Behind, but it went past the other off to the side: round it, not through.
                        continue;
                    }

                    if (isHands && signed < 0.0f)
                    {
                        _isBehind[(pieceA * PieceCount) + pieceB] = true;
                    }

                    // The hands' press stays where it was last frame (on or next to the same pieces) unless another spot is
                    // clearly deeper (PER-147): along a shallow bind two spots press nearly alike, and hopping between them
                    // swung the give back and forth from frame to frame.
                    var rank = overlap;
                    if (isHands && overlap > 0.0f && Math.Abs(pieceA - _lastPieceA) <= 1 && Math.Abs(pieceB - _lastPieceB) <= 1)
                    {
                        rank += StickyMeters;
                    }

                    if (rank <= bestRank)
                    {
                        continue;
                    }

                    bestRank = rank;
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
        /// True when this pair of pieces or one next to it (along either blade) was through last frame: a press through
        /// slides along the blades from piece to piece.
        /// </summary>
        private bool WasThroughNear(int pieceA, int pieceB)
        {
            for (var nearA = Math.Max(0, pieceA - 1); nearA <= Math.Min(PieceCount - 1, pieceA + 1); nearA++)
            {
                for (var nearB = Math.Max(0, pieceB - 1); nearB <= Math.Min(PieceCount - 1, pieceB + 1); nearB++)
                {
                    if (_wasBehind[(nearA * PieceCount) + nearB])
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// True when piece <paramref name="pieceA"/> of the hands' blade A and piece <paramref name="pieceB"/> of B came
        /// within touching of each other on their way from last frame to this one (each piece moved in steps of at most
        /// <see cref="SweepStepMeters"/>): they went through each other there. True too with no last frame.
        /// </summary>
        private bool HasSweptThrough(int pieceA, int pieceB)
        {
            if (!_hasPriorShapes)
            {
                return true;
            }

            var moved = Math.Max(
                Math.Max(Vector3.Distance(_priorShapeA[pieceA], _lastShapeA[pieceA]), Vector3.Distance(_priorShapeA[pieceA + 1], _lastShapeA[pieceA + 1])),
                Math.Max(Vector3.Distance(_priorShapeB[pieceB], _lastShapeB[pieceB]), Vector3.Distance(_priorShapeB[pieceB + 1], _lastShapeB[pieceB + 1])));
            var steps = Math.Clamp((int)Math.Ceiling(2.0f * moved / SweepStepMeters), 1, MaxSweepSteps);
            for (var step = 0; step <= steps; step++)
            {
                var fraction = (float)step / steps;
                FindClosest(
                    Vector3.Lerp(_priorShapeA[pieceA], _lastShapeA[pieceA], fraction),
                    Vector3.Lerp(_priorShapeA[pieceA + 1], _lastShapeA[pieceA + 1], fraction),
                    Vector3.Lerp(_priorShapeB[pieceB], _lastShapeB[pieceB], fraction),
                    Vector3.Lerp(_priorShapeB[pieceB + 1], _lastShapeB[pieceB + 1], fraction),
                    out var onA,
                    out var onB,
                    out _,
                    out _);
                if (Vector3.DistanceSquared(onA, onB) <= ContactMeters * ContactMeters)
                {
                    return true;
                }
            }

            return false;
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
        /// Before the contact is looked at: when this frame's hands' blades are on the other side of each other from the
        /// side held, and they went there round each other (a disengage round a point, or over a blade) rather than
        /// through, the side follows them (PER-138). Through, it stays: that is a press.
        /// </summary>
        private void FollowRoundCrossing()
        {
            var geometric = FindGeometricSide(_lastShapeA, _lastShapeB, out _, out _);
            var wasAcross = _areHandsAcross;
            _areHandsAcross = Vector3.Dot(geometric, _side.Value) < 0.0f;
            if (!_areHandsAcross || wasAcross)
            {
                // On the side held, or already across it last frame (a press going on): nothing went across this frame.
                return;
            }

            if (FindSweptCrossing(_side.Value) == Crossing.Round)
            {
                _side = geometric;
                _areHandsAcross = false;
            }
        }

        /// <summary>
        /// How the hands' blades went from last frame's to this frame's against <paramref name="held"/> (the side as
        /// held), looked at in steps of at most <see cref="SweepStepMeters"/> of their move against each other: across
        /// the other blade where they lie over each other (<see cref="Crossing.Through"/>), round a blade's point
        /// (<see cref="Crossing.Round"/>, a disengage), or not across at all this frame. The last crossing counts.
        /// </summary>
        private Crossing FindSweptCrossing(Vector3 held)
        {
            if (!_hasPriorShapes)
            {
                return Crossing.None;
            }

            var steps = Math.Clamp((int)Math.Ceiling(_relativeMotion / SweepStepMeters), 1, MaxSideSweepSteps);
            var crossing = Crossing.None;
            var wasOnHeldSide = true;
            var wasDistance = float.MaxValue;
            var wasAtPoint = false;
            for (var step = 0; step <= steps; step++)
            {
                var fraction = (float)step / steps;
                for (var index = 0; index <= PieceCount; index++)
                {
                    _sweepA[index] = Vector3.Lerp(_priorShapeA[index], _lastShapeA[index], fraction);
                    _sweepB[index] = Vector3.Lerp(_priorShapeB[index], _lastShapeB[index], fraction);
                }

                var side = FindGeometricSide(_sweepA, _sweepB, out var distance, out var isAtPoint);
                var isOnHeldSide = Vector3.Dot(side, held) >= 0.0f;
                if (step > 0 && isOnHeldSide != wasOnHeldSide)
                {
                    // Across beside a blade's point, apart: round it (a disengage). Touching, or across a blade's length:
                    // through it.
                    var isRound = (isAtPoint || wasAtPoint) && Math.Min(distance, wasDistance) > ContactMeters;
                    crossing = isRound ? Crossing.Round : Crossing.Through;
                }

                wasOnHeldSide = isOnHeldSide;
                wasDistance = distance;
                wasAtPoint = isAtPoint;
            }

            return crossing;
        }

        /// <summary>
        /// Which way A is from B with no memory, from <paramref name="shapeA"/> and <paramref name="shapeB"/>: where they
        /// come closest, the crossing's normal turned toward A, or the way from B to A. <paramref name="isAtPoint"/>: the
        /// closest spot is a blade's point (beside its end, not across its length).
        /// </summary>
        private static Vector3 FindGeometricSide(Vector3[] shapeA, Vector3[] shapeB, out float closest, out bool isAtPoint)
        {
            closest = float.MaxValue;
            isAtPoint = false;
            var side = Vector3.UnitY;
            for (var pieceA = 0; pieceA < PieceCount; pieceA++)
            {
                for (var pieceB = 0; pieceB < PieceCount; pieceB++)
                {
                    FindClosest(shapeA[pieceA], shapeA[pieceA + 1], shapeB[pieceB], shapeB[pieceB + 1], out var onA, out var onB, out var alongA, out var alongB);
                    var apart = onA - onB;
                    var distance = apart.Length();
                    if (distance >= closest)
                    {
                        continue;
                    }

                    closest = distance;
                    isAtPoint = (pieceA == PieceCount - 1 && alongA >= AtPointShare) || (pieceB == PieceCount - 1 && alongB >= AtPointShare);
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

        /// <summary>
        /// One blade (<paramref name="blade"/>, as given so far) against the other's hilt, its bell and then its grip:
        /// for each, the overlap along the side held in <paramref name="memory"/> taken by the blade's grip turn and bow,
        /// past their limits slid off, and the side kept for the next frame. <paramref name="otherBladeSide"/> is which
        /// way this blade is from the other blade (held while <paramref name="isOtherBladeEngaged"/>): it slides off the
        /// hilt that way, not through the blade.
        /// </summary>
        private void SolveHilt(Work blade, Vector3[] shape, Work other, Hilt hilt, HiltMemory memory, Vector3 otherBladeSide, bool isOtherBladeEngaged)
        {
            var hint = isOtherBladeEngaged ? otherBladeSide : Vector3.Zero;
            FillPoints(other, _otherPoints);
            _otherBladeSide = hint;
            SolveHiltPart(blade, shape, hilt, HiltPart.Bell, memory.Bell, hint);
            SolveHiltPart(blade, shape, hilt, HiltPart.Grip, memory.Grip, hint);
        }

        /// <summary>
        /// One part of the hilt (<see cref="SolveHilt"/>). While the blade was engaged with it last frame, the side held
        /// then stands (the hand's blade may have gone right through in between); otherwise it is taken from the blade.
        /// </summary>
        private void SolveHiltPart(Work blade, Vector3[] shape, Hilt hilt, HiltPart part, HiltPartMemory memory, Vector3 otherBladeSide)
        {
            FillPoints(blade, shape);
            if (!memory.IsEngaged && !hilt.IsNear(shape, _engagedMeters))
            {
                // Far from it (most frames): nothing to look at, and the side is taken afresh when it comes near.
                memory.Side = null;
                memory.PressMeters = 0.0f;
                return;
            }

            var side = memory.IsEngaged && memory.Side.HasValue ? memory.Side.Value : hilt.FindSide(shape, part, memory.Side, _engagedMeters);
            var hit = FindHiltDeepest(shape, hilt, part, side);
            memory.IsEngaged = hit.Overlap > -_engagedMeters;
            memory.PressMeters = Math.Max(0.0f, hit.Overlap);
            Span<Stage> stages = stackalloc Stage[2];
            Span<float> taken = stackalloc float[2];
            for (var pass = 0; pass < Passes && hit.Overlap > (pass == 0 ? 0.0f : HiltSettledMeters); pass++)
            {
                // Each look shares what is left out again (the bowed blade's curve moves the deepest spot a little).
                stages[0] = blade.FindTurnStage(hit.Point, hit.Fraction, side);
                stages[1] = blade.FindBowStage(hit.Fraction, side);
                var excess = ShareOut(stages, hit.Overlap, taken, out _, out _);
                blade.Turn(hit.Point, side, taken[0]);
                blade.AddBow(hit.Fraction, side, taken[1]);
                if (excess > 0.0f)
                {
                    SlideOffHilt(blade, shape, hilt, part, side, hit, excess, otherBladeSide);
                }

                FillPoints(blade, shape);
                hit = FindHiltDeepest(shape, hilt, part, side);
            }

            // The side follows the blade as drawn, out of the hilt: off the bell's face, round its rim, round the grip.
            // Only a frame's move is measured along the held side, so a fast one cannot carry it through. Pressed on the
            // bell, it never goes to the bell's back (the hand is there): it slides off forward or round the rim.
            var drawnSide = hilt.FindSide(shape, part, side, _engagedMeters);
            var isToBack = part == HiltPart.Bell && Vector3.Dot(drawnSide, hilt.Axis) < -BackShare && Vector3.Dot(side, hilt.Axis) >= -BackShare;
            memory.Side = memory.IsEngaged && isToBack ? side : drawnSide;
        }

        /// <summary>
        /// Past the grip's and the bow's limits by <paramref name="excess"/>: the blade slides off the hilt's part as off a
        /// blade, turning in the grip toward the nearer way round (the bell's rim, the grip's ends) by the share of that
        /// turn the excess is of <see cref="ReleaseSpanMeters"/>. What overlap is left turns it on, past its grip's limit,
        /// and a blade end-on to the press (its point on the bell's face) skids on until it is clear: never through.
        /// </summary>
        private void SlideOffHilt(Work blade, Vector3[] shape, Hilt hilt, HiltPart part, Vector3 side, HiltHit hit, float excess, Vector3 otherBladeSide)
        {
            var axis = FindHiltSlideAxis(shape, blade.Guard, hilt, part, side, hit.Point, otherBladeSide, out var isEndOn);
            if (axis != Vector3.Zero && !isEndOn)
            {
                // Across the press: it slides off round the hilt as the press goes on past the limits. End-on (a point on
                // the bell's face), or with no way round within the release's turn, it only skids, below.
                var clearRadians = FindHiltClearingTurn(shape, blade.Guard, hilt, part, side, axis, isSeenAlong: true);
                var share = Math.Min(1.0f, excess / ReleaseSpanMeters);
                var radians = clearRadians < MaxReleaseRadians ? clearRadians * share * share * (3.0f - (2.0f * share)) : 0.0f;
                blade.TurnAbout(axis, radians);
            }

            FillPoints(blade, shape);
            var left = FindHiltDeepest(shape, hilt, part, side);
            if (left.Overlap > HiltSettledMeters)
            {
                // Pushed on out of the hilt along the side, past the grip's limit, unless that carries it through the
                // other blade (wedged between that blade and this bell, the blade comes first).
                var lever = left.Point - blade.Guard;
                var turnAxis = Vector3.Cross(lever, side);
                var leverSquare = turnAxis.Length();
                if (leverSquare > 1e-6f)
                {
                    var radians = left.Overlap / leverSquare;
                    turnAxis /= leverSquare;
                    if (!IsThroughOtherBlade(shape, blade.Guard, turnAxis, radians))
                    {
                        blade.TurnAbout(turnAxis, radians);
                    }
                }

                FillPoints(blade, shape);
                left = FindHiltDeepest(shape, hilt, part, side);
            }

            if (left.Overlap > HiltSettledMeters && axis != Vector3.Zero)
            {
                // A turn that clears it at all (none: it stays pressed into the hilt rather than through the blade).
                var clearRadians = FindHiltClearingTurn(shape, blade.Guard, hilt, part, side, axis, isSeenAlong: false);
                if (clearRadians < MaxReleaseRadians)
                {
                    blade.TurnAbout(axis, clearRadians);
                }
            }
        }

        /// <summary>
        /// True when <paramref name="shape"/> turned by <paramref name="radians"/> about <paramref name="axis"/> through
        /// <paramref name="guard"/> would be in or through the other blade (<see cref="_otherPoints"/>): closer than
        /// touching, or on its far side from <see cref="_otherBladeSide"/> where the two lie over each other. Never with
        /// the blades apart (no side held).
        /// </summary>
        private bool IsThroughOtherBlade(Vector3[] shape, Vector3 guard, Vector3 axis, float radians)
        {
            if (_otherBladeSide == Vector3.Zero)
            {
                return false;
            }

            var turn = Quaternion.CreateFromAxisAngle(axis, radians);
            for (var index = 0; index <= PieceCount; index++)
            {
                _turned[index] = guard + Vector3.Transform(shape[index] - guard, turn);
            }

            for (var piece = 0; piece < PieceCount; piece++)
            {
                var middle = (_turned[piece] + _turned[piece + 1]) * 0.5f;
                var halfLength = Vector3.Distance(_turned[piece], _turned[piece + 1]) * 0.5f;
                for (var otherPiece = 0; otherPiece < PieceCount; otherPiece++)
                {
                    // Too far apart, seen along the side, to touch or lie over each other: no need to look closer.
                    var otherMiddle = (_otherPoints[otherPiece] + _otherPoints[otherPiece + 1]) * 0.5f;
                    var reach = halfLength + (Vector3.Distance(_otherPoints[otherPiece], _otherPoints[otherPiece + 1]) * 0.5f) + ContactMeters;
                    if (Flatten(middle - otherMiddle, _otherBladeSide).LengthSquared() > reach * reach)
                    {
                        continue;
                    }

                    FindClosest(_turned[piece], _turned[piece + 1], _otherPoints[otherPiece], _otherPoints[otherPiece + 1], out var onThis, out var onOther, out _, out _);
                    var apart = onThis - onOther;
                    if (apart.LengthSquared() < ContactMeters * ContactMeters * 0.25f)
                    {
                        return true;
                    }

                    if (Vector3.Dot(apart, _otherBladeSide) < 0.0f && Flatten(apart, _otherBladeSide).Length() < ContactMeters)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// The axis (through the sliding blade's guard) it turns about to slide off the hilt's part: its press spot moves
        /// square to the press and to the blade, away from the hilt's axis, or the way <paramref name="otherBladeSide"/>
        /// says when there is one, so it never slides through the other blade; end-on (<paramref name="isEndOn"/>),
        /// straight away from the hilt's axis. Zero when there is no such turn.
        /// </summary>
        private static Vector3 FindHiltSlideAxis(Vector3[] shape, Vector3 guard, Hilt hilt, HiltPart part, Vector3 side, Vector3 point, Vector3 otherBladeSide, out bool isEndOn)
        {
            var away = Flatten(point - hilt.FindClosestOnAxis(point, part), side);
            var along = Flatten(shape[PieceCount] - shape[0], side);
            var bladeLength = Vector3.Distance(shape[PieceCount], shape[0]);
            isEndOn = along.Length() < EndOnShare * bladeLength;
            if (!isEndOn && part == HiltPart.Bell && Math.Abs(Vector3.Dot(side, hilt.Axis)) < BackShare)
            {
                // Across the bell's own depth, pressed on its rim: it slides off its front face, not round to the back
                // (the hand is there) and not the long way round the rim.
                away = hilt.Axis;
            }
            else if (!isEndOn)
            {
                var direction = Vector3.Normalize(along);
                away -= Vector3.Dot(away, direction) * direction;
                var hint = Flatten(otherBladeSide, side);
                hint -= Vector3.Dot(hint, direction) * direction;
                if (hint.LengthSquared() > 1e-6f)
                {
                    away = Vector3.Dot(away, hint) < 0.0f || away.LengthSquared() < 1e-8f ? hint : away;
                }
            }

            if (away.LengthSquared() < 1e-10f)
            {
                return Vector3.Zero;
            }

            var axis = Vector3.Cross(point - guard, Vector3.Normalize(away));
            return axis.LengthSquared() > 1e-10f ? Vector3.Normalize(axis) : Vector3.Zero;
        }

        /// <summary>
        /// The least turn of <paramref name="shape"/> about <paramref name="axis"/> through <paramref name="guard"/> that
        /// clears the hilt's part: seen along <paramref name="side"/> (no spot over it) when <paramref name="isSeenAlong"/>,
        /// else only out of it along the side. At most <see cref="MaxReleaseDegrees"/>.
        /// </summary>
        private float FindHiltClearingTurn(Vector3[] shape, Vector3 guard, Hilt hilt, HiltPart part, Vector3 side, Vector3 axis, bool isSeenAlong)
        {
            if (IsClearOfHilt(shape, guard, hilt, part, side, axis, 0.0f, isSeenAlong))
            {
                return 0.0f;
            }

            if (!IsClearOfHilt(shape, guard, hilt, part, side, axis, MaxReleaseRadians, isSeenAlong))
            {
                return MaxReleaseRadians;
            }

            var blocked = 0.0f;
            var clear = MaxReleaseRadians;
            for (var step = 0; step < ReleaseSearchSteps; step++)
            {
                var middle = (blocked + clear) * 0.5f;
                if (IsClearOfHilt(shape, guard, hilt, part, side, axis, middle, isSeenAlong))
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

        private bool IsClearOfHilt(Vector3[] shape, Vector3 guard, Hilt hilt, HiltPart part, Vector3 side, Vector3 axis, float radians, bool isSeenAlong)
        {
            var turn = Quaternion.CreateFromAxisAngle(axis, radians);
            for (var index = 0; index <= PieceCount; index++)
            {
                _turned[index] = guard + Vector3.Transform(shape[index] - guard, turn);
            }

            var deepest = FindHiltDeepest(_turned, hilt, part, side);
            var isClear = isSeenAlong ? !deepest.IsOver : deepest.Overlap <= HiltSettledMeters * 0.5f;
            return isClear && !IsThroughOtherBlade(shape, guard, axis, radians);
        }

        /// <summary>
        /// The spot of <paramref name="shape"/> deepest in the hilt's part along <paramref name="side"/>: for each spot
        /// whose line along the side runs through the part, how far it must move along the side to be out of it
        /// (negative: already clear by that much). A spot behind the part counts as pushed through it, however far.
        /// </summary>
        private static HiltHit FindHiltDeepest(Vector3[] shape, Hilt hilt, HiltPart part, Vector3 side)
        {
            var best = new HiltHit { Overlap = -float.MaxValue };
            hilt.GetBounds(part, out var partMiddle, out var partReach);
            var flatMiddle = Flatten(partMiddle, side);
            for (var piece = 0; piece < PieceCount; piece++)
            {
                // A piece that, seen along the side, is nowhere over the part's bounding sphere has no spot over the part.
                var reach = partReach + (Vector3.Distance(shape[piece], shape[piece + 1]) * 0.5f);
                if (Vector3.DistanceSquared(Flatten((shape[piece] + shape[piece + 1]) * 0.5f, side), flatMiddle) > reach * reach)
                {
                    continue;
                }

                for (var step = piece == 0 ? 0 : 1; step <= HiltStepsPerPiece; step++)
                {
                    var along = (float)step / HiltStepsPerPiece;
                    var point = Vector3.Lerp(shape[piece], shape[piece + 1], along);
                    if (hilt.TryFindExit(point, side, part, out var exit) && exit > best.Overlap)
                    {
                        best = new HiltHit { IsOver = true, Overlap = exit, Point = point, Fraction = (piece + along) / PieceCount };
                    }
                }
            }

            return best;
        }

        /// <summary>The blade as given so far, as <see cref="PieceCount"/> + 1 points.</summary>
        private static void FillPoints(Work work, Vector3[] shape)
        {
            var give = work.ToGive();
            for (var index = 0; index <= PieceCount; index++)
            {
                var fraction = (float)index / PieceCount;
                shape[index] = Vector3.Lerp(give.Chord.GuardPosition, give.Chord.TipPosition, fraction) + give.Droop.GetChordOffset(fraction);
            }
        }

        /// <summary>What a frame's press came to (<see cref="SolveContact"/>).</summary>
        private enum ContactOutcome
        {
            /// <summary>Taken by the give, or sliding off: the side is held.</summary>
            Held,

            /// <summary>Slid all the way off its point and let go.</summary>
            Released,

            /// <summary>Pushed through past every give, the slide and the margin (PER-147).</summary>
            Through,
        }

        /// <summary>
        /// A blade's give as a <see cref="Work"/> held it: its turn in the grip and its bows. What a blade let go swings
        /// back from (<see cref="StepReturn"/>).
        /// </summary>
        private readonly struct GiveMemory
        {
            public static readonly GiveMemory None = new GiveMemory(Quaternion.Identity, Vector3.Zero, 1.0f, Vector3.Zero, 1.0f);

            public GiveMemory(Quaternion turn, Vector3 firstBow, float firstFraction, Vector3 secondBow, float secondFraction)
            {
                Turn = turn;
                FirstBow = firstBow;
                FirstFraction = firstFraction;
                SecondBow = secondBow;
                SecondFraction = secondFraction;
            }

            public Quaternion Turn { get; }

            public Vector3 FirstBow { get; }

            public float FirstFraction { get; }

            public Vector3 SecondBow { get; }

            public float SecondFraction { get; }

            public bool IsNone => Turn.IsIdentity && FirstBow == Vector3.Zero && SecondBow == Vector3.Zero;

            /// <summary>How far the blade is turned in the grip, radians.</summary>
            public float TurnRadians => 2.0f * (float)Math.Acos(Math.Min(1.0f, Math.Abs(Turn.W)));

            /// <summary>How far the bows put the point aside.</summary>
            public float TipBowMeters => new ContactBow(FirstBow, FirstFraction, SecondBow, SecondFraction).TipDeflection.Length();

            public static GiveMemory FromTurn(Quaternion turn)
            {
                return new GiveMemory(turn, Vector3.Zero, 1.0f, Vector3.Zero, 1.0f);
            }
        }

        /// <summary>How the hands' blades went across each other between two frames (<see cref="FindSweptCrossing"/>).</summary>
        private enum Crossing
        {
            None,
            Through,
            Round,
        }

        /// <summary>The two solid parts of a hilt.</summary>
        private enum HiltPart
        {
            Bell,
            Grip,
        }

        /// <summary>
        /// A blade's hilt as the other blade meets it, where the hand holds it: the bell (<see cref="BellGuard"/>, its
        /// front face on the guard) and the grip behind it, one round cylinder each about the hilt's axis, each grown by
        /// the other blade's half-width (<see cref="DrawnBlade.BladeRadiusMeters"/>).
        /// </summary>
        private readonly struct Hilt
        {
            // The other blade's half-width, and 2 mm for its drawn curve between the solver's straight pieces.
            private const float Margin = DrawnBlade.BladeRadiusMeters + 0.002f;
            private const float BellRadius = BellGuard.RadiusMeters + Margin;
            private const float BellFront = Margin;
            private const float BellBack = -BellGuard.DepthMeters - Margin;
            private const float GripRadius = GripRadiusMeters + Margin;
            private const float GripFront = -BellGuard.DepthMeters;
            private const float GripBack = GripFront - GripLengthMeters - Margin;

            public Hilt(BladeGive blade)
            {
                Guard = blade.Chord.GuardPosition;
                var axis = blade.Droop.GetHiltDirection(blade.Chord.Direction);
                Axis = axis.LengthSquared() > 1e-12f ? Vector3.Normalize(axis) : Vector3.UnitZ;
            }

            public Vector3 Guard { get; }

            /// <summary>Along the blade, out of the bell's front face.</summary>
            public Vector3 Axis { get; }

            /// <summary>
            /// False when no piece of <paramref name="shape"/> comes within <paramref name="bandMeters"/> of the hilt's
            /// bounding sphere (about its middle, reaching the bell's rim and the grip's end).
            /// </summary>
            public bool IsNear(Vector3[] shape, float bandMeters)
            {
                var middle = Guard + (Axis * (GripBack * 0.5f));
                var reach = (float)Math.Sqrt((BellRadius * BellRadius) + (GripBack * GripBack * 0.25f)) + bandMeters;
                for (var piece = 0; piece < PieceCount; piece++)
                {
                    var onPiece = SegmentMath.FindClosestPointOnSegment(middle, shape[piece], shape[piece + 1]);
                    if (Vector3.DistanceSquared(onPiece, middle) <= reach * reach)
                    {
                        return true;
                    }
                }

                return false;
            }

            /// <summary>The middle of the grown part and the radius of a sphere about it that holds all of it.</summary>
            public void GetBounds(HiltPart part, out Vector3 middle, out float reach)
            {
                GetRange(part, out var back, out var front, out var radius);
                var half = (front - back) * 0.5f;
                middle = Guard + (Axis * (back + half));
                reach = (float)Math.Sqrt((half * half) + (radius * radius));
            }

            /// <summary>The point on the part's axis nearest <paramref name="point"/>.</summary>
            public Vector3 FindClosestOnAxis(Vector3 point, HiltPart part)
            {
                GetRange(part, out var back, out var front, out _);
                return Guard + (Axis * Math.Clamp(Vector3.Dot(point - Guard, Axis), back, front));
            }

            /// <summary>
            /// Which way from the part <paramref name="shape"/> is. The bell is a thin disc: a blade that does not run
            /// through its depth is in front of it or behind it; one that does outside the rim went round it, out from
            /// the rim there; one that does inside the rim went through, so the side held (<paramref name="held"/>)
            /// stands. The grip: out of it where the blade comes closest. Within <paramref name="bandMeters"/> of either,
            /// a side opposite the one held is a blade pushed right through in one frame, not round: the held one stands.
            /// </summary>
            public Vector3 FindSide(Vector3[] shape, HiltPart part, Vector3? held, float bandMeters)
            {
                var side = part == HiltPart.Bell ? FindBellSide(shape, held, out var distance) : FindGripSide(shape, held, out distance);
                return held.HasValue && distance < bandMeters && Vector3.Dot(side, held.Value) < 0.0f ? held.Value : side;
            }

            /// <summary>
            /// How far along <paramref name="side"/> <paramref name="point"/> must move to be out of the grown part, when its
            /// line along the side runs through it (false when it misses).
            /// </summary>
            public bool TryFindExit(Vector3 point, Vector3 side, HiltPart part, out float exit)
            {
                GetRange(part, out var back, out var front, out var radius);
                exit = 0.0f;
                var offset = point - Guard;
                var along = Vector3.Dot(offset, Axis);
                var sideAlong = Vector3.Dot(side, Axis);
                var low = -float.MaxValue;
                var high = float.MaxValue;
                if (Math.Abs(sideAlong) < 1e-6f)
                {
                    if (along < back || along > front)
                    {
                        return false;
                    }
                }
                else
                {
                    var first = (back - along) / sideAlong;
                    var second = (front - along) / sideAlong;
                    low = Math.Min(first, second);
                    high = Math.Max(first, second);
                }

                var radial = offset - (along * Axis);
                var radialStep = side - (sideAlong * Axis);
                var a = radialStep.LengthSquared();
                var b = Vector3.Dot(radial, radialStep);
                var c = radial.LengthSquared() - (radius * radius);
                if (a < 1e-9f)
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

                exit = high;
                return low <= high;
            }

            private static void GetRange(HiltPart part, out float back, out float front, out float radius)
            {
                back = part == HiltPart.Bell ? BellBack : GripBack;
                front = part == HiltPart.Bell ? BellFront : GripFront;
                radius = part == HiltPart.Bell ? BellRadius : GripRadius;
            }

            private Vector3 FindBellSide(Vector3[] shape, Vector3? held, out float distance)
            {
                // Where the blade runs through the bell's own depth (between its faces) nearest its axis, if it does.
                const float Front = 0.0f;
                const float Back = -BellGuard.DepthMeters;
                var nearest = float.MaxValue;
                var outward = Vector3.Zero;
                for (var piece = 0; piece < PieceCount; piece++)
                {
                    var start = shape[piece] - Guard;
                    var step = shape[piece + 1] - shape[piece];
                    var startAlong = Vector3.Dot(start, Axis);
                    var stepAlong = Vector3.Dot(step, Axis);
                    var low = 0.0f;
                    var high = 1.0f;
                    if (Math.Abs(stepAlong) < 1e-9f)
                    {
                        if (startAlong < Back || startAlong > Front)
                        {
                            continue;
                        }
                    }
                    else
                    {
                        var first = (Back - startAlong) / stepAlong;
                        var second = (Front - startAlong) / stepAlong;
                        low = Math.Max(low, Math.Min(first, second));
                        high = Math.Min(high, Math.Max(first, second));
                        if (low > high)
                        {
                            continue;
                        }
                    }

                    var radialStart = start - (startAlong * Axis);
                    var radialStep = step - (stepAlong * Axis);
                    var stepSquared = radialStep.LengthSquared();
                    var at = stepSquared > 1e-12f ? Math.Clamp(-Vector3.Dot(radialStart, radialStep) / stepSquared, low, high) : low;
                    var radial = radialStart + (radialStep * at);
                    var radius = radial.Length();
                    if (radius < nearest)
                    {
                        nearest = radius;
                        outward = radius > 1e-6f ? radial / radius : Vector3.Zero;
                    }
                }

                if (nearest == float.MaxValue)
                {
                    // Wholly in front of the bell or behind it.
                    distance = float.MaxValue;
                    var center = BellGuard.FindCenter(Guard, Axis);
                    foreach (var point in shape)
                    {
                        distance = Math.Min(distance, BellGuard.FindDistance(point, center, Axis, out _));
                    }

                    return Vector3.Dot(shape[0] - Guard, Axis) >= (Front + Back) * 0.5f ? Axis : -Axis;
                }

                distance = nearest - BellRadius;
                if (nearest >= BellRadius - HiltSettledMeters && outward != Vector3.Zero)
                {
                    return outward;
                }

                return held ?? Axis;
            }

            private Vector3 FindGripSide(Vector3[] shape, Vector3? held, out float distance)
            {
                var closest = float.MaxValue;
                var outward = Axis;
                var half = (GripFront - GripBack) * 0.5f;
                var center = Guard + (Axis * (GripBack + half));
                for (var piece = 0; piece < PieceCount; piece++)
                {
                    for (var step = piece == 0 ? 0 : 1; step <= HiltStepsPerPiece; step++)
                    {
                        var point = Vector3.Lerp(shape[piece], shape[piece + 1], (float)step / HiltStepsPerPiece);
                        var pointDistance = BellGuard.FindCylinderDistance(point, center, Axis, GripRadius, half, out var surfaceOutward);
                        if (pointDistance < closest)
                        {
                            closest = pointDistance;
                            outward = surfaceOutward;
                        }
                    }
                }

                distance = closest;
                return closest < -HiltSettledMeters && held.HasValue ? held.Value : outward;
            }
        }

        /// <summary>What one blade remembers about the other's hilt: which way from each part it is, and whether it is near.</summary>
        private sealed class HiltMemory
        {
            public HiltPartMemory Bell { get; } = new HiltPartMemory();

            public HiltPartMemory Grip { get; } = new HiltPartMemory();

            public bool IsEngaged => Bell.IsEngaged || Grip.IsEngaged;

            public float PressMeters => Math.Max(Bell.PressMeters, Grip.PressMeters);

            public void Reset()
            {
                Bell.Reset();
                Grip.Reset();
            }
        }

        /// <summary>What one blade remembers about one part of the other's hilt.</summary>
        private sealed class HiltPartMemory
        {
            public Vector3? Side;
            public bool IsEngaged;
            public float PressMeters;

            public void Reset()
            {
                Side = null;
                IsEngaged = false;
                PressMeters = 0.0f;
            }
        }

        /// <summary>A blade's deepest spot in a part of the other's hilt.</summary>
        private struct HiltHit
        {
            public bool IsOver;
            public float Overlap;
            public Vector3 Point;
            public float Fraction;
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
                Restart();
            }

            /// <summary>Drops the give put on so far this frame: the blade as the hand holds it.</summary>
            public void Restart()
            {
                _turn = Quaternion.Identity;
                _limitedTurnRadians = 0.0f;
                _firstBow = Vector3.Zero;
                _firstBowFraction = 1.0f;
                _secondBow = Vector3.Zero;
                _secondBowFraction = 1.0f;
                _isGiveStale = true;
            }

            public Vector3 Guard => _hold.Chord.GuardPosition;

            public float Length => Math.Max(1e-3f, Vector3.Distance(_hold.Chord.GuardPosition, _hold.Chord.TipPosition));

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

            /// <summary>
            /// Turns the blade about the hand (its guard) by <paramref name="radians"/> about <paramref name="axis"/>, on top
            /// of its turn so far. The whole turn never passes <see cref="MaxReleaseDegrees"/> (PER-147): as far as lying
            /// along the other blade, whatever the slide, the overlap left over or a hilt asks.
            /// </summary>
            public void TurnAbout(Vector3 axis, float radians)
            {
                if (radians == 0.0f)
                {
                    return;
                }

                _turn = CapTurn(Quaternion.Normalize(Quaternion.Concatenate(_turn, Quaternion.CreateFromAxisAngle(axis, radians))));
                _isGiveStale = true;
            }

            /// <summary>This blade's give as it stands: its turn and its bows.</summary>
            public GiveMemory Remember()
            {
                return new GiveMemory(_turn, _firstBow, _firstBowFraction, _secondBow, _secondBowFraction);
            }

            /// <summary>
            /// Puts <paramref name="turnShare"/> (0 none to 1 all) of <paramref name="give"/>'s turn and
            /// <paramref name="bowShare"/> of its bows on a blade with no give yet this frame (just started); the bows square
            /// to the blade as the hand now holds it.
            /// </summary>
            public void Apply(GiveMemory give, float turnShare, float bowShare)
            {
                if (give.IsNone)
                {
                    return;
                }

                if (turnShare > 0.0f)
                {
                    _turn = CapTurn(Quaternion.Normalize(Quaternion.Slerp(Quaternion.Identity, give.Turn, turnShare)));
                }

                if (bowShare > 0.0f)
                {
                    var direction = Vector3.Normalize(Vector3.Transform(_hold.Chord.TipPosition - Guard, _turn));
                    _firstBow = MakeSquareTo(give.FirstBow, direction) * bowShare;
                    _firstBowFraction = give.FirstFraction;
                    _secondBow = MakeSquareTo(give.SecondBow, direction) * bowShare;
                    _secondBowFraction = give.SecondFraction;
                }

                _isGiveStale = true;
            }

            private static Vector3 MakeSquareTo(Vector3 bow, Vector3 direction)
            {
                return bow - (Vector3.Dot(bow, direction) * direction);
            }

            /// <summary><paramref name="turn"/>, or as far round the same axis as <see cref="MaxReleaseDegrees"/> when it is further.</summary>
            private static Quaternion CapTurn(Quaternion turn)
            {
                var halfCosine = Math.Abs(turn.W);
                if (halfCosine >= MaxReleaseHalfCosine)
                {
                    return turn;
                }

                var axis = new Vector3(turn.X, turn.Y, turn.Z) * (turn.W < 0.0f ? -1.0f : 1.0f);
                return axis.LengthSquared() > 1e-12f ? Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MaxReleaseRadians) : Quaternion.Identity;
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
