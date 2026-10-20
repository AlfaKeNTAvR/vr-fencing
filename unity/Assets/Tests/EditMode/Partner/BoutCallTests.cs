// What each headset reads, says and counts in the bout flow (PER-83): the stance behind the En Garde Line and on the
// Strip, the calls and their timing, the halt reasons from each side, the Paired Practice tally, and their messages.
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Dummy;
using VrFencing.Partner;
using VrFencing.Session;

namespace VrFencing.Tests.Partner
{
    public class BoutCallTests
    {
        // 8 m Strips with their rear ends 2 m behind the En Garde Line: the bout's lines are 4 m apart and this Fencer's
        // is at z = 0.
        private static readonly StripExtent EightMetres = new StripExtent(8.0f, -2.0f);
        private static readonly BoutStrip Bout = BoutStrip.Create(EightMetres, EightMetres);

        [Test]
        public void AFencerWithTheFrontFootBehindTheLineIsBehindIt()
        {
            var body = BodyWithFrontToeAt(-0.3f);

            var stance = FencerStance.Read(body, null, EightMetres, Bout, 0.0f, hasPlacedStrip: true);

            Assert.That(Bout.MyEnGardeLineZ, Is.EqualTo(0.0f).Within(1e-5));
            Assert.That(stance.IsBehindLine, Is.True);
            Assert.That(stance.OffStrip, Is.EqualTo(OffStripKind.None));
            Assert.That(stance.IsReady, Is.True);
        }

        [Test]
        public void AFrontFootOverTheLineIsNotBehindIt()
        {
            var justOver = BodyWithFrontToeAt(FencerStance.EnGardeLineToleranceMeters + 0.05f);
            var withinTolerance = BodyWithFrontToeAt(FencerStance.EnGardeLineToleranceMeters - 0.05f);

            Assert.That(FencerStance.Read(justOver, null, EightMetres, Bout, 0.0f, true).IsBehindLine, Is.False);
            Assert.That(FencerStance.Read(withinTolerance, null, EightMetres, Bout, 0.0f, true).IsBehindLine, Is.True);
        }

        [Test]
        public void WithNoBodyTheHeadStandsInAndWithNothingItIsNotBehind()
        {
            Assert.That(FencerStance.Read(null, new Vector3(0.0f, 1.6f, -0.5f), EightMetres, Bout, 0.0f, true).IsBehindLine, Is.True);
            Assert.That(FencerStance.Read(null, new Vector3(0.0f, 1.6f, 1.0f), EightMetres, Bout, 0.0f, true).IsBehindLine, Is.False);
            Assert.That(FencerStance.Read(null, null, EightMetres, Bout, 0.0f, true).IsBehindLine, Is.False);
        }

        [Test]
        public void OffTheStripSaysWhichWayButNeverWithoutAPlacedStrip()
        {
            var offSide = PartnerTestData.Body(BodyTrackingFidelity.High, new Vector3(3.0f, 0.0f, -1.0f));
            var pastRear = PartnerTestData.Body(BodyTrackingFidelity.High, new Vector3(0.0f, 0.0f, -4.0f));

            Assert.That(FencerStance.Read(offSide, null, EightMetres, Bout, 0.1f, true).OffStrip, Is.EqualTo(OffStripKind.Side));
            Assert.That(FencerStance.Read(pastRear, null, EightMetres, Bout, 0.1f, true).OffStrip, Is.EqualTo(OffStripKind.RearEnd));
            Assert.That(FencerStance.Read(pastRear, null, EightMetres, Bout, 0.1f, true).IsReady, Is.False, "Behind the line, but off the Strip.");
            Assert.That(FencerStance.Read(offSide, null, EightMetres, Bout, 0.1f, hasPlacedStrip: false).OffStrip, Is.EqualTo(OffStripKind.None));
        }

