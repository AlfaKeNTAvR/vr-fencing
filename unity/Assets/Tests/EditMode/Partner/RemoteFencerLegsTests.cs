// The receiver's legs for the remote Fencer (PER-112): built from the tracked hips down to the sender's feet with the
// Dummy's leg rig (bones keep their length, knees bend forward as in en garde, a lifted or pitched foot shows), unsure
// feet held where they were last sure, jumps stepped instead of teleported, and out-of-reach feet dragged in a lunge.
using System;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Dummy;
using VrFencing.Partner;
using VrFencing.Session;
using VrFencing.Settings;

namespace VrFencing.Tests.Partner
{
    public class RemoteFencerLegsTests
    {
        private const float Height = PartnerTestData.HeightMeters;
        private const double FrameSeconds = 1.0 / 72.0;
        private const float Tolerance = 1e-3f;

        [Test]
        public void TheAnklesStandOnTheSentFeetAndTheHipsStay()
        {
            var body = Body(Hand.Right, Sure(FootworkPhase.Standing, 0.0f, 0.0f));
            var rest = EnGarde(Hand.Right);

            var legs = new RemoteFencerLegs().Apply(body, Height, Hand.Right, 1.0);

            AssertOnSpot(legs, SkeletonJoint.FrontAnkle, rest.Front.Ankle);
            AssertOnSpot(legs, SkeletonJoint.BackAnkle, rest.Back.Ankle);
            Assert.That(legs.Get(SkeletonJoint.FrontHip), Is.EqualTo(body.Get(SkeletonJoint.FrontHip)));
            Assert.That(legs.Get(SkeletonJoint.HipCenter), Is.EqualTo(body.Get(SkeletonJoint.HipCenter)));
            Assert.That(legs.Footwork.HasValue, Is.True);
        }

        [Test]
        public void TheBonesKeepTheirLengthAndTheKneesBendForward()
        {
            // The sent feet a little wider than en garde, so the knees must bend differently.
            var body = Body(Hand.Right, Sure(FootworkPhase.Standing, 0.05f, -0.04f));

            var legs = new RemoteFencerLegs().Apply(body, Height, Hand.Right, 1.0);

            AssertLegShape(legs, Hand.Right);
        }

        [Test]
        public void ALeftHandersLegsAreMirroredWithKneesForward()
        {
            var body = Body(Hand.Left, Sure(FootworkPhase.Standing, 0.05f, 0.0f, Hand.Left));
            var rest = EnGarde(Hand.Left);

            var legs = new RemoteFencerLegs().Apply(body, Height, Hand.Left, 1.0);

            AssertOnSpot(legs, SkeletonJoint.FrontAnkle, rest.Front.Ankle + new Vector3(0.0f, 0.0f, 0.05f));
            AssertLegShape(legs, Hand.Left);
            Assert.That(legs.Get(SkeletonJoint.BackToe).Value.X, Is.GreaterThan(legs.Get(SkeletonJoint.BackAnkle).Value.X), "The back foot turns out to the left-hander's right.");
        }

        [Test]
        public void ALiftedToeUpFootShowsLiftedAndToeUp()
        {
            var rest = EnGarde(Hand.Right);
            var lifted = new FootPlacement(rest.Front.Ankle.X, rest.Front.Ankle.Z + 0.1f, 0.06f, 0.3f);
            var footwork = new FootworkState(FootworkPhase.Lunging, lifted, Spot(rest.Back.Ankle), 1.0f);

            var legs = new RemoteFencerLegs().Apply(Body(Hand.Right, footwork), Height, Hand.Right, 1.0);

            Assert.That(legs.Get(SkeletonJoint.FrontAnkle).Value.Y, Is.GreaterThanOrEqualTo(rest.Front.Ankle.Y + 0.06f - Tolerance));
            Assert.That(legs.Get(SkeletonJoint.FrontToe).Value.Y, Is.GreaterThan(legs.Get(SkeletonJoint.FrontAnkle).Value.Y), "Toe up.");
        }

        [Test]
        public void UnsureFeetStayWhereTheyWereLastSure()
        {
            var legs = new RemoteFencerLegs();
            var rest = EnGarde(Hand.Right);
            legs.Apply(Body(Hand.Right, Sure(FootworkPhase.Standing, 0.0f, 0.0f)), Height, Hand.Right, 1.0);

            // Unsure, the sender's feet wander 8 cm: not followed.
            FencerBody result = null;
            for (var frame = 1; frame <= 36; frame++)
            {
                var unsure = new FootworkState(FootworkPhase.Advancing, Spot(rest.Front.Ankle + new Vector3(0.0f, 0.0f, 0.08f)), Spot(rest.Back.Ankle), 0.3f);
                result = legs.Apply(Body(Hand.Right, unsure), Height, Hand.Right, 1.0 + (frame * FrameSeconds));
            }

            AssertOnSpot(result, SkeletonJoint.FrontAnkle, rest.Front.Ankle);
        }

