// Both headsets' side of the judge (PER-82), wired together through a pretend network with a one-way delay and two
// clocks that read differently: the Partner's claims land on the Host's clock, the Host's lights reach the Partner,
// both see the ping, and being touched is felt once.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VrFencing.Dummy;
using VrFencing.Partner;
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
            bout.RunFor(1.2);
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
            bout.RunFor(1.2);

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
        public void ClearForgetsThePartnerSoARejoinStartsClean()
        {
            var bout = new PretendBout();
            bout.RunFor(1.2);
            bout.Partner.Claim(TargetZone.Body, bout.PartnerNow, false, bout.PartnerNow);
            bout.RunFor(0.3);

            bout.Partner.Clear();

            Assert.That(bout.Partner.Clock.IsSynced, Is.False);
            Assert.That(bout.Partner.HasHostRules, Is.False);
            Assert.That(bout.Partner.Box.IsMyLampLit(bout.PartnerNow), Is.False);
        }

        /// <summary>Two links, two clocks, and messages that arrive <see cref="OneWaySeconds"/> after they were sent, checked once a frame.</summary>
        private sealed class PretendBout
        {
            private readonly List<(double ArriveHostSeconds, byte[] Message)> _toHost = new List<(double, byte[])>();
            private readonly List<(double ArriveHostSeconds, byte[] Message)> _toPartner = new List<(double, byte[])>();

            public BoutLink Host { get; } = new BoutLink(BoutSide.Host);

            public BoutLink Partner { get; } = new BoutLink(BoutSide.Partner);

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
                _toPartner.AddRange(Host.TakeOutgoing().Select(outgoing => (HostNow + OneWaySeconds, outgoing.Message)));
                _toHost.AddRange(Partner.TakeOutgoing().Select(outgoing => (HostNow + OneWaySeconds, outgoing.Message)));
                HostLog.AddRange(Host.TakeLog());
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
