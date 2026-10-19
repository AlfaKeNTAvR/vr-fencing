// PER-22 prototype: the variants Nikita cycles through on the headset.
// Throwaway tuning data; edit the numbers here and rebuild to try others.
using UnityEngine;

namespace VrFencing.Prototypes.BladeFeel
{
    /// <summary>Where the front of the guard sits relative to the right controller anchor.</summary>
    public readonly struct BladeMountPreset
    {
        public readonly string Name;
        /// <summary>Along the controller's forward axis; positive is towards the blade.</summary>
        public readonly float GuardForwardMeters;

        public BladeMountPreset(string name, float guardForwardMeters)
        {
            Name = name;
            GuardForwardMeters = guardForwardMeters;
        }
    }

    public readonly struct HapticSegment
    {
        public readonly float Amplitude;
        /// <summary>OVRInput frequency 0..1 (Touch controllers map this to their actuator range).</summary>
        public readonly float Frequency;
        public readonly float DurationSeconds;

        public HapticSegment(float amplitude, float frequency, float durationSeconds)
        {
            Amplitude = amplitude;
            Frequency = frequency;
            DurationSeconds = durationSeconds;
        }
    }

    public sealed class HapticPreset
    {
        public readonly string Name;
        public readonly HapticSegment[] Touch;
        public readonly HapticSegment[] Flat;

        public HapticPreset(string name, HapticSegment[] touch, HapticSegment[] flat)
        {
            Name = name;
            Touch = touch;
            Flat = flat;
        }
    }

    public static class BladeFeelPresets
    {
        public const float BladeLengthMeters = 0.90f;   // FIE m.16.3 maximum
        public const float GuardDiameterMeters = 0.135f; // FIE m.17.1
        public const float GuardDepthMeters = 0.04f;     // FIE m.17.1: 3-5.5 cm
        public const float BladeVisualDiameterMeters = 0.008f;
        public const float TipMarkerDiameterMeters = 0.016f;
        public const float PitchStepDegrees = 5f;

        public static readonly BladeMountPreset[] BladeMounts =
        {
            new("Center (guard at controller centre)", 0.00f),
            new("Pistol (guard +5 cm)", 0.05f),
            new("French posted (guard +12 cm)", 0.12f),
        };

        public static readonly HapticPreset[] Haptics =
        {
            new("Crisp",
                new[] { new HapticSegment(1.0f, 1.0f, 0.030f) },
                new[] { new HapticSegment(0.30f, 0.3f, 0.200f) }),
            new("Heavy",
                new[] { new HapticSegment(1.0f, 0.5f, 0.070f) },
                new[] { new HapticSegment(0.20f, 1.0f, 0.300f) }),
            new("Double-tap",
                new[]
                {
                    new HapticSegment(1.0f, 1.0f, 0.025f),
                    new HapticSegment(0f, 0f, 0.040f),
                    new HapticSegment(1.0f, 1.0f, 0.025f),
                },
                new[]
                {
                    new HapticSegment(0.35f, 0.2f, 0.120f),
                    new HapticSegment(0f, 0f, 0.060f),
                    new HapticSegment(0.20f, 0.2f, 0.120f),
                }),
        };

        /// <summary>A static Target, placed relative to the Fencer's start pose (feet on the floor, facing forward).</summary>
        public readonly struct TargetLayout
        {
            public readonly string Id;
            public readonly float ForwardMeters;
            public readonly float RightMeters;
            public readonly float HeightMeters;
            public readonly float RadiusMeters;

            public TargetLayout(string id, float forwardMeters, float rightMeters, float heightMeters, float radiusMeters)
            {
                Id = id;
                ForwardMeters = forwardMeters;
                RightMeters = rightMeters;
                HeightMeters = heightMeters;
                RadiusMeters = radiusMeters;
            }
        }

        // An imaginary opponent standing about 2.3 m away: hand and forearm reachable
        // with an arm extension, torso/thigh/foot need a lunge.
        public static readonly TargetLayout[] Targets =
        {
            new("hand", 1.6f, 0.15f, 1.00f, 0.05f),
            new("forearm", 1.85f, 0.20f, 1.15f, 0.05f),
            new("torso", 2.35f, 0.0f, 1.30f, 0.15f),
            new("thigh", 2.2f, 0.10f, 0.70f, 0.08f),
            new("foot", 2.0f, 0.15f, 0.10f, 0.06f),
        };

        public static readonly Color TargetIdleColor = new(0.85f, 0.85f, 0.9f);
        public static readonly Color TouchColor = new(0.1f, 0.95f, 0.2f);
        public static readonly Color FlatColor = new(1f, 0.6f, 0.05f);
        public static readonly Color GlanceColor = new(0.3f, 0.5f, 1f);
        public static readonly Color BladeColor = new(0.75f, 0.77f, 0.8f);
        public static readonly Color TipColor = new(0.95f, 0.15f, 0.15f);
        public const float FlashSeconds = 0.35f;
    }
}
