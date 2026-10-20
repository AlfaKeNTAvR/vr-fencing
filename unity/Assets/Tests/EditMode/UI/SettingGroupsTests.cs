// The settings groups (PER-70): every Setting is in exactly one group, each group fits on one page, and the groups
// are the six Nikita chose, in his order.
using System;
using System.Linq;
using NUnit.Framework;
using VrFencing.UI;

namespace VrFencing.Tests.UI
{
    public class SettingGroupsTests
    {
        [Test]
        public void EverySettingIsInExactlyOneGroup()
        {
            foreach (SettingId id in Enum.GetValues(typeof(SettingId)))
            {
                Assert.DoesNotThrow(() => SettingGroups.GetGroup(id), $"{id} is in no settings group; add it to one in SettingGroups.");
            }

            var grouped = SettingGroups.All.SelectMany(SettingGroups.GetSettings).ToArray();
            Assert.That(grouped, Is.Unique, "A Setting is in two groups, or twice in one.");
            Assert.That(grouped, Is.EquivalentTo(SettingsPage.All));
        }

        [Test]
        public void EachSettingsGroupIsTheOneItIsListedIn()
        {
            foreach (var group in SettingGroups.All)
            {
                foreach (var id in SettingGroups.GetSettings(group))
                {
                    Assert.That(SettingGroups.GetGroup(id), Is.EqualTo(group));
                }
            }
        }

        [Test]
        public void EveryGroupHasALabelAndFitsOnOnePage()
        {
            foreach (var group in SettingGroups.All)
            {
                Assert.That(SettingGroups.GetLabel(group), Is.Not.Empty);
                Assert.That(SettingGroups.GetSettings(group), Is.Not.Empty);
                Assert.That(SettingGroups.GetSettings(group).Count, Is.LessThanOrEqualTo(SettingGroups.MaxSettingsPerGroup),
                    $"{group} would need a second page.");
            }
        }

        [Test]
        public void TheGroupsAreTheSixNikitaChoseInHisOrder()
        {
            Assert.That(SettingGroups.All.Select(SettingGroups.GetLabel), Is.EqualTo(new[] { "You", "Drills", "Strip", "Touch", "Blade", "Haptics" }));
            Assert.That(SettingGroups.GetSettings(SettingGroup.You), Is.EqualTo(new[] { SettingId.Hand, SettingId.FencerHeight, SettingId.LaserTilt }));
            Assert.That(SettingGroups.GetSettings(SettingGroup.Drills), Is.EqualTo(new[]
            {
                SettingId.RepsPerDrill, SettingId.RestBetweenReps, SettingId.ReactionWindow, SettingId.CompoundSecondTouchWindow,
                SettingId.DistanceChoiceWindow, SettingId.FootworkCallWindow, SettingId.LightNamedZones,
            }));

            // PER-92: "Shrink Drills to fit" moved to Strip to make room for "Light named zones".
            Assert.That(SettingGroups.GetSettings(SettingGroup.Strip), Is.EqualTo(new[]
            {
                SettingId.WarningZoneLengthOnPiste, SettingId.WarningZoneMinimumLength, SettingId.OffStripTolerance, SettingId.ShrinkDrillsToFit,
                SettingId.DoubleWindow,
            }));

            // PER-102: the Dummy's look joins Touch, the group about the Target Zones.
            Assert.That(SettingGroups.GetSettings(SettingGroup.Touch), Is.EqualTo(new[]
            {
                SettingId.TouchRule, SettingId.TouchPressure, SettingId.ShowTargetZones, SettingId.ZoneOpacity, SettingId.ZoneThickness,
                SettingId.BodyOpacity, SettingId.ParryRule,
            }));
            Assert.That(SettingGroups.GetSettings(SettingGroup.Blade), Is.EqualTo(new[]
            {
                SettingId.BladeMount, SettingId.BladePitch, SettingId.BladeYaw, SettingId.BladeSet, SettingId.BladeFlex,
            }));
            Assert.That(SettingGroups.GetSettings(SettingGroup.Haptics), Is.EqualTo(new[]
            {
                SettingId.TouchHaptic, SettingId.FlatHaptic, SettingId.GlanceHaptic, SettingId.HapticFloor, SettingId.HapticFullStrengthSpeed,
            }));
        }

        [Test]
        public void OnlyTheBladeGroupOpensTheGripCalibration()
        {
            Assert.That(SettingGroups.All.Where(SettingGroups.HasGripCalibration), Is.EqualTo(new[] { SettingGroup.Blade }));
        }
    }
}
