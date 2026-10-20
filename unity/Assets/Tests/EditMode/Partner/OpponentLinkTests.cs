// The receiving end of the partner streams and the tracked body (PER-81): the partner's poses arrive in their Strip
// frame and are drawn on the far side of the bout Strip facing this Fencer, the body's sword hand stays on the drawn
// hilt, and a tracked body reads into the rig's joints and the capsules a blade bends against.
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Dummy;
using VrFencing.Partner;
using VrFencing.Session;
using VrFencing.Settings;

namespace VrFencing.Tests.Partner
{
    public class OpponentLinkTests
    {
        private const float Tolerance = 1e-3f;

        private static readonly StripExtent MyStrip = new StripExtent(8.0f, -1.0f);
        private static readonly StripExtent PartnerStrip = new StripExtent(8.0f, -1.0f);

        [Test]
        public void NothingIsDrawnBeforeThePartnersInfoAndStreams()
        {
            var link = new OpponentLink();
            Assert.That(link.Sample(1.0), Is.Null);

            link.Receive(PartnerMessages.EncodeInfo(new FencerInfo(PartnerStrip, 1.7f, Hand.Right)), 1.0);
            Assert.That(link.Sample(1.0), Is.Null);
        }

        [Test]
        public void ThePartnerStandingOnTheirLineIsDrawnOnTheFarSideFacingMe()
        {
            var link = ConnectedLink();
            var body = PartnerTestData.Body(BodyTrackingFidelity.High, Vector3.Zero);
            link.Receive(PartnerMessages.EncodeBody(new BodySample(10.0, body)), 1.0);
            link.Receive(PartnerMessages.EncodeHand(PartnerTestData.Hand(10.0, new Vector3(0.2f, 1.0f, 0.5f), Vector3.Zero)), 1.0);

            var view = link.Sample(1.2);

            // The partner's own line (z = 0 on their Strip) sits 8 m from mine (rear ends at -1, bout centre at 3).
            var hip = view.Body.Get(SkeletonJoint.HipCenter).Value;
            Assert.That(hip.Z, Is.EqualTo(6.0f - body.Get(SkeletonJoint.HipCenter).Value.Z).Within(Tolerance));
            Assert.That(view.Body.Get(SkeletonJoint.SwordShoulder).Value.X, Is.EqualTo(-body.Get(SkeletonJoint.SwordShoulder).Value.X).Within(Tolerance));
            Assert.That(Vector3.Transform(Vector3.UnitZ, view.Controller.Value.Rotation).Z, Is.LessThan(0.0f));
            Assert.That(view.Grip.Value.Mount, Is.EqualTo(BladeMount.French));
            Assert.That(view.IsBodyStale, Is.False);
            Assert.That(view.Info.HeightMeters, Is.EqualTo(1.7f));
        }

        [Test]
        public void TheBodysSwordHandFollowsTheDrawnHilt()
        {
            var link = ConnectedLink();
            var velocity = new Vector3(0.0f, 0.0f, 2.0f);
            for (var step = 0; step <= 30; step++)
            {
                var time = 10.0 + (step / 72.0);
                var offset = velocity * (float)(time - 10.0);
                link.Receive(PartnerMessages.EncodeHand(PartnerTestData.Hand(time, new Vector3(0.25f, 1.1f, 0.4f) + offset, velocity)), 1.0 + (time - 10.0));
                if (step % 2 == 0)
                {
                    var body = PartnerTestData.Body(BodyTrackingFidelity.High, offset);
                    link.Receive(PartnerMessages.EncodeBody(new BodySample(time, body)), 1.0 + (time - 10.0));
                }
            }

            var view = link.Sample(1.0 + (30 / 72.0));
            var hiltToWrist = view.Body.Get(SkeletonJoint.SwordWrist).Value - view.Controller.Value.Position;

            // Without the carry the wrist would trail the hilt by the body's extra 0.1 s plus the hand's prediction: 21 cm at 2 m/s.
            var restWrist = PartnerTestData.Body(BodyTrackingFidelity.High, Vector3.Zero).Get(SkeletonJoint.SwordWrist).Value;
            var restHiltToWrist = Vector3.Distance(restWrist, new Vector3(0.25f, 1.1f, 0.4f));
            Assert.That(hiltToWrist.Length(), Is.EqualTo(restHiltToWrist).Within(0.01f));
        }

        [Test]
        public void ALostBodyIsStaleAndALostHandHasNoController()
        {
            var link = ConnectedLink();
            link.Receive(PartnerMessages.EncodeBody(new BodySample(10.0, PartnerTestData.Body(BodyTrackingFidelity.High, Vector3.Zero))), 1.0);
            link.Receive(PartnerMessages.EncodeHand(PartnerTestData.Hand(10.0, Vector3.Zero, Vector3.Zero)), 1.0);

            var view = link.Sample(2.0);

            Assert.That(view.IsBodyStale, Is.True);
            Assert.That(view.Controller, Is.Null);
            Assert.That(view.Body, Is.Not.Null);
        }

        [Test]
        public void GarbageIsCountedAndChangesNothing()
        {
            var link = ConnectedLink();

            Assert.That(link.Receive(new byte[] { 2, 1, 7 }, 1.0), Is.False);
            Assert.That(link.Receive(new byte[0], 1.0), Is.False);
            Assert.That(link.RejectedMessages, Is.EqualTo(2));
            Assert.That(link.Sample(1.0), Is.Null);
        }

