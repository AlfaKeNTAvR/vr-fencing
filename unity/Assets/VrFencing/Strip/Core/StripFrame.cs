using System;
using System.Numerics;
using VrFencing.Settings;

namespace VrFencing.Strip
{
    /// <summary>
    /// Converts between the room (Unity world, floor-level tracking: y = 0 is the floor) and the Strip frame
    /// (see <see cref="StripGeometry"/>). The Strip frame's pose in the room is the En Garde Line pose: on the floor,
    /// turned about the vertical only.
    /// </summary>
    public static class StripFrame
    {
        /// <summary>
        /// The En Garde Line pose and placed length from the two placement points: the first is on the En Garde Line,
        /// the second at the far end. Both are dropped to the floor; only their horizontal positions count.
        /// </summary>
        /// <exception cref="ArgumentException">The points are at the same spot on the floor, so there is no direction.</exception>
        public static (TrackedPose EnGardeLine, float PlacedLengthMeters) FromPlacementPoints(Vector3 enGardePoint, Vector3 farEndPoint)
        {
            var origin = new Vector3(enGardePoint.X, 0.0f, enGardePoint.Z);
            var along = new Vector3(farEndPoint.X - enGardePoint.X, 0.0f, farEndPoint.Z - enGardePoint.Z);
            var length = along.Length();
            if (length < 1e-4f)
            {
                throw new ArgumentException("The placement points are at the same spot; the Strip has no direction.");
            }

            // Yaw that turns +z toward the far end. Unity's y rotation and System.Numerics' agree: +z goes to (sin, 0, cos).
            var yaw = (float)Math.Atan2(along.X, along.Z);
            var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
            return (new TrackedPose(origin, rotation), length);
        }

        /// <summary>A room point in the Strip frame of a Strip whose En Garde Line is at <paramref name="enGardeLine"/>.</summary>
        public static Vector3 ToStripFrame(TrackedPose enGardeLine, Vector3 pointInRoom)
        {
            return Vector3.Transform(pointInRoom - enGardeLine.Position, Quaternion.Inverse(enGardeLine.Rotation));
        }

        /// <summary>A Strip-frame point in the room.</summary>
        public static Vector3 ToRoom(TrackedPose enGardeLine, Vector3 pointInStrip)
        {
            return enGardeLine.Position + Vector3.Transform(pointInStrip, enGardeLine.Rotation);
        }
    }
}
