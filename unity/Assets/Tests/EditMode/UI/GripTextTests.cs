// The grip page and the thrust calibration's words (PER-52), and that calibrating puts the panels away.
using NUnit.Framework;
using VrFencing.Settings;
using VrFencing.UI;

namespace VrFencing.Tests.UI
{
    public class GripTextTests
    {
        [Test]
        public void TheGripPageComparesComputedSavedFeelTestAndInUse()
        {
            var settings = new BladeSettings { Mount = BladeMount.French };

            Assert.That(GripText.FormatComparison(settings), Is.EqualTo(new[]
            {
                "Grip: French (guard 7.5 cm ahead)",
                "Computed: pitch -13, yaw -4",
                "Yours: none yet (the computed angles are in use)",
                "Feel test (old default): pitch +10, yaw -10",
                "In use: pitch -13, yaw -4",
            }));

            settings.SavedAngles.French.PitchDegrees = -9.0f;
            var lines = GripText.FormatComparison(settings);
            Assert.That(lines[2], Is.EqualTo("Yours: pitch -9, yaw computed"));
            Assert.That(lines[4], Is.EqualTo("In use: pitch -9, yaw -4"));
        }

        [Test]
        public void ThePromptCountsTargetsAndTheHold()
        {
            Assert.That(GripText.FormatPrompt(0, 3, isAimed: false, 0.0f), Is.EqualTo("Target 1 of 3\nThrust at the dot with a straight arm"));
            Assert.That(GripText.FormatPrompt(2, 3, isAimed: true, 0.5f), Is.EqualTo("Target 3 of 3\nHold still... 50%"));
        }

        [Test]
        public void TheResultWarnsWhenTheThrustsDisagree()
        {
            Assert.That(GripText.FormatResult(new GripAngles(-11.0f, -2.0f), 3.0f), Is.EqualTo("Saved pitch -11, yaw -2 (thrusts agree within 3 deg)"));
            Assert.That(GripText.FormatResult(new GripAngles(-11.0f, -2.0f), 12.0f), Does.EndWith("disagree by 12 deg: try again"));
        }

        [Test]
        public void CalibratingPutsThePanelsAndTheLaserAwayAndEndsOnTheGripPage()
        {
            var navigator = new MenuNavigator();
            navigator.Open(MenuScreen.Grip);

            navigator.SetCalibrating(true);

            Assert.That(navigator.GetShownPanel(isPlacingStrip: false, isDrillRunning: false), Is.EqualTo(MenuScreen.Hidden));
            Assert.That(navigator.IsPointerShown(isPlacingStrip: false, isDrillRunning: false), Is.False);

            navigator.SetCalibrating(false);

            Assert.That(navigator.GetShownPanel(isPlacingStrip: false, isDrillRunning: false), Is.EqualTo(MenuScreen.Grip));
        }
    }
}
