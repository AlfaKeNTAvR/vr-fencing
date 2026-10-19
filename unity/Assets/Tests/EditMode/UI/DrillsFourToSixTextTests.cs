// The words Drills 4 to 6 (PER-39) put on the Dummy, the Running panel and the results: footwork calls,
// a compound's two zones, and a hit map per footwork call without a tip speed.
using System;
using NUnit.Framework;
using VrFencing.Drills;
using VrFencing.Dummy;
using VrFencing.Session;
using VrFencing.Settings;
using VrFencing.Tests.Drills;
using VrFencing.UI;

namespace VrFencing.Tests.UI
{
    public class DrillsFourToSixTextTests
    {
        [Test]
        public void AFootworkCallShowsTheActionInCapitalsAboveTheDummy()
        {
            var cue = new RepCue(0, targetZone: null, CueType.FootworkCall, footworkCall: FootworkAction.Retreat);

            Assert.That(RunStatusText.FormatDummyLabel(cue), Is.EqualTo("RETREAT"));
        }

        [Test]
        public void ACompoundNamesBothZonesInOrderAboveTheDummy()
        {
            var cue = new RepCue(0, TargetZone.Hand, CueType.LitTargetZone, secondTargetZone: TargetZone.UpperArm);

            Assert.That(RunStatusText.FormatDummyLabel(cue), Is.EqualTo("Hand, then upper arm"));
        }

        [Test]
        public void DistanceChoiceLeavesTheLabelToTheLitBody()
        {
            var cue = new RepCue(0, TargetZone.Body, CueType.LitTargetZone, DummyDistance.StepLunge);

            Assert.That(RunStatusText.FormatDummyLabel(cue), Is.Empty);
        }

        [Test]
        public void TheRunningPanelSaysTheCallAndTheCompoundsSteps()
        {
            var footworkRunner = new DrillRunner(new FakeClock(), new Random(1));
            footworkRunner.Start(DrillCatalog.CreateFootworkCallsDrill(new TrainerSettings()), 4.0f, false);
            var call = footworkRunner.CurrentCue.FootworkCall.Value;
            Assert.That(RunStatusText.Format(footworkRunner, true).Instruction, Is.EqualTo(FootworkNames.Get(call)));

            var compoundRunner = new DrillRunner(new FakeClock(), new Random(1));
            compoundRunner.Start(DrillCatalog.CreateCompoundDrill(new TrainerSettings()), 4.0f, false);
            var cue = compoundRunner.CurrentCue;
            Assert.That(RunStatusText.Format(compoundRunner, true).Instruction, Does.StartWith("Touch the " + TargetZoneNames.Get(cue.TargetZone.Value).ToLowerInvariant() + ", then"));

            compoundRunner.ReportContact(TestDrills.PointOn(cue.TargetZone.Value));
            Assert.That(RunStatusText.Format(compoundRunner, true).Instruction,
                Is.EqualTo($"Now the {TargetZoneNames.Get(cue.SecondTargetZone.Value).ToLowerInvariant()}!"));
        }

        [Test]
        public void FootworkResultsListEachCallAndNoTipSpeed()
        {
            var results = new DrillResults(
                DrillCatalog.FootworkCallsDrillId, repCount: 3, successCount: 2, averageReactionSeconds: 0.4, averageTipSpeedMetersPerSecond: null,
                isShrunk: false, new ZoneHitCount[0],
                new[] { new FootworkHitCount(FootworkAction.Advance, 2, 1), new FootworkHitCount(FootworkAction.StepLunge, 1, 1) });

            var summary = ResultsText.FormatSummary(results, wasCancelled: false);
            var hitMap = ResultsText.FormatHitMap(results);

            Assert.That(summary, Is.EqualTo(new[] { "Success: 2 of 3 Reps (67%)", "Average reaction time: 0.40 s" }));
            Assert.That(hitMap.Count, Is.EqualTo(2));
            Assert.That(hitMap[0].ZoneName, Is.EqualTo("Advance"));
            Assert.That(hitMap[0].CountText, Is.EqualTo("Done 1 of 2"));
            Assert.That(hitMap[1].ZoneName, Is.EqualTo("Step-lunge"));
        }
    }
}