        [Test]
        public void AHeldFootStepsAfterHipsThatLeftItFarBehind()
        {
            var legs = new RemoteFencerLegs();
            var rest = EnGarde(Hand.Right);
            legs.Apply(Body(Hand.Right, Sure(FootworkPhase.Standing, 0.0f, 0.0f)), Height, Hand.Right, 1.0);

            // No sure feet while the hips move 40 cm forward: the feet step after them, lifting on the way.
            var moved = new Vector3(0.0f, 0.0f, 0.4f);
            var maxLift = 0.0f;
            FencerBody result = null;
            for (var frame = 1; frame <= 72; frame++)
            {
                result = legs.Apply(Body(Hand.Right, null, moved), Height, Hand.Right, 1.0 + (frame * FrameSeconds));
                maxLift = Math.Max(maxLift, result.Get(SkeletonJoint.FrontAnkle).Value.Y - rest.Front.Ankle.Y);
            }

            Assert.That(maxLift, Is.GreaterThan(0.01f), "It steps, not slides.");
            AssertOnSpot(result, SkeletonJoint.FrontAnkle, rest.Front.Ankle + moved);
            AssertOnSpot(result, SkeletonJoint.BackAnkle, rest.Back.Ankle + moved);
        }

        [Test]
        public void ABodyWithoutFootworkStandsInEnGardeUnderItsHips()
        {
            var offset = new Vector3(0.3f, 0.0f, 1.2f);

            var legs = new RemoteFencerLegs().Apply(Body(Hand.Right, null, offset), Height, Hand.Right, 1.0);

            var rest = EnGarde(Hand.Right);
            AssertOnSpot(legs, SkeletonJoint.FrontAnkle, rest.Front.Ankle + offset);
            AssertOnSpot(legs, SkeletonJoint.BackAnkle, rest.Back.Ankle + offset);
        }

        [Test]
        public void AJumpingFootStepsThereInsteadOfTeleporting()
        {
            var legs = new RemoteFencerLegs();
            var rest = EnGarde(Hand.Right);
            legs.Apply(Body(Hand.Right, Sure(FootworkPhase.Standing, 0.0f, 0.0f)), Height, Hand.Right, 1.0);

            var jumped = Sure(FootworkPhase.Standing, 0.15f, 0.0f);
            var first = legs.Apply(Body(Hand.Right, jumped), Height, Hand.Right, 1.0 + FrameSeconds);
            Assert.That(first.Get(SkeletonJoint.FrontAnkle).Value.Z - rest.Front.Ankle.Z, Is.LessThan(0.02f), "Not there at once.");

            var middle = legs.Apply(Body(Hand.Right, jumped), Height, Hand.Right, 1.0 + (RemoteFootworkTuning.ReplantSeconds * 0.5));
            Assert.That(middle.Get(SkeletonJoint.FrontAnkle).Value.Y, Is.GreaterThan(rest.Front.Ankle.Y + 0.02f), "Lifted on the way.");

            var after = legs.Apply(Body(Hand.Right, jumped), Height, Hand.Right, 1.0 + RemoteFootworkTuning.ReplantSeconds + FrameSeconds);
            AssertOnSpot(after, SkeletonJoint.FrontAnkle, rest.Front.Ankle + new Vector3(0.0f, 0.0f, 0.15f));
        }

        [Test]
        public void InALungeAFootOutOfReachDragsAlongTheFloor()
        {
            var rest = EnGarde(Hand.Right);
            var farBack = Spot(rest.Back.Ankle - new Vector3(0.0f, 0.0f, 0.6f));
            var footwork = new FootworkState(FootworkPhase.Lunging, Spot(rest.Front.Ankle), farBack, 1.0f);
            var legs = new RemoteFencerLegs();
            legs.Apply(Body(Hand.Right, footwork), Height, Hand.Right, 1.0);

            var result = legs.Apply(Body(Hand.Right, footwork), Height, Hand.Right, 1.0 + RemoteFootworkTuning.ReplantSeconds + FrameSeconds);

            var ankle = result.Get(SkeletonJoint.BackAnkle).Value;
            var hip = result.Get(SkeletonJoint.BackHip).Value;
            var legLength = Vector3.Distance(rest.Back.Hip, rest.Back.Knee) + Vector3.Distance(rest.Back.Knee, rest.Back.Ankle);
            Assert.That(ankle.Y, Is.EqualTo(rest.Back.Ankle.Y).Within(Tolerance), "On the floor.");
            Assert.That(Vector3.Distance(hip, ankle), Is.LessThanOrEqualTo(legLength + Tolerance), "Within reach.");
            Assert.That(ankle.Z, Is.GreaterThan(farBack.Z), "Dragged toward the hips.");
        }

