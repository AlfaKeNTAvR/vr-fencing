using System;
using System.Numerics;

namespace VrFencing.Blade
{
    /// <summary>
    /// Visual-only blade flex (Nikita's call, PER-22): the drawn point lags behind the straight blade like a springy
    /// blade's tip, bending a few centimeters on fast moves and springing back, including after the point slides off a
    /// body (<see cref="BladeContactBend"/>). Touch detection always uses the straight blade; this only moves what is drawn.
    ///
    /// The bend is the drawn point's offset from the straight point, kept perpendicular to the blade and at most
    /// <see cref="MaxBendMeters"/> (the 4.5-7 cm tip bend scale from the rules research). It follows a damped spring
    /// driven by the straight point's motion. Positions are in any one frame (the Strip frame in the app), time in seconds.
    ///
    /// A held blade must look still (PER-48): tracking jitters the point by up to about 7 mm, and a lightly damped spring
    /// fed raw frame-to-frame acceleration shook visibly. So the point's motion is smoothed first, speeds below
    /// <see cref="DeadbandMetersPerSecond"/> are ignored, and the spring is well damped: only real fast moves bend it.
    /// </summary>
    public sealed class BladeFlex
    {
        /// <summary>The upper end of the 4.5-7 cm tip bend scale.</summary>
        public const float MaxBendMeters = 0.07f;

        /// <summary>Point speeds up to this are hand jitter or slow aiming, not a move that bends the blade.</summary>
        public const float DeadbandMetersPerSecond = 0.35f;

        // A 7 Hz spring, well damped: a sharp stop overshoots once, a little, and settles in about a fifth of a second.
        private const float NaturalFrequencyHertz = 7.0f;
        private const float DampingRatio = 0.6f;

        // Let go of a held bend (the point slid off or came out of a body, the drawn blade caught up with the hand), a
        // real épée rings: a few quick decaying cycles. So the spring rings lightly damped until it is nearly still,
        // then is well damped again (motion alone never rings it, so a held blade stays calm).
        private const float RingingDampingRatio = 0.12f;
        private const float RingingEndsMeters = 0.002f;

        // The point's position is low-passed with this time constant before its speed is taken: frame-to-frame
        // tracking noise differentiated twice is far larger than any real acceleration.
        private const float SmoothingSeconds = 0.03f;

        // Integrate in short steps so the spring stays stable even on a long frame.
        private const float MaxSubstepSeconds = 0.004f;
        private const float MaxFrameSeconds = 0.1f;

        private Vector3 _bend;
        private Vector3 _bendVelocity;
        private Vector3? _smoothedTip;
        private Vector3? _previousDrivingVelocity;
        private bool _isRinging;

        /// <summary>The drawn point minus the straight point.</summary>
        public Vector3 Bend => _bend;

        /// <summary>True while a let-go bend rings out (lightly damped), after <see cref="SpringBackFrom"/>.</summary>
        public bool IsRinging => _isRinging;

        /// <summary>Straightens the blade at once, e.g. after the blade jumped.</summary>
        public void Reset()
        {
            _bend = Vector3.Zero;
            _bendVelocity = Vector3.Zero;
            _smoothedTip = null;
            _previousDrivingVelocity = null;
            _isRinging = false;
        }

        /// <summary>
        /// Starts the spring from <paramref name="bend"/> at rest (made perpendicular to the blade and limited), with the
        /// blade's motion history restarted at <paramref name="straightBlade"/>: the point was held elsewhere (pinned on
        /// a body, or held off the hand) and now springs back, ringing a few decaying cycles.
        /// </summary>
        public void SpringBackFrom(BladeSegment straightBlade, Vector3 bend)
        {
            Reset();
            _bend = bend;
            _isRinging = true;
            KeepPerpendicularAndLimited(straightBlade.Direction);
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
            var tipAcceleration = FindDrivingAcceleration(straightBlade.TipPosition, frameSeconds);
            var angularFrequency = 2.0f * (float)Math.PI * NaturalFrequencyHertz;
            var dampingRatio = _isRinging ? RingingDampingRatio : DampingRatio;
            var substeps = (int)Math.Ceiling(frameSeconds / MaxSubstepSeconds);
            var substepSeconds = frameSeconds / substeps;
            for (var step = 0; step < substeps; step++)
            {
                var springAcceleration = (-angularFrequency * angularFrequency * _bend) - (2.0f * dampingRatio * angularFrequency * _bendVelocity);
                _bendVelocity += (springAcceleration - tipAcceleration) * substepSeconds;
                _bend += _bendVelocity * substepSeconds;
            }

            KeepPerpendicularAndLimited(straightBlade.Direction);
            if (_isRinging && _bend.Length() + (_bendVelocity.Length() / angularFrequency) < RingingEndsMeters)
            {
                _isRinging = false;
            }
        }

        /// <summary>The smoothed, deadbanded acceleration of the point; zero until there are two frames of motion.</summary>
        private Vector3 FindDrivingAcceleration(Vector3 tip, float frameSeconds)
        {
            if (!_smoothedTip.HasValue)
            {
                _smoothedTip = tip;
                return Vector3.Zero;
            }

            var previousSmoothedTip = _smoothedTip.Value;
            var smoothedTip = Vector3.Lerp(previousSmoothedTip, tip, frameSeconds / (SmoothingSeconds + frameSeconds));
            _smoothedTip = smoothedTip;
            var drivingVelocity = ApplyDeadband((smoothedTip - previousSmoothedTip) / frameSeconds);
            var previousDrivingVelocity = _previousDrivingVelocity;
            _previousDrivingVelocity = drivingVelocity;
            return previousDrivingVelocity.HasValue ? (drivingVelocity - previousDrivingVelocity.Value) / frameSeconds : Vector3.Zero;
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

        /// <summary>Takes <see cref="DeadbandMetersPerSecond"/> off the speed, keeping the direction: slow motion drives nothing, and there is no jump at the edge.</summary>
        private static Vector3 ApplyDeadband(Vector3 velocity)
        {
            var speed = velocity.Length();
            return speed <= DeadbandMetersPerSecond ? Vector3.Zero : velocity * ((speed - DeadbandMetersPerSecond) / speed);
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
