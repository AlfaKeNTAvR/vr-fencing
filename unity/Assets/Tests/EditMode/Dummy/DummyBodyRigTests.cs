// The Dummy's rigged Humanoid body (PER-78): Movement SDK's Armature.fbx imports as a valid Humanoid, its bones
// are posed on the Dummy's en garde joints (the sword arm on the sword hand's side), it is drawn with the URP
// Dummy material instead of the capsule silhouette, and the Target Zones stay where the Core judges them.
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Tests.Dummy
{
    public class DummyBodyRigTests
    {
        private const string BodyModelPath = "Packages/com.meta.xr.sdk.movement/Shared/ThirdParty/Unity/Armature.fbx";
        private const string MaterialResourcePath = "VrFencingDummy";
        private const float Height = 1.65f;

        // Bones are placed exactly on their joints; this only absorbs float error through the transform hierarchy.
        private const float JointTolerance = 1e-3f;

        private GameObject _dummyObject;

        [TearDown]
        public void DestroyDummy()
        {
            if (_dummyObject != null)
            {
                Object.DestroyImmediate(_dummyObject);
            }
        }

        [Test]
        public void TheBodyModelIsAValidHumanoid()
        {
            var model = LoadBodyModel();

            var animator = model.GetComponent<Animator>();
            Assert.That(animator, Is.Not.Null);
            Assert.That(animator.avatar, Is.Not.Null);
            Assert.That(animator.avatar.isValid, Is.True);
            Assert.That(animator.avatar.isHuman, Is.True, "Armature.fbx must import with Animation Type Humanoid.");
        }

        [TestCase(Hand.Right)]
        [TestCase(Hand.Left)]
        public void TheBodyReplacesTheSilhouetteCapsulesAndUsesTheUrpMaterial(Hand hand)
        {
            var dummy = CreateDummyWithBody();

            dummy.Configure(Height, hand);

            var material = Resources.Load<Material>(MaterialResourcePath);
            Assert.That(_dummyObject.GetComponentsInChildren<MeshRenderer>(), Has.Length.EqualTo(dummy.Layout.Zones.Count), "Only the Target Zones stay capsules.");
            var bodyRenderers = _dummyObject.GetComponentsInChildren<SkinnedMeshRenderer>();
            Assert.That(bodyRenderers, Is.Not.Empty);
            foreach (var bodyRenderer in bodyRenderers)
            {
                Assert.That(bodyRenderer.sharedMaterials, Has.All.SameAs(material));
            }

            Assert.That(_dummyObject.GetComponentsInChildren<Collider>(), Is.Empty);
        }

        [TestCase(Hand.Right)]
        [TestCase(Hand.Left)]
        public void BonesSitOnTheSkeletonJoints(Hand hand)
        {
            var dummy = CreateDummyWithBody();
            dummy.Configure(Height, hand);
            dummy.SetDistanceMeters(2.0f);

            var sword = hand == Hand.Right;
            AssertBoneOnJoint(dummy, sword ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm, SkeletonJoint.SwordShoulder);
            AssertBoneOnJoint(dummy, sword ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm, SkeletonJoint.SwordElbow);
            AssertBoneOnJoint(dummy, sword ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand, SkeletonJoint.SwordWrist);
            AssertBoneOnJoint(dummy, sword ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm, SkeletonJoint.BackShoulder);
            AssertBoneOnJoint(dummy, sword ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand, SkeletonJoint.BackHand);
            AssertBoneOnJoint(dummy, sword ? HumanBodyBones.RightUpperLeg : HumanBodyBones.LeftUpperLeg, SkeletonJoint.FrontHip);
            AssertBoneOnJoint(dummy, sword ? HumanBodyBones.RightLowerLeg : HumanBodyBones.LeftLowerLeg, SkeletonJoint.FrontKnee);
            AssertBoneOnJoint(dummy, sword ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg, SkeletonJoint.BackHip);
            AssertBoneOnJoint(dummy, sword ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg, SkeletonJoint.BackKnee);
        }

        [Test]
        public void TheSwordForearmPointsAtTheWrist()
        {
            var dummy = CreateDummyWithBody();
            dummy.Configure(Height, Hand.Right);

            var lowerArm = FindBone(HumanBodyBones.RightLowerArm);
            var hand = FindBone(HumanBodyBones.RightHand);
            var expected = (JointInWorld(dummy, SkeletonJoint.SwordWrist) - JointInWorld(dummy, SkeletonJoint.SwordElbow)).normalized;

            Assert.That(Vector3.Dot((hand.position - lowerArm.position).normalized, expected), Is.EqualTo(1.0f).Within(JointTolerance));
        }

        [Test]
        public void PosingTheArmMovesTheSwordHandBone()
        {
            var dummy = CreateDummyWithBody();
            dummy.Configure(Height, Hand.Right);
            var handBefore = FindBone(HumanBodyBones.RightHand).position;

            dummy.SetArmPose(SwordArmPose.ArmInLine.WithElbowBend(0.6f));

            Assert.That(Vector3.Distance(FindBone(HumanBodyBones.RightHand).position, handBefore), Is.GreaterThan(0.05f));
            AssertBoneOnJoint(dummy, HumanBodyBones.RightHand, SkeletonJoint.SwordWrist);
        }

        [Test]
        public void ZonesStayWhereTheCoreJudgesThem()
        {
            var dummy = CreateDummyWithBody();
            dummy.Configure(Height, Hand.Right);
            dummy.SetDistanceMeters(2.0f);

            foreach (var zone in dummy.GetZonesOnStrip())
            {
                var part = FindChild($"Zone {zone.Zone}");
                Assert.That(part, Is.Not.Null, zone.Zone.ToString());
                Assert.That(Vector3.Distance(part.position, zone.Shape.Center.ToUnity()), Is.LessThan(1e-4f), zone.Zone.ToString());
            }
        }

        [TestCase(Hand.Right)]
        [TestCase(Hand.Left)]
        public void SteppingLegsMoveTheLegBonesAndTheZonesWithThem(Hand hand)
        {
            var dummy = CreateDummyWithBody();
            dummy.Configure(Height, hand);
            var legs = SteppedLegs(hand);
            var frontFoot = hand == Hand.Right ? HumanBodyBones.RightFoot : HumanBodyBones.LeftFoot;
            var footBefore = FindBone(frontFoot).position;

            dummy.SetLegs(legs);

            Assert.That(Vector3.Distance(FindBone(frontFoot).position, footBefore), Is.GreaterThan(0.02f));
            AssertBoneOnJoint(dummy, hand == Hand.Right ? HumanBodyBones.RightLowerLeg : HumanBodyBones.LeftLowerLeg, SkeletonJoint.FrontKnee);
            AssertBoneOnJoint(dummy, hand == Hand.Right ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg, SkeletonJoint.BackKnee);
            foreach (var zone in dummy.GetZonesOnStrip())
            {
                var part = FindChild($"Zone {zone.Zone}");
                Assert.That(Vector3.Distance(part.position, zone.Shape.Center.ToUnity()), Is.LessThan(1e-4f), zone.Zone.ToString());
            }
        }

        [Test]
        public void PosingTheStepDummyEveryFrameCostsLittle()
        {
            var dummy = CreateDummyWithBody();
            dummy.Configure(Height, Hand.Right);
            dummy.SetDistanceMeters(2.0f);
            var footwork = dummy.Footwork;
            var lunge = ArmedDummyAttack.GetLungeMeters(Height);
            const int frames = 2000;

            // Steps back and forth, then lunges and recovers, as the distance keeper and the Armed Dummy drive it.
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (var frame = 0; frame < frames; frame++)
            {
                if (frame < frames / 2)
                {
                    footwork.Step((frame / 40) % 2 == 0 ? 1.5f : 2.5f, 1.0f / 72.0f);
                }
                else
                {
                    var phase = (frame % 144) / 72.0f;
                    footwork.StepLunge(lunge * (phase < 1.0f ? phase : 2.0f - phase), lunge, 1.0f / 72.0f);
                }

                dummy.ApplyFootwork();
            }

            watch.Stop();
            var microsecondsPerFrame = watch.Elapsed.TotalMilliseconds * 1000.0 / frames;
            TestContext.WriteLine($"Footwork plus Dummy posing (body rig), steps and lunges: {microsecondsPerFrame:0.0} us per frame.");
            Debug.Log($"[Dummy] Footwork plus Dummy posing (body rig): {microsecondsPerFrame:0.0} us per frame in the Editor.");

            // A 72 Hz frame is 13.9 ms. Loose: the Editor is slower than a build, the Quest slower than this laptop.
            Assert.That(microsecondsPerFrame, Is.LessThan(500.0));
        }

        [Test]
        public void AModelThatIsNotAHumanoidFallsBackToTheCapsuleSilhouette()
        {
            var notHumanoid = new GameObject("Not a humanoid");
            try
            {
                var dummy = CreateDummy(notHumanoid);
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(@"\[Dummy\] The body model 'Not a humanoid' cannot be used"));

                dummy.Configure(Height, Hand.Right);

                Assert.That(_dummyObject.GetComponentsInChildren<MeshRenderer>(), Has.Length.EqualTo(dummy.Layout.Zones.Count + dummy.Layout.SilhouetteParts.Count));
                Assert.That(_dummyObject.GetComponentsInChildren<SkinnedMeshRenderer>(), Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(notHumanoid);
            }
        }

        /// <summary>The legs a few frames into an advance, when the front foot is up and forward.</summary>
        private static DummyLegs SteppedLegs(Hand hand)
        {
            var footwork = new DummyFootwork(Height, hand, 2.0f, new System.Random(1));
            for (var frame = 0; frame < 10; frame++)
            {
                footwork.Step(1.6f, 1.0f / 72.0f);
            }

            return footwork.Legs;
        }

        private void AssertBoneOnJoint(DummyBehaviour dummy, HumanBodyBones bone, SkeletonJoint joint)
        {
            var boneTransform = FindBone(bone);
            Assert.That(boneTransform, Is.Not.Null, bone.ToString());
            Assert.That(Vector3.Distance(boneTransform.position, JointInWorld(dummy, joint)), Is.LessThan(JointTolerance), $"{bone} on {joint}");
        }

        private static Vector3 JointInWorld(DummyBehaviour dummy, SkeletonJoint joint)
        {
            // The Dummy's parts hang under it at identity, so its transform is the Dummy frame.
            return dummy.transform.TransformPoint(DummySkeleton.GetJointPosition(dummy.Layout, joint).ToUnity());
        }

        private Transform FindBone(HumanBodyBones bone)
        {
            var boneName = FindBoneName(bone);
            return boneName == null ? null : FindChild(boneName);
        }

        private static string FindBoneName(HumanBodyBones bone)
        {
            var humanName = HumanTrait.BoneName[(int)bone];
            foreach (var human in LoadBodyModel().GetComponent<Animator>().avatar.humanDescription.human)
            {
                if (human.humanName == humanName)
                {
                    return human.boneName;
                }
            }

            return null;
        }

        private Transform FindChild(string childName)
        {
            foreach (var child in _dummyObject.GetComponentsInChildren<Transform>())
            {
                if (child.name == childName)
                {
                    return child;
                }
            }

            return null;
        }

        private static GameObject LoadBodyModel()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(BodyModelPath);
            Assert.That(model, Is.Not.Null, $"No model at {BodyModelPath}.");
            return model;
        }

        private DummyBehaviour CreateDummyWithBody()
        {
            return CreateDummy(LoadBodyModel());
        }

        private DummyBehaviour CreateDummy(GameObject bodyModel)
        {
            _dummyObject = new GameObject("Test Dummy");
            var dummy = _dummyObject.AddComponent<DummyBehaviour>();
            var serialized = new SerializedObject(dummy);
            serialized.FindProperty("_bodyModel").objectReferenceValue = bodyModel;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return dummy;
        }
    }
}
