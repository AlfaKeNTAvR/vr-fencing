// The rig posing on the real Armature model (PER-142): the Core test fixture is the model's own rest pose (a T-pose,
// palms down), the Dummy's standard poses hinge its elbows and knees in place and keep its feet upright, the opponent's
// fitted bones are scaled with the scale undone below them, and posing the opponent from the real recording costs little.
using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VrFencing.Dummy;
using VrFencing.Partner;
using VrFencing.Settings;
using VrFencing.Tests.Dummy;
using Object = UnityEngine.Object;

namespace VrFencing.Tests.Partner
{
    public class OpponentRigUnityTests
    {
        private const string BodyModelPath = "Packages/com.meta.xr.sdk.movement/Shared/ThirdParty/Unity/Armature.fbx";
        private const float Height = 1.65f;

        private static readonly (RigBone Rig, HumanBodyBones Unity)[] Bones =
        {
            (RigBone.Hips, HumanBodyBones.Hips),
            (RigBone.Chest, HumanBodyBones.Chest),
            (RigBone.UpperChest, HumanBodyBones.UpperChest),
            (RigBone.Neck, HumanBodyBones.Neck),
            (RigBone.Head, HumanBodyBones.Head),
            (RigBone.LeftUpperArm, HumanBodyBones.LeftUpperArm),
            (RigBone.LeftLowerArm, HumanBodyBones.LeftLowerArm),
            (RigBone.LeftHand, HumanBodyBones.LeftHand),
            (RigBone.RightUpperArm, HumanBodyBones.RightUpperArm),
            (RigBone.RightLowerArm, HumanBodyBones.RightLowerArm),
            (RigBone.RightHand, HumanBodyBones.RightHand),
            (RigBone.LeftUpperLeg, HumanBodyBones.LeftUpperLeg),
            (RigBone.LeftLowerLeg, HumanBodyBones.LeftLowerLeg),
            (RigBone.LeftFoot, HumanBodyBones.LeftFoot),
            (RigBone.RightUpperLeg, HumanBodyBones.RightUpperLeg),
            (RigBone.RightLowerLeg, HumanBodyBones.RightLowerLeg),
            (RigBone.RightFoot, HumanBodyBones.RightFoot),
        };

        private GameObject _root;

        [TearDown]
        public void DestroyRoot()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        [Test]
        public void TheCoreFixtureIsTheModelsRestPose()
        {
            // A rig at rest: built, then every bone put back as the model has it, read in the parts frame.
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(BodyModelPath);
            _root = new GameObject("Rest test");
            var instance = Object.Instantiate(model, _root.transform, false);
            var scale = DummySkeleton.StandingShoulderHeightPerHeight * Height / ArmatureRestData.RestShoulderHeight;
            instance.transform.localScale = Vector3.one * scale;
            var animator = instance.GetComponent<Animator>();
            var fixture = ArmatureRestData.Create(Height);

            // The model's own rest, for ArmatureRestData if it ever changes.
            foreach (var (rig, unity) in Bones)
            {
                var bone = FindBone(animator, unity);
                var p = bone.position / scale;
                var q = bone.rotation;
                TestContext.WriteLine(FormattableString.Invariant($"{{ RigBone.{rig}, (new Vector3({p.x:0.00000}f, {p.y:0.00000}f, {p.z:0.00000}f), new Quaternion({q.x:0.00000}f, {q.y:0.00000}f, {q.z:0.00000}f, {q.w:0.00000}f)) }},"));
            }

            foreach (var point in new[]
                     {
                         HumanBodyBones.LeftMiddleProximal, HumanBodyBones.RightMiddleProximal,
                         HumanBodyBones.LeftIndexProximal, HumanBodyBones.RightIndexProximal, HumanBodyBones.LeftLittleProximal, HumanBodyBones.RightLittleProximal,
                     })
            {
                var p = FindBone(animator, point).position / scale;
                TestContext.WriteLine(FormattableString.Invariant($"{point}: new Vector3({p.x:0.00000}f, {p.y:0.00000}f, {p.z:0.00000}f)"));
            }

            var eyes = (FindNamed(instance, DummyBodyRig.LeftEyeName).position + FindNamed(instance, DummyBodyRig.RightEyeName).position) * 0.5f;
            TestContext.WriteLine(FormattableString.Invariant($"Eyes: new Vector3({eyes.x / scale:0.00000}f, {eyes.y / scale:0.00000}f, {eyes.z / scale:0.00000}f)"));

            foreach (var (rig, unity) in Bones)
            {
                var bone = FindBone(animator, unity);
                Assert.That(bone, Is.Not.Null, unity.ToString());
                Assert.That(Vector3.Distance(bone.position, fixture.GetPosition(rig).ToUnity()), Is.LessThan(0.001f), $"{rig} position");
                Assert.That(Quaternion.Angle(bone.rotation, fixture.GetRotation(rig).ToUnity()), Is.LessThan(0.5f), $"{rig} rotation");
            }

            foreach (var isLeft in new[] { true, false })
            {
                var hand = FindBone(animator, isLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand).position;
                var index = FindBone(animator, isLeft ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal).position;
                var little = FindBone(animator, isLeft ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal).position;
                var middle = FindBone(animator, isLeft ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal).position;
                var palm = BodyRigRest.GetPalm((middle - hand).ToNumerics(), (index - little).ToNumerics(), isLeft);
                var expected = isLeft ? fixture.LeftPalm.Value : fixture.RightPalm.Value;
                Assert.That(System.Numerics.Vector3.Dot(palm, expected), Is.GreaterThan(0.999f), isLeft ? "Left palm." : "Right palm.");
                Assert.That(Vector3.Distance(middle, (isLeft ? fixture.LeftMiddleFinger.Value : fixture.RightMiddleFinger.Value).ToUnity()), Is.LessThan(0.001f));
            }

            Assert.That(Vector3.Distance(eyes, fixture.Eyes.Value.ToUnity()), Is.LessThan(0.001f));
        }

