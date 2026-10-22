using System;
using System.Numerics;
using VrFencing.Settings;

namespace VrFencing.Dummy
{
    /// <summary>What the Dummy's rigged body is posed on (PER-78, PER-142): its skeleton joints for a layout, in the Dummy frame.</summary>
    public static class DummyRigTargets
    {
        /// <summary>
        /// How far below the head's center the rig's head joint sits, as a fraction of height. On the Armature rig
        /// the head joint is about 3.4 cm below the eyes at 1.8 m, at the base of the skull.
        /// </summary>
        public const float HeadJointBelowHeadCenterPerHeight = 0.02f;

        /// <summary>Fills <paramref name="targets"/> (cleared first) with the Dummy's joints for <paramref name="layout"/>.</summary>
        public static void Fill(BodyRigTargets targets, DummyLayout layout)
        {
            if (targets == null)
            {
                throw new ArgumentNullException(nameof(targets));
            }

            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            targets.Clear();
            targets.Hand = layout.Hand;
            for (var index = 0; index < BodyRigTargets.JointCount; index++)
            {
                var joint = (SkeletonJoint)index;
                targets.Set(joint, DummySkeleton.GetJointPosition(layout, joint));
            }

            // The back hand carries on the forearm's line.
            var backElbow = DummySkeleton.GetJointPosition(layout, SkeletonJoint.BackElbow);
            var backHand = DummySkeleton.GetJointPosition(layout, SkeletonJoint.BackHand);
            targets.BackHandEnd = backHand + (backHand - backElbow);
            targets.HeadJoint = layout.Torso.HeadCenter - new Vector3(0.0f, HeadJointBelowHeadCenterPerHeight * layout.HeightMeters, 0.0f);

            // The head tilts with the leaning torso, a little less than the chest (PER-115).
            targets.HeadRotation = layout.Torso.HeadRotation;

            // The sword hand turns with the arm's wrist, holding the roll it had in en garde before PER-142.
            var wrist = layout.Arm.GetPose(ArmJoint.Wrist).Rotation;
            var palm = layout.Hand == Hand.Left ? new Vector3(-SwordPalmInWrist.X, SwordPalmInWrist.Y, SwordPalmInWrist.Z) : SwordPalmInWrist;
            targets.SwordPalm = Vector3.Transform(palm, wrist);
            targets.SwordFingers = Vector3.Transform(Vector3.UnitZ, wrist);
        }

        /// <summary>
        /// The right-handed Dummy's sword palm in its wrist's frame (+z along the hand): mostly down, a little toward the
        /// body, as the rig drew it in en garde with the arm in line before PER-142.
        /// </summary>
        public static readonly Vector3 SwordPalmInWrist = Vector3.Normalize(new Vector3(-0.3174f, -0.9483f, 0.0f));
    }
}
