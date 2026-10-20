using System;
using System.Collections.Generic;
using System.Numerics;

namespace VrFencing.Blade
{
    /// <summary>
    /// The Fencer's blade and the Armed Dummy's blade are solid against each other (PER-104): neither passes through the
    /// other, also on fast parries and when pulling back.
    /// <list type="bullet">
    /// <item><b>Which side.</b> It remembers which side of the Dummy's blade the Fencer's is on (<see cref="Side"/>): the
    /// way from the Dummy's blade to the Fencer's hand blade where they come closest, while they are apart. Once they
    /// are within <see cref="EngagedMeters"/> of touching, or the Fencer's blade is anywhere behind the Dummy's (in the
    /// strip behind it, <see cref="BladeCrossing.TryFindLeastSide"/>), the side is held: a hand that moved across between
    /// two frames, at any speed, is pressing, not through. When they are apart again (pulled back off the end, or gone
    /// round its point) the side follows the hand again.</item>
    /// <item><b>The Fencer's drawn blade</b> takes the Dummy's blade as <see cref="PieceCount"/> thin obstacles kept to
    /// that side (<see cref="AddObstacles"/>): it bends round it as round any body (<see cref="BladeSideContact"/>), its
    /// point slides on it with <see cref="BladeObstacle.BladeFriction"/>, and the drawn hilt lags (the ghost hilt) when
    /// the hand presses further than the blade bends.</item>
    /// <item><b>The Dummy's blade</b> gives way: pressed, its point is pulled aside, square to it, by a share of how far
    /// the hand's blade presses past its line that depends on where each blade is pressed (<see cref="GetPressYieldShare"/>,
    /// PER-116), on the deflection's spring (<see cref="BladeDeflection.HoldAt"/>), so it gives back when let go; and it is
    /// pushed out of the way at once when it
    /// would move into the Fencer's drawn blade (<see cref="FindClearingTipOffset"/>), so a thrust into a parry is
    /// deflected.</item>
    /// </list>
    /// Blade shapes are <see cref="PieceCount"/> + 1 points along the centerline, guard first. Positions in the Strip
    /// frame, meters.
    /// </summary>
    public sealed class BladeBind
    {
        /// <summary>The Dummy's drawn blade is taken as this many straight pieces: its curve is off them by about a millimeter.</summary>
        public const int PieceCount = 4;

        /// <summary>Centerline to centerline at which the two blades touch: both half-widths.</summary>
        public const float ContactMeters = DrawnBlade.BladeRadiusMeters + BladeObstacle.BladeRadiusMeters;

        /// <summary>
        /// The side is held while the blades are this close to touching (or one is behind the other): near enough that a
        /// fast move could carry the Fencer's blade across between frames.
        /// </summary>
        public const float EngagedMeters = 0.02f;

        /// <summary>
        /// How far to either side of the Dummy's blade the strip behind it reaches: the Fencer's blade found in it past the
        /// Dummy's axis got there through it. Wider than touching, so a fast move that also slips a little sideways is
        /// still caught.
        /// </summary>
        public const float StripHalfWidthMeters = ContactMeters + EngagedMeters;

        /// <summary>
        /// The Dummy's point gives way by this share of the press (how far the hand's blade is past the Dummy's line) for
        /// a press on the Dummy's guard: its forte holds (PER-116). The Fencer's blade bends the rest.
        /// </summary>
        public const float PressYieldAtDummyGuard = 0.15f;

        /// <summary>The share for a press at the Dummy's point: its foible gives way to the Fencer's forte (prise de fer).</summary>
        public const float PressYieldAtDummyPoint = 0.8f;

        /// <summary>
        /// The share is scaled by this for a press with the Fencer's point (foible), from the whole with the Fencer's
        /// guard (forte): a foible on a forte gets nowhere.
        /// </summary>
        public const float FencerFoiblePressFactor = 0.5f;

        // Below this sine of the angle between the blades their line-to-line normal is no guide to the side.
        private const float ParallelSine = 0.1f;

        // The Dummy's blade is pushed out of the way this much further than touching, so turning it about its guard
        // (not sliding it) still clears.
        private const float ClearingSlackMeters = 0.001f;

        // A contact closer to the Dummy's guard than this share of its blade is pushed as if here: near the guard the
        // blade cannot turn out of the way (the arm's limit stops it).
        private const float MinLeverFraction = 0.05f;

        private Vector3? _side;

        /// <summary>Which way from the Dummy's blade the Fencer's is, square to it; none until both have been seen.</summary>
        public Vector3? Side => _side;

        /// <summary>How far the hand's blade presses past the Dummy's line (0 when it does not), from the last <see cref="FindYieldTipOffset"/>.</summary>
        public float PressMeters { get; private set; }

