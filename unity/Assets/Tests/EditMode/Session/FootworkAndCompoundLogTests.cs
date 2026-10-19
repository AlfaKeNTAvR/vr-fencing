// What Drills 4 to 6 (PER-39) add to the rep log and the results: the footwork read, the called action,
// a compound's second zone and timing, the Rep's Dummy Distance, and a hit map per footwork call.
// Lines and records written before these existed still read, with the new fields empty.
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VrFencing.Drills;
using VrFencing.Dummy;
using VrFencing.Session;
using static VrFencing.Tests.Session.SessionTestData;

namespace VrFencing.Tests.Session
{
    public class FootworkAndCompoundLogTests
    {
        private ScratchFolder _folder;

        [SetUp]
        public void CreateScratchFolder()
        {
            _folder = new ScratchFolder();
        }

        [TearDown]
        public void DeleteScratchFolder()
        {
            _folder.Dispose();
        }

        private static RepResult FootworkCallResult(FootworkAction call, FootworkAction read, RepOutcome outcome, int repIndex = 0)
        {
            var footwork = new FootworkReading(read, 0.42, forwardTravelMeters: 0.31f, backwardTravelMeters: 0.02f, peakForwardSpeedMetersPerSecond: 1.4f, headDropMeters: 0.03f);
            return new RepResult(
                DrillCatalog.FootworkCallsDrillId, repIndex, requestedZone: null, outcome, contactZone: null,
                reactionSeconds: outcome == RepOutcome.Timeout ? (double?)null : 0.42, tipSpeedMetersPerSecond: null, distanceFromZoneCenterMeters: null,
                isShrunk: false, requestedFootwork: call, footwork: footwork, dummyDistance: DummyDistance.StepLunge);
        }

        private static RepLogEntry Logged(RepResult result)
        {
            return RepLogEntry.FromRepResult(result, Noon, SessionId, flagged: false, armPose: null);
        }

        [Test]
        public void AFootworkCallRepReadsBackWithItsCallAndFootwork()
        {
            var path = _folder.File(RepLogFile.FileName);

            RepLogFile.Append(path, Logged(FootworkCallResult(FootworkAction.Advance, FootworkAction.Check, RepOutcome.WrongFootwork)));
            var entry = RepLogFile.Read(path).Items.Single();

            Assert.That(entry.TargetZone, Is.Null);
            Assert.That(entry.Result, Is.EqualTo(RepOutcome.WrongFootwork));
            Assert.That(entry.FootworkCall, Is.EqualTo(FootworkAction.Advance));
            Assert.That(entry.DummyDistance, Is.EqualTo(DummyDistance.StepLunge));
            Assert.That(entry.Footwork.Action, Is.EqualTo(FootworkAction.Check));
            Assert.That(entry.Footwork.OnsetSeconds, Is.EqualTo(0.42));
            Assert.That(entry.Footwork.ForwardTravelMeters, Is.EqualTo(0.31f));
            Assert.That(entry.Footwork.BackwardTravelMeters, Is.EqualTo(0.02f));
            Assert.That(entry.Footwork.PeakForwardSpeedMetersPerSecond, Is.EqualTo(1.4f));
            Assert.That(entry.Footwork.HeadDropMeters, Is.EqualTo(0.03f));
        }

