// The Drill runner waiting for the Fencer at the Start Mark between Reps (PER-89), with fake Start Mark reports.
// Test Drill timing: cue 0.5 s, window 1.5 s, rest 2.0 s, 3 Reps, at lunge distance (the Fencer moves).
using System;
using System.Linq;
using NUnit.Framework;
using VrFencing.Drills;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Tests.Drills
{
    public class DrillRunnerStartMarkTests
    {
        private const float LongStrip = 4.0f;

        private FakeClock _clock;
        private DrillRunner _runner;
        private RunnerRecorder _recorder;
        private int _prompts;

        [SetUp]
        public void CreateRunner()
        {
            _clock = new FakeClock();
            _runner = new DrillRunner(_clock, new Random(1234));
            _recorder = new RunnerRecorder(_runner);
            _prompts = 0;
            _runner.ReturnPrompted += () => _prompts++;
        }

        private void Wait(double seconds)
        {
            _clock.Advance(seconds);
            _runner.Tick();
        }

        private void Start(DrillDefinition drill, bool shrink = false, float strip = LongStrip)
        {
            Assert.That(_runner.Start(drill, strip, shrink).IsAvailable, Is.True);
        }

        // The first Rep: cue, then a Touch 0.5 s into the window while the Fencer is lunging (away from the mark).
        private void LungeAndTouch()
        {
            Wait(0.5);
            _runner.ReportAtStartMark(false);
            Wait(0.5);
            _runner.ReportContact(TestDrills.PointOn(TargetZone.Thigh));
        }

        [Test]
        public void AfterTheRepTheRunWaitsForTheFencerWithNoTimeLimit()
        {
            Start(TestDrills.Create());
            LungeAndTouch();

            Assert.That(_runner.State, Is.EqualTo(DrillState.Return));
            Wait(30.0);

            Assert.That(_runner.State, Is.EqualTo(DrillState.Return));
            Assert.That(_recorder.Cues, Has.Count.EqualTo(1), "No next cue while the Fencer is away.");
        }

        [Test]
        public void TheRestStartsOnceTheFencerIsBack()
        {
            Start(TestDrills.Create());
            LungeAndTouch();
            Wait(3.0);

            _runner.ReportAtStartMark(true);
            Assert.That(_runner.State, Is.EqualTo(DrillState.Rest));

            Wait(TestDrills.RestSeconds - 0.01);
            Assert.That(_runner.State, Is.EqualTo(DrillState.Rest), "The rest counts from the return, not from the judgement.");

            Wait(0.01);
            Assert.That(_runner.State, Is.EqualTo(DrillState.Cue));
            Assert.That(_recorder.Cues, Has.Count.EqualTo(2));
        }

        [Test]
        public void TheFencerIsAskedBackOnceAfterTwoSeconds()
        {
            Start(TestDrills.Create());
            LungeAndTouch();

            Wait(DrillRunner.ReturnPromptSeconds - 0.01);
            Assert.That(_prompts, Is.Zero);
            Assert.That(_runner.IsPromptingReturn, Is.False);

            Wait(0.01);
            Wait(5.0);
            Assert.That(_prompts, Is.EqualTo(1));
            Assert.That(_runner.IsPromptingReturn, Is.True);

            _runner.ReportAtStartMark(true);
            Assert.That(_runner.IsPromptingReturn, Is.False);
        }

        [Test]
        public void AFencerBackWithinTwoSecondsIsNotAsked()
        {
            Start(TestDrills.Create());
            LungeAndTouch();
            Wait(1.5);
            _runner.ReportAtStartMark(true);

            Wait(5.0);

            Assert.That(_prompts, Is.Zero);
        }

        [Test]
        public void AFencerAlreadyBackAtTheJudgementGoesStraightToTheRest()
        {
            Start(TestDrills.Create());
            Wait(1.0);
            _runner.ReportContact(TestDrills.PointOn(TargetZone.Thigh));

            Assert.That(_recorder.States, Has.No.Member(DrillState.Return));
            Assert.That(_runner.State, Is.EqualTo(DrillState.Rest));
        }

        [Test]
        public void NothingReportingTheStartMarkNeverHoldsTheRun()
        {
            // No head tracking: the run keeps its fixed rhythm.
            Start(TestDrills.Create());
            Wait(2.0);
            Wait(2.0);

            Assert.That(_recorder.States, Has.No.Member(DrillState.Return));
            Assert.That(_recorder.Cues, Has.Count.EqualTo(2));
        }

        [Test]
        public void ADrillAtExtensionDistanceDoesNotWait()
        {
            Start(DrillCatalog.CreatePointControlDrill(new TrainerSettings()));
            _runner.ReportAtStartMark(false);
            _runner.ReportContact(TestDrills.PointOn(_runner.CurrentCue.TargetZone.Value));

            Assert.That(_runner.State, Is.EqualTo(DrillState.Rest));
        }

        [Test]
        public void AShortenedVariantAtExtensionDistanceDoesNotWait()
        {
            Start(DrillCatalog.CreateReactionDrill(new TrainerSettings()), shrink: true, strip: 1.5f);
            Assert.That(_runner.IsShrunk, Is.True);
            _runner.ReportAtStartMark(false);
            _runner.ReportContact(TestDrills.PointOn(_runner.CurrentCue.TargetZone.Value));

            Assert.That(_runner.State, Is.EqualTo(DrillState.Rest));
        }

        [Test]
        public void APauseWhileWaitingKeepsWaiting()
        {
            Start(TestDrills.Create());
            LungeAndTouch();

            _runner.ReportLeftStrip();
            Wait(10.0);
            _runner.ReportBackOnStrip();

            Assert.That(_runner.State, Is.EqualTo(DrillState.Return));
            Assert.That(_recorder.Cues, Has.Count.EqualTo(1));
            Assert.That(_prompts, Is.Zero, "Paused time does not count toward the prompt.");
        }

        [Test]
        public void TheLastRepEndsTheRunWithoutWaiting()
        {
            Start(TestDrills.Create(repCount: 1));
            LungeAndTouch();

            Assert.That(_runner.State, Is.EqualTo(DrillState.Done));
        }

        [TestCase(DrillCatalog.PointControlDrillId, false)]
        [TestCase(DrillCatalog.LungeToTargetDrillId, true)]
        [TestCase(DrillCatalog.ReactionDrillId, true)]
        [TestCase(DrillCatalog.DistanceChoiceDrillId, true)]
        [TestCase(DrillCatalog.CompoundDrillId, true)]
        [TestCase(DrillCatalog.FootworkCallsDrillId, true)]
        public void DrillsTwoToSixWaitAtTheStartMark(string drillId, bool waits)
        {
            var drill = DrillCatalog.CreateDefault(includeTestDrills: false).BuildAll(new TrainerSettings()).Single(built => built.Id == drillId);

            Assert.That(drill.WaitsAtStartMark(isShrunk: false), Is.EqualTo(waits));
        }
    }
}
