using System;
using System.Numerics;
using VrFencing.Settings;

namespace VrFencing.UI
{
    /// <summary>
    /// Dragging a panel by its grab bar with the off-hand laser (PER-86), like the Meta system menu. The point the laser
    /// grabbed stays on the laser at the distance it was grabbed, and the panel keeps the same offset from that point in
    /// its own frame, so it hangs off the laser the way it was picked up. The panel stays upright and turns to face the
    /// head, so twisting the wrist does not roll it. The pose is smoothed a little, because the laser's end magnifies
    /// hand tremor by the grab distance.
    ///
    /// Room frame, Unity's axes (x right, y up, z forward); panel quads and text face -z, so a panel's forward points
    /// from the head through the panel.
    /// </summary>
    public sealed class PanelDrag
    {
        // Sharp enough to feel attached (63% of the way in 25 ms), soft enough to take out tremor at a meter.
        private const float FollowSharpness = 40.0f;

        private readonly float _grabDistanceMeters;
        private readonly Vector3 _grabToPanelInPanel;

        /// <param name="rayOrigin">Where the laser starts (the off-hand controller).</param>
        /// <param name="grabPoint">Where the laser hit the grab bar.</param>
        /// <param name="panelInRoom">The panel's pose when grabbed.</param>
        public PanelDrag(Vector3 rayOrigin, Vector3 grabPoint, TrackedPose panelInRoom)
        {
            _grabDistanceMeters = Vector3.Distance(rayOrigin, grabPoint);
            _grabToPanelInPanel = Vector3.Transform(panelInRoom.Position - grabPoint, Quaternion.Inverse(panelInRoom.Rotation));
            PanelInRoom = panelInRoom;
            GrabPoint = grabPoint;
        }

        /// <summary>The panel's pose now: where it was grabbed until the first <see cref="Move"/>.</summary>
        public TrackedPose PanelInRoom { get; private set; }

        /// <summary>The grabbed point on the laser now, where the laser's line ends while dragging.</summary>
        public Vector3 GrabPoint { get; private set; }

        /// <summary>Moves the panel with the laser and returns its new pose (also <see cref="PanelInRoom"/>).</summary>
        /// <param name="rayDirection">The laser's direction; any length but zero.</param>
        /// <param name="headPosition">The head (center eye), which the panel turns to face.</param>
        public TrackedPose Move(Vector3 rayOrigin, Vector3 rayDirection, Vector3 headPosition, float deltaSeconds)
        {
            if (rayDirection.LengthSquared() < 1e-8f)
            {
                return PanelInRoom;
            }

            var grabPoint = rayOrigin + (Vector3.Normalize(rayDirection) * _grabDistanceMeters);
            var rotation = FaceHead(grabPoint, headPosition) ?? PanelInRoom.Rotation;
            var target = new TrackedPose(grabPoint + Vector3.Transform(_grabToPanelInPanel, rotation), rotation);

            var follow = 1.0f - (float)Math.Exp(-FollowSharpness * Math.Max(0.0f, deltaSeconds));
            PanelInRoom = new TrackedPose(
                Vector3.Lerp(PanelInRoom.Position, target.Position, follow),
                Quaternion.Normalize(Quaternion.Slerp(PanelInRoom.Rotation, target.Rotation, follow)));
            GrabPoint = Vector3.Lerp(GrabPoint, grabPoint, follow);
            return PanelInRoom;
        }

        /// <summary>
        /// An upright rotation (no roll) whose forward points from the head to <paramref name="point"/>, tilted up or
        /// down to look at it, as Unity's LookRotation(point - head, up). Null when the point is at the head.
        /// </summary>
        public static Quaternion? FaceHead(Vector3 point, Vector3 headPosition)
        {
            var forward = point - headPosition;
            if (forward.LengthSquared() < 1e-6f)
            {
                return null;
            }

            forward = Vector3.Normalize(forward);
            // Yaw turns +z toward the point, then a positive pitch about x tips +z down, as in Unity.
            var yaw = (float)Math.Atan2(forward.X, forward.Z);
            var pitch = (float)-Math.Asin(Math.Max(-1.0f, Math.Min(1.0f, forward.Y)));
            return Quaternion.CreateFromYawPitchRoll(yaw, pitch, 0.0f);
        }
    }
}
