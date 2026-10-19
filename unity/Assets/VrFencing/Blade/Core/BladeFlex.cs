using System;
using System.Numerics;
using VrFencing.Settings;

namespace VrFencing.Blade
{
    /// <summary>
    /// Visual-only blade flex (PER-22, livelier in PER-50): the drawn point whips like a real épée's on circles, beats
    /// and sudden stops, bending a few centimeters and ringing out. Touch detection always uses the straight blade; this
    /// only moves what is drawn.
    ///
    /// The bend is the drawn point's offset from the straight point, kept perpendicular to the blade and at most
    /// <see cref="MaxBendMeters"/> (the 4.5-7 cm tip bend scale from the rules research). It is the blade's first bending
    /// mode: a lightly damped spring driven by the point's acceleration (inertia), so the tip lags and overshoots on a
    /// change of direction, traces a larger circle than the hand on a circular parry, and rings a few cycles after a
    /// stop. A real épée's first bending mode is around 8 Hz (a cantilever clamped at the guard with the FIE
    /// flexibility of 4.5-7 cm under 200 g, tapering to the point); the <see cref="BladeFlexSoftness"/> Setting picks
    /// 8 Hz (Normal), 11 Hz (Stiff) or 6 Hz (Soft, livelier than a real blade). Positions are in any one frame (the
    /// Strip frame in the app), time in seconds.
    ///
    /// A held blade must look still (PER-48): tracking jitters the point by up to about 7 mm. So the point's motion is
    /// smoothed first, accelerations below <see cref="AccelerationDeadband"/> are ignored, and the drive is gated by how
    /// fast the point has moved lately (<see cref="GateStartMetersPerSecond"/> to <see cref="GateFullMetersPerSecond"/>):
    /// jitter, tremor and slow aiming carry no momentum, while a fast move keeps driving through its stop.
    /// </summary>
    public sealed class BladeFlex
    {
        /// <summary>The upper end of the 4.5-7 cm tip bend scale.</summary>
        public const float MaxBendMeters = 0.07f;

        /// <summary>Point accelerations up to this (m/s^2, after smoothing) are tracking jitter, not a move.</summary>
        public const float AccelerationDeadband = 4.0f;

        /// <summary>A point that has moved no faster than this lately (m/s) drives no flex: a still hand or slow aiming.</summary>
        public const float GateStartMetersPerSecond = 0.2f;

        /// <summary>From this recent point speed (m/s) on, the flex is driven in full.</summary>
        public const float GateFullMetersPerSecond = 0.45f;

        // How much of the point's acceleration drives the first bending mode: its participation factor for a cantilever
        // shaken at the base, about 1.6 to 1.9 depending on the mass along it (the épée is heavier at the guard).
        private const float DriveGain = 1.8f;

        // The recent top speed fades with this time constant, so the drive keeps going through a sudden stop.
        private const float RecentSpeedSeconds = 0.15f;

        // The point's position and velocity are each low-passed with this time constant before differentiating:
        // frame-to-frame tracking noise differentiated twice is far larger than any real acceleration.
        private const float SmoothingSeconds = 0.02f;

        // A let-go bend (the point slid off or came out of a body, the drawn blade caught up with the hand) counts as
        // ringing until it is this small.
        private const float RingingEndsMeters = 0.002f;

        // Integrate in short steps so the spring stays stable even on a long frame.
        private const float MaxSubstepSeconds = 0.004f;
        private const float MaxFrameSeconds = 0.1f;

        private Vector3 _bend;
        private Vector3 _bendVelocity;
        private Vector3? _smoothedTip;
        private Vector3? _smoothedVelocity;
        private float _recentSpeed;
        private bool _isRinging;
        private float _naturalFrequencyHertz;
        private float _dampingRatio;

        public BladeFlex()
        {
            SetSoftness(BladeFlexSoftness.Normal);
        }

        /// <summary>The drawn point minus the straight point.</summary>
        public Vector3 Bend => _bend;

