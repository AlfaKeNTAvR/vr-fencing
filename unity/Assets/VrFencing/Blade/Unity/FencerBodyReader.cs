using UnityEngine;
using VrFencing.Partner;
using VrFencing.Settings;
using VrFencing.Strip;
using VrFencing.Tracking;

namespace VrFencing.Blade
{
    /// <summary>
    /// Reads the Fencer's headset and tracked body in the Strip frame for the Armed Dummy's hit judging (PER-98). Body
    /// tracking itself is owned by <see cref="BodyTrackingBehaviour"/>; this only reads its joints, as the Partner's
    /// <c>LocalFencerReader</c> does for the network.
    /// </summary>
    internal sealed class FencerBodyReader
    {
        private readonly TrackedPose?[] _fullBody = new TrackedPose?[FullBodyJoints.Count];
        private readonly OVRCameraRig _cameraRig;
        private readonly BodyTrackingBehaviour _bodyTracking;

        public FencerBodyReader(OVRCameraRig cameraRig)
        {
            _cameraRig = cameraRig;
            _bodyTracking = Object.FindFirstObjectByType<BodyTrackingBehaviour>();
            if (_bodyTracking == null)
            {
                Debug.LogWarning("[ArmedDummy] No body tracking in the scene: hits on the Fencer are judged on the headset capsules only.");
            }
        }

        /// <summary>The headset's pose in the Strip frame, or null when it is not on.</summary>
        public TrackedPose? ReadHeadset(TrackedPose stripInRoom)
        {
            if (_cameraRig == null || !OVRManager.isHmdPresent)
            {
                return null;
            }

            var eyes = _cameraRig.centerEyeAnchor;
            return StripFrame.ToStripFrame(stripInRoom, new Pose(eyes.position, eyes.rotation).ToTrackedPose());
        }

        /// <summary>The tracked body in the Strip frame; a body with no joints while body tracking gives none.</summary>
        public FencerBody ReadBody(TrackedPose stripInRoom, TrackedPose? headset, Hand hand, float fencerHeightMeters)
        {
            var fidelity = _bodyTracking != null ? _bodyTracking.Fidelity : null;
            for (var joint = 0; joint < _fullBody.Length; joint++)
            {
                var jointInRoom = fidelity.HasValue ? _bodyTracking.ReadJointInRoom(joint) : null;
                _fullBody[joint] = jointInRoom.HasValue ? StripFrame.ToStripFrame(stripInRoom, jointInRoom.Value) : (TrackedPose?)null;
            }

            return FullBodyJoints.Read(_fullBody, headset?.Rotation, hand, fencerHeightMeters, fidelity);
        }
    }
}
