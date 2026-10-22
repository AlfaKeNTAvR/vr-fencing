using System;

namespace VrFencing.Dummy
{
    /// <summary>
    /// One rig bone's length fitted to a tracked body (PER-142): the median of the tracked lengths seen over the last
    /// <see cref="BodyRigTuning.LengthFitSamples"/> samples, taken at most every
    /// <see cref="BodyRigTuning.LengthFitSampleSeconds"/>. A median, so one bad frame or a blade-lagged wrist does not
    /// make the bone pulse. Nothing is allocated per sample.
    /// </summary>
    public sealed class BoneLengthFit
    {
        private readonly float[] _samples = new float[BodyRigTuning.LengthFitSamples];
        private readonly float[] _sorted = new float[BodyRigTuning.LengthFitSamples];
        private int _count;
        private int _next;
        private double _lastSampleSeconds;

        /// <summary>The fitted length in meters, or null before the first sample.</summary>
        public float? Meters { get; private set; }

        /// <summary>
        /// A tracked length at <paramref name="timeSeconds"/>; kept when the last kept one is at least a sample interval
        /// old (or the time went back: a replay looped). Not a positive finite number: ignored.
        /// </summary>
        public void Add(double timeSeconds, float meters)
        {
            if (!(meters > 0.0f) || float.IsInfinity(meters))
            {
                return;
            }

            // A microsecond's slack, so samples taken exactly an interval apart are not skipped by rounding.
            var isDue = _count == 0
                        || timeSeconds - _lastSampleSeconds >= BodyRigTuning.LengthFitSampleSeconds - 1e-6
                        || timeSeconds < _lastSampleSeconds;
            if (!isDue)
            {
                return;
            }

            _lastSampleSeconds = timeSeconds;
            _samples[_next] = meters;
            _next = (_next + 1) % _samples.Length;
            _count = Math.Min(_count + 1, _samples.Length);
            Array.Copy(_samples, _sorted, _count);
            Array.Sort(_sorted, 0, _count);
            Meters = _count % 2 == 1 ? _sorted[_count / 2] : (_sorted[(_count / 2) - 1] + _sorted[_count / 2]) * 0.5f;
        }

        public void Reset()
        {
            _count = 0;
            _next = 0;
            Meters = null;
        }
    }
}