        /// <summary>True while a let-go bend rings out, after <see cref="SpringBackFrom"/>.</summary>
        public bool IsRinging => _isRinging;

        /// <summary>The first bending mode's frequency for the chosen softness, Hz.</summary>
        public float NaturalFrequencyHertz => _naturalFrequencyHertz;

        /// <summary>Picks the blade's stiffness: its first bending mode's frequency and damping.</summary>
        public void SetSoftness(BladeFlexSoftness softness)
        {
            switch (softness)
            {
                case BladeFlexSoftness.Stiff:
                    _naturalFrequencyHertz = 11.0f;
                    _dampingRatio = 0.2f;
                    break;
                case BladeFlexSoftness.Soft:
                    _naturalFrequencyHertz = 6.0f;
                    _dampingRatio = 0.07f;
                    break;
                default:
                    // About four visible cycles before a ring dies away.
                    _naturalFrequencyHertz = 8.0f;
                    _dampingRatio = 0.1f;
                    break;
            }
        }

        /// <summary>Straightens the blade at once, e.g. after the blade jumped.</summary>
        public void Reset()
        {
            _bend = Vector3.Zero;
            _bendVelocity = Vector3.Zero;
            _smoothedTip = null;
            _smoothedVelocity = null;
            _recentSpeed = 0.0f;
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
            var tipAcceleration = FindDrivingAcceleration(straightBlade.TipPosition, frameSeconds) * DriveGain;
            var angularFrequency = 2.0f * (float)Math.PI * _naturalFrequencyHertz;
            var substeps = (int)Math.Ceiling(frameSeconds / MaxSubstepSeconds);
            var substepSeconds = frameSeconds / substeps;
            for (var step = 0; step < substeps; step++)
            {
                var springAcceleration = (-angularFrequency * angularFrequency * _bend) - (2.0f * _dampingRatio * angularFrequency * _bendVelocity);
                _bendVelocity += (springAcceleration - tipAcceleration) * substepSeconds;
                _bend += _bendVelocity * substepSeconds;
            }

            KeepPerpendicularAndLimited(straightBlade.Direction);
            if (_isRinging && _bend.Length() + (_bendVelocity.Length() / angularFrequency) < RingingEndsMeters)
            {
                _isRinging = false;
            }
        }

        /// <summary>
        /// The smoothed point's acceleration with the deadband taken off, scaled by the recent-speed gate; zero until
        /// there are three frames of motion.
        /// </summary>
        private Vector3 FindDrivingAcceleration(Vector3 tip, float frameSeconds)
        {
            if (!_smoothedTip.HasValue)
            {
                _smoothedTip = tip;
                return Vector3.Zero;
            }

            var blend = frameSeconds / (SmoothingSeconds + frameSeconds);
            var previousTip = _smoothedTip.Value;
            var smoothedTip = Vector3.Lerp(previousTip, tip, blend);
            _smoothedTip = smoothedTip;
            var velocity = (smoothedTip - previousTip) / frameSeconds;
            if (!_smoothedVelocity.HasValue)
            {
                _smoothedVelocity = velocity;
                return Vector3.Zero;
            }

            var previousVelocity = _smoothedVelocity.Value;
            var smoothedVelocity = Vector3.Lerp(previousVelocity, velocity, blend);
            _smoothedVelocity = smoothedVelocity;

            _recentSpeed = Math.Max(smoothedVelocity.Length(), _recentSpeed * (float)Math.Exp(-frameSeconds / RecentSpeedSeconds));
            var gate = Math.Max(0.0f, Math.Min(1.0f, (_recentSpeed - GateStartMetersPerSecond) / (GateFullMetersPerSecond - GateStartMetersPerSecond)));
            var acceleration = (smoothedVelocity - previousVelocity) / frameSeconds;
            var magnitude = acceleration.Length();
            if (magnitude <= AccelerationDeadband || gate <= 0.0f)
            {
                return Vector3.Zero;
            }

            // Take the deadband off the size, keeping the direction: no jump at its edge.
            return acceleration * ((magnitude - AccelerationDeadband) / magnitude * gate);
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