        /// <summary>
        /// True while the blades touch, nearly touch (<see cref="EngagedMeters"/>) or one is behind the other, as of the
        /// last <see cref="UpdateSide"/>: the side is held.
        /// </summary>
        public bool IsEngaged { get; private set; }

        /// <summary>Forget the side, e.g. after a jump or when the Dummy's blade appears.</summary>
        public void Reset()
        {
            _side = null;
            PressMeters = 0.0f;
            IsEngaged = false;
        }

        /// <summary>
        /// How far a piece of the Fencer's blade overlaps a piece of the Dummy's, kept to <paramref name="side"/>: by how
        /// much it is closer than touching, or, behind the Dummy's blade (past its axis, in the strip behind it), by how
        /// far it would have to go back to touch from its side. 0 or less when clear.
        /// </summary>
        /// <param name="away">Which way the Dummy's piece would have to move to clear it.</param>
        /// <param name="onDummy">Where on the Dummy's piece.</param>
        public static float FindOverlap(
            Vector3 start,
            Vector3 end,
            int joints,
            Vector3 dummyStart,
            Vector3 dummyEnd,
            int dummyJoints,
            Vector3 side,
            out Vector3 away,
            out Vector3 onDummy)
        {
            away = -side;
            onDummy = dummyStart;
            var axis = dummyEnd - dummyStart;
            var length = axis.Length();
            if (length > 1e-6f)
            {
                // Wholly before or past the Dummy's piece along it, by more than engaging: far apart, skip the rest.
                var startAlong = Vector3.Dot(start - dummyStart, axis) / length;
                var endAlong = Vector3.Dot(end - dummyStart, axis) / length;
                var reach = ContactMeters + EngagedMeters + (length * BladeCrossing.StripJointSlack);
                if (Math.Max(startAlong, endAlong) < -reach || Math.Min(startAlong, endAlong) > length + reach)
                {
                    return -1.0f;
                }
            }

            BladeCrossing.FindClosest(start, end, joints, dummyStart, dummyEnd, dummyJoints, out var onFencer, out onDummy, out _, out _);
            var apart = onFencer - onDummy;
            var distance = apart.Length();
            var overlap = ContactMeters - distance;
            away = distance > 1e-6f ? -apart / distance : -side;
            var square = MakeSquare(side, dummyEnd - dummyStart);
            if (square != Vector3.Zero
                && BladeCrossing.TryFindLeastSide(start, end, dummyStart, dummyEnd, dummyJoints, square, StripHalfWidthMeters, out var leastSide, out var behind, out _)
                && leastSide < 0.0f)
            {
                overlap = ContactMeters - leastSide;
                away = -square;
                onDummy = SegmentMath.FindClosestPointOnSegment(behind, dummyStart, dummyEnd);
            }

            return overlap;
        }

        /// <summary>
        /// The share of the press the Dummy's point gives way by, for a press at <paramref name="dummyBladeFraction"/>
        /// along its blade (0 guard, 1 point) with the Fencer's blade at <paramref name="fencerBladeFraction"/>: from
        /// <see cref="PressYieldAtDummyGuard"/> to <see cref="PressYieldAtDummyPoint"/>, times 1 for the Fencer's forte down
        /// to <see cref="FencerFoiblePressFactor"/> for its point.
        /// </summary>
        public static float GetPressYieldShare(float dummyBladeFraction, float fencerBladeFraction)
        {
            var dummy = Math.Clamp(dummyBladeFraction, 0.0f, 1.0f);
            var fencer = Math.Clamp(fencerBladeFraction, 0.0f, 1.0f);
            var share = PressYieldAtDummyGuard + ((PressYieldAtDummyPoint - PressYieldAtDummyGuard) * dummy);
            return share * (1.0f + ((FencerFoiblePressFactor - 1.0f) * fencer));
        }

