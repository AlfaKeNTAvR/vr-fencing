using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace VrFencing.Session
{
    /// <summary>One finished Drill run, saved to the session history. A session is one app launch and can hold many runs.</summary>
    public sealed class DrillRunRecord
    {
        public const int CurrentSchema = 1;

        [JsonConstructor]
        public DrillRunRecord(
            string sessionId,
            DateTimeOffset sessionStartedAt,
            DateTimeOffset finishedAt,
            bool wasCancelled,
            DrillResults results,
            int schema = CurrentSchema)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new ArgumentException("A Drill run needs a session id.", nameof(sessionId));
            }

            Schema = schema;
            SessionId = sessionId;
            SessionStartedAt = sessionStartedAt;
            FinishedAt = finishedAt;
            WasCancelled = wasCancelled;
            Results = results ?? throw new ArgumentNullException(nameof(results));
        }

        public int Schema { get; }

        [JsonProperty(Required = Required.Always)]
        public string SessionId { get; }

        /// <summary>Wall-clock time (UTC) the session started; orders sessions in the trend.</summary>
        [JsonProperty(Required = Required.Always)]
        public DateTimeOffset SessionStartedAt { get; }

        /// <summary>Wall-clock time (UTC) the run ended.</summary>
        [JsonProperty(Required = Required.Always)]
        public DateTimeOffset FinishedAt { get; }

        /// <summary>The Fencer stopped the run early; <see cref="Results"/> covers the Reps judged before that.</summary>
        public bool WasCancelled { get; }

        [JsonProperty(Required = Required.Always)]
        public DrillResults Results { get; }
    }

    /// <summary>The session history file: one JSON line per finished Drill run (<see cref="DrillRunRecord"/>).</summary>
    public static class SessionHistoryFile
    {
        public const string FileName = "session-history.jsonl";

        public static void Append(string path, DrillRunRecord record)
        {
            JsonLinesFile.Append(path, record);
        }

        public static JsonLinesReadResult<DrillRunRecord> Read(string path)
        {
            return JsonLinesFile.Read<DrillRunRecord>(path);
        }
    }

    /// <summary>One session's results for a Drill, a point on the trend.</summary>
    public sealed class TrendPoint
    {
        public TrendPoint(string sessionId, DateTimeOffset sessionStartedAt, DrillResults results)
        {
            SessionId = sessionId;
            SessionStartedAt = sessionStartedAt;
            Results = results;
        }

        public string SessionId { get; }

        public DateTimeOffset SessionStartedAt { get; }

        /// <summary>Every run of the Drill in that session, combined.</summary>
        public DrillResults Results { get; }
    }

    /// <summary>The saved Drill runs, queried for the per-Drill trend the results panel shows.</summary>
    public sealed class SessionHistory
    {
        /// <summary>The trend covers the last 10 sessions in which the Drill was run (PER-27).</summary>
        public const int TrendSessionCount = 10;

        private readonly IReadOnlyList<DrillRunRecord> _records;

        public SessionHistory(IReadOnlyList<DrillRunRecord> records)
        {
            _records = records ?? throw new ArgumentNullException(nameof(records));
        }

        public IReadOnlyList<DrillRunRecord> Records => _records;

        /// <summary>
        /// The Drill's results per session, for the last <paramref name="sessionCount"/> sessions that ran it, oldest first.
        /// Full and shortened runs are kept apart (<paramref name="isShrunk"/>) because their numbers are not comparable.
        /// </summary>
        public IReadOnlyList<TrendPoint> GetTrend(string drillId, bool isShrunk, int sessionCount = TrendSessionCount)
        {
            if (sessionCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(sessionCount), sessionCount, "Must be at least one session.");
            }

            var sessions = _records
                .Where(record => record.Results.DrillId == drillId && record.Results.IsShrunk == isShrunk)
                .GroupBy(record => record.SessionId)
                .Select(session => new TrendPoint(
                    session.Key,
                    session.Min(record => record.SessionStartedAt),
                    DrillResults.Combine(session.Select(record => record.Results).ToArray())))
                .OrderBy(point => point.SessionStartedAt)
                .ToList();

            return sessions.Skip(Math.Max(0, sessions.Count - sessionCount)).ToList();
        }
    }
}
