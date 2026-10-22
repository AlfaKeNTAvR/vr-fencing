using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using VrFencing.Settings;

namespace VrFencing.Blade
{
    /// <summary>One step of a haptic pattern: amplitude and frequency from 0 to 1, held for a duration. Amplitude 0 is a gap.</summary>
    public readonly struct HapticSegment
    {
        public HapticSegment(float amplitude, float frequency, float durationSeconds)
        {
            Amplitude = amplitude;
            Frequency = frequency;
            DurationSeconds = durationSeconds;
        }

        public float Amplitude { get; }

        public float Frequency { get; }

        public float DurationSeconds { get; }
    }

    /// <summary>
    /// What the sword hand feels for each Blade event (PER-22): the chosen <see cref="HapticPattern"/> per event, played
    /// louder the faster the tip was moving, never below <see cref="BladeSettings.HapticFloor"/>. The Unity layer plays
    /// the patterns through the Meta Haptics SDK, since OVRInput vibration does nothing under OpenXR.
    /// </summary>
    public static class BladeHaptics
    {
        // A glance's patterns are the Touch's, this much weaker: "a light buzz, weaker than a Touch" (PER-22).
        private const float GlanceAmplitudeScale = 0.6f;

        private static readonly HapticSegment[] NoSegments = Array.Empty<HapticSegment>();

        // The Touch variants, from the feel-test prototype's presets.
        private static readonly HapticSegment[] TouchTick = { new HapticSegment(0.6f, 0.9f, 0.020f) };
        private static readonly HapticSegment[] TouchCrisp = { new HapticSegment(1.0f, 1.0f, 0.030f) };
        private static readonly HapticSegment[] TouchHeavy = { new HapticSegment(1.0f, 0.5f, 0.070f) };
        private static readonly HapticSegment[] TouchDoubleTap =
        {
            new HapticSegment(1.0f, 1.0f, 0.025f),
            new HapticSegment(0.0f, 0.0f, 0.040f),
            new HapticSegment(1.0f, 1.0f, 0.025f),
        };

        // The flat-contact variants, from the same presets: softer and longer than a Touch.
        private static readonly HapticSegment[] FlatTick = { new HapticSegment(0.3f, 0.3f, 0.030f) };
        private static readonly HapticSegment[] FlatCrisp = { new HapticSegment(0.30f, 0.3f, 0.200f) };
        private static readonly HapticSegment[] FlatHeavy = { new HapticSegment(0.20f, 1.0f, 0.300f) };
        private static readonly HapticSegment[] FlatDoubleTap =
        {
            new HapticSegment(0.35f, 0.2f, 0.120f),
            new HapticSegment(0.0f, 0.0f, 0.060f),
            new HapticSegment(0.20f, 0.2f, 0.120f),
        };

        public static HapticPattern GetChosenPattern(BladeSettings blade, BladeEventType eventType)
        {
            switch (eventType)
            {
                case BladeEventType.Touch:
                    return blade.TouchHaptic;
                case BladeEventType.FlatContact:
                    return blade.FlatHaptic;
                case BladeEventType.TipGlance:
                    return blade.GlanceHaptic;
                default:
                    throw new ArgumentOutOfRangeException(nameof(eventType), eventType, "Not a Blade event.");
            }
        }

        /// <summary>The segments of <paramref name="pattern"/> as played for <paramref name="eventType"/>; empty for <see cref="HapticPattern.Off"/>.</summary>
        public static IReadOnlyList<HapticSegment> GetSegments(BladeEventType eventType, HapticPattern pattern)
        {
            if (pattern == HapticPattern.Off)
            {
                return NoSegments;
            }

            switch (eventType)
            {
                case BladeEventType.Touch:
                    return GetTouchSegments(pattern);
                case BladeEventType.TipGlance:
                    return Scale(GetTouchSegments(pattern), GlanceAmplitudeScale);
                case BladeEventType.FlatContact:
                    return GetFlatSegments(pattern);
                default:
                    throw new ArgumentOutOfRangeException(nameof(eventType), eventType, "Not a Blade event.");
            }
        }

        /// <summary>
        /// The strength multiplier for a contact at <paramref name="tipSpeedMetersPerSecond"/>: rises in a straight line
        /// from the floor at standstill to 1 at the full-strength speed, and stays at 1 above it.
        /// </summary>
        public static float GetStrength(BladeSettings blade, float tipSpeedMetersPerSecond)
        {
            var floor = Clamp01(blade.HapticFloor);
            if (!(blade.HapticFullStrengthSpeedMetersPerSecond > 0.0f))
            {
                return 1.0f;
            }

            var speedFraction = Clamp01(Math.Max(0.0f, tipSpeedMetersPerSecond) / blade.HapticFullStrengthSpeedMetersPerSecond);
            return floor + ((1.0f - floor) * speedFraction);
        }

