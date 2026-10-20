// Hits on the Fencer (PER-98, PER-23 resolution): the Armed Dummy's point is judged against IOBT body tracking while it
// is confident (high fidelity, every joint), else against capsules from the headset plus the sword arm, and each hit
// names the body that judged it. Chest and thigh thrusts aim at the body as tracked.
using System;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Blade;
using VrFencing.Dummy;
using VrFencing.Partner;
using VrFencing.Session;
using VrFencing.Settings;

namespace VrFencing.Tests.Partner
{
    public class FencerTargetTests
    {
        private const float HeightMeters = PartnerTestData.HeightMeters;

        private static readonly DummyLayout Stance = DummyLayout.Create(HeightMeters, Hand.Right);

        // The Fencer's guard en garde, out in front and to the sword-hand side of the chest.
        private static readonly Vector3 Guard = new Vector3(0.2f, 1.05f, 0.35f);

        /// <summary>The headset looking down the Strip, its eyes where a head centered on <paramref name="headCenter"/> has them.</summary>
        private static TrackedPose HeadsetFor(Vector3 headCenter)
        {
            var eyes = headCenter + new Vector3(0.0f, 0.0f, FencerTarget.HeadCenterBehindEyesPerHeight * HeightMeters);
            return new TrackedPose(eyes, Quaternion.Identity);
        }

        private static Vector3 StanceHead => EnGardePose.HeadCenter * HeightMeters;

        private static FencerTarget TargetOn(FencerBody trackedBody, TrackedPose? headset, Vector3? guard = null)
        {
            var target = new FencerTarget();
            target.Update(trackedBody, headset, guard ?? Guard, Stance);
            return target;
        }

        private static FencerBody WithoutJoint(FencerBody body, SkeletonJoint missing)
        {
            var joints = Enumerable.Range(0, FencerBody.JointCount).Select(index => index == (int)missing ? null : body.Get(index)).ToArray();
            return new FencerBody(joints, body.HeadRotation, body.Fidelity);
        }

        private static Capsule PartShape(FencerTarget target, BodyPart part)
        {
            return target.Parts.First(candidate => candidate.Part == part).Shape;
        }

        /// <summary>A level thrust straight down the Strip from 1.2 m on the Dummy's side of <paramref name="aim"/>, ending there.</summary>
        private static FencerHit? ThrustAt(FencerTarget target, Vector3 aim)
        {
            return target.FindHit(aim + new Vector3(0.0f, 0.0f, 1.2f), aim);
        }

        [Test]
        public void AConfidentTrackedBodyIsJudgedByIobt()
        {
            var body = PartnerTestData.Body(BodyTrackingFidelity.High, Vector3.Zero);

            var target = TargetOn(body, HeadsetFor(StanceHead));

            Assert.That(FencerTarget.IsTrackedBodyConfident(body, HeightMeters), Is.True);
            Assert.That(target.Source, Is.EqualTo(FencerHitSource.Iobt));
            Assert.That(target.Parts, Has.Count.EqualTo(FencerBodyShape.CapsuleCount));
            Assert.That(target.Capsules, Is.EqualTo(target.Parts.Select(part => part.Shape)));
        }

        [Test]
        public void LowFidelityFallsBackToTheHeadsetCapsules()
        {
            var body = PartnerTestData.Body(BodyTrackingFidelity.Low, Vector3.Zero);

            var target = TargetOn(body, HeadsetFor(StanceHead));

            Assert.That(FencerTarget.IsTrackedBodyConfident(body, HeightMeters), Is.False);
            Assert.That(target.Source, Is.EqualTo(FencerHitSource.HeadsetCapsules));
        }

        [TestCase(SkeletonJoint.FrontKnee)]
        [TestCase(SkeletonJoint.ShoulderCenter)]
        [TestCase(SkeletonJoint.SwordWrist)]
        public void AMissingJointFallsBackToTheHeadsetCapsules(SkeletonJoint missing)
        {
            var body = WithoutJoint(PartnerTestData.Body(BodyTrackingFidelity.High, Vector3.Zero), missing);

            var target = TargetOn(body, HeadsetFor(StanceHead));

            Assert.That(target.Source, Is.EqualTo(FencerHitSource.HeadsetCapsules));
        }

        [Test]
        public void StoppedBodyTrackingFallsBackToTheHeadsetCapsules()
        {
            var target = TargetOn(trackedBody: null, HeadsetFor(StanceHead));

            Assert.That(target.Source, Is.EqualTo(FencerHitSource.HeadsetCapsules));
        }

