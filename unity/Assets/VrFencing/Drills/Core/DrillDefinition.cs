using System;
using System.Collections.Generic;
using System.Linq;
using VrFencing.Dummy;

namespace VrFencing.Drills
{
    /// <summary>How a Rep's Target Zone is announced to the Fencer. The runner passes it through; the Dummy and UI render it.</summary>
    public enum CueType
    {
        /// <summary>The app names the Target Zone (point control, lunge to target).</summary>
        NamedTargetZone,

        /// <summary>The Target Zone lights up and goes dark when the window closes (reaction).</summary>
        LitTargetZone,
    }

    /// <summary>How a Rep is judged. Later Drills (compound sequence, footwork calls) add their own rules here.</summary>
    public enum SuccessRule
    {
        /// <summary>A point-first Touch on the cued Target Zone within the window. A wrong zone, a flat contact or a timeout fails.</summary>
        PointFirstTouchOnTargetZone,
    }

    /// <summary>
    /// One Drill as data (Drill decision PER-21, Strip decision PER-24). Specific Drills are built as instances of this,
    /// with the Rep count, rest and window taken from <c>TrainerSettings</c>.
    /// Validated on construction, so a <see cref="DrillRunner"/> never sees an impossible Drill.
    /// </summary>
    public sealed class DrillDefinition
    {
        public DrillDefinition(
            string id,
            string name,
            int order,
            float requiredStripLengthMeters,
            float? shrunkStripLengthMeters,
            int repCount,
            float cueSeconds,
            float windowSeconds,
            float restSeconds,
            CueType cueType,
            SuccessRule successRule,
            IReadOnlyList<TargetZone> targetZones)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A Drill needs an id.", nameof(id));
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A Drill needs a name.", nameof(name));
            }

            if (!(requiredStripLengthMeters > 0.0f))
            {
                throw new ArgumentOutOfRangeException(nameof(requiredStripLengthMeters), requiredStripLengthMeters, "Must be positive.");
            }

            if (shrunkStripLengthMeters.HasValue
                && !(shrunkStripLengthMeters.Value > 0.0f && shrunkStripLengthMeters.Value < requiredStripLengthMeters))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(shrunkStripLengthMeters), shrunkStripLengthMeters, "Must be positive and shorter than the required Strip length.");
            }

            if (repCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(repCount), repCount, "A Drill needs at least one Rep.");
            }

            if (!(cueSeconds >= 0.0f))
            {
                throw new ArgumentOutOfRangeException(nameof(cueSeconds), cueSeconds, "Must not be negative.");
            }

            if (!(windowSeconds > 0.0f))
            {
                throw new ArgumentOutOfRangeException(nameof(windowSeconds), windowSeconds, "Must be positive.");
            }

            if (!(restSeconds >= 0.0f))
            {
                throw new ArgumentOutOfRangeException(nameof(restSeconds), restSeconds, "Must not be negative.");
            }

            if (targetZones == null || targetZones.Count == 0)
            {
                throw new ArgumentException("A Drill needs at least one Target Zone to cue.", nameof(targetZones));
            }

            Id = id;
            Name = name;
            Order = order;
            RequiredStripLengthMeters = requiredStripLengthMeters;
            ShrunkStripLengthMeters = shrunkStripLengthMeters;
            RepCount = repCount;
            CueSeconds = cueSeconds;
            WindowSeconds = windowSeconds;
            RestSeconds = restSeconds;
            CueType = cueType;
            SuccessRule = successRule;
            TargetZones = targetZones.ToArray();
        }

        /// <summary>Stable key for the rep log.</summary>
        public string Id { get; }

        /// <summary>What the Drill picker shows.</summary>
        public string Name { get; }

        /// <summary>Position in the Drill picker, easy to hard.</summary>
        public int Order { get; }

        /// <summary>Strip length the full Drill needs.</summary>
        public float RequiredStripLengthMeters { get; }

        /// <summary>
        /// Strip length the shortened variant needs when "shrink drills to fit" is on,
        /// or null if the Drill has no shortened variant.
        /// </summary>
        public float? ShrunkStripLengthMeters { get; }

        public int RepCount { get; }

        /// <summary>Time from the cue until the window opens. Zero opens the window with the cue.</summary>
        public float CueSeconds { get; }

        /// <summary>Time the Fencer has to Touch after the window opens.</summary>
        public float WindowSeconds { get; }

        /// <summary>Rest between one Rep's judgement and the next Rep's cue.</summary>
        public float RestSeconds { get; }

        public CueType CueType { get; }

        public SuccessRule SuccessRule { get; }

        /// <summary>The Target Zones a Rep may cue; each Rep picks one at random.</summary>
        public IReadOnlyList<TargetZone> TargetZones { get; }
    }
}
