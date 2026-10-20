// Both headsets' side of the judge (PER-82) and the bout flow (PER-83), wired together through a pretend network with
// a one-way delay and two clocks that read differently: the Partner's claims land on the Host's clock, the Host's lights
// and flow reach the Partner, both see the ping and hear the calls together, being touched is felt once, and the
// Paired Practice tally is kept on the Host and saved on both.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VrFencing.Dummy;
using VrFencing.Partner;
using VrFencing.Session;
using VrFencing.Settings;

namespace VrFencing.Tests.Partner
{
    public class BoutLinkTests
    {
        // The Partner's clock reads this much less than the Host's (apps started at different times).
        private const double PartnerBehindHostSeconds = 500.0;
        private const double OneWaySeconds = 0.03;
        private const double FrameSeconds = 1.0 / 72.0;

        [Test]
        public void BothSidesMeasureTheRoundTripAndThePartnerFindsTheHostsClock()
        {
            var bout = new PretendBout();

            bout.RunFor(1.2);

            Assert.That(bout.Host.Clock.RoundTripSeconds, Is.EqualTo(2 * OneWaySeconds).Within(FrameSeconds * 2));
            Assert.That(bout.Partner.Clock.RoundTripSeconds, Is.EqualTo(2 * OneWaySeconds).Within(FrameSeconds * 2));
            Assert.That(bout.Partner.Clock.OffsetSeconds, Is.EqualTo(PartnerBehindHostSeconds).Within(FrameSeconds), "Off by at most the frame the answer waited for.");
        }

        [Test]
        public void ThePartnersTouchLightsOnBothBoxesAndTheHostFeelsIt()
        {
            var bout = new PretendBout();
            bout.OpenThePhrase();
            var touchedAtHost = bout.HostNow;

            Assert.That(bout.Partner.Claim(TargetZone.Body, bout.PartnerNow, isOffStrip: false, bout.PartnerNow), Is.True);
            bout.RunFor(0.3);

            Assert.That(bout.Host.Box.LastLight.Value.Kind, Is.EqualTo(LightKind.PartnerTouch));
            Assert.That(bout.Partner.Box.LastLight.Value.Kind, Is.EqualTo(LightKind.PartnerTouch));
            Assert.That(bout.Partner.Box.IsMyLampLit(bout.PartnerNow), Is.True, "Green: the Partner's own Touch.");
            Assert.That(bout.Host.Box.IsOpponentsLampLit(bout.HostNow), Is.True, "Red on the Host's box.");
            Assert.That(bout.Host.TakeBeingTouched(), Is.True);
            Assert.That(bout.Host.TakeBeingTouched(), Is.False, "Felt once.");
            Assert.That(bout.Partner.TakeBeingTouched(), Is.False);
            Assert.That(bout.HostLog, Has.Some.Contains("Touch 1 by the partner (Body").And.Contains("Accepted"));
            Assert.That(bout.HostNow - touchedAtHost, Is.GreaterThanOrEqualTo(0.12), "Lit after the light delay.");
        }

        [Test]
        public void TouchesFromBothWithinTheWindowAreADoubleOnBothBoxes()
        {
            var bout = new PretendBout();
            bout.OpenThePhrase();

            bout.Host.Claim(TargetZone.Hand, bout.HostNow, false, bout.HostNow);
            bout.Step();
            bout.Step();
            bout.Partner.Claim(TargetZone.Thigh, bout.PartnerNow, false, bout.PartnerNow);
            bout.RunFor(0.3);

            Assert.That(bout.Host.Box.LastLight.Value.Kind, Is.EqualTo(LightKind.Double));
            Assert.That(bout.Partner.Box.LastLight.Value.Kind, Is.EqualTo(LightKind.Double));
            Assert.That(bout.Host.TakeBeingTouched(), Is.True);
            Assert.That(bout.Partner.TakeBeingTouched(), Is.True);
        }

        [Test]
        public void ThePartnerCannotClaimBeforeItsClockIsSynced()
        {
            var bout = new PretendBout();

            Assert.That(bout.Partner.Claim(TargetZone.Body, 0.0, false, 0.0), Is.False);
            Assert.That(bout.Partner.TakeLog(), Has.Some.Contains("not synced"));
        }

