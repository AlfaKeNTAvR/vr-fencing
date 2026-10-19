using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VrFencing.Settings;
using VrFencing.Strip;

namespace VrFencing.UI
{
    /// <summary>Every Setting the settings screen shows, in screen order. One per value in <see cref="TrainerSettings"/>.</summary>
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
        WarningZoneLengthOnPiste,
        WarningZoneMinimumLength,
        OffStripTolerance,
        TouchRule,
        BladeMount,
        BladePitch,
        BladeYaw,
        TouchHaptic,
        FlatHaptic,
        GlanceHaptic,
        HapticFloor,
        HapticFullStrengthSpeed,
        LaserTilt,
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
    }

    /// <summary>
    /// The settings screen (PER-30 Settings): a label and value per Setting and a step down and up. Toggles flip on
    /// either step. Numbers move by a fixed step within a safe range, so the screen can never save a value that
    /// another module rejects. Changes return a new <see cref="TrainerSettings"/>; the one passed in is untouched.
    /// </summary>
    public static class SettingsPage
    {
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
            [SettingId.BladePitch] = new NumberSetting(-45.0f, 45.0f, 5.0f, "+0;-0;0", "deg",
                settings => settings.Blade.PitchDegrees, (settings, value) => settings.Blade.PitchDegrees = value),
            [SettingId.BladeYaw] = new NumberSetting(-45.0f, 45.0f, 5.0f, "+0;-0;0", "deg",
                settings => settings.Blade.YawDegrees, (settings, value) => settings.Blade.YawDegrees = value),
            [SettingId.HapticFloor] = new NumberSetting(0.0f, 1.0f, 0.1f, "0%", string.Empty,
                settings => settings.Blade.HapticFloor, (settings, value) => settings.Blade.HapticFloor = value),
            [SettingId.HapticFullStrengthSpeed] = new NumberSetting(0.5f, 6.0f, 0.5f, "0.0", "m/s",
                settings => settings.Blade.HapticFullStrengthSpeedMetersPerSecond,
                (settings, value) => settings.Blade.HapticFullStrengthSpeedMetersPerSecond = value),
            [SettingId.LaserTilt] = new NumberSetting(-30.0f, 60.0f, 5.0f, "+0;-0;0", "deg",
                settings => settings.LaserTiltDegrees, (settings, value) => settings.LaserTiltDegrees = value),
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
                case SettingId.WarningZoneLengthOnPiste: return "Warning Zone on a full piste";
                case SettingId.WarningZoneMinimumLength: return "Warning Zone at least";
                case SettingId.OffStripTolerance: return "Off-strip tolerance";
                case SettingId.TouchRule: return "Touch rule";
                case SettingId.BladeMount: return "Blade in the hand";
                case SettingId.BladePitch: return "Blade pitch (up +)";
                case SettingId.BladeYaw: return "Blade yaw (right +)";
                case SettingId.TouchHaptic: return "Haptic: Touch";
                case SettingId.FlatHaptic: return "Haptic: flat contact";
                case SettingId.GlanceHaptic: return "Haptic: glance";
                case SettingId.HapticFloor: return "Haptic strength when slow";
                case SettingId.HapticFullStrengthSpeed: return "Full haptic strength at";
                case SettingId.LaserTilt: return "Menu laser tilt (down +)";
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
                case SettingId.OffStripTolerance:
                    return FormatNumber(settings.WarningZone.OffStripToleranceMeters, "0.0", "m");
                case SettingId.TouchRule:
                    return settings.Blade.TouchRule == TouchRule.Strict ? "Strict" : "Loose";
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
                    return FormatNumber(number.Get(settings), number.Format, number.Unit);
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
                case SettingId.TouchRule:
                case SettingId.BladeMount:
                case SettingId.BladePitch:
                case SettingId.BladeYaw:
                case SettingId.TouchHaptic:
                case SettingId.FlatHaptic:
                case SettingId.GlanceHaptic:
                case SettingId.HapticFloor:
                case SettingId.HapticFullStrengthSpeed:
                    return SettingImpact.Blade;
                case SettingId.LaserTilt:
                    // The menu reads it every frame.
                    return SettingImpact.None;
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
                case SettingId.OffStripTolerance:
                    changed.WarningZone.OffStripToleranceMeters = StepTolerance(settings.WarningZone.OffStripToleranceMeters, direction);
                    break;
                case SettingId.TouchRule:
                    changed.Blade.TouchRule = Cycle(settings.Blade.TouchRule, direction);
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

        /// <summary>The next (or, stepping down, previous) value of a choice, wrapping around at both ends.</summary>
        private static T Cycle<T>(T current, int direction)
            where T : struct, Enum
        {
            var values = (T[])Enum.GetValues(typeof(T));
            var index = Array.IndexOf(values, current);
            var next = (index + Math.Sign(direction) + values.Length) % values.Length;
            return values[next];
        }

        private static string FormatMount(BladeMount mount)
        {
            switch (mount)
            {
                case BladeMount.Center: return "Center";
                case BladeMount.Pistol: return "Pistol +5 cm";
                case BladeMount.FrenchPosted: return "French +12 cm";
                default: throw new ArgumentOutOfRangeException(nameof(mount), mount, "Not a blade mount.");
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

            public NumberSetting(float min, float max, float step, string format, string unit, Func<TrainerSettings, float> get, Action<TrainerSettings, float> set)
            {
                _min = min;
                _max = max;
                _step = step;
                Format = format;
                Unit = unit;
                Get = get;
                _set = set;
            }

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
