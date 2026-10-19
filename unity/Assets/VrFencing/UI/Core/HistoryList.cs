using System;
using System.Collections.Generic;
using System.Linq;
using VrFencing.Drills;
using VrFencing.Session;

namespace VrFencing.UI
{
    /// <summary>One Drill in the history view. Full and shortened runs are listed apart: their numbers are not comparable.</summary>
    public sealed class HistoryEntry
    {
        public HistoryEntry(string drillId, bool isShrunk, string label, DrillRunRecord lastRun)
        {
            DrillId = drillId;
            IsShrunk = isShrunk;
            Label = label;
            LastRun = lastRun;
        }

        public string DrillId { get; }

        public bool IsShrunk { get; }

        /// <summary>The Drill's name, marked when shortened; the id for a Drill no longer registered.</summary>
        public string Label { get; }

        /// <summary>The newest saved run, shown beside the trend.</summary>
        public DrillRunRecord LastRun { get; }
    }

    /// <summary>The history view's list: every Drill with saved runs, in picker order, then Drills no longer registered by id.</summary>
    public static class HistoryList
    {
        public static IReadOnlyList<HistoryEntry> Build(SessionHistory history, IReadOnlyList<DrillDefinition> knownDrills)
        {
            if (history == null)
            {
                throw new ArgumentNullException(nameof(history));
            }

            var drills = (knownDrills ?? Array.Empty<DrillDefinition>())
                .OrderBy(drill => drill.Order)
                .ThenBy(drill => drill.Name, StringComparer.Ordinal)
                .ToArray();

            return history.Records
                .GroupBy(record => (record.Results.DrillId, record.Results.IsShrunk))
                .Select(group =>
                {
                    var drillIndex = Array.FindIndex(drills, drill => drill.Id == group.Key.DrillId);
                    var name = drillIndex >= 0 ? drills[drillIndex].Name : group.Key.DrillId;
                    var label = group.Key.IsShrunk ? name + " (shortened)" : name;
                    var lastRun = group.OrderBy(record => record.FinishedAt).Last();
                    return (Entry: new HistoryEntry(group.Key.DrillId, group.Key.IsShrunk, label, lastRun), Rank: drillIndex >= 0 ? drillIndex : int.MaxValue);
                })
                .OrderBy(item => item.Rank)
                .ThenBy(item => item.Entry.DrillId, StringComparer.Ordinal)
                .ThenBy(item => item.Entry.IsShrunk)
                .Select(item => item.Entry)
                .ToArray();
        }
    }
}
