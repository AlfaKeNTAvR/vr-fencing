using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using VrFencing.Drills;
using VrFencing.Dummy;

namespace VrFencing.Session
{
    /// <summary>
    /// What the results panel shows for a Drill (PER-27): success %, average reaction time, average tip speed
    /// and a hit map per Target Zone (per called action for footwork calls, per attack target for Parry Drills, which
    /// also count the lines parried and the hits on the Fencer by the body that judged them, PER-100). Built from one run's Reps with <see cref="Aggregate"/>, or merged across runs
    /// with <see cref="Combine"/> for the history trend.
    /// Averages count successful Reps only: a wrong-zone or flat contact is not a Touch, so its timing is not a
    /// reaction time the Fencer should compare.
    /// </summary>
    public sealed class DrillResults
    {
        [JsonConstructor]
        public DrillResults(
            string drillId,
            int repCount,
            int successCount,
            double? averageReactionSeconds,
            double? averageTipSpeedMetersPerSecond,
            bool isShrunk,
            IReadOnlyList<ZoneHitCount> zoneHits,
            IReadOnlyList<FootworkHitCount> footworkHits = null,
            IReadOnlyList<AttackHitCount> attackHits = null,
            IReadOnlyList<ParryLineCount> parryLines = null,
            IReadOnlyList<FencerHitCount> fencerHits = null,
            BeatHitCount? beatHits = null)
        {
            // Results saved before footwork calls (or the Parry Drills) existed have no such hit map.
            footworkHits = footworkHits ?? Array.Empty<FootworkHitCount>();
            attackHits = attackHits ?? Array.Empty<AttackHitCount>();
            if (string.IsNullOrWhiteSpace(drillId))
            {
                throw new ArgumentException("Results need a Drill id.", nameof(drillId));
            }

            if (repCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(repCount), repCount, "Results need at least one Rep.");
            }

            if (successCount < 0 || successCount > repCount)
            {
                throw new ArgumentOutOfRangeException(nameof(successCount), successCount, "Must be between 0 and the Rep count.");
            }

            if (zoneHits == null
                || zoneHits.Sum(hits => hits.Attempts) + footworkHits.Sum(hits => hits.Attempts) + attackHits.Sum(hits => hits.Attempts)
                    + (beatHits?.Attempts ?? 0) != repCount
                || zoneHits.Sum(hits => hits.Touches) + footworkHits.Sum(hits => hits.Successes) + attackHits.Sum(hits => hits.Successes)
                    + (beatHits?.Successes ?? 0) != successCount)
            {
                throw new ArgumentException("The hit maps must account for every Rep and every success.", nameof(zoneHits));
            }

            DrillId = drillId;
            RepCount = repCount;
            SuccessCount = successCount;
            AverageReactionSeconds = averageReactionSeconds;
            AverageTipSpeedMetersPerSecond = averageTipSpeedMetersPerSecond;
            IsShrunk = isShrunk;
            ZoneHits = zoneHits;
            FootworkHits = footworkHits;
            AttackHits = attackHits;
            ParryLines = parryLines ?? Array.Empty<ParryLineCount>();
            FencerHits = fencerHits ?? Array.Empty<FencerHitCount>();
            BeatHits = beatHits;
        }

        public string DrillId { get; }

        public int RepCount { get; }

        /// <summary>Reps judged a success: a point-first Touch on the cued Target Zone in time.</summary>
        public int SuccessCount { get; }

        /// <summary>0 to 100.</summary>
        public double SuccessPercent => 100.0 * SuccessCount / RepCount;

        /// <summary>Over successful Reps; null when none succeeded.</summary>
        public double? AverageReactionSeconds { get; }

        /// <summary>Over successful Reps; null when none succeeded.</summary>
        public double? AverageTipSpeedMetersPerSecond { get; }

        /// <summary>The Reps ran in the shortened variant of the Drill, so they are not comparable with full runs.</summary>
        public bool IsShrunk { get; }