        [Test]
        public void WithNoHeadsetAndNoConfidentBodyThereIsNothingToHit()
        {
            var target = TargetOn(PartnerTestData.Body(BodyTrackingFidelity.Low, Vector3.Zero), headset: null);

            Assert.That(target.HasBody, Is.False);
            Assert.That(target.Capsules, Is.Empty);
            Assert.That(ThrustAt(target, StanceHead), Is.Null);
            Assert.That(target.TryGetAim(AttackTarget.Chest, out _), Is.False);
        }

        [Test]
        public void AThrustAtTheTrackedChestHitsTheTorsoAndNamesIobt()
        {
            var offset = new Vector3(0.1f, 0.0f, -0.2f);
            var target = TargetOn(PartnerTestData.Body(BodyTrackingFidelity.High, offset), HeadsetFor(StanceHead + offset));
            Assert.That(target.TryGetAim(AttackTarget.Chest, out var aim), Is.True);

            var hit = ThrustAt(target, aim);

            Assert.That(hit.HasValue, Is.True);
            Assert.That(hit.Value.Source, Is.EqualTo(FencerHitSource.Iobt));
            Assert.That(hit.Value.Part, Is.EqualTo(BodyPart.Torso));
            Assert.That(SegmentMath.FindDistanceToCapsule(hit.Value.PointOnStrip, PartShape(target, BodyPart.Torso)), Is.EqualTo(0.0f).Within(0.002f),
                "The hit is where the point met the surface.");
        }

        [Test]
        public void AThrustThatStopsShortOfTheTrackedBodyMisses()
        {
            var target = TargetOn(PartnerTestData.Body(BodyTrackingFidelity.High, Vector3.Zero), HeadsetFor(StanceHead));
            target.TryGetAim(AttackTarget.Chest, out var aim);

            Assert.That(ThrustAt(target, aim + new Vector3(0.0f, 0.0f, 0.15f)), Is.Null);
        }

        [Test]
        public void AFencerWhoStoodBackIsMissedByAThrustAtTheStandIn()
        {
            var stoodBack = new Vector3(0.0f, 0.0f, -0.4f);
            var target = TargetOn(PartnerTestData.Body(BodyTrackingFidelity.High, stoodBack), HeadsetFor(StanceHead + stoodBack));
            var standInChest = DummyLayout.DummyToStrip(AttackAim.GetAimInDummyFrame(AttackTarget.Chest, Stance, 2.0f, null, Vector3.UnitZ), 2.0f);

            Assert.That(ThrustAt(target, standInChest), Is.Null);
        }

        [Test]
        public void AThrustAtTheTrackedFrontThighHitsIt()
        {
            var target = TargetOn(PartnerTestData.Body(BodyTrackingFidelity.High, new Vector3(-0.05f, 0.0f, 0.1f)), HeadsetFor(StanceHead));
            Assert.That(target.TryGetAim(AttackTarget.Thigh, out var aim), Is.True);

            var hit = ThrustAt(target, aim);

            Assert.That(hit?.Part, Is.EqualTo(BodyPart.FrontThigh));
            Assert.That(SegmentMath.FindDistanceToCapsule(aim, PartShape(target, BodyPart.FrontThigh)), Is.LessThan(0.0f), "The aim is inside the thigh.");
        }

        [TestCase(AttackTarget.Hand)]
        [TestCase(AttackTarget.Forearm)]
        public void HandAndForearmAimsAreLeftToTheBlade(AttackTarget target)
        {
            var fencer = TargetOn(PartnerTestData.Body(BodyTrackingFidelity.High, Vector3.Zero), HeadsetFor(StanceHead));

            Assert.That(fencer.TryGetAim(target, out _), Is.False);
        }

        [Test]
        public void AFastPointCannotPassThroughTheSwordArmBetweenFrames()
        {
            var body = PartnerTestData.Body(BodyTrackingFidelity.High, Vector3.Zero);
            var target = TargetOn(body, HeadsetFor(StanceHead));
            var forearmMiddle = (body.Get(SkeletonJoint.SwordElbow).Value + body.Get(SkeletonJoint.SwordWrist).Value) * 0.5f;

            var hit = target.FindHit(forearmMiddle + new Vector3(0.0f, 0.2f, 0.0f), forearmMiddle - new Vector3(0.0f, 0.2f, 0.0f));

            Assert.That(hit?.Part, Is.EqualTo(BodyPart.SwordArm));
        }

