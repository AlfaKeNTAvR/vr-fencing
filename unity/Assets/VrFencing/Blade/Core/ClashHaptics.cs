using System;
using System.Collections.Generic;
using VrFencing.Settings;

namespace VrFencing.Blade
{
    /// <summary>
    /// What the sword hand feels when the Fencer's blade meets the Armed Dummy's (PER-99, decided in PER-23): the chosen
    /// clash pattern, a sharp high hit that rings down, unlike the Touch's single thump, the glance's tick and the flat
    /// contact's soft buzz. It plays louder the harder the contact, from the chosen floor at the lightest contact to full
    /// at <see cref="FullStrengthSpeedMetersPerSecond"/> of relative blade speed. The Crisp variant and the 35% floor are
    /// the prototype's (PER-95, defaults in <see cref="BladeSettings"/>), kept until its feel test picks others.
    /// </summary>
    public static class ClashHaptics
    {
        /// <summary>Relative blade speed at which the clash buzz is full: a firm parry is about 1 to 2 m/s at the blade.</summary>
        public const float FullStrengthSpeedMetersPerSecond = 2.0f;

        private static readonly HapticSegment[] NoSegments = Array.Empty<HapticSegment>();

        // A light ring: the hit and a short tail, for a Fencer who wants the clash quieter than the Touch.
        private static readonly HapticSegment[] ClashTick =
        {
            new HapticSegment(0.8f, 1.0f, 0.010f),
            new HapticSegment(0.35f, 0.8f, 0.025f),
        };

        // The prototype's clash (PER-95): a sharp high hit that rings down, 85 ms.
        private static readonly HapticSegment[] ClashCrisp =
        {
            new HapticSegment(1.0f, 1.0f, 0.015f),
            new HapticSegment(0.55f, 0.85f, 0.030f),
            new HapticSegment(0.25f, 0.7f, 0.040f),
        };

        // Heavier steel: the same hit with a longer, lower ring, 130 ms.
        private static readonly HapticSegment[] ClashHeavy =
        {
            new HapticSegment(1.0f, 0.9f, 0.020f),
            new HapticSegment(0.75f, 0.6f, 0.045f),
            new HapticSegment(0.4f, 0.45f, 0.065f),
        };

        // Two rings close together, like a blade rattling on the other, 110 ms.
        private static readonly HapticSegment[] ClashDoubleTap =
        {
            new HapticSegment(1.0f, 1.0f, 0.015f),
            new HapticSegment(0.4f, 0.8f, 0.015f),
            new HapticSegment(0.0f, 0.0f, 0.030f),
            new HapticSegment(0.8f, 1.0f, 0.015f),
            new HapticSegment(0.3f, 0.7f, 0.035f),
        };

        /// <summary>The clash segments for <paramref name="pattern"/>; empty for <see cref="HapticPattern.Off"/>.</summary>
        public static IReadOnlyList<HapticSegment> GetSegments(HapticPattern pattern)
        {
            switch (pattern)
            {
                case HapticPattern.Off:
                    return NoSegments;
                case HapticPattern.Tick:
                    return ClashTick;
                case HapticPattern.Crisp:
                    return ClashCrisp;
                case HapticPattern.Heavy:
                    return ClashHeavy;
                case HapticPattern.DoubleTap:
                    return ClashDoubleTap;
                default:
                    throw new ArgumentOutOfRangeException(nameof(pattern), pattern, "Not a haptic pattern.");
            }
        }

        /// <summary>
        /// How hard the clash buzz plays for a contact at <paramref name="relativeSpeedMetersPerSecond"/>: from
        /// <paramref name="floor"/> at no speed, rising in a straight line to 1 at <see cref="FullStrengthSpeedMetersPerSecond"/>.
        /// </summary>
        public static float GetStrength(float floor, float relativeSpeedMetersPerSecond)
        {
            var clampedFloor = Math.Clamp(floor, 0.0f, 1.0f);
            var fraction = Math.Clamp(relativeSpeedMetersPerSecond / FullStrengthSpeedMetersPerSecond, 0.0f, 1.0f);
            return clampedFloor + ((1.0f - clampedFloor) * fraction);
        }
    }

    /// <summary>
    /// Lets one clash buzz through per blade contact (<see cref="BladeClashDetector"/> already raises one clash per
    /// contact) and at most one every <see cref="MinIntervalSeconds"/>, so blades that bounce apart and meet again at
    /// once, or rattle along each other, never machine-gun the buzz.
    /// </summary>
    public sealed class ClashBuzzGate
    {
        /// <summary>Shortest time between two clash buzzes: longer than the 85 ms default buzz, shorter than two real beats.</summary>
        public const float MinIntervalSeconds = 0.12f;

        private double? _lastBuzzSeconds;

        /// <summary>True if a clash at <paramref name="nowSeconds"/> may buzz; it then counts as the last buzz.</summary>
        public bool TryPass(double nowSeconds)
        {
            if (_lastBuzzSeconds.HasValue && nowSeconds - _lastBuzzSeconds.Value < MinIntervalSeconds)
            {
                return false;
            }

            _lastBuzzSeconds = nowSeconds;
            return true;
        }

        /// <summary>Forget the last buzz, e.g. when the Armed Dummy is armed again.</summary>
        public void Reset()
        {
            _lastBuzzSeconds = null;
        }
    }
}