        /// <summary>One entry per Target Zone that was cued at least once, in <see cref="TargetZone"/> order.</summary>
        public IReadOnlyList<ZoneHitCount> ZoneHits { get; }

        /// <summary>Footwork calls: one entry per action that was called at least once, in <see cref="FootworkAction"/> order. Empty for other Drills.</summary>
        public IReadOnlyList<FootworkHitCount> FootworkHits { get; }

        /// <summary>Parry Drills: one entry per target the Armed Dummy thrust at, in <see cref="AttackTarget"/> order. Empty for other Drills.</summary>
        public IReadOnlyList<AttackHitCount> AttackHits { get; }

        /// <summary>
        /// Parry Drills: the Reps whose attack was deflected, per line (6, 4, 8, 7), in <see cref="ParryLine"/> order; a
        /// deflection the strict Parry rule rejected counts too. Lines never parried are left out.
        /// </summary>
        public IReadOnlyList<ParryLineCount> ParryLines { get; }

        /// <summary>
        /// Parry Drills: the Reps whose thrust landed on the Fencer, per body that judged it (IOBT or the headset capsules),
        /// in <see cref="FencerHitSource"/> order. Sources that judged no hit are left out.
        /// </summary>
        public IReadOnlyList<FencerHitCount> FencerHits { get; }

        /// <summary>Beat Drills (PER-101): the Reps, how many Beats landed and how many were followed by a Touch; null for other Drills.</summary>
        public BeatHitCount? BeatHits { get; }

        /// <summary>The hit count for a zone; zero attempts when the zone was never cued.</summary>
        public ZoneHitCount HitsFor(TargetZone zone)
        {
            foreach (var hits in ZoneHits)
            {
                if (hits.Zone == zone)
                {
                    return hits;
                }
            }

            return new ZoneHitCount(zone, attempts: 0, touches: 0);
        }

        /// <summary>Results of one Drill's Reps. Shrunk if any Rep was.</summary>
        /// <exception cref="ArgumentException">No Reps, or Reps from more than one Drill.</exception>
        public static DrillResults Aggregate(IReadOnlyList<RepLogEntry> reps)
        {
            if (reps == null || reps.Count == 0)
            {
                throw new ArgumentException("Results need at least one Rep.", nameof(reps));
            }

            var drillId = reps[0].DrillId;
            if (reps.Any(rep => rep.DrillId != drillId))
            {
                throw new ArgumentException("Results cover one Drill at a time.", nameof(reps));
            }

            var successes = reps.Where(rep => rep.IsSuccess).ToArray();
            var beatReps = reps.Where(IsBeatRep).ToArray();
            var otherReps = reps.Where(rep => !IsBeatRep(rep)).ToArray();
            var zoneHits = otherReps
                .Where(rep => !rep.AttackTarget.HasValue && rep.TargetZone.HasValue)
                .GroupBy(rep => rep.TargetZone.Value)
                .OrderBy(group => group.Key)
                .Select(group => new ZoneHitCount(group.Key, group.Count(), group.Count(rep => rep.IsSuccess)))
                .ToArray();
            var footworkHits = otherReps
                .Where(rep => !rep.AttackTarget.HasValue && !rep.TargetZone.HasValue)
                .GroupBy(rep => rep.FootworkCall ?? FootworkAction.None)
                .OrderBy(group => group.Key)
                .Select(group => new FootworkHitCount(group.Key, group.Count(), group.Count(rep => rep.IsSuccess)))
                .ToArray();
            var attackHits = reps
                .Where(rep => rep.AttackTarget.HasValue)
                .GroupBy(rep => rep.AttackTarget.Value)
                .OrderBy(group => group.Key)
                .Select(group => new AttackHitCount(group.Key, group.Count(), group.Count(rep => rep.IsSuccess)))
                .ToArray();
            var parryLines = reps
                .Where(rep => rep.ParryLine.HasValue)
                .GroupBy(rep => rep.ParryLine.Value)
                .OrderBy(group => group.Key)
                .Select(group => new ParryLineCount(group.Key, group.Count()))
                .ToArray();
            var fencerHits = reps
                .Where(rep => rep.HitSource.HasValue)
                .GroupBy(rep => rep.HitSource.Value)
                .OrderBy(group => group.Key)
                .Select(group => new FencerHitCount(group.Key, group.Count()))
                .ToArray();

            return new DrillResults(
                drillId,
                reps.Count,
                successes.Length,
                AverageOf(successes.Select(rep => rep.ReactionSeconds)),
                AverageOf(successes.Select(rep => (double?)rep.TipSpeedMetersPerSecond)),
                reps.Any(rep => rep.IsShrunk),
                zoneHits,
                footworkHits,
                attackHits,
                parryLines,
                fencerHits,
                beatReps.Length == 0
                    ? (BeatHitCount?)null
                    : new BeatHitCount(beatReps.Length, beatReps.Count(rep => rep.BeatOutOfLineMeters.HasValue), beatReps.Count(rep => rep.IsSuccess)));
        }

