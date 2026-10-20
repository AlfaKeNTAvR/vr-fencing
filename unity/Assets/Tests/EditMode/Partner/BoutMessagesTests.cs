// The bytes of the judge's messages (PER-82): each comes back the same, and a broken or foreign one is refused.
using System.IO;
using System.Linq;
using NUnit.Framework;
using VrFencing.Dummy;
using VrFencing.Partner;
using VrFencing.Settings;

namespace VrFencing.Tests.Partner
{
    public class BoutMessagesTests
    {
        [Test]
        public void APingAndItsPongComeBackTheSame()
        {
            Assert.That(BoutMessages.DecodePing(BoutMessages.EncodePing(12.345)), Is.EqualTo(12.345));

            var pong = BoutMessages.DecodePong(BoutMessages.EncodePong(12.345, 678.9));

            Assert.That(pong.EchoedSenderSeconds, Is.EqualTo(12.345));
            Assert.That(pong.ReplierSeconds, Is.EqualTo(678.9));
        }

        [Test]
        public void AClaimComesBackTheSame()
        {
            var claim = new TouchClaim(7, BoutSide.Partner, TargetZone.Forearm, 1234.5678, isOffStrip: true);

            var decoded = BoutMessages.DecodeClaim(BoutMessages.EncodeClaim(claim));

            Assert.That(decoded.Sequence, Is.EqualTo(7));
            Assert.That(decoded.Attacker, Is.EqualTo(BoutSide.Partner));
            Assert.That(decoded.Zone, Is.EqualTo(TargetZone.Forearm));
            Assert.That(decoded.ContactHostSeconds, Is.EqualTo(1234.5678));
            Assert.That(decoded.IsOffStrip, Is.True);
        }

        [Test]
        public void ALightComesBackTheSameWithAMissingZone()
        {
            var decoded = BoutMessages.DecodeLight(BoutMessages.EncodeLight(new BoutLight(3, LightKind.HostTouch, TargetZone.Body, null)));

            Assert.That(decoded.Sequence, Is.EqualTo(3));
            Assert.That(decoded.Kind, Is.EqualTo(LightKind.HostTouch));
            Assert.That(decoded.HostZone, Is.EqualTo(TargetZone.Body));
            Assert.That(decoded.PartnerZone, Is.Null);
        }

        [Test]
        public void TheRulesComeBackTheSameWithTheWindowKeptInRange()
        {
            var decoded = BoutMessages.DecodeRules(BoutMessages.EncodeRules(new BoutRules(TouchPressure.OnContact, 0.1)));
            Assert.That(decoded.Pressure, Is.EqualTo(TouchPressure.OnContact));
            Assert.That(decoded.DoubleWindowSeconds, Is.EqualTo(0.1));

            var tooLong = BoutMessages.DecodeRules(BoutMessages.EncodeRules(new BoutRules(TouchPressure.Realistic, 3.0)));
            Assert.That(tooLong.DoubleWindowSeconds, Is.EqualTo(BoutJudge.MaxDoubleWindowSeconds));
        }

        [Test]
        public void AMessageOfAnotherKindOrFormatIsRefused()
        {
            var ping = BoutMessages.EncodePing(1.0);
            Assert.Throws<InvalidDataException>(() => BoutMessages.DecodeClaim(ping));

            ping[1] = BoutMessages.FormatVersion + 1;
            Assert.That(BoutMessages.ReadKind(ping), Is.EqualTo(0));
            Assert.That(BoutMessages.ReadKind(null), Is.EqualTo(0));
        }

        [Test]
        public void ACutOrPaddedMessageIsRefused()
        {
            var claim = BoutMessages.EncodeClaim(new TouchClaim(1, BoutSide.Host, TargetZone.Body, 1.0, false));

            Assert.Throws<InvalidDataException>(() => BoutMessages.DecodeClaim(claim.Take(claim.Length - 3).ToArray()));
            Assert.Throws<InvalidDataException>(() => BoutMessages.DecodeClaim(Pad(claim)));
        }

        [Test]
        public void AnUnknownZoneOrSideIsRefused()
        {
            var claim = BoutMessages.EncodeClaim(new TouchClaim(1, BoutSide.Host, TargetZone.Body, 1.0, false));

            // Kind, version, a 4-byte sequence, then the side and the zone.
            var badSide = (byte[])claim.Clone();
            badSide[6] = 9;
            var badZone = (byte[])claim.Clone();
            badZone[7] = 99;

            Assert.Throws<InvalidDataException>(() => BoutMessages.DecodeClaim(badSide));
            Assert.Throws<InvalidDataException>(() => BoutMessages.DecodeClaim(badZone));
        }

        private static byte[] Pad(byte[] message)
        {
            var padded = new byte[message.Length + 1];
            message.CopyTo(padded, 0);
            return padded;
        }
    }
}
