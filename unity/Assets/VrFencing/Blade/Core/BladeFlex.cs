using System;
using System.Numerics;

namespace VrFencing.Blade
{
    /// <summary>
    /// Visual-only blade flex (Nikita's call, PER-22): the drawn point lags behind the straight blade like a springy
    /// blade's tip, bending a few centimeters on fast moves and springing back, and bows on a Touch. Touch detection
    /// always uses the straight blade; this only moves what is drawn.
    ///
    /// The bend is the drawn point's offset from the straight point, kept perpendicular to the blade and at most
    /// <see cref="MaxBendMeters"/> (the 4.5-7 cm tip bend scale from the rules research). It follows a damped spring
    /// driven by the straight point's motion. Positions are in any one frame (world in the app), time in seconds.
    /// </summary>
    public sealed class BladeFlex
    {
        /// <summary>The upper end of the 4.5-7 cm tip bend scale.</summary>
        public const float MaxBendMeters = 0.07f;

        /// <summary>How far the point bows on a Touch, inside the 4.5-7 cm scale.</summary>
        public const float TouchBendMeters = 0.05f;

        // A 7 Hz spring, lightly damped: a sharp stop overshoots once or twice and settles in about a quarter second.
        private const float NaturalFrequencyHertz = 7.0f;
        private const float DampingRatio = 0.3f;

        // Integrate in short steps so the spring stays stable even on a long frame.
        private const float MaxSubstepSeconds = 0.004f;
        private const float MaxFrameSeconds = 0.1f;

        private Vector3 _bend;
        private Vector3 _bendVelocity;
        private Vector3? _previousStraightTip;
        private Vector3 _previousStraightTipVelocity;

        /// <summary>The drawn point minus the straight point.</summary>
        public Vector3 Bend => _bend;

        /// <summary>Straightens the blade at once, e.g. after the blade jumped.</summary>
        public void Reset()
        {
            _bend = Vector3.Zero;
            _bendVelocity = Vector3.Zero;
            _previousStraightTip = null;
            _previousStraightTipVelocity = Vector3.Zero;
        }

        /// <summary>
        /// Advances the spring by <paramref name="deltaSeconds"/> to the blade's new straight pose. The point's
        /// acceleration pushes the bend the other way (inertia); the spring pulls it back.
        /// </summary>
        public void Step(BladeSegment straightBlade, float deltaSeconds)
        {
            if (!(deltaSeconds > 0.0f))
            {
                return;
            }

            var frameSeconds = Math.Min(deltaSeconds, MaxFrameSeconds);
            var tip = straightBlade.TipPosition;
            if (!_previousStraightTip.HasValue)
            {
                _previousStraightTip = tip;
                _previousStraightTipVelocity = Vector3.Zero;
                return;
            }

            var tipVelocity = (tip - _previousStraightTip.Value) / frameSeconds;
            var tipAcceleration = (tipVelocity - _previousStraightTipVelocity) / frameSeconds;
            _previousStraightTip = tip;
            _previousStraightTipVelocity = tipVelocity;

            var angularFrequency = 2.0f * (float)Math.PI * NaturalFrequencyHertz;
            var substeps = (int)Math.Ceiling(frameSeconds / MaxSubstepSeconds);
            var substepSeconds = frameSeconds / substeps;
            for (var step = 0; step < substeps; step++)
            {
                var springAcceleration = (-angularFrequency * angularFrequency * _bend) - (2.0f * DampingRatio * angularFrequency * _bendVelocity);
                _bendVelocity += (springAcceleration - tipAcceleration) * substepSeconds;
                _bend += _bendVelocity * substepSeconds;
            }

            KeepPerpendicularAndLimited(straightBlade.Direction);
        }

        /// <summary>Bows the point by <see cref="TouchBendMeters"/> toward <paramref name="direction"/> (made perpendicular to the blade), then it springs back.</summary>
        public void Kick(BladeSegment straightBlade, Vector3 direction)
        {
            var bladeDirection = straightBlade.Direction;
            var sideways = direction - (Vector3.Dot(direction, bladeDirection) * bladeDirection);
            if (sideways.LengthSquared() < 1e-10f)
            {
                return;
            }

            // An undamped spring started at speed v swings out to v / w; damping takes the first swing down by
            // exp(-z / sqrt(1 - z^2) * atan(sqrt(1 - z^2) / z)). Start fast enough that the first swing is the bow.
            var angularFrequency = 2.0f * (float)Math.PI * NaturalFrequencyHertz;
            var dampedFraction = Math.Sqrt(1.0 - (DampingRatio * DampingRatio));
            var firstSwingFraction = (float)Math.Exp(-DampingRatio / dampedFraction * Math.Atan(dampedFraction / DampingRatio));
            _bendVelocity += Vector3.Normalize(sideways) * (TouchBendMeters * angularFrequency / firstSwingFraction);
        }

        /// <summary>
        /// Where the drawn blade's centerline is at <paramref name="fraction"/> of the way from the guard (0) to the point
        /// (1): the straight blade plus the bend shaped like a cantilever under an end load, 0 at the guard and the full
        /// bend at the point.
        /// </summary>
        public Vector3 GetCenterlinePoint(BladeSegment straightBlade, float fraction)
        {
            var clamped = Math.Max(0.0f, Math.Min(1.0f, fraction));
            var shape = clamped * clamped * (3.0f - clamped) * 0.5f;
            return Vector3.Lerp(straightBlade.GuardPosition, straightBlade.TipPosition, clamped) + (_bend * shape);
        }

        private void KeepPerpendicularAndLimited(Vector3 bladeDirection)
        {
            _bend -= Vector3.Dot(_bend, bladeDirection) * bladeDirection;
            _bendVelocity -= Vector3.Dot(_bendVelocity, bladeDirection) * bladeDirection;
            var length = _bend.Length();
            if (length > MaxBendMeters)
            {
                _bend *= MaxBendMeters / length;
                var outward = _bend / MaxBendMeters;
                var outwardSpeed = Vector3.Dot(_bendVelocity, outward);
                if (outwardSpeed > 0.0f)
                {
                    _bendVelocity -= outwardSpeed * outward;
                }
            }
        }
    }
}
