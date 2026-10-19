// The Drill runner state machine (PER-34), driven by fake blade contacts, fake Strip events and a fake clock.
// Test Drill timing: cue 0.5 s, window 1.5 s, rest 2.0 s, 3 Reps, Target Zone thigh unless stated.
using System;
using System.Linq;
using NUnit.Framework;
using VrFencing.Drills;
using VrFencing.Dummy;

namespace VrFencing.Tests.Drills
{
    public class DrillRunnerTests
    {
        private const float LongStrip = 4.0f;
        private const double Tolerance = 1e-9;

        private FakeClock _clock;
        private DrillRunner _runner;
        private RunnerRecorder _recorder;

        [SetUp]
        public void CreateRunner()
        {
            _clock = new FakeClock();
            _runner = new DrillRunner(_clock, new Random(1234));
            _recorder = new RunnerRecorder(_runner);
        }

        // Moves time forward and ticks, like frames passing.
        private void Wait(double seconds)
        {
            _clock.Advance(seconds);
            _runner.Tick();
        }

        private void StartTestDrill(int repCount = 3, float cueSeconds = TestDrills.CueSeconds, float restSeconds = TestDrills.RestSeconds)
        {
            var availability = _runner.Start(TestDrills.Create(repCount, cueSeconds, restSeconds), LongStrip, shrinkDrillsToFit: false);
            Assert.That(availability.IsAvailable, Is.True);
        }

        [Test]
        public void NewRunnerIsIdleAndIgnoresInputs()
        {
            _runner.Tick();
            _runner.ReportContact(TestDrills.PointOn(TestDrills.Create().TargetZones[0]));

            Assert.That(_runner.State, Is.EqualTo(DrillState.Idle));
            Assert.That(_recorder.States, Is.Empty);
        }

        [Test]
        public void StartCuesTheFirstRep()
        {
            StartTestDrill();

            Assert.That(_runner.State, Is.EqualTo(DrillState.Cue));
            Assert.That(_recorder.Cues, Has.Count.EqualTo(1));
            Assert.That(_recorder.Cues[0].RepIndex, Is.EqualTo(0));
            Assert.That(_recorder.Cues[0].TargetZone, Is.EqualTo(TargetZone.Thigh));
            Assert.That(_recorder.Cues[0].CueType, Is.EqualTo(CueType.NamedTargetZone));
        }

        [Test]
        public void WindowOpensWhenTheCueTimeRunsOut()
        {
            StartTestDrill();

            Wait(0.25);
            Assert.That(_runner.State, Is.EqualTo(DrillState.Cue));

            Wait(0.25);
            Assert.That(_runner.State, Is.EqualTo(DrillState.Window));
        }

        [Test]
        public void ZeroCueTimeOpensTheWindowImmediately()
        {
            StartTestDrill(cueSeconds: 0.0f);

            Assert.That(_runner.State, Is.EqualTo(DrillState.Window));
        }

        [Test]
        public void PointFirstTouchOnTheCuedZoneIsASuccess()
        {
            StartTestDrill();
            Wait(0.5);
            Wait(0.75);

            _runner.ReportContact(TestDrills.PointOn(TargetZone.Thigh));

            var result = _recorder.Judged.Single();
            Assert.That(result.Outcome, Is.EqualTo(RepOutcome.Success));
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.DrillId, Is.EqualTo("lunge-to-target"));
            Assert.That(result.RepIndex, Is.EqualTo(0));
            Assert.That(result.RequestedZone, Is.EqualTo(TargetZone.Thigh));
            Assert.That(result.ContactZone, Is.EqualTo(TargetZone.Thigh));
            Assert.That(result.ReactionSeconds, Is.EqualTo(0.75).Within(Tolerance));
            Assert.That(result.TipSpeedMetersPerSecond, Is.EqualTo(3.0f));
            Assert.That(result.DistanceFromZoneCenterMeters, Is.EqualTo(0.02f));
            Assert.That(result.IsShrunk, Is.False);
        }

