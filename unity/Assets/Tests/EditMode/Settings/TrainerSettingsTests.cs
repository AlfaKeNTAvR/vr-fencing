// Settings defaults and JSON persistence (PER-30). Defaults come from the
// Drill decision (PER-21) and the Strip decision (PER-24).
using System.IO;
using NUnit.Framework;
using VrFencing.Settings;

namespace VrFencing.Tests.Settings
{
    public class TrainerSettingsTests
    {
        private string _directory;
        private string _settingsPath;

        [SetUp]
        public void CreateScratchDirectory()
        {
            _directory = Path.Combine(Path.GetTempPath(), "vrfencing-settings-tests-" + Path.GetRandomFileName());
            Directory.CreateDirectory(_directory);
            _settingsPath = Path.Combine(_directory, SettingsStorage.FileName);
        }

        [TearDown]
        public void DeleteScratchDirectory()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        [Test]
        public void AFileFromBeforeTheReachAndTheLightLoadsWithTheirDefaults()
        {
            // PER-92: an older settings.json has no LightNamedZones and no ReachMeters.
            File.WriteAllText(_settingsPath,
                "{ \"FencerHeightMeters\": 1.8, \"Blade\": { \"Mount\": \"French\", \"SavedAngles\": { \"French\": { \"PitchDegrees\": -9.0 } } } }");

            var loaded = SettingsStorage.Load(_settingsPath);

            Assert.That(loaded.FencerHeightMeters, Is.EqualTo(1.8f));
            Assert.That(loaded.LightNamedZones, Is.True);
            Assert.That(loaded.Blade.SavedAngles.French.PitchDegrees, Is.EqualTo(-9.0f));
            Assert.That(loaded.Blade.GetMeasuredReachMeters(), Is.Null, "No reach: the Dummy's distance comes from height.");
        }

        [Test]
        public void TheMeasuredReachAndTheLightSaveAndLoad()
        {
            var saved = new TrainerSettings { LightNamedZones = false };
            saved.Blade.SavedAngles.Pistol.ReachMeters = 1.57f;

            SettingsStorage.Save(saved, _settingsPath);
            var loaded = SettingsStorage.Load(_settingsPath);

            Assert.That(loaded.LightNamedZones, Is.False);
            Assert.That(loaded.Blade.SavedAngles.Pistol.ReachMeters, Is.EqualTo(1.57f));
            Assert.That(loaded.Blade.SavedAngles.Center.ReachMeters, Is.Null);
        }

        [Test]
        public void DefaultsUseRightHandAndAverageFencerHeight()
        {
            var settings = new TrainerSettings();

            Assert.That(settings.Hand, Is.EqualTo(Hand.Right));
            Assert.That(settings.FencerHeightMeters, Is.EqualTo(1.65f));
        }

        [Test]
        public void DefaultsRunTenRepsWithTwoSecondsRest()
        {
            var settings = new TrainerSettings();

            Assert.That(settings.RepsPerDrill, Is.EqualTo(10));
            Assert.That(settings.RestBetweenRepsSeconds, Is.EqualTo(2.0f));
        }

        [Test]
        public void DefaultTimeWindowsMatchTheDrillDecision()
        {
            var windows = new TrainerSettings().TimeWindows;

            Assert.That(windows.ReactionSeconds, Is.EqualTo(1.5f));
            Assert.That(windows.CompoundSecondTouchSeconds, Is.EqualTo(1.0f));
            Assert.That(windows.DistanceChoiceSeconds, Is.EqualTo(2.0f));
            Assert.That(windows.FootworkCallSeconds, Is.EqualTo(2.0f));
        }

        [Test]
        public void DefaultsDoNotShrinkDrillsToFit()
        {
            Assert.That(new TrainerSettings().ShrinkDrillsToFit, Is.False);
        }

        [Test]
        public void DefaultWarningZoneScalesFromTwoMetersDownToHalfAMeter()
        {
            var warning = new TrainerSettings().WarningZone;

            Assert.That(warning.LengthOnPisteMeters, Is.EqualTo(2.0f));
            Assert.That(warning.MinimumLengthMeters, Is.EqualTo(0.5f));
            Assert.That(warning.OffStripToleranceMeters, Is.EqualTo(0.0f));
        }

        [Test]
        public void SaveThenLoadReturnsEveryChangedValue()
        {
            var saved = new TrainerSettings
            {
                Hand = Hand.Left,
                FencerHeightMeters = 1.82f,
                RepsPerDrill = 7,
                RestBetweenRepsSeconds = 3.5f,
                TimeWindows = new TimeWindowSettings
                {
                    ReactionSeconds = 1.2f,
                    CompoundSecondTouchSeconds = 0.8f,
                    DistanceChoiceSeconds = 2.5f,
                    FootworkCallSeconds = 1.7f,
                },
                ShrinkDrillsToFit = true,
                WarningZone = new WarningZoneSettings
                {
                    LengthOnPisteMeters = 1.5f,
                    MinimumLengthMeters = 0.4f,
                    OffStripToleranceMeters = 0.1f,
                },
            };

            SettingsStorage.Save(saved, _settingsPath);
            var loaded = SettingsStorage.Load(_settingsPath);

            Assert.That(loaded.Hand, Is.EqualTo(Hand.Left));
            Assert.That(loaded.FencerHeightMeters, Is.EqualTo(1.82f));
            Assert.That(loaded.RepsPerDrill, Is.EqualTo(7));
            Assert.That(loaded.RestBetweenRepsSeconds, Is.EqualTo(3.5f));
            Assert.That(loaded.TimeWindows.ReactionSeconds, Is.EqualTo(1.2f));
            Assert.That(loaded.TimeWindows.CompoundSecondTouchSeconds, Is.EqualTo(0.8f));
            Assert.That(loaded.TimeWindows.DistanceChoiceSeconds, Is.EqualTo(2.5f));
            Assert.That(loaded.TimeWindows.FootworkCallSeconds, Is.EqualTo(1.7f));
            Assert.That(loaded.ShrinkDrillsToFit, Is.True);
            Assert.That(loaded.WarningZone.LengthOnPisteMeters, Is.EqualTo(1.5f));
            Assert.That(loaded.WarningZone.MinimumLengthMeters, Is.EqualTo(0.4f));
            Assert.That(loaded.WarningZone.OffStripToleranceMeters, Is.EqualTo(0.1f));
        }

