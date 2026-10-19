// Session history (PER-35, PER-27): one saved record per Drill run, and a last-10-sessions trend per Drill.
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VrFencing.Dummy;
using VrFencing.Session;
using static VrFencing.Tests.Session.SessionTestData;

namespace VrFencing.Tests.Session
{
    public class SessionHistoryTests
    {
        private const double Tolerance = 1e-6;

        private ScratchFolder _folder;
        private string _historyPath;

        [SetUp]
        public void CreateScratchFolder()
        {
            _folder = new ScratchFolder();
            _historyPath = _folder.File(SessionHistoryFile.FileName);
        }

        [TearDown]
        public void DeleteScratchFolder()
        {
            _folder.Dispose();
        }

        private static DateTimeOffset Day(int day)
        {
            return Noon.AddDays(day);
        }

        [Test]
        public void SavedRunReadsBackWithItsResults()
        {
            var written = Run("session-a", Day(0), repCount: 4, successCount: 3, averageReactionSeconds: 0.45);

            SessionHistoryFile.Append(_historyPath, written);
            var read = SessionHistoryFile.Read(_historyPath);

            Assert.That(read.SkippedLineCount, Is.EqualTo(0));
            Assert.That(read.Items, Has.Count.EqualTo(1));
            var record = read.Items[0];
            Assert.That(record.SessionId, Is.EqualTo("session-a"));
            Assert.That(record.SessionStartedAt, Is.EqualTo(Day(0)));
            Assert.That(record.FinishedAt, Is.EqualTo(written.FinishedAt));
            Assert.That(record.WasCancelled, Is.False);
            Assert.That(record.Results.DrillId, Is.EqualTo(DrillId));
            Assert.That(record.Results.RepCount, Is.EqualTo(4));
            Assert.That(record.Results.SuccessCount, Is.EqualTo(3));
            Assert.That(record.Results.SuccessPercent, Is.EqualTo(75.0).Within(Tolerance));
            Assert.That(record.Results.AverageReactionSeconds, Is.EqualTo(0.45).Within(Tolerance));
            Assert.That(record.Results.AverageTipSpeedMetersPerSecond, Is.EqualTo(3.0).Within(Tolerance));
            Assert.That(record.Results.IsShrunk, Is.False);
            Assert.That(record.Results.HitsFor(TargetZone.Thigh), Is.EqualTo(new ZoneHitCount(TargetZone.Thigh, attempts: 4, touches: 3)));
        }

        [Test]
        public void MissingHistoryReadsAsEmpty()
        {
            var read = SessionHistoryFile.Read(_folder.File("does-not-exist.jsonl"));

            Assert.That(read.Items, Is.Empty);
            Assert.That(read.SkippedLineCount, Is.EqualTo(0));
        }

        [Test]
        public void BrokenHistoryLinesAreSkippedAndCounted()
        {
            SessionHistoryFile.Append(_historyPath, Run("session-a", Day(0)));
            File.AppendAllText(_historyPath, "{\"schema\":1,\"sessionId\":\"session-b\"}\n");
            File.AppendAllText(_historyPath, "]]]\n");
            SessionHistoryFile.Append(_historyPath, Run("session-c", Day(2)));

            var read = SessionHistoryFile.Read(_historyPath);

            Assert.That(read.Items.Select(record => record.SessionId).ToArray(), Is.EqualTo(new[] { "session-a", "session-c" }));
            Assert.That(read.SkippedLineCount, Is.EqualTo(2));
        }

        [Test]
        public void TrendKeepsTheLastTenSessionsOldestFirst()
        {
            // Saved out of order on purpose: the trend sorts by when each session started.
            var records = Enumerable.Range(1, 12)
                .Reverse()
                .Select(day => Run($"session-{day:00}", Day(day), successCount: day % 11))
                .ToArray();

            var trend = new SessionHistory(records).GetTrend(DrillId, isShrunk: false);

            Assert.That(SessionHistory.TrendSessionCount, Is.EqualTo(10));
            Assert.That(
                trend.Select(point => point.SessionId).ToArray(),
                Is.EqualTo(Enumerable.Range(3, 10).Select(day => $"session-{day:00}").ToArray()));
            Assert.That(trend[0].SessionStartedAt, Is.EqualTo(Day(3)));
            Assert.That(trend.Last().Results.SuccessPercent, Is.EqualTo(10.0).Within(Tolerance));
        }

        [Test]
        public void TrendWithFewerSessionsShowsThemAll()
        {
            var records = new[] { Run("session-a", Day(0)), Run("session-b", Day(1)) };

            var trend = new SessionHistory(records).GetTrend(DrillId, isShrunk: false);

            Assert.That(trend.Select(point => point.SessionId).ToArray(), Is.EqualTo(new[] { "session-a", "session-b" }));
        }

        [Test]
        public void TrendCombinesSeveralRunsOfTheDrillInOneSession()
        {
            var records = new[]
            {
                Run("session-a", Day(0), repCount: 10, successCount: 2, averageReactionSeconds: 0.2),
                Run("session-a", Day(0), repCount: 10, successCount: 6, averageReactionSeconds: 0.6),
            };

            var trend = new SessionHistory(records).GetTrend(DrillId, isShrunk: false);

            Assert.That(trend, Has.Count.EqualTo(1));
            Assert.That(trend[0].Results.RepCount, Is.EqualTo(20));
            Assert.That(trend[0].Results.SuccessPercent, Is.EqualTo(40.0).Within(Tolerance));
            Assert.That(trend[0].Results.AverageReactionSeconds, Is.EqualTo(0.5).Within(Tolerance));
        }

        [Test]
        public void TrendLeavesOutOtherDrillsAndTheOtherStripVariant()
        {
            var records = new[]
            {
                Run("session-a", Day(0)),
                Run("session-b", Day(1), drillId: "point-control"),
                Run("session-c", Day(2), isShrunk: true),
                Run("session-d", Day(3)),
            };
            var history = new SessionHistory(records);

            Assert.That(history.GetTrend(DrillId, isShrunk: false).Select(point => point.SessionId).ToArray(), Is.EqualTo(new[] { "session-a", "session-d" }));
            Assert.That(history.GetTrend(DrillId, isShrunk: true).Select(point => point.SessionId).ToArray(), Is.EqualTo(new[] { "session-c" }));
            Assert.That(history.GetTrend("footwork-calls", isShrunk: false), Is.Empty);
        }

        [Test]
        public void TrendWindowCanBeNarrowed()
        {
            var records = Enumerable.Range(0, 5).Select(day => Run($"session-{day}", Day(day))).ToArray();

            var trend = new SessionHistory(records).GetTrend(DrillId, isShrunk: false, sessionCount: 3);

            Assert.That(trend.Select(point => point.SessionId).ToArray(), Is.EqualTo(new[] { "session-2", "session-3", "session-4" }));
        }
    }
}
