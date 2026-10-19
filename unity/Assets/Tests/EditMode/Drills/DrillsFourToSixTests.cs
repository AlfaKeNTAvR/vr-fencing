// Drills 4 to 6 (PER-39): distance choice, compound sequence and footwork calls, as registered in the Drill
// catalog and run by the Drill runner with fake blade contacts, fake footwork readings and a fake clock.
using System;
using System.Linq;
using NUnit.Framework;
using VrFencing.Drills;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Tests.Drills
{
    public class DrillsFourToSixTests
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
            _runner = new DrillRunner(_clock, new Random(42));
            _recorder = new RunnerRecorder(_runner);
        }

        private static DrillDefinition Build(string id, TrainerSettings settings = null)
        {
            return DrillCatalog.CreateDefault(includeTestDrills: false).BuildAll(settings ?? new TrainerSettings()).Single(drill => drill.Id == id);
        }

        private static FootworkReading Footwork(FootworkAction action, double? onsetSeconds = 0.3)
        {
            return new FootworkReading(action, onsetSeconds, forwardTravelMeters: 0.5f, backwardTravelMeters: 0.0f, peakForwardSpeedMetersPerSecond: 2.0f, headDropMeters: 0.1f);
        }

        private void Wait(double seconds)
        {
            _clock.Advance(seconds);
            _runner.Tick();
        }

        private void Start(string id, TrainerSettings settings = null)
        {
            Assert.That(_runner.Start(Build(id, settings), LongStrip, shrinkDrillsToFit: false).IsAvailable, Is.True);
        }

        // ---- The catalog entries ----

        [Test]
        public void DistanceChoiceLightsTheBodyAtARandomDistanceAndNeedsThreeAndAHalfMeters()
        {
            var settings = new TrainerSettings { RepsPerDrill = 6, RestBetweenRepsSeconds = 3.0f };
            settings.TimeWindows.DistanceChoiceSeconds = 2.4f;

            var drill = Build(DrillCatalog.DistanceChoiceDrillId, settings);

            Assert.That(drill.Order, Is.EqualTo(4));
            Assert.That(drill.RepCount, Is.EqualTo(6));
            Assert.That(drill.RestSeconds, Is.EqualTo(3.0f));
            Assert.That(drill.WindowSeconds, Is.EqualTo(2.4f));
            Assert.That(drill.CueSeconds, Is.Zero);
            Assert.That(drill.CueType, Is.EqualTo(CueType.LitTargetZone));
            Assert.That(drill.SuccessRule, Is.EqualTo(SuccessRule.TouchWithFootworkForDistance));
            Assert.That(drill.TargetZones, Is.EqualTo(new[] { TargetZone.Body }));
            Assert.That(drill.RepDummyDistances, Is.EquivalentTo(new[] { DummyDistance.Extension, DummyDistance.Lunge, DummyDistance.StepLunge }));
            Assert.That(drill.UsesFootwork, Is.True);
            Assert.That(drill.RequiredStripLengthMeters, Is.EqualTo(3.5f));
            Assert.That(drill.ShrunkStripLengthMeters, Is.Null);
        }

        [Test]
        public void CompoundLightsTwoZonesInOrderWithTheSecondTouchWindowFromTheSettings()
        {
            var settings = new TrainerSettings();
            settings.TimeWindows.CompoundSecondTouchSeconds = 0.8f;

            var drill = Build(DrillCatalog.CompoundDrillId, settings);

            Assert.That(drill.Order, Is.EqualTo(5));
            Assert.That(drill.CueType, Is.EqualTo(CueType.LitTargetZone));
            Assert.That(drill.SuccessRule, Is.EqualTo(SuccessRule.TwoTouchesInOrder));
            Assert.That(drill.SecondTouchWindowSeconds, Is.EqualTo(0.8f));
            Assert.That(drill.WindowSeconds, Is.EqualTo(DrillCatalog.CompoundFirstTouchWindowSeconds));
            Assert.That(drill.ZonePairs, Is.Not.Empty);
            Assert.That(drill.ZonePairs.Any(pair => pair.First == TargetZone.Hand && pair.Second == TargetZone.Thigh), Is.True, "PER-21's example.");
            Assert.That(drill.UsesFootwork, Is.False);
            Assert.That(drill.RequiredStripLengthMeters, Is.EqualTo(3.0f));
            Assert.That(drill.GetDummyDistance(isShrunk: false), Is.EqualTo(DummyDistance.Lunge));
            Assert.That(drill.GetDummyDistance(isShrunk: true), Is.EqualTo(DummyDistance.Extension));
            Assert.That(DrillAvailability.Check(drill, 1.5f, shrinkDrillsToFit: true).IsShrunk, Is.True);
        }

        [Test]
        public void FootworkCallsCallTheFourActionsWithNoZoneAndNeedThreeMeters()
        {
            var settings = new TrainerSettings();
            settings.TimeWindows.FootworkCallSeconds = 1.7f;

            var drill = Build(DrillCatalog.FootworkCallsDrillId, settings);

            Assert.That(drill.Order, Is.EqualTo(6));
            Assert.That(drill.CueType, Is.EqualTo(CueType.FootworkCall));
            Assert.That(drill.SuccessRule, Is.EqualTo(SuccessRule.FootworkCall));
            Assert.That(drill.WindowSeconds, Is.EqualTo(1.7f));
            Assert.That(drill.TargetZones, Is.Empty);
            Assert.That(drill.FootworkCalls, Is.EquivalentTo(new[] { FootworkAction.Advance, FootworkAction.Retreat, FootworkAction.Lunge, FootworkAction.Check }));
            Assert.That(drill.UsesFootwork, Is.True);
            Assert.That(drill.RequiredStripLengthMeters, Is.EqualTo(3.0f));
            Assert.That(DrillAvailability.Check(drill, 2.5f, shrinkDrillsToFit: true).IsAvailable, Is.False);
        }

        // ---- Drill 4: distance choice ----

        [Test]
        public void DistanceChoiceCuesEveryDistanceAcrossItsReps()
        {
            var settings = new TrainerSettings { RepsPerDrill = 30, RestBetweenRepsSeconds = 0.0f };
            Start(DrillCatalog.DistanceChoiceDrillId, settings);

            for (var rep = 0; rep < 30; rep++)
            {
                _runner.ReportContact(TestDrills.PointOn(TargetZone.Body));
            }

            Assert.That(_recorder.Cues.Select(cue => cue.DummyDistance).Distinct(), Is.EquivalentTo(DrillCatalog.DistanceChoiceDistances));
            Assert.That(_recorder.Judged.Select(result => result.DummyDistance), Is.EqualTo(_recorder.Cues.Select(cue => (DummyDistance?)cue.DummyDistance)));
        }

        [Test]
        public void DistanceChoiceSucceedsWithFootworkThatSuitsTheDistance()
        {
            Start(DrillCatalog.DistanceChoiceDrillId);
            var distance = _runner.CurrentCue.DummyDistance;
            var suitable = distance == DummyDistance.Extension ? FootworkAction.None : distance == DummyDistance.Lunge ? FootworkAction.Lunge : FootworkAction.StepLunge;

            Wait(0.6);
            _runner.ReportFootwork(Footwork(suitable));
            _runner.ReportContact(TestDrills.PointOn(TargetZone.Body));

            var result = _recorder.Judged.Single();
            Assert.That(result.Outcome, Is.EqualTo(RepOutcome.Success));
            Assert.That(result.Footwork.Value.Action, Is.EqualTo(suitable));
            Assert.That(result.ReactionSeconds, Is.EqualTo(0.6).Within(Tolerance));
        }

        [Test]
        public void DistanceChoiceFailsAsWrongFootworkWhenTheFootworkDoesNotSuitTheDistance()
        {
            Start(DrillCatalog.DistanceChoiceDrillId);
            var distance = _runner.CurrentCue.DummyDistance;
            var unsuitable = distance == DummyDistance.Extension ? FootworkAction.Lunge : FootworkAction.Advance;

            _runner.ReportFootwork(Footwork(unsuitable));
            _runner.ReportContact(TestDrills.PointOn(TargetZone.Body));

            Assert.That(_recorder.Judged.Single().Outcome, Is.EqualTo(RepOutcome.WrongFootwork));
        }

        [Test]
        public void DistanceChoiceJudgesTheTouchAloneWhenNoFootworkWasRead()
        {
            Start(DrillCatalog.DistanceChoiceDrillId);

            _runner.ReportContact(TestDrills.PointOn(TargetZone.Body));

            Assert.That(_recorder.Judged.Single().Outcome, Is.EqualTo(RepOutcome.Success));
            Assert.That(_recorder.Judged.Single().Footwork, Is.Null);
        }

        [Test]
        public void DistanceChoiceStillFailsAWrongZoneWhateverTheFootwork()
        {
            Start(DrillCatalog.DistanceChoiceDrillId);

            _runner.ReportFootwork(Footwork(FootworkAction.Advance));
            _runner.ReportContact(TestDrills.PointOn(TargetZone.Hand));

            Assert.That(_recorder.Judged.Single().Outcome, Is.EqualTo(RepOutcome.WrongZone));
        }

        [Test]
        public void FootworkFromTheLastRepDoesNotCarryIntoTheNext()
        {
            var settings = new TrainerSettings { RestBetweenRepsSeconds = 1.0f };
            Start(DrillCatalog.DistanceChoiceDrillId, settings);
            _runner.ReportFootwork(Footwork(FootworkAction.Advance));
            _runner.ReportContact(TestDrills.PointOn(TargetZone.Body));

            _runner.ReportFootwork(Footwork(FootworkAction.Advance));
            Wait(1.0);
            _runner.ReportContact(TestDrills.PointOn(TargetZone.Body));

            Assert.That(_recorder.Judged[1].Footwork, Is.Null, "Readings in the rest and the last Rep's are dropped.");
            Assert.That(_recorder.Judged[1].Outcome, Is.EqualTo(RepOutcome.Success));
        }

        [Test]
        public void FootworkIsIgnoredByDrillsThatDoNotJudgeIt()
        {
            _runner.Start(Build(DrillCatalog.LungeToTargetDrillId), LongStrip, false);

            _runner.ReportFootwork(Footwork(FootworkAction.Advance));
            _runner.ReportContact(TestDrills.PointOn(_runner.CurrentTargetZone.Value));

            Assert.That(_recorder.Judged.Single().Footwork, Is.Null);
            Assert.That(_recorder.Judged.Single().Outcome, Is.EqualTo(RepOutcome.Success));
        }

        // ---- Drill 5: compound sequence ----

        [Test]
        public void CompoundCuesBothZonesThenTheSecondAfterTheFirstTouch()
        {
            var secondCues = new System.Collections.Generic.List<TargetZone>();
            _runner.SecondZoneCued += zone => secondCues.Add(zone);
            Start(DrillCatalog.CompoundDrillId);
            var cue = _recorder.Cues.Single();

            Assert.That(cue.TargetZone, Is.Not.Null);
            Assert.That(cue.SecondTargetZone, Is.Not.Null);
            Assert.That(_runner.CurrentTargetZone, Is.EqualTo(cue.TargetZone));

            Wait(0.7);
            _runner.ReportContact(TestDrills.PointOn(cue.TargetZone.Value));

            Assert.That(_recorder.Judged, Is.Empty, "The first Touch does not end the Rep.");
            Assert.That(secondCues, Is.EqualTo(new[] { cue.SecondTargetZone.Value }));
            Assert.That(_runner.IsAwaitingSecondTouch, Is.True);
            Assert.That(_runner.CurrentTargetZone, Is.EqualTo(cue.SecondTargetZone));
        }

        [Test]
        public void CompoundSucceedsWhenTheSecondTouchLandsInItsWindow()
        {
            Start(DrillCatalog.CompoundDrillId);
            var cue = _runner.CurrentCue;

            Wait(0.7);
            _runner.ReportContact(TestDrills.PointOn(cue.TargetZone.Value));
            Wait(0.9);
            _runner.ReportContact(TestDrills.PointOn(cue.SecondTargetZone.Value));

            var result = _recorder.Judged.Single();
            Assert.That(result.Outcome, Is.EqualTo(RepOutcome.Success));
            Assert.That(result.RequestedZone, Is.EqualTo(cue.TargetZone));
            Assert.That(result.RequestedSecondZone, Is.EqualTo(cue.SecondTargetZone));
            Assert.That(result.ContactZone, Is.EqualTo(cue.SecondTargetZone));
            Assert.That(result.ReactionSeconds, Is.EqualTo(0.7).Within(Tolerance), "Reaction time runs from the cue to the first Touch.");
            Assert.That(result.SecondTouchSeconds, Is.EqualTo(0.9).Within(Tolerance));
        }

        [Test]
        public void CompoundTimesOutWhenTheSecondTouchIsTooLate()
        {
            Start(DrillCatalog.CompoundDrillId);
            var cue = _runner.CurrentCue;

            Wait(2.5);
            _runner.ReportContact(TestDrills.PointOn(cue.TargetZone.Value));
            Wait(0.99);
            Assert.That(_recorder.Judged, Is.Empty, "The second window runs from the first Touch, past the first window's end.");

            Wait(0.02);
            var result = _recorder.Judged.Single();
            Assert.That(result.Outcome, Is.EqualTo(RepOutcome.Timeout));
            Assert.That(result.ReactionSeconds, Is.EqualTo(2.5).Within(Tolerance), "The first Touch's time is kept.");
            Assert.That(result.SecondTouchSeconds, Is.Null);
            Assert.That(result.ContactZone, Is.Null);
        }

        [Test]
        public void CompoundFailsWhenTheSecondTouchHitsAnotherZone()
        {
            Start(DrillCatalog.CompoundDrillId);
            var cue = _runner.CurrentCue;
            var otherZone = DrillCatalog.AllTargetZones.First(zone => zone != cue.TargetZone && zone != cue.SecondTargetZone);

            _runner.ReportContact(TestDrills.PointOn(cue.TargetZone.Value));
            Wait(0.3);
            _runner.ReportContact(TestDrills.PointOn(otherZone));

            var result = _recorder.Judged.Single();
            Assert.That(result.Outcome, Is.EqualTo(RepOutcome.WrongZone));
            Assert.That(result.ContactZone, Is.EqualTo(otherZone));
            Assert.That(result.SecondTouchSeconds, Is.EqualTo(0.3).Within(Tolerance));
        }

        [Test]
        public void CompoundFailsAtOnceWhenTheSecondZoneIsTouchedFirst()
        {
            Start(DrillCatalog.CompoundDrillId);
            var cue = _runner.CurrentCue;

            _runner.ReportContact(TestDrills.PointOn(cue.SecondTargetZone.Value));

            var result = _recorder.Judged.Single();
            Assert.That(result.Outcome, Is.EqualTo(RepOutcome.WrongZone));
            Assert.That(result.SecondTouchSeconds, Is.Null);
        }

        [Test]
        public void CompoundFailsOnAFlatContactAfterTheFirstTouch()
        {
            Start(DrillCatalog.CompoundDrillId);
            var cue = _runner.CurrentCue;

            _runner.ReportContact(TestDrills.PointOn(cue.TargetZone.Value));
            _runner.ReportContact(TestDrills.FlatOn(cue.SecondTargetZone.Value));

            Assert.That(_recorder.Judged.Single().Outcome, Is.EqualTo(RepOutcome.FlatContact));
        }

        [Test]
        public void CompoundTimesOutWithoutAFirstTouch()
        {
            Start(DrillCatalog.CompoundDrillId);

            Wait(DrillCatalog.CompoundFirstTouchWindowSeconds + 0.01);

            var result = _recorder.Judged.Single();
            Assert.That(result.Outcome, Is.EqualTo(RepOutcome.Timeout));
            Assert.That(result.ReactionSeconds, Is.Null);
        }

        [Test]
        public void APauseAfterTheFirstTouchRestartsTheCompoundFromItsFirstZone()
        {
            Start(DrillCatalog.CompoundDrillId);
            _runner.ReportContact(TestDrills.PointOn(_runner.CurrentCue.TargetZone.Value));

            _runner.PauseByFencer();
            Wait(5.0);
            _runner.ResumeByFencer();

            Assert.That(_runner.IsAwaitingSecondTouch, Is.False);
            Assert.That(_runner.CurrentTargetZone, Is.EqualTo(_runner.CurrentCue.TargetZone));
            Assert.That(_recorder.Cues, Has.Count.EqualTo(2));
            Assert.That(_recorder.Judged, Is.Empty);
        }

        // ---- Drill 6: footwork calls ----

        [Test]
        public void AFootworkCallCuesAnActionAndNoZone()
        {
            Start(DrillCatalog.FootworkCallsDrillId);
            var cue = _recorder.Cues.Single();

            Assert.That(cue.FootworkCall, Is.Not.Null);
            Assert.That(cue.TargetZone, Is.Null);
            Assert.That(cue.CueType, Is.EqualTo(CueType.FootworkCall));
            Assert.That(_runner.CurrentTargetZone, Is.Null);
        }

        [Test]
        public void AFootworkCallIsJudgedWhenItsWindowEndsAndSucceedsWhenAnswered()
        {
            Start(DrillCatalog.FootworkCallsDrillId);
            var call = _runner.CurrentCue.FootworkCall.Value;

            Wait(0.8);
            _runner.ReportFootwork(Footwork(call, onsetSeconds: 0.35));
            Wait(1.0);
            Assert.That(_recorder.Judged, Is.Empty, "Judged only when the 2.0 s window ends.");

            Wait(0.21);
            var result = _recorder.Judged.Single();
            Assert.That(result.Outcome, Is.EqualTo(RepOutcome.Success));
            Assert.That(result.RequestedFootwork, Is.EqualTo(call));
            Assert.That(result.RequestedZone, Is.Null);
            Assert.That(result.ReactionSeconds, Is.EqualTo(0.35).Within(Tolerance), "Reaction time is the footwork's onset.");
            Assert.That(result.TipSpeedMetersPerSecond, Is.Null);
        }

        [Test]
        public void AFootworkCallAnsweredWithAnotherActionFailsAsWrongFootwork()
        {
            Start(DrillCatalog.FootworkCallsDrillId);
            var call = _runner.CurrentCue.FootworkCall.Value;
            var other = call == FootworkAction.Check ? FootworkAction.Advance : FootworkAction.Check;

            _runner.ReportFootwork(Footwork(other));
            Wait(2.01);

            Assert.That(_recorder.Judged.Single().Outcome, Is.EqualTo(RepOutcome.WrongFootwork));
            Assert.That(_recorder.Judged.Single().Footwork.Value.Action, Is.EqualTo(other));
        }

        [Test]
        public void AFootworkCallWithNoMovementTimesOut()
        {
            Start(DrillCatalog.FootworkCallsDrillId);

            _runner.ReportFootwork(Footwork(FootworkAction.None, onsetSeconds: null));
            Wait(2.01);

            Assert.That(_recorder.Judged.Single().Outcome, Is.EqualTo(RepOutcome.Timeout));
            Assert.That(_recorder.Judged.Single().ReactionSeconds, Is.Null);
        }

        [Test]
        public void TheLatestFootworkReadingCounts()
        {
            Start(DrillCatalog.FootworkCallsDrillId);
            var call = _runner.CurrentCue.FootworkCall.Value;

            _runner.ReportFootwork(Footwork(FootworkAction.None, onsetSeconds: null));
            Wait(1.0);
            _runner.ReportFootwork(Footwork(call));
            Wait(1.01);

            Assert.That(_recorder.Judged.Single().Outcome, Is.EqualTo(RepOutcome.Success));
        }

        [Test]
        public void FootworkCallsIgnoreTheBlade()
        {
            Start(DrillCatalog.FootworkCallsDrillId);

            _runner.ReportContact(TestDrills.PointOn(TargetZone.Body));
            _runner.ReportContact(TestDrills.FlatOn(TargetZone.Hand));

            Assert.That(_recorder.Judged, Is.Empty);
            Assert.That(_runner.State, Is.EqualTo(DrillState.Window));
        }

        [Test]
        public void FootworkCallsStopCallingAdvancesTwoStepsAhead()
        {
            var settings = new TrainerSettings { RepsPerDrill = 40, RestBetweenRepsSeconds = 0.0f };
            Start(DrillCatalog.FootworkCallsDrillId, settings);

            // The Fencer always advances, whatever the call; after two judged advances no more are called.
            for (var rep = 0; rep < 40; rep++)
            {
                _runner.ReportFootwork(Footwork(FootworkAction.Advance));
                Wait(2.0);
            }

            Assert.That(_recorder.Cues.Skip(2).Select(cue => cue.FootworkCall), Has.None.EqualTo(FootworkAction.Advance));
        }

        // ---- Drill definitions for the new rules ----

        [Test]
        public void ACompoundDrillNeedsZonePairsAndASecondWindow()
        {
            Assert.Throws<ArgumentException>(() => CreateDrill(SuccessRule.TwoTouchesInOrder, CueType.LitTargetZone, secondWindow: 1.0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => CreateDrill(
                SuccessRule.TwoTouchesInOrder, CueType.LitTargetZone, pairs: new[] { new ZonePair(TargetZone.Hand, TargetZone.Body) }));
            Assert.Throws<ArgumentException>(() => new ZonePair(TargetZone.Hand, TargetZone.Hand));
        }

        [Test]
        public void AFootworkCallDrillNeedsCallsAndTheFootworkCue()
        {
            Assert.Throws<ArgumentException>(() => CreateDrill(SuccessRule.FootworkCall, CueType.FootworkCall));
            Assert.Throws<ArgumentException>(() => CreateDrill(SuccessRule.FootworkCall, CueType.NamedTargetZone, calls: new[] { FootworkAction.Advance }));
            Assert.Throws<ArgumentException>(() => CreateDrill(SuccessRule.PointFirstTouchOnTargetZone, CueType.FootworkCall, zones: new[] { TargetZone.Body }));
        }

        [Test]
        public void PerRepDistancesNeedADistanceAndNoShortenedVariant()
        {
            Assert.Throws<ArgumentException>(() => CreateDrill(
                SuccessRule.PointFirstTouchOnTargetZone, CueType.LitTargetZone, zones: new[] { TargetZone.Body }, distances: new DummyDistance[0]));
            Assert.Throws<ArgumentException>(() => CreateDrill(
                SuccessRule.PointFirstTouchOnTargetZone, CueType.LitTargetZone, zones: new[] { TargetZone.Body }, distances: new[] { DummyDistance.Lunge },
                shrunkLength: 1.0f));
        }

        private static DrillDefinition CreateDrill(
            SuccessRule rule,
            CueType cueType,
            TargetZone[] zones = null,
            ZonePair[] pairs = null,
            float secondWindow = 0.0f,
            FootworkAction[] calls = null,
            DummyDistance[] distances = null,
            float? shrunkLength = null)
        {
            return new DrillDefinition(
                "test", "Test", 1, 3.0f, shrunkLength, 3, 0.0f, 2.0f, 1.0f, cueType, rule, zones,
                repDummyDistances: distances, zonePairs: pairs, secondTouchWindowSeconds: secondWindow, footworkCalls: calls);
        }
    }
}