        /// <summary>
        /// Merges results of the same Drill, as if their Reps had been aggregated together:
        /// averages are weighted by each part's successful Reps, hit maps are summed.
        /// </summary>
        /// <exception cref="ArgumentException">Nothing to merge, or parts from different Drills or Strip variants.</exception>
        public static DrillResults Combine(IReadOnlyList<DrillResults> parts)
        {
            if (parts == null || parts.Count == 0)
            {
                throw new ArgumentException("Nothing to combine.", nameof(parts));
            }

            var first = parts[0];
            if (parts.Any(part => part.DrillId != first.DrillId || part.IsShrunk != first.IsShrunk))
            {
                throw new ArgumentException("Only results of the same Drill and Strip variant combine.", nameof(parts));
            }

            var zoneHits = parts
                .SelectMany(part => part.ZoneHits)
                .GroupBy(hits => hits.Zone)
                .OrderBy(group => group.Key)
                .Select(group => new ZoneHitCount(group.Key, group.Sum(hits => hits.Attempts), group.Sum(hits => hits.Touches)))
                .ToArray();
            var footworkHits = parts
                .SelectMany(part => part.FootworkHits)
                .GroupBy(hits => hits.Call)
                .OrderBy(group => group.Key)
                .Select(group => new FootworkHitCount(group.Key, group.Sum(hits => hits.Attempts), group.Sum(hits => hits.Successes)))
                .ToArray();
            var attackHits = parts
                .SelectMany(part => part.AttackHits)
                .GroupBy(hits => hits.Target)
                .OrderBy(group => group.Key)
                .Select(group => new AttackHitCount(group.Key, group.Sum(hits => hits.Attempts), group.Sum(hits => hits.Successes)))
                .ToArray();
            var parryLines = parts
                .SelectMany(part => part.ParryLines)
                .GroupBy(count => count.Line)
                .OrderBy(group => group.Key)
                .Select(group => new ParryLineCount(group.Key, group.Sum(count => count.Count)))
                .ToArray();
            var fencerHits = parts
                .SelectMany(part => part.FencerHits)
                .GroupBy(count => count.Source)
                .OrderBy(group => group.Key)
                .Select(group => new FencerHitCount(group.Key, group.Sum(count => count.Count)))
                .ToArray();

            return new DrillResults(
                first.DrillId,
                parts.Sum(part => part.RepCount),
                parts.Sum(part => part.SuccessCount),
                WeightedAverageOf(parts, part => part.AverageReactionSeconds),
                WeightedAverageOf(parts, part => part.AverageTipSpeedMetersPerSecond),
                first.IsShrunk,
                zoneHits,
                footworkHits,
                attackHits,
                parryLines,
                fencerHits,
                CombineBeatHits(parts));
        }