        [Test]
        public void BodiesApartIsMeasuredAcrossTheFloorBetweenTheChests()
        {
            var mine = PartnerTestData.Body(BodyTrackingFidelity.High, Vector3.Zero);
            var theirs = PartnerTestData.Body(BodyTrackingFidelity.High, new Vector3(0.3f, 0.5f, 0.4f));

            Assert.That(FencerStance.GetBodiesApartMeters(mine, null, theirs), Is.EqualTo(0.5f).Within(1e-4), "Height does not count.");
            Assert.That(FencerStance.GetBodiesApartMeters(null, new Vector3(0.0f, 1.6f, 0.0f), theirs), Is.Not.Null, "The headset stands in for a body with no joints.");
            Assert.That(FencerStance.GetBodiesApartMeters(mine, null, null), Is.Null);
        }

        [Test]
        public void TheCallSpeaksEachWordOnceOnTimeAndWritesIt()
        {
            var announcer = new BoutAnnouncer(BoutSide.Host);
            var calling = Flow(BoutPhase.Calling, 1, callStart: 10.0);

            Assert.That(announcer.Step(calling, 10.0, 10.0), Is.EqualTo(new[] { BoutCallClip.EnGarde }));
            Assert.That(announcer.Word, Is.EqualTo("En garde!"));
            Assert.That(announcer.Step(calling, 10.0, 10.5), Is.Empty, "Spoken once.");
            Assert.That(announcer.Step(calling, 10.0, 11.0), Is.EqualTo(new[] { BoutCallClip.Prets }));
            Assert.That(announcer.Word, Is.EqualTo("Prets?"));

            var fencing = Flow(BoutPhase.Fencing, 2, callStart: 10.0);
            Assert.That(announcer.Step(fencing, 10.0, 12.0), Is.EqualTo(new[] { BoutCallClip.Allez }), "Said even when the phrase opened the same frame.");
            Assert.That(announcer.Word, Is.EqualTo("Allez!"));
            announcer.Step(fencing, 10.0, 12.0 + BoutCall.AllezShownSeconds + 0.01);
            Assert.That(announcer.Word, Is.Empty);
        }

        [Test]
        public void ACallSeenLateIsWrittenButNotSpoken()
        {
            var announcer = new BoutAnnouncer(BoutSide.Partner);

            var due = announcer.Step(Flow(BoutPhase.Fencing, 4, callStart: 10.0), 10.0, 12.6);

            Assert.That(due, Is.Empty);
            Assert.That(announcer.Word, Is.EqualTo("Allez!"));
        }

        [Test]
        public void TheCallKeepsItsWordsWhenThePartnersClockEstimateMoves()
        {
            var announcer = new BoutAnnouncer(BoutSide.Partner);
            var calling = Flow(BoutPhase.Calling, 1, callStart: 510.0);

            announcer.Step(calling, 10.0, 10.0);

            Assert.That(announcer.Step(calling, 10.003, 10.02), Is.Empty, "The same call, with a slightly different local start.");
        }

        [Test]
        public void HalteIsSpokenOnceWithTheReasonFromEachSide()
        {
            var light = new BoutFlowState(PartnerMode.Bout, BoutPhase.Halted, 5, HaltReason.Light, null, OffStripKind.None, LightKind.HostTouch, 0.0, false, false);
            var host = new BoutAnnouncer(BoutSide.Host);
            var partner = new BoutAnnouncer(BoutSide.Partner);

            Assert.That(host.Step(light, 0.0, 20.0), Is.EqualTo(new[] { BoutCallClip.Halte }));
            Assert.That(host.Step(light, 0.0, 20.5), Is.Empty);
            partner.Step(light, 0.0, 20.0);

            Assert.That(host.Word, Is.EqualTo("Halte!"));
            Assert.That(host.Reason, Is.EqualTo("Your Touch"));
            Assert.That(partner.Reason, Is.EqualTo("Opponent's Touch"));
        }

