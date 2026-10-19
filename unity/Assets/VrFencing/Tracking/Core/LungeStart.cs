using System.Collections.Generic;

namespace VrFencing.Tracking
{
    /// <summary>How far along the Strip (Strip-frame z, toward the Dummy) the Fencer's head was at a moment.</summary>
    public readonly struct ForwardPosition
    {
        public ForwardPosition(double timeSeconds, float forwardMeters)
        {
            TimeSeconds = timeSeconds;
            ForwardMeters = forwardMeters;
        }

        public double TimeSeconds { get; }

        public float ForwardMeters { get; }
    }

    /// <summary>
    /// Finds when a lunge started from the head's forward motion along the Strip. The head is used rather than
    /// the IOBT pelvis because headset tracking is always available and precise, while the pelvis is mostly
    /// inferred from the head anyway.
    /// <para>
    /// The lunge is the fastest forward burst in the samples: its peak head speed must reach
    /// <see cref="MinPeakSpeedMetersPerSecond"/>. It started at the last moment before that peak when the head
    /// was moving forward slower than <see cref="OnsetSpeedMetersPerSecond"/>. A step-lunge with no pause
    /// between the step and the lunge therefore starts at the step.
    /// </para>
    /// </summary>
    public static class LungeStart
    {
        /// <summary>
        /// Forward head speed a lunge must reach. A lunge drives the head forward at about 1.5 to 3 m/s; leaning in
        /// or reaching with the arm stays well under 1 m/s. Tune on the headset.
        /// </summary>
        public const float MinPeakSpeedMetersPerSecond = 0.9f;

        /// <summary>Forward head speed below which the Fencer counts as not yet lunging (en garde sway stays below it).</summary>
        public const float OnsetSpeedMetersPerSecond = 0.2f;

        /// <summary>
        /// Speed is measured over at least this span of samples (about 4 frames at 72 Hz), so single-frame tracking
        /// jitter does not read as motion.
        /// </summary>
        public const double SpeedWindowSeconds = 0.05;

        /// <summary>
        /// The time the lunge started, or null if no forward burst reached <see cref="MinPeakSpeedMetersPerSecond"/>.
        /// Samples must be in time order.
        /// </summary>
        public static double? FindStartSeconds(IReadOnlyList<ForwardPosition> track)
        {
            var speeds = MeasureForwardSpeeds(track);

            var peakIndex = -1;
            for (var i = 0; i < speeds.Length; i++)
            {
                if (speeds[i].HasValue && (peakIndex < 0 || speeds[i].Value > speeds[peakIndex].Value))
                {
                    peakIndex = i;
                }
            }

            if (peakIndex < 0 || speeds[peakIndex].Value < MinPeakSpeedMetersPerSecond)
            {
                return null;
            }

            // Speeds are unknown only at the very start (too early to measure); a lunge already under way there
            // started at or before the first sample.
            var onsetIndex = peakIndex;
            while (onsetIndex > 0 && (!speeds[onsetIndex].HasValue || speeds[onsetIndex].Value >= OnsetSpeedMetersPerSecond))
            {
                onsetIndex--;
            }

            return track[onsetIndex].TimeSeconds;
        }

        /// <summary>
        /// Forward speed at each sample over the span back to the latest sample at least
        /// <see cref="SpeedWindowSeconds"/> earlier; null where no sample is that far back.
        /// </summary>
        private static float?[] MeasureForwardSpeeds(IReadOnlyList<ForwardPosition> track)
        {
            var speeds = new float?[track.Count];
            var spanStart = -1;
            for (var i = 0; i < track.Count; i++)
            {
                var latestSpanStartTime = track[i].TimeSeconds - SpeedWindowSeconds;
                while (spanStart + 1 < i && track[spanStart + 1].TimeSeconds <= latestSpanStartTime)
                {
                    spanStart++;
                }

                if (spanStart < 0 || track[spanStart].TimeSeconds > latestSpanStartTime)
                {
                    continue;
                }

                var elapsed = track[i].TimeSeconds - track[spanStart].TimeSeconds;
                speeds[i] = (float)((track[i].ForwardMeters - track[spanStart].ForwardMeters) / elapsed);
            }

            return speeds;
        }
    }
}
