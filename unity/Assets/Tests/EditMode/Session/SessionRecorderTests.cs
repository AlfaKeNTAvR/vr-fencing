// The Session recorder (PER-35) driven by a real Drill runner: Reps reach the rep log with a wall-clock
// timestamp and the flag button, and each finished Drill run is saved to the history.
// Test Drill timing (from the Drill tests): cue 0.5 s, window 1.5 s, rest 2.0 s, 3 Reps on the thigh.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Drills;
using VrFencing.Dummy;
using VrFencing.Session;
using VrFencing.Settings;
using VrFencing.Tests.Drills;

namespace VrFencing.Tests.Session
{
    public class SessionRecorderTests
    {
        private const float LongStrip = 4.0f;

        private ScratchFolder _folder;
        private FakeWallClock _wallClock;
        private FakeClock _drillClock;
        private DrillRunner _runner;
        private SessionRecorder _recorder;
        private List<DrillRunRecord> _recorded;

        [SetUp]
        public void CreateRecorder()
        {
            _folder = new ScratchFolder();
            _wallClock = new FakeWallClock();
            _drillClock = new FakeClock();
            _runner = new DrillRunner(_drillClock, new Random(1234));
            _recorder = CreateRecorder(armPoseSource: null);
        }

        [TearDown]
        public void DeleteScratchFolder()
        {
            _folder.Dispose();
        }

        private SessionRecorder CreateRecorder(Func<ArmPoseSnapshot> armPoseSource)
        {
            var recorder = new SessionRecorder(_folder.Path, "session-a", _wallClock, armPoseSource);
            _recorded = new List<DrillRunRecord>();
            recorder.DrillRunRecorded += record => _recorded.Add(record);
            recorder.Attach(_runner);
            return recorder;
        }

        private void Wait(double seconds)
        {
            _drillClock.Advance(seconds);
            _wallClock.Advance(seconds);
            _runner.Tick();
        }

        private void StartDrill()
        {
            Assert.That(_runner.Start(TestDrills.Create(repCount: 3), LongStrip, shrinkDrillsToFit: false).IsAvailable, Is.True);
        }

        // From a fresh cue: wait out the cue, then Touch (or miss) the thigh.
        private void PlayRep(bool touch)
        {
            Wait(TestDrills.CueSeconds);
            if (touch)
            {
                Wait(0.25);
                _runner.ReportContact(TestDrills.PointOn(TargetZone.Thigh));
            }
            else
            {
                Wait(TestDrills.WindowSeconds);
            }
        }

        private void RestUntilNextCue()
        {
            Wait(TestDrills.RestSeconds);
        }

        private IReadOnlyList<RepLogEntry> LoggedReps()
        {
            var read = RepLogFile.Read(_recorder.RepLogPath);
            Assert.That(read.SkippedLineCount, Is.EqualTo(0));
            return read.Items;
        }

        [Test]
        public void FilesUseTheSessionNamesInTheDataFolder()
        {
            Assert.That(_recorder.RepLogPath, Is.EqualTo(Path.Combine(_folder.Path, "session-reps.jsonl")));
            Assert.That(_recorder.HistoryPath, Is.EqualTo(Path.Combine(_folder.Path, "session-history.jsonl")));
            Assert.That(_recorder.SessionStartedAt, Is.EqualTo(SessionTestData.Noon));
        }

        [Test]
        public void JudgedRepIsWrittenWithItsJudgingTimeOnceTheNextRepIsCued()
        {
            StartDrill();
            PlayRep(touch: true);
            var judgedAt = _wallClock.UtcNow;

            Assert.That(LoggedReps(), Is.Empty, "The Rep waits through the rest so the flag button can still mark it.");

            RestUntilNextCue();

            var entry = LoggedReps().Single();
            Assert.That(entry.Timestamp, Is.EqualTo(judgedAt));
            Assert.That(entry.SessionId, Is.EqualTo("session-a"));
            Assert.That(entry.DrillId, Is.EqualTo("lunge-to-target"));
            Assert.That(entry.RepIndex, Is.EqualTo(0));
            Assert.That(entry.Result, Is.EqualTo(RepOutcome.Success));
            Assert.That(entry.ReactionSeconds, Is.EqualTo(0.25).Within(1e-9));
            Assert.That(entry.Flagged, Is.False);
        }

        [Test]
        public void FlagDuringRestMarksTheRepJustJudged()
        {
            StartDrill();
            PlayRep(touch: true);
            Wait(0.5);
            _recorder.FlagRep();
            RestUntilNextCue();
            PlayRep(touch: true);
            RestUntilNextCue();

            Assert.That(LoggedReps().Select(entry => entry.Flagged).ToArray(), Is.EqualTo(new[] { true, false }));
        }

        [Test]
        public void FlagDuringTheWindowMarksThatRep()
        {
            StartDrill();
            PlayRep(touch: true);
            RestUntilNextCue();
            Wait(TestDrills.CueSeconds + 0.1);
            _recorder.FlagRep();
            _runner.ReportContact(TestDrills.PointOn(TargetZone.Thigh));
            RestUntilNextCue();

            Assert.That(LoggedReps().Select(entry => entry.Flagged).ToArray(), Is.EqualTo(new[] { false, true }));
        }

