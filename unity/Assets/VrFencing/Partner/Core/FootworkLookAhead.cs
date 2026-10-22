using System;
using System.Numerics;
using VrFencing.Settings;

namespace VrFencing.Partner
{
    /// <summary>
    /// The sender's footwork read with a look-ahead (PER-142, decided by Nikita): the receiver draws the body
    /// <see cref="BodyStream.BufferSeconds"/> late anyway, so the feet can be read from hips that are already
    /// <see cref="Seconds"/> further on. Each frame is kept, and the <see cref="FootworkStateTracker"/> reads the frame
    /// from <see cref="Seconds"/> ago with a preview of the newest hips: a step starts as the hips start (not 125 to
    /// 170 ms after, as before), while the lunge and recovery stay read from the frame's own hips.
    /// <see cref="State"/> is the feet for the frame <see cref="Seconds"/> ago; it goes out with the newest body, and the
    /// receiver pairs it with the body from that time (<see cref="BodyStream.Sample"/>), so the feet arrive no later than
    /// before, and steps no longer lag the hips. Nothing is allocated per frame.
    /// </summary>
    public sealed class FootworkLookAhead
    {
        /// <summary>
        /// How far ahead the steps are read: the receiver's body buffer (0.1 s) less one body interval (1/30 s) and a little
        /// network jitter, so the footwork for the drawn moment has nearly always arrived.
        /// </summary>
        public const double Seconds = 0.06;

        // Frames kept: 0.06 s plus the 50 ms speed window at up to about 600 frames per second.
        private const int Capacity = 72;

        private readonly double[] _times = new double[Capacity];
        private readonly Vector3?[] _heads = new Vector3?[Capacity];
        private readonly Vector3?[] _hips = new Vector3?[Capacity];
        private readonly FootworkStateTracker _tracker = new FootworkStateTracker();
        private int _count;
        private int _newest = -1;
        private int _toRead;

        /// <summary>The feet at the frame <see cref="Seconds"/> before the newest (with no confidence until there is one).</summary>
        public FootworkState State => _tracker.State;

        /// <summary>
        /// One rendered frame, as <see cref="FootworkStateTracker.Update"/> takes it; the frames that are now
        /// <see cref="Seconds"/> old are read. A frame at the same or an earlier time is ignored.
        /// </summary>
        public void Update(double timeSeconds, Vector3? head, Vector3? hips, float heightMeters, Hand hand)
        {
            if (_count > 0 && timeSeconds <= _times[_newest])
            {
                return;
            }

            if (_count == Capacity && _toRead == Capacity)
            {
                // The oldest frame is dropped while still unread (frames far faster than expected): it is skipped.
                _toRead--;
            }

            _newest = (_newest + 1) % Capacity;
            _times[_newest] = timeSeconds;
            _heads[_newest] = head;
            _hips[_newest] = hips;
            _count = Math.Min(_count + 1, Capacity);
            _toRead = Math.Min(_toRead + 1, _count);

            var preview = GetPreview();
            while (_toRead > 0)
            {
                var index = (_newest - (_toRead - 1) + Capacity) % Capacity;
                if (_times[index] > timeSeconds - Seconds)
                {
                    break;
                }

                _tracker.Update(_times[index], _heads[index], _hips[index], heightMeters, hand, preview);
                _toRead--;
            }
        }

        /// <summary>The newest hips and their velocity over the speed window, or null when the newest frame has no tracked hips.</summary>
        private HipsPreview? GetPreview()
        {
            var newestHips = _hips[_newest];
            if (!newestHips.HasValue)
            {
                return null;
            }

            var velocity = Vector3.Zero;
            for (var back = 1; back < _count; back++)
            {
                var index = (_newest - back + Capacity) % Capacity;
                var span = _times[_newest] - _times[index];
                if (span >= RemoteFootworkTuning.SpeedWindowSeconds)
                {
                    if (_hips[index].HasValue)
                    {
                        velocity = (newestHips.Value - _hips[index].Value) / (float)span;
                    }

                    break;
                }
            }

            return new HipsPreview(newestHips.Value, new Vector3(velocity.X, 0.0f, velocity.Z));
        }
    }
}