        /// <summary>
        /// The short buzz repeated every <see cref="PressPulseIntervalSeconds"/> while the point is pinned and the hand
        /// presses on (PER-48), played at <see cref="GetPressStrength"/>. Gaps between pulses keep it a buzz, not a hum.
        /// </summary>
        public static IReadOnlyList<HapticSegment> PressPulse { get; } = new[] { new HapticSegment(1.0f, 0.6f, 0.035f) };

        /// <summary>
        /// Being touched in a bout (PER-82, decided in PER-74): a short buzz on both controllers, unlike any blade event
        /// (those play on the sword hand only): a low rumble, a gap, and a sharp pulse, 0.2 s in all.
        /// </summary>
        public static IReadOnlyList<HapticSegment> BeingTouched { get; } = new[]
        {
            new HapticSegment(1.0f, 0.2f, 0.090f),
            new HapticSegment(0.0f, 0.0f, 0.040f),
            new HapticSegment(1.0f, 1.0f, 0.070f),
        };

        /// <summary>
        /// The Armed Dummy's point landing on the Fencer (PER-99, decided in PER-23 as a sharp buzz unlike a Touch): on
        /// both controllers, a hard sharp jab and then a low rumble that fades, 0.28 s in all. The reverse of
        /// <see cref="BeingTouched"/> (rumble first, jab last), longer and lower than any blade event or the clash.
        /// </summary>
        public static IReadOnlyList<HapticSegment> ArmedDummyHit { get; } = new[]
        {
            new HapticSegment(1.0f, 1.0f, 0.040f),
            new HapticSegment(0.8f, 0.3f, 0.080f),
            new HapticSegment(0.5f, 0.2f, 0.080f),
            new HapticSegment(0.25f, 0.15f, 0.080f),
        };

        /// <summary>How often the press buzz repeats while pinned.</summary>
        public const float PressPulseIntervalSeconds = 0.06f;

        /// <summary>The press buzz at the lightest press, as a fraction of full: still felt.</summary>
        public const float PressFloor = 0.25f;

        /// <summary>
        /// How hard the press buzz plays (PER-48): from <see cref="PressFloor"/> as the point lands, rising in a straight
        /// line with the push to full at <see cref="BladeContactBend.MaxPushMeters"/>, and full while the hand is past
        /// it (the drawn blade lags the hand).
        /// </summary>
        /// <param name="isOnFloor">The point is pinned on the floor, not a body.</param>
        /// <param name="isFloorBuzzOn">
        /// The "Floor buzz" Setting (<see cref="BladeSettings.FloorBuzz"/>): off, a point on the floor buzzes not at all (0).
        /// The blade still stops and bends on the floor; only the buzz goes.
        /// </param>
        public static float GetPressStrength(float pushMeters, bool isDecoupled, bool isOnFloor = false, bool isFloorBuzzOn = true)
        {
            if (isOnFloor && !isFloorBuzzOn)
            {
                return 0.0f;
            }

            if (isDecoupled)
            {
                return 1.0f;
            }

            var pushFraction = Clamp01(Math.Max(0.0f, pushMeters) / BladeContactBend.MaxPushMeters);
            return PressFloor + ((1.0f - PressFloor) * pushFraction);
        }

        /// <summary>
        /// The bind buzz (PER-122): while the blades press on each other, a low, steady hum in place of the force a real
        /// blade would push back with (controllers have none). Each pulse runs a little past the next one's start
        /// (<see cref="BindPulseIntervalSeconds"/>), so it is continuous, unlike the gapped press buzz of a pinned point;
        /// low (0.15) where the clash rings high (0.7 to 1) and the Touch, flat, glance and hit are short.
        /// </summary>
        public static IReadOnlyList<HapticSegment> BindPulse { get; } = new[] { new HapticSegment(1.0f, 0.15f, 0.075f) };

        /// <summary>How often the bind buzz's pulse restarts while the blades press.</summary>
        public const float BindPulseIntervalSeconds = 0.06f;

        /// <summary>The bind buzz as the blades just touch, as a fraction of full: still felt.</summary>
        public const float BindFloor = 0.2f;

