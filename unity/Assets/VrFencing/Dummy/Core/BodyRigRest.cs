using System;
using System.Numerics;

namespace VrFencing.Dummy
{
    /// <summary>
    /// The bones of a Humanoid body rig that <see cref="BodyRigPoser"/> poses (PER-142), in parent-first order: every
    /// bone comes after the bones it hangs from, so a caller that applies them in this order sets each parent first.
    /// Left and right are anatomical.
    /// </summary>
    public enum RigBone
    {
        Hips,
        Chest,
        UpperChest,
        Neck,
        Head,
        LeftUpperArm,
        LeftLowerArm,
        LeftHand,
        RightUpperArm,
        RightLowerArm,
        RightHand,
        LeftUpperLeg,
        LeftLowerLeg,
        LeftFoot,
        RightUpperLeg,
        RightLowerLeg,
        RightFoot,
    }

    /// <summary>
    /// A rig's rest pose (PER-142): each bone's position and rotation in the frame the rig is posed in (the parts frame),
    /// after the rig is scaled to its height, plus the rest-only points the poser needs (the middle fingers' base and the
    /// eyes, the palms). The rig stands facing +z, +y up, +x its right; its arms may rest in any pose (Movement SDK's
    /// <c>Armature.fbx</c> rests with them hanging out and down, elbows bent, hands forward). A bone or point the model does
    /// not have is left unset.
    /// </summary>
    public sealed class BodyRigRest
    {
        public static readonly int BoneCount = Enum.GetValues(typeof(RigBone)).Length;

        private readonly Vector3[] _positions = new Vector3[BoneCount];
        private readonly Quaternion[] _rotations = new Quaternion[BoneCount];
        private readonly bool[] _has = new bool[BoneCount];

        /// <summary>The left middle finger's first joint, which the hand points at; null when the model has no fingers.</summary>
        public Vector3? LeftMiddleFinger { get; set; }

        /// <inheritdoc cref="LeftMiddleFinger"/>
        public Vector3? RightMiddleFinger { get; set; }

        /// <summary>
        /// The left palm's outward normal at rest (from the knuckles); null when the model has no fingers, and the hand is
        /// then taken to rest palm down.
        /// </summary>
        public Vector3? LeftPalm { get; set; }

        /// <inheritdoc cref="LeftPalm"/>
        public Vector3? RightPalm { get; set; }

        /// <summary>
        /// The outward palm normal of a hand from its knuckles: the line from wrist to middle knuckle and the line from the
        /// little to the index knuckle (down for a hand of either side held out palm down).
        /// </summary>
        public static Vector3 GetPalm(Vector3 fingers, Vector3 littleToIndex, bool isLeft)
        {
            return Vector3.Normalize(isLeft ? Vector3.Cross(littleToIndex, fingers) : Vector3.Cross(fingers, littleToIndex));
        }

        /// <summary>Midway between the eyes; null when the model has no eye bones.</summary>
        public Vector3? Eyes { get; set; }

        public void Set(RigBone bone, Vector3 position, Quaternion rotation)
        {
            _positions[(int)bone] = position;
            _rotations[(int)bone] = Quaternion.Normalize(rotation);
            _has[(int)bone] = true;
        }

        public bool Has(RigBone bone)
        {
            return _has[(int)bone];
        }

        public Vector3 GetPosition(RigBone bone)
        {
            Require(bone);
            return _positions[(int)bone];
        }

        public Quaternion GetRotation(RigBone bone)
        {
            Require(bone);
            return _rotations[(int)bone];
        }

        /// <summary>
        /// The bones every pose needs: the hips, chest and head, and the upper and lower limb bones with the hands and
        /// feet. The upper chest and the neck are optional.
        /// </summary>
        public bool HasRequiredBones()
        {
            for (var index = 0; index < BoneCount; index++)
            {
                var bone = (RigBone)index;
                if (bone != RigBone.UpperChest && bone != RigBone.Neck && !_has[index])
                {
                    return false;
                }
            }

            return true;
        }

        private void Require(RigBone bone)
        {
            if (!_has[(int)bone])
            {
                throw new InvalidOperationException($"The rig has no {bone} bone.");
            }
        }
    }

    /// <summary>One bone as posed: position and rotation in the parts frame, and its size relative to its rest size.</summary>
    public struct RigBonePose
    {
        public RigBonePose(Vector3 position, Quaternion rotation, float scale)
        {
            Position = position;
            Rotation = rotation;
            Scale = scale;
        }

        public Vector3 Position { get; }

        public Quaternion Rotation { get; }

        /// <summary>
        /// The bone's uniform size over its rest size (1: as rest), absolute, not relative to its parent. A bone fitted to
        /// a tracked length is longer or shorter (and a little thicker or thinner) by this much.
        /// </summary>
        public float Scale { get; }
    }
}