        [Test]
        public void ACompoundRepReadsBackWithItsSecondZoneAndTiming()
        {
            var path = _folder.File(RepLogFile.FileName);
            var result = new RepResult(
                DrillCatalog.CompoundDrillId, 1, TargetZone.Hand, RepOutcome.Success, TargetZone.Thigh, 0.8, 2.5f, 0.03f, isShrunk: false,
                requestedSecondZone: TargetZone.Thigh, secondTouchSeconds: 0.55, dummyDistance: DummyDistance.Lunge);

            RepLogFile.Append(path, Logged(result).WithFlagged(true));
            var entry = RepLogFile.Read(path).Items.Single();

            Assert.That(entry.TargetZone, Is.EqualTo(TargetZone.Hand));
            Assert.That(entry.SecondTargetZone, Is.EqualTo(TargetZone.Thigh));
            Assert.That(entry.SecondTouchSeconds, Is.EqualTo(0.55));
            Assert.That(entry.ContactZone, Is.EqualTo(TargetZone.Thigh));
            Assert.That(entry.Footwork, Is.Null);
            Assert.That(entry.FootworkCall, Is.Null);
            Assert.That(entry.Flagged, Is.True, "Flagging keeps the new fields.");
            Assert.That(entry.DummyDistance, Is.EqualTo(DummyDistance.Lunge));
        }

        [Test]
        public void TheNewFieldsAreWrittenWithTheirDocumentedNames()
        {
            var path = _folder.File(RepLogFile.FileName);

            RepLogFile.Append(path, Logged(FootworkCallResult(FootworkAction.Lunge, FootworkAction.Lunge, RepOutcome.Success)));
            var line = File.ReadAllText(path);

            foreach (var field in new[]
            {
                "\"targetZone\":null", "\"secondTargetZone\":null", "\"secondTouchSeconds\":null", "\"footworkCall\":\"Lunge\"",
                "\"dummyDistance\":\"StepLunge\"", "\"footwork\":{\"action\":\"Lunge\",\"onsetSeconds\":0.42,\"forwardTravelMeters\":",
                "\"backwardTravelMeters\":", "\"peakForwardSpeedMetersPerSecond\":", "\"headDropMeters\":",
            })
            {
                Assert.That(line, Does.Contain(field));
            }
        }

        [Test]
        public void ALineWrittenBeforeDrillsFourToSixReadsTheirFieldsAsEmpty()
        {
            var path = _folder.File(RepLogFile.FileName);
            File.AppendAllText(path, "{\"schema\":1,\"timestamp\":\"2026-10-19T12:00:00+00:00\",\"sessionId\":\"s\",\"drillId\":\"d\",\"repIndex\":0,"
                                     + "\"targetZone\":\"Hand\",\"result\":\"Success\",\"isShrunk\":false,\"flagged\":false,\"armPose\":null}\n");

            var read = RepLogFile.Read(path);

            Assert.That(read.SkippedLineCount, Is.EqualTo(0));
            var entry = read.Items.Single();
            Assert.That(entry.TargetZone, Is.EqualTo(TargetZone.Hand));
            Assert.That(entry.SecondTargetZone, Is.Null);
            Assert.That(entry.SecondTouchSeconds, Is.Null);
            Assert.That(entry.FootworkCall, Is.Null);
            Assert.That(entry.Footwork, Is.Null);
            Assert.That(entry.DummyDistance, Is.Null);
        }

        [Test]
        public void ALineWithoutATargetZoneFieldIsStillSkipped()
        {
            var path = _folder.File(RepLogFile.FileName);
            File.AppendAllText(path, "{\"schema\":1,\"timestamp\":\"2026-10-19T12:00:00+00:00\",\"sessionId\":\"s\",\"drillId\":\"d\",\"repIndex\":0,"
                                     + "\"result\":\"Success\"}\n");

            Assert.That(RepLogFile.Read(path).SkippedLineCount, Is.EqualTo(1), "A footwork call writes targetZone as null; it is never left out.");
        }

