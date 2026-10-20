// Parry Drills (PER-97): the runner judges a Rep from the Blade's Parry reports, then a Riposte within 1 s when
// the Drill requires one (PER-23: a per-Drill setting). Fake Parry reports, contacts and clock.
using System;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Drills;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Tests.Drills
{
    public class DrillRunnerParryTests
    {
        private const float Strip = 2.0f;
        private const float CueSeconds = 0.0f;
        private const float WindowSeconds = 8.0f;
        private const double Tolerance = 1e-9;

        private FakeClock _clock;
        private DrillRunner _runner;
        private RunnerRecorder _recorder;

        [SetUp]
        public void CreateRunner()
        {
            _clock = new FakeClock();
            _runner = new DrillRunner(_clock, new Random(7));
            _recorder = new RunnerRecorder(_runner);
        }

        private static DrillDefinition ParryDrill(bool requiresRiposte)
        {
            return new DrillDefinition(
                id: "parry",
                name: "Parry",
                order: 7,
                requiredStripLengthMeters: 1.0f,
                shrunkStripLengthMeters: null,
                repCount: 3,
                cueSeconds: CueSeconds,
                windowSeconds: WindowSeconds,
                restSeconds: 2.0f,
                cueType: CueType.NamedTargetZone,
                successRule: SuccessRule.Parry,
                targetZones: new[] { TargetZone.Body },
                dummyDistance: DummyDistance.Extension,
                requiresRiposte: requiresRiposte);
        }

        private static ParryReport Parry(ParryLine line = ParryLine.Six, bool countsAsParry = true)
        {
            return new ParryReport(line, isForteOnFoible: countsAsParry, countsAsParry, deflectionMeters: 0.2f, fencerBladeFraction: 0.3f, dummyBladeFraction: 0.7f);
        }

        private void Start(bool requiresRiposte = true)
        {
            Assert.That(_runner.Start(ParryDrill(requiresRiposte), Strip, shrinkDrillsToFit: false).IsAvailable, Is.True);
            Assert.That(_runner.State, Is.EqualTo(DrillState.Window));
        }

        private void Wait(double seconds)
        {
            _clock.Advance(seconds);
            _runner.Tick();
        }

        [Test]
        public void AParryThenATouchWithinOneSecondIsASuccessWithTheLineAndRiposteTime()
        {
            Start();
            Wait(2.0);
            _runner.ReportParry(Parry(ParryLine.Four));

            Assert.That(_recorder.Judged, Is.Empty, "The Riposte is still to come.");
            Assert.That(_runner.IsAwaitingRiposte, Is.True);

            Wait(0.6);
            _runner.ReportContact(TestDrills.PointOn(TargetZone.Body));

            var result = _recorder.Judged[0];
            Assert.That(result.Outcome, Is.EqualTo(RepOutcome.Success));
            Assert.That(result.ParryLine, Is.EqualTo(ParryLine.Four));
            Assert.That(result.ReactionSeconds, Is.EqualTo(2.0).Within(Tolerance), "From the window opening to the Parry.");
            Assert.That(result.RiposteSeconds, Is.EqualTo(0.6).Within(Tolerance));
        }

        [Test]
        public void NoTouchWithinOneSecondOfTheParryIsNoRiposte()
        {
            Start();
            Wait(2.0);
            _runner.ReportParry(Parry());
            Wait(DrillDefinition.RiposteWindowSeconds - 0.01);
            Assert.That(_recorder.Judged, Is.Empty);

            Wait(0.02);

            Assert.That(_recorder.Judged[0].Outcome, Is.EqualTo(RepOutcome.NoRiposte));
            Assert.That(_recorder.Judged[0].ParryLine, Is.EqualTo(ParryLine.Six));
            Assert.That(_recorder.Judged[0].RiposteSeconds, Is.Null);
        }

        [Test]
        public void TheRiposteWindowRunsFromTheParryEvenPastTheRepWindow()
        {
            Start();
            Wait(WindowSeconds - 0.2);
            _runner.ReportParry(Parry());
            Wait(0.9);
            _runner.ReportContact(TestDrills.PointOn(TargetZone.Body));

            Assert.That(_recorder.Judged[0].Outcome, Is.EqualTo(RepOutcome.Success));
        }

        [Test]
        public void ATouchOnAnotherZoneOrAFlatContactAfterTheParryFails()
        {
            Start();
            _runner.ReportParry(Parry());
            _runner.ReportContact(TestDrills.FlatOn(TargetZone.Body));
            Assert.That(_recorder.Judged[0].Outcome, Is.EqualTo(RepOutcome.FlatContact));

            Wait(2.0);
            _runner.ReportParry(Parry());
            _runner.ReportContact(TestDrills.PointOn(TargetZone.Thigh));
            Assert.That(_recorder.Judged[1].Outcome, Is.EqualTo(RepOutcome.WrongZone));
        }

        [Test]
        public void ContactsWithTheDummyBeforeTheParryAreIgnored()
        {
            Start();
            _runner.ReportContact(TestDrills.PointOn(TargetZone.Body));
            _runner.ReportContact(TestDrills.FlatOn(TargetZone.Forearm));

            Assert.That(_recorder.Judged, Is.Empty);
            Assert.That(_runner.State, Is.EqualTo(DrillState.Window));
        }

        [Test]
        public void ADeflectionThatFailsTheParryRuleFailsTheRep()
        {
            Start();
            _runner.ReportParry(Parry(ParryLine.Eight, countsAsParry: false));

            Assert.That(_recorder.Judged[0].Outcome, Is.EqualTo(RepOutcome.NotForteOnFoible));
            Assert.That(_recorder.Judged[0].ParryLine, Is.EqualTo(ParryLine.Eight));
        }

        [Test]
        public void AnAttackThatLandsUnparriedFailsTheRep()
        {
            Start();
            Wait(3.0);
            _runner.ReportAttackLanded();

            Assert.That(_recorder.Judged[0].Outcome, Is.EqualTo(RepOutcome.NotParried));
            Assert.That(_recorder.Judged[0].ParryLine, Is.Null);
        }

        [Test]
        public void AHitOnTheFencerFailsTheRepWithWhereItLandedAndTheBodyThatJudgedIt()
        {
            Start();
            Wait(2.0);
            _runner.ReportAttackLanded(new FencerHit(FencerHitSource.HeadsetCapsules, BodyPart.FrontThigh, new Vector3(0.0f, 0.7f, 0.1f)));

            Assert.That(_recorder.Judged[0].Outcome, Is.EqualTo(RepOutcome.NotParried));
            Assert.That(_recorder.Judged[0].FencerHit?.Source, Is.EqualTo(FencerHitSource.HeadsetCapsules));
            Assert.That(_recorder.Judged[0].FencerHit?.Part, Is.EqualTo(BodyPart.FrontThigh));
        }

        [Test]
        public void AThrustThatEndsShortFailsTheRepWithNoHit()
        {
            Start();
            Wait(2.0);
            _runner.ReportAttackLanded();

            Assert.That(_recorder.Judged[0].Outcome, Is.EqualTo(RepOutcome.NotParried));
            Assert.That(_recorder.Judged[0].FencerHit, Is.Null);
        }

        [Test]
        public void ALandingAfterTheParryOrASecondParryChangesNothing()
        {
            Start();
            _runner.ReportParry(Parry(ParryLine.Six));
            _runner.ReportAttackLanded();
            _runner.ReportParry(Parry(ParryLine.Four));
            _runner.ReportContact(TestDrills.PointOn(TargetZone.Body));

            Assert.That(_recorder.Judged, Has.Count.EqualTo(1));
            Assert.That(_recorder.Judged[0].Outcome, Is.EqualTo(RepOutcome.Success));
            Assert.That(_recorder.Judged[0].ParryLine, Is.EqualTo(ParryLine.Six));
        }

        [Test]
        public void WithoutARiposteTheParryAloneIsTheSuccess()
        {
            Start(requiresRiposte: false);
            Wait(1.5);
            _runner.ReportParry(Parry(ParryLine.Seven));

            Assert.That(_recorder.Judged[0].Outcome, Is.EqualTo(RepOutcome.Success));
            Assert.That(_recorder.Judged[0].ParryLine, Is.EqualTo(ParryLine.Seven));
            Assert.That(_recorder.Judged[0].ReactionSeconds, Is.EqualTo(1.5).Within(Tolerance));
            Assert.That(_runner.State, Is.EqualTo(DrillState.Rest));
        }

        [Test]
        public void NoParryAndNoLandingIsATimeout()
        {
            Start();
            Wait(WindowSeconds + 0.01);

            Assert.That(_recorder.Judged[0].Outcome, Is.EqualTo(RepOutcome.Timeout));
        }

        [Test]
        public void ParryReportsAreIgnoredInOtherDrillsAndDuringTheRest()
        {
            Assert.That(_runner.Start(TestDrills.Create(), TestDrills.RequiredLength, shrinkDrillsToFit: false).IsAvailable, Is.True);
            Wait(TestDrills.CueSeconds);
            _runner.ReportParry(Parry());
            _runner.ReportAttackLanded();
            Assert.That(_recorder.Judged, Is.Empty);
            _runner.Cancel();

            Start(requiresRiposte: false);
            _runner.ReportParry(Parry());
            Assert.That(_runner.State, Is.EqualTo(DrillState.Rest));
            _runner.ReportAttackLanded();
            _runner.ReportParry(Parry());
            Assert.That(_recorder.Judged, Has.Count.EqualTo(1));
        }

        [Test]
        public void ANewRepForgetsTheLastParry()
        {
            Start();
            _runner.ReportParry(Parry());
            _runner.ReportContact(TestDrills.PointOn(TargetZone.Body));
            Wait(2.0);
            Assert.That(_runner.State, Is.EqualTo(DrillState.Window));

            _runner.ReportContact(TestDrills.PointOn(TargetZone.Body));
            Assert.That(_recorder.Judged, Has.Count.EqualTo(1), "The new Rep has no Parry yet: the Touch is ignored.");
            _runner.ReportAttackLanded();
            Assert.That(_recorder.Judged[1].ParryLine, Is.Null);
        }

        [Test]
        public void OnlyAParryDrillCanRequireARiposte()
        {
            Assert.Throws<ArgumentException>(() => new DrillDefinition(
                "touch", "Touch", 1, 1.0f, null, 1, 0.0f, 1.0f, 1.0f, CueType.NamedTargetZone, SuccessRule.PointFirstTouchOnTargetZone,
                new[] { TargetZone.Body }, requiresRiposte: true));
            Assert.That(ParryDrill(requiresRiposte: true).UsesArmedDummy, Is.True);
            Assert.That(TestDrills.Create().UsesArmedDummy, Is.False);
            Assert.That(TestDrills.Create().RequiresRiposte, Is.False);
        }

        [Test]
        public void TheArmedDummyTestDrillJudgesAParryThenARiposte()
        {
            var drill = DrillCatalog.CreateArmedDummyTestDrill(new TrainerSettings());

            Assert.That(drill.SuccessRule, Is.EqualTo(SuccessRule.Parry));
            Assert.That(drill.RequiresRiposte, Is.True);
            Assert.That(drill.UsesArmedDummy, Is.True);
        }
    }
}
