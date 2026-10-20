using System.Collections.Generic;
using VrFencing.Dummy;

namespace VrFencing.Drills
{
    /// <summary>
    /// Where a Drill run is. One Rep goes Cue, Window, Judge, then Rest (or Done after the last Rep). In a Drill where
    /// the Fencer moves, Return comes between Judge and Rest until the Fencer is back at the Start Mark.
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

        /// <summary>
        /// Waiting, with no time limit, for the Fencer to be back at the Start Mark and still before the rest starts.
        /// Skipped when the Fencer is already there at the judgement.
        /// </summary>
        Return,

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

        /// <summary>
        /// The footwork did not fit: at the Touch it did not suit the Dummy Distance (distance choice), or in the window
        /// it was another action than the one called (footwork calls).
        /// </summary>
        WrongFootwork,

        /// <summary>
        /// Parry Drills: the Armed Dummy's attack got through without a Parry: it landed on the Fencer
        /// (<see cref="RepResult.FencerHit"/>), or ended its hold without reaching them.
        /// </summary>
        NotParried,

        /// <summary>
        /// Parry Drills with the strict Parry rule: a contact deflected the attack, but not with the Fencer's forte on the
        /// Dummy's foible.
        /// </summary>
        NotForteOnFoible,

        /// <summary>Parry Drills that require a Riposte: no Touch within the Riposte window after the Parry.</summary>
        NoRiposte,
    }

    /// <summary>A Rep's cue, for the Dummy (light or name the zone, call the footwork, stand at its distance) and the UI.</summary>
    public readonly struct RepCue
    {
        public RepCue(
            int repIndex,
            TargetZone? targetZone,
            CueType cueType,
            DummyDistance dummyDistance = DummyDistance.Lunge,
            TargetZone? secondTargetZone = null,
            FootworkAction? footworkCall = null)
        {
            RepIndex = repIndex;
            TargetZone = targetZone;
            CueType = cueType;
            DummyDistance = dummyDistance;
            SecondTargetZone = secondTargetZone;
            FootworkCall = footworkCall;
        }

        /// <summary>Zero-based.</summary>
        public int RepIndex { get; }

        /// <summary>The zone to Touch (a compound's first one); null for a footwork call.</summary>
        public TargetZone? TargetZone { get; }

        public CueType CueType { get; }

        /// <summary>Where the Dummy stands for this Rep. It differs from Rep to Rep only in distance choice.</summary>
        public DummyDistance DummyDistance { get; }

        /// <summary>A compound's second zone, Touched after the first; null for other Drills.</summary>
        public TargetZone? SecondTargetZone { get; }

        /// <summary>The called footwork action; null for Drills with the blade.</summary>
        public FootworkAction? FootworkCall { get; }
    }

    /// <summary>One judged Rep, as plain data for the rep log (Session).</summary>
    public sealed class RepResult
    {
        public RepResult(
            string drillId,
            int repIndex,
            TargetZone? requestedZone,
            RepOutcome outcome,
            TargetZone? contactZone,
            double? reactionSeconds,
            float? tipSpeedMetersPerSecond,
            float? distanceFromZoneCenterMeters,
            bool isShrunk,
            TargetZone? requestedSecondZone = null,
            double? secondTouchSeconds = null,
            FootworkAction? requestedFootwork = null,
            FootworkReading? footwork = null,
            DummyDistance? dummyDistance = null,
            ParryLine? parryLine = null,
            double? riposteSeconds = null,
            FencerHit? fencerHit = null)
        {
            DrillId = drillId;
            ParryLine = parryLine;
            RiposteSeconds = riposteSeconds;
            FencerHit = fencerHit;
            RepIndex = repIndex;
            RequestedZone = requestedZone;
            Outcome = outcome;
            ContactZone = contactZone;
            ReactionSeconds = reactionSeconds;
            TipSpeedMetersPerSecond = tipSpeedMetersPerSecond;
            DistanceFromZoneCenterMeters = distanceFromZoneCenterMeters;
            IsShrunk = isShrunk;
            RequestedSecondZone = requestedSecondZone;
            SecondTouchSeconds = secondTouchSeconds;
            RequestedFootwork = requestedFootwork;
            Footwork = footwork;
            DummyDistance = dummyDistance;
        }

        public string DrillId { get; }

        /// <summary>Zero-based.</summary>
        public int RepIndex { get; }

        /// <summary>The cued zone (a compound's first); null for a footwork call.</summary>
        public TargetZone? RequestedZone { get; }

        public RepOutcome Outcome { get; }

        public bool IsSuccess => Outcome == RepOutcome.Success;

        /// <summary>The zone of the contact that decided the Rep (a compound's second contact once the first landed), or null with no contact.</summary>
        public TargetZone? ContactZone { get; }

        /// <summary>
        /// Time from the window opening to the contact (a compound's first Touch; a footwork call's first head movement),
        /// or null when nothing came. Paused time is not counted.
        /// </summary>
        public double? ReactionSeconds { get; }

        /// <summary>Of the contact that decided the Rep; null without one.</summary>
        public float? TipSpeedMetersPerSecond { get; }

        public float? DistanceFromZoneCenterMeters { get; }

        /// <summary>The Rep ran in the shortened variant of the Drill, so its stats are not comparable with full Reps.</summary>
        public bool IsShrunk { get; }

        /// <summary>Compound only: the second cued zone.</summary>
        public TargetZone? RequestedSecondZone { get; }

        /// <summary>Compound only: from the first Touch to the second contact; null when no second contact came.</summary>
        public double? SecondTouchSeconds { get; }

        /// <summary>Footwork calls only: the called action.</summary>
        public FootworkAction? RequestedFootwork { get; }

        /// <summary>The footwork read up to the judging (distance choice, footwork calls); null when not read.</summary>
        public FootworkReading? Footwork { get; }

        /// <summary>Where the Dummy stood for this Rep.</summary>
        public DummyDistance? DummyDistance { get; }

        /// <summary>Parry Drills: the line of the contact that deflected the attack (a Parry, or one strict did not count); else null.</summary>
        public ParryLine? ParryLine { get; }

        /// <summary>Parry Drills: from the Parry to the Riposte's contact; null without both.</summary>
        public double? RiposteSeconds { get; }

        /// <summary>
        /// Parry Drills: where the Armed Dummy's unparried point landed on the Fencer and which body judged it; null when it
        /// did not land.
        /// </summary>
        public FencerHit? FencerHit { get; }
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
