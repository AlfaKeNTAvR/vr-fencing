using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VrFencing.Dummy;
using VrFencing.Settings;
using VrFencing.Strip;

namespace VrFencing.UI
{
    /// <summary>Every Setting the settings screen shows, each in one <see cref="SettingGroup"/>. One per value in <see cref="TrainerSettings"/>.</summary>
    public enum SettingId
    {
        Hand,
        FencerHeight,
        RepsPerDrill,
        RestBetweenReps,
        ReactionWindow,
        CompoundSecondTouchWindow,
        DistanceChoiceWindow,
        FootworkCallWindow,
        ShrinkDrillsToFit,
        LightNamedZones,
        WarningZoneLengthOnPiste,
        WarningZoneMinimumLength,
        OffStripTolerance,
        TouchRule,
        TouchPressure,
        BladeFlex,
        BladeMount,
        BladePitch,
        BladeYaw,
        BladeSet,
        TouchHaptic,
        FlatHaptic,
        GlanceHaptic,
        HapticFloor,
        HapticFullStrengthSpeed,
        LaserTilt,
        ShowTargetZones,
        ZoneOpacity,
        ZoneThickness,
        BodyOpacity,
        ParryRule,
        DoubleWindow,
    }

    /// <summary>What else must follow a changed Setting, beyond saving it.</summary>
    [Flags]
    public enum SettingImpact
    {
        None = 0,

        /// <summary>Rebuild the Dummy (height, sword hand).</summary>
        Dummy = 1,

        /// <summary>Rebuild the Strip geometry, warnings and off-hand buttons (height, hand, Warning Zone, tolerance).</summary>
        Strip = 2,

        /// <summary>Rebuild the Drill list: Rep count, rest, windows and Strip fit.</summary>
        Drills = 4,

        /// <summary>Hand the Blade its new Touch rule, mount, haptics or sword hand.</summary>
        Blade = 8,

        /// <summary>Redraw the Dummy's body and Target Zones with the new look (PER-102), without rebuilding it.</summary>
        DummyLook = 16,

        /// <summary>Hand the bout (PER-82) its rules: the Touch pressure and the double window, sent to the Partner when hosting.</summary>
        Bout = 32,
    }

    /// <summary>
    /// The settings screen (PER-30 Settings): a label and value per Setting and a step down and up. Toggles flip on
    /// either step. Numbers move by a fixed step within a safe range, so the screen can never save a value that
    /// another module rejects. Changes return a new <see cref="TrainerSettings"/>; the one passed in is untouched.
    /// </summary>
    public static class SettingsPage
    {
        /// <summary>The blade's pitch and yaw stay within this of the controller's forward axis, either way.</summary>
        public const float MaxAngleDegrees = 45.0f;