        [Test]
        public void LastRepIsWrittenOnFlushAndCanStillBeFlagged()
        {
            StartDrill();
            PlayRep(touch: true);
            RestUntilNextCue();
            PlayRep(touch: false);
            RestUntilNextCue();
            PlayRep(touch: true);
            _recorder.FlagRep();

            Assert.That(LoggedReps(), Has.Count.EqualTo(2));

            _recorder.Flush();
            _recorder.Flush();

            var reps = LoggedReps();
            Assert.That(reps.Select(entry => entry.RepIndex).ToArray(), Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(reps.Select(entry => entry.Result).ToArray(), Is.EqualTo(new[] { RepOutcome.Success, RepOutcome.Timeout, RepOutcome.Success }));
            Assert.That(reps[2].Flagged, Is.True);
        }

        [Test]
        public void FinishedRunIsSavedToTheHistoryWithItsResults()
        {
            StartDrill();
            PlayRep(touch: true);
            RestUntilNextCue();
            PlayRep(touch: false);
            RestUntilNextCue();
            PlayRep(touch: true);

            var saved = SessionHistoryFile.Read(_recorder.HistoryPath).Items;
            Assert.That(saved, Has.Count.EqualTo(1));
            Assert.That(_recorded, Has.Count.EqualTo(1));
            var record = saved[0];
            Assert.That(record.SessionId, Is.EqualTo("session-a"));
            Assert.That(record.SessionStartedAt, Is.EqualTo(SessionTestData.Noon));
            Assert.That(record.FinishedAt, Is.EqualTo(_wallClock.UtcNow));
            Assert.That(record.WasCancelled, Is.False);
            Assert.That(record.Results.DrillId, Is.EqualTo("lunge-to-target"));
            Assert.That(record.Results.RepCount, Is.EqualTo(3));
            Assert.That(record.Results.SuccessCount, Is.EqualTo(2));
            Assert.That(record.Results.AverageReactionSeconds, Is.EqualTo(0.25).Within(1e-9));
            Assert.That(_recorded[0].Results.SuccessCount, Is.EqualTo(2));
        }

        [Test]
        public void SecondRunStartsItsOwnResults()
        {
            StartDrill();
            PlayRep(touch: true);
            _runner.Cancel();
            StartDrill();
            PlayRep(touch: false);
            _runner.Cancel();

            Assert.That(_recorded.Select(record => record.Results.SuccessCount).ToArray(), Is.EqualTo(new[] { 1, 0 }));
            Assert.That(_recorded.Select(record => record.Results.RepCount).ToArray(), Is.EqualTo(new[] { 1, 1 }));
            Assert.That(LoggedReps(), Has.Count.EqualTo(1), "The first run's Rep is written when the second run cues.");
        }

        [Test]
        public void CancelledRunKeepsItsJudgedReps()
        {
            StartDrill();
            PlayRep(touch: true);
            Wait(0.5);
            _runner.Cancel();
            _recorder.Flush();

            Assert.That(_recorded.Single().WasCancelled, Is.True);
            Assert.That(_recorded.Single().Results.RepCount, Is.EqualTo(1));
            Assert.That(LoggedReps(), Has.Count.EqualTo(1));
        }

        [Test]
        public void RunCancelledBeforeAnyRepSavesNothing()
        {
            StartDrill();
            Wait(0.2);
            _runner.Cancel();
            _recorder.Flush();

            Assert.That(_recorded, Is.Empty);
            Assert.That(File.Exists(_recorder.HistoryPath), Is.False);
            Assert.That(File.Exists(_recorder.RepLogPath), Is.False);
        }

        [Test]
        public void ArmPoseIsSampledWhenTheRepIsJudged()
        {
            _recorder.Detach(_runner);
            var samples = 0;
            _recorder = CreateRecorder(() =>
            {
                samples++;
                var pose = new TrackedPose(new Vector3(samples, 0.0f, 0.0f), Quaternion.Identity);
                return new ArmPoseSnapshot(pose, pose, pose);
            });

            StartDrill();
            PlayRep(touch: true);
            RestUntilNextCue();

            Assert.That(samples, Is.EqualTo(1));
            Assert.That(LoggedReps().Single().ArmPose.Elbow.Value.Position.X, Is.EqualTo(1.0f));
        }

        [Test]
        public void WriteFailureIsReportedAndTheDrillCarriesOn()
        {
            Directory.CreateDirectory(_recorder.RepLogPath);
            var failures = new List<Exception>();
            _recorder.WriteFailed += failures.Add;

            StartDrill();
            PlayRep(touch: true);
            RestUntilNextCue();

            Assert.That(failures, Has.Count.EqualTo(1));
            Assert.That(_runner.State, Is.EqualTo(DrillState.Cue));
            Assert.That(_runner.CurrentRepIndex, Is.EqualTo(1));
        }

        [Test]
        public void DetachedRecorderWritesNothing()
        {
            _recorder.Detach(_runner);

            StartDrill();
            PlayRep(touch: true);
            RestUntilNextCue();
            _recorder.Flush();

            Assert.That(File.Exists(_recorder.RepLogPath), Is.False);
        }
    }
}
