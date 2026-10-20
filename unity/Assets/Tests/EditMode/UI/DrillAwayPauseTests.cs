// Auto-pause (PER-106): a running Drill pauses as if the Fencer pressed Pause when the headset sleeps, the Meta menu
// opens or the headset comes off, and waits for Resume. Test Drill timing: cue 0.5 s, window 1.5 s, rest 2.0 s, 3 Reps.
using System;
using NUnit.Framework;
using VrFencing.Drills;
using VrFencing.Dummy;
using VrFencing.Tests.Drills;
using VrFencing.UI;

namespace VrFencing.Tests.UI
{
    public class DrillAwayPauseTests
    {
        private const float LongStrip = 4.0f;

        [Test]
        public void ARunningDrillIsPaused()
        {
            Assert.That(DrillAwayPause.Decide(isIntroShowing: false, isRunning: true, isPausedByFencer: false), Is.EqualTo(AwayAction.PauseRun));
        }

        [Test]
        public void ARunPausedOnlyByLeavingTheStripIsPausedByTheFencerToo()
        {
            var runner = new DrillRunner(new FakeClock(), new Random(1234));
            runner.Start(TestDrills.Create(), LongStrip, shrinkDrillsToFit: false);
            runner.ReportLeftStrip();
            Assert.That(runner.IsPaused, Is.True);

            // So it waits for Resume, not just for the Fencer back on the Strip.
            Assert.That(DrillAwayPause.Decide(false, runner.IsRunning, runner.IsPausedByFencer), Is.EqualTo(AwayAction.PauseRun));
            runner.PauseByFencer();
            runner.ReportBackOnStrip();

            Assert.That(runner.IsPaused, Is.True);
        }

        [Test]
        public void ARunTheFencerAlreadyPausedIsLeftAlone()
        {
            Assert.That(DrillAwayPause.Decide(isIntroShowing: false, isRunning: true, isPausedByFencer: true), Is.EqualTo(AwayAction.None));
        }

        [Test]
        public void NothingHappensWithNoDrillOn()
        {
            Assert.That(DrillAwayPause.Decide(isIntroShowing: false, isRunning: false, isPausedByFencer: false), Is.EqualTo(AwayAction.None));
        }

        [Test]
        public void AStartStillShowingItsTaskLineIsDropped()
        {
            Assert.That(DrillAwayPause.Decide(isIntroShowing: true, isRunning: false, isPausedByFencer: false), Is.EqualTo(AwayAction.DropIntro));
        }

        [Test]
        public void EachReasonHasItsOwnLogWording()
        {
            var reasons = (AwayReason[])Enum.GetValues(typeof(AwayReason));
            var described = Array.ConvertAll(reasons, DrillAwayPause.Describe);

            Assert.That(described, Is.Unique);
            Assert.That(DrillAwayPause.Describe(AwayReason.AppPaused), Is.EqualTo("headset asleep"));
        }

        [Test]
        public void NoRepTimesOutWhileAwayAndResumeCuesTheInterruptedRepAgain()
        {
            var clock = new FakeClock();
            var runner = new DrillRunner(clock, new Random(1234));
            var recorder = new RunnerRecorder(runner);
            runner.Start(TestDrills.Create(), LongStrip, shrinkDrillsToFit: false);
            clock.Advance(1.0);
            runner.Tick();
            Assert.That(runner.State, Is.EqualTo(DrillState.Window));

            Assert.That(DrillAwayPause.Decide(false, runner.IsRunning, runner.IsPausedByFencer), Is.EqualTo(AwayAction.PauseRun));
            runner.PauseByFencer();

            // The Meta menu keeps the frames running: a minute of ticks, the Fencer's head off and back on the Strip.
            for (var frame = 0; frame < 60; frame++)
            {
                clock.Advance(1.0);
                runner.Tick();
            }

            runner.ReportLeftStrip();
            runner.ReportBackOnStrip();

            Assert.That(recorder.Judged, Is.Empty, "No Rep may time out while the Fencer is away.");
            Assert.That(runner.IsPaused, Is.True, "The run waits for Resume, even back on the Strip.");
            Assert.That(DrillAwayPause.Decide(false, runner.IsRunning, runner.IsPausedByFencer), Is.EqualTo(AwayAction.None),
                "A second callback for the same sleep changes nothing.");

            runner.ResumeByFencer();

            Assert.That(runner.State, Is.EqualTo(DrillState.Cue));
            Assert.That(recorder.Cues, Has.Count.EqualTo(2));
            Assert.That(recorder.Cues[1].RepIndex, Is.EqualTo(0), "The interrupted Rep is cued again.");

            // The fresh window is the full 1.5 s: a Touch 1.9 s after Resume (cue 0.5 s + 1.4 s) still counts.
            clock.Advance(1.9);
            runner.Tick();
            runner.ReportContact(TestDrills.PointOn(TargetZone.Thigh));

            Assert.That(recorder.Judged, Has.Count.EqualTo(1));
            Assert.That(recorder.Judged[0].Outcome, Is.EqualTo(RepOutcome.Success));
            Assert.That(recorder.Judged[0].ReactionSeconds, Is.EqualTo(1.4).Within(1e-9), "Time away is not counted.");
        }

        [Test]
        public void AutoPauseOpensFoldedPanelsOnTheRunningPanel()
        {
            var navigator = new MenuNavigator();
            navigator.SetCollapsed(true);
            Assert.That(navigator.GetShownPanel(isPlacingStrip: false, isDrillRunning: true), Is.EqualTo(MenuScreen.Hidden));

            navigator.OnDrillAutoPaused();

            Assert.That(navigator.GetShownPanel(isPlacingStrip: false, isDrillRunning: true), Is.EqualTo(MenuScreen.Running));
        }
    }
}
