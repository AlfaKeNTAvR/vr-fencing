using System;
using System.Collections.Generic;

namespace VrFencing.Tracking
{
    /// <summary>
    /// The Start Mark (PER-89): the mark on the floor at the En Garde Line where the Fencer's front foot stands between
    /// Reps of a Drill where the Fencer moves. The headset sees the head, not the foot, so the Fencer counts as back
    /// when the head is within <see cref="ToleranceMeters"/> along the Strip of where it is in en garde with the front
    /// foot on the mark, and still. Only the distance along the Strip counts: drift happens along it, and the head's
    /// side position depends on the stance.
    /// </summary>
    public static class StartMark
    {
        /// <summary>The mark is on the En Garde Line: Strip-frame z of the front foot.</summary>
        public const float FrontFootZ = 0.0f;

        /// <summary>
        /// How far behind the front foot the headset is in en garde: 0.11 of the height (18 cm for 1.65 m). The Dummy's
        /// en garde pose has the head's center 0.16 of the height behind the front foot, and the eyes are about 8 cm in
        /// front of the head's center. Tune on the headset with the logged offset.
        /// </summary>
        public const float HeadBehindFrontFootPerHeight = 0.11f;

        /// <summary>How close along the Strip the head must be to its en garde place (PER-89: about 15 cm).</summary>
        public const float ToleranceMeters = 0.15f;

        /// <summary>The head must stay still this long before the Fencer counts as back (en garde, not passing through).</summary>
        public const double StillSeconds = 0.3;

        /// <summary>Still: the head moved at most this far along the Strip over <see cref="StillSeconds"/> (en garde sway is about 1 to 2 cm).</summary>
        public const float StillSpreadMeters = 0.03f;

        /// <summary>Strip-frame z of the head in en garde with the front foot on the mark, for a Fencer of <paramref name="fencerHeightMeters"/>.</summary>
        public static float GetHeadZ(float fencerHeightMeters)
        {
            return FrontFootZ - (HeadBehindFrontFootPerHeight * fencerHeightMeters);
        }
    }

    /// <summary>
    /// Watches the head for the Start Mark: fed every frame, it tells whether the Fencer is back at the mark and still,
    /// and how far the head is from its place there (for the log). Keeps only the last <see cref="StartMark.StillSeconds"/>.
    /// </summary>
    public sealed class StartMarkWatch
    {
        private readonly List<HeadSample> _samples = new List<HeadSample>();

        /// <summary>Adds a frame. Samples must come in time order; an older or same-time sample is ignored.</summary>
        public void AddSample(HeadSample sample)
        {
            if (_samples.Count > 0 && sample.TimeSeconds <= _samples[_samples.Count - 1].TimeSeconds)
            {
                return;
            }

            _samples.Add(sample);

            // Keep the newest sample at least StillSeconds old, so the window always spans the full time.
            while (_samples.Count > 1 && _samples[1].TimeSeconds <= sample.TimeSeconds - StartMark.StillSeconds)
            {
                _samples.RemoveAt(0);
            }
        }

        /// <summary>Forgets the head, as after a pause or a jump of the Strip.</summary>
        public void Clear()
        {
            _samples.Clear();
        }

        /// <summary>
        /// Signed distance along the Strip from the head's en garde place at the mark to the head now (+ toward the
        /// Dummy), for a Fencer of <paramref name="fencerHeightMeters"/>; null with no sample yet.
        /// </summary>
        public float? MeasureOffsetMeters(float fencerHeightMeters)
        {
            if (_samples.Count == 0)
            {
                return null;
            }

            return _samples[_samples.Count - 1].HeadInStrip.Z - StartMark.GetHeadZ(fencerHeightMeters);
        }

        /// <summary>
        /// The head has been within <see cref="StartMark.ToleranceMeters"/> of its place at the mark, and still, for the
        /// last <see cref="StartMark.StillSeconds"/>.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The height is not a positive number.</exception>
        public bool IsBackAndStill(float fencerHeightMeters)
        {
            if (!(fencerHeightMeters > 0.0f))
            {
                throw new ArgumentOutOfRangeException(nameof(fencerHeightMeters), fencerHeightMeters, "Must be positive.");
            }

            if (_samples.Count < 2 || _samples[_samples.Count - 1].TimeSeconds - _samples[0].TimeSeconds < StartMark.StillSeconds)
            {
                return false;
            }

            var headZ = StartMark.GetHeadZ(fencerHeightMeters);
            var lowestZ = float.MaxValue;
            var highestZ = float.MinValue;
            foreach (var sample in _samples)
            {
                if (Math.Abs(sample.HeadInStrip.Z - headZ) > StartMark.ToleranceMeters)
                {
                    return false;
                }

                lowestZ = Math.Min(lowestZ, sample.HeadInStrip.Z);
                highestZ = Math.Max(highestZ, sample.HeadInStrip.Z);
            }

            return highestZ - lowestZ <= StartMark.StillSpreadMeters;
        }
    }
}