        /// <summary>
        /// The press: how far the hand's blade (the Fencer's chord) has gone into or past the Dummy's blade on its line
        /// (<paramref name="dummyLine"/>: where the Dummy holds it, before any deflection). Returns the tip offset the
        /// Dummy's blade yields to (<see cref="GetPressYieldShare"/> of the deepest press, for where it presses on each
        /// blade, square to it, away from the Fencer's side); zero when nothing presses.
        /// </summary>
        public Vector3 FindYieldTipOffset(BladeSegment hand, IReadOnlyList<Vector3> dummyLine)
        {
            CheckShape(dummyLine);
            PressMeters = 0.0f;
            if (!_side.HasValue)
            {
                return Vector3.Zero;
            }

            var pressedAt = dummyLine[0];
            for (var piece = 0; piece < PieceCount; piece++)
            {
                var overlap = FindOverlap(hand.GuardPosition, hand.TipPosition, 0, dummyLine[piece], dummyLine[piece + 1], FindJoints(piece), _side.Value, out _, out var onDummy);
                if (overlap > PressMeters)
                {
                    PressMeters = overlap;
                    pressedAt = onDummy;
                }
            }

            if (PressMeters <= 0.0f)
            {
                return Vector3.Zero;
            }

            var chord = dummyLine[PieceCount] - dummyLine[0];
            var dummyFraction = FindFraction(pressedAt, dummyLine[0], dummyLine[PieceCount]);
            var fencerFraction = FindFraction(SegmentMath.FindClosestPointOnSegment(pressedAt, hand.GuardPosition, hand.TipPosition), hand.GuardPosition, hand.TipPosition);
            return -MakeSquare(_side.Value, chord) * (GetPressYieldShare(dummyFraction, fencerFraction) * PressMeters);
        }

        /// <summary>
        /// The tip offset, square to the Dummy's blade, that turns it about its guard out of the Fencer's drawn blade:
        /// back to its own side if it got past it, or just apart where they touch. Zero when <paramref name="dummyShape"/>
        /// is clear of it.
        /// </summary>
        /// <param name="fencerDrawn">The Fencer's drawn blade as drawn last (<see cref="DrawnBlade.SampleCount"/> + 1 points).</param>
        public Vector3 FindClearingTipOffset(IReadOnlyList<Vector3> dummyShape, IReadOnlyList<Vector3> fencerDrawn)
        {
            CheckShape(dummyShape);
            if (fencerDrawn == null)
            {
                throw new ArgumentNullException(nameof(fencerDrawn));
            }

            var guard = dummyShape[0];
            var chord = dummyShape[PieceCount] - guard;
            var chordLength = chord.Length();
            if (chordLength < 1e-6f)
            {
                return Vector3.Zero;
            }

            var side = _side ?? Vector3.Zero;
            var largest = Vector3.Zero;
            var lastFencerPiece = fencerDrawn.Count - 2;
            for (var piece = 0; piece < PieceCount; piece++)
            {
                for (var fencerPiece = 0; fencerPiece <= lastFencerPiece; fencerPiece++)
                {
                    var need = FindOverlap(
                        fencerDrawn[fencerPiece],
                        fencerDrawn[fencerPiece + 1],
                        FindFencerJoints(fencerPiece, lastFencerPiece),
                        dummyShape[piece],
                        dummyShape[piece + 1],
                        FindJoints(piece),
                        side,
                        out var away,
                        out var onDummy);
                    if (need <= 0.0f)
                    {
                        continue;
                    }

                    // Turning about the guard moves the contact by its share of the tip's move.
                    var lever = Math.Max(MinLeverFraction, Vector3.Dot(onDummy - guard, chord) / (chordLength * chordLength));
                    var tipMove = MakeSquare(away, chord) * ((need + ClearingSlackMeters) / lever);
                    if (tipMove.LengthSquared() > largest.LengthSquared())
                    {
                        largest = tipMove;
                    }
                }
            }

            return largest;
        }

        /// <summary>
        /// Adds the Dummy's blade (<paramref name="dummyShape"/>, as drawn this frame) to <paramref name="obstacles"/> as
        /// <see cref="PieceCount"/> blade pieces kept to <see cref="Side"/>.
        /// </summary>
        public void AddObstacles(IReadOnlyList<Vector3> dummyShape, ICollection<BladeObstacle> obstacles)
        {
            CheckShape(dummyShape);
            if (obstacles == null)
            {
                throw new ArgumentNullException(nameof(obstacles));
            }

            var side = _side ?? Vector3.Zero;
            for (var piece = 0; piece < PieceCount; piece++)
            {
                obstacles.Add(BladeObstacle.FromBladePiece(dummyShape[piece], dummyShape[piece + 1], side, piece > 0, piece < PieceCount - 1));
            }
        }

