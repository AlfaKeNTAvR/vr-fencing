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

        /// <summary>The Target Zone lights up and goes dark when the window closes (reaction, distance choice, compound).</summary>
        LitTargetZone,

        /// <summary>The app calls a footwork action ("Advance") above the Dummy; no zone is cued (footwork calls).</summary>
        FootworkCall,

        /// <summary>
        /// The Armed Dummy's attack is the cue (PER-100): nothing is named or lit, and the Riposte may land on any of the
        /// Drill's Target Zones (in épée the whole body is target).
        /// </summary>
        ArmedDummyAttack,

        /// <summary>
        /// The Armed Dummy holds its blade in line (PER-101, Beat then thrust): nothing is named or lit, and the Touch
        /// after the Beat may land on any of the Drill's Target Zones.
        /// </summary>
        BladeInLine,
    }

    /// <summary>How a Rep is judged.</summary>
    public enum SuccessRule
    {
        /// <summary>A point-first Touch on the cued Target Zone within the window. A wrong zone, a flat contact or a timeout fails.</summary>
        PointFirstTouchOnTargetZone,

        /// <summary>
        /// As <see cref="PointFirstTouchOnTargetZone"/>, and the footwork read up to the Touch must suit the Rep's Dummy
        /// Distance (<see cref="FootworkJudging.SuitsDistance"/>), else the Rep fails as wrong footwork (distance choice).
        /// </summary>
        TouchWithFootworkForDistance,

        /// <summary>
        /// Two point-first Touches in order: the Rep's first zone within the window, then its second zone within
        /// <see cref="DrillDefinition.SecondTouchWindowSeconds"/> of the first Touch (compound sequence).
        /// </summary>
        TwoTouchesInOrder,

        /// <summary>
        /// No blade: when the window ends, the footwork read since the cue must answer the called action
        /// (<see cref="FootworkJudging.AnswersCall"/>). Blade contacts are ignored (footwork calls).
        /// </summary>
        FootworkCall,

        /// <summary>
        /// The Armed Dummy attacks (<see cref="DrillDefinition.Attacks"/>) and the Fencer must Parry it
        /// (<see cref="DrillRunner.ReportParry"/>, PER-97): a contact that deflects its point off the body and passes the
        /// Parry rule. With <see cref="DrillDefinition.RequiresRiposte"/>, a point-first Touch on the cued zone (with the
        /// <see cref="CueType.ArmedDummyAttack"/> cue, any of the Drill's zones) must follow within
        /// <see cref="DrillDefinition.RiposteWindowSeconds"/> of the Parry. Contacts with the Dummy before the Parry are
        /// ignored; an attack that lands unparried fails the Rep.
        /// </summary>
        Parry,

        /// <summary>
        /// The Armed Dummy holds its blade in line (PER-101): the Fencer must Beat it out of line
        /// (<see cref="DrillRunner.ReportBeat"/>), then land a point-first Touch on any of the Drill's zones within
        /// <see cref="DrillDefinition.BeatTouchWindowSeconds"/>. A Touch before the Beat fails the Rep.
        /// </summary>
        BeatThenTouch,

        /// <summary>
        /// The Armed Dummy attacks (<see cref="DrillDefinition.Attacks"/>) and the Fencer must Touch its arm (the Drill's
        /// zones) while the attack is on the way, before its point lands (PER-101). The Dummy's point landing within
        /// <see cref="DrillDefinition.StopHitDoubleWindowSeconds"/> after the Stop Hit is a double, a fail. Contacts
        /// before the attack starts (<see cref="DrillRunner.ReportAttackUnderWay"/>) and Parries are ignored.
        /// </summary>
        StopHit,
    }

    /// <summary>Two Target Zones a compound Rep cues in order.</summary>
    public readonly struct ZonePair
    {
        public ZonePair(TargetZone first, TargetZone second)
        {
            if (first == second)
            {
                throw new ArgumentException("A compound needs two different Target Zones.", nameof(second));
            }

            First = first;
            Second = second;
        }

        public TargetZone First { get; }

        public TargetZone Second { get; }
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
            IReadOnlyList<TargetZone> targetZones,
            DummyDistance dummyDistance = DummyDistance.Lunge,
            DummyDistance? shrunkDummyDistance = null,
            IReadOnlyList<DummyDistance> repDummyDistances = null,
            IReadOnlyList<ZonePair> zonePairs = null,
            float secondTouchWindowSeconds = 0.0f,
            IReadOnlyList<FootworkAction> footworkCalls = null,
            string taskLine = null,
            bool lightsNamedZone = false,
            bool requiresRiposte = false,
            AttackScript attacks = null,
            IReadOnlyList<DummyDifficulty> dummyDifficulties = null,
            bool showsFootwork = false)
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

            if (successRule == SuccessRule.FootworkCall)
            {
                if (footworkCalls == null || footworkCalls.Count == 0)
                {
                    throw new ArgumentException("A footwork-call Drill needs at least one call.", nameof(footworkCalls));
                }

                if (cueType != CueType.FootworkCall)
                {
                    throw new ArgumentException("A footwork-call Drill cues footwork calls.", nameof(cueType));
                }
            }
            else if (successRule == SuccessRule.TwoTouchesInOrder)
            {
                if (zonePairs == null || zonePairs.Count == 0)
                {
                    throw new ArgumentException("A compound Drill needs at least one pair of Target Zones.", nameof(zonePairs));
                }

                if (!(secondTouchWindowSeconds > 0.0f))
                {
                    throw new ArgumentOutOfRangeException(nameof(secondTouchWindowSeconds), secondTouchWindowSeconds, "Must be positive.");
                }
            }
            else if (targetZones == null || targetZones.Count == 0)
            {
                throw new ArgumentException("A Drill needs at least one Target Zone to cue.", nameof(targetZones));
            }

            if (cueType == CueType.FootworkCall && successRule != SuccessRule.FootworkCall)
            {
                throw new ArgumentException("Footwork calls are judged by the footwork-call rule.", nameof(successRule));
            }

            if (requiresRiposte && successRule != SuccessRule.Parry)
            {
                throw new ArgumentException("Only a Parry Drill can require a Riposte.", nameof(requiresRiposte));
            }

            var isAttackRule = successRule == SuccessRule.Parry || successRule == SuccessRule.StopHit;
            if (attacks != null && !isAttackRule)
            {
                throw new ArgumentException("Only a Parry or Stop Hit Drill has an attack script.", nameof(attacks));
            }

            if (successRule == SuccessRule.StopHit && attacks == null)
            {
                throw new ArgumentException("A Stop Hit Drill needs an attack script.", nameof(attacks));
            }

            if (cueType == CueType.ArmedDummyAttack && !isAttackRule)
            {
                throw new ArgumentException("Only a Parry or Stop Hit Drill is cued by the Armed Dummy's attack.", nameof(cueType));
            }

            if ((cueType == CueType.BladeInLine) != (successRule == SuccessRule.BeatThenTouch))
            {
                throw new ArgumentException("A Beat Drill, and only a Beat Drill, is cued by the blade held in line.", nameof(cueType));
            }

            if (lightsNamedZone && cueType != CueType.NamedTargetZone)
            {
                throw new ArgumentException("Only a Drill that names its Target Zone can also light it.", nameof(lightsNamedZone));
            }

            if (shrunkDummyDistance.HasValue && !shrunkStripLengthMeters.HasValue)
            {
                throw new ArgumentException(
                    "Only a Drill with a shortened variant can set the shortened variant's Dummy Distance.", nameof(shrunkDummyDistance));
            }

            if (repDummyDistances != null && (repDummyDistances.Count == 0 || shrunkStripLengthMeters.HasValue))
            {
                throw new ArgumentException(
                    "Per-Rep Dummy Distances need at least one distance, and a Drill that has them has no shortened variant.",
                    nameof(repDummyDistances));
            }

            if (dummyDifficulties != null)
            {
                if (dummyDifficulties.Count == 0)
                {
                    throw new ArgumentException("A Drill whose Dummy keeps its distance needs at least one difficulty.", nameof(dummyDifficulties));
                }

                // The Armed Dummy plans its thrusts from where it stands, and distance choice moves it per Rep: neither
                // follows the Fencer yet (PER-44 stage 2 merges the keeper with the Armed Dummy).
                if (successRule == SuccessRule.Parry || successRule == SuccessRule.StopHit || successRule == SuccessRule.BeatThenTouch
                    || repDummyDistances != null)
                {
                    throw new ArgumentException(
                        "Only a Drill whose Dummy is unarmed and stands at one distance can keep that distance.", nameof(dummyDifficulties));
                }
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
            TargetZones = targetZones?.ToArray() ?? Array.Empty<TargetZone>();
            DummyDistance = dummyDistance;
            ShrunkDummyDistance = shrunkDummyDistance ?? dummyDistance;
            RepDummyDistances = repDummyDistances?.ToArray();
            ZonePairs = zonePairs?.ToArray() ?? Array.Empty<ZonePair>();
            SecondTouchWindowSeconds = secondTouchWindowSeconds;
            FootworkCalls = footworkCalls?.ToArray() ?? Array.Empty<FootworkAction>();
            TaskLine = taskLine ?? string.Empty;
            LightsNamedZone = lightsNamedZone;
            RequiresRiposte = requiresRiposte;
            Attacks = isAttackRule ? attacks ?? AttackScript.EveryTargetAndSpeed : null;
            DummyDifficulties = dummyDifficulties?.ToArray();
            ShowsFootwork = showsFootwork;
        }

        /// <summary>
        /// The Dummy runs through every footwork move it has, over and over, for watching them (PER-111, the footwork
        /// feel test): advances, retreats, a lunge and recovery, a fleche, and breathing in between.
        /// </summary>
        public bool ShowsFootwork { get; }

        /// <summary>
        /// When set, the Dummy keeps its Dummy Distance as the Fencer advances and retreats (PER-105, the moving Dummy's
        /// distance keeper), reacting at these difficulties in turn by Rep, then again from the first. Null when the Dummy
        /// stands still, as in every Armed Dummy Drill.
        /// </summary>
        public IReadOnlyList<DummyDifficulty> DummyDifficulties { get; }

        /// <summary>The Dummy keeps its distance from the Fencer in this Drill (<see cref="DummyDifficulties"/>).</summary>
        public bool KeepsDistance => DummyDifficulties != null;

        /// <summary>The keeper's difficulty for the Rep at <paramref name="repIndex"/> (zero-based); null when the Dummy stands still.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The index is negative.</exception>
        public DummyDifficulty? GetDummyDifficulty(int repIndex)
        {
            if (repIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(repIndex), repIndex, "Must not be negative.");
            }

            return DummyDifficulties?[repIndex % DummyDifficulties.Count];
        }

        /// <summary>A Riposte must land within this of the Parry (PER-23: 1 s).</summary>
        public const float RiposteWindowSeconds = 1.0f;

        /// <summary>The Touch must land within this of the Beat (PER-23: 1 s).</summary>
        public const float BeatTouchWindowSeconds = 1.0f;

        /// <summary>
        /// The Armed Dummy's point landing this soon after a Stop Hit makes a double (PER-23: the 40 to 50 ms double
        /// window; the épée rule's 50 ms, as in a Bout).
        /// </summary>
        public const float StopHitDoubleWindowSeconds = 0.05f;

        /// <summary>
        /// A Parry Drill also needs a Riposte after the Parry (PER-23: per Drill, so a parry-only warm-up turns it off).
        /// Always false for other rules.
        /// </summary>
        public bool RequiresRiposte { get; }

        /// <summary>The Armed Dummy holds a blade and attacks in this Drill (<see cref="SuccessRule.Parry"/>).</summary>
        public bool UsesArmedDummy =>
            SuccessRule == SuccessRule.Parry || SuccessRule == SuccessRule.StopHit || SuccessRule == SuccessRule.BeatThenTouch;

        /// <summary>
        /// The Touch may land on any of <see cref="TargetZones"/>, none cued: the Armed Dummy's attack or its blade in
        /// line is the cue.
        /// </summary>
        public bool TouchesAnyDrillZone => CueType == CueType.ArmedDummyAttack || CueType == CueType.BladeInLine;

        /// <summary>
        /// The Armed Dummy's targets and speeds (PER-100); <see cref="AttackScript.EveryTargetAndSpeed"/> for a Parry Drill
        /// built without one, null when the Drill does not use the Armed Dummy.
        /// </summary>
        public AttackScript Attacks { get; }

        /// <summary>
        /// One line saying what the Fencer does (PER-92), e.g. "Extend and Touch the named zone. Arm only, no step.". The
        /// picker shows it under the Drill's name, and it stands above the Dummy for a few seconds before the first Rep.
        /// </summary>
        public string TaskLine { get; }

        /// <summary>A named-zone Drill also lights the named zone (PER-92, the "Light named zones" Setting).</summary>
        public bool LightsNamedZone { get; }

        /// <summary>
        /// Each Rep lights its cued Target Zone on the Dummy: a lit-zone Drill, or a named-zone Drill that also lights it.
        /// A footwork call cues no zone, so nothing lights.
        /// </summary>
        public bool LightsCuedZone => CueType == CueType.LitTargetZone || LightsNamedZone;

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

        /// <summary>
        /// The Target Zones a Rep may cue; each Rep picks one at random. With the <see cref="CueType.ArmedDummyAttack"/>
        /// cue, the zones a Riposte may land on, none cued. Empty for footwork calls and compounds.
        /// </summary>
        public IReadOnlyList<TargetZone> TargetZones { get; }

        /// <summary>The ordered zone pairs a compound Rep may cue; each Rep picks one at random. Empty for other Drills.</summary>
        public IReadOnlyList<ZonePair> ZonePairs { get; }

        /// <summary>Compound only: time from the first Touch for the second to land.</summary>
        public float SecondTouchWindowSeconds { get; }

        /// <summary>The actions a footwork-call Rep may call. Empty for other Drills.</summary>
        public IReadOnlyList<FootworkAction> FootworkCalls { get; }

        /// <summary>
        /// When set, each Rep stands the Dummy at one of these, picked at random (distance choice); the Unity layer moves
        /// the Dummy on each cue. Null when the Dummy stays at <see cref="DummyDistance"/> for the whole run.
        /// </summary>
        public IReadOnlyList<DummyDistance> RepDummyDistances { get; }

        /// <summary>The Rep's footwork is read from the head and reported to the runner (distance choice, footwork calls).</summary>
        public bool UsesFootwork => SuccessRule == SuccessRule.TouchWithFootworkForDistance || SuccessRule == SuccessRule.FootworkCall;

        /// <summary>Where the Dummy stands for the full Drill. The Unity layer moves the Dummy there when a run starts.</summary>
        public DummyDistance DummyDistance { get; }

        /// <summary>Where the Dummy stands for the shortened variant; the same as <see cref="DummyDistance"/> unless the variant moves it nearer.</summary>
        public DummyDistance ShrunkDummyDistance { get; }

        /// <summary>
        /// Whether the Fencer moves along the Strip in a run of the full Drill or, with <paramref name="isShrunk"/>, its
        /// shortened variant: the Dummy stands beyond extension distance, or the Drill reads footwork. The runner then waits
        /// for the Fencer back at the Start Mark between Reps (PER-89: Drills 2 to 6). A run at extension distance with no
        /// footwork (Drill 1, a shortened variant) needs no wait.
        /// </summary>
        public bool WaitsAtStartMark(bool isShrunk)
        {
            return UsesFootwork || RepDummyDistances != null || GetDummyDistance(isShrunk) != DummyDistance.Extension;
        }

        /// <summary>The Dummy Distance for a run of the full Drill or, with <paramref name="isShrunk"/>, its shortened variant.</summary>
        public DummyDistance GetDummyDistance(bool isShrunk)
        {
            return isShrunk ? ShrunkDummyDistance : DummyDistance;
        }
    }
}
