// What Drills 10 to 12 (PER-101) say on the Running panel and in the results, and what they log: the Beat's count and
// timing, the Stop Hit's results without a Parry line, and the lunge Drill's Riposte switch.
using System.IO;
using System.Linq;
using NUnit.Framework;
using VrFencing.Drills;
using VrFencing.Dummy;
using VrFencing.Session;
using VrFencing.Settings;
using VrFencing.Tests.Drills;
using VrFencing.UI;
using static VrFencing.Tests.Session.SessionTestData;

namespace VrFencing.Tests.UI
{
    public class ArmedDummyDrillsTenToTwelveTextTests
    {
        private static DrillRunner Start(string id)
        {
            var runner = new DrillRunner(new FakeClock(), new System.Random(4));
            var drill = DrillCatalog.CreateDefault(includeTestDrills: false).BuildAll(new TrainerSettings()).Single(built => built.Id == id);
            runner.Start(drill, 3.0f, shrinkDrillsToFit: false);
            return runner;
        }

        private static RepLogEntry BeatRep(int repIndex, bool hasBeat, RepOutcome outcome)
        {
            var result = new RepResult(
                DrillCatalog.BeatDrillId, repIndex, requestedZone: null, outcome, contactZone: outcome == RepOutcome.Success ? TargetZone.Body : (TargetZone?)null,
                reactionSeconds: 1.2, tipSpeedMetersPerSecond: 2.0f, distanceFromZoneCenterMeters: null, isShrunk: false, dummyDistance: DummyDistance.Lunge,
                touchAfterBeatSeconds: outcome == RepOutcome.Success ? 0.6 : (double?)null, beatOutOfLineMeters: hasBeat ? 0.2f : (float?)null);
            return RepLogEntry.FromRepResult(result, Noon, SessionId, flagged: false, armPose: null);
        }

        [Test]
        public void TheBeatDrillSaysBeatThenTouchNow()
        {
            var runner = Start(DrillCatalog.BeatDrillId);

            Assert.That(RunStatusText.FormatDummyLabel(runner.CurrentCue), Is.Empty, "The blade in line is the cue.");
            Assert.That(RunStatusText.Format(runner, isFencerOnStrip: true).Instruction, Is.EqualTo(RunStatusText.BeatText));
            runner.ReportBeat(new BeatReport(0.2f, 0.5f, 0.8f));
            Assert.That(RunStatusText.Format(runner, isFencerOnStrip: true).Instruction, Is.EqualTo(RunStatusText.TouchAfterBeatText));
        }

        [Test]
        public void TheStopHitDrillAsksForATouchOnTheArm()
        {
            Assert.That(RunStatusText.Format(Start(DrillCatalog.StopHitDrillId), isFencerOnStrip: true).Instruction, Is.EqualTo(RunStatusText.StopHitText));
        }

        [Test]
        public void TheLungeDrillAsksForAParryThenARiposte()
        {
            Assert.That(RunStatusText.Format(Start(DrillCatalog.LungeParryDrillId), isFencerOnStrip: true).Instruction,
                Is.EqualTo(RunStatusText.ParryThenRiposteText));
        }

        [Test]
        public void BeatResultsCountTheBeatsAndTheTouchesAfterThem()
        {
            var results = DrillResults.Aggregate(new[]
            {
                BeatRep(0, hasBeat: true, RepOutcome.Success),
                BeatRep(1, hasBeat: true, RepOutcome.NoTouchAfterBeat),
                BeatRep(2, hasBeat: false, RepOutcome.NoBeat),
            });

            Assert.That(results.BeatHits, Is.EqualTo(new BeatHitCount(3, 2, 1)));
            Assert.That(results.ZoneHits, Is.Empty);
            Assert.That(results.FootworkHits, Is.Empty, "A Beat Rep is not a footwork call.");
            Assert.That(ResultsText.FormatSummary(results, wasCancelled: false), Does.Contain("Beats: 2 of 3 Reps"));
            Assert.That(ResultsText.FormatHitMap(results).Select(line => $"{line.ZoneName}: {line.CountText}"),
                Is.EqualTo(new[] { "After the Beat: Touched 1 of 2" }));

            var combined = DrillResults.Combine(new[] { results, results });
            Assert.That(combined.BeatHits, Is.EqualTo(new BeatHitCount(6, 4, 2)));
        }

        [Test]
        public void TheBeatLogsAndSavesWithItsTiming()
        {
            using var folder = new VrFencing.Tests.Session.ScratchFolder();
            var repLog = folder.File(RepLogFile.FileName);
            RepLogFile.Append(repLog, BeatRep(0, hasBeat: true, RepOutcome.Success));
            var entry = RepLogFile.Read(repLog).Items.Single();
            Assert.That(entry.TouchAfterBeatSeconds, Is.EqualTo(0.6));
            Assert.That(entry.BeatOutOfLineMeters, Is.EqualTo(0.2f));
            Assert.That(File.ReadAllText(repLog), Does.Contain("\"touchAfterBeatSeconds\":0.6").And.Contain("\"beatOutOfLineMeters\":0.2"));
            Assert.That(entry.WithFlagged(true).BeatOutOfLineMeters, Is.EqualTo(0.2f));

            var history = folder.File(SessionHistoryFile.FileName);
            var results = DrillResults.Aggregate(new[] { entry });
            SessionHistoryFile.Append(history, new DrillRunRecord(SessionId, Noon, Noon.AddMinutes(1), wasCancelled: false, results));
            Assert.That(SessionHistoryFile.Read(history).Items.Single().Results.BeatHits, Is.EqualTo(new BeatHitCount(1, 1, 1)));
        }

        [Test]
        public void StopHitResultsCountHitsOnTheFencerButNoParries()
        {
            var results = new DrillResults(
                DrillCatalog.StopHitDrillId, repCount: 2, successCount: 1, averageReactionSeconds: 2.0, averageTipSpeedMetersPerSecond: 1.5,
                isShrunk: false, zoneHits: new ZoneHitCount[0],
                attackHits: new[] { new AttackHitCount(AttackTarget.Chest, 2, 1) },
                fencerHits: new[] { new FencerHitCount(FencerHitSource.Iobt, 1) });

            var summary = ResultsText.FormatSummary(results, wasCancelled: false);
            Assert.That(summary, Has.None.StartsWith("Parries"));
            Assert.That(summary, Does.Contain("Hits on you: 1 (body tracking 1)"));
        }

        [Test]
        public void TheLungeDrillsRiposteSwitchIsInTheArmedDummyGroup()
        {
            var settings = new TrainerSettings();

            Assert.That(SettingsPage.GetLabel(SettingId.RiposteInLungeParries), Is.EqualTo("Riposte: Dummy lunges"));
            Assert.That(SettingsPage.FormatValue(settings, SettingId.RiposteInLungeParries), Is.EqualTo("On (within 1 s)"));
            Assert.That(SettingsPage.Change(settings, SettingId.RiposteInLungeParries, 1).ArmedDummy.RiposteInLungeParries, Is.False);
            Assert.That(SettingGroups.GetGroup(SettingId.RiposteInLungeParries), Is.EqualTo(SettingGroup.ArmedDummy));
        }
    }
}
