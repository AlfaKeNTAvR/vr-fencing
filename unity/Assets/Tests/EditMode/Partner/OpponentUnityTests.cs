// The Unity side of the opponent (PER-81): the Core's Full Body indices are OVRPlugin's, the rig's mesh parts are found
// by the model's own material names, and the rig takes a tracked body.
using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VrFencing.Dummy;
using VrFencing.Partner;
using VrFencing.Settings;
using Object = UnityEngine.Object;

namespace VrFencing.Tests.Partner
{
    public class OpponentUnityTests
    {
        private const string BodyModelPath = "Packages/com.meta.xr.sdk.movement/Shared/ThirdParty/Unity/Armature.fbx";

        [TestCase(FullBodyJoints.Hips, OVRPlugin.BoneId.FullBody_Hips)]
        [TestCase(FullBodyJoints.Head, OVRPlugin.BoneId.FullBody_Head)]
        [TestCase(FullBodyJoints.LeftArmUpper, OVRPlugin.BoneId.FullBody_LeftArmUpper)]
        [TestCase(FullBodyJoints.LeftArmLower, OVRPlugin.BoneId.FullBody_LeftArmLower)]
        [TestCase(FullBodyJoints.RightArmUpper, OVRPlugin.BoneId.FullBody_RightArmUpper)]
        [TestCase(FullBodyJoints.RightArmLower, OVRPlugin.BoneId.FullBody_RightArmLower)]
        [TestCase(FullBodyJoints.LeftHandWrist, OVRPlugin.BoneId.FullBody_LeftHandWrist)]
        [TestCase(FullBodyJoints.LeftHandMiddleProximal, OVRPlugin.BoneId.FullBody_LeftHandMiddleProximal)]
        [TestCase(FullBodyJoints.RightHandWrist, OVRPlugin.BoneId.FullBody_RightHandWrist)]
        [TestCase(FullBodyJoints.RightHandMiddleProximal, OVRPlugin.BoneId.FullBody_RightHandMiddleProximal)]
        [TestCase(FullBodyJoints.LeftUpperLeg, OVRPlugin.BoneId.FullBody_LeftUpperLeg)]
        [TestCase(FullBodyJoints.LeftLowerLeg, OVRPlugin.BoneId.FullBody_LeftLowerLeg)]
        [TestCase(FullBodyJoints.LeftFootAnkle, OVRPlugin.BoneId.FullBody_LeftFootAnkle)]
        [TestCase(FullBodyJoints.LeftFootBall, OVRPlugin.BoneId.FullBody_LeftFootBall)]
        [TestCase(FullBodyJoints.RightUpperLeg, OVRPlugin.BoneId.FullBody_RightUpperLeg)]
        [TestCase(FullBodyJoints.RightLowerLeg, OVRPlugin.BoneId.FullBody_RightLowerLeg)]
        [TestCase(FullBodyJoints.RightFootAnkle, OVRPlugin.BoneId.FullBody_RightFootAnkle)]
        [TestCase(FullBodyJoints.RightFootBall, OVRPlugin.BoneId.FullBody_RightFootBall)]
        [TestCase(FullBodyJoints.Count, OVRPlugin.BoneId.FullBody_End)]
        public void TheFullBodyIndexIsOvrPlugins(int coreIndex, OVRPlugin.BoneId boneId)
        {
            Assert.That(coreIndex, Is.EqualTo((int)boneId));
        }

        [TestCase("M_Arms", RigMeshPart.Arms)]
        [TestCase("M_Legs", RigMeshPart.Legs)]
        [TestCase("M_Body", RigMeshPart.Body)]
        [TestCase("Armature_Mesh", RigMeshPart.Body)]
        [TestCase(null, RigMeshPart.Body)]
        public void TheModelsMaterialNamesItsPart(string name, RigMeshPart part)
        {
            Assert.That(RigMeshSlot.GetPart(name), Is.EqualTo(part));
        }

        [Test]
        public void TheArmatureRigHasItsArmsAndLegsAsSeparateSlots()
        {
            var rig = CreateRig(out var root);
            try
            {
                Assert.That(rig.MeshSlots, Has.Some.Matches<RigMeshSlot>(slot => slot.Part == RigMeshPart.Legs), "The legs must fade on their own (PER-59).");
                Assert.That(rig.MeshSlots, Has.Some.Matches<RigMeshSlot>(slot => slot.Part == RigMeshPart.Body));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TheRigTakesATrackedBodyAndKeepsAPartWhoseJointsAreMissing()
        {
            var rig = CreateRig(out var root);
            try
            {
                var layout = DummyLayout.Create(1.7f, Hand.Right);
                var moved = new Vector3(0.0f, 0.0f, 0.3f);

                // Every joint 30 cm further along +z, but the back leg's knee missing: that leg is skipped, not thrown on.
                rig.PoseTracked(
                    Hand.Right,
                    1.7f,
                    joint => joint == SkeletonJoint.BackKnee ? (Vector3?)null : DummySkeleton.GetJointPosition(layout, joint).ToUnity() + moved,
                    null,
                    Quaternion.identity);

                var rightHand = rig.GetBone(HumanBodyBones.RightHand).position;
                var expectedWrist = DummySkeleton.GetJointPosition(layout, SkeletonJoint.SwordWrist).ToUnity() + moved;
                Assert.That(Vector3.Distance(rightHand, expectedWrist), Is.LessThan(1e-3f), "The sword (right) hand sits on the tracked wrist.");
                var frontAnkle = DummySkeleton.GetJointPosition(layout, SkeletonJoint.FrontAnkle).ToUnity() + moved;
                Assert.That(Mathf.Abs(rig.GetBone(HumanBodyBones.RightFoot).position.z - frontAnkle.z), Is.LessThan(1e-3f), "The front (right) foot follows its tracked ankle.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static DummyBodyRig CreateRig(out GameObject root)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(BodyModelPath);
            Assert.That(model, Is.Not.Null);
            root = new GameObject("Opponent test");
            var material = Resources.Load<Material>("VrFencingDummy");
            var rig = DummyBodyRig.TryCreate(model, root.transform, material, DummyLayout.Create(1.7f, Hand.Right));
            Assert.That(rig, Is.Not.Null);
            return rig;
        }
    }
}
