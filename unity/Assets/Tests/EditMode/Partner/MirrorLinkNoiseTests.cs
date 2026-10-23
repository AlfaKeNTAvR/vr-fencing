// A rougher pretend link (PER-145): "Mirror me" adds jitter and loss to its delay, cycled by a button beside the delay;
// jitter never reorders messages, loss only drops the ones sent unreliable, and the draws repeat. The PC Editor's
// extra delay holds every message it sends or receives that much longer, each way.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VrFencing.Partner;

namespace VrFencing.Tests.Partner
{
    public class MirrorLinkNoiseTests
    {
        [Test]
        public void TheNoiseButtonCyclesCleanJitterLossAndBothWhileMirroring()
        {
            var session = new PartnerSession();
            Assert.That(session.CycleMirrorNoise(), Is.False, "Only while mirroring.");
            session.BeginMirror();

            var seen = new List<string>();
            for (var press = 0; press < 4; press++)
            {
                session.CycleMirrorNoise();
                seen.Add(MirrorLinkNoise.FormatLabel(session.MirrorNoise));
            }

            Assert.That(seen, Is.EqualTo(new[] { "Jitter 20 ms", "Loss 2%", "Jitter + loss", "Link clean" }));
            Assert.That(MirrorLinkNoise.GetJitterSeconds(MirrorNoise.JitterAndLoss), Is.EqualTo(0.02));
            Assert.That(MirrorLinkNoise.GetLossFraction(MirrorNoise.Jitter), Is.EqualTo(0.0));
        }

        [Test]
        public void JitterDelaysMessagesUpToItsSizeButNeverReordersThem()
        {
            var loopback = new MirrorLoopback(0.1);
            loopback.SetNoise(0.02, 0.0);
            var arrivals = new List<(byte Message, double Seconds)>();
            for (var index = 0; index < 200; index++)
            {
                var now = index / 72.0;
                loopback.Send(new[] { (byte)index }, now, mayBeLost: true);
                foreach (var message in loopback.TakeDue(now))
                {
                    arrivals.Add((message[0], now));
                }
            }

            Assert.That(arrivals.Select(arrival => (int)arrival.Message), Is.Ordered, "In the order sent.");
            foreach (var (message, seconds) in arrivals)
            {
                var sent = message / 72.0;
                Assert.That(seconds - sent, Is.GreaterThanOrEqualTo(0.1 - 1e-9));
                Assert.That(seconds - sent, Is.LessThan(0.1 + 0.02 + (2.0 / 72.0)), "Within the jitter, give or take a frame of bunching.");
            }

            Assert.That(arrivals.Select(arrival => arrival.Seconds - (arrival.Message / 72.0)).Distinct().Count(), Is.GreaterThan(5), "The delays vary.");
        }

        [Test]
        public void LossDropsOnlyMessagesThatMayBeLostAndRepeats()
        {
            int CountArrivals(bool mayBeLost)
            {
                var loopback = new MirrorLoopback(0.0);
                loopback.SetNoise(0.0, 0.5);
                for (var index = 0; index < 400; index++)
                {
                    loopback.Send(new byte[] { 1 }, 1.0, mayBeLost);
                }

                return loopback.TakeDue(1.0).Count;
            }

            Assert.That(CountArrivals(mayBeLost: false), Is.EqualTo(400), "Reliable messages always arrive.");
            var arrived = CountArrivals(mayBeLost: true);
            Assert.That(arrived, Is.InRange(150, 250));
            Assert.That(CountArrivals(mayBeLost: true), Is.EqualTo(arrived), "The same seed loses the same messages.");
        }

        [Test]
        public void TheEditorsExtraDelayHoldsBothWaysAndPassesStraightThroughAtZero()
        {
            var delay = new ExtraLinkDelay();
            Assert.That(delay.IsActive, Is.False);

            delay.SetDelay(0.2);
            delay.Send(new byte[] { 1 }, isReliable: true, 1.0);
            delay.Send(new byte[] { 2 }, isReliable: false, 1.0);
            delay.Receive(new byte[] { 3 }, 1.0);

            Assert.That(delay.TakeDueOutgoing(1.19), Is.Empty);
            Assert.That(delay.TakeDueIncoming(1.19), Is.Empty);
            var outgoing = delay.TakeDueOutgoing(1.2).ToArray();
            Assert.That(outgoing.Select(message => (message.Message[0], message.IsReliable)), Is.EqualTo(new[] { ((byte)1, true), ((byte)2, false) }));
            Assert.That(delay.TakeDueIncoming(1.2).Single()[0], Is.EqualTo(3));

            delay.SetDelay(0.0);
            Assert.That(delay.IsActive, Is.False, "Nothing on its way and no delay: messages go straight through.");
        }
    }
}
