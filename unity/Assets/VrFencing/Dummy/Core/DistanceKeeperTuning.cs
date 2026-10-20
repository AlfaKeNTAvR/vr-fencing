using System;

namespace VrFencing.Dummy
{
    /// <summary>
    /// How hard the moving Dummy is to fence (PER-44: difficulty is a parameter set, not a separate brain). Stage 1
    /// (PER-105) sets only how fast the distance keeper reacts; later stages add attack speed, accuracy and tactics.
    /// </summary>
    public enum DummyDifficulty
    {
        Easy,
        Medium,
        Hard,
    }

    /// <summary>
    /// How long the Dummy takes to react to the Fencer's footwork: a mean delay, and how far one reaction may land
    /// either side of it (drawn evenly from minus to plus <see cref="JitterSeconds"/>), so the Fencer cannot time it.
    /// </summary>
    public readonly struct ReactionTiming
    {
        public ReactionTiming(float delaySeconds, float jitterSeconds)
        {
            if (!(delaySeconds >= 0.0f) || float.IsInfinity(delaySeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(delaySeconds), delaySeconds, "Must be a non-negative number of seconds.");
            }

            if (!(jitterSeconds >= 0.0f) || jitterSeconds > delaySeconds)
            {
                throw new ArgumentOutOfRangeException(nameof(jitterSeconds), jitterSeconds, "Must be between zero and the delay.");
            }

            DelaySeconds = delaySeconds;
            JitterSeconds = jitterSeconds;
        }

        public float DelaySeconds { get; }

        public float JitterSeconds { get; }

        /// <summary>One reaction's delay: the mean plus a jitter drawn evenly from minus to plus <see cref="JitterSeconds"/>.</summary>
        public float DrawDelaySeconds(Random random)
        {
            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            var jitter = ((float)random.NextDouble() * 2.0f - 1.0f) * JitterSeconds;
            return DelaySeconds + jitter;
        }
    }

    /// <summary>
    /// Every number of the moving Dummy's distance keeper (PER-105, stage 1 of PER-44), in one place for tuning on the
    /// headset. None has been confirmed on the headset yet.
    /// </summary>
    public static class DistanceKeeperTuning
    {
        // ---- Reaction delay per difficulty (PER-44: elite fencers average about 350 ms; start here, then tune) ----

        public const float EasyReactionDelaySeconds = 0.6f;

        public const float MediumReactionDelaySeconds = 0.4f;

        public const float HardReactionDelaySeconds = 0.25f;

        // ---- Jitter: one reaction lands up to this much either side of the delay (about a fifth of it) ----

        public const float EasyReactionJitterSeconds = 0.12f;

        public const float MediumReactionJitterSeconds = 0.08f;

        public const float HardReactionJitterSeconds = 0.05f;

        // ---- What counts as the Fencer moving ----

        /// <summary>
        /// The Dummy reacts once the Fencer's front foot is this far from where the Dummy last set the distance. En garde
        /// sway (1 to 2 cm) and an arm extension never move it; an advance or retreat (about 33 cm) and a check (about
        /// 7 cm) do.
        /// </summary>
        public const float ToleranceMeters = 0.06f;

        /// <summary>The Dummy stops following once its target is this close to where the Fencer now is.</summary>
        public const float SettledMeters = 0.02f;

        // ---- Across the Strip (PER-118) ----

        /// <summary>
        /// The Dummy follows this share of the Fencer's side position (measured from the Strip's center line): enough to
        /// see it answer a move aside, not so much that it mirrors every lean.
        /// </summary>
        public const float SideFollowFraction = 0.5f;

        /// <summary>
        /// The Dummy's body stays within this of the Strip's center line. The Strip is 1.5 m wide and the en garde stance
        /// reaches about 0.13 of the height to the side of the front foot, so this keeps the feet 0.2 m or more inside it.
        /// </summary>
        public const float MaxSideMeters = 0.25f;

        /// <summary>
        /// The Dummy reacts once its wanted side is this far from where it last set it: about a 16 cm move aside by the
        /// Fencer. Looser than along the Strip, so sway and a small dodge do not make it shuffle.
        /// </summary>
        public const float SideToleranceMeters = 0.08f;

        /// <summary>The reaction timing for <paramref name="difficulty"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException">Not a difficulty.</exception>
        public static ReactionTiming GetReaction(DummyDifficulty difficulty)
        {
            switch (difficulty)
            {
                case DummyDifficulty.Easy:
                    return new ReactionTiming(EasyReactionDelaySeconds, EasyReactionJitterSeconds);
                case DummyDifficulty.Medium:
                    return new ReactionTiming(MediumReactionDelaySeconds, MediumReactionJitterSeconds);
                case DummyDifficulty.Hard:
                    return new ReactionTiming(HardReactionDelaySeconds, HardReactionJitterSeconds);
                default:
                    throw new ArgumentOutOfRangeException(nameof(difficulty), difficulty, "Not a difficulty.");
            }
        }
    }
}