        private static Transform FindNamed(GameObject instance, string transformName)
        {
            foreach (var child in instance.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == transformName)
                {
                    return child;
                }
            }

            return null;
        }

        [TestCase(Hand.Right, "en garde")]
        [TestCase(Hand.Left, "en garde")]
        [TestCase(Hand.Right, "arm bent")]
        [TestCase(Hand.Right, "advance")]
        [TestCase(Hand.Right, "lunge")]
        [TestCase(Hand.Left, "lunge")]
        public void TheDummysStandardPosesOnTheModelKeepTheirHingesAndFeet(Hand hand, string pose)
        {
            var layout = BodyRigPoserTests.CreateStandardPose(hand, pose);
            var rig = CreateRig(layout, fitsLengths: false);
            rig.Pose(layout);
            var targets = BodyRigPoserTests.DummyTargets(layout);
            var isRight = hand == Hand.Right;

            AssertHinge(rig, isRight ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm, targets.Get(SkeletonJoint.SwordShoulder), targets.Get(SkeletonJoint.SwordElbow), targets.Get(SkeletonJoint.SwordWrist), true);
            AssertHinge(rig, isRight ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm, targets.Get(SkeletonJoint.BackShoulder), targets.Get(SkeletonJoint.BackElbow), targets.Get(SkeletonJoint.BackHand), true);
            AssertHinge(rig, isRight ? HumanBodyBones.RightUpperLeg : HumanBodyBones.LeftUpperLeg, targets.Get(SkeletonJoint.FrontHip), targets.Get(SkeletonJoint.FrontKnee), rig.GetBone(isRight ? HumanBodyBones.RightFoot : HumanBodyBones.LeftFoot).position.ToNumerics(), false);
            AssertHinge(rig, isRight ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg, targets.Get(SkeletonJoint.BackHip), targets.Get(SkeletonJoint.BackKnee), rig.GetBone(isRight ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot).position.ToNumerics(), false);
            foreach (var foot in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
            {
                var rest = RestRotation(foot);
                var up = (rig.GetBone(foot).rotation * Quaternion.Inverse(rest)) * Vector3.up;
                Assert.That(up.y, Is.GreaterThan(0.999f), $"{foot} sole down.");
            }
        }

        [Test]
        public void TheOpponentsFittedBonesAreScaledAndTheBonesBelowAreNot()
        {
            var replay = MirrorRecordingReplay.Run();
            var rig = CreateRig(DummyLayout.Create(replay.HeightMeters, replay.Hand), fitsLengths: true);
            var targets = new BodyRigTargets();
            foreach (var frame in replay.Frames)
            {
                OpponentRigTargets.Fill(targets, frame.View.Body, replay.HeightMeters, replay.Hand, frame.View.Controller, frame.TimeSeconds);
                rig.PoseTracked(targets);
            }

            var instanceScale = rig.Instance.transform.lossyScale.x;
            var upperArm = rig.GetBone(HumanBodyBones.RightUpperArm).lossyScale.x / instanceScale;
            var hand = rig.GetBone(HumanBodyBones.RightHand).lossyScale.x / instanceScale;
            var head = rig.GetBone(HumanBodyBones.Head).lossyScale.x / instanceScale;
            TestContext.WriteLine($"Opponent bone scales: upper arm {upperArm:0.00}, hand {hand:0.00}, head {head:0.00}.");
            Assert.That(upperArm, Is.EqualTo(0.226f / 0.270f).Within(0.03f), "The upper arm fitted to the tracked 22.6 cm.");
            Assert.That(hand, Is.EqualTo(1.0f).Within(1e-3f));
            Assert.That(head, Is.EqualTo(1.0f).Within(1e-3f));

            // The bones still sit on the drawn joints.
            var last = replay.Frames[replay.Frames.Count - 1].View.Body;
            Assert.That(Vector3.Distance(rig.GetBone(HumanBodyBones.RightLowerArm).position, last.Get(SkeletonJoint.SwordElbow).Value.ToUnity()), Is.LessThan(1e-3f));
        }

        [Test]
        public void PosingTheOpponentFromTheRecordingCostsLittle()
        {
            var replay = MirrorRecordingReplay.Run();
            var rig = CreateRig(DummyLayout.Create(replay.HeightMeters, replay.Hand), fitsLengths: true);
            var targets = new BodyRigTargets();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            foreach (var frame in replay.Frames)
            {
                OpponentRigTargets.Fill(targets, frame.View.Body, replay.HeightMeters, replay.Hand, frame.View.Controller, frame.TimeSeconds);
                rig.PoseTracked(targets);
            }

            watch.Stop();
            var microseconds = watch.Elapsed.TotalMilliseconds * 1000.0 / replay.Frames.Count;
            TestContext.WriteLine($"Opponent rig posing on the model: {microseconds:0.0} us per frame in the Editor.");
            Debug.Log($"[Opponent] Rig posing on the model (PER-142): {microseconds:0.0} us per frame in the Editor.");
            Assert.That(microseconds, Is.LessThan(500.0));
        }

        private DummyBodyRig CreateRig(DummyLayout layout, bool fitsLengths)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(BodyModelPath);
            _root = new GameObject("Rig test");
            var rig = DummyBodyRig.TryCreate(model, _root.transform, Resources.Load<Material>("VrFencingDummy"), layout, fitsLengths);
            Assert.That(rig, Is.Not.Null);
            return rig;
        }