        /// <summary>A Beat Drill's Rep cues no zone, attack or footwork (PER-101); it is logged with its Drill's id.</summary>
        private static bool IsBeatRep(RepLogEntry rep)
        {
            return rep.DrillId == DrillCatalog.BeatDrillId;
        }

        private static BeatHitCount? CombineBeatHits(IReadOnlyList<DrillResults> parts)
        {
            var present = parts.Where(part => part.BeatHits.HasValue).Select(part => part.BeatHits.Value).ToArray();
            if (present.Length == 0)
            {
                return null;
            }

            return new BeatHitCount(present.Sum(hits => hits.Attempts), present.Sum(hits => hits.Beats), present.Sum(hits => hits.Successes));
        }

        private static double? AverageOf(IEnumerable<double?> values)
        {
            var present = values.Where(value => value.HasValue).Select(value => value.Value).ToArray();
            return present.Length == 0 ? (double?)null : present.Average();
        }

        private static double? WeightedAverageOf(IReadOnlyList<DrillResults> parts, Func<DrillResults, double?> average)
        {
            var weighted = parts.Where(part => average(part).HasValue && part.SuccessCount > 0).ToArray();
            var totalWeight = weighted.Sum(part => part.SuccessCount);
            if (totalWeight == 0)
            {
                return null;
            }

            return weighted.Sum(part => average(part).Value * part.SuccessCount) / totalWeight;
        }
    }

    /// <summary>One cell of the hit map: how often a Target Zone was cued and how often it was Touched.</summary>
    public readonly struct ZoneHitCount : IEquatable<ZoneHitCount>
    {
        [JsonConstructor]
        public ZoneHitCount(TargetZone zone, int attempts, int touches)
        {
            if (attempts < 0 || touches < 0 || touches > attempts)
            {
                throw new ArgumentOutOfRangeException(nameof(touches), touches, "Touches must be between 0 and the attempts.");
            }

            Zone = zone;
            Attempts = attempts;
            Touches = touches;
        }

        public TargetZone Zone { get; }

        /// <summary>Reps that cued this zone.</summary>
        public int Attempts { get; }

        /// <summary>Of those, Reps judged a success.</summary>
        public int Touches { get; }

        public bool Equals(ZoneHitCount other)
        {
            return Zone == other.Zone && Attempts == other.Attempts && Touches == other.Touches;
        }

        public override bool Equals(object obj)
        {
            return obj is ZoneHitCount other && Equals(other);
        }

        public override int GetHashCode()
        {
            return ((int)Zone * 397 ^ Attempts) * 397 ^ Touches;
        }

        public override string ToString()
        {
            return $"{Zone}: {Touches}/{Attempts}";
        }
    }

    /// <summary>One cell of the footwork hit map: how often an action was called and how often it was answered.</summary>
    public readonly struct FootworkHitCount : IEquatable<FootworkHitCount>
    {
        [JsonConstructor]
        public FootworkHitCount(FootworkAction call, int attempts, int successes)
        {
            if (attempts < 0 || successes < 0 || successes > attempts)
            {
                throw new ArgumentOutOfRangeException(nameof(successes), successes, "Successes must be between 0 and the attempts.");
            }

            Call = call;
            Attempts = attempts;
            Successes = successes;
        }

        public FootworkAction Call { get; }

        /// <summary>Reps that called this action.</summary>
        public int Attempts { get; }

        /// <summary>Of those, Reps judged a success.</summary>
        public int Successes { get; }

        public bool Equals(FootworkHitCount other)
        {
            return Call == other.Call && Attempts == other.Attempts && Successes == other.Successes;
        }

        public override bool Equals(object obj)
        {
            return obj is FootworkHitCount other && Equals(other);
        }

        public override int GetHashCode()
        {
            return ((int)Call * 397 ^ Attempts) * 397 ^ Successes;
        }

        public override string ToString()
        {
            return $"{Call}: {Successes}/{Attempts}";
        }
    }
}
