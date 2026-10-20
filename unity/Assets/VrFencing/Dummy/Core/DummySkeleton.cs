using System;
using System.Numerics;

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
    /// for a left-handed Dummy. The sword arm joints follow the layout's <see cref="SwordArm"/> pose, the leg joints
    /// its <see cref="DummyLayout.Legs"/> (PER-110) and the rest its <see cref="DummyLayout.Torso"/> (PER-115), at the ends
    /// of the capsules the Target Zones and the silhouette are built from.
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
                case SkeletonJoint.HipCenter:
                    return layout.Torso.HipCenter;
                case SkeletonJoint.ShoulderCenter:
                    return layout.Torso.ShoulderCenter;
                case SkeletonJoint.HeadCenter:
                    return layout.Torso.HeadCenter;
                case SkeletonJoint.BackShoulder:
                    return layout.Torso.BackShoulder;
                case SkeletonJoint.BackElbow:
                    return layout.Torso.BackElbow;
                case SkeletonJoint.BackHand:
                    return layout.Torso.BackHand;
                case SkeletonJoint.FrontHip:
                    return layout.Legs.Front.Hip;
                case SkeletonJoint.FrontKnee:
                    return layout.Legs.Front.Knee;
                case SkeletonJoint.FrontAnkle:
                    return layout.Legs.Front.Ankle;
                case SkeletonJoint.FrontToe:
                    return layout.Legs.Front.Toe;
                case SkeletonJoint.BackHip:
                    return layout.Legs.Back.Hip;
                case SkeletonJoint.BackKnee:
                    return layout.Legs.Back.Knee;
                case SkeletonJoint.BackAnkle:
                    return layout.Legs.Back.Ankle;
                case SkeletonJoint.BackToe:
                    return layout.Legs.Back.Toe;
                default:
                    throw new ArgumentOutOfRangeException(nameof(joint), joint, "Not a skeleton joint.");
            }
        }
    }
}