        // Ranges: wide enough for any adult Fencer and any Drill pace, narrow enough that every value is valid.
        private static readonly Dictionary<SettingId, NumberSetting> Numbers = new Dictionary<SettingId, NumberSetting>
        {
            [SettingId.FencerHeight] = new NumberSetting(1.20f, 2.20f, 0.01f, "0.00", "m",
                settings => settings.FencerHeightMeters, (settings, value) => settings.FencerHeightMeters = value),
            [SettingId.RepsPerDrill] = new NumberSetting(1.0f, 50.0f, 1.0f, "0", string.Empty,
                settings => settings.RepsPerDrill, (settings, value) => settings.RepsPerDrill = (int)Math.Round(value)),
            [SettingId.RestBetweenReps] = new NumberSetting(0.0f, 10.0f, 0.5f, "0.0", "s",
                settings => settings.RestBetweenRepsSeconds, (settings, value) => settings.RestBetweenRepsSeconds = value),
            [SettingId.ReactionWindow] = new NumberSetting(0.3f, 5.0f, 0.1f, "0.0", "s",
                settings => settings.TimeWindows.ReactionSeconds, (settings, value) => settings.TimeWindows.ReactionSeconds = value),
            [SettingId.CompoundSecondTouchWindow] = new NumberSetting(0.3f, 5.0f, 0.1f, "0.0", "s",
                settings => settings.TimeWindows.CompoundSecondTouchSeconds, (settings, value) => settings.TimeWindows.CompoundSecondTouchSeconds = value),
            [SettingId.DistanceChoiceWindow] = new NumberSetting(0.5f, 5.0f, 0.1f, "0.0", "s",
                settings => settings.TimeWindows.DistanceChoiceSeconds, (settings, value) => settings.TimeWindows.DistanceChoiceSeconds = value),
            [SettingId.FootworkCallWindow] = new NumberSetting(0.5f, 5.0f, 0.1f, "0.0", "s",
                settings => settings.TimeWindows.FootworkCallSeconds, (settings, value) => settings.TimeWindows.FootworkCallSeconds = value),
            [SettingId.WarningZoneLengthOnPiste] = new NumberSetting(0.5f, 3.0f, 0.1f, "0.0", "m",
                settings => settings.WarningZone.LengthOnPisteMeters, (settings, value) => settings.WarningZone.LengthOnPisteMeters = value),
            [SettingId.WarningZoneMinimumLength] = new NumberSetting(0.2f, 1.5f, 0.1f, "0.0", "m",
                settings => settings.WarningZone.MinimumLengthMeters, (settings, value) => settings.WarningZone.MinimumLengthMeters = value),
            // Pitch and yaw show the chosen grip's angles in use; a step saves that grip's own angle (PER-52).
            [SettingId.BladePitch] = new NumberSetting(-MaxAngleDegrees, MaxAngleDegrees, 1.0f, "+0;-0;0", "deg",
                settings => GripGeometry.GetActiveAngles(settings.Blade).PitchDegrees,
                (settings, value) => settings.Blade.SavedAngles.For(settings.Blade.Mount).PitchDegrees = value),
            [SettingId.BladeYaw] = new NumberSetting(-MaxAngleDegrees, MaxAngleDegrees, 1.0f, "+0;-0;0", "deg",
                settings => GripGeometry.GetActiveAngles(settings.Blade).YawDegrees,
                (settings, value) => settings.Blade.SavedAngles.For(settings.Blade.Mount).YawDegrees = value),
            [SettingId.BladeSet] = new NumberSetting(0.0f, 0.01f, 0.001f, "0.0", "cm",
                settings => settings.Blade.SetSagittaMeters, (settings, value) => settings.Blade.SetSagittaMeters = value, displayScale: 100.0f),
            [SettingId.HapticFloor] = new NumberSetting(0.0f, 1.0f, 0.1f, "0%", string.Empty,
                settings => settings.Blade.HapticFloor, (settings, value) => settings.Blade.HapticFloor = value),
            [SettingId.HapticFullStrengthSpeed] = new NumberSetting(0.5f, 6.0f, 0.5f, "0.0", "m/s",
                settings => settings.Blade.HapticFullStrengthSpeedMetersPerSecond,
                (settings, value) => settings.Blade.HapticFullStrengthSpeedMetersPerSecond = value),
            [SettingId.LaserTilt] = new NumberSetting(-30.0f, 60.0f, 5.0f, "+0;-0;0", "deg",
                settings => settings.LaserTiltDegrees, (settings, value) => settings.LaserTiltDegrees = value),
            // The Dummy's look (PER-102). The low ends keep a dim zone and the body faintly visible and a zone a thin line.
            [SettingId.ZoneOpacity] = new NumberSetting(0.1f, 1.0f, 0.1f, "0%", string.Empty,
                settings => settings.DummyLook.ZoneOpacity, (settings, value) => settings.DummyLook.ZoneOpacity = value),
            [SettingId.ZoneThickness] = new NumberSetting(0.2f, 1.0f, 0.1f, "0%", string.Empty,
                settings => settings.DummyLook.ZoneThicknessFraction, (settings, value) => settings.DummyLook.ZoneThicknessFraction = value),
            [SettingId.BodyOpacity] = new NumberSetting(0.1f, 1.0f, 0.1f, "0%", string.Empty,
                settings => settings.DummyLook.BodyOpacity, (settings, value) => settings.DummyLook.BodyOpacity = value),
            // The bout's double window when hosting (PER-56): the rule's 50 ms up to 150 ms.
            [SettingId.DoubleWindow] = new NumberSetting(0.05f, 0.15f, 0.025f, "0", "ms",
                settings => settings.BoutDoubleWindowSeconds, (settings, value) => settings.BoutDoubleWindowSeconds = value, displayScale: 1000.0f),
        };

        public static IReadOnlyList<SettingId> All { get; } = (SettingId[])Enum.GetValues(typeof(SettingId));