        [Test]
        public void TouchOnAnotherZoneFailsAsWrongZone()
        {
            StartTestDrill();
            Wait(0.5);

            _runner.ReportContact(TestDrills.PointOn(TargetZone.Hand));

            var result = _recorder.Judged.Single();
            Assert.That(result.Outcome, Is.EqualTo(RepOutcome.WrongZone));
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ContactZone, Is.EqualTo(TargetZone.Hand));
        }

        [Test]
        public void FlatContactOnTheCuedZoneFails()
        {
            StartTestDrill();
            Wait(0.5);

            _runner.ReportContact(TestDrills.FlatOn(TargetZone.Thigh));

            Assert.That(_recorder.Judged.Single().Outcome, Is.EqualTo(RepOutcome.FlatContact));
        }

        [Test]
        public void NoTouchWithinTheWindowIsATimeout()
        {
            StartTestDrill();
            Wait(0.5);

            Wait(1.25);
            Assert.That(_recorder.Judged, Is.Empty);

            Wait(0.25);
            var result = _recorder.Judged.Single();
            Assert.That(result.Outcome, Is.EqualTo(RepOutcome.Timeout));
            Assert.That(result.ContactZone, Is.Null);
            Assert.That(result.ReactionSeconds, Is.Null);
            Assert.That(_runner.State, Is.EqualTo(DrillState.Rest));
        }

        [Test]
        public void ContactArrivingAfterTheWindowRanOutIsATimeoutEvenBeforeTheNextTick()
        {
            StartTestDrill();
            Wait(0.5);

            _clock.Advance(1.6);
            _runner.ReportContact(TestDrills.PointOn(TargetZone.Thigh));

            Assert.That(_recorder.Judged.Single().Outcome, Is.EqualTo(RepOutcome.Timeout));
        }

        [Test]
        public void ContactDuringTheCueIsIgnored()
        {
            StartTestDrill();

            _runner.ReportContact(TestDrills.PointOn(TargetZone.Thigh));

            Assert.That(_recorder.Judged, Is.Empty);
            Assert.That(_runner.State, Is.EqualTo(DrillState.Cue));
        }

        [Test]
        public void OnlyTheFirstContactInAWindowCounts()
        {
            StartTestDrill();
            Wait(0.5);

            _runner.ReportContact(TestDrills.FlatOn(TargetZone.Thigh));
            _runner.ReportContact(TestDrills.PointOn(TargetZone.Thigh));

            Assert.That(_recorder.Judged.Single().Outcome, Is.EqualTo(RepOutcome.FlatContact));
        }

        [Test]
        public void OneRepPassesThroughCueWindowJudgeAndRestThenCuesTheNext()
        {
            StartTestDrill();
            Wait(0.5);
            _runner.ReportContact(TestDrills.PointOn(TargetZone.Thigh));

            Wait(1.75);
            Assert.That(_runner.State, Is.EqualTo(DrillState.Rest));

            Wait(0.25);
            Assert.That(_recorder.States, Is.EqualTo(new[]
            {
                DrillState.Cue, DrillState.Window, DrillState.Judge, DrillState.Rest, DrillState.Cue,
            }));
            Assert.That(_runner.CurrentRepIndex, Is.EqualTo(1));
            Assert.That(_recorder.Cues.Last().RepIndex, Is.EqualTo(1));
        }

        [Test]
        public void LastRepGoesFromJudgeToDoneAndReportsEveryResult()
        {
            StartTestDrill(repCount: 2);

            Wait(0.5);
            _runner.ReportContact(TestDrills.PointOn(TargetZone.Thigh));
            Wait(2.0);
            Wait(0.5);
            Wait(1.5);

            Assert.That(_runner.State, Is.EqualTo(DrillState.Done));
            Assert.That(_recorder.States.Skip(_recorder.States.Count - 2), Is.EqualTo(new[] { DrillState.Judge, DrillState.Done }));
            var summary = _recorder.Summaries.Single();
            Assert.That(summary.WasCancelled, Is.False);
            Assert.That(summary.Results.Select(result => result.Outcome), Is.EqualTo(new[] { RepOutcome.Success, RepOutcome.Timeout }));
            Assert.That(summary.Results.Select(result => result.RepIndex), Is.EqualTo(new[] { 0, 1 }));
        }

        [Test]
        public void OneLongFrameRunsThroughSeveralStatesOnSchedule()
        {
            StartTestDrill(repCount: 3);

            // Cue 0.5 + window 1.5 + rest 2.0 = rep 1 cued at 4.0; its window opens at 4.5.
            Wait(4.6);

            Assert.That(_recorder.Judged.Single().Outcome, Is.EqualTo(RepOutcome.Timeout));
            Assert.That(_runner.State, Is.EqualTo(DrillState.Window));
            Assert.That(_runner.CurrentRepIndex, Is.EqualTo(1));

            _runner.ReportContact(TestDrills.PointOn(TargetZone.Thigh));
            Assert.That(_recorder.Judged.Last().ReactionSeconds, Is.EqualTo(0.1).Within(1e-6));
        }

        [Test]
        public void ZeroRestCuesTheNextRepRightAfterTheJudgement()
        {
            StartTestDrill(cueSeconds: 0.0f, restSeconds: 0.0f);

            _runner.ReportContact(TestDrills.PointOn(TargetZone.Thigh));

            Assert.That(_runner.State, Is.EqualTo(DrillState.Window));
            Assert.That(_runner.CurrentRepIndex, Is.EqualTo(1));
        }

        [Test]
        public void EachRepCuesAZoneFromTheDrillsList()
        {
            var zones = new[] { TargetZone.Hand, TargetZone.Foot, TargetZone.Body };
            _runner.Start(TestDrills.Create(repCount: 20, cueSeconds: 0.0f, restSeconds: 0.0f, zones: zones), LongStrip, false);

            for (var rep = 0; rep < 20; rep++)
            {
                _runner.ReportContact(TestDrills.PointOn(_runner.CurrentTargetZone));
            }

            Assert.That(_recorder.Cues.Select(cue => cue.TargetZone).Distinct(), Is.SubsetOf(zones));
            Assert.That(_recorder.Cues.Select(cue => cue.TargetZone).Distinct().Count(), Is.GreaterThan(1));
            Assert.That(_recorder.Judged.All(result => result.IsSuccess), Is.True);
        }

        [Test]
        public void LeavingTheStripDuringTheWindowPausesAndFreezesTheTimeout()
        {
            StartTestDrill();
            Wait(0.5);
            Wait(1.0);

            _runner.ReportLeftStrip();
            Wait(10.0);

            Assert.That(_runner.IsPaused, Is.True);
            Assert.That(_recorder.Events, Is.EqualTo(new[] { "Paused" }));
            Assert.That(_recorder.Judged, Is.Empty);
        }

        [Test]
        public void ContactsWhilePausedAreIgnored()
        {
            StartTestDrill();
            Wait(0.5);
            _runner.ReportLeftStrip();

            _runner.ReportContact(TestDrills.PointOn(TargetZone.Thigh));

            Assert.That(_recorder.Judged, Is.Empty);
        }

        [Test]
        public void ComingBackDuringTheWindowRestartsTheRepWithAFreshCue()
        {
            StartTestDrill();
            Wait(0.5);
            Wait(1.0);
            _runner.ReportLeftStrip();
            Wait(10.0);

            _runner.ReportBackOnStrip();

            Assert.That(_runner.IsPaused, Is.False);
            Assert.That(_recorder.Events, Is.EqualTo(new[] { "Paused", "Resumed" }));
            Assert.That(_runner.State, Is.EqualTo(DrillState.Cue));
            Assert.That(_recorder.Cues.Select(cue => cue.RepIndex), Is.EqualTo(new[] { 0, 0 }));

            // The restarted Rep gets its full cue and window again.
            Wait(0.5);
            Wait(1.4);
            Assert.That(_recorder.Judged, Is.Empty);
            _runner.ReportContact(TestDrills.PointOn(TargetZone.Thigh));
            Assert.That(_recorder.Judged.Single().ReactionSeconds, Is.EqualTo(1.4).Within(1e-6));
            Assert.That(_recorder.Judged.Single().RepIndex, Is.EqualTo(0));
        }

        [Test]
        public void ComingBackDuringTheRestContinuesTheRestWhereItStopped()
        {
            StartTestDrill();
            Wait(0.5);
            _runner.ReportContact(TestDrills.PointOn(TargetZone.Thigh));
            Wait(1.5);

            _runner.ReportLeftStrip();
            Wait(30.0);
            _runner.ReportBackOnStrip();

            Assert.That(_runner.State, Is.EqualTo(DrillState.Rest));
            Wait(0.25);
            Assert.That(_runner.State, Is.EqualTo(DrillState.Rest));
            Wait(0.25);
            Assert.That(_runner.State, Is.EqualTo(DrillState.Cue));
            Assert.That(_runner.CurrentRepIndex, Is.EqualTo(1));
        }

        [Test]
        public void StartingWhileOffTheStripStartsPausedAndCuesOnReturn()
        {
            _runner.ReportLeftStrip();
            StartTestDrill();
            Wait(5.0);

            Assert.That(_runner.IsPaused, Is.True);
            Assert.That(_runner.State, Is.EqualTo(DrillState.Cue));

            _runner.ReportBackOnStrip();
            Wait(0.5);

            Assert.That(_runner.State, Is.EqualTo(DrillState.Window));
        }

        [Test]
        public void StripEventsWhileIdleOrNotPausedRaiseNothing()
        {
            _runner.ReportLeftStrip();
            _runner.ReportBackOnStrip();
            StartTestDrill();
            _runner.ReportBackOnStrip();

            Assert.That(_recorder.Events, Is.Empty);
            Assert.That(_runner.IsPaused, Is.False);
        }

        [Test]
        public void CancelEndsTheRunKeepingTheJudgedReps()
        {
            StartTestDrill();
            Wait(0.5);
            _runner.ReportContact(TestDrills.PointOn(TargetZone.Thigh));
            Wait(2.0);
            Wait(0.5);

            _runner.Cancel();

            Assert.That(_runner.State, Is.EqualTo(DrillState.Done));
            var summary = _recorder.Summaries.Single();
            Assert.That(summary.WasCancelled, Is.True);
            Assert.That(summary.Results.Count, Is.EqualTo(1));

            Wait(5.0);
            Assert.That(_recorder.Judged, Has.Count.EqualTo(1));
        }

        [Test]
        public void CancelWhilePausedEndsTheRun()
        {
            StartTestDrill();
            _runner.ReportLeftStrip();

            _runner.Cancel();

            Assert.That(_runner.State, Is.EqualTo(DrillState.Done));
            Assert.That(_runner.IsPaused, Is.False);
            Assert.That(_recorder.Summaries.Single().WasCancelled, Is.True);
        }

        [Test]
        public void CancelWhenNothingIsRunningDoesNothing()
        {
            _runner.Cancel();

            Assert.That(_runner.State, Is.EqualTo(DrillState.Idle));
            Assert.That(_recorder.Summaries, Is.Empty);
        }

        [Test]
        public void StartingADrillThatDoesNotFitLeavesTheRunnerIdle()
        {
            var availability = _runner.Start(TestDrills.Create(), stripLengthMeters: 2.0f, shrinkDrillsToFit: false);

            Assert.That(availability.IsAvailable, Is.False);
            Assert.That(availability.RequiredStripLengthMeters, Is.EqualTo(TestDrills.RequiredLength));
            Assert.That(_runner.State, Is.EqualTo(DrillState.Idle));
            Assert.That(_recorder.States, Is.Empty);
        }

        [Test]
        public void ShrunkRunTagsEveryRep()
        {
            var availability = _runner.Start(TestDrills.Create(repCount: 2), stripLengthMeters: 2.0f, shrinkDrillsToFit: true);
            Wait(0.5);
            _runner.ReportContact(TestDrills.PointOn(TargetZone.Thigh));
            Wait(2.0);
            Wait(0.5);
            Wait(1.5);

            Assert.That(availability.IsShrunk, Is.True);
            Assert.That(_runner.IsShrunk, Is.True);
            Assert.That(_recorder.Judged.Select(result => result.IsShrunk), Is.EqualTo(new[] { true, true }));
            Assert.That(_recorder.Summaries.Single().IsShrunk, Is.True);
        }

        [Test]
        public void StartingWhileRunningThrows()
        {
            StartTestDrill();

            Assert.Throws<InvalidOperationException>(() => StartTestDrill());
        }

        [Test]
        public void ADoneRunnerCanStartAgainWithFreshResults()
        {
            StartTestDrill(repCount: 1);
            Wait(0.5);
            _runner.ReportContact(TestDrills.PointOn(TargetZone.Hand));
            Assert.That(_runner.State, Is.EqualTo(DrillState.Done));

            StartTestDrill(repCount: 1);

            Assert.That(_runner.State, Is.EqualTo(DrillState.Cue));
            Assert.That(_runner.Results, Is.Empty);
            Assert.That(_recorder.Summaries.Single().Results.Count, Is.EqualTo(1));
        }
    }
}
