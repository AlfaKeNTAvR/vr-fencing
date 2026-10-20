// The bout flow on the Host (PER-83, decided in PER-56, PER-57, PER-58 and PER-72): the En Garde Line gate and the
// call, the halts (a light, off the Strip, corps-a-corps), and Paired Practice with no calls, halts or lock-out.
using NUnit.Framework;
using VrFencing.Dummy;
using VrFencing.Partner;

namespace VrFencing.Tests.Partner
{
    public class BoutFlowTests
    {
        private const double Start = 50.0;
        private const double FrameSeconds = 1.0 / 72.0;

        private static readonly FencerStance Ready = new FencerStance(true, OffStripKind.None);
        private static readonly FencerStance Forward = new FencerStance(false, OffStripKind.None);

        [Test]
        public void ABoutStartsWaitingWithThePhraseClosed()
        {
            var judge = new BoutJudge();
            var flow = new BoutFlow(judge);

            Assert.That(flow.State.Mode, Is.EqualTo(PartnerMode.Bout));
            Assert.That(flow.State.Phase, Is.EqualTo(BoutPhase.WaitingForEnGarde));
            Assert.That(judge.Submit(Claim(BoutSide.Host, Start), Start), Is.EqualTo(ClaimVerdict.LockedOut));
        }

        [Test]
        public void BothBehindTheirLinesForOneSecondCallsAndAllezOpensThePhrase()
        {
            var judge = new BoutJudge();
            var flow = new BoutFlow(judge);

            var now = Run(flow, Start, BoutFlow.EnGardeHoldSeconds - 0.05, Ready, Ready);
            Assert.That(flow.State.Phase, Is.EqualTo(BoutPhase.WaitingForEnGarde), "Not a full second yet.");

            now = Run(flow, now, 0.1, Ready, Ready);
            Assert.That(flow.State.Phase, Is.EqualTo(BoutPhase.Calling));
            var callStart = flow.State.CallStartHostSeconds;
            Assert.That(callStart - Start, Is.EqualTo(BoutFlow.EnGardeHoldSeconds).Within(FrameSeconds * 2));
            Assert.That(judge.Submit(Claim(BoutSide.Host, now), now), Is.EqualTo(ClaimVerdict.LockedOut), "Not before \"Allez!\".");

            Run(flow, now, BoutCall.AllezAtSeconds, Ready, Ready);
            Assert.That(flow.State.Phase, Is.EqualTo(BoutPhase.Fencing));
            var allez = callStart + BoutCall.AllezAtSeconds;
            Assert.That(judge.Submit(Claim(BoutSide.Host, allez + 0.01), allez + 0.02), Is.EqualTo(ClaimVerdict.Accepted));
        }

        [Test]
        public void AStepForwardStartsTheSecondAgain()
        {
            var flow = new BoutFlow(new BoutJudge());

            var now = Run(flow, Start, 0.8, Ready, Ready);
            now = Run(flow, now, FrameSeconds, Forward, Ready);
            Run(flow, now, 0.8, Ready, Ready);

            Assert.That(flow.State.Phase, Is.EqualTo(BoutPhase.WaitingForEnGarde));
            Assert.That(flow.State.IsHostReady, Is.True);
            Assert.That(flow.State.IsPartnerReady, Is.True);
        }

        [Test]
        public void AnUnknownStanceIsNeverReady()
        {
            var flow = new BoutFlow(new BoutJudge());

            Run(flow, Start, 3.0, Ready, null);

            Assert.That(flow.State.Phase, Is.EqualTo(BoutPhase.WaitingForEnGarde));
            Assert.That(flow.State.IsPartnerReady, Is.False);
        }

        [Test]
        public void ALightHaltsThenTheFlowWaitsForTheLinesAgain()
        {
            var judge = new BoutJudge();
            var flow = new BoutFlow(judge);
            var now = StartPhrase(flow);

            judge.Submit(Claim(BoutSide.Partner, now), now);
            var light = judge.Step(now + 0.12).Value;
            flow.OnLight(light, now + 0.12);

            Assert.That(flow.State.Phase, Is.EqualTo(BoutPhase.Halted));
            Assert.That(flow.State.Halt, Is.EqualTo(HaltReason.Light));
            Assert.That(flow.State.HaltLight, Is.EqualTo(LightKind.PartnerTouch));

            now = Run(flow, now + 0.12, BoutFlow.HaltShownSeconds - 0.1, Ready, Ready);
            Assert.That(flow.State.Phase, Is.EqualTo(BoutPhase.Halted), "\"Halte!\" shows for a while even with both already back.");
            now = Run(flow, now, 0.2, Forward, Ready);
            Assert.That(flow.State.Phase, Is.EqualTo(BoutPhase.WaitingForEnGarde));
            Assert.That(judge.Submit(Claim(BoutSide.Host, now), now), Is.EqualTo(ClaimVerdict.LockedOut), "Nothing counts until the next \"Allez!\".");
        }