        [Test]
        public void APointTheFencerMovesOntoHitsWhereItIs()
        {
            var target = TargetOn(PartnerTestData.Body(BodyTrackingFidelity.High, Vector3.Zero), HeadsetFor(StanceHead));
            var torsoMiddle = PartShape(target, BodyPart.Torso).Center;

            var hit = target.FindHit(torsoMiddle, torsoMiddle + new Vector3(0.0f, 0.0f, 0.01f));

            Assert.That(hit?.Part, Is.EqualTo(BodyPart.Torso));
            Assert.That(hit.Value.PointOnStrip, Is.EqualTo(torsoMiddle));
        }

        [Test]
        public void TheHeadsetCapsulesFollowTheHeadsetAlongTheFloor()
        {
            var moved = new Vector3(0.3f, 0.0f, -0.5f);
            var atLine = TargetOn(trackedBody: null, HeadsetFor(StanceHead));
            var movedAway = TargetOn(trackedBody: null, HeadsetFor(StanceHead + moved), Guard + moved);
            atLine.TryGetAim(AttackTarget.Chest, out var chestAtLine);
            Assert.That(movedAway.TryGetAim(AttackTarget.Chest, out var chestMoved), Is.True);

            Assert.That(Vector3.Distance(chestMoved, chestAtLine + moved), Is.LessThan(1e-4f));
            Assert.That(ThrustAt(movedAway, chestAtLine), Is.Null, "The stance under the old place is gone.");
            var hit = ThrustAt(movedAway, chestMoved);
            Assert.That(hit?.Part, Is.EqualTo(BodyPart.Torso));
            Assert.That(hit.Value.Source, Is.EqualTo(FencerHitSource.HeadsetCapsules));
        }

        [Test]
        public void TheHeadsetCapsulesKeepTheStanceHeights()
        {
            var lunging = TargetOn(trackedBody: null, HeadsetFor(StanceHead + new Vector3(0.0f, -0.25f, 0.4f)));

            var stanceThigh = Stance.GetZone(TargetZone.Thigh);
            var thigh = PartShape(lunging, BodyPart.FrontThigh);

            Assert.That(thigh.Start.Y, Is.EqualTo(stanceThigh.Start.Y).Within(1e-5f));
            Assert.That(thigh.End.Y, Is.EqualTo(stanceThigh.End.Y).Within(1e-5f));
        }

        [Test]
        public void TheHeadsetCapsulesHoldTheSwordArmToTheGuard()
        {
            var target = TargetOn(trackedBody: null, HeadsetFor(StanceHead), Guard);
            var arm = PartShape(target, BodyPart.SwordArm);
            var armMiddle = arm.Center;

            Assert.That(arm.Start, Is.EqualTo(Guard));
            Assert.That(target.FindHit(armMiddle + new Vector3(0.0f, 0.3f, 0.0f), armMiddle)?.Part, Is.EqualTo(BodyPart.SwordArm));

            var noBlade = new FencerTarget();
            noBlade.Update(null, HeadsetFor(StanceHead), null, Stance);
            Assert.That(noBlade.Parts.Any(part => part.Part == BodyPart.SwordArm), Is.False, "No arm while the blade is unknown.");
        }

        [Test]
        public void TheHeadCenterIsBehindTheEyesWhereverTheHeadTurns()
        {
            var turned = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)(Math.PI / 2.0));
            var eyes = new Vector3(0.0f, 1.6f, 0.0f);

            var head = FencerTarget.GetHeadCenter(new TrackedPose(eyes, turned), HeightMeters);

            var behind = FencerTarget.HeadCenterBehindEyesPerHeight * HeightMeters;
            Assert.That(Vector3.Distance(head, eyes - new Vector3(behind, 0.0f, 0.0f)), Is.LessThan(1e-5f), "Turned to look along +x.");
        }

        [Test]
        public void EveryCapsuleListHasAPartForEachCapsule()
        {
            Assert.That(FencerBodyShape.CapsuleParts.Count, Is.EqualTo(FencerBodyShape.CapsuleCount));
            Assert.That(DummyLayout.SilhouetteBodyParts.Count, Is.EqualTo(Stance.SilhouetteParts.Count));
            Assert.That(AttackAim.GetFencerStanceOnStrip(Stance).Select(part => part.Shape), Is.EqualTo(AttackAim.GetFencerBodyOnStrip(Stance, null)));
        }
    }
}