        [Test]
        public void FootworkCallResultsHaveAHitMapPerCall()
        {
            var results = DrillResults.Aggregate(new[]
            {
                Logged(FootworkCallResult(FootworkAction.Advance, FootworkAction.Advance, RepOutcome.Success, 0)),
                Logged(FootworkCallResult(FootworkAction.Advance, FootworkAction.Check, RepOutcome.WrongFootwork, 1)),
                Logged(FootworkCallResult(FootworkAction.Lunge, FootworkAction.Lunge, RepOutcome.Success, 2)),
                Logged(FootworkCallResult(FootworkAction.Check, FootworkAction.None, RepOutcome.Timeout, 3)),
            });

            Assert.That(results.ZoneHits, Is.Empty);
            Assert.That(results.FootworkHits, Is.EqualTo(new[]
            {
                new FootworkHitCount(FootworkAction.Advance, 2, 1),
                new FootworkHitCount(FootworkAction.Lunge, 1, 1),
                new FootworkHitCount(FootworkAction.Check, 1, 0),
            }));
            Assert.That(results.SuccessCount, Is.EqualTo(2));
            Assert.That(results.AverageReactionSeconds, Is.EqualTo(0.42).Within(1e-9));
            Assert.That(results.AverageTipSpeedMetersPerSecond, Is.Null);
        }

        [Test]
        public void FootworkHitMapsAddUpAcrossRuns()
        {
            var first = DrillResults.Aggregate(new[] { Logged(FootworkCallResult(FootworkAction.Retreat, FootworkAction.Retreat, RepOutcome.Success)) });
            var second = DrillResults.Aggregate(new[] { Logged(FootworkCallResult(FootworkAction.Retreat, FootworkAction.None, RepOutcome.Timeout)) });

            var combined = DrillResults.Combine(new[] { first, second });

            Assert.That(combined.FootworkHits, Is.EqualTo(new[] { new FootworkHitCount(FootworkAction.Retreat, 2, 1) }));
            Assert.That(combined.RepCount, Is.EqualTo(2));
        }

        [Test]
        public void AFootworkCallRunIsSavedAndReadBackWithItsHitMap()
        {
            var path = _folder.File(SessionHistoryFile.FileName);
            var results = DrillResults.Aggregate(new[] { Logged(FootworkCallResult(FootworkAction.Check, FootworkAction.Check, RepOutcome.Success)) });

            SessionHistoryFile.Append(path, new DrillRunRecord(SessionId, Noon, Noon.AddMinutes(1), wasCancelled: false, results));
            var read = SessionHistoryFile.Read(path).Items.Single().Results;

            Assert.That(read.FootworkHits, Is.EqualTo(new[] { new FootworkHitCount(FootworkAction.Check, 1, 1) }));
            Assert.That(read.ZoneHits, Is.Empty);
        }

        [Test]
        public void ResultsSavedBeforeFootworkCallsReadWithAnEmptyFootworkHitMap()
        {
            var path = _folder.File(SessionHistoryFile.FileName);
            File.AppendAllText(path, "{\"schema\":1,\"sessionId\":\"s\",\"sessionStartedAt\":\"2026-10-19T12:00:00+00:00\","
                                     + "\"finishedAt\":\"2026-10-19T12:05:00+00:00\",\"wasCancelled\":false,\"results\":{\"drillId\":\"d\","
                                     + "\"repCount\":1,\"successCount\":1,\"averageReactionSeconds\":0.5,\"averageTipSpeedMetersPerSecond\":3.0,"
                                     + "\"isShrunk\":false,\"zoneHits\":[{\"zone\":\"Hand\",\"attempts\":1,\"touches\":1}]}}\n");

            var read = SessionHistoryFile.Read(path);

            Assert.That(read.SkippedLineCount, Is.EqualTo(0));
            Assert.That(read.Items.Single().Results.FootworkHits, Is.Empty);
            Assert.That(read.Items.Single().Results.ZoneHits.Single().Zone, Is.EqualTo(TargetZone.Hand));
        }

        [Test]
        public void HitMapsMustStillAccountForEveryRep()
        {
            Assert.Throws<ArgumentException>(() => new DrillResults(
                "d", repCount: 2, successCount: 1, 0.5, null, isShrunk: false,
                new ZoneHitCount[0], new[] { new FootworkHitCount(FootworkAction.Advance, 1, 1) }));
        }
    }
}
