using System;
using System.Collections.Generic;
using System.Numerics;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Blade
{
    /// <summary>
    /// The Armed Dummy's épée, solid against the Fencer's (PER-104): where the Dummy holds it, knocked off its line by
    /// clashes and presses, wrist and arm (<see cref="BladeDeflection"/>, PER-116), pushed aside by the Fencer's blade
    /// (<see cref="BladeBind"/>), and drawn like the Fencer's (<see cref="DrawnBlade"/>). Each frame:
    /// <list type="number">
    /// <item><see cref="Step"/>, before the Fencer's drawn blade moves: the press holds the point aside, the deflection
    /// steps, the blade is pushed out of the Fencer's drawn blade if it moved into it, and it is drawn;</item>
    /// <item><see cref="AddObstacles"/>: the drawn blade becomes thin obstacles for the Fencer's drawn blade;</item>
    /// <item><see cref="UpdateSide"/>, after the Fencer's drawn blade moved: which side of this blade the Fencer's is on.</item>
    /// </list>
    /// Positions in the Strip frame, meters, seconds.
    /// </summary>
    public sealed class ArmedDummyBlade
    {
        // The drawn blade's points taken as the pieces' ends.
        private const int SamplesPerPiece = DrawnBlade.SampleCount / BladeBind.PieceCount;

        // Pushing the blade out of the Fencer's is checked this many times: turning it about its guard moves the contact
        // a little along the blades, so a second look catches the rest.
        private const int ClearingPasses = 2;

        private static readonly BladeObstacle[] FloorOnly = { BladeObstacle.Floor };

        private readonly BladeDeflection _deflection = new BladeDeflection();
        private readonly BladeBind _bind = new BladeBind();
        private readonly DrawnBlade _drawn = new DrawnBlade();
        private readonly Vector3[] _line = new Vector3[BladeBind.PieceCount + 1];
        private readonly Vector3[] _shape = new Vector3[BladeBind.PieceCount + 1];
        private readonly Vector3[] _fencerDrawn = new Vector3[DrawnBlade.SampleCount + 1];
        private bool _hasShape;

        /// <summary>The point off its line: knocked by a clash, held aside by a press, the arm going with a big one.</summary>
        public BladeDeflection Deflection => _deflection;

        /// <summary>The side the Fencer's blade keeps to, and the press.</summary>
        public BladeBind Bind => _bind;

        /// <summary>The blade as drawn.</summary>
        public DrawnBlade Drawn => _drawn;

        /// <summary>The guard to the real point this frame (deflected, pushed and drooped): what the clash test uses.</summary>
        public BladeSegment Chord { get; private set; }

        /// <summary>
        /// The knocked arm's turn about the shoulder this frame (<see cref="BladeDeflection.GetArmTurn"/>, Strip frame):
        /// the arm is drawn turned by it so the hand stays on the deflected guard.
        /// </summary>
        public Quaternion ArmTurn { get; private set; } = Quaternion.Identity;

        /// <summary>The drawn blade as <see cref="BladeBind.PieceCount"/> + 1 points, guard first (after <see cref="Step"/>).</summary>
        public IReadOnlyList<Vector3> Shape => _shape;

        /// <summary>True while the Fencer's hand presses past this blade's line (<see cref="BladeBind.PressMeters"/>).</summary>
        public bool IsPressed => _bind.PressMeters > 0.0f;

        /// <summary>Straight in line again at once, no side known: the blade appeared or jumped.</summary>
        public void Reset()
        {
            _deflection.Reset();
            _bind.Reset();
            _drawn.Reset();
            _hasShape = false;
        }

        /// <summary>Moves the blade for this frame, before the Fencer's drawn blade moves.</summary>
        /// <param name="forte">The guard to the undrooped point, where the Dummy's arm holds it in line (not deflected).</param>
        /// <param name="shoulder">The Dummy's sword shoulder: the knocked arm turns about it.</param>
        /// <param name="gripUp">The Dummy's grip's up.</param>
        /// <param name="down">Gravity's direction.</param>
        /// <param name="fencerHand">The Fencer's blade from the hand (its chord to the real point).</param>
        /// <param name="fencerDrawn">The Fencer's drawn blade as drawn last frame; null or not yet drawn when there is none.</param>
        public void Step(BladeSegment forte, Vector3 shoulder, Vector3 gripUp, Vector3 down, BladeSettings blade, BladeSegment fencerHand, DrawnBlade fencerDrawn, float deltaSeconds)
        {
            if (blade == null)
            {
                throw new ArgumentNullException(nameof(blade));
            }

            var lineDroop = BladeDroop.Create(forte.Direction, gripUp, down, blade);
            FillRestShape(lineDroop.GetChord(forte), lineDroop, _line);
            _deflection.HoldAt(_bind.FindYieldTipOffset(fencerHand, _line));
            _deflection.Step(deltaSeconds);

            var droop = Deflect(forte, shoulder, gripUp, down, blade, out var chord);
            if (_bind.IsEngaged)
            {
                // Held against the Fencer's blade, it does not whip: drawn exactly where it is kept out of the Fencer's.
                _drawn.Flex.Reset();
            }

            if (fencerDrawn != null && fencerDrawn.HasShape)
            {
                CopySamples(fencerDrawn, _fencerDrawn);
                for (var pass = 0; pass < ClearingPasses; pass++)
                {
                    FillFlexedShape(chord, droop);
                    var clearing = _bind.FindClearingTipOffset(_shape, _fencerDrawn);
                    if (clearing == Vector3.Zero)
                    {
                        break;
                    }

                    _deflection.Push(clearing);
                    droop = Deflect(forte, shoulder, gripUp, down, blade, out chord);
                }
            }

            Chord = chord;
            ArmTurn = _deflection.GetArmTurn(forte, shoulder);
            _drawn.Step(chord, FloorOnly, gripUp, deltaSeconds, droop);
            for (var piece = 0; piece <= BladeBind.PieceCount; piece++)
            {
                _shape[piece] = _drawn.GetSample(piece * SamplesPerPiece);
            }

            _hasShape = true;
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
            var along = Math.Clamp(fraction, 0.0f, 1.0f) * BladeBind.PieceCount;
            var piece = Math.Min(BladeBind.PieceCount - 1, (int)along);
            return Vector3.Lerp(_shape[piece], _shape[piece + 1], along - piece);
        }

        /// <summary>Adds this blade, as drawn this frame, to the Fencer's obstacles (nothing before the first <see cref="Step"/>).</summary>
        public void AddObstacles(ICollection<BladeObstacle> obstacles)
        {
            if (_hasShape)
            {
                _bind.AddObstacles(_shape, obstacles);
            }
        }

        /// <summary>After the Fencer's drawn blade moved this frame: updates which side of this blade it is on.</summary>
        public void UpdateSide(BladeSegment fencerHand, DrawnBlade fencerDrawn)
        {
            if (!_hasShape)
            {
                return;
            }

            var hasDrawn = fencerDrawn != null && fencerDrawn.HasShape;
            if (hasDrawn)
            {
                CopySamples(fencerDrawn, _fencerDrawn);
            }

            _bind.UpdateSide(fencerHand, hasDrawn ? _fencerDrawn : null, _shape);
        }

        /// <summary>The blade turned by the deflection, and its droop; <paramref name="chord"/> is the guard to the real point.</summary>
        private BladeDroop Deflect(BladeSegment forte, Vector3 shoulder, Vector3 gripUp, Vector3 down, BladeSettings blade, out BladeSegment chord)
        {
            var swung = _deflection.Apply(forte, shoulder);
            var droop = BladeDroop.Create(swung.Direction, gripUp, down, blade);
            chord = droop.GetChord(swung);
            return droop;
        }

        /// <summary>The shape the drawn blade will have: the chord with the flex it has now and the rest curve.</summary>
        private void FillFlexedShape(BladeSegment chord, BladeDroop droop)
        {
            for (var piece = 0; piece <= BladeBind.PieceCount; piece++)
            {
                var fraction = (float)piece / BladeBind.PieceCount;
                _shape[piece] = _drawn.Flex.GetCenterlinePoint(chord, fraction) + droop.GetChordOffset(fraction);
            }
        }

        private static void FillRestShape(BladeSegment chord, BladeDroop droop, Vector3[] shape)
        {
            for (var piece = 0; piece <= BladeBind.PieceCount; piece++)
            {
                var fraction = (float)piece / BladeBind.PieceCount;
                shape[piece] = Vector3.Lerp(chord.GuardPosition, chord.TipPosition, fraction) + droop.GetChordOffset(fraction);
            }
        }

        /// <summary>A turn in the Strip frame as seen in the Dummy frame (half a turn about the vertical from it).</summary>
        private static Quaternion ToDummyFrame(Quaternion turnOnStrip)
        {
            return new Quaternion(-turnOnStrip.X, turnOnStrip.Y, -turnOnStrip.Z, turnOnStrip.W);
        }

        private static void CopySamples(DrawnBlade drawn, Vector3[] samples)
        {
            for (var index = 0; index <= DrawnBlade.SampleCount; index++)
            {
                samples[index] = drawn.GetSample(index);
            }
        }
    }
}
