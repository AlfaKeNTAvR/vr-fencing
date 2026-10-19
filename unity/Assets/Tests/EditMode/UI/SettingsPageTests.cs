// The settings screen's rules (PER-36): every Setting is on it, steps stay in range and on the step grid,
// and a change never touches the settings in use.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VrFencing.Drills;
using VrFencing.Dummy;
using VrFencing.Settings;
using VrFencing.Strip;
using VrFencing.Tests.Settings;
using VrFencing.UI;

namespace VrFencing.Tests.UI
{
    public class SettingsPageTests
    {
        [Test]
        public void EverySettingIsOnTheScreenExactlyOnce()
        {
            var defaults = new TrainerSettings();
            var before = SettingsLeaves.Read(defaults);
            var covered = new List<string>();

            foreach (var id in SettingsPage.All)
            {
                var after = SettingsLeaves.Read(SettingsPage.Change(defaults, id, 1));
                var changed = before.Keys.Where(path => !Equals(before[path], after[path])).ToArray();
                Assert.That(changed, Has.Length.EqualTo(1), $"{id} should change exactly one Setting.");
                covered.Add(changed[0]);
            }

            Assert.That(covered, Is.EquivalentTo(before.Keys), "A Setting is missing from the settings screen, or shown twice.");
        }

        [Test]
        public void EverySettingHasALabelAndAValue()
        {
            foreach (var id in SettingsPage.All)
            {
                Assert.That(SettingsPage.GetLabel(id), Is.Not.Empty);
                Assert.That(SettingsPage.FormatValue(new TrainerSettings(), id), Is.Not.Empty);
            }
        }

        [Test]
        public void ChangingReturnsACopyAndLeavesTheOriginalAlone()
        {
            var settings = new TrainerSettings();

            var changed = SettingsPage.Change(settings, SettingId.ReactionWindow, 1);

            Assert.That(changed, Is.Not.SameAs(settings));
            Assert.That(settings.TimeWindows.ReactionSeconds, Is.EqualTo(1.5f));
            Assert.That(changed.TimeWindows.ReactionSeconds, Is.EqualTo(1.6f).Within(1e-5f));
        }

        [Test]
        public void HeightStepsByOneCentimeterWithoutDrifting()
        {
            var settings = new TrainerSettings();
            for (var i = 0; i < 15; i++)
            {
                settings = SettingsPage.Change(settings, SettingId.FencerHeight, 1);
            }

            Assert.That(settings.FencerHeightMeters, Is.EqualTo(1.80f).Within(1e-5f));
            Assert.That(SettingsPage.FormatValue(settings, SettingId.FencerHeight), Is.EqualTo("1.80 m"));
        }

        [Test]
        public void NumbersStopAtTheEndsOfTheirRange()
        {
            var settings = new TrainerSettings();
            for (var i = 0; i < 100; i++)
            {
                settings = SettingsPage.Change(settings, SettingId.RepsPerDrill, -1);
            }

            Assert.That(settings.RepsPerDrill, Is.EqualTo(1));

            for (var i = 0; i < 100; i++)
            {
                settings = SettingsPage.Change(settings, SettingId.RestBetweenReps, 1);
            }

            Assert.That(settings.RestBetweenRepsSeconds, Is.EqualTo(10.0f));
        }

        [Test]
        public void TheLowestAndHighestValuesAreAcceptedByTheModulesThatUseThem()
        {
            foreach (var direction in new[] { -1, 1 })
            {
                var settings = new TrainerSettings();
                for (var i = 0; i < 200; i++)
                {
                    foreach (var id in SettingsPage.All.Where(id => id != SettingId.Hand && id != SettingId.ShrinkDrillsToFit))
                    {
                        settings = SettingsPage.Change(settings, id, direction);
                    }
                }

                Assert.DoesNotThrow(() => DummyLayout.Create(settings.FencerHeightMeters, settings.Hand));
                Assert.DoesNotThrow(() => StripGeometry.Create(StripGeometry.MinimumPlacedLengthMeters, settings.FencerHeightMeters, settings.WarningZone));
                Assert.DoesNotThrow(() => DrillCatalog.CreateDefault(includeTestDrills: true).BuildAll(settings));
            }
        }