        /// <summary>
        /// The press at which the bind buzz is full: the hands' blades this far into each other (the overlap the contact
        /// solver takes up, <see cref="BladeContactSolver.PressMeters"/>), about where a grip and a bow are at their limits.
        /// </summary>
        public const float BindFullPressMeters = 0.04f;

        /// <summary>
        /// How hard the bind buzz plays for a press of <paramref name="pressMeters"/>: 0 with no press; from
        /// <see cref="BindFloor"/> rising in a straight line to full at <see cref="BindFullPressMeters"/>.
        /// </summary>
        public static float GetBindStrength(float pressMeters)
        {
            if (!(pressMeters > 0.0f))
            {
                return 0.0f;
            }

            return BindFloor + ((1.0f - BindFloor) * Clamp01(pressMeters / BindFullPressMeters));
        }

        /// <summary>
        /// A Meta Haptics Studio clip (.haptic, version 1) for the segments: stepped amplitude and frequency envelopes.
        /// Plain text, so the Unity layer can build clips at runtime without assets.
        /// </summary>
        public static string BuildClipJson(IReadOnlyList<HapticSegment> segments)
        {
            // Each step holds its value until just before the next starts, so the envelope is square, not ramped.
            const float EdgeSeconds = 0.0005f;
            var amplitude = new StringBuilder();
            var frequency = new StringBuilder();
            var time = 0.0f;
            foreach (var segment in segments)
            {
                var end = time + segment.DurationSeconds;
                AppendPoint(amplitude, "amplitude", time, segment.Amplitude);
                AppendPoint(amplitude, "amplitude", end - EdgeSeconds, segment.Amplitude);
                AppendPoint(frequency, "frequency", time, segment.Frequency);
                AppendPoint(frequency, "frequency", end - EdgeSeconds, segment.Frequency);
                time = end;
            }

            AppendPoint(amplitude, "amplitude", time, 0.0f);
            AppendPoint(frequency, "frequency", time, 0.0f);
            return "{\"version\":{\"major\":1,\"minor\":0,\"patch\":0}," +
                "\"metadata\":{\"editor\":\"VR Fencing\",\"source\":\"\",\"project\":\"blade\",\"tags\":[],\"description\":\"\"}," +
                "\"signals\":{\"continuous\":{\"envelopes\":{" +
                $"\"amplitude\":[{amplitude}],\"frequency\":[{frequency}]" + "}}}}";
        }

        private static HapticSegment[] GetTouchSegments(HapticPattern pattern)
        {
            switch (pattern)
            {
                case HapticPattern.Tick:
                    return TouchTick;
                case HapticPattern.Crisp:
                    return TouchCrisp;
                case HapticPattern.Heavy:
                    return TouchHeavy;
                case HapticPattern.DoubleTap:
                    return TouchDoubleTap;
                default:
                    throw new ArgumentOutOfRangeException(nameof(pattern), pattern, "Not a haptic pattern.");
            }
        }

        private static HapticSegment[] GetFlatSegments(HapticPattern pattern)
        {
            switch (pattern)
            {
                case HapticPattern.Tick:
                    return FlatTick;
                case HapticPattern.Crisp:
                    return FlatCrisp;
                case HapticPattern.Heavy:
                    return FlatHeavy;
                case HapticPattern.DoubleTap:
                    return FlatDoubleTap;
                default:
                    throw new ArgumentOutOfRangeException(nameof(pattern), pattern, "Not a haptic pattern.");
            }
        }

        private static HapticSegment[] Scale(HapticSegment[] segments, float amplitudeScale)
        {
            var scaled = new HapticSegment[segments.Length];
            for (var index = 0; index < segments.Length; index++)
            {
                var segment = segments[index];
                scaled[index] = new HapticSegment(segment.Amplitude * amplitudeScale, segment.Frequency, segment.DurationSeconds);
            }

            return scaled;
        }

        private static void AppendPoint(StringBuilder points, string key, float time, float value)
        {
            if (points.Length > 0)
            {
                points.Append(',');
            }

            points.Append("{\"time\":").Append(time.ToString("0.####", CultureInfo.InvariantCulture))
                .Append(",\"").Append(key).Append("\":").Append(Clamp01(value).ToString("0.###", CultureInfo.InvariantCulture))
                .Append('}');
        }

        private static float Clamp01(float value)
        {
            return value < 0.0f ? 0.0f : (value > 1.0f ? 1.0f : value);
        }
    }
}
