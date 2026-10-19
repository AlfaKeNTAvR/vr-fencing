namespace VrFencing.Drills
{
    /// <summary>
    /// Whether a Drill fits the current Strip (Strip decision, PER-24). Drills that don't fit are greyed out in the picker
    /// with <see cref="RequiredStripLengthMeters"/> shown. With "shrink drills to fit" on, a Drill with a shortened
    /// variant runs that variant instead, and its Reps are tagged.
    /// </summary>
    public readonly struct DrillAvailability
    {
        public DrillAvailability(bool isAvailable, bool isShrunk, float requiredStripLengthMeters)
        {
            IsAvailable = isAvailable;
            IsShrunk = isShrunk;
            RequiredStripLengthMeters = requiredStripLengthMeters;
        }

        public bool IsAvailable { get; }

        /// <summary>The shortened variant runs; every Rep of the run is tagged as shrunk.</summary>
        public bool IsShrunk { get; }

        /// <summary>
        /// Shortest Strip that runs this Drill under the current settings: the shortened variant's length when shrink
        /// mode is on and the Drill has one, the full length otherwise. This is the number to show on a greyed-out Drill.
        /// </summary>
        public float RequiredStripLengthMeters { get; }

        /// <summary>Checks <paramref name="drill"/> against a Strip of <paramref name="stripLengthMeters"/>.</summary>
        public static DrillAvailability Check(DrillDefinition drill, float stripLengthMeters, bool shrinkDrillsToFit)
        {
            if (stripLengthMeters >= drill.RequiredStripLengthMeters)
            {
                return new DrillAvailability(isAvailable: true, isShrunk: false, drill.RequiredStripLengthMeters);
            }

            if (shrinkDrillsToFit && drill.ShrunkStripLengthMeters.HasValue)
            {
                var shrunkLength = drill.ShrunkStripLengthMeters.Value;
                var fits = stripLengthMeters >= shrunkLength;
                return new DrillAvailability(isAvailable: fits, isShrunk: fits, shrunkLength);
            }

            return new DrillAvailability(isAvailable: false, isShrunk: false, drill.RequiredStripLengthMeters);
        }
    }
}
