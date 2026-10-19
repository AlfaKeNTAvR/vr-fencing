// Synthetic head motion for the Tracking tests: 72 Hz frames, with forward bursts shaped like a
// lunge (speed rises and falls smoothly as sin^2 over the burst).
using System;
using System.Collections.Generic;
using VrFencing.Tracking;

namespace VrFencing.Tests.Tracking
{
    internal static class TrackingTestData
    {
        public const double FrameSeconds = 1.0 / 72.0;

        /// <summary>A forward burst: starts at <see cref="StartSeconds"/>, lasts <see cref="DurationSeconds"/>, peaks at <see cref="PeakSpeed"/> m/s.</summary>
        public readonly struct Burst
        {
            public Burst(double startSeconds, double durationSeconds, float peakSpeed)
            {
                StartSeconds = startSeconds;
                DurationSeconds = durationSeconds;
                PeakSpeed = peakSpeed;
            }

            public double StartSeconds { get; }

            public double DurationSeconds { get; }

            public float PeakSpeed { get; }

            public double SpeedAt(double timeSeconds)
            {
                var phase = (timeSeconds - StartSeconds) / DurationSeconds;
                if (phase < 0.0 || phase > 1.0)
                {
                    return 0.0;
                }

                var sine = Math.Sin(Math.PI * phase);
                return PeakSpeed * sine * sine;
            }
        }

        /// <summary>A typical lunge: 0.5 s, peaking at 2 m/s (about 0.5 m forward).</summary>
        public static Burst Lunge(double startSeconds)
        {
            return new Burst(startSeconds, 0.5, 2.0f);
        }

        /// <summary>
        /// Head forward positions frame by frame from 0 to <paramref name="durationSeconds"/>, moving with the bursts
        /// plus a small en garde sway (1 cm at 1 Hz), starting at <paramref name="startForwardMeters"/>.
        /// </summary>
        public static List<ForwardPosition> HeadTrack(double durationSeconds, float startForwardMeters, params Burst[] bursts)
        {
            var track = new List<ForwardPosition>();
            var forward = (double)startForwardMeters;
            var frameCount = (int)Math.Round(durationSeconds / FrameSeconds);
            for (var frame = 0; frame <= frameCount; frame++)
            {
                var time = frame * FrameSeconds;
                if (frame > 0)
                {
                    foreach (var burst in bursts)
                    {
                        forward += burst.SpeedAt(time - (FrameSeconds / 2.0)) * FrameSeconds;
                    }
                }

                var sway = 0.01 * Math.Sin(2.0 * Math.PI * time);
                track.Add(new ForwardPosition(time, (float)(forward + sway)));
            }

            return track;
        }
    }
}
