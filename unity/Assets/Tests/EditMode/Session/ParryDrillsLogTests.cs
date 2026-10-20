// What the Parry Drills 7 to 9 (PER-100) add to the rep log and the results: the Armed Dummy's target and speed per
// Rep, a hit map per target, the lines parried and the hits on the Fencer by the body that judged them. Lines and
// records written before these existed still read, with the new fields empty.
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Drills;
using VrFencing.Dummy;
using VrFencing.Session;
using static VrFencing.Tests.Session.SessionTestData;

namespace VrFencing.Tests.Session
{
    public class ParryDrillsLogTests
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

        private static RepLogEntry Parried(int repIndex, AttackTarget target, ParryLine line, RepOutcome outcome = RepOutcome.Success)
        {
            var result = new RepResult(
                DrillCatalog.SlowParryDrillId, repIndex, requestedZone: null, outcome, contactZone: TargetZone.Body, reactionSeconds: 2.5,
                tipSpeedMetersPerSecond: 1.5f, distanceFromZoneCenterMeters: 0.03f, isShrunk: false, dummyDistance: DummyDistance.Extension,
                parryLine: line, riposteSeconds: 0.4, attackTarget: target, attackSpeed: AttackSpeed.Slow);
            return RepLogEntry.FromRepResult(result, Noon, SessionId, flagged: false, armPose: null);
        }

        private static RepLogEntry Hit(int repIndex, AttackTarget target, FencerHitSource source)
        {
            var result = new RepResult(
                DrillCatalog.SlowParryDrillId, repIndex, requestedZone: null, RepOutcome.NotParried, contactZone: null, reactionSeconds: null,
                tipSpeedMetersPerSecond: null, distanceFromZoneCenterMeters: null, isShrunk: false, dummyDistance: DummyDistance.Extension,
                fencerHit: new FencerHit(source, BodyPart.Torso, Vector3.Zero), attackTarget: target, attackSpeed: AttackSpeed.Slow);
            return RepLogEntry.FromRepResult(result, Noon, SessionId, flagged: false, armPose: null);
        }

        private static DrillResults SampleResults()
        {
            return DrillResults.Aggregate(new[]
            {
                Parried(0, AttackTarget.Hand, ParryLine.Six),
                Parried(1, AttackTarget.Hand, ParryLine.Four, RepOutcome.NoRiposte),
                Hit(2, AttackTarget.Chest, FencerHitSource.Iobt),
                Hit(3, AttackTarget.Forearm, FencerHitSource.HeadsetCapsules),
                Parried(4, AttackTarget.Chest, ParryLine.Six),
            });
        }

        [Test]
        public void AParryRepReadsBackWithItsAttackTargetAndSpeed()
        {
            var path = _folder.File(RepLogFile.FileName);

            RepLogFile.Append(path, Parried(0, AttackTarget.Forearm, ParryLine.Four));
            var entry = RepLogFile.Read(path).Items.Single();

            Assert.That(entry.AttackTarget, Is.EqualTo(AttackTarget.Forearm));
            Assert.That(entry.AttackSpeed, Is.EqualTo(AttackSpeed.Slow));
            Assert.That(entry.ParryLine, Is.EqualTo(ParryLine.Four));
            Assert.That(entry.TargetZone, Is.Null);
            Assert.That(File.ReadAllText(path), Does.Contain("\"attackTarget\":\"Forearm\"").And.Contain("\"attackSpeed\":\"Slow\""));
            Assert.That(entry.WithFlagged(true).AttackTarget, Is.EqualTo(AttackTarget.Forearm), "The flag keeps the attack.");
        }

        [Test]
        public void ALineWrittenBeforeTheParryDrillsReadsTheAttackAsEmpty()
        {
            var path = _folder.File(RepLogFile.FileName);
            File.AppendAllText(path, "{\"schema\":1,\"timestamp\":\"2026-10-19T12:00:00+00:00\",\"sessionId\":\"s\",\"drillId\":\"d\",\"repIndex\":0,"
                                     + "\"targetZone\":\"Body\",\"result\":\"Success\",\"isShrunk\":false,\"flagged\":false,\"armPose\":null}\n");

            var entry = RepLogFile.Read(path).Items.Single();

            Assert.That(entry.AttackTarget, Is.Null);
            Assert.That(entry.AttackSpeed, Is.Null);
        }

