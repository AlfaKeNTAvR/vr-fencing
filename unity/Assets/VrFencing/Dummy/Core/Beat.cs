namespace VrFencing.Dummy
{
    /// <summary>
    /// One judged Beat (PER-101): a Fencer's blade contact that knocked the Armed Dummy's blade, held in line, out of
    /// line. Where on each blade it was, and how far the point was knocked.
    /// </summary>
    public readonly struct BeatReport
    {
        public BeatReport(float outOfLineMeters, float fencerBladeFraction, float dummyBladeFraction)
        {
            OutOfLineMeters = outOfLineMeters;
            FencerBladeFraction = fencerBladeFraction;
            DummyBladeFraction = dummyBladeFraction;
        }

        /// <summary>How far the Dummy's point was knocked off its line when the Beat was judged.</summary>
        public float OutOfLineMeters { get; }

        /// <summary>Where on the Fencer's blade: 0 at the guard to 1 at the point.</summary>
        public float FencerBladeFraction { get; }

        /// <summary>Where on the Dummy's blade: 0 at its guard to 1 at its point.</summary>
        public float DummyBladeFraction { get; }
    }
}
