namespace VrFencing.Settings
{
    /// <summary>
    /// How strictly the point must arrive for a Touch. There is no force sensing in VR, so the épée's "point pressed
    /// along its axis" is stood in for by direction limits (PER-22). The numbers behind each rule live in the Blade Core.
    /// </summary>
    public enum TouchRule
    {
        /// <summary>Motion within 70 degrees and blade within 75 degrees of straight in. The default until fast thrusts are re-tested on strict.</summary>
        Loose,

        /// <summary>Motion within 40 degrees and blade within 45 degrees of straight in.</summary>
        Strict,
    }

    /// <summary>Where the front of the guard sits along the sword-hand controller's forward axis.</summary>
    public enum BladeMount
    {
        /// <summary>Guard at the controller's center (the feel-test choice).</summary>
        Center,

        /// <summary>Guard 5 cm ahead, like a pistol grip.</summary>
        Pistol,

        /// <summary>Guard 12 cm ahead, like a French grip held posted.</summary>
        FrenchPosted,
    }

    /// <summary>
    /// A haptic pattern for one Blade event. Each event plays its own variant of the pattern (a flat contact's Heavy is
    /// softer and longer than a Touch's), as in the feel-test prototype.
    /// </summary>
    public enum HapticPattern
    {
        Off,

        /// <summary>One light, short tick.</summary>
        Tick,

        /// <summary>One short, sharp pulse.</summary>
        Crisp,

        /// <summary>One strong, longer, lower pulse.</summary>
        Heavy,

        /// <summary>Two pulses.</summary>
        DoubleTap,
    }

    /// <summary>
    /// The Blade's Settings. Defaults from the PER-22 feel tests: Center mount, pitch +10 degrees, yaw -10 degrees,
    /// Touch = Heavy, flat contact = Double tap, glance = Tick, loose Touch rule.
    /// Angles are for the right hand; the left hand mirrors the yaw.
    /// </summary>
    public sealed class BladeSettings
    {
        public TouchRule TouchRule { get; set; } = TouchRule.Loose;

        public BladeMount Mount { get; set; } = BladeMount.Center;

        /// <summary>Tips the blade up (positive) or down from the controller's forward axis.</summary>
        public float PitchDegrees { get; set; } = 10.0f;

        /// <summary>Turns the blade right (positive) or left, for a right-handed Fencer.</summary>
        public float YawDegrees { get; set; } = -10.0f;

        public HapticPattern TouchHaptic { get; set; } = HapticPattern.Heavy;

        public HapticPattern FlatHaptic { get; set; } = HapticPattern.DoubleTap;

        public HapticPattern GlanceHaptic { get; set; } = HapticPattern.Tick;

        /// <summary>
        /// Haptic strength at the slowest contact, as a fraction of full. Strength rises with tip speed from this floor
        /// to full at <see cref="HapticFullStrengthSpeedMetersPerSecond"/>, on top of the chosen pattern.
        /// </summary>
        public float HapticFloor { get; set; } = 0.4f;

        /// <summary>Tip speed at contact that gives full haptic strength.</summary>
        public float HapticFullStrengthSpeedMetersPerSecond { get; set; } = 2.0f;
    }
}
