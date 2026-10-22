namespace VrFencing.Settings
{
    /// <summary>Which hand holds the blade. Left mirrors every pose and layout (see <see cref="HandMirroring"/>).</summary>
    public enum Hand
    {
        Right,
        Left,
    }

    /// <summary>
    /// What the Dummy does in Free Practice (PER-123), outside any Drill: the Armed Dummy holds its blade and, with no
    /// Reps or scoring, stands in guard, keeps its distance from the Fencer, runs through its footwork, or attacks now and
    /// then. Picked on the home screen. <see cref="HandFixed"/> is a test stand for blade on blade contact alone: en garde
    /// with a hand that never moves or gives, so only the two blades react.
    /// </summary>
    public enum FreePracticeBehavior
    {
        Guard,
        KeepDistance,
        Footwork,
        Attacks,
        HandFixed,
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

        /// <summary>
        /// Drills 1 and 2 (point control, lunge to target) also light the named Target Zone, so the Fencer learns where
        /// each name is on the Dummy (PER-92). On by default; a file from before PER-92 loads it on.
        /// </summary>
        public bool LightNamedZones { get; set; } = true;

        public WarningZoneSettings WarningZone { get; set; } = new WarningZoneSettings();

        /// <summary>The Blade: Touch rule, how it sits in the hand, and its haptics (PER-22 feel tests, PER-33).</summary>
        public BladeSettings Blade { get; set; } = new BladeSettings();

        /// <summary>
        /// Tilts the menu laser down (positive) or up from the off-hand controller's forward axis (PER-48). Meta's SDK
        /// gives no separate aim pose for controllers under OpenXR; its controller rotation already follows the aim
        /// pose's, which Nikita found close but pointing a little high, so the default tilts it down.
        /// </summary>
        public float LaserTiltDegrees { get; set; } = 15.0f;

        /// <summary>How the Dummy's body and Target Zones are drawn (PER-102). Never changes how Touches are judged.</summary>
        public DummyLookSettings DummyLook { get; set; } = new DummyLookSettings();

        /// <summary>
        /// A Bout's double window when this Fencer hosts (PER-56): two Touches this close count as a double. 50 ms is the
        /// rule's; up to 150 ms for beginners or a bad connection. The Partner's own value is not used in a bout.
        /// </summary>
        public float BoutDoubleWindowSeconds { get; set; } = 0.05f;

        /// <summary>
        /// The Armed Dummy: which Parry Drills ask for a Riposte after the Parry (PER-100), and whether the Dummy holds its
        /// blade in the Drills where it does not attack (PER-117).
        /// </summary>
        public ArmedDummySettings ArmedDummy { get; set; } = new ArmedDummySettings();

        /// <summary>
        /// How far to either side of the Strip's center line the head may be and still count as back at the Start Mark
        /// between Reps (PER-117). Along the Strip the check stays tight (<c>StartMark.ToleranceMeters</c>); across it is
        /// generous, since where the head sits across the Strip depends on the stance and nobody stands exactly on the
        /// center line. Before PER-117 the side was not checked at all.
        /// </summary>
        public float StartMarkSideToleranceMeters { get; set; } = 0.45f;

        /// <summary>
        /// What the armed Dummy does outside any Drill (PER-123). Keeping its distance by default, Nikita's pick; a file
        /// from before PER-123 loads that too.
        /// </summary>
        public FreePracticeBehavior FreePractice { get; set; } = FreePracticeBehavior.KeepDistance;

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
                LightNamedZones = LightNamedZones,
                WarningZone = new WarningZoneSettings
                {
                    LengthOnPisteMeters = WarningZone.LengthOnPisteMeters,
                    MinimumLengthMeters = WarningZone.MinimumLengthMeters,
                    OffStripToleranceMeters = WarningZone.OffStripToleranceMeters,
                },
                Blade = Blade.Clone(),
                LaserTiltDegrees = LaserTiltDegrees,
                DummyLook = DummyLook.Clone(),
                BoutDoubleWindowSeconds = BoutDoubleWindowSeconds,
                ArmedDummy = ArmedDummy.Clone(),
                StartMarkSideToleranceMeters = StartMarkSideToleranceMeters,
                FreePractice = FreePractice,
            };
        }
    }

    /// <summary>
    /// The Armed Dummy's Parry Drills (PER-100). The Riposte after the Parry is a per-Drill switch (PER-23), so a Drill
    /// can be run as a parry-only warm-up. Every switch is on by default.
    /// </summary>
    public sealed class ArmedDummySettings
    {
        /// <summary>
        /// The Dummy holds its blade in every Drill (PER-117), so the Fencer always has a blade to sense while moving and
        /// attacking: in a Drill where it does not attack, it holds its en garde line and never thrusts. Off leaves it
        /// unarmed there. The Drills where it attacks or holds its blade for a Beat always arm it, whatever this says.
        /// </summary>
        public bool BladeInEveryDrill { get; set; } = true;

        /// <summary>Parry 6/4 at slow speed asks for a Riposte within 1 s of the Parry.</summary>
        public bool RiposteInSlowParries { get; set; } = true;

        /// <summary>Parry 6/4 at medium and real speed asks for a Riposte.</summary>
        public bool RiposteInFastParries { get; set; } = true;

        /// <summary>Parry 8/7 asks for a Riposte.</summary>
        public bool RiposteInLowParries { get; set; } = true;

        /// <summary>The Dummy lunges (PER-101) asks for a Riposte.</summary>
        public bool RiposteInLungeParries { get; set; } = true;

        /// <summary>
        /// How strong the Dummy's sword hand is against a press or bind (PER-122): how readily its blade turns in its grip
        /// and how readily its hand gives way. Missing from an older file: Medium.
        /// </summary>
        public OpponentHand OpponentHand { get; set; } = OpponentHand.Medium;

        public ArmedDummySettings Clone()
        {
            return new ArmedDummySettings
            {
                BladeInEveryDrill = BladeInEveryDrill,
                RiposteInSlowParries = RiposteInSlowParries,
                RiposteInFastParries = RiposteInFastParries,
                RiposteInLowParries = RiposteInLowParries,
                RiposteInLungeParries = RiposteInLungeParries,
                OpponentHand = OpponentHand,
            };
        }
    }

    /// <summary>How strong the Armed Dummy's sword hand is against the Fencer's blade (PER-122).</summary>
    public enum OpponentHand
    {
        /// <summary>Its blade turns in the hand readily and its hand gives way to a light press.</summary>
        Light,

        /// <summary>As firm as the Fencer's own grip.</summary>
        Medium,

        /// <summary>Its blade turns little and its hand holds against all but a hard press.</summary>
        Strong,
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

    /// <summary>
    /// How the Dummy is drawn (PER-102, Nikita's feel test): the Target Zone capsules thinner than the ones Touches are
    /// judged on, so they no longer cover the body, and the body more solid than before. Visual only: the Core
    /// <c>TargetZone</c> capsules keep their size, so Touch judging is the same at any value here.
    /// </summary>
    public sealed class DummyLookSettings
    {
        /// <summary>
        /// Draw the Target Zones while they are dim. Off hides dim zones only: a zone a Drill lights or a Touch flashes
        /// is always drawn, so cues and hits still show.
        /// </summary>
        public bool ShowTargetZones { get; set; } = true;

        /// <summary>Opacity of a dim Target Zone, 0 (invisible) to 1 (solid). Lit and flashing zones keep their own, brighter look.</summary>
        public float ZoneOpacity { get; set; } = 0.3f;

        /// <summary>Opacity of the Dummy's body, 0 (invisible) to 1 (solid). Was 0.15 before PER-102.</summary>
        public float BodyOpacity { get; set; } = 0.6f;

        /// <summary>Drawn Target Zone radius as a fraction of the radius Touches are judged on.</summary>
        public float ZoneThicknessFraction { get; set; } = 0.6f;

        public DummyLookSettings Clone()
        {
            return new DummyLookSettings
            {
                ShowTargetZones = ShowTargetZones,
                ZoneOpacity = ZoneOpacity,
                BodyOpacity = BodyOpacity,
                ZoneThicknessFraction = ZoneThicknessFraction,
            };
        }
    }
}