        [Test]
        public void TheHaltReasonsReadRightFromEachSide()
        {
            var offStrip = new BoutFlowState(PartnerMode.Bout, BoutPhase.Halted, 1, HaltReason.OffStrip, BoutSide.Partner, OffStripKind.RearEnd, null, 0.0, false, false);
            var corps = new BoutFlowState(PartnerMode.Bout, BoutPhase.Halted, 1, HaltReason.CorpsACorps, null, OffStripKind.None, null, 0.0, false, false);
            var double_ = new BoutFlowState(PartnerMode.Bout, BoutPhase.Halted, 1, HaltReason.Light, null, OffStripKind.None, LightKind.Double, 0.0, false, false);

            Assert.That(BoutCall.FormatHaltReason(offStrip, BoutSide.Partner), Is.EqualTo("You left the Strip (rear end)"));
            Assert.That(BoutCall.FormatHaltReason(offStrip, BoutSide.Host), Is.EqualTo("Opponent left the Strip (rear end)"));
            Assert.That(BoutCall.FormatHaltReason(corps, BoutSide.Host), Is.EqualTo("Corps-a-corps"));
            Assert.That(BoutCall.FormatHaltReason(double_, BoutSide.Host), Is.EqualTo("Double"));
        }

        [Test]
        public void TheInfoLinePromptsBetweenPhrasesAndShowsTheTallyInPairedPractice()
        {
            var waiting = new BoutFlowState(PartnerMode.Bout, BoutPhase.WaitingForEnGarde, 1, HaltReason.None, null, OffStripKind.None, null, 0.0, false, true);
            var tally = new PairedPracticeTally();
            tally.CountLight(new BoutLight(1, LightKind.PartnerTouch, null, TargetZone.Body));
            tally.CountGlance(BoutSide.Host);
            var practice = new BoutFlowState(PartnerMode.PairedPractice, BoutPhase.PairedPractice, 1, HaltReason.None, null, OffStripKind.None, null, 0.0, false, false);

            Assert.That(BoutCall.FormatInfo(waiting, BoutSide.Host, tally, false), Is.EqualTo("Go back behind your En Garde Line"));
            Assert.That(BoutCall.FormatInfo(waiting, BoutSide.Partner, tally, false), Is.EqualTo("Waiting for your opponent to go back"));
            Assert.That(BoutCall.FormatInfo(practice, BoutSide.Host, tally, false),
                Is.EqualTo("You: 0 Touches, 0 doubles, 1 glance\nOpponent: 1 Touch, 0 doubles, 0 glances"));
            Assert.That(BoutCall.FormatInfo(practice, BoutSide.Host, tally, true), Does.StartWith("You are off the Strip\n"));
        }

        [Test]
        public void TheTallyCountsSinglesDoublesAndGlancesAndALateDoubleMovesTheTouch()
        {
            var tally = new PairedPracticeTally();

            tally.CountLight(new BoutLight(1, LightKind.HostTouch, TargetZone.Body, null));
            tally.CountLight(new BoutLight(1, LightKind.HostTouch, TargetZone.Body, null));
            Assert.That(tally.Host.Touches, Is.EqualTo(1), "The same light counts once.");

            tally.CountLight(new BoutLight(1, LightKind.Double, TargetZone.Body, TargetZone.Hand));
            Assert.That(tally.Host, Is.EqualTo(new PairedPracticeCounts(0, 1, 0)));
            Assert.That(tally.Partner, Is.EqualTo(new PairedPracticeCounts(0, 1, 0)));

            tally.CountLight(new BoutLight(2, LightKind.PartnerTouch, null, TargetZone.Foot));
            tally.CountGlance(BoutSide.Partner);
            Assert.That(tally.Partner, Is.EqualTo(new PairedPracticeCounts(1, 1, 1)));

            tally.Reset();
            Assert.That(tally.IsEmpty, Is.True);
            tally.CountLight(new BoutLight(2, LightKind.Double, TargetZone.Body, TargetZone.Foot));
            Assert.That(tally.Partner, Is.EqualTo(new PairedPracticeCounts(0, 1, 0)), "Upgraded after a reset: a new double, never below zero.");
        }

