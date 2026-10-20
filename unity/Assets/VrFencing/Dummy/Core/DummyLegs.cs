using System.Numerics;
using VrFencing.Settings;

namespace VrFencing.Dummy
{
    /// <summary>One leg's joints in the Dummy frame: hip, knee, ankle, and the foot from heel to toe.</summary>
    public readonly struct LegPose
    {
        public LegPose(Vector3 hip, Vector3 knee, Vector3 ankle, Vector3 heel, Vector3 toe)
        {
            Hip = hip;
            Knee = knee;
            Ankle = ankle;
            Heel = heel;
            Toe = toe;
        }

        public Vector3 Hip { get; }

        public Vector3 Knee { get; }

        public Vector3 Ankle { get; }

        public Vector3 Heel { get; }

        public Vector3 Toe { get; }

        /// <summary>The same leg scaled by <paramref name="scale"/> and, for a left-handed Dummy, reflected across x = 0.</summary>
        public LegPose ScaledForHand(float scale, Hand hand)
        {
            return new LegPose(Place(Hip, scale, hand), Place(Knee, scale, hand), Place(Ankle, scale, hand), Place(Heel, scale, hand), Place(Toe, scale, hand));
        }

        private static Vector3 Place(Vector3 point, float scale, Hand hand)
        {
            var scaled = point * scale;
            return hand == Hand.Left ? HandMirroring.MirrorPosition(scaled) : scaled;
        }
    }

    /// <summary>
    /// Both legs of the Dummy in the Dummy frame (see <see cref="DummyLayout"/>), in meters and already mirrored for a
    /// left-handed Dummy. <see cref="EnGarde"/> is the standing stance; <see cref="DummyFootwork"/> moves them as the
    /// Dummy steps (PER-110), and the thigh and foot Target Zones and the leg silhouette follow them.
    /// </summary>
    public readonly struct DummyLegs
    {
        public DummyLegs(LegPose front, LegPose back)
        {
            Front = front;
            Back = back;
        }

        /// <summary>The leg on the sword hand's side.</summary>
        public LegPose Front { get; }

        public LegPose Back { get; }

        /// <summary>The en garde stance of <see cref="EnGardePose"/> for a Dummy of <paramref name="heightMeters"/>.</summary>
        public static DummyLegs EnGarde(float heightMeters, Hand hand)
        {
            return new DummyLegs(FrontAtUnitHeight.ScaledForHand(heightMeters, hand), BackAtUnitHeight.ScaledForHand(heightMeters, hand));
        }

        /// <summary>The front leg of <see cref="EnGardePose"/> for a right-handed Dummy of height 1.</summary>
        internal static LegPose FrontAtUnitHeight { get; } = new LegPose(
            EnGardePose.FrontHip, EnGardePose.FrontKnee, EnGardePose.FrontAnkle, EnGardePose.FrontHeel, EnGardePose.FrontToe);

        /// <summary>The back leg of <see cref="EnGardePose"/> for a right-handed Dummy of height 1.</summary>
        internal static LegPose BackAtUnitHeight { get; } = new LegPose(
            EnGardePose.BackHip, EnGardePose.BackKnee, EnGardePose.BackAnkle, EnGardePose.BackHeel, EnGardePose.BackToe);
    }
}