        [TestCase(true, OffStripKind.Side)]
        [TestCase(false, OffStripKind.RearEnd)]
        public void LeavingTheStripHaltsNamingWhoAndWhichWay(bool isHost, OffStripKind way)
        {
            var judge = new BoutJudge();
            var flow = new BoutFlow(judge);
            var now = StartPhrase(flow);
            var off = new FencerStance(false, way);

            flow.Step(now, isHost ? off : Ready, isHost ? Ready : off, 3.0f);

            Assert.That(flow.State.Phase, Is.EqualTo(BoutPhase.Halted));
            Assert.That(flow.State.Halt, Is.EqualTo(HaltReason.OffStrip));
            Assert.That(flow.State.HaltedBy, Is.EqualTo(isHost ? BoutSide.Host : BoutSide.Partner));
            Assert.That(flow.State.HaltOffStrip, Is.EqualTo(way));
            Assert.That(judge.Submit(Claim(BoutSide.Host, now + 0.01), now + 0.02), Is.EqualTo(ClaimVerdict.LockedOut));
        }

        [Test]
        public void BodiesWithinHalfAMetreHaltForCorpsACorps()
        {
            var flow = new BoutFlow(new BoutJudge());
            var now = StartPhrase(flow);

            flow.Step(now, Ready, Ready, FencerStance.CorpsACorpsMeters + 0.05f);
            Assert.That(flow.State.Phase, Is.EqualTo(BoutPhase.Fencing));

            flow.Step(now + FrameSeconds, Ready, Ready, FencerStance.CorpsACorpsMeters - 0.05f);
            Assert.That(flow.State.Phase, Is.EqualTo(BoutPhase.Halted));
            Assert.That(flow.State.Halt, Is.EqualTo(HaltReason.CorpsACorps));
        }

        [Test]
        public void PairedPracticeNeverCallsOrHaltsAndJudgesWithNoLockout()
        {
            var judge = new BoutJudge();
            var flow = new BoutFlow(judge);

            Assert.That(flow.SetMode(PartnerMode.PairedPractice, Start), Is.True);
            var now = Run(flow, Start, 3.0, Ready, Ready);
            flow.Step(now, new FencerStance(false, OffStripKind.Side), Ready, 0.1f);

            Assert.That(flow.State.Phase, Is.EqualTo(BoutPhase.PairedPractice));
            Assert.That(judge.KeepsPhraseClosedAfterLight, Is.False);
            Assert.That(judge.RefusesOffStrip, Is.False);
            Assert.That(judge.Submit(new TouchClaim(1, BoutSide.Host, TargetZone.Body, now, isOffStrip: true), now), Is.EqualTo(ClaimVerdict.Accepted));
        }

        [Test]
        public void SwitchingBackToABoutClosesThePhraseAndWaits()
        {
            var judge = new BoutJudge();
            var flow = new BoutFlow(judge);
            flow.SetMode(PartnerMode.PairedPractice, Start);

            Assert.That(flow.SetMode(PartnerMode.Bout, Start + 5.0), Is.True);
            Assert.That(flow.SetMode(PartnerMode.Bout, Start + 5.0), Is.False, "Already a Bout.");

            Assert.That(flow.State.Phase, Is.EqualTo(BoutPhase.WaitingForEnGarde));
            Assert.That(judge.RefusesOffStrip, Is.True);
            Assert.That(judge.Submit(Claim(BoutSide.Host, Start + 5.1), Start + 5.1), Is.EqualTo(ClaimVerdict.LockedOut));
        }

        [Test]
        public void EveryPhaseChangeCountsUp()
        {
            var flow = new BoutFlow(new BoutJudge());
            var first = flow.State.Sequence;

            StartPhrase(flow);

            Assert.That(flow.State.Sequence, Is.EqualTo(first + 2), "Calling, then Fencing.");
        }

        /// <summary>Both ready until the phrase is on; returns the time "Allez!" opened it.</summary>
        private static double StartPhrase(BoutFlow flow)
        {
            var now = Run(flow, Start, BoutFlow.EnGardeHoldSeconds + BoutCall.AllezAtSeconds + 0.05, Ready, Ready);
            Assert.That(flow.State.Phase, Is.EqualTo(BoutPhase.Fencing));
            return now;
        }

        private static double Run(BoutFlow flow, double from, double seconds, FencerStance? host, FencerStance? partner)
        {
            var now = from;
            var end = from + seconds;
            while (now < end)
            {
                now += FrameSeconds;
                flow.Step(now, host, partner, 3.0f);
            }

            return now;
        }

        private static TouchClaim Claim(BoutSide attacker, double contact)
        {
            return new TouchClaim(1, attacker, TargetZone.Body, contact, isOffStrip: false);
        }
    }
}