        [Test]
        public void ABodyWithoutHipsIsLeftAlone()
        {
            var joints = new Vector3?[FencerBody.JointCount];
            joints[(int)SkeletonJoint.HeadCenter] = new Vector3(0.0f, 1.6f, 0.0f);
            var body = new FencerBody(joints, null, BodyTrackingFidelity.High, Sure(FootworkPhase.Standing, 0.0f, 0.0f));

            Assert.That(new RemoteFencerLegs().Apply(body, Height, Hand.Right, 1.0), Is.SameAs(body));
        }

        private static DummyLegs EnGarde(Hand hand)
        {
            return DummyLegs.EnGarde(Height, hand);
        }

        private static FootPlacement Spot(Vector3 ankle)
        {
            return new FootPlacement(ankle.X, ankle.Z, 0.0f, 0.0f);
        }

        /// <summary>Sure feet in en garde, the front moved <paramref name="frontForward"/> and the back <paramref name="backForward"/> along the Strip.</summary>
        private static FootworkState Sure(FootworkPhase phase, float frontForward, float backForward, Hand hand = Hand.Right)
        {
            var rest = EnGarde(hand);
            return new FootworkState(
                phase,
                Spot(rest.Front.Ankle + new Vector3(0.0f, 0.0f, frontForward)),
                Spot(rest.Back.Ankle + new Vector3(0.0f, 0.0f, backForward)),
                1.0f);
        }

        /// <summary>A Fencer in the Dummy's en garde moved by <paramref name="offset"/>, with generated legs that are wrong on purpose.</summary>
        private static FencerBody Body(Hand hand, FootworkState? footwork, Vector3 offset = default)
        {
            var layout = DummyLayout.Create(Height, hand);
            var joints = new Vector3?[FencerBody.JointCount];
            foreach (SkeletonJoint joint in Enum.GetValues(typeof(SkeletonJoint)))
            {
                joints[(int)joint] = DummySkeleton.GetJointPosition(layout, joint) + offset;
            }

            // Generative Legs' symmetric crouch: both ankles under the hips.
            joints[(int)SkeletonJoint.FrontAnkle] = joints[(int)SkeletonJoint.HipCenter].Value - new Vector3(0.0f, 0.45f * Height, 0.0f);
            joints[(int)SkeletonJoint.BackAnkle] = joints[(int)SkeletonJoint.FrontAnkle];
            return new FencerBody(joints, Quaternion.Identity, BodyTrackingFidelity.High, footwork);
        }

        private static void AssertOnSpot(FencerBody body, SkeletonJoint ankle, Vector3 expected)
        {
            var actual = body.Get(ankle).Value;
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(Tolerance), $"{ankle} across");
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(Tolerance), $"{ankle} height");
            Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(Tolerance), $"{ankle} along");
        }

        private static void AssertLegShape(FencerBody legs, Hand hand)
        {
            var rest = EnGarde(hand);
            AssertLeg(legs, SkeletonJoint.FrontHip, SkeletonJoint.FrontKnee, SkeletonJoint.FrontAnkle, rest.Front);
            AssertLeg(legs, SkeletonJoint.BackHip, SkeletonJoint.BackKnee, SkeletonJoint.BackAnkle, rest.Back);
        }

        private static void AssertLeg(FencerBody legs, SkeletonJoint hipJoint, SkeletonJoint kneeJoint, SkeletonJoint ankleJoint, LegPose rest)
        {
            var hip = legs.Get(hipJoint).Value;
            var knee = legs.Get(kneeJoint).Value;
            var ankle = legs.Get(ankleJoint).Value;
            Assert.That(Vector3.Distance(hip, knee), Is.EqualTo(Vector3.Distance(rest.Hip, rest.Knee)).Within(Tolerance), $"{kneeJoint} thigh");
            Assert.That(Vector3.Distance(knee, ankle), Is.EqualTo(Vector3.Distance(rest.Knee, rest.Ankle)).Within(Tolerance), $"{kneeJoint} shin");

            // The knee is forward of the hip-to-ankle line.
            var along = Vector3.Normalize(ankle - hip);
            var bend = (knee - hip) - (along * Vector3.Dot(knee - hip, along));
            Assert.That(bend.Z, Is.GreaterThan(0.0f), $"{kneeJoint} bends forward");
        }
    }
}
