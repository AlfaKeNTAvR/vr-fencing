// Posing the rigged body (PER-142): each limb bone is aimed through its parent, so a body facing -z (the opponent)
// twists its limbs as one facing +z (the Dummy); the elbow and knee hinge in the tracked plane; a foot pointing straight
// back stays right side up; a hand with a palm rolls with it through full turns without jumping, the forearm taking a
// share; tracked lengths are fitted by their median. The Dummy's standard poses keep every bone on its joint.
using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Tests.Dummy
{
    public class BodyRigPoserTests
    {
        private const float Height = 1.65f;
        private const float DegreesPerRadian = (float)(180.0 / Math.PI);

        private static readonly Quaternion HalfTurn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)Math.PI);

        [Test]
        public void AFrameFromTwoDirectionsPutsItsAxesOnThem()
        {
            var x = Vector3.Normalize(new Vector3(1.0f, 2.0f, -0.5f));
            var frame = RigMath.FrameFromXY(x, new Vector3(0.0f, 1.0f, 3.0f));

            Assert.That(Vector3.Distance(Vector3.Transform(Vector3.UnitX, frame), x), Is.LessThan(1e-5f));
            Assert.That(Vector3.Dot(Vector3.Transform(Vector3.UnitY, frame), new Vector3(0.0f, 1.0f, 3.0f)), Is.GreaterThan(0.0f));
            Assert.That(Vector3.Distance(Vector3.Transform(Vector3.UnitZ, frame), Vector3.Cross(Vector3.Transform(Vector3.UnitX, frame), Vector3.Transform(Vector3.UnitY, frame))), Is.LessThan(1e-5f));
        }

        [TestCase(Hand.Right)]
        [TestCase(Hand.Left)]
        public void ABodyTurnedHalfWayRoundGetsTheSameLimbsTurnedWithIt(Hand hand)
        {
            var layout = DummyLayout.Create(Height, hand, SwordArmPose.ArmInLine.WithElbowBend(0.7f));
            var facingPlusZ = Pose(DummyTargets(layout), fitsLengths: false);
            var turned = DummyTargets(layout);
            TurnHalfWay(turned);
            var facingMinusZ = Pose(turned, fitsLengths: false);

            foreach (RigBone bone in Enum.GetValues(typeof(RigBone)))
            {
                if (bone == RigBone.Neck)
                {
                    continue;
                }

                var expected = HalfTurn * facingPlusZ.GetPose(bone).Rotation;
                Assert.That(AngleDegrees(facingMinusZ.GetPose(bone).Rotation, expected), Is.LessThan(0.5f), bone.ToString());
            }
        }

        [Test]
        public void TheElbowHingesInThePlaneOfTheArmWhicheverWayTheBodyFaces()
        {
            var random = new Random(3);
            foreach (var isTurned in new[] { false, true })
            {
                var poser = new BodyRigPoser(ArmatureRestData.Create(Height), false);
                var worst = 0.0f;
                for (var trial = 0; trial < 300; trial++)
                {
                    var targets = DummyTargets(DummyLayout.Create(Height, Hand.Right));
                    var shoulder = targets.Get(SkeletonJoint.SwordShoulder).Value;
                    var upper = RandomDirection(random);
                    var elbow = shoulder + (upper * 0.27f);
                    var bend = (float)(0.6 + (random.NextDouble() * 1.8));
                    var lower = Vector3.Transform(upper, Quaternion.CreateFromAxisAngle(RigMath.GetPerpendicular(upper), bend));
                    lower = Vector3.Transform(lower, Quaternion.CreateFromAxisAngle(upper, (float)(random.NextDouble() * 2.0 * Math.PI)));
                    var wrist = elbow + (lower * 0.23f);
                    targets.Set(SkeletonJoint.SwordElbow, elbow);
                    targets.Set(SkeletonJoint.SwordWrist, wrist);
                    targets.Set(SkeletonJoint.SwordHandEnd, wrist + (lower * 0.08f));
                    if (isTurned)
                    {
                        TurnHalfWay(targets);
                    }

                    poser.Pose(targets);
                    worst = Math.Max(worst, GetElbowHingeErrorDegrees(poser, ArmatureRestData.Create(Height), false, targets));
                }

                Assert.That(worst, Is.LessThan(0.5f), isTurned ? "Facing -z" : "Facing +z");
            }
        }

        [TestCase(Hand.Right)]
        [TestCase(Hand.Left)]
        public void AFootPointingStraightBackStaysRightSideUpAndPointsBack(Hand hand)
        {
            // The opponent's en garde: the Dummy's stance turned to face -z, the front heel-to-toe line exactly (0, 0, -1).
            var targets = DummyTargets(DummyLayout.Create(Height, hand));
            var ankle = targets.Get(SkeletonJoint.FrontAnkle).Value;
            targets.Set(SkeletonJoint.FrontToe, new Vector3(ankle.X, ankle.Y, ankle.Z + 0.12f));
            TurnHalfWay(targets);
            var rest = ArmatureRestData.Create(Height);
            var poser = Pose(targets, rest, false);

            var foot = hand == Hand.Right ? RigBone.RightFoot : RigBone.LeftFoot;
            var turn = poser.GetPose(foot).Rotation * Quaternion.Inverse(rest.GetRotation(foot));
            Assert.That(Vector3.Transform(Vector3.UnitY, turn).Y, Is.GreaterThan(0.999f), "Sole down.");
            Assert.That(Vector3.Dot(Vector3.Transform(Vector3.UnitZ, turn), -Vector3.UnitZ), Is.GreaterThan(0.999f), "Toes toward -z.");
        }

        [TestCase(Hand.Right)]
        [TestCase(Hand.Left)]
        public void AHandRollsWithItsPalmThroughFullTurnsWithoutJumps(Hand hand)
        {
            var rest = ArmatureRestData.Create(Height);
            var poser = new BodyRigPoser(rest, false);
            var targets = DummyTargets(DummyLayout.Create(Height, hand, SwordArmPose.ArmInLine.WithElbowBend(0.5f)));
            TurnHalfWay(targets);
            var elbow = targets.Get(SkeletonJoint.SwordElbow).Value;
            var wrist = targets.Get(SkeletonJoint.SwordWrist).Value;
            var handEnd = targets.Get(SkeletonJoint.SwordHandEnd).Value;
            var fingers = Vector3.Normalize(handEnd - wrist);
            var handBone = hand == Hand.Right ? RigBone.RightHand : RigBone.LeftHand;
            var forearmBone = hand == Hand.Right ? RigBone.RightLowerArm : RigBone.LeftLowerArm;
            var startPalm = RigMath.GetPerpendicular(fingers);

            Quaternion? lastHand = null;
            Quaternion? lastForearm = null;
            const float stepDegrees = 3.0f;
            for (var step = 0; step <= 2 * 360 / stepDegrees; step++)
            {
                // Two full turns one way: past any pronation or supination a wrist can do.
                var palm = Vector3.Transform(startPalm, Quaternion.CreateFromAxisAngle(fingers, step * stepDegrees / DegreesPerRadian));
                targets.SwordPalm = palm;
                poser.Pose(targets);

                // The rig's rest palm (down), square to its rest hand line, turned with the hand, faces the palm.
                var handRotation = poser.GetPose(handBone).Rotation;
                var handTurn = handRotation * Quaternion.Inverse(rest.GetRotation(handBone));
                var restFingers = Vector3.Normalize((hand == Hand.Right ? rest.RightMiddleFinger.Value : rest.LeftMiddleFinger.Value) - rest.GetPosition(handBone));
                var restPalm = hand == Hand.Right ? rest.RightPalm.Value : rest.LeftPalm.Value;
                restPalm = Vector3.Normalize(restPalm - (restFingers * Vector3.Dot(restFingers, restPalm)));
                Assert.That(Vector3.Dot(Vector3.Transform(restPalm, handTurn), palm), Is.GreaterThan(0.9999f), $"The palm faces where it should (step {step}).");
                if (lastHand.HasValue)
                {
                    Assert.That(AngleDegrees(lastHand.Value, handRotation), Is.LessThan(stepDegrees + 0.5f), $"Hand, step {step}.");
                    Assert.That(AngleDegrees(lastForearm.Value, poser.GetPose(forearmBone).Rotation), Is.LessThan((stepDegrees * BodyRigTuning.ForearmTwistShare) + 0.5f), $"Forearm, step {step}.");
                }

                lastHand = handRotation;
                lastForearm = poser.GetPose(forearmBone).Rotation;
                Assert.That(Vector3.Distance(poser.GetPose(handBone).Position, wrist), Is.LessThan(1e-5f));
                Assert.That(Vector3.Distance(poser.GetPose(forearmBone).Position, elbow), Is.LessThan(1e-5f));
            }
        }

        [Test]
        public void ALengthFitTakesTheMedianOfItsSamples()
        {
            var fit = new BoneLengthFit();
            Assert.That(fit.Meters, Is.Null);

            var lengths = new[] { 0.22f, 0.23f, 0.50f, 0.225f, 0.226f };
            for (var index = 0; index < lengths.Length; index++)
            {
                fit.Add(index * BodyRigTuning.LengthFitSampleSeconds, lengths[index]);
            }

            Assert.That(fit.Meters, Is.EqualTo(0.226f).Within(1e-6f), "One wild sample does not move it.");

            // Samples closer together than the interval are skipped.
            fit.Add((lengths.Length - 1) * BodyRigTuning.LengthFitSampleSeconds + 0.01, 0.9f);
            Assert.That(fit.Meters, Is.EqualTo(0.226f).Within(1e-6f));
        }

        [Test]
        public void FittingLengthsScalesTheUpperArmToTheTrackedOne()
        {
            var rest = ArmatureRestData.Create(Height);
            var poser = new BodyRigPoser(rest, true);
            var targets = DummyTargets(DummyLayout.Create(Height, Hand.Right));
            var shoulder = targets.Get(SkeletonJoint.SwordShoulder).Value;
            var elbow = targets.Get(SkeletonJoint.SwordElbow).Value;
            var tracked = Vector3.Distance(shoulder, elbow);

            poser.Pose(targets);

            var restLength = Vector3.Distance(rest.GetPosition(RigBone.RightUpperArm), rest.GetPosition(RigBone.RightLowerArm));
            Assert.That(restLength * poser.GetPose(RigBone.RightUpperArm).Scale, Is.EqualTo(tracked).Within(1e-4f));
            Assert.That(new BodyRigPoser(rest, false).FitsLengths, Is.False);
        }

        [TestCase(Hand.Right, "en garde")]
        [TestCase(Hand.Left, "en garde")]
        [TestCase(Hand.Right, "arm bent")]
        [TestCase(Hand.Right, "advance")]
        [TestCase(Hand.Left, "advance")]
        [TestCase(Hand.Right, "lunge")]
        [TestCase(Hand.Left, "lunge")]
        public void TheDummysStandardPosesPutEveryBoneOnItsJointWithTheHingesInPlace(Hand hand, string pose)
        {
            var layout = CreateStandardPose(hand, pose);
            var targets = DummyTargets(layout);
            var rest = ArmatureRestData.Create(Height);
            var poser = Pose(targets, rest, false);
            var isRight = hand == Hand.Right;

            AssertOn(poser, isRight ? RigBone.RightUpperArm : RigBone.LeftUpperArm, targets.Get(SkeletonJoint.SwordShoulder));
            AssertOn(poser, isRight ? RigBone.RightLowerArm : RigBone.LeftLowerArm, targets.Get(SkeletonJoint.SwordElbow));
            AssertOn(poser, isRight ? RigBone.RightHand : RigBone.LeftHand, targets.Get(SkeletonJoint.SwordWrist));
            AssertOn(poser, isRight ? RigBone.LeftLowerArm : RigBone.RightLowerArm, targets.Get(SkeletonJoint.BackElbow));
            AssertOn(poser, isRight ? RigBone.RightUpperLeg : RigBone.LeftUpperLeg, targets.Get(SkeletonJoint.FrontHip));
            AssertOn(poser, isRight ? RigBone.RightLowerLeg : RigBone.LeftLowerLeg, targets.Get(SkeletonJoint.FrontKnee));
            AssertOn(poser, isRight ? RigBone.LeftLowerLeg : RigBone.RightLowerLeg, targets.Get(SkeletonJoint.BackKnee));

            Assert.That(GetElbowHingeErrorDegrees(poser, rest, false, targets), Is.LessThan(1.0f), "Sword elbow.");
            Assert.That(GetElbowHingeErrorDegrees(poser, rest, true, targets), Is.LessThan(1.0f), "Back elbow.");
            Assert.That(GetKneeHingeErrorDegrees(poser, rest, true, targets), Is.LessThan(1.0f), "Front knee.");
            Assert.That(GetKneeHingeErrorDegrees(poser, rest, false, targets), Is.LessThan(1.0f), "Back knee.");
            foreach (var foot in new[] { RigBone.LeftFoot, RigBone.RightFoot })
            {
                var turn = poser.GetPose(foot).Rotation * Quaternion.Inverse(rest.GetRotation(foot));
                Assert.That(Vector3.Transform(Vector3.UnitY, turn).Y, Is.GreaterThan(0.999f), $"{foot} sole down.");
            }

            // How far each limb bone turned from the old posing (the least turn from rest, each bone on its own), for the record.
            var changes = new List<string>();
            foreach (var bone in new[] { RigBone.RightUpperArm, RigBone.RightLowerArm, RigBone.RightHand, RigBone.LeftUpperArm, RigBone.LeftLowerArm, RigBone.LeftHand, RigBone.RightUpperLeg, RigBone.RightLowerLeg, RigBone.LeftUpperLeg, RigBone.LeftLowerLeg })
            {
                changes.Add($"{bone} {AngleDegrees(poser.GetPose(bone).Rotation, GetOldRotation(rest, bone, targets)):0}");
            }

            TestContext.WriteLine($"{hand} {pose}: turned from the old posing (degrees): {string.Join(", ", changes)}");
            var swordHand = isRight ? RigBone.RightHand : RigBone.LeftHand;
            var restPalm = isRight ? rest.RightPalm.Value : rest.LeftPalm.Value;
            var palmNow = Vector3.Transform(restPalm, poser.GetPose(swordHand).Rotation * Quaternion.Inverse(rest.GetRotation(swordHand)));
            var palmOld = Vector3.Transform(restPalm, GetOldRotation(rest, swordHand, targets) * Quaternion.Inverse(rest.GetRotation(swordHand)));
            TestContext.WriteLine($"{hand} {pose}: sword palm now {palmNow}, before {palmOld}.");
        }

        [Test]
        public void PosingCostsLittle()
        {
            var poser = new BodyRigPoser(ArmatureRestData.Create(Height), true);
            var targets = DummyTargets(DummyLayout.Create(Height, Hand.Right));
            targets.SwordPalm = -Vector3.UnitX;
            targets.BackPalm = Vector3.UnitY;
            const int frames = 20000;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (var frame = 0; frame < frames; frame++)
            {
                targets.TimeSeconds = frame / 72.0;
                poser.Pose(targets);
            }

            watch.Stop();
            var microseconds = watch.Elapsed.TotalMilliseconds * 1000.0 / frames;
            TestContext.WriteLine($"Body rig posing: {microseconds:0.0} us per frame.");
            Assert.That(microseconds, Is.LessThan(100.0));
        }

        public static DummyLayout CreateStandardPose(Hand hand, string pose)
        {
            switch (pose)
            {
                case "en garde":
                    return DummyLayout.Create(Height, hand);
                case "arm bent":
                    return DummyLayout.Create(Height, hand, SwordArmPose.ArmInLine.WithElbowBend(0.6f));
                case "advance":
                    var advance = new DummyFootwork(Height, hand, 2.0f, new Random(1));
                    for (var frame = 0; frame < 10; frame++)
                    {
                        advance.Step(1.6f, 1.0f / 72.0f);
                    }

                    return WithLegs(hand, advance.Legs);
                case "lunge":
                    var lunge = new DummyFootwork(Height, hand, 2.0f, new Random(1));
                    var length = ArmedDummyAttack.GetLungeMeters(Height);
                    for (var frame = 0; frame < 30; frame++)
                    {
                        lunge.StepLunge(length * Math.Min(1.0f, frame / 20.0f), length, 1.0f / 72.0f);
                    }

                    return WithLegs(hand, lunge.Legs);
                default:
                    throw new ArgumentException(pose);
            }
        }

        private static DummyLayout WithLegs(Hand hand, DummyLegs legs)
        {
            var layout = DummyLayout.Create(Height, hand);
            layout.PoseLegs(legs);
            return layout;
        }

        public static BodyRigTargets DummyTargets(DummyLayout layout)
        {
            var targets = new BodyRigTargets();
            DummyRigTargets.Fill(targets, layout);
            return targets;
        }

        /// <summary>Every target turned half a turn about the vertical: the same body facing -z.</summary>
        public static void TurnHalfWay(BodyRigTargets targets)
        {
            for (var index = 0; index < BodyRigTargets.JointCount; index++)
            {
                var joint = (SkeletonJoint)index;
                var position = targets.Get(joint);
                targets.Set(joint, position.HasValue ? Vector3.Transform(position.Value, HalfTurn) : (Vector3?)null);
            }

            targets.BackHandEnd = Turn(targets.BackHandEnd);
            targets.HeadJoint = Turn(targets.HeadJoint);
            targets.Eyes = Turn(targets.Eyes);
            targets.SwordPalm = Turn(targets.SwordPalm);
            targets.SwordFingers = Turn(targets.SwordFingers);
            targets.BackPalm = Turn(targets.BackPalm);
            targets.BackFingers = Turn(targets.BackFingers);
            targets.HeadRotation = targets.HeadRotation.HasValue ? HalfTurn * targets.HeadRotation.Value : HalfTurn;
        }

        private static Vector3? Turn(Vector3? point)
        {
            return point.HasValue ? Vector3.Transform(point.Value, HalfTurn) : (Vector3?)null;
        }

        private static BodyRigPoser Pose(BodyRigTargets targets, bool fitsLengths)
        {
            return Pose(targets, ArmatureRestData.Create(Height), fitsLengths);
        }

        private static BodyRigPoser Pose(BodyRigTargets targets, BodyRigRest rest, bool fitsLengths)
        {
            var poser = new BodyRigPoser(rest, fitsLengths);
            poser.Pose(targets);
            return poser;
        }

        private static void AssertOn(BodyRigPoser poser, RigBone bone, Vector3? joint)
        {
            Assert.That(Vector3.Distance(poser.GetPose(bone).Position, joint.Value), Is.LessThan(1e-4f), bone.ToString());
        }

        /// <summary>
        /// The angle between the posed upper arm's elbow hinge (its rest hinge, forearm bending forward from the T-pose,
        /// carried by the bone's turn) and the normal of the shoulder-elbow-wrist plane; 0 when the arm is nearly straight.
        /// </summary>
        public static float GetElbowHingeErrorDegrees(BodyRigPoser poser, BodyRigRest rest, bool isBackArm, BodyRigTargets targets)
        {
            var isLeft = isBackArm == (targets.Hand != Hand.Left);
            var upper = isLeft ? RigBone.LeftUpperArm : RigBone.RightUpperArm;
            var shoulder = targets.Get(isBackArm ? SkeletonJoint.BackShoulder : SkeletonJoint.SwordShoulder).Value;
            var elbow = targets.Get(isBackArm ? SkeletonJoint.BackElbow : SkeletonJoint.SwordElbow).Value;
            var wrist = targets.Get(isBackArm ? SkeletonJoint.BackHand : SkeletonJoint.SwordWrist).Value;
            var restHinge = poser.GetRestHinge(upper);
            return GetHingeErrorDegrees(poser, rest, upper, restHinge, shoulder, elbow, wrist);
        }

        public static float GetKneeHingeErrorDegrees(BodyRigPoser poser, BodyRigRest rest, bool isFrontLeg, BodyRigTargets targets)
        {
            var isLeft = isFrontLeg == (targets.Hand == Hand.Left);
            var upper = isLeft ? RigBone.LeftUpperLeg : RigBone.RightUpperLeg;
            var hip = targets.Get(isFrontLeg ? SkeletonJoint.FrontHip : SkeletonJoint.BackHip).Value;
            var knee = targets.Get(isFrontLeg ? SkeletonJoint.FrontKnee : SkeletonJoint.BackKnee).Value;
            var foot = isLeft ? RigBone.LeftFoot : RigBone.RightFoot;
            var restHinge = poser.GetRestHinge(upper);
            return GetHingeErrorDegrees(poser, rest, upper, restHinge, hip, knee, poser.GetPose(foot).Position);
        }

        private static float GetHingeErrorDegrees(BodyRigPoser poser, BodyRigRest rest, RigBone upper, Vector3 restHinge, Vector3 root, Vector3 middle, Vector3 end)
        {
            if (RigMath.GetAngle(middle - root, end - middle) * DegreesPerRadian < BodyRigTuning.FullBendDegrees)
            {
                return 0.0f;
            }

            var hinge = Vector3.Transform(restHinge, poser.GetPose(upper).Rotation * Quaternion.Inverse(rest.GetRotation(upper)));
            return RigMath.GetAngle(hinge, Vector3.Cross(middle - root, end - middle)) * DegreesPerRadian;
        }

        /// <summary>The old posing (before PER-142): the least turn from the bone's rest direction, each bone on its own.</summary>
        private static Quaternion GetOldRotation(BodyRigRest rest, RigBone bone, BodyRigTargets targets)
        {
            var poser = new BodyRigPoser(rest, false);
            poser.Pose(targets);
            var child = bone switch
            {
                RigBone.RightUpperArm => RigBone.RightLowerArm,
                RigBone.RightLowerArm => RigBone.RightHand,
                RigBone.LeftUpperArm => RigBone.LeftLowerArm,
                RigBone.LeftLowerArm => RigBone.LeftHand,
                RigBone.RightUpperLeg => RigBone.RightLowerLeg,
                RigBone.RightLowerLeg => RigBone.RightFoot,
                RigBone.LeftUpperLeg => RigBone.LeftLowerLeg,
                RigBone.LeftLowerLeg => RigBone.LeftFoot,
                _ => (RigBone?)null,
            };
            Vector3 restDirection;
            Vector3 direction;
            if (child.HasValue)
            {
                restDirection = rest.GetPosition(child.Value) - rest.GetPosition(bone);
                direction = poser.GetPose(child.Value).Position - poser.GetPose(bone).Position;
            }
            else
            {
                var isLeft = bone == RigBone.LeftHand;
                var isSword = isLeft == (targets.Hand == Hand.Left);
                var end = isSword ? targets.Get(SkeletonJoint.SwordHandEnd).Value : targets.BackHandEnd.Value;
                direction = end - poser.GetPose(bone).Position;
                restDirection = (isLeft ? rest.LeftMiddleFinger.Value : rest.RightMiddleFinger.Value) - rest.GetPosition(bone);
            }

            return RigMath.FromTo(restDirection, direction) * rest.GetRotation(bone);
        }

        private static Vector3 RandomDirection(Random random)
        {
            while (true)
            {
                var v = new Vector3((float)((random.NextDouble() * 2.0) - 1.0), (float)((random.NextDouble() * 2.0) - 1.0), (float)((random.NextDouble() * 2.0) - 1.0));
                if (v.LengthSquared() is > 0.01f and <= 1.0f)
                {
                    return Vector3.Normalize(v);
                }
            }
        }

        public static float AngleDegrees(Quaternion a, Quaternion b)
        {
            var dot = Math.Abs(Quaternion.Dot(Quaternion.Normalize(a), Quaternion.Normalize(b)));
            return 2.0f * (float)Math.Acos(Math.Min(1.0f, dot)) * DegreesPerRadian;
        }
    }
}
