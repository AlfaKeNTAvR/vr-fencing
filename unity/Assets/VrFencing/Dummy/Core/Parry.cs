namespace VrFencing.Dummy
{
    /// <summary>
    /// The line a Parry closes (PER-23), for the Fencer's sword hand: high lines with the point up (6 outside, toward
    /// the sword hand's side; 4 inside), low lines with the point dropped below the hand (8 outside, 7 inside). The
    /// value is the line's number.
    /// </summary>
    public enum ParryLine
    {
        Four = 4,
        Six = 6,
        Seven = 7,
        Eight = 8,
    }

    /// <summary>
    /// One judged blade-on-blade contact that deflected the Armed Dummy's point off the Fencer's body (PER-97): the line
    /// it closed, where on each blade it was, and whether it counts as a Parry under the Parry rule Setting.
    /// </summary>
    public readonly struct ParryReport
    {
        public ParryReport(
            ParryLine line,
            bool isForteOnFoible,
            bool countsAsParry,
            float deflectionMeters,
            float fencerBladeFraction,
            float dummyBladeFraction)
        {
            Line = line;
            IsForteOnFoible = isForteOnFoible;
            CountsAsParry = countsAsParry;
            DeflectionMeters = deflectionMeters;
            FencerBladeFraction = fencerBladeFraction;
            DummyBladeFraction = dummyBladeFraction;
        }

        public ParryLine Line { get; }

        /// <summary>The contact was on the Fencer's forte and the Dummy's foible.</summary>
        public bool IsForteOnFoible { get; }

        /// <summary>
        /// The contact passes the Parry rule: any deflecting contact when loose, only forte on foible when strict. A
        /// deflecting contact that does not still turns the attack aside, but the Rep fails.
        /// </summary>
        public bool CountsAsParry { get; }

        /// <summary>How far the Dummy's point was knocked off its line, square to the thrust, when it was judged.</summary>
        public float DeflectionMeters { get; }

        /// <summary>Where on the Fencer's blade: 0 at the guard (forte) to 1 at the point (foible).</summary>
        public float FencerBladeFraction { get; }

        /// <summary>Where on the Dummy's blade: 0 at its guard to 1 at its point.</summary>
        public float DummyBladeFraction { get; }
    }
}