        /// <summary>
        /// After both blades moved this frame: the side is held while the blades are engaged (<see cref="IsEngaged"/>),
        /// turned with them as they cross at new angles, and otherwise follows the hand's blade.
        /// </summary>
        /// <param name="fencerDrawn">The Fencer's drawn blade as drawn this frame, if any.</param>
        public void UpdateSide(BladeSegment hand, IReadOnlyList<Vector3> fencerDrawn, IReadOnlyList<Vector3> dummyShape)
        {
            CheckShape(dummyShape);
            var dummyDirection = dummyShape[PieceCount] - dummyShape[0];
            if (dummyDirection.LengthSquared() < 1e-12f)
            {
                return;
            }

            dummyDirection = Vector3.Normalize(dummyDirection);
            IsEngaged = _side.HasValue && FindEngaged(hand, fencerDrawn, dummyShape, _side.Value);
            if (IsEngaged)
            {
                var kept = _side.Value;
                var isAcross = BladeCrossing.FindClosest(hand.GuardPosition, hand.TipPosition, 0, dummyShape[0], dummyShape[PieceCount], 0, out _, out _, out _, out _);
                var normal = Vector3.Cross(hand.Direction, dummyDirection);
                if (isAcross && normal.Length() >= ParallelSine && Math.Abs(Vector3.Dot(Vector3.Normalize(normal), kept)) > 0.3f)
                {
                    // Crossing over each other: the side is the line-to-line normal, kept pointing the same way.
                    var turned = Vector3.Normalize(normal);
                    kept = Vector3.Dot(turned, kept) >= 0.0f ? turned : -turned;
                }

                var square = MakeSquare(kept, dummyDirection);
                _side = square != Vector3.Zero ? square : _side;
                return;
            }

            // Apart: the side is the way from the Dummy's blade to the hand's where they come closest.
            var closest = float.MaxValue;
            for (var piece = 0; piece < PieceCount; piece++)
            {
                BladeCrossing.FindClosest(hand.GuardPosition, hand.TipPosition, 0, dummyShape[piece], dummyShape[piece + 1], FindJoints(piece), out var onHand, out var onDummy, out _, out _);
                var distance = Vector3.Distance(onHand, onDummy);
                var apart = MakeSquare(onHand - onDummy, dummyShape[piece + 1] - dummyShape[piece]);
                if (distance < closest && apart != Vector3.Zero)
                {
                    closest = distance;
                    _side = apart;
                }
            }
        }

        /// <summary>True while the hand's blade or the drawn blade is within <see cref="EngagedMeters"/> of touching the Dummy's, or behind it.</summary>
        private static bool FindEngaged(BladeSegment hand, IReadOnlyList<Vector3> fencerDrawn, IReadOnlyList<Vector3> dummyShape, Vector3 side)
        {
            for (var piece = 0; piece < PieceCount; piece++)
            {
                var start = dummyShape[piece];
                var end = dummyShape[piece + 1];
                var joints = FindJoints(piece);
                if (FindOverlap(hand.GuardPosition, hand.TipPosition, 0, start, end, joints, side, out _, out _) > -EngagedMeters)
                {
                    return true;
                }

                if (fencerDrawn == null)
                {
                    continue;
                }

                var lastFencerPiece = fencerDrawn.Count - 2;
                for (var fencerPiece = 0; fencerPiece <= lastFencerPiece; fencerPiece++)
                {
                    var fencerJoints = FindFencerJoints(fencerPiece, lastFencerPiece);
                    if (FindOverlap(fencerDrawn[fencerPiece], fencerDrawn[fencerPiece + 1], fencerJoints, start, end, joints, side, out _, out _) > -EngagedMeters)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>How far along from <paramref name="start"/> to <paramref name="end"/> <paramref name="point"/> lies, 0 to 1.</summary>
        private static float FindFraction(Vector3 point, Vector3 start, Vector3 end)
        {
            var along = end - start;
            var lengthSquared = along.LengthSquared();
            return lengthSquared < 1e-12f ? 0.0f : Math.Clamp(Vector3.Dot(point - start, along) / lengthSquared, 0.0f, 1.0f);
        }

        private static int FindJoints(int piece)
        {
            return (piece > 0 ? 1 : 0) | (piece < PieceCount - 1 ? 2 : 0);
        }

        private static int FindFencerJoints(int piece, int lastPiece)
        {
            return (piece > 0 ? 1 : 0) | (piece < lastPiece ? 2 : 0);
        }

        /// <summary><paramref name="vector"/> made square to <paramref name="axis"/> and unit length; zero if it lies along it.</summary>
        private static Vector3 MakeSquare(Vector3 vector, Vector3 axis)
        {
            var axisLengthSquared = axis.LengthSquared();
            var square = axisLengthSquared > 1e-12f ? vector - (Vector3.Dot(vector, axis) / axisLengthSquared * axis) : vector;
            return square.LengthSquared() > 1e-12f ? Vector3.Normalize(square) : Vector3.Zero;
        }

        private static void CheckShape(IReadOnlyList<Vector3> shape)
        {
            if (shape == null)
            {
                throw new ArgumentNullException(nameof(shape));
            }

            if (shape.Count != PieceCount + 1)
            {
                throw new ArgumentException($"A blade shape here has {PieceCount + 1} points, guard first.", nameof(shape));
            }
        }
    }
}
