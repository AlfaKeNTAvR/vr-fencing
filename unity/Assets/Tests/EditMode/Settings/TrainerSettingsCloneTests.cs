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
            };
            settings.TimeWindows.ReactionSeconds = 1.1f;
            settings.TimeWindows.CompoundSecondTouchSeconds = 1.2f;
            settings.TimeWindows.DistanceChoiceSeconds = 1.3f;
            settings.TimeWindows.FootworkCallSeconds = 1.4f;
            settings.WarningZone.LengthOnPisteMeters = 1.5f;
            settings.WarningZone.MinimumLengthMeters = 0.6f;
            settings.WarningZone.OffStripToleranceMeters = 0.2f;

            Assert.That(SettingsLeaves.Read(settings.Clone()), Is.EqualTo(SettingsLeaves.Read(settings)));
        }

        [Test]
        public void CloneDoesNotShareNestedSettings()
        {
            var settings = new TrainerSettings();
            var clone = settings.Clone();

            clone.TimeWindows.ReactionSeconds = 3.0f;
            clone.WarningZone.OffStripToleranceMeters = 0.4f;

            Assert.That(settings.TimeWindows.ReactionSeconds, Is.EqualTo(new TrainerSettings().TimeWindows.ReactionSeconds));
            Assert.That(settings.WarningZone.OffStripToleranceMeters, Is.EqualTo(0.0f));
        }
    }
}
