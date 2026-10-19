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
    /// <item>a shape that would pass into or through a body (the flat swept into the Dummy's arm) is not taken: the drawn
    /// blade stops at the closest allowed pose on the way back to where it was drawn, or holds there, and lags the hand
    /// (<see cref="IsDecoupled"/>) until the hand's own pose is reachable again: back, or around;</item>
    /// <item>when a pinned point lets go, or the drawn blade catches up with the hand, it springs back ringing a few
    /// decaying cycles, like a real épée.</item>
    /// </list>
    /// The bodies are <see cref="BladeObstacle"/>s: the Dummy's capsules and the floor, and another blade's capsule can
    /// join them later (the Armed Dummy, PER-40). Positions in any one frame (the Strip frame in the app), meters, time
    /// in seconds.
    /// </summary>
    public sealed class DrawnBlade
    {
        /// <summary>Pieces the drawn blade is made of; the centerline has one more point than this.</summary>
        public const int SampleCount = 24;

        /// <summary>The drawn blade's points keep this far out of a body's surface: about the blade's half-width.</summary>
        public const float BladeRadiusMeters = 0.004f;

        private readonly BladeFlex _flex = new BladeFlex();
        private readonly BladeContactBend _contact = new BladeContactBend();
        private readonly Vector3[] _samples = new Vector3[SampleCount + 1];
        private readonly Vector3[] _candidate = new Vector3[SampleCount + 1];
        private readonly DrawnShape.PoseTest _tryFreeShape;
        private bool _hasSamples;
        private bool _isFreeOffHand;
        private BladeSegment _drawnPose = new BladeSegment(Vector3.Zero, Vector3.UnitZ * BladePose.BladeLengthMeters);
        private IReadOnlyList<BladeObstacle> _bodies;
        private BladeDroop _droop;

        public DrawnBlade()
        {
            _tryFreeShape = TryFreeShape;
        }

        /// <summary>The fast-move flex; its bend is zero while the point is pinned.</summary>
        public BladeFlex Flex => _flex;

        /// <summary>The pin against a body and the bend.</summary>
        public BladeContactBend Contact => _contact;

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
        /// the hand went through. A ghost hilt shows the hand.
        /// </summary>
        public bool IsDecoupled => _contact.IsPinned ? _contact.IsDecoupled : _isFreeOffHand;

        /// <summary>Straight again at once, nothing pinned, e.g. after the blade or the Strip jumped.</summary>
        public void Reset()
        {
            _flex.Reset();
            _contact.Reset();
            _hasSamples = false;
            _isFreeOffHand = false;
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

        /// <summary>Where the drawn centerline is at <paramref name="fraction"/> of the way from the guard (0) to the point (1).</summary>
        public Vector3 GetCenterlinePoint(float fraction)
        {
            var scaled = Math.Max(0.0f, Math.Min(1.0f, fraction)) * SampleCount;
            var index = Math.Min((int)scaled, SampleCount - 1);
            return Vector3.Lerp(_samples[index], _samples[index + 1], scaled - index);
        }

        /// <summary>
        /// The free blade (nothing pinned): the hand's flexed blade if it is clear of every body and got there without
        /// crossing one; else the allowed pose closest to the hand on the way back to the last drawn pose; else it holds.
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
            _bodies = null;
            if (reached.HasValue)
            {
                if (_isFreeOffHand && reached.Value == 0.0f)
                {
                    // Caught up with the hand: the blade springs from where it was drawn instead of jumping, if that is clear.
                    _flex.SpringBackFrom(straightBlade, _samples[SampleCount] - straightBlade.TipPosition);
                    FillFlexed(straightBlade, _candidate);
                    if (!DrawnShape.IsClear(_candidate, _samples, bodies, BladeRadiusMeters, 1, sweepsTip: true))
                    {
                        _flex.Reset();
                        FillFlexed(straightBlade, _candidate);
                    }
                }

                Array.Copy(_candidate, _samples, _samples.Length);
                _drawnPose = reached.Value > 0.0f ? DrawnShape.Interpolate(straightBlade, _drawnPose, reached.Value) : straightBlade;
            }

            _isFreeOffHand = DrawnShape.IsOffHand(_drawnPose.GuardPosition, _drawnPose.Direction, straightBlade);
        }

        private bool TryFreeShape(BladeSegment pose)
        {
            FillFlexed(pose, _candidate);
            return DrawnShape.IsClear(_candidate, _samples, _bodies, BladeRadiusMeters, 1, sweepsTip: true);
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
