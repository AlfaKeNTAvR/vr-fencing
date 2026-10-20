// The Paired Practice log (PER-83): a tally that ends is saved beside the session history, from this Fencer's side;
// an empty one is not.
using NUnit.Framework;
using VrFencing.Session;

namespace VrFencing.Tests.Session
{
    public class PairedPracticeLogTests
    {
        private ScratchFolder _folder;

        [SetUp]
        public void CreateFolder()
        {
            _folder = new ScratchFolder();
        }

        [TearDown]
        public void DeleteFolder()
        {
            _folder.Dispose();
        }

        [Test]
        public void AnEndedTallyIsSavedAndReadsBack()
        {
            var clock = new FakeWallClock();
            var recorder = new SessionRecorder(_folder.Path, "session-p", clock);

            var saved = recorder.RecordPairedPractice("Partner", PairedPracticeEnd.Reset, new PairedPracticeCounts(3, 1, 2), new PairedPracticeCounts(1, 1, 0));
            var read = PairedPracticeLogFile.Read(recorder.PairedPracticePath);

            Assert.That(saved, Is.Not.Null);
            Assert.That(recorder.PairedPracticePath, Does.EndWith(PairedPracticeLogFile.FileName));
            Assert.That(read.SkippedLineCount, Is.EqualTo(0));
            var record = read.Items[0];
            Assert.That(record.SessionId, Is.EqualTo("session-p"));
            Assert.That(record.SavedAt, Is.EqualTo(clock.UtcNow));
            Assert.That(record.Role, Is.EqualTo("Partner"));
            Assert.That(record.End, Is.EqualTo(PairedPracticeEnd.Reset));
            Assert.That(record.You, Is.EqualTo(new PairedPracticeCounts(3, 1, 2)));
            Assert.That(record.Opponent, Is.EqualTo(new PairedPracticeCounts(1, 1, 0)));
        }

        [Test]
        public void AnEmptyTallyIsNotSaved()
        {
            var recorder = new SessionRecorder(_folder.Path, "session-p", new FakeWallClock());

            Assert.That(recorder.RecordPairedPractice("Host", PairedPracticeEnd.Disconnected, default, default), Is.Null);
            Assert.That(PairedPracticeLogFile.Read(recorder.PairedPracticePath).Items, Is.Empty);
        }
    }
}
