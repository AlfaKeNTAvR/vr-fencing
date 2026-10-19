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
            TargetZone targetZone,
            RepOutcome result,
            TargetZone? contactZone,
            double? reactionSeconds,
            float? tipSpeedMetersPerSecond,
            float? distanceFromZoneCenterMeters,
            bool isShrunk,
            bool flagged,
            ArmPoseSnapshot armPose,
            int schema = CurrentSchema)
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

        /// <summary>The Target Zone the Rep cued.</summary>
        [JsonProperty(Required = Required.Always)]
        public TargetZone TargetZone { get; }

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
                armPose);
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
                Schema);
        }
    }

    /// <summary>
    /// The blade arm's joints from IOBT body tracking, in the Strip frame (+x the Fencer's right, +y up, +z forward).
    /// Any joint the tracker did not report is null. Filled by the Tracking module; logged only, never used for judging.
    /// </summary>
    public sealed class ArmPoseSnapshot
    {
        [JsonConstructor]
        public ArmPoseSnapshot(TrackedPose? shoulder, TrackedPose? elbow, TrackedPose? wrist)
        {
            Shoulder = shoulder;
            Elbow = elbow;
            Wrist = wrist;
        }

        public TrackedPose? Shoulder { get; }

        public TrackedPose? Elbow { get; }

        public TrackedPose? Wrist { get; }
    }
}
