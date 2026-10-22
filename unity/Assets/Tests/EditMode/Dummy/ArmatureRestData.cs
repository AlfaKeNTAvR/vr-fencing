// The rest pose of Movement SDK's Armature.fbx (the rig the Dummy and the opponent are drawn with) as Unity imports it,
// so the rig posing can be tested without Unity (PER-142); OpponentRigUnityTests checks it against the model. Model space,
// meters, before the rig is scaled to a height, facing +z: legs straight, arms hanging out and down with the elbows bent
// and the hands forward, palms in and down.
using System.Collections.Generic;
using System.Numerics;
using VrFencing.Dummy;

namespace VrFencing.Tests.Dummy
{
    public static class ArmatureRestData
    {
        /// <summary>The rig's rest shoulder height, which the Dummy scales to its standing shoulder height.</summary>
        public const float RestShoulderHeight = 1.42306f;

        private static readonly Dictionary<RigBone, (Vector3 Position, Quaternion Rotation)> Bones = new Dictionary<RigBone, (Vector3, Quaternion)>
        {
            { RigBone.Hips, (new Vector3(0.00000f, 0.98110f, -0.01590f), new Quaternion(0.00000f, 0.00000f, 0.00000f, 1.00000f)) },
            { RigBone.Chest, (new Vector3(0.00000f, 1.14273f, -0.01468f), new Quaternion(0.00000f, 0.00000f, 0.00000f, 1.00000f)) },
            { RigBone.UpperChest, (new Vector3(0.00000f, 1.24614f, -0.01468f), new Quaternion(0.00000f, 0.00000f, 0.00000f, 1.00000f)) },
            { RigBone.Neck, (new Vector3(0.00000f, 1.49718f, -0.03001f), new Quaternion(0.06069f, 0.00000f, 0.00000f, 0.99816f)) },
            { RigBone.Head, (new Vector3(0.00000f, 1.62372f, -0.01457f), new Quaternion(0.00000f, 0.00000f, 0.00000f, 1.00000f)) },
            { RigBone.LeftUpperArm, (new Vector3(-0.16345f, 1.42306f, -0.06107f), new Quaternion(0.07169f, -0.00416f, 0.93371f, 0.35076f)) },
            { RigBone.LeftLowerArm, (new Vector3(-0.35035f, 1.20813f, -0.04895f), new Quaternion(0.19772f, 0.34550f, 0.86744f, 0.29845f)) },
            { RigBone.LeftHand, (new Vector3(-0.44197f, 1.06798f, 0.12349f), new Quaternion(0.39042f, 0.21955f, 0.81393f, 0.36998f)) },
            { RigBone.RightUpperArm, (new Vector3(0.16345f, 1.42306f, -0.06107f), new Quaternion(0.35076f, -0.93371f, -0.00416f, -0.07169f)) },
            { RigBone.RightLowerArm, (new Vector3(0.35035f, 1.20813f, -0.04895f), new Quaternion(0.29845f, -0.86744f, 0.34550f, -0.19772f)) },
            { RigBone.RightHand, (new Vector3(0.44197f, 1.06797f, 0.12350f), new Quaternion(0.36998f, -0.81393f, 0.21955f, -0.39042f)) },
            { RigBone.LeftUpperLeg, (new Vector3(-0.08610f, 0.92764f, -0.02738f), new Quaternion(0.99984f, -0.01775f, 0.00005f, -0.00261f)) },
            { RigBone.LeftLowerLeg, (new Vector3(-0.10078f, 0.51456f, -0.02953f), new Quaternion(0.99917f, -0.01774f, 0.00065f, -0.03665f)) },
            { RigBone.LeftFoot, (new Vector3(-0.11544f, 0.10190f, -0.05986f), new Quaternion(0.99872f, 0.00000f, 0.05065f, 0.00000f)) },
            { RigBone.RightUpperLeg, (new Vector3(0.08610f, 0.92764f, -0.02738f), new Quaternion(0.00261f, 0.00005f, 0.01775f, 0.99984f)) },
            { RigBone.RightLowerLeg, (new Vector3(0.10078f, 0.51456f, -0.02953f), new Quaternion(0.03665f, 0.00065f, 0.01774f, 0.99917f)) },
            { RigBone.RightFoot, (new Vector3(0.11544f, 0.10190f, -0.05986f), new Quaternion(-0.00000f, 0.05065f, -0.00000f, 0.99872f)) },
        };

        private static readonly Vector3 Eyes = new Vector3(0.00000f, 1.65832f, 0.07217f);
        private static readonly Vector3 LeftMiddleFinger = new Vector3(-0.48173f, 1.02392f, 0.18727f);
        private static readonly Vector3 LeftIndexFinger = new Vector3(-0.46362f, 1.01800f, 0.20246f);
        private static readonly Vector3 LeftLittleFinger = new Vector3(-0.49866f, 1.02349f, 0.15512f);
        private static readonly Vector3 RightMiddleFinger = new Vector3(0.48173f, 1.02392f, 0.18728f);
        private static readonly Vector3 RightIndexFinger = new Vector3(0.46362f, 1.01800f, 0.20246f);
        private static readonly Vector3 RightLittleFinger = new Vector3(0.49867f, 1.02349f, 0.15512f);

        /// <summary>The rest pose scaled as the Dummy scales its rig for <paramref name="heightMeters"/> (DummyBodyRig.ScaleToHeight).</summary>
        public static BodyRigRest Create(float heightMeters)
        {
            var scale = DummySkeleton.StandingShoulderHeightPerHeight * heightMeters / RestShoulderHeight;
            var rest = new BodyRigRest
            {
                Eyes = Eyes * scale,
                LeftMiddleFinger = LeftMiddleFinger * scale,
                RightMiddleFinger = RightMiddleFinger * scale,
                LeftPalm = BodyRigRest.GetPalm(LeftMiddleFinger - Bones[RigBone.LeftHand].Position, LeftIndexFinger - LeftLittleFinger, true),
                RightPalm = BodyRigRest.GetPalm(RightMiddleFinger - Bones[RigBone.RightHand].Position, RightIndexFinger - RightLittleFinger, false),
            };
            foreach (var bone in Bones)
            {
                rest.Set(bone.Key, bone.Value.Position * scale, bone.Value.Rotation);
            }

            return rest;
        }
    }
}
