using System;
using Newtonsoft.Json;
using VrFencing.Dummy;

namespace VrFencing.Session
{
    /// <summary>
    /// One cell of a Parry Drill's hit map (PER-100): how often the Armed Dummy thrust at a target and how many of those
    /// Reps succeeded (parried, and riposted when the Drill asks for it).
    /// </summary>
    public readonly struct AttackHitCount : IEquatable<AttackHitCount>
    {
        [JsonConstructor]
        public AttackHitCount(AttackTarget target, int attempts, int successes)
        {
            if (attempts < 0 || successes < 0 || successes > attempts)
            {
                throw new ArgumentOutOfRangeException(nameof(successes), successes, "Successes must be between 0 and the attempts.");
            }

            Target = target;
            Attempts = attempts;
            Successes = successes;
        }

        public AttackTarget Target { get; }

        /// <summary>Reps whose thrust aimed here.</summary>
        public int Attempts { get; }

        /// <summary>Of those, Reps judged a success.</summary>
        public int Successes { get; }

        public bool Equals(AttackHitCount other)
        {
            return Target == other.Target && Attempts == other.Attempts && Successes == other.Successes;
        }

        public override bool Equals(object obj)
        {
            return obj is AttackHitCount other && Equals(other);
        }

        public override int GetHashCode()
        {
            return ((int)Target * 397 ^ Attempts) * 397 ^ Successes;
        }

        public override string ToString()
        {
            return $"{Target}: {Successes}/{Attempts}";
        }
    }

    /// <summary>A Beat Drill's Reps (PER-101): how many there were, how many Beats landed, and how many were followed by a Touch in time.</summary>
    public readonly struct BeatHitCount : IEquatable<BeatHitCount>
    {
        [JsonConstructor]
        public BeatHitCount(int attempts, int beats, int successes)
        {
            if (attempts < 0 || beats < 0 || beats > attempts || successes < 0 || successes > beats)
            {
                throw new ArgumentOutOfRangeException(nameof(successes), successes, "Need 0 <= successes <= beats <= attempts.");
            }

            Attempts = attempts;
            Beats = beats;
            Successes = successes;
        }

        public int Attempts { get; }

        /// <summary>Reps whose Beat knocked the Dummy's blade out of line.</summary>
        public int Beats { get; }

        /// <summary>Of those, Reps judged a success: a Touch within 1 s of the Beat.</summary>
        public int Successes { get; }

        public bool Equals(BeatHitCount other)
        {
            return Attempts == other.Attempts && Beats == other.Beats && Successes == other.Successes;
        }

        public override bool Equals(object obj)
        {
            return obj is BeatHitCount other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (Attempts * 397 ^ Beats) * 397 ^ Successes;
        }

        public override string ToString()
        {
            return $"Beats {Beats}/{Attempts}, Touches {Successes}";
        }
    }

    /// <summary>How many of a Parry Drill's Reps deflected the attack with a contact in one line (PER-100).</summary>
    public readonly struct ParryLineCount : IEquatable<ParryLineCount>
    {
        [JsonConstructor]
        public ParryLineCount(ParryLine line, int count)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, "Must not be negative.");
            }

            Line = line;
            Count = count;
        }

        public ParryLine Line { get; }

        public int Count { get; }

        public bool Equals(ParryLineCount other)
        {
            return Line == other.Line && Count == other.Count;
        }

        public override bool Equals(object obj)
        {
            return obj is ParryLineCount other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (int)Line * 397 ^ Count;
        }

        public override string ToString()
        {
            return $"{(int)Line}: {Count}";
        }
    }

    /// <summary>How many of a Parry Drill's Reps the Armed Dummy's point landed on the Fencer, judged by one source (PER-100).</summary>
    public readonly struct FencerHitCount : IEquatable<FencerHitCount>
    {
        [JsonConstructor]
        public FencerHitCount(FencerHitSource source, int count)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, "Must not be negative.");
            }

            Source = source;
            Count = count;
        }

        public FencerHitSource Source { get; }

        public int Count { get; }

        public bool Equals(FencerHitCount other)
        {
            return Source == other.Source && Count == other.Count;
        }

        public override bool Equals(object obj)
        {
            return obj is FencerHitCount other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (int)Source * 397 ^ Count;
        }

        public override string ToString()
        {
            return $"{Source}: {Count}";
        }
    }
}
