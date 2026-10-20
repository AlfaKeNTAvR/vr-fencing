using System;
using System.Collections.Generic;
using System.Numerics;
using VrFencing.Settings;

namespace VrFencing.Blade
{
    /// <summary>
    /// The Armed Dummy's épée, solid against the Fencer's (PER-104): where the Dummy holds it, swung by clashes
    /// (<see cref="BladeSwing"/>, PER-95), pushed aside by the Fencer's blade and springing back (<see cref="BladeBind"/>),
    /// and drawn like the Fencer's (<see cref="DrawnBlade"/>). Each frame:
    /// <list type="number">
    /// <item><see cref="Step"/>, before the Fencer's drawn blade moves: the press pulls the swing's spring aside, the swing
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

        private readonly BladeSwing _swing = new BladeSwing();
        private readonly BladeBind _bind = new BladeBind();
        private readonly DrawnBlade _drawn = new DrawnBlade();
        private readonly Vector3[] _line = new Vector3[BladeBind.PieceCount + 1];
        private readonly Vector3[] _shape = new Vector3[BladeBind.PieceCount + 1];
        private readonly Vector3[] _fencerDrawn = new Vector3[DrawnBlade.SampleCount + 1];
        private bool _hasShape;

        /// <summary>The swing: kicked by a clash, held aside by a press.</summary>
        public BladeSwing Swing => _swing;

        /// <summary>The side the Fencer's blade keeps to, and the press.</summary>
        public BladeBind Bind => _bind;

        /// <summary>The blade as drawn.</summary>
        public DrawnBlade Drawn => _drawn;

        /// <summary>The guard to the real point this frame (swung, pushed and drooped): what the clash test uses.</summary>
        public BladeSegment Chord { get; private set; }

        /// <summary>The drawn blade as <see cref="BladeBind.PieceCount"/> + 1 points, guard first (after <see cref="Step"/>).</summary>
        public IReadOnlyList<Vector3> Shape => _shape;

        /// <summary>True while the Fencer's hand presses past this blade's line (<see cref="BladeBind.PressMeters"/>).</summary>
        public bool IsPressed => _bind.PressMeters > 0.0f;

        /// <summary>Straight in line again at once, no side known: the blade appeared or jumped.</summary>
        public void Reset()
        {
            _swing.Reset();
            _bind.Reset();
            _drawn.Reset();
            _hasShape = false;
        }

        /// <summary>Moves the blade for this frame, before the Fencer's drawn blade moves.</summary>
        /// <param name="forte">The guard to the undrooped point, where the Dummy's hand holds it.</param>
        /// <param name="gripUp">The Dummy's grip's up.</param>
        /// <param name="down">Gravity's direction.</param>
        /// <param name="fencerHand">The Fencer's blade from the hand (its chord to the real point).</param>
        /// <param name="fencerDrawn">The Fencer's drawn blade as drawn last frame; null or not yet drawn when there is none.</param>
        public void Step(BladeSegment forte, Vector3 gripUp, Vector3 down, BladeSettings blade, BladeSegment fencerHand, DrawnBlade fencerDrawn, float deltaSeconds)
        {
            if (blade == null)
            {
                throw new ArgumentNullException(nameof(blade));
            }

            var lineDroop = BladeDroop.Create(forte.Direction, gripUp, down, blade);
            FillRestShape(lineDroop.GetChord(forte), lineDroop, _line);
            _swing.HoldAt(_bind.FindYieldTipOffset(fencerHand, _line));
            _swing.Step(deltaSeconds);

            var droop = TurnBySwing(forte, gripUp, down, blade, out var chord);
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

                    _swing.Push(clearing);
                    droop = TurnBySwing(forte, gripUp, down, blade, out chord);
                }
            }

            Chord = chord;
            _drawn.Step(chord, FloorOnly, gripUp, deltaSeconds, droop);
            for (var piece = 0; piece <= BladeBind.PieceCount; piece++)
            {
                _shape[piece] = _drawn.GetSample(piece * SamplesPerPiece);
            }

            _hasShape = true;
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

        /// <summary>The blade turned by the swing, and its droop; <paramref name="chord"/> is the guard to the real point.</summary>
        private BladeDroop TurnBySwing(BladeSegment forte, Vector3 gripUp, Vector3 down, BladeSettings blade, out BladeSegment chord)
        {
            var swung = _swing.Apply(forte);
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

        private static void CopySamples(DrawnBlade drawn, Vector3[] samples)
        {
            for (var index = 0; index <= DrawnBlade.SampleCount; index++)
            {
                samples[index] = drawn.GetSample(index);
            }
        }
    }
}
