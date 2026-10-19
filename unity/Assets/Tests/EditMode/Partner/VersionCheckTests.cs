// Join is refused across different app versions (PER-80), and the refusal names both.
using System.Text;
using NUnit.Framework;
using VrFencing.Partner;

namespace VrFencing.Tests.Partner
{
    public class VersionCheckTests
    {
        [Test]
        public void TheSameVersionIsAccepted()
        {
            var verdict = VersionCheck.Judge("1.0", VersionCheck.EncodeRequest("1.0"));

            Assert.That(verdict.IsAccepted, Is.True);
            Assert.That(verdict.Reason, Is.Empty);
        }

        [Test]
        public void ADifferentVersionIsRefusedNamingBoth()
        {
            var verdict = VersionCheck.Judge("1.1", VersionCheck.EncodeRequest("1.0"));

            Assert.That(verdict.IsAccepted, Is.False);
            Assert.That(verdict.HostVersion, Is.EqualTo("1.1"));
            Assert.That(verdict.PartnerVersion, Is.EqualTo("1.0"));
            Assert.That(verdict.Reason, Does.Contain("host 1.1").And.Contain("partner 1.0"));
        }

        [Test]
        public void VersionsDifferingOnlyInCaseAreDifferent()
        {
            Assert.That(VersionCheck.Judge("1.0-a", VersionCheck.EncodeRequest("1.0-A")).IsAccepted, Is.False);
        }

        [Test]
        public void ARequestWithoutAVersionIsRefusedAsUnknown()
        {
            var empty = VersionCheck.Judge("1.0", null);
            var foreign = VersionCheck.Judge("1.0", Encoding.UTF8.GetBytes("1.0"));

            Assert.That(empty.IsAccepted, Is.False);
            Assert.That(empty.Reason, Does.Contain("partner unknown"));
            Assert.That(foreign.IsAccepted, Is.False, "A payload without this app's prefix is not a version.");
        }

        [Test]
        public void ARequestThatIsNotTextReadsAsUnknown()
        {
            Assert.That(VersionCheck.DecodeRequest(new byte[] { 0xff, 0xfe, 0x00 }), Is.Empty);
        }

        [Test]
        public void ARefusalIsToldFromTheNetworksOwnReasons()
        {
            Assert.That(VersionCheck.IsRefusal(VersionCheck.Judge("1.1", VersionCheck.EncodeRequest("1.0")).Reason), Is.True);
            Assert.That(VersionCheck.IsRefusal("[Disconnect Event][Client-0][TransportClientId-0][TransportShutdown] The transport was shutdown."), Is.False);
            Assert.That(VersionCheck.IsRefusal(string.Empty), Is.False);
            Assert.That(VersionCheck.IsRefusal(null), Is.False);
        }

        [Test]
        public void TheRequestRoundTrips()
        {
            Assert.That(VersionCheck.DecodeRequest(VersionCheck.EncodeRequest("2.3.1 (abc)")), Is.EqualTo("2.3.1 (abc)"));
        }

        [Test]
        public void AnEmptyVersionCannotBeSent()
        {
            Assert.Throws<System.ArgumentException>(() => VersionCheck.EncodeRequest(string.Empty));
        }
    }
}
