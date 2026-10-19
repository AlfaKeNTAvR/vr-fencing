using System;
using System.Numerics;
using VrFencing.Settings;

namespace VrFencing.Dummy
{
    /// <summary>
    /// A joint of the Dummy's body, named by fencing side: the sword arm and the front leg are on the sword hand's
    /// side, the back arm and the back leg on the other.
    /// </summary>
    public enum SkeletonJoint
    {
        HipCenter,
        ShoulderCenter,
        HeadCenter,
        SwordShoulder,
        SwordElbow,
        SwordWrist,
        SwordHandEnd,
        BackShoulder,
        BackElbow,
        BackHand,
        FrontHip,
        FrontKnee,
        FrontAnkle,
        FrontToe,
        BackHip,
        BackKnee,
        BackAnkle,
        BackToe,
    }

    /// <summary>
    /// The joints a rigged body is posed on, so its mesh wraps the same en garde stance as the Target Zones
    /// (PER-78). Positions are in the Dummy frame (see <see cref="DummyLayout"/>), scaled to its height and mirrored
    /// for a left-handed Dummy. The sword arm joints follow the layout's <see cref="SwordArm"/> pose; the rest are
    /// fixed to the stance, at the ends of the capsules the Target Zones and the silhouette are built from.
    /// </summary>
    public static class DummySkeleton
    {
        /// <summary>
        /// Standing shoulder height as a fraction of height (Winter 2009, see <see cref="EnGardePose"/>): what a rigged
        /// body standing in its rest pose is scaled by, so its limbs come out about as thick as the Dummy's.
        /// </summary>
        public const float StandingShoulderHeightPerHeight = 0.818f;

        /// <summary>The joint's position in the Dummy frame for <paramref name="layout"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="joint"/> is not a skeleton joint.</exception>
        public static Vector3 GetJointPosition(DummyLayout layout, SkeletonJoint joint)
        {
            switch (joint)
            {
                case SkeletonJoint.SwordShoulder:
                    return layout.Arm.GetPose(ArmJoint.Shoulder).Position;
                case SkeletonJoint.SwordElbow:
                    return layout.Arm.GetPose(ArmJoint.Elbow).Position;
                case SkeletonJoint.SwordWrist:
                    return layout.Arm.GetPose(ArmJoint.Wrist).Position;
                case SkeletonJoint.SwordHandEnd:
                    var wrist = layout.Arm.GetPose(ArmJoint.Wrist);
                    var handAlongWrist = new Vector3(0.0f, 0.0f, EnGardePose.HandLength * layout.HeightMeters);
                    return wrist.Position + Vector3.Transform(handAlongWrist, wrist.Rotation);
                default:
                    var position = GetStanceJointAtUnitHeight(joint) * layout.HeightMeters;
                    return layout.Hand == Hand.Left ? HandMirroring.MirrorPosition(position) : position;
            }
        }

        /// <summary>A joint that stays with the stance, for a right-handed Dummy of height 1.</summary>
        private static Vector3 GetStanceJointAtUnitHeight(SkeletonJoint joint)
        {
            switch (joint)
            {
                case SkeletonJoint.HipCenter:
                    return (EnGardePose.FrontHip + EnGardePose.BackHip) * 0.5f;
                case SkeletonJoint.ShoulderCenter:
                    return (EnGardePose.SwordShoulder + EnGardePose.BackShoulder) * 0.5f;
                case SkeletonJoint.HeadCenter:
                    return EnGardePose.HeadCenter;
                case SkeletonJoint.BackShoulder:
                    return EnGardePose.BackShoulder;
                case SkeletonJoint.BackElbow:
                    return EnGardePose.BackElbow;
                case SkeletonJoint.BackHand:
                    return EnGardePose.BackHand;
                case SkeletonJoint.FrontHip:
                    return EnGardePose.FrontHip;
                case SkeletonJoint.FrontKnee:
                    return EnGardePose.FrontKnee;
                case SkeletonJoint.FrontAnkle:
                    return EnGardePose.FrontAnkle;
                case SkeletonJoint.FrontToe:
                    return EnGardePose.FrontToe;
                case SkeletonJoint.BackHip:
                    return EnGardePose.BackHip;
                case SkeletonJoint.BackKnee:
                    return EnGardePose.BackKnee;
                case SkeletonJoint.BackAnkle:
                    return EnGardePose.BackAnkle;
                case SkeletonJoint.BackToe:
                    return EnGardePose.BackToe;
                default:
                    throw new ArgumentOutOfRangeException(nameof(joint), joint, "Not a stance skeleton joint.");
            }
        }
    }
}
