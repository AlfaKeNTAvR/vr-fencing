using System;
using System.Collections.Generic;
using System.Numerics;
using VrFencing.Dummy;

namespace VrFencing.Blade
{
    /// <summary>
    /// The blade as drawn (visual only; Touch detection uses the straight blade), as <see cref="SampleCount"/> + 1
    /// centerline points from the guard to the point. It never passes through a body (PER-48):
    /// <list type="bullet">
    /// <item>the point pins on the near-side surface it meets and the blade bows as the hand pushes on
    /// (<see cref="BladeContactBend"/>); past the most bow the drawn blade lags the hand
    /// (<see cref="IsDecoupled"/>);</item>
    /// <item>otherwise it flexes on fast moves (<see cref="BladeFlex"/>), and when a pinned point lets go the flex
    /// starts from the bent pose and the blade springs back;</item>
    /// <item>every point along the blade is then kept out of every body, pushed back out on the side it was on the
    /// step before, so the blade's body cannot cut through a thin arm either.</item>
    /// </list>
    /// The bodies are plain capsules, so another blade's capsule can join them later (the Armed Dummy, PER-40).
    /// Positions in any one frame (the Strip frame in the app), meters, time in seconds.
    /// </summary>
    public sealed class DrawnBlade
    {
        /// <summary>Pieces the drawn blade is made of; the centerline has one more point than this.</summary>
        public const int SampleCount = 24;

        /// <summary>The drawn blade's points keep this far out of a body's surface: about the blade's half-width.</summary>
        public const float BladeRadiusMeters = 0.004f;

        // Overlapping bodies (hand and wrist) can push a point from one into the other; a second pass settles it.
        private const int ConstraintPasses = 2;

        private readonly BladeFlex _flex = new BladeFlex();
        private readonly BladeContactBend _contact = new BladeContactBend();
        private readonly Vector3[] _samples = new Vector3[SampleCount + 1];
        private readonly Vector3[] _previousSamples = new Vector3[SampleCount + 1];
        private bool _hasPreviousSamples;

        /// <summary>The fast-move flex; its bend is zero while the point is pinned.</summary>
        public BladeFlex Flex => _flex;

        /// <summary>The pin against a body and the bow.</summary>
        public BladeContactBend Contact => _contact;

        /// <summary>Where the drawn guard is: the hand's guard, unless <see cref="IsDecoupled"/>.</summary>
        public Vector3 GuardPosition => _samples[0];

        /// <summary>The hand pushed further than the blade can bow: the drawn blade stays put and lags the hand.</summary>
        public bool IsDecoupled => _contact.IsDecoupled;

        /// <summary>Straight again at once, nothing pinned, e.g. after the blade or the Strip jumped.</summary>
        public void Reset()
        {
            _flex.Reset();
            _contact.Reset();
            _hasPreviousSamples = false;
        }

        /// <summary>The drawn centerline point <paramref name="index"/>, 0 at the guard to <see cref="SampleCount"/> at the point.</summary>
        public Vector3 GetSample(int index)
        {
            return _samples[index];
        }

        /// <summary>Moves to the blade's new straight pose.</summary>
        /// <param name="bodies">Every capsule the drawn blade cannot pass, in the blade's frame, in the same order every step.</param>
        /// <param name="bowDirection">Which way the blade bows when pushed straight in (the grip's up).</param>
        /// <returns>Why a pinned point let go this step, or <see cref="PinRelease.None"/>.</returns>
        public PinRelease Step(BladeSegment straightBlade, IReadOnlyList<Capsule> bodies, Vector3 bowDirection, float deltaSeconds)
        {
            if (bodies == null)
            {
                throw new ArgumentNullException(nameof(bodies));
            }

            var wasPinned = _contact.IsPinned;
            var bowBefore = _contact.BowMeters;
            var release = _contact.Step(straightBlade, bodies, bowDirection);
            if (_contact.IsPinned)
            {
                // The pin holds the point; restart the flex so it does not carry stale motion into the release.
                _flex.Reset();
            }
            else if (wasPinned)
            {
                // Spring back from where the point was drawn, plus the bow, both limited by the flex.
                var heldOffset = _samples[SampleCount] - straightBlade.TipPosition;
                _flex.SpringBackFrom(straightBlade, heldOffset + (bowDirection * Math.Min(bowBefore, BladeFlex.MaxBendMeters)));
            }
            else
            {
                _flex.Step(straightBlade, deltaSeconds);
            }

            for (var index = 0; index <= SampleCount; index++)
            {
                var fraction = (float)index / SampleCount;
                _samples[index] = _contact.IsPinned
                    ? _contact.GetCenterlinePoint(straightBlade, fraction)
                    : _flex.GetCenterlinePoint(straightBlade, fraction);
            }

            KeepOutOfBodies(bodies);
            Array.Copy(_samples, _previousSamples, _samples.Length);
            _hasPreviousSamples = true;
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
        /// Keeps every drawn point but the guard (the hand) out of every body. A point whose path from its last drawn
        /// position runs into a body is held where it met the surface, even if the hand has carried it right across
        /// in one step; it follows again once that path is clear (the hand came back or went around). A point found
        /// inside is pushed out on the side it was on the step before.
        /// </summary>
        private void KeepOutOfBodies(IReadOnlyList<Capsule> bodies)
        {
            for (var pass = 0; pass < ConstraintPasses; pass++)
            {
                for (var index = 1; index <= SampleCount; index++)
                {
                    foreach (var body in bodies)
                    {
                        var previous = _hasPreviousSamples ? _previousSamples[index] : (Vector3?)null;
                        _samples[index] = previous.HasValue
                            ? HoldOnNearSide(_samples[index], body, previous.Value)
                            : _samples[index];
                        _samples[index] = PushOut(_samples[index], body, previous);
                    }
                }
            }
        }

        /// <summary>Where the path from <paramref name="previous"/> meets the body, resting on its surface; the point itself if the path is clear.</summary>
        private static Vector3 HoldOnNearSide(Vector3 point, Capsule body, Vector3 previous)
        {
            // Held points rest at the full margin, so their next path starts outside this slightly smaller one.
            if (!SegmentMath.TryFindCapsuleEntry(previous, point, body, BladeRadiusMeters * 0.5f, out var fraction))
            {
                return point;
            }

            var entry = Vector3.Lerp(previous, point, fraction);
            var onAxis = SegmentMath.FindClosestPointOnSegment(entry, body.Start, body.End);
            return onAxis + (SegmentMath.FindOutwardNormal(entry, body) * (body.RadiusMeters + BladeRadiusMeters));
        }

        private static Vector3 PushOut(Vector3 point, Capsule body, Vector3? previous)
        {
            var reach = body.RadiusMeters + BladeRadiusMeters;
            var onAxis = SegmentMath.FindClosestPointOnSegment(point, body.Start, body.End);
            if (Vector3.Distance(point, onAxis) >= reach - 1e-5f)
            {
                return point;
            }

            var outward = SegmentMath.FindOutwardNormal(point, body);
            if (previous.HasValue)
            {
                var fromPrevious = previous.Value - onAxis;
                if (fromPrevious.LengthSquared() > 1e-10f)
                {
                    outward = Vector3.Normalize(fromPrevious);
                }
            }

            return onAxis + (outward * reach);
        }
    }
}