        [Test]
        public void TheHostsRulesReachThePartner()
        {
            var bout = new PretendBout();
            bout.Host.SetMyRules(new BoutRules(TouchPressure.OnContact, 0.1));
            bout.Partner.SetMyRules(new BoutRules(TouchPressure.Realistic, 0.05));

            Assert.That(bout.Partner.HasHostRules, Is.False);
            Assert.That(bout.Partner.Rules.Pressure, Is.EqualTo(TouchPressure.Realistic), "Its own until the Host's arrive.");

            bout.RunFor(0.2);

            Assert.That(bout.Partner.HasHostRules, Is.True);
            Assert.That(bout.Partner.Rules.Pressure, Is.EqualTo(TouchPressure.OnContact));
            Assert.That(bout.Partner.Rules.DoubleWindowSeconds, Is.EqualTo(0.1));
            Assert.That(bout.Host.Judge.DoubleWindowSeconds, Is.EqualTo(0.1));
        }

        [Test]
        public void ClaimsLightsAndRulesOnlyCountFromTheRightSide()
        {
            var host = new BoutLink(BoutSide.Host);
            var partner = new BoutLink(BoutSide.Partner);

            Assert.That(partner.Receive(BoutMessages.EncodeClaim(new TouchClaim(1, BoutSide.Host, TargetZone.Body, 0.0, false)), 0.0), Is.False);
            Assert.That(host.Receive(BoutMessages.EncodeLight(new BoutLight(1, LightKind.HostTouch, TargetZone.Body, null)), 0.0), Is.False);
            Assert.That(host.Receive(BoutMessages.EncodeClaim(new TouchClaim(1, BoutSide.Host, TargetZone.Body, 0.0, false)), 0.0), Is.False, "The Partner cannot claim for the Host.");
            Assert.That(host.Receive(new byte[] { 9, 9, 9 }, 0.0), Is.False);
            Assert.That(host.RejectedMessages, Is.EqualTo(3));
            Assert.That(partner.RejectedMessages, Is.EqualTo(1));
        }

        [Test]
        public void LosingThePartnerForgetsTheConnectionSoARejoinStartsClean()
        {
            var bout = new PretendBout();
            bout.OpenThePhrase();
            bout.Partner.Claim(TargetZone.Body, bout.PartnerNow, false, bout.PartnerNow);
            bout.RunFor(0.3);

            bout.Partner.LosePartner(mayRejoin: false, bout.PartnerNow);
            bout.Host.LosePartner(mayRejoin: true, bout.HostNow);

            Assert.That(bout.Partner.Clock.IsSynced, Is.False);
            Assert.That(bout.Partner.HasHostRules, Is.False);
            Assert.That(bout.Partner.Box.IsMyLampLit(bout.PartnerNow), Is.False);
            Assert.That(bout.Host.Clock.IsSynced, Is.False);
            Assert.That(bout.Host.FlowState.Phase, Is.EqualTo(BoutPhase.WaitingForEnGarde), "The Bout starts again from the lines.");
        }

        [Test]
        public void ADroppedPartnerRejoinsAndTheTallyCarriesOn()
        {
            var bout = new PretendBout();
            bout.Host.SetMode(PartnerMode.PairedPractice, bout.HostNow);
            bout.RunFor(1.2);
            bout.Partner.Claim(TargetZone.Body, bout.PartnerNow, false, bout.PartnerNow);
            bout.Host.ReportGlance();
            bout.RunFor(0.3);

            bout.Drop();
            bout.Host.LosePartner(mayRejoin: true, bout.HostNow);
            bout.Partner.LosePartner(mayRejoin: true, bout.PartnerNow);

            var hostSoFar = bout.Host.TakeEndedTallies().Single();
            var partnerSoFar = bout.Partner.TakeEndedTallies().Single();
            Assert.That(hostSoFar.End, Is.EqualTo(PairedPracticeEnd.Interrupted), "Each headset writes its log up to the drop.");
            Assert.That(hostSoFar.Mine, Is.EqualTo(new PairedPracticeCounts(0, 0, 1)));
            Assert.That(partnerSoFar.End, Is.EqualTo(PairedPracticeEnd.Interrupted));
            Assert.That(partnerSoFar.Mine, Is.EqualTo(new PairedPracticeCounts(1, 0, 0)));
            Assert.That(bout.Host.Tally.IsEmpty, Is.False, "The Host keeps the tally for a rejoin.");
            Assert.That(bout.Host.FlowState.Mode, Is.EqualTo(PartnerMode.PairedPractice), "The mode stays.");

            bout.RunFor(30.0);
            bout.Rejoin();
            bout.RunFor(1.2);

            Assert.That(bout.Partner.Tally.Partner, Is.EqualTo(new PairedPracticeCounts(1, 0, 0)), "The rejoined Partner sees the kept tally.");
            Assert.That(bout.Partner.Tally.Host, Is.EqualTo(new PairedPracticeCounts(0, 0, 1)));

            bout.Partner.Claim(TargetZone.Hand, bout.PartnerNow, false, bout.PartnerNow);
            bout.RunFor(0.3);
            Assert.That(bout.Host.Tally.Partner, Is.EqualTo(new PairedPracticeCounts(2, 0, 0)), "Counting goes on from the kept tally.");
            Assert.That(bout.Partner.Tally.Partner, Is.EqualTo(new PairedPracticeCounts(2, 0, 0)));
        }

