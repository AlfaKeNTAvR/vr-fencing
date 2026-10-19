using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VrFencing.Drills;

namespace VrFencing.UI
{
    /// <summary>One row of the Drill picker: the Drill, whether it can start on the current Strip, and the note shown beside it.</summary>
    public sealed class DrillPickerEntry
    {
        public DrillPickerEntry(DrillDefinition drill, bool isAvailable, bool isShrunk, string note)
        {
            Drill = drill;
            IsAvailable = isAvailable;
            IsShrunk = isShrunk;
            Note = note;
        }

        public DrillDefinition Drill { get; }

        /// <summary>False greys the row out; it cannot be selected.</summary>
        public bool IsAvailable { get; }

        /// <summary>The Drill runs its shortened variant on this Strip.</summary>
        public bool IsShrunk { get; }

        /// <summary>Why a row is greyed out (with the Strip length it needs), or that it runs shortened. Empty otherwise.</summary>
        public string Note { get; }
    }

    /// <summary>
    /// The Drill picker's rows (PER-27): every registered Drill, easy to hard, with the ones that don't fit the
    /// current Strip greyed out and their required length shown. Availability is <see cref="DrillAvailability"/>,
    /// the same check the runner makes on start.
    /// </summary>
    public static class DrillPicker
    {
        public const string NoStripNote = "Place the Strip first";

        /// <param name="stripLengthMeters">The Strip's end-to-end length, or null when no Strip is placed (every Drill greyed out).</param>
        public static IReadOnlyList<DrillPickerEntry> BuildEntries(IReadOnlyList<DrillDefinition> drills, float? stripLengthMeters, bool shrinkDrillsToFit)
        {
            if (drills == null)
            {
                throw new ArgumentNullException(nameof(drills));
            }

            return drills
                .OrderBy(drill => drill.Order)
                .ThenBy(drill => drill.Name, StringComparer.Ordinal)
                .Select(drill => BuildEntry(drill, stripLengthMeters, shrinkDrillsToFit))
                .ToArray();
        }

        /// <summary>
        /// The Drill to keep selected after the rows change: the current one if it is still available, otherwise the
        /// first available Drill, or null when none is.
        /// </summary>
        public static string ResolveSelection(IReadOnlyList<DrillPickerEntry> entries, string selectedDrillId)
        {
            var available = entries.Where(entry => entry.IsAvailable).ToArray();
            if (available.Any(entry => entry.Drill.Id == selectedDrillId))
            {
                return selectedDrillId;
            }

            return available.FirstOrDefault()?.Drill.Id;
        }

        private static DrillPickerEntry BuildEntry(DrillDefinition drill, float? stripLengthMeters, bool shrinkDrillsToFit)
        {
            if (!stripLengthMeters.HasValue)
            {
                return new DrillPickerEntry(drill, isAvailable: false, isShrunk: false, NoStripNote);
            }

            var availability = DrillAvailability.Check(drill, stripLengthMeters.Value, shrinkDrillsToFit);
            if (!availability.IsAvailable)
            {
                return new DrillPickerEntry(drill, isAvailable: false, isShrunk: false, $"Needs a {FormatMeters(availability.RequiredStripLengthMeters)} Strip");
            }

            return new DrillPickerEntry(drill, isAvailable: true, availability.IsShrunk, availability.IsShrunk ? "Shortened to fit" : string.Empty);
        }

        private static string FormatMeters(float meters)
        {
            return meters.ToString("0.0", CultureInfo.InvariantCulture) + " m";
        }
    }
}
