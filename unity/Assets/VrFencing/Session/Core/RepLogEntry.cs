using System;
using Newtonsoft.Json;
using VrFencing.Drills;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Session
{
    /// <summary>
    /// One line of the rep log: a judged Rep plus what only the Session knows (wall-clock time, session, flag button,
    /// arm pose). Serialized by <see cref="RepLogFile"/> with camelCase field names, enums as names and every field
    /// written (null when empty), so an agent can read the log with jq after pulling it off the headset.
    /// </summary>
    public sealed class RepLogEntry
    {
        /// <summary>Bump when a field changes meaning or is removed. Adding a field keeps the version.</summary>
        public const int CurrentSchema = 1;

        [JsonConstructor]
        public RepLogEntry(
            DateTimeOffset timestamp,
            string sessionId,
            string drillId,
            int repIndex,
            TargetZone? targetZone,
            RepOutcome result,
            TargetZone? contactZone,
            double? reactionSeconds,
            float? tipSpeedMetersPerSecond,
            float? distanceFromZoneCenterMeters,
            bool isShrunk,
            bool flagged,
            ArmPoseSnapshot armPose,
            int schema = CurrentSchema,
            TargetZone? secondTargetZone = null,
            double? secondTouchSeconds = null,
            FootworkAction? footworkCall = null,
            FootworkLog footwork = null,
            DummyDistance? dummyDistance = null,
            ParryLine? parryLine = null,
            double? riposteSeconds = null,
            FencerHitSource? hitSource = null,
            BodyPart? hitPart = null)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new ArgumentException("A logged Rep needs a session id.", nameof(sessionId));
            }

            if (string.IsNullOrWhiteSpace(drillId))
            {
                throw new ArgumentException("A logged Rep needs a Drill id.", nameof(drillId));
            }

            if (repIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(repIndex), repIndex, "Must not be negative.");
            }

            Schema = schema;
            Timestamp = timestamp;
            SessionId = sessionId;
            DrillId = drillId;
            RepIndex = repIndex;
            TargetZone = targetZone;
            Result = result;
            ContactZone = contactZone;
            ReactionSeconds = reactionSeconds;
            TipSpeedMetersPerSecond = tipSpeedMetersPerSecond;
            DistanceFromZoneCenterMeters = distanceFromZoneCenterMeters;
            IsShrunk = isShrunk;
            Flagged = flagged;
            ArmPose = armPose;
            SecondTargetZone = secondTargetZone;
            SecondTouchSeconds = secondTouchSeconds;
            FootworkCall = footworkCall;
            Footwork = footwork;
            DummyDistance = dummyDistance;
            ParryLine = parryLine;
            RiposteSeconds = riposteSeconds;
            HitSource = hitSource;
            HitPart = hitPart;
        }

        public int Schema { get; }

        /// <summary>Wall-clock time (UTC) when the Rep was judged.</summary>
        [JsonProperty(Required = Required.Always)]
        public DateTimeOffset Timestamp { get; }

        /// <summary>Joins the rep log with the session history.</summary>
        [JsonProperty(Required = Required.Always)]
        public string SessionId { get; }

        [JsonProperty(Required = Required.Always)]
        public string DrillId { get; }

        /// <summary>Zero-based, within one Drill run.</summary>
        [JsonProperty(Required = Required.Always)]
        public int RepIndex { get; }

        /// <summary>The Target Zone the Rep cued (a compound's first); null for a footwork call, which cues no zone.</summary>
        [JsonProperty(Required = Required.AllowNull)]
        public TargetZone? TargetZone { get; }

        [JsonProperty(Required = Required.Always)]
        public RepOutcome Result { get; }

        [JsonIgnore]
        public bool IsSuccess => Result == RepOutcome.Success;

        /// <summary>The zone the blade met, or null on a timeout.</summary>
        public TargetZone? ContactZone { get; }

        /// <summary>From the window opening to the contact, paused time excluded; null on a timeout.</summary>
        public double? ReactionSeconds { get; }

        public float? TipSpeedMetersPerSecond { get; }

        public float? DistanceFromZoneCenterMeters { get; }

        /// <summary>The shrink tag: the Rep ran in the shortened variant of the Drill.</summary>
        public bool IsShrunk { get; }

        /// <summary>The Fencer pressed the flag button for this Rep (for example, a Touch that was judged wrongly).</summary>
        public bool Flagged { get; }

        /// <summary>IOBT arm pose at judging time, or null when body tracking was not available.</summary>
        public ArmPoseSnapshot ArmPose { get; }

        /// <summary>Compound only: the second cued zone. Null for other Drills and in lines written before it existed.</summary>
        public TargetZone? SecondTargetZone { get; }

        /// <summary>Compound only: from the first Touch to the second contact; null when no second contact came.</summary>
        public double? SecondTouchSeconds { get; }

        /// <summary>Footwork calls only: the called action.</summary>
        public FootworkAction? FootworkCall { get; }

        /// <summary>The footwork read from the head (distance choice, footwork calls); null for other Drills or when not read.</summary>
        public FootworkLog Footwork { get; }

        /// <summary>Where the Dummy stood for the Rep. Null in lines written before it existed.</summary>
        public DummyDistance? DummyDistance { get; }

        /// <summary>Parry Drills: the line of the contact that deflected the attack; null otherwise and in older lines.</summary>
        public ParryLine? ParryLine { get; }

        /// <summary>Parry Drills: from the Parry to the Riposte's contact; null without both.</summary>
        public double? RiposteSeconds { get; }

        /// <summary>
        /// Parry Drills: which body judged the Armed Dummy's point landing on the Fencer (IOBT or the headset capsules,
        /// PER-98); null when it did not land, and in older lines.
        /// </summary>
        public FencerHitSource? HitSource { get; }

        /// <summary>Parry Drills: the part of the Fencer the Armed Dummy's point landed on; null when it did not land.</summary>
        public BodyPart? HitPart { get; }

        public static RepLogEntry FromRepResult(RepResult result, DateTimeOffset timestamp, string sessionId, bool flagged, ArmPoseSnapshot armPose)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            return new RepLogEntry(
                timestamp,
                sessionId,
                result.DrillId,
                result.RepIndex,
                result.RequestedZone,
                result.Outcome,
                result.ContactZone,
                result.ReactionSeconds,
                result.TipSpeedMetersPerSecond,
                result.DistanceFromZoneCenterMeters,
                result.IsShrunk,
                flagged,
                armPose,
                CurrentSchema,
                result.RequestedSecondZone,
                result.SecondTouchSeconds,
                result.RequestedFootwork,
                FootworkLog.FromReading(result.Footwork),
                result.DummyDistance,
                result.ParryLine,
                result.RiposteSeconds,
                result.FencerHit?.Source,
                result.FencerHit?.Part);
        }

        public RepLogEntry WithFlagged(bool flagged)
        {
            return new RepLogEntry(
                Timestamp,
                SessionId,
                DrillId,
                RepIndex,
                TargetZone,
                Result,
                ContactZone,
                ReactionSeconds,
                TipSpeedMetersPerSecond,
                DistanceFromZoneCenterMeters,
                IsShrunk,
                flagged,
                ArmPose,
                Schema,
                SecondTargetZone,
                SecondTouchSeconds,
                FootworkCall,
                Footwork,
                DummyDistance,
                ParryLine,
                RiposteSeconds,
                HitSource,
                HitPart);
        }
    }

    /// <summary>
    /// The rep log's record of the footwork read from the head (the Tracking module's footwork classifier): the action
    /// it read and the measurements it read it from, so its thresholds can be tuned from real Reps.
    /// </summary>
    public sealed class FootworkLog
    {
        [JsonConstructor]
        public FootworkLog(
            FootworkAction action,
            double? onsetSeconds,
            float forwardTravelMeters,
            float backwardTravelMeters,
            float peakForwardSpeedMetersPerSecond,
            float headDropMeters)
        {
            Action = action;
            OnsetSeconds = onsetSeconds;
            ForwardTravelMeters = forwardTravelMeters;
            BackwardTravelMeters = backwardTravelMeters;
            PeakForwardSpeedMetersPerSecond = peakForwardSpeedMetersPerSecond;
            HeadDropMeters = headDropMeters;
        }

        public FootworkAction Action { get; }

        /// <summary>From the cue to the head first moving off its place; null if it never did.</summary>
        public double? OnsetSeconds { get; }

        /// <summary>Farthest the head got toward the Dummy from where it was at the cue.</summary>
        public float ForwardTravelMeters { get; }

        /// <summary>Farthest the head got away from the Dummy from where it was at the cue.</summary>
        public float BackwardTravelMeters { get; }

        public float PeakForwardSpeedMetersPerSecond { get; }

        /// <summary>How far the head went down from its height at the cue.</summary>
        public float HeadDropMeters { get; }

        /// <summary>The log record of a reading, or null without one.</summary>
        public static FootworkLog FromReading(FootworkReading? reading)
        {
            if (!reading.HasValue)
            {
                return null;
            }

            var value = reading.Value;
            return new FootworkLog(
                value.Action,
                value.OnsetSecondsAfterCue,
                value.ForwardTravelMeters,
                value.BackwardTravelMeters,
                value.PeakForwardSpeedMetersPerSecond,
                value.HeadDropMeters);
        }
    }

    /// <summary>
    /// The blade arm's joints from IOBT body tracking, in the Strip frame (+x the Fencer's right, +y up, +z forward).
    /// Any joint the tracker did not report is null. Filled by the Tracking module; logged only, never used for judging.
    /// <para>
    /// The lunge fields come from the Rep's samples between its cue and its judging (see the Tracking module's
    /// <c>RepArmTracker</c>). They are null when no lunge was found or the arm was not tracked when it started.
    /// Lines written before these fields existed read back with them null.
    /// </para>
    /// </summary>
    public sealed class ArmPoseSnapshot
    {
        [JsonConstructor]
        public ArmPoseSnapshot(
            TrackedPose? shoulder,
            TrackedPose? elbow,
            TrackedPose? wrist,
            BodyTrackingFidelity? fidelity = null,
            bool? armExtendedBeforeLunge = null,
            float? elbowAngleAtLungeStartDegrees = null,
            double? lungeStartSecondsBeforeJudging = null)
        {
            Shoulder = shoulder;
            Elbow = elbow;
            Wrist = wrist;
            Fidelity = fidelity;
            ArmExtendedBeforeLunge = armExtendedBeforeLunge;
            ElbowAngleAtLungeStartDegrees = elbowAngleAtLungeStartDegrees;
            LungeStartSecondsBeforeJudging = lungeStartSecondsBeforeJudging;
        }

        /// <summary>Shoulder joint (top of the upper arm) at judging time.</summary>
        public TrackedPose? Shoulder { get; }

        public TrackedPose? Elbow { get; }

        public TrackedPose? Wrist { get; }

        /// <summary>What the body tracker delivered at judging time: High is IOBT, Low the fallback from head and controllers only.</summary>
        public BodyTrackingFidelity? Fidelity { get; }

        /// <summary>The blade arm was extended (straight and reaching forward) when the lunge started.</summary>
        public bool? ArmExtendedBeforeLunge { get; }

        /// <summary>Elbow angle when the lunge started: 180 is a straight arm.</summary>
        public float? ElbowAngleAtLungeStartDegrees { get; }

        /// <summary>How long before the Rep was judged the lunge started.</summary>
        public double? LungeStartSecondsBeforeJudging { get; }
    }

    /// <summary>Body tracking quality as the headset reports it. Low is the quiet fallback when IOBT is not available.</summary>
    public enum BodyTrackingFidelity
    {
        Low,
        High,
    }
}