        [Test]
        public void ThePartnerNotComingBackEndsTheTallyAndResetsIt()
        {
            var bout = new PretendBout();
            bout.Host.SetMode(PartnerMode.PairedPractice, bout.HostNow);
            bout.RunFor(1.2);
            bout.Host.ReportGlance();
            bout.RunFor(0.2);
            bout.Drop();
            bout.Host.LosePartner(mayRejoin: true, bout.HostNow);
            bout.Host.TakeEndedTallies();

            bout.Host.EndSession();

            var ended = bout.Host.TakeEndedTallies().Single();
            Assert.That(ended.End, Is.EqualTo(PairedPracticeEnd.Disconnected));
            Assert.That(ended.Mine, Is.EqualTo(new PairedPracticeCounts(0, 0, 1)));
            Assert.That(bout.Host.Tally.IsEmpty, Is.True);
        }

        [Test]
        public void SwitchingToABoutWhileThePartnerIsAwayEndsTheKeptTally()
        {
            var bout = new PretendBout();
            bout.Host.SetMode(PartnerMode.PairedPractice, bout.HostNow);
            bout.RunFor(1.2);
            bout.Host.ReportGlance();
            bout.RunFor(0.2);
            bout.Drop();
            bout.Host.LosePartner(mayRejoin: true, bout.HostNow);
            bout.Host.TakeEndedTallies();

            bout.Host.SetMode(PartnerMode.Bout, bout.HostNow);

            Assert.That(bout.Host.TakeEndedTallies().Single().End, Is.EqualTo(PairedPracticeEnd.SwitchedToBout));
            Assert.That(bout.Host.Tally.IsEmpty, Is.True);
        }

        [Test]
        public void ASnapshotSavesTheTallySoFarAndKeepsCounting()
        {
            var bout = new PretendBout();
            bout.Host.SetMode(PartnerMode.PairedPractice, bout.HostNow);
            bout.RunFor(1.2);
            bout.Host.ReportGlance();

            bout.Host.SnapshotTally();

            Assert.That(bout.Host.TakeEndedTallies().Single().End, Is.EqualTo(PairedPracticeEnd.Interrupted));
            Assert.That(bout.Host.Tally.Host, Is.EqualTo(new PairedPracticeCounts(0, 0, 1)), "Still counting.");
        }

        [Test]
        public void BothBehindTheirLinesHearTheCallTogetherAndThePhraseOpensOnAllez()
        {
            var bout = new PretendBout();
            bout.RunFor(1.2);
            bout.Host.SetMyStance(Ready, 3.0f);
            bout.Partner.SetMyStance(Ready, null);

            bout.RunFor(BoutFlow.EnGardeHoldSeconds + BoutCall.AllezAtSeconds + 0.2);

            Assert.That(bout.Host.FlowState.Phase, Is.EqualTo(BoutPhase.Fencing));
            Assert.That(bout.Partner.FlowState.Phase, Is.EqualTo(BoutPhase.Fencing));
            Assert.That(bout.HostCalls.Select(call => call.Clip), Is.EqualTo(new[] { BoutCallClip.EnGarde, BoutCallClip.Prets, BoutCallClip.Allez }));
            Assert.That(bout.PartnerCalls.Select(call => call.Clip), Is.EqualTo(new[] { BoutCallClip.EnGarde, BoutCallClip.Prets, BoutCallClip.Allez }));
            var hostAllez = bout.HostCalls.Last().HostSeconds;
            var partnerAllez = bout.PartnerCalls.Last().HostSeconds;
            Assert.That(partnerAllez - hostAllez, Is.EqualTo(0.0).Within(FrameSeconds * 1.5), "\"Allez!\" together, not a one-way trip late.");
        }

