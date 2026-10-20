using System;
using System.Numerics;

namespace VrFencing.Dummy
{
    /// <summary>
    /// The planned path of the Armed Dummy's point in a thrust (PER-96), for judging (Parry, hits on the Fencer): a
    /// straight line from where the wind-up left the point to the aim, run on a minimum-jerk profile (smooth start and
    /// stop, like a reaching arm) whose peak speed is the attack's speed. Positions in the Dummy frame (the Dummy's
    /// own, not mirrored); <see cref="DummyLayout.DummyToStrip(Vector3, float)"/> takes them to the Strip frame.
    /// Times in seconds on the attack's clock. It is the plan: a Parry cuts the real thrust short of it.
    /// </summary>
    public readonly struct AttackPath
    {
        /// <summary>Peak speed of a minimum-jerk move as a multiple of its average speed.</summary>
        public const float MinimumJerkPeakFactor = 1.875f;

        public AttackPath(Vector3 startPoint, Vector3 aimPoint, double startSeconds, float durationSeconds)
        {
            if (!(durationSeconds > 0.0f) || float.IsInfinity(durationSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(durationSeconds), durationSeconds, "Must be a positive number of seconds.");
            }

            StartPoint = startPoint;
            AimPoint = aimPoint;
            StartSeconds = startSeconds;
            DurationSeconds = durationSeconds;
        }

        /// <summary>Where the point is when the thrust starts, at the end of the wind-up.</summary>
        public Vector3 StartPoint { get; }

        /// <summary>Where the point lands: the aim, or as near it as the arm reaches.</summary>
        public Vector3 AimPoint { get; }

        public double StartSeconds { get; }

        public float DurationSeconds { get; }

        public double EndSeconds => StartSeconds + DurationSeconds;

        public float LengthMeters => Vector3.Distance(StartPoint, AimPoint);

        public float PeakSpeedMetersPerSecond => MinimumJerkPeakFactor * LengthMeters / DurationSeconds;

        /// <summary>The planned point at <paramref name="nowSeconds"/>: the start before the thrust, the aim after it.</summary>
        public Vector3 GetPoint(double nowSeconds)
        {
            return Vector3.Lerp(StartPoint, AimPoint, GetProgress(nowSeconds));
        }

        /// <summary>How far along the path the point is at <paramref name="nowSeconds"/>, 0 to 1.</summary>
        public float GetProgress(double nowSeconds)
        {
            var fraction = (float)Math.Clamp((nowSeconds - StartSeconds) / DurationSeconds, 0.0, 1.0);
            return MinimumJerk(fraction);
        }

        /// <summary>10 f^3 - 15 f^4 + 6 f^5: zero speed and acceleration at both ends, peak speed 1.875 at the middle.</summary>
        public static float MinimumJerk(float fraction)
        {
            var cube = fraction * fraction * fraction;
            return cube * (10.0f + (fraction * ((6.0f * fraction) - 15.0f)));
        }
    }
}
