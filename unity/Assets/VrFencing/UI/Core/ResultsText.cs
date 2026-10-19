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
                $"Average tip speed: {FormatOrNoTouches(results.AverageTipSpeedMetersPerSecond, "0.0", "m/s")}",
            };

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

        /// <summary>The hit map, one line per Target Zone that was cued, in Target Zone order.</summary>
        public static IReadOnlyList<HitMapLine> FormatHitMap(DrillResults results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            return results.ZoneHits
                .Select(hits => new HitMapLine(
                    TargetZoneNames.Get(hits.Zone),
                    $"Touched {hits.Touches} of {hits.Attempts}",
                    hits.Attempts == 0 ? 0.0f : (float)hits.Touches / hits.Attempts))
                .ToArray();
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