        [Test]
        public void BladeChoicesCycleBothWaysAndShowReadableNames()
        {
            var settings = new TrainerSettings();

            Assert.That(SettingsPage.FormatValue(settings, SettingId.TouchRule), Is.EqualTo("Loose"));
            Assert.That(SettingsPage.Change(settings, SettingId.TouchRule, 1).Blade.TouchRule, Is.EqualTo(TouchRule.Strict));
            Assert.That(SettingsPage.Change(settings, SettingId.TouchRule, -1).Blade.TouchRule, Is.EqualTo(TouchRule.Strict));
            Assert.That(SettingsPage.FormatValue(settings, SettingId.FlatHaptic), Is.EqualTo("Double tap"));
            Assert.That(SettingsPage.Change(settings, SettingId.FlatHaptic, 1).Blade.FlatHaptic, Is.EqualTo(HapticPattern.Off));
            Assert.That(SettingsPage.Change(settings, SettingId.TouchHaptic, -1).Blade.TouchHaptic, Is.EqualTo(HapticPattern.Crisp));
            Assert.That(SettingsPage.FormatValue(settings, SettingId.BladeMount), Is.EqualTo("Center"));
            Assert.That(SettingsPage.FormatValue(settings, SettingId.BladePitch), Is.EqualTo("+10 deg"));
            Assert.That(SettingsPage.FormatValue(settings, SettingId.BladeYaw), Is.EqualTo("-10 deg"));
            Assert.That(SettingsPage.FormatValue(settings, SettingId.HapticFloor), Is.EqualTo("40%"));
            Assert.That(SettingsPage.Change(settings, SettingId.BladePitch, 1).Blade.PitchDegrees, Is.EqualTo(15.0f));
        }

        [Test]
        public void BladeSettingsGoToTheBladeAndTheHandMovesItToo()
        {
            Assert.That(SettingsPage.GetImpact(SettingId.TouchHaptic), Is.EqualTo(SettingImpact.Blade));
            Assert.That(SettingsPage.GetImpact(SettingId.BladeYaw), Is.EqualTo(SettingImpact.Blade));
            Assert.That(SettingsPage.GetImpact(SettingId.Hand) & SettingImpact.Blade, Is.EqualTo(SettingImpact.Blade));
        }

        [Test]
        public void TogglesFlipOnEitherStep()
        {
            var settings = new TrainerSettings();

            Assert.That(SettingsPage.Change(settings, SettingId.Hand, 1).Hand, Is.EqualTo(Hand.Left));
            Assert.That(SettingsPage.Change(settings, SettingId.Hand, -1).Hand, Is.EqualTo(Hand.Left));
            Assert.That(SettingsPage.Change(settings, SettingId.ShrinkDrillsToFit, -1).ShrinkDrillsToFit, Is.True);
            Assert.That(SettingsPage.FormatValue(settings, SettingId.Hand), Is.EqualTo("Right"));
            Assert.That(SettingsPage.FormatValue(settings, SettingId.ShrinkDrillsToFit), Is.EqualTo("Off"));
        }

        [Test]
        public void ToleranceCyclesThroughItsStepsBothWays()
        {
            var settings = new TrainerSettings();
            var up = new List<float>();
            var down = new List<float>();
            var current = settings;
            for (var i = 0; i < 3; i++)
            {
                current = SettingsPage.Change(current, SettingId.OffStripTolerance, 1);
                up.Add(current.WarningZone.OffStripToleranceMeters);
            }

            current = settings;
            for (var i = 0; i < 3; i++)
            {
                current = SettingsPage.Change(current, SettingId.OffStripTolerance, -1);
                down.Add(current.WarningZone.OffStripToleranceMeters);
            }

            Assert.That(up, Is.EqualTo(new[] { 0.2f, 0.4f, 0.0f }));
            Assert.That(down, Is.EqualTo(new[] { 0.4f, 0.2f, 0.0f }));
            Assert.That(OffStripToleranceSteps.StepsMeters, Is.EquivalentTo(new[] { 0.0f, 0.2f, 0.4f }));
        }

        [Test]
        public void HeightAndHandRebuildTheDummyAndTheStrip()
        {
            Assert.That(SettingsPage.GetImpact(SettingId.FencerHeight), Is.EqualTo(SettingImpact.Dummy | SettingImpact.Strip | SettingImpact.Drills));
            Assert.That(SettingsPage.GetImpact(SettingId.Hand) & SettingImpact.Dummy, Is.EqualTo(SettingImpact.Dummy));
            Assert.That(SettingsPage.GetImpact(SettingId.WarningZoneMinimumLength) & SettingImpact.Dummy, Is.EqualTo(SettingImpact.None));
            Assert.That(SettingsPage.GetImpact(SettingId.OffStripTolerance), Is.EqualTo(SettingImpact.Strip));
        }
    }
}