        public static string GetLabel(SettingId id)
        {
            switch (id)
            {
                case SettingId.Hand: return "Sword hand";
                case SettingId.FencerHeight: return "Your height";
                case SettingId.RepsPerDrill: return "Reps per Drill";
                case SettingId.RestBetweenReps: return "Rest between Reps";
                case SettingId.ReactionWindow: return "Reaction window";
                case SettingId.CompoundSecondTouchWindow: return "Compound: second Touch within";
                case SettingId.DistanceChoiceWindow: return "Distance choice window";
                case SettingId.FootworkCallWindow: return "Footwork call window";
                case SettingId.ShrinkDrillsToFit: return "Shrink Drills to fit the Strip";
                case SettingId.LightNamedZones: return "Light named zones (Drills 1, 2)";
                case SettingId.WarningZoneLengthOnPiste: return "Warning Zone on a full piste";
                case SettingId.WarningZoneMinimumLength: return "Warning Zone at least";
                case SettingId.OffStripTolerance: return "Off-strip tolerance";
                case SettingId.TouchRule: return "Touch rule";
                case SettingId.TouchPressure: return "Touch pressure";
                case SettingId.BladeFlex: return "Blade flex";
                case SettingId.BladeMount: return "Grip";
                case SettingId.BladePitch: return "Blade pitch (up +)";
                case SettingId.BladeYaw: return "Blade yaw (right +)";
                case SettingId.BladeSet: return "Blade set (curve)";
                case SettingId.TouchHaptic: return "Haptic: Touch";
                case SettingId.FlatHaptic: return "Haptic: flat contact";
                case SettingId.GlanceHaptic: return "Haptic: glance";
                case SettingId.HapticFloor: return "Haptic strength when slow";
                case SettingId.HapticFullStrengthSpeed: return "Full haptic strength at";
                case SettingId.LaserTilt: return "Menu laser tilt (down +)";
                case SettingId.ShowTargetZones: return "Show Target Zones";
                case SettingId.ZoneOpacity: return "Zone opacity";
                case SettingId.ZoneThickness: return "Zone thickness (of scoring)";
                case SettingId.BodyOpacity: return "Dummy body opacity";
                case SettingId.ParryRule: return "Parry rule";
                case SettingId.DoubleWindow: return "Bout double window (host)";
                default: throw new ArgumentOutOfRangeException(nameof(id), id, "Not a Setting.");
            }
        }

        public static string FormatValue(TrainerSettings settings, SettingId id)
        {
            switch (id)
            {
                case SettingId.Hand:
                    return settings.Hand == Hand.Left ? "Left" : "Right";
                case SettingId.ShrinkDrillsToFit:
                    return settings.ShrinkDrillsToFit ? "On" : "Off";
                case SettingId.LightNamedZones:
                    return settings.LightNamedZones ? "On" : "Off";
                case SettingId.ShowTargetZones:
                    return settings.DummyLook.ShowTargetZones ? "On" : "Off";
                case SettingId.OffStripTolerance:
                    return FormatNumber(settings.WarningZone.OffStripToleranceMeters, "0.0", "m");
                case SettingId.TouchRule:
                    return settings.Blade.TouchRule == TouchRule.Strict ? "Strict" : "Loose";
                case SettingId.ParryRule:
                    return settings.Blade.ParryRule == ParryRule.Strict ? "Strict (forte on foible)" : "Loose";
                case SettingId.TouchPressure:
                    return settings.Blade.TouchPressure == TouchPressure.OnContact ? "On contact" : "Realistic (750 g)";
                case SettingId.BladeFlex:
                    return settings.Blade.Flex.ToString();
                case SettingId.BladeMount:
                    return FormatMount(settings.Blade.Mount);
                case SettingId.TouchHaptic:
                    return FormatHaptic(settings.Blade.TouchHaptic);
                case SettingId.FlatHaptic:
                    return FormatHaptic(settings.Blade.FlatHaptic);
                case SettingId.GlanceHaptic:
                    return FormatHaptic(settings.Blade.GlanceHaptic);
                default:
                    var number = Numbers[id];
                    return FormatNumber(number.Get(settings) * number.DisplayScale, number.Format, number.Unit);
            }
        }

