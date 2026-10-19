// Builders for Session tests (PER-35): rep log entries, Drill run records, a hand-set wall clock
// and a scratch folder for the JSONL files.
using System;
using System.IO;
using System.Linq;
using VrFencing.Drills;
using VrFencing.Dummy;
using VrFencing.Session;

namespace VrFencing.Tests.Session
{
    internal sealed class FakeWallClock : IWallClock
    {
        public DateTimeOffset UtcNow { get; set; } = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        public void Advance(double seconds)
        {
            UtcNow = UtcNow.AddSeconds(seconds);
        }
    }

    internal sealed class ScratchFolder : IDisposable
    {
        public ScratchFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vrfencing-session-tests-" + System.IO.Path.GetRandomFileName());
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string File(string name)
        {
            return System.IO.Path.Combine(Path, name);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    internal static class SessionTestData
    {
        public const string DrillId = "lunge-to-target";
        public const string SessionId = "session-a";

        public static readonly DateTimeOffset Noon = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        public static RepLogEntry Touch(
            TargetZone zone,
            double reactionSeconds,
            float tipSpeed,
            string drillId = DrillId,
            bool isShrunk = false,
            int repIndex = 0)
        {
            return new RepLogEntry(
                Noon, SessionId, drillId, repIndex, zone, RepOutcome.Success, zone,
                reactionSeconds, tipSpeed, distanceFromZoneCenterMeters: 0.02f, isShrunk, flagged: false, armPose: null);
        }

        public static RepLogEntry Miss(
            TargetZone zone,
            RepOutcome outcome,
            double? reactionSeconds = null,
            float? tipSpeed = null,
            string drillId = DrillId,
            bool isShrunk = false,
            int repIndex = 0)
        {
            var contactZone = outcome == RepOutcome.Timeout ? (TargetZone?)null : TargetZone.Body;
            return new RepLogEntry(
                Noon, SessionId, drillId, repIndex, zone, outcome, contactZone,
                reactionSeconds, tipSpeed, distanceFromZoneCenterMeters: null, isShrunk, flagged: false, armPose: null);
        }

        public static RepResult Result(int repIndex, RepOutcome outcome, TargetZone zone = TargetZone.Thigh)
        {
            var isContact = outcome != RepOutcome.Timeout;
            return new RepResult(
                DrillId,
                repIndex,
                zone,
                outcome,
                isContact ? zone : (TargetZone?)null,
                isContact ? 0.4 : (double?)null,
                isContact ? 3.0f : (float?)null,
                isContact ? 0.02f : (float?)null,
                isShrunk: false);
        }

        /// <summary>A record whose Drill had <paramref name="successCount"/> Touches out of <paramref name="repCount"/> Reps on the thigh.</summary>
        public static DrillRunRecord Run(
            string sessionId,
            DateTimeOffset sessionStartedAt,
            int repCount = 10,
            int successCount = 5,
            double averageReactionSeconds = 0.5,
            string drillId = DrillId,
            bool isShrunk = false)
        {
            var reps = Enumerable.Range(0, repCount)
                .Select(index => index < successCount
                    ? Touch(TargetZone.Thigh, averageReactionSeconds, 3.0f, drillId, isShrunk, index)
                    : Miss(TargetZone.Thigh, RepOutcome.Timeout, drillId: drillId, isShrunk: isShrunk, repIndex: index))
                .ToArray();
            return new DrillRunRecord(sessionId, sessionStartedAt, sessionStartedAt.AddMinutes(5), wasCancelled: false, DrillResults.Aggregate(reps));
        }
    }
}