        [Test]
        public void BladeSettingsSaveAndLoadAsReadableText()
        {
            var saved = new TrainerSettings();
            saved.Blade.TouchRule = TouchRule.Strict;
            saved.Blade.Mount = BladeMount.Pistol;
            saved.Blade.SavedAngles.Pistol.PitchDegrees = 15.0f;
            saved.Blade.GlanceHaptic = HapticPattern.Off;
            saved.Blade.HapticFloor = 0.3f;

            SettingsStorage.Save(saved, _settingsPath);
            var loaded = SettingsStorage.Load(_settingsPath);

            Assert.That(File.ReadAllText(_settingsPath), Does.Contain("\"Strict\"").And.Contain("\"Pistol\""));
            Assert.That(loaded.Blade.TouchRule, Is.EqualTo(TouchRule.Strict));
            Assert.That(loaded.Blade.Mount, Is.EqualTo(BladeMount.Pistol));
            Assert.That(loaded.Blade.SavedAngles.Pistol.PitchDegrees, Is.EqualTo(15.0f));
            Assert.That(loaded.Blade.SavedAngles.Pistol.YawDegrees, Is.Null, "Unset: the grip's computed yaw.");
            Assert.That(GripGeometry.GetActiveAngles(loaded.Blade).YawDegrees, Is.EqualTo(-3.0f));
            Assert.That(loaded.Blade.GlanceHaptic, Is.EqualTo(HapticPattern.Off));
            Assert.That(loaded.Blade.HapticFloor, Is.EqualTo(0.3f));
        }

        [Test]
        public void AFileFromBeforeTheGripsLoadsOnTheComputedAngles()
        {
            // Before PER-52 the feel-test angles were saved as plain Settings; they are no longer read.
            File.WriteAllText(_settingsPath, "{ \"Blade\": { \"Mount\": \"Pistol\", \"PitchDegrees\": 10.0, \"YawDegrees\": -10.0, \"HapticFloor\": 0.3 } }");

            var loaded = SettingsStorage.Load(_settingsPath);

            Assert.That(loaded.Blade.Mount, Is.EqualTo(BladeMount.Pistol));
            Assert.That(GripGeometry.GetActiveAngles(loaded.Blade).PitchDegrees, Is.EqualTo(-12.0f));
            Assert.That(GripGeometry.GetActiveAngles(loaded.Blade).YawDegrees, Is.EqualTo(-3.0f));
            Assert.That(loaded.Blade.HapticFloor, Is.EqualTo(0.3f));
            Assert.That(loaded.Blade.SetSagittaMeters, Is.EqualTo(0.007f));
        }

        [Test]
        public void AFileFromBeforeTheBladeGetsTheBladeDefaults()
        {
            File.WriteAllText(_settingsPath, "{ \"Hand\": \"Left\" }");

            var blade = SettingsStorage.Load(_settingsPath).Blade;

            Assert.That(blade.TouchRule, Is.EqualTo(TouchRule.Loose));
            Assert.That(blade.TouchHaptic, Is.EqualTo(HapticPattern.Heavy));
            Assert.That(blade.FlatHaptic, Is.EqualTo(HapticPattern.DoubleTap));
            Assert.That(blade.GlanceHaptic, Is.EqualTo(HapticPattern.Tick));
        }

        [Test]
        public void SaveWritesTheHandAsReadableText()
        {
            SettingsStorage.Save(new TrainerSettings { Hand = Hand.Left }, _settingsPath);

            Assert.That(File.ReadAllText(_settingsPath), Does.Contain("\"Left\""));
        }

        [Test]
        public void LoadWithoutAFileReturnsDefaults()
        {
            var loaded = SettingsStorage.Load(_settingsPath);

            Assert.That(loaded.Hand, Is.EqualTo(Hand.Right));
            Assert.That(loaded.RepsPerDrill, Is.EqualTo(10));
        }

        [Test]
        public void LoadKeepsDefaultsForValuesMissingFromAnOlderFile()
        {
            File.WriteAllText(_settingsPath, "{ \"Hand\": \"Left\", \"TimeWindows\": { \"ReactionSeconds\": 1.1 } }");

            var loaded = SettingsStorage.Load(_settingsPath);

            Assert.That(loaded.Hand, Is.EqualTo(Hand.Left));
            Assert.That(loaded.TimeWindows.ReactionSeconds, Is.EqualTo(1.1f));
            Assert.That(loaded.TimeWindows.FootworkCallSeconds, Is.EqualTo(2.0f));
            Assert.That(loaded.FencerHeightMeters, Is.EqualTo(1.65f));
        }

        [Test]
        public void LoadOfAMalformedFileThrows()
        {
            File.WriteAllText(_settingsPath, "{ not json");

            Assert.Throws<SettingsFormatException>(() => SettingsStorage.Load(_settingsPath));
        }
    }
}
