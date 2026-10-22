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

        /// <summary>
        /// A beat carries the Dummy's blade on only where the Fencer's blade is into its outer part, from this share of
        /// the way to its point: the part a knock of the point moves. Nearer its guard (a point driven at its forte) the
        /// two blades' give and slide take the press, as for any press.
        /// </summary>
        public const float MinCarryFraction = 0.5f;

        /// <summary>
        /// A beat carries the Dummy's blade on where the Fencer's blade is this share of its lead off to the side, seen
        /// along the knock, as well as where they lie over each other: a fast sweep slips sideways a little as it pushes.
        /// </summary>
        public const float CarrySlack = 0.5f;

        // The outer part of the Dummy's blade is looked at in this many steps (about 6 cm each).
        private const int CarrySamples = 8;

        // The least carry that clears the Fencer's blade is found to about 4 mm by halving this many times.
        private const int CarrySearchSteps = 8;

        // The knock must be at least this square to the blade to carry it (else it slides along it).
        private const float MinCarrySquare = 0.3f;

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

        /// <summary>For the blade-contact log (PER-136): where the Dummy's hand gives way to this frame's press, at its point (zero with none).</summary>
        public Vector3 YieldTipOffset { get; private set; }

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
        /// One frame: the Dummy's hand reads last frame's press and moves, a beat (<see cref="Knock"/>) carrying it on with
        /// the blade that hit it (PER-138: the knock is motion the contact sees, on the side it was hit, never a jump
        /// through); then the two blades' contact is solved and the Dummy's blade drawn. Call before the Fencer's drawn
        /// blade steps with <see cref="FencerGive"/>.
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
                YieldTipOffset = Vector3.Zero;
                _deflection.Reset();
            }
            else
            {
                // The Dummy's hand: it gives way to last frame's press as a fencer's hand would, and its knocks run on.
                var yield = _solver.IsInContact
                    ? FindYieldTipOffset(_solver.PressNewtons, _solver.PushOnB, Chord.Direction, hand)
                    : Vector3.Zero;
                YieldTipOffset = yield;
                _deflection.HoldAt(yield);
                _deflection.Step(deltaSeconds);
            }

            var droop = Deflect(forte, shoulder, gripUp, down, blade, out var handChord);

            // The blades: the Fencer's is A, the Dummy's is B.
            var bendStiffness = BladeContactSolver.GetBendStiffness(blade.Flex);
            var fencerHold = new BladeHold(fencerHand, fencerDroop, BladeContactSolver.FencerGripStiffness, bendStiffness);
            if (_deflection.IsKnockedOut && TryFindCarry(forte, shoulder, gripUp, down, blade, fencerHand, out var carry))
            {
                _deflection.CarryBy(carry);
                droop = Deflect(forte, shoulder, gripUp, down, blade, out handChord);
            }

            HandChord = handChord;
            ArmTurn = _deflection.GetArmTurn(forte, shoulder);
            _solver.Solve(fencerHold, new BladeHold(handChord, droop, GetGripStiffness(hand), bendStiffness), deltaSeconds);
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

        /// <summary>
        /// A clash (<see cref="BladeClashDetector"/>) knocks the Dummy's hand (<see cref="BladeDeflection.Kick"/>), the next
        /// step carrying it on with the blade that hit it. Only away from the Fencer's blade, on the side the contact holds
        /// (PER-138): a blade drawn back off the Dummy's (a pull-back grazing it) knocks nothing, so a knock never moves the
        /// Dummy's blade toward or through the Fencer's. Nothing for a hand that never moves (<see cref="IsHandFixed"/>).
        /// </summary>
        public void Knock(BladeClash clash)
        {
            if (IsHandFixed || (_solver.Side.HasValue && Vector3.Dot(clash.PushDirection, _solver.Side.Value) >= 0.0f))
            {
                return;
            }

            _deflection.Kick(clash);
        }

        /// <summary>
        /// While a beat knocks the Dummy's hand out, the blade that hit it carries it on (PER-138): where the Fencer's
        /// hand's blade is into or past the outer part of the Dummy's hand's blade (<see cref="MinCarryFraction"/>) the way
        /// it was knocked, the Dummy's point moves on that way by the least that clears it, at most to
        /// <see cref="BladeDeflection.MaxCarriedMeters"/>. False when there is nothing to carry.
        /// </summary>
        private bool TryFindCarry(BladeSegment forte, Vector3 shoulder, Vector3 gripUp, Vector3 down, BladeSettings blade, BladeSegment fencer, out Vector3 carry)
        {
            carry = Vector3.Zero;
            var start = _deflection.Apply(forte, shoulder);
            var bladeDirection = start.Direction;
            var direction = _deflection.KnockDirection - (Vector3.Dot(_deflection.KnockDirection, bladeDirection) * bladeDirection);
            if (direction.Length() < MinCarrySquare || !(FindCarryOverlap(0.0f) > 0.0f))
            {
                return false;
            }

            direction = Vector3.Normalize(direction);

            // As far as the largest carried offset allows: |offset + direction t| = MaxCarriedMeters.
            var offset = _deflection.TipOffset;
            var along = Vector3.Dot(offset, direction);
            var most = -along + (float)Math.Sqrt(Math.Max(0.0f, (along * along) - offset.LengthSquared() + (BladeDeflection.MaxCarriedMeters * BladeDeflection.MaxCarriedMeters)));
            var clear = most;
            if (FindCarryOverlap(most) <= 0.0f)
            {
                var blocked = 0.0f;
                for (var step = 0; step < CarrySearchSteps; step++)
                {
                    var middle = (blocked + clear) * 0.5f;
                    if (FindCarryOverlap(middle) > 0.0f)
                    {
                        blocked = middle;
                    }
                    else
                    {
                        clear = middle;
                    }
                }
            }

            carry = direction * clear;
            return clear > 0.0f;

            float FindCarryOverlap(float meters)
            {
                var swung = _deflection.Apply(forte, shoulder, direction * meters);
                var chord = BladeDroop.Create(swung.Direction, gripUp, down, blade).GetChord(swung);
                return FindLead(fencer, chord, direction);
            }
        }

        /// <summary>
        /// How far <paramref name="fencer"/> is ahead of the outer part of <paramref name="dummy"/> (from
        /// <see cref="MinCarryFraction"/> to its point) along <paramref name="knock"/>, plus touching, at the spot of that
        /// part furthest behind it where the two lie over each other seen along the knock (within the slack a fast
        /// sweep slips sideways, <see cref="CarrySlack"/>): above 0, the Fencer's blade is into or past the Dummy's that
        /// way. Far below 0 when they do not lie over each other.
        /// </summary>
        private static float FindLead(BladeSegment fencer, BladeSegment dummy, Vector3 knock)
        {
            var fencerStart = Flatten(fencer.GuardPosition, knock);
            var fencerEnd = Flatten(fencer.TipPosition, knock);
            var fencerAlong = fencerEnd - fencerStart;
            var fencerLengthSquared = Math.Max(1e-12f, fencerAlong.LengthSquared());
            var most = -float.MaxValue;
            for (var index = 0; index <= CarrySamples; index++)
            {
                var fraction = MinCarryFraction + ((1.0f - MinCarryFraction) * index / CarrySamples);
                var dummyPoint = Vector3.Lerp(dummy.GuardPosition, dummy.TipPosition, fraction);
                var flatDummy = Flatten(dummyPoint, knock);
                var onFencer = Math.Clamp(Vector3.Dot(flatDummy - fencerStart, fencerAlong) / fencerLengthSquared, 0.0f, 1.0f);
                var sideways = Vector3.Distance(flatDummy, fencerStart + (fencerAlong * onFencer));
                var fencerPoint = Vector3.Lerp(fencer.GuardPosition, fencer.TipPosition, onFencer);
                var lead = Vector3.Dot(fencerPoint - dummyPoint, knock) + BladeContactSolver.ContactMeters;
                if (sideways <= BladeContactSolver.ContactMeters + (CarrySlack * Math.Max(0.0f, lead)))
                {
                    most = Math.Max(most, lead);
                }
            }

            return most;
        }

        /// <summary><paramref name="point"/> seen along <paramref name="axis"/> (unit): its part along it taken away.</summary>
        private static Vector3 Flatten(Vector3 point, Vector3 axis)
        {
            return point - (Vector3.Dot(point, axis) * axis);
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
