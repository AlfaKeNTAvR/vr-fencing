// TrainerSettings.Clone, which the settings screen relies on to change a copy instead of the settings in use.
using NUnit.Framework;
using VrFencing.Settings;

namespace VrFencing.Tests.Settings
{
    public class TrainerSettingsCloneTests
    {
        [Test]
        public void CloneCopiesEverySetting()
        {
            var settings = new TrainerSettings
            {
                Hand = Hand.Left,
                FencerHeightMeters = 1.8f,
                RepsPerDrill = 12,
                RestBetweenRepsSeconds = 3.0f,
                ShrinkDrillsToFit = true,
                LightNamedZones = false,
                BoutDoubleWindowSeconds = 0.125f,
            };
            settings.TimeWindows.ReactionSeconds = 1.1f;
            settings.TimeWindows.CompoundSecondTouchSeconds = 1.2f;
            settings.TimeWindows.DistanceChoiceSeconds = 1.3f;
            settings.TimeWindows.FootworkCallSeconds = 1.4f;
            settings.WarningZone.LengthOnPisteMeters = 1.5f;
            settings.WarningZone.MinimumLengthMeters = 0.6f;
            settings.WarningZone.OffStripToleranceMeters = 0.2f;
            settings.Blade.TouchRule = TouchRule.Strict;
            settings.Blade.TouchPressure = TouchPressure.OnContact;
            settings.Blade.ParryRule = ParryRule.Strict;
            settings.Blade.Flex = BladeFlexSoftness.Soft;
            settings.Blade.Mount = BladeMount.FrenchPosted;
            settings.Blade.SavedAngles.Center.PitchDegrees = 1.0f;
            settings.Blade.SavedAngles.Pistol.YawDegrees = 2.0f;
            settings.Blade.SavedAngles.French.PitchDegrees = 3.0f;
            settings.Blade.SavedAngles.FrenchPosted.PitchDegrees = 5.0f;
            settings.Blade.SavedAngles.FrenchPosted.YawDegrees = 15.0f;
            settings.Blade.SavedAngles.Pistol.ReachMeters = 1.6f;
            settings.Blade.SetSagittaMeters = 0.004f;
            settings.Blade.TouchHaptic = HapticPattern.Crisp;
            settings.Blade.FlatHaptic = HapticPattern.Off;
            settings.Blade.GlanceHaptic = HapticPattern.Heavy;
            settings.Blade.HapticFloor = 0.2f;
            settings.Blade.HapticFullStrengthSpeedMetersPerSecond = 3.5f;
            settings.DummyLook.ShowTargetZones = false;
            settings.DummyLook.ZoneOpacity = 0.7f;
            settings.DummyLook.BodyOpacity = 0.2f;
            settings.DummyLook.ZoneThicknessFraction = 0.4f;
            settings.ArmedDummy.RiposteInSlowParries = false;
            settings.ArmedDummy.RiposteInFastParries = false;
            settings.ArmedDummy.RiposteInLowParries = false;

            Assert.That(SettingsLeaves.Read(settings.Clone()), Is.EqualTo(SettingsLeaves.Read(settings)));
        }

        [Test]
        public void CloneDoesNotShareNestedSettings()
        {
            var settings = new TrainerSettings();
            var clone = settings.Clone();

            clone.TimeWindows.ReactionSeconds = 3.0f;
            clone.WarningZone.OffStripToleranceMeters = 0.4f;
            clone.Blade.SavedAngles.French.PitchDegrees = 30.0f;
            clone.ArmedDummy.RiposteInLowParries = false;

            Assert.That(settings.TimeWindows.ReactionSeconds, Is.EqualTo(new TrainerSettings().TimeWindows.ReactionSeconds));
            Assert.That(settings.WarningZone.OffStripToleranceMeters, Is.EqualTo(0.0f));
            Assert.That(settings.Blade.SavedAngles.French.PitchDegrees, Is.Null);
            Assert.That(settings.ArmedDummy.RiposteInLowParries, Is.True);
        }
    }
}