        /// <summary>The bone's rest rotation in model space (the model's root at identity).</summary>
        private static Quaternion RestRotation(HumanBodyBones bone)
        {
            return ReadRest(bone).rotation;
        }

        private static Vector3 RestPosition(HumanBodyBones bone)
        {
            return ReadRest(bone).position;
        }

        private static Pose ReadRest(HumanBodyBones bone)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(BodyModelPath);
            var instance = Object.Instantiate(model);
            try
            {
                var boneTransform = FindBone(instance.GetComponent<Animator>(), bone);
                return new Pose(boneTransform.position, boneTransform.rotation);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>The elbow or knee hinge, carried by the posed upper bone from rest, against the plane of the three joints.</summary>
        private static void AssertHinge(DummyBodyRig rig, HumanBodyBones upperBone, System.Numerics.Vector3? rootJoint, System.Numerics.Vector3? middleJoint, System.Numerics.Vector3? endJoint, bool isArm)
        {
            var root = rootJoint.Value.ToUnity();
            var middle = middleJoint.Value.ToUnity();
            var end = endJoint.Value.ToUnity();
            if (Vector3.Angle(middle - root, end - middle) < BodyRigTuning.FullBendDegrees)
            {
                return;
            }

            var upper = rig.GetBone(upperBone);
            var restUpper = RestRotation(upperBone);
            var lowerBone = upperBone switch
            {
                HumanBodyBones.LeftUpperArm => HumanBodyBones.LeftLowerArm,
                HumanBodyBones.RightUpperArm => HumanBodyBones.RightLowerArm,
                HumanBodyBones.LeftUpperLeg => HumanBodyBones.LeftLowerLeg,
                _ => HumanBodyBones.RightLowerLeg,
            };
            var endBone = lowerBone switch
            {
                HumanBodyBones.LeftLowerArm => HumanBodyBones.LeftHand,
                HumanBodyBones.RightLowerArm => HumanBodyBones.RightHand,
                HumanBodyBones.LeftLowerLeg => HumanBodyBones.LeftFoot,
                _ => HumanBodyBones.RightFoot,
            };

            // The model's own rest bend where it has one (its arms rest bent), else forward for an elbow, back for a knee.
            var restDirection = RestPosition(lowerBone) - RestPosition(upperBone);
            var restLower = RestPosition(endBone) - RestPosition(lowerBone);
            var restHinge = Vector3.Angle(restDirection, restLower) >= BodyRigTuning.MinBendDegrees
                ? Vector3.Cross(restDirection, restLower)
                : Vector3.Cross(restDirection, isArm ? Vector3.forward : Vector3.back);
            var hinge = (upper.rotation * Quaternion.Inverse(restUpper)) * restHinge;
            Assert.That(Vector3.Angle(hinge, Vector3.Cross(middle - root, end - middle)), Is.LessThan(2.0f), upperBone.ToString());
        }

        private static Transform FindBone(Animator animator, HumanBodyBones bone)
        {
            var humanName = HumanTrait.BoneName[(int)bone];
            foreach (var human in animator.avatar.humanDescription.human)
            {
                if (human.humanName == humanName)
                {
                    foreach (var child in animator.GetComponentsInChildren<Transform>(true))
                    {
                        if (child.name == human.boneName)
                        {
                            return child;
                        }
                    }
                }
            }

            return null;
        }
    }
}
