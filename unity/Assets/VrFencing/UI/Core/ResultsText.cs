using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VrFencing.Dummy;
using VrFencing.Session;

namespace VrFencing.UI
{
    /// <summary>One bar of the "last 10 sessions" trend: the session's date and its success rate.</summary>
    public readonly struct TrendBar
    {
        public TrendBar(string dateLabel, float successFraction, string valueLabel)
        {
            DateLabel = dateLabel;
            SuccessFraction = successFraction;
            ValueLabel = valueLabel;
        }

        /// <summary>Month/day of the session, local time.</summary>
        public string DateLabel { get; }

        /// <summary>0 to 1: the bar's height.</summary>
        public float SuccessFraction { get; }

        /// <summary>The success percentage, e.g. "70%".</summary>
        public string ValueLabel { get; }
    }

    /// <summary>One line of the hit map: a Target Zone and how many of its Reps were Touched.</summary>
    public readonly struct HitMapLine
    {
        public HitMapLine(string zoneName, string countText, float successFraction)
        {
            ZoneName = zoneName;
            CountText = countText;
            SuccessFraction = successFraction;
        }

        public string ZoneName { get; }

        /// <summary>"Touched 2 of 3".</summary>
        public string CountText { get; }

        public float SuccessFraction { get; }
    }

    /// <summary>
    /// The words and numbers of the results panel and the history view (PER-27): success %, average reaction time,
    /// average tip speed, the hit map per Target Zone, and the last-10-sessions trend. Rendering is the Unity layer's.
    /// </summary>
    public static class ResultsText
    {
        public const string NoTouchesText = "no Touches";

        public const string NoneText = "none";

        /// <summary>Lines of the summary for one run (or one session's runs combined).</summary>
        /// <param name="wasCancelled">The run was stopped early; the lines say how many Reps it covers.</param>
        public static IReadOnlyList<string> FormatSummary(DrillResults results, bool wasCancelled)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            var lines = new List<string>
            {
                $"Success: {results.SuccessCount} of {results.RepCount} Reps ({FormatPercent(results.SuccessPercent)})",
                $"Average reaction time: {FormatOrNoTouches(results.AverageReactionSeconds, "0.00", "s")}",
            };

            // Footwork calls use no blade, so they have no tip speed to show.
            if (results.ZoneHits.Count > 0)
            {
                lines.Add($"Average tip speed: {FormatOrNoTouches(results.AverageTipSpeedMetersPerSecond, "0.0", "m/s")}");
            }

            // Parry Drills (PER-100): the lines parried, and the hits on the Fencer by the body that judged them.
            if (results.AttackHits.Count > 0)
            {
                lines.Add("Parries: " + FormatParryLines(results.ParryLines));
                lines.Add("Hits on you: " + FormatFencerHits(results.FencerHits));
            }

            if (wasCancelled)
            {
                lines.Add($"Cancelled after {results.RepCount} Rep{(results.RepCount == 1 ? string.Empty : "s")}");
            }

            if (results.IsShrunk)
            {
                lines.Add("Shortened Drill: not compared with full runs");
            }

            return lines;
        }

        /// <summary>
        /// The hit map, one line per Target Zone that was cued, in Target Zone order; for footwork calls, one line per
        /// called action ("Advance: Done 2 of 3"); for Parry Drills, one line per target the Armed Dummy thrust at
        /// ("At your hand: Done 2 of 3").
        /// </summary>
        public static IReadOnlyList<HitMapLine> FormatHitMap(DrillResults results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            var zoneLines = results.ZoneHits
                .Select(hits => new HitMapLine(
                    TargetZoneNames.Get(hits.Zone),
                    $"Touched {hits.Touches} of {hits.Attempts}",
                    hits.Attempts == 0 ? 0.0f : (float)hits.Touches / hits.Attempts));
            var footworkLines = results.FootworkHits
                .Select(hits => new HitMapLine(
                    FootworkNames.Get(hits.Call),
                    $"Done {hits.Successes} of {hits.Attempts}",
                    hits.Attempts == 0 ? 0.0f : (float)hits.Successes / hits.Attempts));
            var attackLines = results.AttackHits
                .Select(hits => new HitMapLine(
                    AttackTargetNames.Get(hits.Target),
                    $"Done {hits.Successes} of {hits.Attempts}",
                    hits.Attempts == 0 ? 0.0f : (float)hits.Successes / hits.Attempts));
            return zoneLines.Concat(footworkLines).Concat(attackLines).ToArray();
        }

        /// <summary>"6 x3, 4 x2", or "none" when no attack was deflected.</summary>
        public static string FormatParryLines(IReadOnlyList<ParryLineCount> parryLines)
        {
            if (parryLines == null || parryLines.Count == 0)
            {
                return NoneText;
            }

            return string.Join(", ", parryLines.Select(count => $"{(int)count.Line} x{count.Count}"));
        }

        /// <summary>"3 (body tracking 2, headset 1)", or "none".</summary>
        public static string FormatFencerHits(IReadOnlyList<FencerHitCount> fencerHits)
        {
            var total = fencerHits?.Sum(count => count.Count) ?? 0;
            if (total == 0)
            {
                return NoneText;
            }

            var bySource = string.Join(", ", fencerHits.Where(count => count.Count > 0).Select(count => $"{FormatHitSource(count.Source)} {count.Count}"));
            return $"{total} ({bySource})";
        }

        /// <summary>How the body that judged a hit on the Fencer reads: body tracking (IOBT) or the headset stand-in.</summary>
        public static string FormatHitSource(FencerHitSource source)
        {
            switch (source)
            {
                case FencerHitSource.Iobt:
                    return "body tracking";
                case FencerHitSource.HeadsetCapsules:
                    return "headset";
                default:
                    return source.ToString();
            }
        }

        /// <summary>One bar per session in <paramref name="trend"/>, oldest first, dated in <paramref name="localOffset"/>.</summary>
        public static IReadOnlyList<TrendBar> FormatTrend(IReadOnlyList<TrendPoint> trend, TimeSpan localOffset)
        {
            if (trend == null)
            {
                throw new ArgumentNullException(nameof(trend));
            }

            return trend
                .Select(point => new TrendBar(
                    point.SessionStartedAt.ToOffset(localOffset).ToString("M/d", CultureInfo.InvariantCulture),
                    (float)(point.Results.SuccessPercent / 100.0),
                    FormatPercent(point.Results.SuccessPercent)))
                .ToArray();
        }

        private static string FormatPercent(double percent)
        {
            return Math.Round(percent).ToString("0", CultureInfo.InvariantCulture) + "%";
        }

        private static string FormatOrNoTouches(double? value, string format, string unit)
        {
            return value.HasValue ? value.Value.ToString(format, CultureInfo.InvariantCulture) + " " + unit : NoTouchesText;
        }
    }

    /// <summary>How the Armed Dummy's attack targets read in the results: the Fencer's own body part (PER-100).</summary>
    public static class AttackTargetNames
    {
        public static string Get(AttackTarget target)
        {
            return "At your " + target.ToString().ToLowerInvariant();
        }
    }

    /// <summary>How Target Zones read in the menus and cues.</summary>
    public static class TargetZoneNames
    {
        public static string Get(TargetZone zone)
        {
            switch (zone)
            {
                case TargetZone.UpperArm:
                    return "Upper arm";
                default:
                    return zone.ToString();
            }
        }
    }
}
