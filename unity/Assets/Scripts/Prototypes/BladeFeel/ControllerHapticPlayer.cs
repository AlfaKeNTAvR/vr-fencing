// Plays a short sequence of vibration segments on one Touch controller through
// OVRInput. Timing is checked once per frame, so segment lengths are rounded to
// the frame time (about 14 ms at 72 Hz).
using UnityEngine;

namespace VrFencing.Prototypes.BladeFeel
{
    public sealed class ControllerHapticPlayer
    {
        private readonly OVRInput.Controller _controller;
        private HapticSegment[] _pattern;
        private int _segmentIndex;
        private float _segmentEndTime;

        public ControllerHapticPlayer(OVRInput.Controller controller)
        {
            _controller = controller;
        }

        /// <summary>Starts a pattern, replacing whatever is playing.</summary>
        public void Play(HapticSegment[] pattern, float now)
        {
            _pattern = pattern;
            StartSegment(0, now);
        }

        public void Tick(float now)
        {
            if (_pattern == null || now < _segmentEndTime)
                return;
            StartSegment(_segmentIndex + 1, now);
        }

        public void Stop()
        {
            _pattern = null;
            OVRInput.SetControllerVibration(0f, 0f, _controller);
        }

        private void StartSegment(int index, float now)
        {
            if (index >= _pattern.Length)
            {
                Stop();
                return;
            }

            _segmentIndex = index;
            var segment = _pattern[index];
            _segmentEndTime = now + segment.DurationSeconds;
            OVRInput.SetControllerVibration(segment.Frequency, segment.Amplitude, _controller);
        }
    }
}
