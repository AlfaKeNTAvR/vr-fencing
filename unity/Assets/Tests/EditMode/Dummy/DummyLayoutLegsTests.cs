// The layout's legs (PER-110): posing them moves the thigh and foot Target Zones, the leg silhouette and the
// skeleton's leg joints, in place, and the body zone's bottom with the hips (PER-111), while its top, the arm zones and
// the head stay; a copy made for another arm pose keeps the legs and does not move with the original.
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
            AssertNear(layout.GetZone(TargetZone.Body).Start, bodyBefore.Start + hipShift);
            AssertNear(layout.GetZone(TargetZone.Body).End, bodyBefore.End);
            AssertNear(layout.GetZone(TargetZone.Hand).Start, handBefore.Start);
            AssertNear(layout.SilhouetteParts[0].Start, headBefore.Start);
        }

        [TestCase(Hand.Right)]
        [TestCase(Hand.Left)]
        public void LungingHipsTiltTheBodyZoneButLeaveTheShouldersAndHead(Hand hand)
        {
            var layout = DummyLayout.Create(Height, hand);
            var bodyBefore = layout.GetZone(TargetZone.Body);
            var shoulderBefore = DummySkeleton.GetJointPosition(layout, SkeletonJoint.SwordShoulder);
            var headBefore = DummySkeleton.GetJointPosition(layout, SkeletonJoint.HeadCenter);
            var footwork = new DummyFootwork(Height, hand, 3.0f, new System.Random(1), isAlive: false);
            var lunge = ArmedDummyAttack.GetLungeMeters(Height);
            for (var frame = 1; frame <= 72; frame++)
            {
                footwork.StepLunge(lunge * frame / 72.0f, lunge, 1.0f / 72.0f);
            }

            layout.PoseLegs(footwork.Legs);

            var body = layout.GetZone(TargetZone.Body);
            Assert.That(body.Start.Y, Is.LessThan(bodyBefore.Start.Y - (0.1f * Height)), "The pelvis drops.");
            Assert.That(body.Start.Z, Is.LessThan(bodyBefore.Start.Z - (0.1f * Height)), "The pelvis stays behind.");
            AssertNear(body.End, bodyBefore.End);
            AssertNear(DummySkeleton.GetJointPosition(layout, SkeletonJoint.SwordShoulder), shoulderBefore);
            AssertNear(DummySkeleton.GetJointPosition(layout, SkeletonJoint.HeadCenter), headBefore);
            AssertNear(DummySkeleton.GetJointPosition(layout, SkeletonJoint.HipCenter), (footwork.Legs.Front.Hip + footwork.Legs.Back.Hip) * 0.5f);
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

        private static void AssertNear(Vector3 actual, Vector3 expected)
        {
            Assert.That(Vector3.Distance(actual, expected), Is.LessThan(Tolerance), $"{actual} vs {expected}");
        }
    }
}