        [Test]
        public void ClearForgetsThePartner()
        {
            var link = ConnectedLink();
            link.Receive(PartnerMessages.EncodeHand(PartnerTestData.Hand(10.0, Vector3.Zero, Vector3.Zero)), 1.0);

            link.Clear();

            Assert.That(link.Partner, Is.Null);
            Assert.That(link.Sample(1.0), Is.Null);
        }

        [Test]
        public void TheBoutUsesTheShorterStrip()
        {
            var link = new OpponentLink();
            link.SetMyStrip(new StripExtent(5.0f, -0.8f));
            link.Receive(PartnerMessages.EncodeInfo(new FencerInfo(new StripExtent(9.0f, -1.2f), 1.8f, Hand.Left)), 0.0);

            Assert.That(link.Bout.LengthMeters, Is.EqualTo(5.0f));
            Assert.That(link.Bout.RearEndZ, Is.EqualTo(-0.8f));
        }

        [Test]
        public void ARightHandersFullBodyReadsSwordSideRight()
        {
            var fullBody = FullBodyWithEachJointAtItsIndex();

            var body = FullBodyJoints.Read(fullBody, Quaternion.Identity, Hand.Right, 1.7f, BodyTrackingFidelity.High);

            Assert.That(body.Get(SkeletonJoint.SwordWrist).Value.X, Is.EqualTo(FullBodyJoints.RightHandWrist));
            Assert.That(body.Get(SkeletonJoint.BackHand).Value.X, Is.EqualTo(FullBodyJoints.LeftHandWrist));
            Assert.That(body.Get(SkeletonJoint.FrontToe).Value.X, Is.EqualTo(FullBodyJoints.RightFootBall));
            Assert.That(body.BackHandEnd.Value.X, Is.EqualTo(FullBodyJoints.LeftHandMiddleProximal));
            Assert.That(body.Get(SkeletonJoint.HipCenter).Value.X, Is.EqualTo((FullBodyJoints.LeftUpperLeg + FullBodyJoints.RightUpperLeg) * 0.5f));
            Assert.That(body.Get(SkeletonJoint.HeadCenter).Value.Y, Is.EqualTo(FullBodyJoints.HeadCenterAboveHeadJointPerHeight * 1.7f).Within(1e-5f));
        }

        [Test]
        public void ALeftHandersFullBodyReadsSwordSideLeft()
        {
            var body = FullBodyJoints.Read(FullBodyWithEachJointAtItsIndex(), null, Hand.Left, 1.7f, BodyTrackingFidelity.Low);

            Assert.That(body.Get(SkeletonJoint.SwordElbow).Value.X, Is.EqualTo(FullBodyJoints.LeftArmLower));
            Assert.That(body.Get(SkeletonJoint.FrontKnee).Value.X, Is.EqualTo(FullBodyJoints.LeftLowerLeg));
            Assert.That(body.Fidelity, Is.EqualTo(BodyTrackingFidelity.Low));
        }

        [Test]
        public void NoBodyFromTheTrackerReadsNoJoints()
        {
            var body = FullBodyJoints.Read(FullBodyWithEachJointAtItsIndex(), Quaternion.Identity, Hand.Right, 1.7f, null);

            Assert.That(Enumerable.Range(0, FencerBody.JointCount).All(index => !body.Get(index).HasValue), Is.True);
            Assert.That(body.HeadRotation.HasValue, Is.True);
        }

        [Test]
        public void TheBodyAlwaysGivesTheSameNumberOfCapsules()
        {
            var capsules = new List<Capsule>();
            FencerBodyShape.GetCapsules(PartnerTestData.Body(BodyTrackingFidelity.High, Vector3.Zero), 1.7f, capsules);
            Assert.That(capsules, Has.Count.EqualTo(FencerBodyShape.CapsuleCount));
            Assert.That(capsules.All(capsule => capsule.Start.Y > -1.0f), Is.True);

            FencerBodyShape.GetCapsules(null, 1.7f, capsules);
            Assert.That(capsules, Has.Count.EqualTo(FencerBodyShape.CapsuleCount));
            Assert.That(capsules.All(capsule => capsule.Start == FencerBodyShape.ParkedPosition), Is.True);
        }

        [Test]
        public void AMissingPartIsParkedUnderTheFloor()
        {
            var joints = new Vector3?[FencerBody.JointCount];
            joints[(int)SkeletonJoint.HeadCenter] = new Vector3(0.0f, 1.6f, 0.0f);
            var capsules = new List<Capsule>();

            FencerBodyShape.GetCapsules(new FencerBody(joints, null, BodyTrackingFidelity.High), 1.7f, capsules);

            Assert.That(capsules[0].Start, Is.EqualTo(new Vector3(0.0f, 1.6f, 0.0f)));
            Assert.That(capsules.Skip(1).All(capsule => capsule.Start == FencerBodyShape.ParkedPosition), Is.True);
        }

        private static OpponentLink ConnectedLink()
        {
            var link = new OpponentLink();
            link.SetMyStrip(MyStrip);
            link.Receive(PartnerMessages.EncodeInfo(new FencerInfo(PartnerStrip, 1.7f, Hand.Right)), 1.0);
            return link;
        }

        /// <summary>A Full Body skeleton whose joint i sits at x = i, so a test reads back which joint went where.</summary>
        private static TrackedPose?[] FullBodyWithEachJointAtItsIndex()
        {
            return Enumerable.Range(0, FullBodyJoints.Count)
                .Select(index => (TrackedPose?)new TrackedPose(new Vector3(index, 0.0f, 0.0f), Quaternion.Identity))
                .ToArray();
        }
    }
}
