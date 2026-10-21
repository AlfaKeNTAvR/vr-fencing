using System;

namespace VrFencing.Blade
{
    /// <summary>
    /// The steel scrape while the blades press on each other (PER-122, the "Contact sound" Setting): a looping noise
    /// whose loudness follows how hard they press (<see cref="BladeContactSolver.PressMeters"/>) and whose pitch follows
    /// how fast the contact slides along the blades. Silent when they do not touch. The Unity layer plays it.
    /// </summary>
    public static class ScrapeSound
    {
        /// <summary>Loudness as the blades just touch, of full: a held touch is heard, faintly.</summary>
        public const float QuietVolume = 0.15f;

        /// <summary>The press at which the scrape is full loudness, as for the bind buzz.</summary>
        public const float FullPressMeters = BladeHaptics.BindFullPressMeters;

        /// <summary>A slide this fast along the blades and faster adds full loudness on top of the press's share.</summary>
        public const float FullSlideMetersPerSecond = 1.0f;

        /// <summary>The loop's pitch with the blades still on each other, as a playback rate.</summary>
        public const float LowPitch = 0.6f;

        /// <summary>The loop's pitch at <see cref="FullSlideMetersPerSecond"/> or faster: a glide rings higher.</summary>
        public const float HighPitch = 1.6f;

        /// <summary>
        /// How loud the scrape plays, 0 to 1: silent with no press; from <see cref="QuietVolume"/>, rising with the press
        /// to full at <see cref="FullPressMeters"/>, and with the slide.
        /// </summary>
        public static float GetVolume(float pressMeters, float slideMetersPerSecond)
        {
            if (!(pressMeters > 0.0f))
            {
                return 0.0f;
            }

            var press = Clamp01(pressMeters / FullPressMeters);
            var slide = Clamp01(Math.Abs(slideMetersPerSecond) / FullSlideMetersPerSecond);
            return Clamp01(QuietVolume + ((1.0f - QuietVolume) * Math.Max(press, slide)));
        }

        /// <summary>The loop's playback rate for a slide of <paramref name="slideMetersPerSecond"/> along the blades.</summary>
        public static float GetPitch(float slideMetersPerSecond)
        {
            var slide = Clamp01(Math.Abs(slideMetersPerSecond) / FullSlideMetersPerSecond);
            return LowPitch + ((HighPitch - LowPitch) * slide);
        }

        /// <summary>
        /// How fast the contact slides along the blades between two frames: the larger of its moves along either blade,
        /// from where it was on each (fractions of a blade of <paramref name="bladeLengthMeters"/>).
        /// </summary>
        public static float FindSlideSpeed(float fractionABefore, float fractionA, float fractionBBefore, float fractionB, float bladeLengthMeters, float deltaSeconds)
        {
            if (!(deltaSeconds > 0.0f))
            {
                return 0.0f;
            }

            var moved = Math.Max(Math.Abs(fractionA - fractionABefore), Math.Abs(fractionB - fractionBBefore)) * bladeLengthMeters;
            return moved / deltaSeconds;
        }

        /// <summary>
        /// The scrape's loop, mono, at <paramref name="sampleRate"/>: noise rung through a few narrow steel resonances
        /// (<see cref="RingHertz"/>), peaking at <see cref="LoopPeak"/>, its end crossfaded into its start so it loops
        /// without a click. The same every time.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The sample rate is too low for the resonances.</exception>
        public static float[] BuildLoop(int sampleRate)
        {
            if (sampleRate < 4 * RingHertz[RingHertz.Length - 1])
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Too low for the scrape's resonances.");
            }

            var count = (int)(sampleRate * LoopSeconds);
            var fade = (int)(sampleRate * LoopFadeSeconds);
            var raw = new float[count + fade];
            var random = new Random(LoopSeed);
            var rings = new Resonator[RingHertz.Length];
            for (var ring = 0; ring < rings.Length; ring++)
            {
                rings[ring] = new Resonator(RingHertz[ring], RingQuality, sampleRate);
            }

            for (var index = 0; index < raw.Length; index++)
            {
                var noise = (float)((random.NextDouble() * 2.0) - 1.0);
                var sample = noise * NoiseShare;
                for (var ring = 0; ring < rings.Length; ring++)
                {
                    sample += rings[ring].Step(noise);
                }

                raw[index] = sample;
            }

            var loop = new float[count];
            var peak = 1e-6f;
            for (var index = 0; index < count; index++)
            {
                var weight = index < fade ? (float)index / fade : 1.0f;
                loop[index] = index < fade ? (raw[index] * weight) + (raw[count + index] * (1.0f - weight)) : raw[index];
                peak = Math.Max(peak, Math.Abs(loop[index]));
            }

            for (var index = 0; index < count; index++)
            {
                loop[index] *= LoopPeak / peak;
            }

            return loop;
        }

        /// <summary>The loop's length.</summary>
        public const float LoopSeconds = 1.0f;

        /// <summary>The loop's loudest sample.</summary>
        public const float LoopPeak = 0.8f;

        // The steel's ringing: a few narrow resonances of a thin blade rubbed, and how narrow (Q).
        private static readonly float[] RingHertz = { 2300.0f, 3650.0f, 5200.0f, 7900.0f };
        private const float RingQuality = 12.0f;

        // The share of plain noise (the hiss of the rub) under the rings.
        private const float NoiseShare = 0.08f;

        // The end crossfaded into the start, and the noise's seed (the same loop every build).
        private const float LoopFadeSeconds = 0.05f;
        private const int LoopSeed = 122;

        private static float Clamp01(float value)
        {
            return value < 0.0f ? 0.0f : (value > 1.0f ? 1.0f : value);
        }

        /// <summary>A band-pass biquad (0 dB peak gain), Robert Bristow-Johnson's cookbook.</summary>
        private sealed class Resonator
        {
            private readonly float _b0;
            private readonly float _b2;
            private readonly float _a1;
            private readonly float _a2;
            private float _x1;
            private float _x2;
            private float _y1;
            private float _y2;

            public Resonator(float hertz, float quality, int sampleRate)
            {
                var omega = 2.0 * Math.PI * hertz / sampleRate;
                var alpha = Math.Sin(omega) / (2.0 * quality);
                var a0 = 1.0 + alpha;
                _b0 = (float)(alpha / a0);
                _b2 = (float)(-alpha / a0);
                _a1 = (float)(-2.0 * Math.Cos(omega) / a0);
                _a2 = (float)((1.0 - alpha) / a0);
            }

            public float Step(float input)
            {
                var output = (_b0 * input) + (_b2 * _x2) - (_a1 * _y1) - (_a2 * _y2);
                _x2 = _x1;
                _x1 = input;
                _y2 = _y1;
                _y1 = output;
                return output;
            }
        }
    }
}
