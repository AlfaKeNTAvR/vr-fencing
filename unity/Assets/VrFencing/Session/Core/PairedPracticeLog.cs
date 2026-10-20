using System;
using Newtonsoft.Json;

namespace VrFencing.Session
{
    /// <summary>
    /// One Fencer's Paired Practice tally (PER-72): their Touches that lit alone, the doubles (the same for both), and
    /// their glances. Never negative.
    /// </summary>
    public readonly struct PairedPracticeCounts : IEquatable<PairedPracticeCounts>
    {
        [JsonConstructor]
        public PairedPracticeCounts(int touches, int doubles, int glances)
        {
            if (touches < 0 || doubles < 0 || glances < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(touches), "A tally never counts below zero.");
            }

            Touches = touches;
            Doubles = doubles;
            Glances = glances;
        }

        public int Touches { get; }

        public int Doubles { get; }

        public int Glances { get; }

        [JsonIgnore]
        public bool IsEmpty => Touches == 0 && Doubles == 0 && Glances == 0;

        public bool Equals(PairedPracticeCounts other)
        {
            return Touches == other.Touches && Doubles == other.Doubles && Glances == other.Glances;
        }

        public override bool Equals(object obj)
        {
            return obj is PairedPracticeCounts other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (Touches * 397) ^ (Doubles * 31) ^ Glances;
        }
    }

    /// <summary>Why a Paired Practice tally was saved.</summary>
    public enum PairedPracticeEnd
    {
        /// <summary>A Fencer reset the tally.</summary>
        Reset,

        /// <summary>The Host switched to a Bout.</summary>
        SwitchedToBout,

        /// <summary>The session ended or the partner left.</summary>
        Disconnected,
    }

    /// <summary>One Paired Practice tally as this headset saw it, saved to the Session log when it ends (PER-83).</summary>
    public sealed class PairedPracticeRecord
    {
        public const int CurrentSchema = 1;

        [JsonConstructor]
        public PairedPracticeRecord(
            string sessionId,
            DateTimeOffset savedAt,
            string role,
            PairedPracticeEnd end,
            PairedPracticeCounts you,
            PairedPracticeCounts opponent,
            int schema = CurrentSchema)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new ArgumentException("A Paired Practice record needs a session id.", nameof(sessionId));
            }

            Schema = schema;
            SessionId = sessionId;
            SavedAt = savedAt;
            Role = role ?? string.Empty;
            End = end;
            You = you;
            Opponent = opponent;
        }

        public int Schema { get; }

        [JsonProperty(Required = Required.Always)]
        public string SessionId { get; }

        /// <summary>Wall-clock time (UTC) the tally ended and was saved.</summary>
        [JsonProperty(Required = Required.Always)]
        public DateTimeOffset SavedAt { get; }

        /// <summary>This headset's side: "Host" or "Partner".</summary>
        public string Role { get; }

        public PairedPracticeEnd End { get; }

        /// <summary>This Fencer's tally.</summary>
        public PairedPracticeCounts You { get; }

        /// <summary>The Opponent's tally, as this headset last knew it.</summary>
        public PairedPracticeCounts Opponent { get; }
    }

    /// <summary>The Paired Practice log, beside the session history: one JSON line per saved tally (<see cref="PairedPracticeRecord"/>).</summary>
    public static class PairedPracticeLogFile
    {
        public const string FileName = "session-paired-practice.jsonl";

        public static void Append(string path, PairedPracticeRecord record)
        {
            JsonLinesFile.Append(path, record);
        }

        public static JsonLinesReadResult<PairedPracticeRecord> Read(string path)
        {
            return JsonLinesFile.Read<PairedPracticeRecord>(path);
        }
    }
}
