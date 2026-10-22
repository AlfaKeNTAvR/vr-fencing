using UnityEngine;
using VrFencing.Settings;

namespace VrFencing.Blade
{
    /// <summary>
    /// What the blade-contact log (PER-136, <see cref="BladeContactRecorderBehaviour"/>) reads from the Blade after its
    /// frame: the Fencer's blade from the hand and as drawn, the Armed Dummy's part, and the sword-hand controller's
    /// velocity, all in the Strip frame. Read-only.
    /// </summary>
    public sealed partial class BladeBehaviour
    {
        // The Fencer's blade from the hand this frame (guard to the real, drooped point), in the Strip frame.
        private BladeSegment _handBladeOnStrip;

        /// <summary>The Fencer's sword hand, as the Settings say.</summary>
        internal Hand SwordHand => _settings != null ? _settings.Hand : Hand.Right;

        /// <summary>
        /// Fills <paramref name="sample"/> with this frame's blades (call after this behaviour's LateUpdate) and returns
        /// the Armed Dummy's events since the last read.
        /// </summary>
        internal BladeLogEvents ReadForRecording(ref BladeContactSample sample)
        {
            if (_settings == null || _drawnBlade == null || !_isTracked)
            {
                BladeContactProbe.ClearFencer(ref sample);
                BladeContactProbe.ClearArmed(ref sample);
                sample.SwordHandVelocity = new System.Numerics.Vector3(float.NaN);
                sample.DummyMode = null;
                sample.DummyAction = null;
                return BladeLogEvents.None;
            }

            BladeContactProbe.ReadFencer(ref sample, _handBladeOnStrip, _drawnBlade);
            sample.SwordHandVelocity = ReadSwordHandVelocityOnStrip();
            if (_armedDummy == null)
            {
                BladeContactProbe.ClearArmed(ref sample);
                sample.OpponentHand = _settings.ArmedDummy.OpponentHand;
                sample.DummyMode = "unarmed";
                sample.DummyAction = "none";
                return BladeLogEvents.None;
            }

            return _armedDummy.ReadForRecording(ref sample, _drawnBlade, Time.timeAsDouble);
        }

        /// <summary>Forgets the Armed Dummy's events so far: a recording starts with none from before it.</summary>
        internal void ForgetRecordingEvents()
        {
            _armedDummy?.ForgetRecordingEvents();
        }

        /// <summary>The sword-hand controller's velocity in the Strip frame (the room's without a Strip), m/s.</summary>
        private System.Numerics.Vector3 ReadSwordHandVelocityOnStrip()
        {
            var controller = _settings.Hand == Hand.Left ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
            var inTrackingSpace = OVRInput.GetLocalControllerVelocity(controller);
            var trackingSpace = _cameraRig != null ? _cameraRig.trackingSpace : null;
            var inRoom = trackingSpace != null ? trackingSpace.rotation * inTrackingSpace : inTrackingSpace;
            var stripRotation = _strip != null && _strip.EnGardeLine != null ? _strip.EnGardeLine.rotation : Quaternion.identity;
            return (Quaternion.Inverse(stripRotation) * inRoom).ToNumerics();
        }
    }
}
