namespace VrFencing.Settings
{
    /// <summary>Which hand holds the blade. Left mirrors every pose and layout (see <see cref="HandMirroring"/>).</summary>
    public enum Hand
    {
        Right,
        Left,
    }

    /// <summary>
    /// Everything the Fencer can change, persisted as JSON by <see cref="SettingsStorage"/>.
    /// A new instance holds the defaults agreed in the Drill decision (PER-21) and the Strip decision (PER-24).
    /// </summary>
    public sealed class TrainerSettings
    {
        public Hand Hand { get; set; } = Hand.Right;

        /// <summary>Also sets the Dummy's height.</summary>
        public float FencerHeightMeters { get; set; } = 1.65f;

        public int RepsPerDrill { get; set; } = 10;

        public float RestBetweenRepsSeconds { get; set; } = 2.0f;

        public TimeWindowSettings TimeWindows { get; set; } = new TimeWindowSettings();

        /// <summary>Run shorter versions of Drills that need a longer Strip than the room allows. Those Reps are tagged in the rep log.</summary>
        public bool ShrinkDrillsToFit { get; set; } = false;

        public WarningZoneSettings WarningZone { get; set; } = new WarningZoneSettings();

        /// <summary>The Blade: Touch rule, how it sits in the hand, and its haptics (PER-22 feel tests, PER-33).</summary>
        public BladeSettings Blade { get; set; } = new BladeSettings();

        /// <summary>
        /// Tilts the menu laser down (positive) or up from the off-hand controller's forward axis (PER-48). Meta's SDK
        /// gives no separate aim pose for controllers under OpenXR; its controller rotation already follows the aim
        /// pose's, which Nikita found close but pointing a little high, so the default tilts it down.
        /// </summary>
        public float LaserTiltDegrees { get; set; } = 15.0f;

        /// <summary>A deep copy, so a settings screen can change values without touching the settings in use.</summary>
        public TrainerSettings Clone()
        {
            return new TrainerSettings
            {
                Hand = Hand,
                FencerHeightMeters = FencerHeightMeters,
                RepsPerDrill = RepsPerDrill,
                RestBetweenRepsSeconds = RestBetweenRepsSeconds,
                TimeWindows = new TimeWindowSettings
                {
                    ReactionSeconds = TimeWindows.ReactionSeconds,
                    CompoundSecondTouchSeconds = TimeWindows.CompoundSecondTouchSeconds,
                    DistanceChoiceSeconds = TimeWindows.DistanceChoiceSeconds,
                    FootworkCallSeconds = TimeWindows.FootworkCallSeconds,
                },
                ShrinkDrillsToFit = ShrinkDrillsToFit,
                WarningZone = new WarningZoneSettings
                {
                    LengthOnPisteMeters = WarningZone.LengthOnPisteMeters,
                    MinimumLengthMeters = WarningZone.MinimumLengthMeters,
                    OffStripToleranceMeters = WarningZone.OffStripToleranceMeters,
                },
                Blade = new BladeSettings
                {
                    TouchRule = Blade.TouchRule,
                    Mount = Blade.Mount,
                    PitchDegrees = Blade.PitchDegrees,
                    YawDegrees = Blade.YawDegrees,
                    TouchHaptic = Blade.TouchHaptic,
                    FlatHaptic = Blade.FlatHaptic,
                    GlanceHaptic = Blade.GlanceHaptic,
                    HapticFloor = Blade.HapticFloor,
                    HapticFullStrengthSpeedMetersPerSecond = Blade.HapticFullStrengthSpeedMetersPerSecond,
                },
                LaserTiltDegrees = LaserTiltDegrees,
            };
        }
    }

    /// <summary>How long the Fencer has to complete a Rep after its cue, per Drill type.</summary>
    public sealed class TimeWindowSettings
    {
        /// <summary>Reaction Drill: Touch the lit Target Zone before it goes dark.</summary>
        public float ReactionSeconds { get; set; } = 1.5f;

        /// <summary>Compound sequence: the second Touch must land within this time of the first.</summary>
        public float CompoundSecondTouchSeconds { get; set; } = 1.0f;

        public float DistanceChoiceSeconds { get; set; } = 2.0f;

        public float FootworkCallSeconds { get; set; } = 2.0f;
    }

    /// <summary>
    /// Warning Zone size and the off-strip threshold. The Warning Zone is
    /// <see cref="LengthOnPisteMeters"/> on a regulation 14 m piste and scales down
    /// with the Strip length, but never below <see cref="MinimumLengthMeters"/>.
    /// </summary>
    public sealed class WarningZoneSettings
    {
        public float LengthOnPisteMeters { get; set; } = 2.0f;

        public float MinimumLengthMeters { get; set; } = 0.5f;

        /// <summary>How far the Fencer's head may pass a Strip end or side before the Drill pauses.</summary>
        public float OffStripToleranceMeters { get; set; } = 0.0f;
    }
}
