using System;
using System.Numerics;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Partner
{
    /// <summary>
    /// How a tracked hand is turned (PER-142), from the turn of what holds it: the sword hand from its controller (the
    /// same pose the blade is drawn from, so hand and hilt turn together), the back hand from the body tracker's wrist
    /// joint (<see cref="FencerBody.BackWristRotation"/>). A hand is given as its line (wrist to middle knuckle) and its
    /// palm, the outward normal (down for a hand held out palm down). Measured on the recording <c>fencer-20261022-164920</c>: the
    /// tracker holds its hands rigid to the controller (2.5 degrees median spread) and to its own wrist joints (none).
    /// </summary>
    public static class HandRoll
    {
        /// <summary>The right palm in the right controller's frame: it faces the controller's left (-x), a little down.</summary>
        public static readonly Vector3 RightPalmInController = Vector3.Normalize(new Vector3(-0.997f, -0.075f, 0.0f));

        /// <summary>The right hand's line (wrist to middle knuckle) in the right controller's frame: forward (+z), a little down.</summary>
        public static readonly Vector3 RightFingersInController = Vector3.Normalize(new Vector3(0.016f, -0.175f, 0.985f));

        /// <summary>The right palm in the tracker's right wrist joint frame.</summary>
        public static readonly Vector3 RightPalmInWrist = Vector3.Normalize(new Vector3(0.034f, -0.995f, 0.089f));

        /// <summary>The right hand's line in the tracker's right wrist joint frame: its +x.</summary>
        public static readonly Vector3 RightFingersInWrist = Vector3.Normalize(new Vector3(0.999f, 0.036f, 0.018f));

        /// <summary>
        /// The hand holding a controller turned by <paramref name="controllerRotation"/>, as its line (wrist to middle
        /// knuckle) and palm; a left hand is the right one mirrored.
        /// </summary>
        public static (Vector3 Fingers, Vector3 Palm) GetHandFromController(Quaternion controllerRotation, Hand hand)
        {
            return (
                Vector3.Transform(ForHand(RightFingersInController, hand), controllerRotation),
                Vector3.Transform(ForHand(RightPalmInController, hand), controllerRotation));
        }

        /// <summary>
        /// The tracker's <paramref name="isLeft"/> hand whose wrist joint is turned by <paramref name="wristRotation"/>, as
        /// its line and palm. The tracker's left wrist frame is its right one with every axis reversed.
        /// </summary>
        public static (Vector3 Fingers, Vector3 Palm) GetHandFromWrist(Quaternion wristRotation, bool isLeft)
        {
            var sign = isLeft ? -1.0f : 1.0f;
            return (Vector3.Transform(RightFingersInWrist * sign, wristRotation), Vector3.Transform(RightPalmInWrist * sign, wristRotation));
        }

        private static Vector3 ForHand(Vector3 right, Hand hand)
        {
            return hand == Hand.Left ? new Vector3(-right.X, right.Y, right.Z) : right;
        }
    }

    /// <summary>
    /// What the opponent's rig is posed on (PER-142): the drawn body's joints, its hands' palms (<see cref="HandRoll"/>)
    /// and its head. The rig's head goes where the tracked head is, by the rig's own eyes: the tracker's head joint sits
    /// lower and further back in the head than the rig's (by 9.4 and 5.7 cm at 1.65 m, measured), so the head is placed
    /// by the eyes, which both share, and the neck reaches it. Every input is in this Fencer's Strip frame, as drawn.
    /// </summary>
    public static class OpponentRigTargets
    {
        /// <summary>
        /// The tracker's head joint in the headset's frame (+y up, +z where it looks), per meter of height: 9.4 cm under
        /// and 5.7 cm behind the eyes at 1.65 m (recording <c>fencer-20261022-164920</c>, spread about 1 cm).
        /// </summary>
        public static readonly Vector3 TrackerHeadJointInHeadsetPerHeight = new Vector3(0.0f, -0.0567f, -0.0347f);

        /// <summary>
        /// Fills <paramref name="targets"/> (cleared first) from <paramref name="body"/>, for a Fencer of
        /// <paramref name="heightMeters"/> holding the blade in <paramref name="hand"/>. <paramref name="swordController"/>
        /// is the drawn controller (its turn rolls the sword hand), null when lost.
        /// </summary>
        public static void Fill(BodyRigTargets targets, FencerBody body, float heightMeters, Hand hand, TrackedPose? swordController, double timeSeconds)
        {
            if (targets == null)
            {
                throw new ArgumentNullException(nameof(targets));
            }

            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            targets.Clear();
            targets.Hand = hand;
            targets.TimeSeconds = timeSeconds;
            for (var index = 0; index < BodyRigTargets.JointCount; index++)
            {
                targets.Set((SkeletonJoint)index, body.Get(index));
            }

            targets.BackHandEnd = body.BackHandEnd;
            targets.HeadRotation = body.HeadRotation;
            targets.Eyes = GetEyes(body, heightMeters);
            targets.HeadJoint = GetTrackerHeadJoint(body, heightMeters);

            if (swordController.HasValue)
            {
                var (fingers, palm) = HandRoll.GetHandFromController(swordController.Value.Rotation, hand);
                targets.SwordFingers = fingers;
                targets.SwordPalm = palm;
            }

            if (body.BackWristRotation.HasValue)
            {
                var (fingers, palm) = HandRoll.GetHandFromWrist(body.BackWristRotation.Value, hand != Hand.Left);
                targets.BackFingers = fingers;
                targets.BackPalm = palm;
            }
        }

        /// <summary>The tracker's head joint (the body's head center is placed a fixed height over it, see <see cref="FullBodyJoints"/>).</summary>
        private static Vector3? GetTrackerHeadJoint(FencerBody body, float heightMeters)
        {
            var headCenter = body.Get(SkeletonJoint.HeadCenter);
            return headCenter.HasValue
                ? headCenter.Value - new Vector3(0.0f, FullBodyJoints.HeadCenterAboveHeadJointPerHeight * heightMeters, 0.0f)
                : (Vector3?)null;
        }

        /// <summary>The eyes (the headset), from the tracker's head joint and the head's turn; null without either.</summary>
        private static Vector3? GetEyes(FencerBody body, float heightMeters)
        {
            var headJoint = GetTrackerHeadJoint(body, heightMeters);
            if (!headJoint.HasValue || !body.HeadRotation.HasValue)
            {
                return null;
            }

            return headJoint.Value - Vector3.Transform(TrackerHeadJointInHeadsetPerHeight * heightMeters, body.HeadRotation.Value);
        }
    }
}
