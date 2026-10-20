// The words the Parry Drills 7 to 9 (PER-100) put on the Running panel and the results, and their Riposte switches on
// the settings screen.
using System.IO;
using System.Linq;
using NUnit.Framework;
using VrFencing.Drills;
using VrFencing.Dummy;
using VrFencing.Session;
using VrFencing.Settings;
using VrFencing.Tests.Drills;
using VrFencing.UI;

namespace VrFencing.Tests.UI
{
    public class ParryDrillsTextTests
    {
        private static DrillRunner StartParryDrill(TrainerSettings settings)
        {
            var runner = new DrillRunner(new FakeClock(), new System.Random(2));
            var drill = DrillCatalog.CreateDefault(includeTestDrills: false).BuildAll(settings).Single(built => built.Id == DrillCatalog.SlowParryDrillId);
            runner.Start(drill, 2.0f, shrinkDrillsToFit: false);
            return runner;
        }

        private static ParryReport Parry()
        {
            return new ParryReport(ParryLine.Six, isForteOnFoible: true, countsAsParry: true, deflectionMeters: 0.2f, fencerBladeFraction: 0.3f, dummyBladeFraction: 0.7f);
        }

        [Test]
        public void TheAttackCueShowsNoLabelAboveTheDummy()
        {
            var cue = new RepCue(0, targetZone: null, CueType.ArmedDummyAttack, DummyDistance.Extension, attackTarget: AttackTarget.Chest, attackSpeed: AttackSpeed.Slow);

            Assert.That(RunStatusText.FormatDummyLabel(cue), Is.Empty);
        }

        [Test]
        public void TheRunningPanelSaysParryThenRiposteThenRiposteNow()
        {
            var runner = StartParryDrill(new TrainerSettings());

            Assert.That(RunStatusText.Format(runner, isFencerOnStrip: true).Instruction, Is.EqualTo(RunStatusText.ParryThenRiposteText));
            runner.ReportParry(Parry());
            Assert.That(RunStatusText.Format(runner, isFencerOnStrip: true).Instruction, Is.EqualTo(RunStatusText.RiposteText));
        }

        [Test]
        public void WithTheRiposteOffTheRunningPanelSaysParryOnly()
        {
            var settings = new TrainerSettings();
            settings.ArmedDummy.RiposteInSlowParries = false;

            Assert.That(RunStatusText.Format(StartParryDrill(settings), isFencerOnStrip: true).Instruction, Is.EqualTo(RunStatusText.ParryText));
        }

        [Test]
        public void TheResultsNameTheLinesParriedAndTheHitsOnTheFencerBySource()
        {
            var results = new DrillResults(
                DrillCatalog.SlowParryDrillId, repCount: 5, successCount: 2, averageReactionSeconds: 2.4, averageTipSpeedMetersPerSecond: 1.3,
                isShrunk: false, zoneHits: new ZoneHitCount[0],
                attackHits: new[] { new AttackHitCount(AttackTarget.Hand, 3, 1), new AttackHitCount(AttackTarget.Chest, 2, 1) },
                parryLines: new[] { new ParryLineCount(ParryLine.Four, 1), new ParryLineCount(ParryLine.Six, 2) },
                fencerHits: new[] { new FencerHitCount(FencerHitSource.Iobt, 1), new FencerHitCount(FencerHitSource.HeadsetCapsules, 1) });

            var summary = ResultsText.FormatSummary(results, wasCancelled: false);
            Assert.That(summary, Does.Contain("Parries: 4 x1, 6 x2"));
            Assert.That(summary, Does.Contain("Hits on you: 2 (body tracking 1, headset 1)"));

            var hitMap = ResultsText.FormatHitMap(results);
            Assert.That(hitMap.Select(line => $"{line.ZoneName}: {line.CountText}"),
                Is.EqualTo(new[] { "At your hand: Done 1 of 3", "At your chest: Done 1 of 2" }));
        }

        [Test]
        public void ResultsWithNoParryAndNoHitSayNone()
        {
            var results = new DrillResults(
                DrillCatalog.LowParryDrillId, repCount: 1, successCount: 0, averageReactionSeconds: null, averageTipSpeedMetersPerSecond: null,
                isShrunk: false, zoneHits: new ZoneHitCount[0], attackHits: new[] { new AttackHitCount(AttackTarget.Thigh, 1, 0) });

            var summary = ResultsText.FormatSummary(results, wasCancelled: false);
            Assert.That(summary, Does.Contain("Parries: none"));
            Assert.That(summary, Does.Contain("Hits on you: none"));
        }

        [Test]
        public void OtherDrillsResultsSayNothingAboutParries()
        {
            var results = new DrillResults(
                DrillCatalog.PointControlDrillId, repCount: 1, successCount: 1, averageReactionSeconds: 0.5, averageTipSpeedMetersPerSecond: 1.0,
                isShrunk: false, zoneHits: new[] { new ZoneHitCount(TargetZone.Hand, 1, 1) });

            Assert.That(ResultsText.FormatSummary(results, wasCancelled: false), Has.None.StartsWith("Parries").And.None.StartsWith("Hits on you"));
        }

        [Test]
        public void EachRiposteSwitchFlipsOnTheSettingsScreen()
        {
            var settings = new TrainerSettings();
            var ids = new[] { SettingId.RiposteInSlowParries, SettingId.RiposteInFastParries, SettingId.RiposteInLowParries };

            foreach (var id in ids)
            {
                Assert.That(SettingsPage.FormatValue(settings, id), Is.EqualTo("On (within 1 s)"));
                var off = SettingsPage.Change(settings, id, 1);
                Assert.That(SettingsPage.FormatValue(off, id), Is.EqualTo("Off (parry only)"));
                Assert.That(SettingsPage.FormatValue(SettingsPage.Change(off, id, -1), id), Is.EqualTo("On (within 1 s)"));
                Assert.That(SettingsPage.GetImpact(id), Is.EqualTo(SettingImpact.Drills), "The Drill list is rebuilt with the new Riposte rule.");
                Assert.That(SettingGroups.GetGroup(id), Is.EqualTo(SettingGroup.ArmedDummy));
            }

            Assert.That(SettingsPage.GetLabel(SettingId.RiposteInSlowParries), Is.EqualTo("Riposte: Parry 6/4 slow"));
            Assert.That(SettingsPage.GetLabel(SettingId.RiposteInLowParries), Is.EqualTo("Riposte: Parry 8/7"));
            Assert.That(SettingGroups.GetGroup(SettingId.ParryRule), Is.EqualTo(SettingGroup.ArmedDummy));
        }

        [Test]
        public void ASettingsFileFromBeforeTheParryDrillsAsksForEveryRiposte()
        {
            var path = Path.Combine(Path.GetTempPath(), $"vrfencing-settings-{System.Guid.NewGuid():N}.json");
            try
            {
                File.WriteAllText(path, "{ \"Hand\": \"Left\" }");
                var loaded = SettingsStorage.Load(path);

                Assert.That(loaded.ArmedDummy.RiposteInSlowParries, Is.True);
                Assert.That(loaded.ArmedDummy.RiposteInFastParries, Is.True);
                Assert.That(loaded.ArmedDummy.RiposteInLowParries, Is.True);

                loaded.ArmedDummy.RiposteInFastParries = false;
                SettingsStorage.Save(loaded, path);
                Assert.That(SettingsStorage.Load(path).ArmedDummy.RiposteInFastParries, Is.False);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
