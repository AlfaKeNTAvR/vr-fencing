using UnityEngine;
using VrFencing.Session;
using VrFencing.Settings;
using VrFencing.Strip;
using VrFencing.Tracking;

namespace VrFencing.Partner
{
    /// <summary>This Fencer for one frame, in their own Strip frame: what is sent to the partner and what the opponent's blade bends against.</summary>
    public sealed class LocalFencerFrame
    {
        public LocalFencerFrame(
            double timeSeconds,
            TrackedPose? head,
            TrackedPose? swordController,
            FencerBody body,
            StripExtent strip,
            float heightMeters,
            Hand hand,
            BladeSettings blade)
        {
            TimeSeconds = timeSeconds;
            Head = head;
            SwordController = swordController;
            Body = body;
            Strip = strip;
            HeightMeters = heightMeters;
            Hand = hand;
            Blade = blade;
        }

        /// <summary>This app's clock (<c>Time.realtimeSinceStartupAsDouble</c>), sent as the sender's time.</summary>
        public double TimeSeconds { get; }

        public TrackedPose? Head { get; }

        public TrackedPose? SwordController { get; }

        /// <summary>The tracked body's joints for the rig; no joints while body tracking is stopped.</summary>
        public FencerBody Body { get; }

        public StripExtent Strip { get; }

        public float HeightMeters { get; }

        public Hand Hand { get; }

        public BladeSettings Blade { get; }

        public FencerInfo Info => new FencerInfo(Strip, HeightMeters, Hand);
    }

    /// <summary>
    /// Reads this Fencer each frame for the partner streams (PER-81): on the headset, the headset, the sword-hand
    /// controller and the tracked body (from <see cref="BodyTrackingBehaviour"/>, which owns body tracking), in the
    /// placed Strip's frame; in the PC Editor with a Fencer replay in the scene, the replayed Fencer instead, so the
    /// Editor stands in for a second headset (decided in PER-55).
    /// </summary>
    internal sealed class LocalFencerReader
    {
        private readonly TrackedPose?[] _fullBody = new TrackedPose?[FullBodyJoints.Count];
        private OVRCameraRig _cameraRig;
        private BodyTrackingBehaviour _bodyTracking;
        private StripBehaviour _strip;
        private FencerReplayProvider _replay;
        private TrainerSettings _settings;

        /// <summary>True when a Fencer replay stands in for the headset (the PC Editor as the partner).</summary>
        public bool IsReplay => _replay != null;

        public void FindSources(TrainerSettings settings)
        {
            _settings = settings;
            _replay = Object.FindFirstObjectByType<FencerReplayProvider>();
            _cameraRig = Object.FindFirstObjectByType<OVRCameraRig>();
            _bodyTracking = Object.FindFirstObjectByType<BodyTrackingBehaviour>();
            _strip = Object.FindFirstObjectByType<StripBehaviour>();
        }

        public void ApplySettings(TrainerSettings settings)
        {
            _settings = settings;
        }

        /// <summary>This frame's Fencer, or null when there is nothing to read (no camera rig and no replay).</summary>
        public LocalFencerFrame Read(double nowSeconds)
        {
            return _replay != null ? ReadReplay(nowSeconds) : ReadHeadset(nowSeconds);
        }

        private LocalFencerFrame ReadHeadset(double nowSeconds)
        {
            if (_cameraRig == null)
            {
                return null;
            }

            var hasStrip = _strip != null && _strip.IsPlaced && _strip.EnGardeLine != null;
            var enGardeLine = hasStrip
                ? new Pose(_strip.EnGardeLine.position, _strip.EnGardeLine.rotation).ToTrackedPose()
                : new TrackedPose(System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity);

            var head = OVRManager.isHmdPresent
                ? StripFrame.ToStripFrame(enGardeLine, new Pose(_cameraRig.centerEyeAnchor.position, _cameraRig.centerEyeAnchor.rotation).ToTrackedPose())
                : (TrackedPose?)null;

            var isLeft = _settings.Hand == Hand.Left;
            var controller = isLeft ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
            TrackedPose? sword = null;
            if (OVRInput.GetControllerPositionTracked(controller) || OVRInput.GetControllerOrientationTracked(controller))
            {
                var anchor = isLeft ? _cameraRig.leftControllerAnchor : _cameraRig.rightControllerAnchor;
                sword = StripFrame.ToStripFrame(enGardeLine, new Pose(anchor.position, anchor.rotation).ToTrackedPose());
            }

            var fidelity = _bodyTracking != null ? _bodyTracking.Fidelity : null;
            for (var joint = 0; joint < _fullBody.Length; joint++)
            {
                var jointInRoom = fidelity.HasValue ? _bodyTracking.ReadJointInRoom(joint) : null;
                _fullBody[joint] = jointInRoom.HasValue ? StripFrame.ToStripFrame(enGardeLine, jointInRoom.Value) : (TrackedPose?)null;
            }

            var body = FullBodyJoints.Read(_fullBody, head?.Rotation, _settings.Hand, _settings.FencerHeightMeters, fidelity);
            var strip = hasStrip && _strip.Geometry != null ? StripExtent.FromGeometry(_strip.Geometry) : StripExtent.Unplaced;
            return new LocalFencerFrame(nowSeconds, head, sword, body, strip, _settings.FencerHeightMeters, _settings.Hand, _settings.Blade);
        }

        private LocalFencerFrame ReadReplay(double nowSeconds)
        {
            var frame = _replay.CurrentFrame;
            var header = _replay.Header;
            if (frame == null || header == null)
            {
                return null;
            }

            var body = FullBodyJoints.Read(frame.Joints, frame.Head?.Rotation, header.Hand, header.FencerHeightMeters, frame.BodyFidelity);
            var strip = StripExtent.FromRecordedLength(header.StripLengthMeters, header.FencerHeightMeters, _settings.WarningZone);
            return new LocalFencerFrame(nowSeconds, frame.Head, frame.SwordController, body, strip, header.FencerHeightMeters, header.Hand, _settings.Blade);
        }
    }
}