        [Test]
        public void ThePartnersTouchHaltsBothAndNothingCountsUntilTheNextCall()
        {
            var bout = new PretendBout();
            bout.OpenThePhrase();

            bout.Partner.Claim(TargetZone.Body, bout.PartnerNow, false, bout.PartnerNow);
            bout.RunFor(0.3);

            Assert.That(bout.Host.FlowState.Phase, Is.EqualTo(BoutPhase.Halted));
            Assert.That(bout.Partner.FlowState.Phase, Is.EqualTo(BoutPhase.Halted));
            Assert.That(bout.Host.Announcer.Reason, Is.EqualTo("Opponent's Touch"));
            Assert.That(bout.Partner.Announcer.Reason, Is.EqualTo("Your Touch"));
            Assert.That(bout.HostCalls.Last().Clip, Is.EqualTo(BoutCallClip.Halte));
            Assert.That(bout.PartnerCalls.Last().Clip, Is.EqualTo(BoutCallClip.Halte));

            // Both stepped forward after the halt: the lamps go off, but nothing counts.
            bout.Host.SetMyStance(new FencerStance(false, OffStripKind.None), 3.0f);
            bout.RunFor(BoutJudge.LampSeconds + 0.5);
            bout.Host.Claim(TargetZone.Body, bout.HostNow, false, bout.HostNow);
            bout.RunFor(0.3);

            Assert.That(bout.Host.FlowState.Phase, Is.EqualTo(BoutPhase.WaitingForEnGarde));
            Assert.That(bout.HostLog, Has.Some.Contains("Touch 1 by this Fencer").And.Contains("LockedOut"));
            Assert.That(bout.Partner.Announcer.Word, Is.Empty);
        }

        [Test]
        public void ThePartnerLeavingTheirStripHaltsTheBout()
        {
            var bout = new PretendBout();
            bout.OpenThePhrase();

            bout.Partner.SetMyStance(new FencerStance(false, OffStripKind.Side), null);
            bout.RunFor(0.3);

            Assert.That(bout.Host.FlowState.Halt, Is.EqualTo(HaltReason.OffStrip));
            Assert.That(bout.Partner.Announcer.Reason, Is.EqualTo("You left the Strip (side)"));
            Assert.That(bout.Host.Announcer.Reason, Is.EqualTo("Opponent left the Strip (side)"));
        }

        [Test]
        public void PairedPracticeTalliesOnTheHostShowsOnThePartnerAndSavesOnBothWhenReset()
        {
            var bout = new PretendBout();
            bout.Host.SetMode(PartnerMode.PairedPractice, bout.HostNow);
            bout.RunFor(1.2);

            bout.Partner.Claim(TargetZone.Body, bout.PartnerNow, true, bout.PartnerNow);
            bout.RunFor(0.3);
            bout.Host.Claim(TargetZone.Hand, bout.HostNow, false, bout.HostNow);
            bout.Partner.ReportGlance();
            bout.RunFor(0.3);

            Assert.That(bout.Host.FlowState.Phase, Is.EqualTo(BoutPhase.PairedPractice), "No halts.");
            Assert.That(bout.Partner.Tally.Partner, Is.EqualTo(new PairedPracticeCounts(1, 0, 1)), "The off-Strip Touch lit; the glance counted.");
            Assert.That(bout.Partner.Tally.Host, Is.EqualTo(new PairedPracticeCounts(1, 0, 0)), "No lock-out after the first light.");

            bout.Partner.ResetTally();
            bout.RunFor(0.2);

            var partnerSaved = bout.Partner.TakeEndedTallies().Single();
            var hostSaved = bout.Host.TakeEndedTallies().Single();
            Assert.That(partnerSaved.Mine, Is.EqualTo(new PairedPracticeCounts(1, 0, 1)));
            Assert.That(partnerSaved.Opponents, Is.EqualTo(new PairedPracticeCounts(1, 0, 0)));
            Assert.That(hostSaved.End, Is.EqualTo(PairedPracticeEnd.Reset));
            Assert.That(hostSaved.Mine, Is.EqualTo(new PairedPracticeCounts(1, 0, 0)));
            Assert.That(bout.Host.Tally.IsEmpty, Is.True);
            Assert.That(bout.Partner.Tally.IsEmpty, Is.True);
        }

        [Test]
        public void SwitchingToABoutEndsTheTallyOnBothSides()
        {
            var bout = new PretendBout();
            bout.Host.SetMode(PartnerMode.PairedPractice, bout.HostNow);
            bout.RunFor(1.2);
            bout.Host.ReportGlance();
            bout.RunFor(0.2);

            bout.Host.SetMode(PartnerMode.Bout, bout.HostNow);
            bout.RunFor(0.2);

            Assert.That(bout.Host.TakeEndedTallies().Single().End, Is.EqualTo(PairedPracticeEnd.SwitchedToBout));
            Assert.That(bout.Partner.TakeEndedTallies().Single().Opponents, Is.EqualTo(new PairedPracticeCounts(0, 0, 1)));
            Assert.That(bout.Partner.FlowState.Mode, Is.EqualTo(PartnerMode.Bout));
        }

