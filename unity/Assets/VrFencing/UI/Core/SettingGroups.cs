using System;
using System.Collections.Generic;
using System.Linq;

namespace VrFencing.UI
{
    /// <summary>The groups the settings screen opens on (PER-70), in the order it lists them.</summary>
    public enum SettingGroup
    {
        You,
        Drills,
        Strip,
        Touch,
        Blade,
        Haptics,
    }

    /// <summary>
    /// Which group each Setting is in (PER-70, groups chosen by Nikita). The settings screen opens on the list of groups;
    /// picking one shows its Settings, in the order here. Every Setting is in exactly one group: a Setting added
    /// without one fails <see cref="GetGroup"/>, and the Edit-mode tests call it for every <see cref="SettingId"/>.
    /// The Blade group also holds the "Calibrate grip" entry (<see cref="HasGripCalibration"/>).
    /// </summary>
    public static class SettingGroups
    {
        /// <summary>A group shows at most this many Settings, so it fits on one page of the settings screen.</summary>
        public const int MaxSettingsPerGroup = 7;

        private static readonly Dictionary<SettingGroup, SettingId[]> Members = new Dictionary<SettingGroup, SettingId[]>
        {
            [SettingGroup.You] = new[] { SettingId.Hand, SettingId.FencerHeight, SettingId.LaserTilt },
            [SettingGroup.Drills] = new[]
            {
                SettingId.RepsPerDrill, SettingId.RestBetweenReps, SettingId.ReactionWindow, SettingId.CompoundSecondTouchWindow,
                SettingId.DistanceChoiceWindow, SettingId.FootworkCallWindow, SettingId.LightNamedZones,
            },

            // "Shrink Drills to fit" moved here from Drills (PER-92) to make room for "Light named zones": it is about the Strip's length.
            // The bout's double window (PER-82) is here because Touch is full (7): the Strip group is the one about the shared bout.
            [SettingGroup.Strip] = new[]
            {
                SettingId.WarningZoneLengthOnPiste, SettingId.WarningZoneMinimumLength, SettingId.OffStripTolerance, SettingId.ShrinkDrillsToFit,
                SettingId.DoubleWindow,
            },

            // The Dummy's look (PER-102) is here: Touch is the group about the Target Zones, and it has room for all four.
            [SettingGroup.Touch] = new[]
            {
                SettingId.TouchRule, SettingId.TouchPressure, SettingId.ShowTargetZones, SettingId.ZoneOpacity, SettingId.ZoneThickness,
                SettingId.BodyOpacity, SettingId.ParryRule,
            },
            [SettingGroup.Blade] = new[] { SettingId.BladeMount, SettingId.BladePitch, SettingId.BladeYaw, SettingId.BladeSet, SettingId.BladeFlex },
            // The Armed Dummy's clash buzz (PER-99) fills the group to 7; the hit-on-Fencer buzz is fixed, with no Setting.
            [SettingGroup.Haptics] = new[]
            {
                SettingId.TouchHaptic, SettingId.FlatHaptic, SettingId.GlanceHaptic, SettingId.HapticFloor, SettingId.HapticFullStrengthSpeed,
                SettingId.ClashHaptic, SettingId.ClashHapticFloor,
            },
        };

        public static IReadOnlyList<SettingGroup> All { get; } = (SettingGroup[])Enum.GetValues(typeof(SettingGroup));

        public static string GetLabel(SettingGroup group)
        {
            switch (group)
            {
                case SettingGroup.You: return "You";
                case SettingGroup.Drills: return "Drills";
                case SettingGroup.Strip: return "Strip";
                case SettingGroup.Touch: return "Touch";
                case SettingGroup.Blade: return "Blade";
                case SettingGroup.Haptics: return "Haptics";
                default: throw new ArgumentOutOfRangeException(nameof(group), group, "Not a settings group.");
            }
        }

        /// <summary>The group's Settings, in screen order.</summary>
        public static IReadOnlyList<SettingId> GetSettings(SettingGroup group)
        {
            if (!Members.TryGetValue(group, out var settings))
            {
                throw new ArgumentOutOfRangeException(nameof(group), group, "Not a settings group.");
            }

            return settings;
        }

        /// <summary>The group <paramref name="id"/> is in.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The Setting is in no group.</exception>
        public static SettingGroup GetGroup(SettingId id)
        {
            foreach (var pair in Members.Where(pair => pair.Value.Contains(id)))
            {
                return pair.Key;
            }

            throw new ArgumentOutOfRangeException(nameof(id), id, "This Setting is in no settings group; add it to one in SettingGroups.");
        }

        /// <summary>The group's page also opens the grip page with its angles and the thrust calibration (PER-52).</summary>
        public static bool HasGripCalibration(SettingGroup group)
        {
            return group == SettingGroup.Blade;
        }
    }
}
