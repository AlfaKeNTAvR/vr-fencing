// Each headset's own view of the blades' contact, sent with its hand (PER-145): 6 bytes that come back the same, turned
// into the receiver's frame, compared with the receiver's own view and logged where they disagree for long enough; and
// the partner's hand and body as the Host's replay judge reads them, interpolated only, never past what arrived.
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Partner;
using VrFencing.Session;
using VrFencing.Settings;

namespace VrFencing.Tests.Partner
{
    public class ContactReportTests
    {
        private const float Tolerance = 1.0f / 127.0f;

        private static readonly StripExtent Strip = new StripExtent(8.0f, -1.0f);

        [Test]
        public void TheReportComesBackWithTheHandInSixBytes()
        {
            var plain = PartnerTestData.Hand(3.0, new Vector3(0.1f, 1.0f, 0.4f), Vector3.Zero);
            var report = new BladeContactReport(true, 0.0234f, Vector3.Normalize(new Vector3(0.6f, -0.8f, 0.0f)), true, false);
            var withReport = new HandSample(plain.SenderTimeSeconds, plain.Controller, plain.Velocity, plain.AngularVelocity, plain.Hand,
                plain.Mount, plain.Angles, plain.SetSagittaMeters, plain.Flex, report);

            var encoded = PartnerMessages.EncodeHand(withReport);
            var decoded = PartnerMessages.DecodeHand(encoded).Contact.Value;

            Assert.That(encoded.Length, Is.EqualTo(PartnerMessages.EncodeHand(plain).Length), "Always sent: zeros with no report.");
            Assert.That(PartnerMessages.DecodeHand(PartnerMessages.EncodeHand(plain)).Contact, Is.Null);
            Assert.That(decoded.IsInContact, Is.True);
            Assert.That(decoded.PressMeters, Is.EqualTo(0.0234f).Within(BladeContactReport.PressMetersPerUnit));
            Assert.That(Vector3.Distance(decoded.Side, report.Side), Is.LessThan(2.0f * Tolerance));
            Assert.That(decoded.IsSenderSliding, Is.True);
            Assert.That(decoded.IsReceiverSliding, Is.False);
            Assert.That(PartnerMessages.FormatVersion, Is.EqualTo(4));
        }

        [Test]
        public void TheReceiverReadsTheReportTurnedIntoItsFrame()
        {
            var link = new OpponentLink();
            link.SetMyStrip(Strip);
            link.Receive(PartnerMessages.EncodeInfo(new FencerInfo(Strip, 1.7f, Hand.Right)), 1.0);
            var hand = PartnerTestData.Hand(10.0, new Vector3(0.2f, 1.0f, 0.5f), Vector3.Zero);
            var report = new BladeContactReport(true, 0.01f, new Vector3(1.0f, 0.0f, 0.0f), false, true);
            link.Receive(PartnerMessages.EncodeHand(new HandSample(hand.SenderTimeSeconds, hand.Controller, hand.Velocity, hand.AngularVelocity,
                hand.Hand, hand.Mount, hand.Angles, hand.SetSagittaMeters, hand.Flex, report)), 1.0);

            var turned = link.GetPartnerContactReport(1.1).Value;

            Assert.That(turned.Side.X, Is.EqualTo(-1.0f).Within(Tolerance), "The bout Strip's half turn: their +x is my -x.");
            Assert.That(turned.IsReceiverSliding, Is.True);
            Assert.That(link.GetPartnerContactReport(1.0 + HandStream.StaleSeconds + 0.1), Is.Null, "A lost hand reports nothing.");
        }

        [Test]
        public void ViewsThatAgreeLogNothingAndALongDisagreementIsLoggedOnceItEnds()
        {
            var agreement = new ContactViewAgreement();
            var mySide = new Vector3(0.0f, 1.0f, 0.0f);

            // Their side runs from my blade to theirs: the opposite way of mine when we agree.
            var agreeing = new BladeContactReport(true, 0.01f, -mySide, false, false);
            for (var frame = 0; frame < 30; frame++)
            {
                agreement.Step(frame / 72.0, true, mySide, agreeing);
            }

            Assert.That(agreement.TakeLog(), Is.Empty);

            // A frame's flicker is not logged; ten frames pressing only here are.
            agreement.Step(31 / 72.0, true, mySide, new BladeContactReport(false, 0.0f, Vector3.Zero, false, false));
            agreement.Step(32 / 72.0, true, mySide, agreeing);
            Assert.That(agreement.TakeLog(), Is.Empty);
            for (var frame = 40; frame < 50; frame++)
            {
                agreement.Step(frame / 72.0, true, mySide, new BladeContactReport(false, 0.0f, Vector3.Zero, false, false));
            }

            agreement.Step(50 / 72.0, true, mySide, agreeing);
            var log = agreement.TakeLog();
            Assert.That(log.Count, Is.EqualTo(1));
            Assert.That(log[0], Does.StartWith("[Blade] Views disagreed for 139 ms: the blades pressed here, not in the partner's view"));

            Assert.That(ContactViewAgreement.Compare(true, mySide, new BladeContactReport(true, 0.01f, mySide, false, false)),
                Is.EqualTo(ContactDisagreement.OppositeSides));
            Assert.That(ContactViewAgreement.Compare(false, Vector3.Zero, agreeing), Is.EqualTo(ContactDisagreement.PressingOnlyThere));
        }

        [Test]
        public void TheReplayReadsThePartnersHandOnlyWithinWhatArrived()
        {
            var link = new OpponentLink();
            link.SetMyStrip(Strip);
            link.Receive(PartnerMessages.EncodeInfo(new FencerInfo(Strip, 1.7f, Hand.Right)), 1.0);
            link.Receive(PartnerMessages.EncodeHand(PartnerTestData.Hand(10.0, new Vector3(0.0f, 1.0f, 0.5f), Vector3.Zero)), 1.0);
            link.Receive(PartnerMessages.EncodeHand(PartnerTestData.Hand(10.1, new Vector3(0.2f, 1.0f, 0.5f), Vector3.Zero)), 1.1);

            Assert.That(link.NewestHandSenderSeconds, Is.EqualTo(10.1));
            Assert.That(link.TryGetPartnerBladeAt(9.9, out _), Is.False, "Before the oldest sample.");
            Assert.That(link.TryGetPartnerBladeAt(10.2, out _), Is.False, "Not arrived yet: never predicted.");
            Assert.That(link.TryGetPartnerBladeAt(10.05, out var blade), Is.True);
            Assert.That(blade.Chord.GuardPosition.Z, Is.GreaterThan(4.0f), "In my Strip frame, on the far side.");
            Assert.That(link.GetPartnerBodyAt(10.05), Is.Null, "No body came.");

            link.Receive(PartnerMessages.EncodeBody(new BodySample(10.0, PartnerTestData.Body(VrFencing.Session.BodyTrackingFidelity.High, Vector3.Zero))), 1.1);
            Assert.That(link.GetPartnerBodyAt(10.05).Value.HeightMeters, Is.EqualTo(1.7f));
        }

        [Test]
        public void TheReplayLetsATouchStandWhenItHasNothingToReplay()
        {
            var replay = new ParryReplay();
            var claim = new TouchClaim(1, BoutSide.Partner, VrFencing.Dummy.TargetZone.Body, 5.0, false);

            Assert.That(replay.Rule(claim), Is.EqualTo(ParryRuling.Touch), "No partner hand or no clock: nothing to wait for.");
        }
    }
}