        public static SettingImpact GetImpact(SettingId id)
        {
            switch (id)
            {
                case SettingId.Hand:
                    // The blade also moves to the other controller and mirrors its yaw.
                    return SettingImpact.Dummy | SettingImpact.Strip | SettingImpact.Drills | SettingImpact.Blade;
                case SettingId.FencerHeight:
                    // Height also sets the Strip's stance allowance behind the En Garde Line.
                    return SettingImpact.Dummy | SettingImpact.Strip | SettingImpact.Drills;
                case SettingId.WarningZoneLengthOnPiste:
                case SettingId.WarningZoneMinimumLength:
                    return SettingImpact.Strip | SettingImpact.Drills;
                case SettingId.OffStripTolerance:
                    return SettingImpact.Strip;
                case SettingId.BladeMount:
                    // Each grip keeps its own measured reach, which sets the Dummy's distance (PER-92).
                    return SettingImpact.Blade | SettingImpact.Dummy;
                case SettingId.TouchPressure:
                    // The Host's Touch pressure is the bout's rule for both Fencers (PER-56).
                    return SettingImpact.Blade | SettingImpact.Bout;
                case SettingId.DoubleWindow:
                    return SettingImpact.Bout;
                case SettingId.TouchRule:
                case SettingId.ParryRule:
                case SettingId.BladeFlex:
                case SettingId.BladePitch:
                case SettingId.BladeYaw:
                case SettingId.BladeSet:
                case SettingId.TouchHaptic:
                case SettingId.FlatHaptic:
                case SettingId.GlanceHaptic:
                case SettingId.HapticFloor:
                case SettingId.HapticFullStrengthSpeed:
                    return SettingImpact.Blade;
                case SettingId.LaserTilt:
                    // The menu reads it every frame.
                    return SettingImpact.None;
                case SettingId.ShowTargetZones:
                case SettingId.ZoneOpacity:
                case SettingId.ZoneThickness:
                case SettingId.BodyOpacity:
                    return SettingImpact.DummyLook;
                default:
                    return SettingImpact.Drills;
            }
        }

        /// <summary>The settings with <paramref name="id"/> stepped down (<paramref name="direction"/> below 0) or up (above 0).</summary>
        /// <exception cref="ArgumentException"><paramref name="direction"/> is 0.</exception>
        public static TrainerSettings Change(TrainerSettings settings, SettingId id, int direction)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (direction == 0)
            {
                throw new ArgumentException("Step down (negative) or up (positive).", nameof(direction));
            }

            var changed = settings.Clone();
            switch (id)
            {
                case SettingId.Hand:
                    changed.Hand = settings.Hand == Hand.Left ? Hand.Right : Hand.Left;
                    break;
                case SettingId.ShrinkDrillsToFit:
                    changed.ShrinkDrillsToFit = !settings.ShrinkDrillsToFit;
                    break;
                case SettingId.LightNamedZones:
                    changed.LightNamedZones = !settings.LightNamedZones;
                    break;
                case SettingId.ShowTargetZones:
                    changed.DummyLook.ShowTargetZones = !settings.DummyLook.ShowTargetZones;
                    break;
                case SettingId.OffStripTolerance:
                    changed.WarningZone.OffStripToleranceMeters = StepTolerance(settings.WarningZone.OffStripToleranceMeters, direction);
                    break;
                case SettingId.TouchRule:
                    changed.Blade.TouchRule = Cycle(settings.Blade.TouchRule, direction);
                    break;
                case SettingId.ParryRule:
                    changed.Blade.ParryRule = Cycle(settings.Blade.ParryRule, direction);
                    break;
                case SettingId.TouchPressure:
                    changed.Blade.TouchPressure = Cycle(settings.Blade.TouchPressure, direction);
                    break;
                case SettingId.BladeFlex:
                    changed.Blade.Flex = Cycle(settings.Blade.Flex, direction);
                    break;
                case SettingId.BladeMount:
                    changed.Blade.Mount = Cycle(settings.Blade.Mount, direction);
                    break;
                case SettingId.TouchHaptic:
                    changed.Blade.TouchHaptic = Cycle(settings.Blade.TouchHaptic, direction);
                    break;
                case SettingId.FlatHaptic:
                    changed.Blade.FlatHaptic = Cycle(settings.Blade.FlatHaptic, direction);
                    break;
                case SettingId.GlanceHaptic:
                    changed.Blade.GlanceHaptic = Cycle(settings.Blade.GlanceHaptic, direction);
                    break;
                default:
                    Numbers[id].Step(changed, Math.Sign(direction));
                    break;
            }

            return changed;
        }

