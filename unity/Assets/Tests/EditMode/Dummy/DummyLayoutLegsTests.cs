// The layout's legs (PER-110): posing them moves the thigh and foot Target Zones, the leg silhouette and the
// skeleton's leg joints, in place, and the whole upper body riding on the hips (PER-115): the body zone, head, back arm
// and sword arm move and lean with them, every bone keeping its length; a copy made for another arm pose keeps the legs
// and does not move with the original.
using System.Numerics;
using NUnit.Framework;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Tests.Dummy
{
    public class DummyLayoutLegsTests
    {
        private const float Height = 1.8f;
        private const float Tolerance = 1e-4f;

        [TestCase(Hand.Right)]
        [TestCase(Hand.Left)]
        public void ANewLayoutStandsOnTheEnGardeLegs(Hand hand)
        {
            var layout = DummyLayout.Create(Height, hand);
            var legs = DummyLegs.EnGarde(Height, hand);

            AssertNear(layout.Legs.Front.Knee, legs.Front.Knee);
            AssertNear(layout.GetZone(TargetZone.Foot).Start, legs.Front.Heel);
            AssertNear(layout.GetZone(TargetZone.Thigh).End, legs.Front.Hip);
        }

        [Test]
        public void PosingTheLegsMovesTheThighAndFootZonesAndTheLegSilhouette()
        {
            var layout = DummyLayout.Create(Height, Hand.Right);
            var bodyBefore = layout.GetZone(TargetZone.Body);
            var handBefore = layout.GetZone(TargetZone.Hand);
            var headBefore = layout.SilhouetteParts[0];
            var legs = SteppedLegs();

            layout.PoseLegs(legs);

            AssertNear(layout.GetZone(TargetZone.Thigh).Start, legs.Front.Knee);
            AssertNear(layout.GetZone(TargetZone.Thigh).End, legs.Front.Hip);
            AssertNear(layout.GetZone(TargetZone.Foot).Start, legs.Front.Heel);
            AssertNear(layout.GetZone(TargetZone.Foot).End, legs.Front.Toe);
            AssertNear(layout.SilhouetteParts[3].Start, legs.Front.Ankle);
            AssertNear(layout.SilhouetteParts[6].Start, legs.Back.Heel);
            var rest = DummyLegs.EnGarde(Height, Hand.Right);
            var hipShift = ((legs.Front.Hip + legs.Back.Hip) * 0.5f) - ((rest.Front.Hip + rest.Back.Hip) * 0.5f);
            Assert.That(hipShift.Y, Is.LessThan(-0.001f), "The step dips the hips.");
            Assert.That(layout.GetZone(TargetZone.Body).Start.Y, Is.EqualTo(bodyBefore.Start.Y + hipShift.Y).Within(0.002f), "The body dips with them.");
            Assert.That(layout.GetZone(TargetZone.Body).End.Y, Is.EqualTo(bodyBefore.End.Y + hipShift.Y).Within(0.002f));
            Assert.That(layout.GetZone(TargetZone.Hand).Start.Y, Is.EqualTo(handBefore.Start.Y + hipShift.Y).Within(0.002f), "So does the sword arm.");
            Assert.That(layout.SilhouetteParts[0].Start.Y, Is.EqualTo(headBefore.Start.Y + hipShift.Y).Within(0.002f), "And the head.");
        }

        [TestCase(Hand.Right)]
        [TestCase(Hand.Left)]
        public void ALungeLeansTheWholeUpperBodyInOnTheHipsAndNoBoneStretches(Hand hand)
        {
            var layout = DummyLayout.Create(Height, hand);
            var restLengths = GetBoneLengths(layout);
            var shoulderBefore = DummySkeleton.GetJointPosition(layout, SkeletonJoint.SwordShoulder);
            var headBefore = DummySkeleton.GetJointPosition(layout, SkeletonJoint.HeadCenter);
            var footwork = new DummyFootwork(Height, hand, 3.0f, new System.Random(1), isAlive: false);
            var lunge = ArmedDummyAttack.GetLungeMeters(Height);
            for (var frame = 1; frame <= 72; frame++)
            {
                footwork.StepLunge(lunge * frame / 72.0f, lunge, 1.0f / 72.0f);
            }

            layout.PoseLegs(footwork.Legs);

            var lengths = GetBoneLengths(layout);
            for (var bone = 0; bone < lengths.Length; bone++)
            {
                Assert.That(lengths[bone], Is.EqualTo(restLengths[bone]).Within(Tolerance), $"Bone {bone} keeps its length.");
            }

            var hips = DummySkeleton.GetJointPosition(layout, SkeletonJoint.HipCenter);
            var shoulders = DummySkeleton.GetJointPosition(layout, SkeletonJoint.ShoulderCenter);
            var head = DummySkeleton.GetJointPosition(layout, SkeletonJoint.HeadCenter);
            var leanDegrees = layout.Torso.LeanRadians * 180.0f / System.MathF.PI;
            var headTiltRadians = 2.0f * System.MathF.Asin(layout.Torso.HeadRotation.X);
            AssertNear(hips, (footwork.Legs.Front.Hip + footwork.Legs.Back.Hip) * 0.5f);
            Assert.That(leanDegrees, Is.EqualTo(FootworkTuning.LungeTorsoLeanDegrees + FootworkTuning.HipDropTorsoLeanDegrees).Within(0.5f), "The torso leans in.");
            Assert.That(shoulders.Z - hips.Z, Is.GreaterThan(0.1f * Height), "The shoulders lean out over the front leg.");
            Assert.That(DummySkeleton.GetJointPosition(layout, SkeletonJoint.SwordShoulder).Y, Is.LessThan(shoulderBefore.Y - (0.15f * Height)), "The shoulders come down.");
            Assert.That(head.Y, Is.LessThan(headBefore.Y - (0.15f * Height)), "The head follows the chest down.");
            Assert.That(headTiltRadians, Is.GreaterThan(0.0f), "The head tilts forward with the chest...");
            Assert.That(headTiltRadians, Is.LessThan(layout.Torso.LeanRadians), "...but less, so it keeps looking at the Fencer.");
            AssertNear(layout.Arm.GetPose(ArmJoint.Shoulder).Position, DummySkeleton.GetJointPosition(layout, SkeletonJoint.SwordShoulder));
            AssertNear(layout.GetZone(TargetZone.UpperArm).Start, layout.Arm.GetZone(TargetZone.UpperArm).Start);
        }

        [Test]
        public void TheSkeletonsLegJointsFollowTheLegs()
        {
            var layout = DummyLayout.Create(Height, Hand.Left);
            var legs = SteppedLegs(Hand.Left);

            layout.PoseLegs(legs);

            AssertNear(DummySkeleton.GetJointPosition(layout, SkeletonJoint.FrontKnee), legs.Front.Knee);
            AssertNear(DummySkeleton.GetJointPosition(layout, SkeletonJoint.FrontToe), legs.Front.Toe);
            AssertNear(DummySkeleton.GetJointPosition(layout, SkeletonJoint.BackAnkle), legs.Back.Ankle);
        }

        [Test]
        public void TheZonesOnTheStripFollowThePosedFoot()
        {
            var layout = DummyLayout.Create(Height, Hand.Right);
            var legs = SteppedLegs();

            layout.PoseLegs(legs);

            foreach (var zone in layout.GetZonesOnStrip(2.0f))
            {
                if (zone.Zone == TargetZone.Foot)
                {
                    AssertNear(zone.Shape.End, DummyLayout.DummyToStrip(legs.Front.Toe, 2.0f));
                }
            }
        }

        [Test]
        public void ACopyForAnotherArmPoseKeepsTheLegsButNotTheOriginalsLaterMoves()
        {
            var layout = DummyLayout.Create(Height, Hand.Right);
            var stepped = SteppedLegs();
            layout.PoseLegs(stepped);

            var copy = layout.WithArmPose(SwordArmPose.ArmInLine.WithElbowBend(0.5f));
            layout.PoseLegs(DummyLegs.EnGarde(Height, Hand.Right));

            AssertNear(copy.Legs.Front.Toe, stepped.Front.Toe);
            AssertNear(copy.SilhouetteParts[6].End, stepped.Back.Toe);
            AssertNear(layout.SilhouetteParts[6].End, DummyLegs.EnGarde(Height, Hand.Right).Back.Toe);
        }

        /// <summary>The legs a few frames into an advance, when the front foot is up and forward.</summary>
        private static DummyLegs SteppedLegs(Hand hand = Hand.Right)
        {
            var footwork = new DummyFootwork(Height, hand, 2.0f, new System.Random(1), isAlive: false);
            for (var frame = 0; frame < 10; frame++)
            {
                footwork.Step(1.6f, 1.0f / 72.0f);
            }

            Assert.That(footwork.IsFrontFootPlanted, Is.False);
            return footwork.Legs;
        }

        /// <summary>The length of every upper-body and sword arm bone, and of the body zone, in a fixed order.</summary>
        private static float[] GetBoneLengths(DummyLayout layout)
        {
            Vector3 Joint(SkeletonJoint joint) => DummySkeleton.GetJointPosition(layout, joint);
            var body = layout.GetZone(TargetZone.Body);
            return new[]
            {
                Vector3.Distance(Joint(SkeletonJoint.HipCenter), Joint(SkeletonJoint.ShoulderCenter)),
                Vector3.Distance(Joint(SkeletonJoint.ShoulderCenter), Joint(SkeletonJoint.HeadCenter)),
                Vector3.Distance(Joint(SkeletonJoint.SwordShoulder), Joint(SkeletonJoint.BackShoulder)),
                Vector3.Distance(Joint(SkeletonJoint.HipCenter), Joint(SkeletonJoint.SwordShoulder)),
                Vector3.Distance(Joint(SkeletonJoint.BackShoulder), Joint(SkeletonJoint.BackElbow)),
                Vector3.Distance(Joint(SkeletonJoint.BackElbow), Joint(SkeletonJoint.BackHand)),
                Vector3.Distance(Joint(SkeletonJoint.SwordShoulder), Joint(SkeletonJoint.SwordElbow)),
                Vector3.Distance(Joint(SkeletonJoint.SwordElbow), Joint(SkeletonJoint.SwordWrist)),
                Vector3.Distance(Joint(SkeletonJoint.SwordWrist), Joint(SkeletonJoint.SwordHandEnd)),
                Vector3.Distance(body.Start, body.End),
                Vector3.Distance(Joint(SkeletonJoint.HipCenter), body.Start),
            };
        }

        private static void AssertNear(Vector3 actual, Vector3 expected)
        {
            Assert.That(Vector3.Distance(actual, expected), Is.LessThan(Tolerance), $"{actual} vs {expected}");
        }
    }
}
