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

    /// <summary>
    /// What blade-on-blade contact counts as a Parry of the Armed Dummy's attack (PER-23, PER-97). Either way the contact
    /// must deflect the Dummy's point off the Fencer's body; the numbers live in the Blade Core (<c>ParryJudge</c>).
    /// </summary>
    public enum ParryRule
    {
        /// <summary>Any deflecting contact. The default while blade-on-blade contact is new on the headset.</summary>
        Loose,

        /// <summary>The deflecting contact must also be the Fencer's forte on the Dummy's foible.</summary>
        Strict,
    }

    /// <summary>
    /// How hard the point must press for a Touch (PER-50). A real épée scores only with at least 750 g (about 7.4 N) on
    /// the point; the numbers that stand in for it live in the Blade Core (<c>TouchRules</c>).
    /// </summary>
    public enum TouchPressure
    {
        /// <summary>The point must be pressed in (or arrive fast enough to press it in) as with 750 g: a light brush does not score.</summary>
        Realistic,

        /// <summary>The first contact scores, as before PER-50.</summary>
        OnContact,
    }

    /// <summary>How lively the drawn blade's flex is (PER-50): its first bending mode. Visual only.</summary>
    public enum BladeFlexSoftness
    {
        /// <summary>11 Hz, well damped: a small whip that settles quickly.</summary>
        Stiff,

        /// <summary>8 Hz, lightly damped: about a real épée's first bending mode, ringing a few visible cycles.</summary>
        Normal,

        /// <summary>6 Hz, very lightly damped: livelier than a real blade.</summary>
        Soft,
    }

    /// <summary>
    /// The grip the virtual épée has in the hand (PER-51, PER-52). Each sets how far ahead of the controller the front of
    /// the guard sits on the blade's line, and the computed pitch and yaw (<see cref="GripGeometry"/>).
    /// </summary>
    public enum BladeMount
    {
        /// <summary>Guard at the controller's center, the blade on the forearm line.</summary>
        Center,

        /// <summary>A pistol grip (Visconti, Belgian): guard 5.5 cm ahead, the hand right behind it.</summary>
        Pistol,

        /// <summary>A French grip held normally: guard 7.5 cm ahead, canted slightly down and inward.</summary>
        French,

        /// <summary>A French grip held posted (the hand slid back toward the pommel): guard 13 cm ahead.</summary>
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
    /// The Blade's Settings. Defaults from the PER-22 feel tests: Touch = Heavy, flat contact = Double tap, glance = Tick,
    /// loose Touch rule; PER-50: realistic Touch pressure, normal blade flex; PER-52: Center grip at its computed pitch
    /// and yaw (nothing saved), a 0.7 cm set.
    /// Angles are for the right hand; the left hand mirrors the yaw.
    /// </summary>
    public sealed class BladeSettings
    {
        public TouchRule TouchRule { get; set; } = TouchRule.Loose;

        public TouchPressure TouchPressure { get; set; } = TouchPressure.Realistic;

        /// <summary>What counts as a Parry of the Armed Dummy's attack (PER-97). Loose until strict is tried on the headset.</summary>
        public ParryRule ParryRule { get; set; } = ParryRule.Loose;

        public BladeFlexSoftness Flex { get; set; } = BladeFlexSoftness.Normal;

        /// <summary>The grip: where the guard sits and the computed angles (<see cref="GripGeometry"/>).</summary>
        public BladeMount Mount { get; set; } = BladeMount.Center;

        /// <summary>
        /// The Fencer's own pitch and yaw per grip, from the thrust calibration or stepped on the settings screen. An
        /// angle left unset uses the grip's computed one.
        /// </summary>
        public SavedGripAngles SavedAngles { get; set; } = new SavedGripAngles();

        /// <summary>
        /// The blade's set: its deliberate downward curve, as the gap under the blade's middle on a flat surface (FIE
        /// m.16.2: under 1 cm). It drops the point 4 times this below the forte's line.
        /// </summary>
        public float SetSagittaMeters { get; set; } = 0.007f;

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

        /// <summary>
        /// The clash buzz when the Fencer's blade meets the Armed Dummy's (PER-99). Crisp is the prototype's ring (PER-95),
        /// the default until its feel test picks another.
        /// </summary>
        public HapticPattern ClashHaptic { get; set; } = HapticPattern.Crisp;

        /// <summary>
        /// Clash buzz strength at the lightest blade contact, as a fraction of full; it rises with how hard the blades met
        /// (PER-23). 35% is the prototype's (PER-95).
        /// </summary>
        public float ClashHapticFloor { get; set; } = 0.35f;

        /// <summary>The reach Calibrate grip measured with the chosen grip, or null when it has not been measured yet.</summary>
        public float? GetMeasuredReachMeters()
        {
            return SavedAngles.For(Mount).ReachMeters;
        }

        /// <summary>A deep copy.</summary>
        public BladeSettings Clone()
        {
            return new BladeSettings
            {
                TouchRule = TouchRule,
                TouchPressure = TouchPressure,
                ParryRule = ParryRule,
                Flex = Flex,
                Mount = Mount,
                SavedAngles = SavedAngles.Clone(),
                SetSagittaMeters = SetSagittaMeters,
                TouchHaptic = TouchHaptic,
                FlatHaptic = FlatHaptic,
                GlanceHaptic = GlanceHaptic,
                HapticFloor = HapticFloor,
                HapticFullStrengthSpeedMetersPerSecond = HapticFullStrengthSpeedMetersPerSecond,
                ClashHaptic = ClashHaptic,
                ClashHapticFloor = ClashHapticFloor,
            };
        }
    }

    /// <summary>
    /// One grip's saved calibration: pitch and yaw (right hand, degrees), and the reach Calibrate grip measured with it.
    /// An angle left null uses the grip's computed one; a reach left null uses the one estimated from height.
    /// </summary>
    public sealed class GripAngleOverride
    {
        /// <summary>Tips the blade up (positive) or down from the controller's forward axis.</summary>
        public float? PitchDegrees { get; set; }

        /// <summary>Turns the blade right (positive) or left, for a right-handed Fencer.</summary>
        public float? YawDegrees { get; set; }

        /// <summary>
        /// The Fencer's reach with this grip (PER-92): from the shoulder to the point with the arm straight, measured on
        /// the thrust targets of Calibrate grip. It sets the Dummy's Extension distance. Null until measured (and in
        /// settings files from before PER-92), which keeps the height-based distance. Not on the settings screen.
        /// </summary>
        public float? ReachMeters { get; set; }

        public GripAngleOverride Clone()
        {
            return new GripAngleOverride { PitchDegrees = PitchDegrees, YawDegrees = YawDegrees, ReachMeters = ReachMeters };
        }
    }

    /// <summary>The Fencer's saved angles, one slot per grip, so changing grip keeps each grip's own calibration.</summary>
    public sealed class SavedGripAngles
    {
        public GripAngleOverride Center { get; set; } = new GripAngleOverride();

        public GripAngleOverride Pistol { get; set; } = new GripAngleOverride();

        public GripAngleOverride French { get; set; } = new GripAngleOverride();

        public GripAngleOverride FrenchPosted { get; set; } = new GripAngleOverride();

        /// <summary>The slot for <paramref name="mount"/>, to read or to change in place.</summary>
        public GripAngleOverride For(BladeMount mount)
        {
            switch (mount)
            {
                case BladeMount.Center: return Center;
                case BladeMount.Pistol: return Pistol;
                case BladeMount.French: return French;
                case BladeMount.FrenchPosted: return FrenchPosted;
                default: throw new System.ArgumentOutOfRangeException(nameof(mount), mount, "Not a grip.");
            }
        }

        public SavedGripAngles Clone()
        {
            return new SavedGripAngles
            {
                Center = Center.Clone(),
                Pistol = Pistol.Clone(),
                French = French.Clone(),
                FrenchPosted = FrenchPosted.Clone(),
            };
        }
    }
}