        [Test]
        public void ParryResultsHaveAHitMapPerAttackTargetAndCountLinesAndHits()
        {
            var results = SampleResults();

            Assert.That(results.RepCount, Is.EqualTo(5));
            Assert.That(results.SuccessCount, Is.EqualTo(2));
            Assert.That(results.ZoneHits, Is.Empty, "No zone was cued: the attack was the cue.");
            Assert.That(results.FootworkHits, Is.Empty);
            Assert.That(results.AttackHits, Is.EqualTo(new[]
            {
                new AttackHitCount(AttackTarget.Hand, 2, 1),
                new AttackHitCount(AttackTarget.Forearm, 1, 0),
                new AttackHitCount(AttackTarget.Chest, 2, 1),
            }));
            Assert.That(results.ParryLines, Is.EqualTo(new[] { new ParryLineCount(ParryLine.Four, 1), new ParryLineCount(ParryLine.Six, 2) }));
            Assert.That(results.FencerHits, Is.EqualTo(new[]
            {
                new FencerHitCount(FencerHitSource.Iobt, 1), new FencerHitCount(FencerHitSource.HeadsetCapsules, 1),
            }));
        }

        [Test]
        public void CombiningParryResultsSumsEveryCount()
        {
            var combined = DrillResults.Combine(new[] { SampleResults(), SampleResults() });

            Assert.That(combined.RepCount, Is.EqualTo(10));
            Assert.That(combined.AttackHits.Single(hits => hits.Target == AttackTarget.Hand), Is.EqualTo(new AttackHitCount(AttackTarget.Hand, 4, 2)));
            Assert.That(combined.ParryLines.Single(count => count.Line == ParryLine.Six).Count, Is.EqualTo(4));
            Assert.That(combined.FencerHits.Sum(count => count.Count), Is.EqualTo(4));
        }

        [Test]
        public void ParryResultsSaveAndLoadInTheSessionHistory()
        {
            var path = _folder.File(SessionHistoryFile.FileName);
            var results = SampleResults();

            SessionHistoryFile.Append(path, new DrillRunRecord(SessionId, Noon, Noon.AddMinutes(1), wasCancelled: false, results));
            var read = SessionHistoryFile.Read(path).Items.Single().Results;

            Assert.That(read.AttackHits, Is.EqualTo(results.AttackHits));
            Assert.That(read.ParryLines, Is.EqualTo(results.ParryLines));
            Assert.That(read.FencerHits, Is.EqualTo(results.FencerHits));
        }

        [Test]
        public void AHistoryRecordFromBeforeTheParryDrillsReadsWithNoParryCounts()
        {
            var path = _folder.File(SessionHistoryFile.FileName);
            var older = DrillResults.Aggregate(new[] { Touch(TargetZone.Hand, 0.5, 1.2f) });

            SessionHistoryFile.Append(path, new DrillRunRecord(SessionId, Noon, Noon.AddMinutes(1), wasCancelled: false, older));
            var json = File.ReadAllText(path)
                .Replace(",\"attackHits\":[]", string.Empty)
                .Replace(",\"parryLines\":[]", string.Empty)
                .Replace(",\"fencerHits\":[]", string.Empty);
            File.WriteAllText(path, json);
            var read = SessionHistoryFile.Read(path).Items.Single().Results;

            Assert.That(json, Does.Not.Contain("attackHits"));
            Assert.That(read.AttackHits, Is.Empty);
            Assert.That(read.ParryLines, Is.Empty);
            Assert.That(read.FencerHits, Is.Empty);
            Assert.That(read.ZoneHits.Single().Zone, Is.EqualTo(TargetZone.Hand));
        }
    }
}