        /// <summary>
        /// The settings with the chosen grip's own pitch and yaw saved (from the thrust calibration), rounded to a whole
        /// degree and kept within the pitch and yaw steps' range, and the reach it measured (PER-92). A reach that is
        /// missing or implausible (<see cref="DistancePresets.IsPlausibleReach"/>) leaves the grip's saved reach as it was.
        /// </summary>
        public static TrainerSettings SaveGripAngles(TrainerSettings settings, GripAngles angles, float? reachMeters = null)
        {
            var changed = settings.Clone();
            var saved = changed.Blade.SavedAngles.For(changed.Blade.Mount);
            saved.PitchDegrees = ClampAngle(angles.PitchDegrees);
            saved.YawDegrees = ClampAngle(angles.YawDegrees);
            if (reachMeters.HasValue && DistancePresets.IsPlausibleReach(reachMeters.Value))
            {
                saved.ReachMeters = (float)Math.Round(reachMeters.Value, 3);
            }

            return changed;
        }

        /// <summary>
        /// The settings with the chosen grip back on its computed angles (its saved ones cleared). The measured reach is
        /// kept: it is the Fencer's arm, not the blade's angle.
        /// </summary>
        public static TrainerSettings ResetGripAngles(TrainerSettings settings)
        {
            var changed = settings.Clone();
            var saved = changed.Blade.SavedAngles.For(changed.Blade.Mount);
            saved.PitchDegrees = null;
            saved.YawDegrees = null;
            return changed;
        }

        private static float ClampAngle(float degrees)
        {
            return Math.Min(MaxAngleDegrees, Math.Max(-MaxAngleDegrees, (float)Math.Round(degrees)));
        }

        /// <summary>The next (or, stepping down, previous) value of a choice, wrapping around at both ends.</summary>
        private static T Cycle<T>(T current, int direction)
            where T : struct, Enum
        {
            var values = (T[])Enum.GetValues(typeof(T));
            var index = Array.IndexOf(values, current);
            var next = (index + Math.Sign(direction) + values.Length) % values.Length;
            return values[next];
        }

        /// <summary>The grip's name as the settings and calibration screens show it.</summary>
        public static string FormatMount(BladeMount mount)
        {
            switch (mount)
            {
                case BladeMount.Center: return "Center";
                case BladeMount.Pistol: return "Pistol";
                case BladeMount.French: return "French";
                case BladeMount.FrenchPosted: return "French posted";
                default: throw new ArgumentOutOfRangeException(nameof(mount), mount, "Not a grip.");
            }
        }

        private static string FormatHaptic(HapticPattern pattern)
        {
            return pattern == HapticPattern.DoubleTap ? "Double tap" : pattern.ToString();
        }

        /// <summary>The tolerance steps (0, 0.2, 0.4 m) wrap around in both directions, like the off-hand button.</summary>
        private static float StepTolerance(float currentMeters, int direction)
        {
            if (direction > 0)
            {
                return OffStripToleranceSteps.GetNext(currentMeters);
            }

            var steps = OffStripToleranceSteps.StepsMeters;
            var below = steps.Where(step => step < currentMeters - 0.001f).ToArray();
            return below.Length > 0 ? below.Max() : steps.Max();
        }

        private static string FormatNumber(float value, string format, string unit)
        {
            var text = value.ToString(format, CultureInfo.InvariantCulture);
            return string.IsNullOrEmpty(unit) ? text : text + " " + unit;
        }

        private sealed class NumberSetting
        {
            private readonly float _min;
            private readonly float _max;
            private readonly float _step;
            private readonly Action<TrainerSettings, float> _set;

            /// <param name="displayScale">Shown as the value times this, e.g. 100 to show meters in centimeters.</param>
            public NumberSetting(
                float min, float max, float step, string format, string unit, Func<TrainerSettings, float> get, Action<TrainerSettings, float> set,
                float displayScale = 1.0f)
            {
                _min = min;
                _max = max;
                _step = step;
                Format = format;
                Unit = unit;
                Get = get;
                _set = set;
                DisplayScale = displayScale;
            }

            public float DisplayScale { get; }

            public string Format { get; }

            public string Unit { get; }

            public Func<TrainerSettings, float> Get { get; }

            /// <summary>Moves one step, snapped to the step grid so repeated presses never drift, and kept in range.</summary>
            public void Step(TrainerSettings settings, int sign)
            {
                var stepsFromMin = Math.Round((Get(settings) - _min) / _step) + sign;
                var value = (float)Math.Round(_min + (stepsFromMin * _step), 4);
                _set(settings, Math.Min(Math.Max(value, _min), _max));
            }
        }
    }
}
