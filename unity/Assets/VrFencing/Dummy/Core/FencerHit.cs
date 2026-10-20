using System.Numerics;

namespace VrFencing.Dummy
{
    /// <summary>
    /// A part of a fencer's body, coarse enough that a tracked body (IOBT) and the en garde stand-in both name every
    /// capsule with one: what an Armed Dummy hit on the Fencer landed on (PER-98). Sword and back sides, front and back
    /// legs are by fencing side, not left and right.
    /// </summary>
    public enum BodyPart
    {
        Head,
        Torso,

        /// <summary>Upper arm and forearm of the sword arm (with the hand, when the hand is not tracked on its own).</summary>
        SwordArm,
        SwordHand,

        /// <summary>The whole back arm, hand included.</summary>
        BackArm,
        FrontThigh,

        /// <summary>Front shin and foot.</summary>
        FrontLowerLeg,

        /// <summary>The whole back leg.</summary>
        BackLeg,
    }

    /// <summary>
    /// Which body judged an Armed Dummy hit on the Fencer (PER-23 resolution, PER-98): IOBT body tracking when it is
    /// confident, otherwise capsules placed from the headset plus the live sword arm.
    /// </summary>
    public enum FencerHitSource
    {
        /// <summary>Inside-out body tracking at high fidelity, with every joint the body capsules need.</summary>
        Iobt,

        /// <summary>
        /// The fallback: the en garde stand-in moved under the headset, plus a sword arm from its shoulder to the live
        /// guard. Used when tracking fidelity is Low, body tracking is stopped, or joints are missing.
        /// </summary>
        HeadsetCapsules,
    }

    /// <summary>One capsule of a fencer's body and the part it belongs to.</summary>
    public readonly struct BodyCapsule
    {
        public BodyCapsule(BodyPart part, Capsule shape)
        {
            Part = part;
            Shape = shape;
        }

        public BodyPart Part { get; }

        public Capsule Shape { get; }
    }

    /// <summary>The Armed Dummy's point landed on the Fencer (PER-98): where, on which part, and which body judged it.</summary>
    public readonly struct FencerHit
    {
        public FencerHit(FencerHitSource source, BodyPart part, Vector3 pointOnStrip)
        {
            Source = source;
            Part = part;
            PointOnStrip = pointOnStrip;
        }

        public FencerHitSource Source { get; }

        public BodyPart Part { get; }

        /// <summary>Where the point first met the body, in the Strip frame.</summary>
        public Vector3 PointOnStrip { get; }
    }
}