        [Test]
        public void TheNewMessagesOnlyCountFromTheRightSide()
        {
            var host = new BoutLink(BoutSide.Host);
            var partner = new BoutLink(BoutSide.Partner);

            Assert.That(partner.Receive(BoutMessages.EncodeStance(Ready), 0.0), Is.False);
            Assert.That(partner.Receive(BoutMessages.EncodeGlance(), 0.0), Is.False);
            Assert.That(partner.Receive(BoutMessages.EncodeTallyReset(), 0.0), Is.False);
            Assert.That(host.Receive(BoutMessages.EncodeFlow(BoutFlowState.Initial, default, default), 0.0), Is.False);
            Assert.That(partner.RejectedMessages, Is.EqualTo(3));
            Assert.That(host.RejectedMessages, Is.EqualTo(1));
        }

        private static readonly FencerStance Ready = new FencerStance(true, OffStripKind.None);

        /// <summary>Two links, two clocks, and messages that arrive <see cref="OneWaySeconds"/> after they were sent, checked once a frame.</summary>
        private sealed class PretendBout
        {
            public List<(BoutCallClip Clip, double HostSeconds)> HostCalls { get; } = new List<(BoutCallClip, double)>();

            public List<(BoutCallClip Clip, double HostSeconds)> PartnerCalls { get; } = new List<(BoutCallClip, double)>();

            /// <summary>Syncs the clocks, then both stand behind their lines until "Allez!" has opened the phrase.</summary>
            public void OpenThePhrase()
            {
                RunFor(1.2);
                Host.SetMyStance(Ready, 3.0f);
                Partner.SetMyStance(Ready, null);
                RunFor(BoutFlow.EnGardeHoldSeconds + BoutCall.AllezAtSeconds + 0.2);
                Assert.That(Host.FlowState.Phase, Is.EqualTo(BoutPhase.Fencing));
            }

            private readonly List<(double ArriveHostSeconds, byte[] Message)> _toHost = new List<(double, byte[])>();
            private readonly List<(double ArriveHostSeconds, byte[] Message)> _toPartner = new List<(double, byte[])>();

            public BoutLink Host { get; } = new BoutLink(BoutSide.Host);

            public BoutLink Partner { get; private set; } = new BoutLink(BoutSide.Partner);

            /// <summary>The connection drops: whatever is on the way is lost, and nothing goes through until <see cref="Rejoin"/>.</summary>
            public void Drop()
            {
                _isLinkUp = false;
                _toHost.Clear();
                _toPartner.Clear();
            }

            /// <summary>The Partner joins again as a new side (its app made a new one), the Host keeping its own.</summary>
            public void Rejoin()
            {
                Partner = new BoutLink(BoutSide.Partner);
                _isLinkUp = true;
            }

            private bool _isLinkUp = true;

            public List<string> HostLog { get; } = new List<string>();

            public double HostNow { get; private set; } = 1000.0;

            public double PartnerNow => HostNow - PartnerBehindHostSeconds;

            public void RunFor(double seconds)
            {
                var end = HostNow + seconds;
                while (HostNow < end)
                {
                    Step();
                }
            }

            public void Step()
            {
                HostNow += FrameSeconds;
                Deliver(_toHost, message => Host.Receive(message, HostNow));
                Deliver(_toPartner, message => Partner.Receive(message, PartnerNow));
                Host.Tick(HostNow);
                Partner.Tick(PartnerNow);
                var toPartner = Host.TakeOutgoing();
                var toHost = Partner.TakeOutgoing();
                if (_isLinkUp)
                {
                    _toPartner.AddRange(toPartner.Select(outgoing => (HostNow + OneWaySeconds, outgoing.Message)));
                    _toHost.AddRange(toHost.Select(outgoing => (HostNow + OneWaySeconds, outgoing.Message)));
                }

                HostLog.AddRange(Host.TakeLog());
                HostCalls.AddRange(Host.TakeCalls().Select(clip => (clip, HostNow)));
                PartnerCalls.AddRange(Partner.TakeCalls().Select(clip => (clip, HostNow)));
            }

            private void Deliver(List<(double ArriveHostSeconds, byte[] Message)> queue, System.Func<byte[], bool> receive)
            {
                var due = queue.Where(item => item.ArriveHostSeconds <= HostNow).ToList();
                foreach (var item in due)
                {
                    receive(item.Message);
                    queue.Remove(item);
                }
            }
        }
    }
}
