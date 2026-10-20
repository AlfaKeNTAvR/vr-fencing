using System;
using System.Numerics;

namespace VrFencing.Blade
{
    /// <summary>
    /// The Armed Dummy's blade knocked aside by the Fencer's (PER-23: "their blade swings away by an amount set by the
    /// Fencer's blade speed and contact point, then recovers"; prototype PER-95). The swing is an offset of the Dummy's
    /// point, square to its blade, on a damped spring back to zero: a <see cref="Kick"/> sends the point out to a peak
    /// that grows with the contact's speed and with how far out along the Dummy's blade the contact was (less leverage
    /// for the Dummy's hand), then it settles back in line in about half a second. Pressed by the Fencer's blade
    /// (PER-104), the spring pulls toward the press's yield instead of the line (<see cref="HoldAt"/>), and the blade is
    /// pushed out of the Fencer's at once (<see cref="Push"/>); let go, it springs back the same way. The point never
    /// goes further than <see cref="MaxPeakMeters"/> off the line: the arm's limit.
    /// Meters, seconds.
    /// </summary>
    public sealed class BladeSwing
    {
        /// <summary>The spring's natural frequency: the swing peaks in about 0.1 s and is back in about 0.5 s.</summary>
        public const float FrequencyHertz = 2.5f;

        /// <summary>Damping ratio: a small overshoot past the line on the way back, as a wrist catching a knocked blade.</summary>
        public const float DampingRatio = 0.6f;

        /// <summary>Peak point offset per m/s of contact speed, for a contact at the Dummy's point.</summary>
        public const float PeakMetersPerMeterPerSecond = 0.15f;

        /// <summary>Share of the full swing a contact at the Dummy's guard still gives (the forte barely moves).</summary>
        public const float GuardLeverage = 0.3f;

        /// <summary>The largest point offset one kick gives: about 22 degrees of a 90 cm blade.</summary>
        public const float MaxPeakMeters = 0.35f;

        // Integration step: well under the spring's period, so the swing is the same at any frame rate.
        private const float MaxStepSeconds = 0.002f;

        private Vector3 _velocity;
        private Vector3 _heldOffset;

        /// <summary>The point's offset now, square to the blade it was kicked off.</summary>
        public Vector3 TipOffset { get; private set; }

        /// <summary>
        /// The peak point offset a contact gives: <see cref="PeakMetersPerMeterPerSecond"/> per m/s, scaled from
        /// <see cref="GuardLeverage"/> at the Dummy's guard to the whole at its point, at most <see cref="MaxPeakMeters"/>.
        /// </summary>
        public static float GetPeakMeters(float relativeSpeedMetersPerSecond, float opponentBladeFraction)
        {
            var leverage = GuardLeverage + ((1.0f - GuardLeverage) * Math.Clamp(opponentBladeFraction, 0.0f, 1.0f));
            return Math.Min(MaxPeakMeters, PeakMetersPerMeterPerSecond * Math.Max(0.0f, relativeSpeedMetersPerSecond) * leverage);
        }

        /// <summary>
        /// The kick speed that, from rest, takes a damped spring out to <paramref name="peakMeters"/>: the peak of
        /// v / w * exp(-z w t) * sin(w_d t) is v / w * exp(-z acos(z) / sqrt(1 - z^2)).
        /// </summary>
        public static float GetKickSpeed(float peakMeters)
        {
            var omega = 2.0 * Math.PI * FrequencyHertz;
            var damping = DampingRatio;
            var peakPerSpeed = Math.Exp(-damping * Math.Acos(damping) / Math.Sqrt(1.0 - (damping * damping))) / omega;
            return (float)(peakMeters / peakPerSpeed);
        }

        /// <summary>Knocks the point aside for a clash; adds to any swing still under way.</summary>
        public void Kick(BladeClash clash)
        {
            var peak = GetPeakMeters(clash.RelativeSpeedMetersPerSecond, clash.OpponentBladeFraction);
            _velocity += clash.PushDirection * GetKickSpeed(peak);
        }

        /// <summary>
        /// Where the spring pulls the point while the Fencer's blade presses on it (PER-104): the press's yield
        /// (<see cref="BladeBind.FindYieldTipOffset"/>), zero when nothing presses, so the blade springs back to its line
        /// on release at the swing's own pace. At most <see cref="MaxPeakMeters"/>, the arm's limit.
        /// </summary>
        public void HoldAt(Vector3 tipOffset)
        {
            _heldOffset = LimitToArm(tipOffset);
        }

        /// <summary>
        /// Moves the point at once by <paramref name="tipOffset"/> (the Fencer's blade in the way, PER-104) and stops its
        /// motion back the other way, as a blade knocked against another does not pass through it. The offset stays
        /// within the arm's limit, <see cref="MaxPeakMeters"/>.
        /// </summary>
        public void Push(Vector3 tipOffset)
        {
            var length = tipOffset.Length();
            if (length < 1e-7f)
            {
                return;
            }

            var direction = tipOffset / length;
            var back = Vector3.Dot(_velocity, direction);
            if (back < 0.0f)
            {
                _velocity -= back * direction;
            }

            TipOffset = LimitToArm(TipOffset + tipOffset);
        }

        public void Reset()
        {
            TipOffset = Vector3.Zero;
            _velocity = Vector3.Zero;
            _heldOffset = Vector3.Zero;
        }

        public void Step(float deltaSeconds)
        {
            if (!(deltaSeconds > 0.0f))
            {
                return;
            }

            var omega = (float)(2.0 * Math.PI * FrequencyHertz);
            var steps = (int)Math.Ceiling(deltaSeconds / MaxStepSeconds);
            var step = deltaSeconds / steps;
            var offset = TipOffset;
            for (var index = 0; index < steps; index++)
            {
                // Semi-implicit Euler: stable for a spring at this step.
                var acceleration = (-omega * omega * (offset - _heldOffset)) - (2.0f * DampingRatio * omega * _velocity);
                _velocity += acceleration * step;
                offset += _velocity * step;
            }

            TipOffset = LimitToArm(offset);
        }

        private static Vector3 LimitToArm(Vector3 tipOffset)
        {
            var length = tipOffset.Length();
            return length > MaxPeakMeters ? tipOffset * (MaxPeakMeters / length) : tipOffset;
        }

        /// <summary>The blade turned about its guard so its point carries the offset, keeping its length.</summary>
        public BladeSegment Apply(BladeSegment blade)
        {
            var along = blade.TipPosition - blade.GuardPosition;
            var length = along.Length();
            if (length < 1e-6f)
            {
                return blade;
            }

            var axis = along / length;
            var square = TipOffset - (Vector3.Dot(TipOffset, axis) * axis);
            var swung = Vector3.Normalize(along + square);
            return new BladeSegment(blade.GuardPosition, blade.GuardPosition + (swung * length));
        }
    }
}
