using System.Numerics;

namespace VrFencing.Dummy
{
    /// <summary>
    /// The Dummy's pose: a right-handed épée fencer in en garde with the sword arm in line, as fractions of
    /// standing height so it scales with the Fencer's height setting.
    ///
    /// Frame (the Dummy frame): origin on the floor under the center of the Dummy's front foot,
    /// +x the Dummy's right, +y up, +z the Dummy's forward (toward the Fencer).
    ///
    /// Segment lengths and heights are standard anthropometric fractions of height (Winter, "Biomechanics and
    /// Motor Control of Human Movement", 4th ed. 2009, fig. 4.1, after Drillis and Contini 1966): shoulder 0.818,
    /// hip 0.530, knee 0.285, ankle 0.039, upper arm 0.186, forearm 0.146, hand 0.108, foot 0.152, thigh 0.245,
    /// shank 0.246. The en garde posture is posed by hand from those: feet about two foot lengths apart (0.30),
    /// front shin vertical, hips dropped to 0.50 by the bent knees, torso upright and turned side-on,
    /// sword arm straight toward the Fencer, back arm raised behind.
    /// </summary>
    public static class EnGardePose
    {
        public const float UpperArmLength = 0.186f;
        public const float ForearmLength = 0.146f;
        public const float HandLength = 0.108f;

        /// <summary>The sword hand grips the handle at about the middle of the hand.</summary>
        public const float ShoulderToGrip = UpperArmLength + ForearmLength + (HandLength * 0.5f);

        /// <summary>Front (sword-side) shoulder: standing shoulder height 0.818 lowered by the en garde crouch.</summary>
        public static readonly Vector3 SwordShoulder = new Vector3(0.06f, 0.79f, -0.05f);

        /// <summary>
        /// Arm in line: straight toward the Fencer, slightly down and in toward the centerline.
        /// <see cref="SwordArmPose.ArmInLine"/> points the shoulder joint along it.
        /// </summary>
        public static readonly Vector3 ArmInLineDirection = Vector3.Normalize(new Vector3(-0.1f, -0.1f, 1.0f));

        // The wrist Target Zone is a band of 0.02 either side of the wrist joint; the forearm and hand zones meet it.
        public const float WristHalfWidth = 0.02f;

        public const float UpperArmRadius = 0.035f;
        public const float ForearmRadius = 0.03f;
        public const float WristRadius = 0.025f;
        public const float HandRadius = 0.035f;

        // Body Target Zone: the trunk from pelvis to shoulders, side-on.
        public static readonly Vector3 TorsoBottom = new Vector3(0.0f, 0.56f, -0.165f);
        public static readonly Vector3 TorsoTop = new Vector3(0.0f, 0.74f, -0.15f);
        public const float TorsoRadius = 0.08f;

        // Front leg: thigh Target Zone from knee to hip, foot Target Zone heel to toe along +z.
        public static readonly Vector3 FrontHip = new Vector3(0.0f, 0.50f, -0.09f);
        public static readonly Vector3 FrontKnee = new Vector3(0.0f, 0.285f, 0.02f);
        public static readonly Vector3 FrontAnkle = new Vector3(0.0f, 0.04f, -0.04f);
        public static readonly Vector3 FrontHeel = new Vector3(0.0f, 0.03f, -0.07f);
        public static readonly Vector3 FrontToe = new Vector3(0.0f, 0.03f, 0.07f);
        public const float ThighRadius = 0.05f;
        public const float ShinRadius = 0.035f;
        public const float FootRadius = 0.035f;

        // Back leg: foot turned out to the Dummy's left, knee bent outward.
        public static readonly Vector3 BackHip = new Vector3(-0.03f, 0.50f, -0.24f);
        public static readonly Vector3 BackKnee = new Vector3(-0.08f, 0.27f, -0.23f);
        public static readonly Vector3 BackAnkle = new Vector3(-0.01f, 0.04f, -0.30f);
        public static readonly Vector3 BackHeel = new Vector3(0.0f, 0.03f, -0.30f);
        public static readonly Vector3 BackToe = new Vector3(-0.13f, 0.03f, -0.30f);

        // Back arm: upper arm back and out, forearm up, as in the classical épée en garde.
        public static readonly Vector3 BackShoulder = new Vector3(-0.06f, 0.79f, -0.25f);
        public static readonly Vector3 BackElbow = new Vector3(-0.10f, 0.80f, -0.42f);
        public static readonly Vector3 BackHand = new Vector3(-0.10f, 0.94f, -0.45f);
        public const float BackArmRadius = 0.03f;

        public static readonly Vector3 HeadCenter = new Vector3(0.0f, 0.90f, -0.16f);
        public const float HeadRadius = 0.065f;

        /// <summary>
        /// How far in front of the front foot the front of the body Target Zone is (negative: it is behind the foot).
        /// </summary>
        public static float BodyFrontAlongForward => System.Math.Max(TorsoBottom.Z, TorsoTop.Z) + TorsoRadius;
    }
}
