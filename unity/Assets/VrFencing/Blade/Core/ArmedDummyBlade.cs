using System;
using System.Collections.Generic;
using System.Numerics;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Blade
{
    /// <summary>
    /// The Armed Dummy's épée against the Fencer's (PER-104, redesigned in PER-122). Two layers, as with a human partner:
    /// <list type="bullet">
    /// <item><b>The Dummy's hand</b> (its behavior). Clashes knock its point off line, wrist and arm, held open and
    /// recovered (<see cref="BladeDeflection"/>, PER-116), and it reads how hard the Fencer presses its blade
    /// (<see cref="BladeContactSolver.PressNewtons"/>, last frame's) and gives way to it: its point moves square to its
    /// blade by the press over its yield stiffness (<see cref="GetYieldStiffness"/>, the "Opponent's hand" Setting),
    /// and let go after a big press the line stays open and recovers, as after a beat.</item>
    /// <item><b>The blades' contact</b>, solved for both blades alike from the two hands (<see cref="BladeContactSolver"/>):
    /// each blade turns in its grip and bows, neither hand moves, they never pass through each other and never wedge.
    /// The Dummy's blade bends from contact too. The Fencer's drawn blade takes <see cref="FencerGive"/>.</item>
    /// </list>
    /// The Dummy's drawn blade (<see cref="Drawn"/>) keeps out of the floor. Each blade's hilt (bell guard and grip) is
    /// solid for the other blade inside the contact solver, so neither hilt ever leaves its hand for it. Positions in the
    /// Strip frame, meters, seconds.
    /// </summary>
    public sealed class ArmedDummyBlade
    {
        /// <summary>The Dummy's grip with a Light hand, N m per radian: its blade turns in the hand twice as readily as the Fencer's.</summary>
        public const float LightGripStiffness = 0.5f * BladeContactSolver.FencerGripStiffness;

        /// <summary>The Dummy's grip with a Medium hand: as the Fencer's.</summary>
        public const float MediumGripStiffness = BladeContactSolver.FencerGripStiffness;

        /// <summary>The Dummy's grip with a Strong hand: half as ready to turn as the Fencer's.</summary>
        public const float StrongGripStiffness = 2.0f * BladeContactSolver.FencerGripStiffness;

        /// <summary>
        /// How readily a Light hand gives way to a press, N per meter of its point's move: a press on its forte that the
        /// blades take up at about 1 N moves its point 5 cm, about as far as the press itself.
        /// </summary>
        public const float LightYieldStiffness = 20.0f;

        /// <summary>A Medium hand gives way half as far as a Light one.</summary>
        public const float MediumYieldStiffness = 40.0f;

        /// <summary>A Strong hand gives way a quarter as far as a Light one.</summary>
        public const float StrongYieldStiffness = 80.0f;

        /// <summary>
        /// A Medium hand holds firm against a press up to this force and gives way only to the rest (PER-122 fix): a press
        /// first bows the Dummy's blade, as against a fencer's set hand. About what bows its foible to the most a blade bows
        /// at a contact (<see cref="BladeContactSolver.MaxBowMeters"/>): its blade's bend, not its hand, takes a firm press.
        /// Without it the hand gave as readily as the foible, so a press only turned the blade.
        /// </summary>
        public const float MediumHoldNewtons = 2.0f;

        /// <summary>A Light hand holds half as hard as a Medium one.</summary>
        public const float LightHoldNewtons = 0.5f * MediumHoldNewtons;

        /// <summary>A Strong hand holds twice as hard as a Medium one.</summary>
        public const float StrongHoldNewtons = 2.0f * MediumHoldNewtons;

        /// <summary>
        /// The most the Dummy's hand gives way to a press, at its point: a hard press opens its line this far (past the
        /// Beat's 15 cm), its blade then slides off the Fencer's. A beat can still knock it to 60 cm.
        /// </summary>
        public const float MaxYieldMeters = 0.25f;

        private readonly BladeDeflection _deflection = new BladeDeflection();
        private readonly BladeContactSolver _solver = new BladeContactSolver();
        private readonly DrawnBlade _drawn = new DrawnBlade();
        private readonly List<BladeObstacle> _obstacles = new List<BladeObstacle>();

        /// <summary>
        /// True for a hand that never moves or gives (Free Practice's "hand fixed", a test stand for blade on blade
        /// contact): the Dummy holds its line exactly, clashes knock nothing and presses move nothing; only the two blades
        /// react, through the contact solver. As a fencer with an infinitely stiff hand: no hand is moved by the contact.
        /// </summary>
        public bool IsHandFixed { get; set; }

        /// <summary>The Dummy's hand: its point off its line, knocked by clashes and giving way to presses.</summary>
        public BladeDeflection Deflection => _deflection;

        /// <summary>The two blades' contact.</summary>
        public BladeContactSolver Contact => _solver;

        /// <summary>The blade as drawn.</summary>
        public DrawnBlade Drawn => _drawn;

        /// <summary>Where the Dummy's hand holds its blade this frame: its line, moved by the hand (guard to the real point).</summary>
        public BladeSegment HandChord { get; private set; }

        /// <summary>
        /// The Dummy's blade this frame as the contact leaves it (guard to the real point): turned and bowed off the
        /// Fencer's. What its point's hits and the clashes are judged on.
        /// </summary>
        public BladeSegment Chord { get; private set; }

        /// <summary>What the Fencer's drawn blade takes this frame: the hand's blade turned and bowed by the contact.</summary>
        public BladeGive FencerGive { get; private set; }

        /// <summary>
        /// The Dummy's point off its line as seen: the hand's knock and give (<see cref="BladeDeflection.TipOffset"/>)
        /// plus the blade's own give in the contact. What Parries and Beats are judged on.
        /// </summary>
        public Vector3 JudgedTipOffset => _deflection.TipOffset + (Chord.TipPosition - HandChord.TipPosition);

        /// <summary>
        /// The knocked arm's turn about the shoulder this frame (<see cref="BladeDeflection.GetArmTurn"/>, Strip frame):
        /// the arm is drawn turned by it so the hand stays on the deflected guard.
        /// </summary>
        public Quaternion ArmTurn { get; private set; } = Quaternion.Identity;

        /// <summary>True while the Fencer's blade presses on this one (<see cref="BladeContactSolver.IsInContact"/>).</summary>
        public bool IsPressed => _solver.IsInContact;

        /// <summary>The Dummy's grip stiffness for the "Opponent's hand" Setting.</summary>
        public static float GetGripStiffness(OpponentHand hand)
        {
            switch (hand)
            {
                case OpponentHand.Light: return LightGripStiffness;
                case OpponentHand.Strong: return StrongGripStiffness;
                default: return MediumGripStiffness;
            }
        }

        /// <summary>How readily the Dummy's hand gives way to a press for the "Opponent's hand" Setting, N per meter at its point.</summary>
        public static float GetYieldStiffness(OpponentHand hand)
        {
            switch (hand)
            {
                case OpponentHand.Light: return LightYieldStiffness;
                case OpponentHand.Strong: return StrongYieldStiffness;
                default: return MediumYieldStiffness;
            }
        }

        /// <summary>How hard a press the Dummy's hand holds against without giving way, for the "Opponent's hand" Setting, N.</summary>
        public static float GetHoldNewtons(OpponentHand hand)
        {
            switch (hand)
            {
                case OpponentHand.Light: return LightHoldNewtons;
                case OpponentHand.Strong: return StrongHoldNewtons;
                default: return MediumHoldNewtons;
            }
        }

        /// <summary>
        /// Where the Dummy's hand holds its point for a press of <paramref name="pressNewtons"/> pushing its blade along
        /// <paramref name="push"/>: square to its blade <paramref name="bladeDirection"/>, the press past what the hand
        /// holds (<see cref="GetHoldNewtons"/>) over the yield stiffness; zero with no press or a press it holds.
        /// </summary>
        public static Vector3 FindYieldTipOffset(float pressNewtons, Vector3 push, Vector3 bladeDirection, OpponentHand hand)
        {
            var square = push - (Vector3.Dot(push, bladeDirection) * bladeDirection);
            var pastHold = pressNewtons - GetHoldNewtons(hand);
            if (!(pastHold > 0.0f) || square.LengthSquared() < 1e-8f)
            {
                return Vector3.Zero;
            }

            return Vector3.Normalize(square) * Math.Min(MaxYieldMeters, pastHold / GetYieldStiffness(hand));
        }

        /// <summary>Straight in line again at once, no side known: the blade appeared or jumped.</summary>
        public void Reset()
        {
            _deflection.Reset();
            _solver.Reset();
            _drawn.Reset();
            ArmTurn = Quaternion.Identity;
        }

        /// <summary>
        /// One frame: the Dummy's hand reads last frame's press and moves, then the two blades' contact is solved and
        /// the Dummy's blade drawn. Call before the Fencer's drawn blade steps with <see cref="FencerGive"/>.
        /// </summary>
        /// <param name="forte">The guard to the undrooped point, where the Dummy's arm holds it in line (not deflected).</param>
        /// <param name="shoulder">The Dummy's sword shoulder: the knocked arm turns about it.</param>
        /// <param name="gripUp">The Dummy's grip's up.</param>
        /// <param name="down">Gravity's direction.</param>
        /// <param name="blade">The blade Settings (set and flex), for both blades.</param>
        /// <param name="hand">How strong the Dummy's hand is (the "Opponent's hand" Setting).</param>
        /// <param name="fencerHand">The Fencer's blade from the hand (its chord to the real point).</param>
        /// <param name="fencerDroop">The Fencer's blade's rest shape on that chord.</param>
        public void Step(
            BladeSegment forte,
            Vector3 shoulder,
            Vector3 gripUp,
            Vector3 down,
            BladeSettings blade,
            OpponentHand hand,
            BladeSegment fencerHand,
            BladeDroop fencerDroop,
            float deltaSeconds)
        {
            if (blade == null)
            {
                throw new ArgumentNullException(nameof(blade));
            }

            if (IsHandFixed)
            {
                // A hand that never moves or gives: no knock, no give to a press, only the blades react.
                _deflection.Reset();
            }
            else
            {
                // The Dummy's hand: it gives way to last frame's press as a fencer's hand would, and its knocks run on.
                var yield = _solver.IsInContact
                    ? FindYieldTipOffset(_solver.PressNewtons, _solver.PushOnB, Chord.Direction, hand)
                    : Vector3.Zero;
                _deflection.HoldAt(yield);
                _deflection.Step(deltaSeconds);
            }

            var droop = Deflect(forte, shoulder, gripUp, down, blade, out var handChord);
            HandChord = handChord;
            ArmTurn = _deflection.GetArmTurn(forte, shoulder);

            // The blades: the Fencer's is A, the Dummy's is B.
            var bendStiffness = BladeContactSolver.GetBendStiffness(blade.Flex);
            _solver.Solve(
                new BladeHold(fencerHand, fencerDroop, BladeContactSolver.FencerGripStiffness, bendStiffness),
                new BladeHold(handChord, droop, GetGripStiffness(hand), bendStiffness),
                deltaSeconds);
            FencerGive = _solver.GiveA;
            var give = _solver.GiveB;
            Chord = give.Chord;

            if (_solver.IsNear)
            {
                // Near the Fencer's blade it does not whip, so a whip never carries it through: drawn as the contact leaves it.
                _drawn.Flex.Reset();
            }

            _obstacles.Clear();
            _obstacles.Add(BladeObstacle.Floor);
            _drawn.Step(give.Chord, _obstacles, gripUp, deltaSeconds, give.Droop);
        }

        /// <summary>
        /// The Dummy's right-handed arm pose <paramref name="pose"/> (as its script has it) knocked aside with the blade
        /// this frame (<see cref="ArmTurn"/>): what the arm is drawn and its Target Zones judged in.
        /// </summary>
        public SwordArmPose GetKnockedArmPose(SwordArmPose pose, Hand hand)
        {
            var turn = ToDummyFrame(ArmTurn);
            return ArmTurn.IsIdentity ? pose : pose.TurnedAtShoulder(hand == Hand.Left ? HandMirroring.MirrorRotation(turn) : turn);
        }

        /// <summary>
        /// The upper body's small turn with the knocked arm (<see cref="DummyTorso.GetKnockedTurn"/>, PER-116), in the
        /// Dummy frame of <paramref name="layout"/>: identity with the arm in line.
        /// </summary>
        public Quaternion GetKnockedTorsoTurn(DummyLayout layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            var arm = _deflection.ArmOffset;
            var armInDummyFrame = new Vector3(-arm.X, arm.Y, -arm.Z);
            return DummyTorso.GetKnockedTurn(layout.Torso.HipCenter, layout.Torso.SwordShoulder, armInDummyFrame);
        }

        /// <summary>
        /// The point <paramref name="fraction"/> of the way along the drawn blade (0 the guard, 1 the point), as drawn this
        /// frame: where the clash spark sits so it moves with the blade.
        /// </summary>
        public Vector3 GetPointAt(float fraction)
        {
            return _drawn.GetCenterlinePoint(fraction);
        }

        /// <summary>The blade turned by the hand's deflection, and its droop; <paramref name="chord"/> is the guard to the real point.</summary>
        private BladeDroop Deflect(BladeSegment forte, Vector3 shoulder, Vector3 gripUp, Vector3 down, BladeSettings blade, out BladeSegment chord)
        {
            var swung = _deflection.Apply(forte, shoulder);
            var droop = BladeDroop.Create(swung.Direction, gripUp, down, blade);
            chord = droop.GetChord(swung);
            return droop;
        }

        /// <summary>A turn in the Strip frame as seen in the Dummy frame (half a turn about the vertical from it).</summary>
        private static Quaternion ToDummyFrame(Quaternion turnOnStrip)
        {
            return new Quaternion(-turnOnStrip.X, turnOnStrip.Y, -turnOnStrip.Z, turnOnStrip.W);
        }
    }
}
