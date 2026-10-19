using System.Numerics;

namespace VrFencing.Settings
{
    /// <summary>
    /// A position and rotation as plain data (System.Numerics, no UnityEngine), so Core
    /// code and tests can handle blade and body poses without a scene.
    /// </summary>
    public readonly struct TrackedPose
    {
        public TrackedPose(Vector3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }

        public Vector3 Position { get; }

        public Quaternion Rotation { get; }
    }

    /// <summary>
    /// Mirrors poses for a left-handed Fencer. All logic is written for the right hand;
    /// left-hand input is mirrored in, and left-hand output mirrored out.
    /// Poses must be in a frame where +x is the Fencer's right, +y up and +z forward
    /// along the Strip (the Strip frame); mirroring is a reflection across its x = 0 plane.
    /// </summary>
    public static class HandMirroring
    {
        public static Vector3 MirrorPosition(Vector3 position)
        {
            return new Vector3(-position.X, position.Y, position.Z);
        }

        /// <summary>
        /// Reflecting across the x = 0 plane keeps the rotation's x component and negates y and z,
        /// so a mirrored direction turned by the result equals the mirror of the original turn.
        /// </summary>
        public static Quaternion MirrorRotation(Quaternion rotation)
        {
            return new Quaternion(rotation.X, -rotation.Y, -rotation.Z, rotation.W);
        }

        public static TrackedPose Mirror(TrackedPose pose)
        {
            return new TrackedPose(MirrorPosition(pose.Position), MirrorRotation(pose.Rotation));
        }

        /// <summary>Returns the pose unchanged for a right-handed Fencer and mirrored for a left-handed one.</summary>
        public static TrackedPose ForHand(TrackedPose pose, Hand hand)
        {
            return hand == Hand.Left ? Mirror(pose) : pose;
        }
    }
}