        [Test]
        public void TheStanceAndFlowMessagesComeBackTheSame()
        {
            var stance = BoutMessages.DecodeStance(BoutMessages.EncodeStance(new FencerStance(true, OffStripKind.RearEnd)));
            Assert.That(stance.IsBehindLine, Is.True);
            Assert.That(stance.OffStrip, Is.EqualTo(OffStripKind.RearEnd));

            var state = new BoutFlowState(PartnerMode.Bout, BoutPhase.Halted, 42, HaltReason.OffStrip, BoutSide.Partner, OffStripKind.Side, null, 1234.5, true, false);
            var (decoded, hostTally, partnerTally) = BoutMessages.DecodeFlow(BoutMessages.EncodeFlow(state, new PairedPracticeCounts(1, 2, 3), new PairedPracticeCounts(4, 2, 0)));

            Assert.That(decoded.Mode, Is.EqualTo(PartnerMode.Bout));
            Assert.That(decoded.Phase, Is.EqualTo(BoutPhase.Halted));
            Assert.That(decoded.Sequence, Is.EqualTo(42));
            Assert.That(decoded.Halt, Is.EqualTo(HaltReason.OffStrip));
            Assert.That(decoded.HaltedBy, Is.EqualTo(BoutSide.Partner));
            Assert.That(decoded.HaltOffStrip, Is.EqualTo(OffStripKind.Side));
            Assert.That(decoded.HaltLight, Is.Null);
            Assert.That(decoded.CallStartHostSeconds, Is.EqualTo(1234.5));
            Assert.That(decoded.IsHostReady, Is.True);
            Assert.That(decoded.IsPartnerReady, Is.False);
            Assert.That(hostTally, Is.EqualTo(new PairedPracticeCounts(1, 2, 3)));
            Assert.That(partnerTally, Is.EqualTo(new PairedPracticeCounts(4, 2, 0)));

            Assert.DoesNotThrow(() => BoutMessages.DecodeGlance(BoutMessages.EncodeGlance()));
            Assert.DoesNotThrow(() => BoutMessages.DecodeTallyReset(BoutMessages.EncodeTallyReset()));
        }

        [Test]
        public void ABrokenFlowMessageIsRefused()
        {
            var message = BoutMessages.EncodeFlow(BoutFlowState.Initial, default, default);
            var cut = message.Take(message.Length - 1).ToArray();
            var badPhase = (byte[])message.Clone();
            badPhase[3] = 77;

            Assert.Throws<System.IO.InvalidDataException>(() => BoutMessages.DecodeFlow(cut));
            Assert.Throws<System.IO.InvalidDataException>(() => BoutMessages.DecodeFlow(badPhase));
            Assert.Throws<System.IO.InvalidDataException>(() => BoutMessages.DecodeStance(new byte[] { BoutMessages.StanceKind, BoutMessages.FormatVersion, 1, 9 }));
        }

        [Test]
        public void TheCreditsNameTheSpeakerAndBothLicences()
        {
            Assert.That(BoutCall.Credits, Does.Contain("LoquaxFR"));
            Assert.That(BoutCall.Credits, Does.Contain("CC BY 4.0"));
            Assert.That(BoutCall.Credits, Does.Contain("CC BY-SA 4.0"));
        }

        private static BoutFlowState Flow(BoutPhase phase, int sequence, double callStart)
        {
            return new BoutFlowState(PartnerMode.Bout, phase, sequence, HaltReason.None, null, OffStripKind.None, null, callStart, true, true);
        }

        /// <summary>A Fencer in en garde moved along the Strip so the front toe is at <paramref name="frontToeZ"/>.</summary>
        private static FencerBody BodyWithFrontToeAt(float frontToeZ)
        {
            var body = PartnerTestData.Body(BodyTrackingFidelity.High, Vector3.Zero);
            var front = new[] { SkeletonJoint.FrontToe, SkeletonJoint.FrontAnkle, SkeletonJoint.BackToe, SkeletonJoint.BackAnkle }
                .Max(joint => body.Get(joint).Value.Z);
            return PartnerTestData.Body(BodyTrackingFidelity.High, new Vector3(0.0f, 0.0f, frontToeZ - front));
        }
    }
}
