// The Fencer's own pause button on the Drill runner (PER-36), and how it combines with the off-strip pause.
// Test Drill timing: cue 0.5 s, window 1.5 s, rest 2.0 s, 3 Reps, Target Zone thigh.
using System;
using NUnit.Framework;
using VrFencing.Drills;
using VrFencing.Dummy;

namespace VrFencing.Tests.Drills
{
    public class DrillRunnerFencerPauseTests
    {
        private const float LongStrip = 4.0f;

        private FakeClock _clock;
        private DrillRunner _runner;
        private RunnerRecorder _recorder;

        [SetUp]
        public void StartTestDrill()
        {
            _clock = new FakeClock();
            _runner = new DrillRunner(_clock, new Random(1234));
            _recorder = new RunnerRecorder(_runner);
            _runner.Start(TestDrills.Create(), LongStrip, shrinkDrillsToFit: false);
        }

        private void Wait(double seconds)
        {
            _clock.Advance(seconds);
            _runner.Tick();
        }

        [Test]
        public void PauseDuringTheWindowFreezesItAndResumeRestartsTheRep()
        {
            Wait(0.5 + 0.5);
            Assert.That(_runner.State, Is.EqualTo(DrillState.Window));

            _runner.PauseByFencer();
            Wait(10.0);

            Assert.That(_runner.IsPaused, Is.True);
            Assert.That(_runner.IsPausedByFencer, Is.True);
            Assert.That(_recorder.Judged, Is.Empty, "A paused window must not time out.");

            _runner.ResumeByFencer();

            Assert.That(_runner.IsPaused, Is.False);
            Assert.That(_runner.State, Is.EqualTo(DrillState.Cue));
            Assert.That(_recorder.Cues, Has.Count.EqualTo(2));
            Assert.That(_recorder.Cues[1].RepIndex, Is.EqualTo(0), "The interrupted Rep is cued again, not skipped.");
            Assert.That(_recorder.Events, Is.EqualTo(new[] { "Paused", "Resumed" }));
        }

        [Test]
        public void ContactsWhilePausedByTheFencerAreIgnored()
        {
            Wait(0.6);
            _runner.PauseByFencer();

            _runner.ReportContact(TestDrills.PointOn(TargetZone.Thigh));

            Assert.That(_recorder.Judged, Is.Empty);
        }

        [Test]
        public void ComingBackOnTheStripDoesNotEndTheFencersPause()
        {
            _runner.PauseByFencer();
            _runner.ReportLeftStrip();
            _runner.ReportBackOnStrip();

            Assert.That(_runner.IsPaused, Is.True);
            Assert.That(_recorder.Events, Is.EqualTo(new[] { "Paused" }), "Leaving an already paused run raises nothing more.");
        }

        [Test]
        public void ResumingWhileOffTheStripWaitsUntilTheFencerIsBack()
        {
            _runner.PauseByFencer();
            _runner.ReportLeftStrip();

            _runner.ResumeByFencer();
            Assert.That(_runner.IsPaused, Is.True);
            Assert.That(_runner.IsPausedByFencer, Is.False);

            _runner.ReportBackOnStrip();
            Assert.That(_runner.IsPaused, Is.False);
        }

        [Test]
        public void PauseDuringTheRestContinuesTheRestOnResume()
        {
            Wait(0.5 + 1.5);
            Assert.That(_runner.State, Is.EqualTo(DrillState.Rest));
            Wait(0.5);

            _runner.PauseByFencer();
            Wait(30.0);
            _runner.ResumeByFencer();
            Assert.That(_runner.State, Is.EqualTo(DrillState.Rest));

            Wait(1.49);
            Assert.That(_runner.State, Is.EqualTo(DrillState.Rest));
            Wait(0.02);
            Assert.That(_runner.State, Is.EqualTo(DrillState.Cue), "The rest runs its remaining 1.5 s, not a fresh 2 s.");
        }

        [Test]
        public void PauseAndResumeWhileIdleDoNothing()
        {
            var idleRunner = new DrillRunner(_clock, new Random(1));
            var idleRecorder = new RunnerRecorder(idleRunner);

            idleRunner.PauseByFencer();
            idleRunner.ResumeByFencer();

            Assert.That(idleRunner.IsPaused, Is.False);
            Assert.That(idleRunner.IsPausedByFencer, Is.False);
            Assert.That(idleRecorder.Events, Is.Empty);
        }

        [Test]
        public void CancelWhilePausedByTheFencerEndsTheRunAndTheNextRunStartsUnpaused()
        {
            _runner.PauseByFencer();
            _runner.Cancel();

            Assert.That(_runner.State, Is.EqualTo(DrillState.Done));
            Assert.That(_runner.IsPausedByFencer, Is.False);

            _runner.Start(TestDrills.Create(), LongStrip, shrinkDrillsToFit: false);
            Assert.That(_runner.IsPaused, Is.False);
            Assert.That(_runner.State, Is.EqualTo(DrillState.Cue));
        }
    }
}
