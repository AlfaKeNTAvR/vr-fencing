using System.Collections.Generic;
using VrFencing.Dummy;

namespace VrFencing.Drills
{
    /// <summary>
    /// Where a Drill run is. One Rep goes Cue, Window, Judge, then Rest (or Done after the last Rep).
    /// Pausing is a flag on the runner, not a state: the state is kept so the run knows what to resume.
    /// </summary>
    public enum DrillState
    {
        Idle,

        /// <summary>The Target Zone is announced; contacts are ignored.</summary>
        Cue,

        /// <summary>The Fencer may Touch; the first contact or the window's end decides the Rep.</summary>
        Window,

        /// <summary>The Rep is being judged. Passed through in one step, so it is only seen in <see cref="DrillRunner.StateChanged"/>.</summary>
        Judge,

        Rest,

        /// <summary>All Reps judged, or the run was cancelled.</summary>
        Done,
    }

    public enum RepOutcome
    {
        Success,
        WrongZone,
        FlatContact,
        Timeout,
    }

    /// <summary>A Rep's cue, for the Dummy (light or name the zone) and the UI.</summary>
    public readonly struct RepCue
    {
        public RepCue(int repIndex, TargetZone targetZone, CueType cueType)
        {
            RepIndex = repIndex;
            TargetZone = targetZone;
            CueType = cueType;
        }

        /// <summary>Zero-based.</summary>
        public int RepIndex { get; }

        public TargetZone TargetZone { get; }

        public CueType CueType { get; }
    }

    /// <summary>One judged Rep, as plain data for the rep log (Session).</summary>
    public sealed class RepResult
    {
        public RepResult(
            string drillId,
            int repIndex,
            TargetZone requestedZone,
            RepOutcome outcome,
            TargetZone? contactZone,
            double? reactionSeconds,
            float? tipSpeedMetersPerSecond,
            float? distanceFromZoneCenterMeters,
            bool isShrunk)
        {
            DrillId = drillId;
            RepIndex = repIndex;
            RequestedZone = requestedZone;
            Outcome = outcome;
            ContactZone = contactZone;
            ReactionSeconds = reactionSeconds;
            TipSpeedMetersPerSecond = tipSpeedMetersPerSecond;
            DistanceFromZoneCenterMeters = distanceFromZoneCenterMeters;
            IsShrunk = isShrunk;
        }

        public string DrillId { get; }

        /// <summary>Zero-based.</summary>
        public int RepIndex { get; }

        public TargetZone RequestedZone { get; }

        public RepOutcome Outcome { get; }

        public bool IsSuccess => Outcome == RepOutcome.Success;

        /// <summary>The zone the blade met, or null on a timeout.</summary>
        public TargetZone? ContactZone { get; }

        /// <summary>Time from the window opening to the contact, or null on a timeout. Paused time is not counted.</summary>
        public double? ReactionSeconds { get; }

        public float? TipSpeedMetersPerSecond { get; }

        public float? DistanceFromZoneCenterMeters { get; }

        /// <summary>The Rep ran in the shortened variant of the Drill, so its stats are not comparable with full Reps.</summary>
        public bool IsShrunk { get; }
    }

    /// <summary>How a run ended: every judged Rep in order, and whether the Fencer cancelled.</summary>
    public sealed class DrillRunSummary
    {
        public DrillRunSummary(DrillDefinition drill, IReadOnlyList<RepResult> results, bool wasCancelled, bool isShrunk)
        {
            Drill = drill;
            Results = results;
            WasCancelled = wasCancelled;
            IsShrunk = isShrunk;
        }

        public DrillDefinition Drill { get; }

        public IReadOnlyList<RepResult> Results { get; }

        /// <summary>The run was cancelled before every Rep was judged; <see cref="Results"/> holds the Reps judged so far.</summary>
        public bool WasCancelled { get; }

        public bool IsShrunk { get; }
    }
}
