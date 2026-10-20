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

            // Pitch and yaw save the chosen grip's own angles: the other grips' are reached by choosing that grip first.
            foreach (var mount in new[] { BladeMount.Pistol, BladeMount.French, BladeMount.FrenchPosted })
            {
                var onGrip = defaults.Clone();
                onGrip.Blade.Mount = mount;
                var onGripLeaves = SettingsLeaves.Read(onGrip);
                foreach (var id in new[] { SettingId.BladePitch, SettingId.BladeYaw })
                {
                    var after = SettingsLeaves.Read(SettingsPage.Change(onGrip, id, 1));
                    var changed = onGripLeaves.Keys.Where(path => !Equals(onGripLeaves[path], after[path])).ToArray();
                    Assert.That(changed, Has.Length.EqualTo(1), $"{id} on {mount} should change exactly one Setting.");
                    covered.Add(changed[0]);
                }
            }

            // The reach is measured by Calibrate grip (PER-92), not stepped on the settings screen.
            var onScreen = before.Keys.Where(path => !path.EndsWith(".ReachMeters")).ToArray();
            Assert.That(covered, Is.EquivalentTo(onScreen), "A Setting is missing from the settings screen, or shown twice.");
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
                    foreach (var id in SettingsPage.All.Where(id => id != SettingId.Hand && id != SettingId.ShrinkDrillsToFit && id != SettingId.LightNamedZones))
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
            Assert.That(SettingsPage.FormatValue(settings, SettingId.ParryRule), Is.EqualTo("Loose"));
            Assert.That(SettingsPage.Change(settings, SettingId.ParryRule, 1).Blade.ParryRule, Is.EqualTo(ParryRule.Strict));
            Assert.That(SettingsPage.FormatValue(SettingsPage.Change(settings, SettingId.ParryRule, 1), SettingId.ParryRule), Is.EqualTo("Strict (forte on foible)"));
            Assert.That(SettingsPage.GetImpact(SettingId.ParryRule), Is.EqualTo(SettingImpact.Blade));
            Assert.That(SettingsPage.FormatValue(settings, SettingId.FlatHaptic), Is.EqualTo("Double tap"));
            Assert.That(SettingsPage.Change(settings, SettingId.FlatHaptic, 1).Blade.FlatHaptic, Is.EqualTo(HapticPattern.Off));
            Assert.That(SettingsPage.Change(settings, SettingId.TouchHaptic, -1).Blade.TouchHaptic, Is.EqualTo(HapticPattern.Crisp));
            Assert.That(SettingsPage.FormatValue(settings, SettingId.BladeMount), Is.EqualTo("Center"));
            Assert.That(SettingsPage.FormatValue(settings, SettingId.BladePitch), Is.EqualTo("-10 deg"));
            Assert.That(SettingsPage.FormatValue(settings, SettingId.BladeYaw), Is.EqualTo("0 deg"));
            Assert.That(SettingsPage.FormatValue(settings, SettingId.BladeSet), Is.EqualTo("0.7 cm"));
            Assert.That(SettingsPage.FormatValue(settings, SettingId.HapticFloor), Is.EqualTo("40%"));
            Assert.That(SettingsPage.FormatValue(settings, SettingId.ClashHaptic), Is.EqualTo("Crisp"));
            Assert.That(SettingsPage.Change(settings, SettingId.ClashHaptic, 1).Blade.ClashHaptic, Is.EqualTo(HapticPattern.Heavy));
            Assert.That(SettingsPage.GetImpact(SettingId.ClashHaptic), Is.EqualTo(SettingImpact.Blade));
            Assert.That(SettingsPage.FormatValue(settings, SettingId.ClashHapticFloor), Is.EqualTo("35%"));
            Assert.That(SettingsPage.Change(settings, SettingId.ClashHapticFloor, 1).Blade.ClashHapticFloor, Is.EqualTo(0.4f).Within(1e-6f));
            Assert.That(SettingsPage.GetImpact(SettingId.ClashHapticFloor), Is.EqualTo(SettingImpact.Blade));
            Assert.That(SettingsPage.Change(settings, SettingId.BladePitch, 1).Blade.SavedAngles.Center.PitchDegrees, Is.EqualTo(-9.0f));
            Assert.That(SettingsPage.Change(settings, SettingId.BladeSet, 1).Blade.SetSagittaMeters, Is.EqualTo(0.008f).Within(1e-6f));
        }

        [Test]
        public void TheGripCyclesThroughAllFourAndShowsItsComputedAngles()
        {
            var settings = new TrainerSettings();
            var names = new List<string>();
            var pitches = new List<string>();
            for (var i = 0; i < 4; i++)
            {
                settings = SettingsPage.Change(settings, SettingId.BladeMount, 1);
                names.Add(SettingsPage.FormatValue(settings, SettingId.BladeMount));
                pitches.Add(SettingsPage.FormatValue(settings, SettingId.BladePitch) + " / " + SettingsPage.FormatValue(settings, SettingId.BladeYaw));
            }

            Assert.That(names, Is.EqualTo(new[] { "Pistol", "French", "French posted", "Center" }));
            Assert.That(pitches, Is.EqualTo(new[] { "-12 deg / -3 deg", "-13 deg / -4 deg", "-13 deg / -4 deg", "-10 deg / 0 deg" }));
        }

        [Test]
        public void SavingAndResettingTheGripsAnglesOnlyTouchesTheChosenGrip()
        {
            var settings = new TrainerSettings();
            settings.Blade.Mount = BladeMount.French;
            settings.Blade.SavedAngles.Pistol.PitchDegrees = 4.0f;

            var calibrated = SettingsPage.SaveGripAngles(settings, new GripAngles(-11.4f, 60.0f));

            Assert.That(calibrated.Blade.SavedAngles.French.PitchDegrees, Is.EqualTo(-11.0f), "Rounded to a degree.");
            Assert.That(calibrated.Blade.SavedAngles.French.YawDegrees, Is.EqualTo(SettingsPage.MaxAngleDegrees), "Kept in range.");
            Assert.That(settings.Blade.SavedAngles.French.PitchDegrees, Is.Null, "The settings passed in are untouched.");

            var reset = SettingsPage.ResetGripAngles(calibrated);

            Assert.That(GripGeometry.GetActiveAngles(reset.Blade).PitchDegrees, Is.EqualTo(-13.0f));
            Assert.That(GripGeometry.GetActiveAngles(reset.Blade).YawDegrees, Is.EqualTo(-4.0f));
            Assert.That(reset.Blade.SavedAngles.Pistol.PitchDegrees, Is.EqualTo(4.0f), "Another grip keeps its own angles.");
        }

        [Test]
        public void TouchPressureAndBladeFlexAreChoices()
        {
            var settings = new TrainerSettings();

            Assert.That(SettingsPage.FormatValue(settings, SettingId.TouchPressure), Is.EqualTo("Realistic (750 g)"));
            var onContact = SettingsPage.Change(settings, SettingId.TouchPressure, 1);
            Assert.That(SettingsPage.FormatValue(onContact, SettingId.TouchPressure), Is.EqualTo("On contact"));
            Assert.That(SettingsPage.Change(onContact, SettingId.TouchPressure, 1).Blade.TouchPressure, Is.EqualTo(TouchPressure.Realistic), "Only two choices.");
            Assert.That(SettingsPage.FormatValue(settings, SettingId.BladeFlex), Is.EqualTo("Normal"));
            Assert.That(SettingsPage.Change(settings, SettingId.BladeFlex, 1).Blade.Flex, Is.EqualTo(BladeFlexSoftness.Soft));
            Assert.That(SettingsPage.Change(settings, SettingId.BladeFlex, -1).Blade.Flex, Is.EqualTo(BladeFlexSoftness.Stiff));
            Assert.That(SettingsPage.GetImpact(SettingId.TouchPressure), Is.EqualTo(SettingImpact.Blade | SettingImpact.Bout), "The Host's pressure is the bout's rule (PER-82).");
            Assert.That(SettingsPage.GetImpact(SettingId.BladeFlex), Is.EqualTo(SettingImpact.Blade));
        }

        [Test]
        public void ThePistolGripSwitchesBetweenViscontiAndBelgianAndOnlyRedrawsTheBlade()
        {
            var settings = new TrainerSettings();

            Assert.That(SettingsPage.FormatValue(settings, SettingId.PistolGrip), Is.EqualTo("Visconti"));
            var belgian = SettingsPage.Change(settings, SettingId.PistolGrip, 1);
            Assert.That(SettingsPage.FormatValue(belgian, SettingId.PistolGrip), Is.EqualTo("Belgian"));
            Assert.That(SettingsPage.Change(belgian, SettingId.PistolGrip, 1).Blade.PistolGrip, Is.EqualTo(PistolGripShape.Visconti), "Only two choices.");
            Assert.That(belgian.Blade.Mount, Is.EqualTo(settings.Blade.Mount), "The shape never changes where the blade sits.");
            Assert.That(SettingsPage.GetImpact(SettingId.PistolGrip), Is.EqualTo(SettingImpact.Blade));
        }

        [Test]
        public void TheBoutDoubleWindowStepsFrom50To150Milliseconds()
        {
            var settings = new TrainerSettings();

            Assert.That(SettingsPage.FormatValue(settings, SettingId.DoubleWindow), Is.EqualTo("50 ms"));
            Assert.That(SettingsPage.Change(settings, SettingId.DoubleWindow, -1).BoutDoubleWindowSeconds, Is.EqualTo(0.05f), "Never below the rule's.");
            Assert.That(SettingsPage.FormatValue(SettingsPage.Change(settings, SettingId.DoubleWindow, 1), SettingId.DoubleWindow), Is.EqualTo("75 ms"));

            var stepped = settings;
            for (var step = 0; step < 10; step++)
            {
                stepped = SettingsPage.Change(stepped, SettingId.DoubleWindow, 1);
            }

            Assert.That(SettingsPage.FormatValue(stepped, SettingId.DoubleWindow), Is.EqualTo("150 ms"));
            Assert.That(SettingsPage.GetImpact(SettingId.DoubleWindow), Is.EqualTo(SettingImpact.Bout));
        }

        [Test]
        public void TheLaserTiltsDownByDefaultAndStepsByFiveDegrees()
        {
            var settings = new TrainerSettings();

            Assert.That(SettingsPage.FormatValue(settings, SettingId.LaserTilt), Is.EqualTo("+15 deg"));
            Assert.That(SettingsPage.Change(settings, SettingId.LaserTilt, -1).LaserTiltDegrees, Is.EqualTo(10.0f));
            Assert.That(SettingsPage.GetImpact(SettingId.LaserTilt), Is.EqualTo(SettingImpact.None), "The menu reads it every frame.");
        }

        [Test]
        public void BladeSettingsGoToTheBladeAndTheHandMovesItToo()
        {
            Assert.That(SettingsPage.GetImpact(SettingId.TouchHaptic), Is.EqualTo(SettingImpact.Blade));
            Assert.That(SettingsPage.GetImpact(SettingId.BladeYaw), Is.EqualTo(SettingImpact.Blade));
            Assert.That(SettingsPage.GetImpact(SettingId.BladeSet), Is.EqualTo(SettingImpact.Blade));
            Assert.That(SettingsPage.GetImpact(SettingId.BladeMount), Is.EqualTo(SettingImpact.Blade | SettingImpact.Dummy), "Each grip has its own reach.");
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
        public void TheDummyBladeInEveryDrillIsAnOnByDefaultArmedDummyToggleForTheBlade()
        {
            var settings = new TrainerSettings();

            Assert.That(settings.ArmedDummy.BladeInEveryDrill, Is.True);
            Assert.That(SettingsPage.FormatValue(settings, SettingId.DummyBladeInEveryDrill), Is.EqualTo("On (holds its line)"));
            var off = SettingsPage.Change(settings, SettingId.DummyBladeInEveryDrill, -1);
            Assert.That(off.ArmedDummy.BladeInEveryDrill, Is.False);
            Assert.That(SettingsPage.FormatValue(off, SettingId.DummyBladeInEveryDrill), Is.EqualTo("Off (attack Drills only)"));
            Assert.That(SettingsPage.Change(off, SettingId.DummyBladeInEveryDrill, 1).ArmedDummy.BladeInEveryDrill, Is.True);
            Assert.That(SettingsPage.GetImpact(SettingId.DummyBladeInEveryDrill), Is.EqualTo(SettingImpact.Blade));
            Assert.That(SettingGroups.GetGroup(SettingId.DummyBladeInEveryDrill), Is.EqualTo(SettingGroup.ArmedDummy));
        }

        [Test]
        public void TheStartMarkSideToleranceIs45CentimetersInFiveCentimeterStepsUpToTheStripsSide()
        {
            var settings = new TrainerSettings();

            Assert.That(SettingsPage.FormatValue(settings, SettingId.StartMarkSideTolerance), Is.EqualTo("45 cm"));
            Assert.That(SettingsPage.Change(settings, SettingId.StartMarkSideTolerance, 1).StartMarkSideToleranceMeters, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(SettingsPage.Change(settings, SettingId.StartMarkSideTolerance, -1).StartMarkSideToleranceMeters, Is.EqualTo(0.4f).Within(1e-5f));

            var widest = settings;
            var narrowest = settings;
            for (var i = 0; i < 20; i++)
            {
                widest = SettingsPage.Change(widest, SettingId.StartMarkSideTolerance, 1);
                narrowest = SettingsPage.Change(narrowest, SettingId.StartMarkSideTolerance, -1);
            }

            Assert.That(widest.StartMarkSideToleranceMeters, Is.EqualTo(StripGeometry.WidthMeters * 0.5f).Within(1e-5f));
            Assert.That(narrowest.StartMarkSideToleranceMeters, Is.EqualTo(0.15f).Within(1e-5f));
            Assert.That(SettingsPage.GetImpact(SettingId.StartMarkSideTolerance), Is.EqualTo(SettingImpact.None), "Read when each run starts.");
            Assert.That(SettingGroups.GetGroup(SettingId.StartMarkSideTolerance), Is.EqualTo(SettingGroup.Strip));
        }

        [Test]
        public void LightNamedZonesIsAnOnByDefaultToggleThatRebuildsTheDrills()
        {
            var settings = new TrainerSettings();

            Assert.That(SettingsPage.FormatValue(settings, SettingId.LightNamedZones), Is.EqualTo("On"));
            Assert.That(SettingsPage.Change(settings, SettingId.LightNamedZones, 1).LightNamedZones, Is.False);
            Assert.That(SettingsPage.Change(settings, SettingId.LightNamedZones, -1).LightNamedZones, Is.False);
            Assert.That(SettingsPage.GetImpact(SettingId.LightNamedZones), Is.EqualTo(SettingImpact.Drills));
            Assert.That(SettingGroups.GetGroup(SettingId.LightNamedZones), Is.EqualTo(SettingGroup.Drills));
        }

        [Test]
        public void TheCalibrationSavesAPlausibleReachOnTheChosenGripOnly()
        {
            var settings = new TrainerSettings();
            settings.Blade.Mount = BladeMount.French;

            var saved = SettingsPage.SaveGripAngles(settings, new GripAngles(-10.0f, -3.0f), 1.6234f);

            Assert.That(saved.Blade.SavedAngles.French.ReachMeters, Is.EqualTo(1.623f).Within(1e-6f));
            Assert.That(saved.Blade.GetMeasuredReachMeters(), Is.EqualTo(1.623f).Within(1e-6f));
            Assert.That(saved.Blade.SavedAngles.Center.ReachMeters, Is.Null);
            Assert.That(settings.Blade.SavedAngles.French.ReachMeters, Is.Null, "The settings in use are untouched.");
        }

        [Test]
        public void AMissingOrImplausibleReachKeepsTheOneSavedBefore()
        {
            var settings = SettingsPage.SaveGripAngles(new TrainerSettings(), new GripAngles(-10.0f, -3.0f), 1.55f);

            Assert.That(SettingsPage.SaveGripAngles(settings, new GripAngles(-9.0f, -3.0f), 0.4f).Blade.GetMeasuredReachMeters(), Is.EqualTo(1.55f));
            Assert.That(SettingsPage.SaveGripAngles(settings, new GripAngles(-9.0f, -3.0f)).Blade.GetMeasuredReachMeters(), Is.EqualTo(1.55f));
            Assert.That(SettingsPage.SaveGripAngles(new TrainerSettings(), new GripAngles(-9.0f, -3.0f), 3.5f).Blade.GetMeasuredReachMeters(), Is.Null);
        }

        [Test]
        public void ResettingTheGripAnglesKeepsTheMeasuredReach()
        {
            var settings = SettingsPage.SaveGripAngles(new TrainerSettings(), new GripAngles(-10.0f, -3.0f), 1.55f);

            var reset = SettingsPage.ResetGripAngles(settings);

            Assert.That(reset.Blade.SavedAngles.Center.PitchDegrees, Is.Null);
            Assert.That(reset.Blade.GetMeasuredReachMeters(), Is.EqualTo(1.55f));
        }

        [Test]
        public void TheDummyLookSettingsOnlyRedrawTheDummy()
        {
            var settings = new TrainerSettings();

            Assert.That(SettingsPage.FormatValue(settings, SettingId.ShowTargetZones), Is.EqualTo("On"));
            Assert.That(SettingsPage.Change(settings, SettingId.ShowTargetZones, -1).DummyLook.ShowTargetZones, Is.False);
            Assert.That(SettingsPage.FormatValue(settings, SettingId.ZoneOpacity), Is.EqualTo("30%"));
            Assert.That(SettingsPage.FormatValue(settings, SettingId.ZoneThickness), Is.EqualTo("60%"));
            Assert.That(SettingsPage.FormatValue(settings, SettingId.BodyOpacity), Is.EqualTo("60%"));
            Assert.That(SettingsPage.Change(settings, SettingId.ZoneThickness, -1).DummyLook.ZoneThicknessFraction, Is.EqualTo(0.5f).Within(1e-5f));
            foreach (var id in new[] { SettingId.ShowTargetZones, SettingId.ZoneOpacity, SettingId.ZoneThickness, SettingId.BodyOpacity })
            {
                Assert.That(SettingsPage.GetImpact(id), Is.EqualTo(SettingImpact.DummyLook), id.ToString());
            }
        }

        [Test]
        public void TheDummyLookStaysVisibleAndNeverDrawsZonesFatterThanTheyAreJudged()
        {
            foreach (var direction in new[] { -1, 1 })
            {
                var settings = new TrainerSettings();
                for (var i = 0; i < 20; i++)
                {
                    foreach (var id in new[] { SettingId.ZoneOpacity, SettingId.ZoneThickness, SettingId.BodyOpacity })
                    {
                        settings = SettingsPage.Change(settings, id, direction);
                    }
                }

                Assert.That(settings.DummyLook.ZoneOpacity, Is.InRange(0.1f, 1.0f));
                Assert.That(settings.DummyLook.BodyOpacity, Is.InRange(0.1f, 1.0f));
                Assert.That(settings.DummyLook.ZoneThicknessFraction, Is.InRange(0.2f, 1.0f));
            }
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
