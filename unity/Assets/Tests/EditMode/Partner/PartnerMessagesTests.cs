// The bytes of the partner streams (PER-81): what goes out comes back the same, small enough for one packet, and a
// message of another format or a broken one is refused instead of drawn.
using System.IO;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Dummy;
using VrFencing.Partner;
using VrFencing.Session;
using VrFencing.Settings;

namespace VrFencing.Tests.Partner
{
    public class PartnerMessagesTests
    {
        private const float Tolerance = 1e-5f;

        // Under one packet with room to spare (Relay carries about 1,400 bytes), and the decided budget: the body at
        // 30 per second and the hand at 72 per second stay under about 6 KB/s and 2 KB/s plus headers (PER-59).
        private const int MaxBodyBytes = 200;
        private const int MaxHandBytes = 100;

        [Test]
        public void TheInfoComesBackTheSame()
        {
            var info = new FencerInfo(new StripExtent(7.5f, -1.1f), 1.72f, Hand.Left);

            var decoded = PartnerMessages.DecodeInfo(PartnerMessages.EncodeInfo(info));

            Assert.That(decoded.Strip.LengthMeters, Is.EqualTo(7.5f));
            Assert.That(decoded.Strip.RearEndZ, Is.EqualTo(-1.1f));
            Assert.That(decoded.HeightMeters, Is.EqualTo(1.72f));
            Assert.That(decoded.Hand, Is.EqualTo(Hand.Left));
        }

        [Test]
        public void TheHandComesBackTheSameWithItsGrip()
        {
            var sample = PartnerTestData.Hand(12.5, new Vector3(0.3f, 1.1f, 0.6f), new Vector3(1.0f, 0.0f, 2.0f));

            var encoded = PartnerMessages.EncodeHand(sample);
            var decoded = PartnerMessages.DecodeHand(encoded);

            Assert.That(encoded.Length, Is.LessThanOrEqualTo(MaxHandBytes));
            Assert.That(decoded.SenderTimeSeconds, Is.EqualTo(12.5));
            Assert.That(Vector3.Distance(decoded.Controller.Position, sample.Controller.Position), Is.LessThan(Tolerance));
            Assert.That(Quaternion.Dot(decoded.Controller.Rotation, sample.Controller.Rotation), Is.EqualTo(1.0f).Within(Tolerance));
            Assert.That(decoded.Velocity, Is.EqualTo(sample.Velocity));
            Assert.That(decoded.AngularVelocity, Is.EqualTo(sample.AngularVelocity));
            Assert.That(decoded.Mount, Is.EqualTo(BladeMount.French));
            Assert.That(decoded.Angles.PitchDegrees, Is.EqualTo(-13.0f));
            Assert.That(decoded.Angles.YawDegrees, Is.EqualTo(-4.0f));
            Assert.That(decoded.SetSagittaMeters, Is.EqualTo(0.008f));
            Assert.That(decoded.Flex, Is.EqualTo(BladeFlexSoftness.Soft));
        }

        [Test]
        public void TheGripBecomesBladeSettingsWithItsAnglesInUse()
        {
            var sample = PartnerTestData.Hand(0.0, Vector3.Zero, Vector3.Zero);

            var settings = sample.ToBladeSettings();

            Assert.That(settings.Mount, Is.EqualTo(BladeMount.French));
            Assert.That(GripGeometry.GetActiveAngles(settings).PitchDegrees, Is.EqualTo(-13.0f));
            Assert.That(GripGeometry.GetActiveAngles(settings).YawDegrees, Is.EqualTo(-4.0f));
            Assert.That(settings.SetSagittaMeters, Is.EqualTo(0.008f));
            Assert.That(settings.Flex, Is.EqualTo(BladeFlexSoftness.Soft));
        }

        [Test]
        public void TheBodyComesBackToTheMillimeter()
        {
            var body = PartnerTestData.Body(BodyTrackingFidelity.High, new Vector3(0.0f, 0.0f, 0.5f));

            var encoded = PartnerMessages.EncodeBody(new BodySample(3.25, body));
            var decoded = PartnerMessages.DecodeBody(encoded);

            Assert.That(encoded.Length, Is.LessThanOrEqualTo(MaxBodyBytes));
            Assert.That(decoded.SenderTimeSeconds, Is.EqualTo(3.25));
            Assert.That(decoded.Body.Fidelity, Is.EqualTo(BodyTrackingFidelity.High));
            for (var index = 0; index < FencerBody.JointCount; index++)
            {
                Assert.That(Vector3.Distance(decoded.Body.Get(index).Value, body.Get(index).Value), Is.LessThanOrEqualTo(0.0009f), $"joint {index}");
            }

            Assert.That(Quaternion.Dot(decoded.Body.HeadRotation.Value, body.HeadRotation.Value), Is.EqualTo(1.0f).Within(Tolerance));
        }

        [Test]
        public void MissingJointsAndNoBodyStayMissing()
        {
            var joints = new Vector3?[FencerBody.JointCount];
            joints[(int)SkeletonJoint.SwordWrist] = new Vector3(0.2f, 1.0f, 0.4f);
            var body = new FencerBody(joints, null, null);

            var decoded = PartnerMessages.DecodeBody(PartnerMessages.EncodeBody(new BodySample(1.0, body))).Body;

            Assert.That(decoded.Fidelity, Is.Null);
            Assert.That(decoded.HeadRotation, Is.Null);
            Assert.That(decoded.Get(SkeletonJoint.SwordWrist).HasValue, Is.True);
            Assert.That(Enumerable.Range(0, FencerBody.JointCount).Count(index => decoded.Get(index).HasValue), Is.EqualTo(1));
        }

        [Test]
        public void AMessageOfAnotherFormatIsNotRead()
        {
            var message = PartnerMessages.EncodeHand(PartnerTestData.Hand(1.0, Vector3.Zero, Vector3.Zero));
            message[1] = PartnerMessages.FormatVersion + 1;

            Assert.That(PartnerMessages.ReadKind(message), Is.EqualTo(0));
            Assert.Throws<InvalidDataException>(() => PartnerMessages.DecodeHand(message));
        }

        [Test]
        public void ACutShortMessageIsRefused()
        {
            var message = PartnerMessages.EncodeBody(new BodySample(1.0, PartnerTestData.Body(BodyTrackingFidelity.Low, Vector3.Zero)));

            Assert.Throws<InvalidDataException>(() => PartnerMessages.DecodeBody(message.Take(message.Length - 3).ToArray()));
        }

        [Test]
        public void AHandThatIsNotANumberIsRefused()
        {
            var sample = PartnerTestData.Hand(1.0, new Vector3(float.NaN, 1.0f, 0.0f), Vector3.Zero);

            Assert.Throws<InvalidDataException>(() => PartnerMessages.DecodeHand(PartnerMessages.EncodeHand(sample)));
        }

        [Test]
        public void TheWrongKindIsRefused()
        {
            var info = PartnerMessages.EncodeInfo(new FencerInfo(StripExtent.Unplaced, 1.7f, Hand.Right));

            Assert.Throws<InvalidDataException>(() => PartnerMessages.DecodeHand(info));
        }
    }
}
