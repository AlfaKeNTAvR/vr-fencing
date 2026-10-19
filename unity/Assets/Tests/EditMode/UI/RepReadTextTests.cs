// The label above the Dummy between Reps (PER-88, PER-89): what the footwork read as, and the prompt back to the line.
using NUnit.Framework;
using VrFencing.Drills;
using VrFencing.Dummy;
using VrFencing.Tests.Drills;
using VrFencing.UI;

namespace VrFencing.Tests.UI
{
    public class RepReadTextTests
    {
        private static FootworkReading Reading(FootworkAction action, float forward = 0.0f, float back = 0.0f, int steps = 0)
        {
            return new FootworkReading(action, 0.3, forward, back, peakForwardSpeedMetersPerSecond: 1.5f, headDropMeters: 0.05f, stepCount: steps);
        }

        [Test]
        public void AStepLungeReadsWithItsTravel()
        {
            Assert.That(RepReadText.Format(Reading(FootworkAction.StepLunge, forward: 1.23f)), Is.EqualTo("read: step-lunge, 1.2 m"));
        }

        [Test]
        public void SeveralAdvancesReadWithTheirCount()
        {
            Assert.That(RepReadText.Format(Reading(FootworkAction.Advance, forward: 1.02f, steps: 3)), Is.EqualTo("read: 3 advances, 1.0 m"));
            Assert.That(RepReadText.Format(Reading(FootworkAction.Advance, forward: 0.34f, steps: 1)), Is.EqualTo("read: advance, 0.3 m"));
        }

        [Test]
        public void ARetreatReadsHowFarBack()
        {
            Assert.That(RepReadText.Format(Reading(FootworkAction.Retreat, forward: 0.02f, back: 0.66f, steps: 2)), Is.EqualTo("read: 2 retreats, 0.7 m"));
        }

        [Test]
        public void ShortMovesReadInCentimeters()
        {
            Assert.That(RepReadText.Format(Reading(FootworkAction.Check, forward: 0.06f)), Is.EqualTo("read: check, 6 cm"));
        }

        [Test]
        public void NoStepReadsAsNoStep()
        {
            Assert.That(RepReadText.Format(Reading(FootworkAction.None, forward: 0.01f)), Is.EqualTo("read: no step"));
        }

        [Test]
        public void TheRunningPanelSaysRestThenBackToTheLineWhileTheRunWaits()
        {
            var clock = new FakeClock();
            var runner = new DrillRunner(clock, new System.Random(1));
            runner.Start(TestDrills.Create(), 4.0f, shrinkDrillsToFit: false);
            clock.Advance(1.0);
            runner.ReportAtStartMark(false);
            runner.ReportContact(TestDrills.PointOn(TargetZone.Thigh));

            Assert.That(RunStatusText.Format(runner, isFencerOnStrip: true).Instruction, Is.EqualTo(RunStatusText.RestText));

            clock.Advance(DrillRunner.ReturnPromptSeconds);
            runner.Tick();

            Assert.That(RunStatusText.Format(runner, isFencerOnStrip: true).Instruction, Is.EqualTo(RepReadText.BackToLineText));
        }

        [Test]
        public void ThePromptGoesUnderTheRead()
        {
            Assert.That(RepReadText.Combine("read: lunge, 0.6 m", isPromptingReturn: true), Is.EqualTo("read: lunge, 0.6 m\nBack to the line"));
            Assert.That(RepReadText.Combine(string.Empty, isPromptingReturn: true), Is.EqualTo("Back to the line"));
            Assert.That(RepReadText.Combine("read: lunge, 0.6 m", isPromptingReturn: false), Is.EqualTo("read: lunge, 0.6 m"));
        }
    }
}
