using System;
using System.Numerics;
using VrFencing.Settings;

namespace VrFencing.Dummy
{
    /// <summary>
    /// The Dummy's upper body hung from its hips (PER-115): pelvis, spine, chest and shoulders as one rigid body that the
    /// hips carry and that leans forward about the hips; the neck lets the head tilt only part of that lean so it keeps
    /// looking at the Fencer, and the back arm, hanging from its shoulder, tilts only part of it too. Every point keeps its en garde distance to the others, so
    /// no bone stretches: when the hips drop and stay back in a lunge, the torso leans in over the front leg and the
    /// shoulders, head and both arms come down with it.
    ///
    /// The lean follows the hips: the further the hips sit behind their en garde place (a lunge, a weight shift onto
    /// the back leg), the further the torso leans in to keep the weight over the feet
    /// (<see cref="FootworkTuning.LungeTorsoLeanDegrees"/>). A dropping hip leans it a little too, so a fleche's crouch
    /// bends the body. Breathing and weight shift move the whole upper body with the hips.
    ///
    /// All positions are in the Dummy frame (see <see cref="EnGardePose"/>), in meters, mirrored for the Dummy's hand.
    /// The sword arm's shoulder rides here too; its pose (where the arm points) stays the arm's own
    /// (<see cref="SwordArm"/>), so the Armed Dummy aims from wherever the shoulder is.
    /// </summary>
    public readonly struct DummyTorso
    {
        private const float DegreesToRadians = (float)(Math.PI / 180.0);

        private DummyTorso(
            float leanRadians,
            Vector3 hipCenter,
            Vector3 shoulderCenter,
            Vector3 swordShoulder,
            Vector3 backShoulder,
            Vector3 backElbow,
            Vector3 backHand,
            Vector3 headCenter,
            Quaternion headRotation,
            Capsule body)
        {
            LeanRadians = leanRadians;
            HipCenter = hipCenter;
            ShoulderCenter = shoulderCenter;
            SwordShoulder = swordShoulder;
            BackShoulder = backShoulder;
            BackElbow = backElbow;
            BackHand = backHand;
            HeadCenter = headCenter;
            HeadRotation = headRotation;
            Body = body;
        }

        /// <summary>How far the torso leans forward (toward the Fencer) from upright en garde, about the hips.</summary>
        public float LeanRadians { get; }

        /// <summary>Halfway between the hip joints: the point the torso turns about.</summary>
        public Vector3 HipCenter { get; }

        public Vector3 ShoulderCenter { get; }

        public Vector3 SwordShoulder { get; }

        public Vector3 BackShoulder { get; }

        public Vector3 BackElbow { get; }

        public Vector3 BackHand { get; }

        public Vector3 HeadCenter { get; }

        /// <summary>The head's turn from upright, looking along the Dummy frame's +z (at the Fencer).</summary>
        public Quaternion HeadRotation { get; }

        /// <summary>The body Target Zone: the trunk from pelvis to shoulders, leaning with the torso.</summary>
        public Capsule Body { get; }

        /// <summary>
        /// The upper body for hips whose center is at <paramref name="hipCenter"/> (Dummy frame, mirrored for
        /// <paramref name="hand"/>) on a Dummy of <paramref name="heightMeters"/>.
        /// </summary>
        public static DummyTorso Pose(float heightMeters, Hand hand, Vector3 hipCenter)
        {
            var restHipCenter = Place(RestHipCenterAtUnitHeight, heightMeters, hand);
            var leanRadians = GetLeanRadians(hipCenter - restHipCenter, heightMeters);
            var lean = GetLeanRotation(leanRadians);
            Vector3 OnTorso(Vector3 pointAtUnitHeight)
            {
                return hipCenter + Vector3.Transform(Place(pointAtUnitHeight, heightMeters, hand) - restHipCenter, lean);
            }

            var shoulderCenter = OnTorso(ShoulderCenterAtUnitHeight);

            // The neck lets the head stay nearer upright than the chest: it tilts only part of the lean.
            var headRotation = GetLeanRotation(leanRadians * FootworkTuning.HeadFollowsLeanFraction);
            var neckToHead = Place(EnGardePose.HeadCenter - ShoulderCenterAtUnitHeight, heightMeters, hand);
            var headCenter = shoulderCenter + Vector3.Transform(neckToHead, headRotation);

            // The back arm hangs from its shoulder and tilts only part of the lean, staying raised behind as in en garde.
            var backShoulder = OnTorso(EnGardePose.BackShoulder);
            var backArmTilt = GetLeanRotation(leanRadians * FootworkTuning.BackArmFollowsLeanFraction);
            Vector3 OnBackArm(Vector3 pointAtUnitHeight)
            {
                return backShoulder + Vector3.Transform(Place(pointAtUnitHeight - EnGardePose.BackShoulder, heightMeters, hand), backArmTilt);
            }

            var body = new Capsule(OnTorso(EnGardePose.TorsoBottom), OnTorso(EnGardePose.TorsoTop), EnGardePose.TorsoRadius * heightMeters);
            return new DummyTorso(
                leanRadians,
                hipCenter,
                shoulderCenter,
                OnTorso(EnGardePose.SwordShoulder),
                backShoulder,
                OnBackArm(EnGardePose.BackElbow),
                OnBackArm(EnGardePose.BackHand),
                headCenter,
                headRotation,
                body);
        }

        /// <summary>The upright en garde upper body of a Dummy of <paramref name="heightMeters"/>.</summary>
        public static DummyTorso EnGarde(float heightMeters, Hand hand)
        {
            return Pose(heightMeters, hand, Place(RestHipCenterAtUnitHeight, heightMeters, hand));
        }

        /// <summary>
        /// The sword shoulder of a right-handed Dummy whose hips moved <paramref name="hipOffset"/> from en garde: what
        /// the Armed Dummy plans a thrust from (PER-115), e.g. with <see cref="DummyFootwork.GetLungeHipOffset"/>.
        /// </summary>
        public static Vector3 GetSwordShoulder(float heightMeters, Vector3 hipOffset)
        {
            var restHipCenter = RestHipCenterAtUnitHeight * heightMeters;
            return Pose(heightMeters, Hand.Right, restHipCenter + hipOffset).SwordShoulder;
        }

        /// <summary>
        /// The lean for hips moved <paramref name="hipShift"/> from en garde: forward as they sit back or drop, a little
        /// back as they shift forward, within the limits in <see cref="FootworkTuning"/>.
        /// </summary>
        public static float GetLeanRadians(Vector3 hipShift, float heightMeters)
        {
            var backFraction = -hipShift.Z / (FootworkTuning.LungeHipBackPerHeight * heightMeters);
            var dropFraction = -hipShift.Y / (FootworkTuning.LungeHipDropPerHeight * heightMeters);
            var degrees = (FootworkTuning.LungeTorsoLeanDegrees * backFraction) + (FootworkTuning.HipDropTorsoLeanDegrees * dropFraction);
            var limited = Math.Min(Math.Max(degrees, -FootworkTuning.MaximumTorsoLeanBackDegrees), FootworkTuning.MaximumTorsoLeanDegrees);
            return limited * DegreesToRadians;
        }

        /// <summary>A forward lean: a turn about the Dummy frame's x axis that takes +y toward +z (the same for either hand).</summary>
        private static Quaternion GetLeanRotation(float leanRadians)
        {
            return Quaternion.CreateFromAxisAngle(Vector3.UnitX, leanRadians);
        }

        private static Vector3 RestHipCenterAtUnitHeight => (EnGardePose.FrontHip + EnGardePose.BackHip) * 0.5f;

        private static Vector3 ShoulderCenterAtUnitHeight => (EnGardePose.SwordShoulder + EnGardePose.BackShoulder) * 0.5f;

        private static Vector3 Place(Vector3 pointAtUnitHeight, float heightMeters, Hand hand)
        {
            var scaled = pointAtUnitHeight * heightMeters;
            return hand == Hand.Left ? HandMirroring.MirrorPosition(scaled) : scaled;
        }
    }
}
