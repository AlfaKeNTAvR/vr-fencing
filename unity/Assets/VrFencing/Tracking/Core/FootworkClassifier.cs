using System;
using System.Collections.Generic;
using System.Numerics;
using VrFencing.Drills;

namespace VrFencing.Tracking
{
    /// <summary>The Fencer's head at a moment, in the Strip frame (+x the Fencer's right, +y up, +z toward the Dummy).</summary>
    public readonly struct HeadSample
    {
        public HeadSample(double timeSeconds, Vector3 headInStrip)
        {
            TimeSeconds = timeSeconds;
            HeadInStrip = headInStrip;
        }

        public double TimeSeconds { get; }

        public Vector3 HeadInStrip { get; }
    }

    /// <summary>
    /// Reads footwork (advance, retreat, lunge, step-lunge, check, or none) from the head's motion along the Strip
    /// since a Rep's cue (PER-39). The headset sees the head, not the feet, so this is rough feedback, not form coaching.
    /// Distances scale with the Fencer's height (the Dummy Distance presets do too); speeds do not.
    /// <para>
    /// Measured from where the head was at the first sample: the farthest it got forward (<c>fwd</c>) and back
    /// (<c>back</c>), where it is now, and the fastest forward speed (measured as <see cref="LungeStart"/> does). In order:
    /// </para>
    /// <list type="number">
    /// <item>A lunge: the forward speed reached <see cref="LungeStart.MinPeakSpeedMetersPerSecond"/> and fwd reached
    /// <see cref="LungeMinTravelPerHeight"/>; a step-lunge if fwd reached <see cref="StepLungeMinTravelPerHeight"/>.
    /// A recovery afterwards does not change it.</item>
    /// <item>A retreat: back reached <see cref="StepMinTravelPerHeight"/> and is at least fwd.</item>
    /// <item>A check: fwd reached <see cref="CheckMinTravelPerHeight"/> and the head came back at least
    /// <see cref="CheckReturnFraction"/> of the way.</item>
    /// <item>An advance: fwd reached <see cref="StepMinTravelPerHeight"/> and the head stayed forward.</item>
    /// <item>Otherwise none.</item>
    /// </list>
    /// </summary>
    public static class FootworkClassifier
    {
        /// <summary>
        /// Head travel that counts as a step: 0.08 of the height (13 cm for a 1.65 m Fencer), about 40% of an advance
        /// (0.2 of the height, see <c>DistancePresets.AdvanceLengthPerHeight</c>; the head moves about as far as the feet).
        /// An extension's lean stays under it.
        /// </summary>
        public const float StepMinTravelPerHeight = 0.08f;

        /// <summary>
        /// Forward head travel a lunge needs: 0.25 of the height (41 cm for 1.65 m). Above an advance (about 0.2), below
        /// a lunge's head travel (estimated 0.3 to 0.45: the lunge moves the front foot 0.6 of the height, the hips and
        /// head roughly half to two thirds of that). Tune on the headset with the logged travel.
        /// </summary>
        public const float LungeMinTravelPerHeight = 0.25f;

        /// <summary>
        /// Forward head travel that makes a lunge a step-lunge: 0.5 of the height (82 cm for 1.65 m), between a lunge's
        /// estimated head travel and a lunge plus an advance (about 0.5 to 0.65).
        /// </summary>
        public const float StepLungeMinTravelPerHeight = 0.5f;

        /// <summary>Forward head travel a check needs: 0.04 of the height (7 cm for 1.65 m), about a half step.</summary>
        public const float CheckMinTravelPerHeight = 0.04f;

        /// <summary>A check comes back at least this share of its forward travel: it ends at most half as far forward as it got.</summary>
        public const float CheckReturnFraction = 0.5f;

        /// <summary>
        /// The head has moved off its place once it is this far from it: 0.02 of the height (3 cm for 1.65 m), above
        /// en garde sway. Its time is the footwork's onset (the reaction time of a footwork call).
        /// </summary>
        public const float MovedOffPerHeight = 0.02f;

        /// <summary>
        /// The footwork in <paramref name="track"/> (samples in time order from the Rep's cue), for a Fencer of
        /// <paramref name="fencerHeightMeters"/>. The onset is measured from <paramref name="cueSeconds"/>. An empty track reads as none.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The height is not a positive number.</exception>
        public static FootworkReading Classify(IReadOnlyList<HeadSample> track, double cueSeconds, float fencerHeightMeters)
        {
            if (!(fencerHeightMeters > 0.0f))
            {
                throw new ArgumentOutOfRangeException(nameof(fencerHeightMeters), fencerHeightMeters, "Must be positive.");
            }

            if (track == null || track.Count == 0)
            {
                return new FootworkReading(FootworkAction.None, null, 0.0f, 0.0f, 0.0f, 0.0f);
            }

            var start = track[0].HeadInStrip;
            var forwardTravel = 0.0f;
            var backwardTravel = 0.0f;
            var headDrop = 0.0f;
            double? onsetSeconds = null;
            var forwardTrack = new List<ForwardPosition>(track.Count);
            foreach (var sample in track)
            {
                var forward = sample.HeadInStrip.Z - start.Z;
                forwardTravel = Math.Max(forwardTravel, forward);
                backwardTravel = Math.Max(backwardTravel, -forward);
                headDrop = Math.Max(headDrop, start.Y - sample.HeadInStrip.Y);
                if (!onsetSeconds.HasValue && Math.Abs(forward) >= MovedOffPerHeight * fencerHeightMeters)
                {
                    onsetSeconds = sample.TimeSeconds - cueSeconds;
                }

                forwardTrack.Add(new ForwardPosition(sample.TimeSeconds, sample.HeadInStrip.Z));
            }

            var peakForwardSpeed = 0.0f;
            foreach (var speed in LungeStart.MeasureForwardSpeeds(forwardTrack))
            {
                peakForwardSpeed = Math.Max(peakForwardSpeed, speed ?? 0.0f);
            }

            var finalForward = track[track.Count - 1].HeadInStrip.Z - start.Z;
            var action = ReadAction(forwardTravel, backwardTravel, finalForward, peakForwardSpeed, fencerHeightMeters);
            return new FootworkReading(action, onsetSeconds, forwardTravel, backwardTravel, peakForwardSpeed, headDrop);
        }

        private static FootworkAction ReadAction(float forwardTravel, float backwardTravel, float finalForward, float peakForwardSpeed, float heightMeters)
        {
            if (peakForwardSpeed >= LungeStart.MinPeakSpeedMetersPerSecond && forwardTravel >= LungeMinTravelPerHeight * heightMeters)
            {
                return forwardTravel >= StepLungeMinTravelPerHeight * heightMeters ? FootworkAction.StepLunge : FootworkAction.Lunge;
            }

            var stepTravel = StepMinTravelPerHeight * heightMeters;
            if (backwardTravel >= stepTravel && backwardTravel >= forwardTravel)
            {
                return FootworkAction.Retreat;
            }

            if (forwardTravel >= CheckMinTravelPerHeight * heightMeters && finalForward <= forwardTravel * (1.0f - CheckReturnFraction))
            {
                return FootworkAction.Check;
            }

            return forwardTravel >= stepTravel ? FootworkAction.Advance : FootworkAction.None;
        }
    }

    /// <summary>
    /// Collects the head samples of the current Rep from its cue, in the Strip frame, and reads its footwork with the
    /// <see cref="FootworkClassifier"/>. Samples older than <see cref="MaxBufferedSeconds"/> are dropped.
    /// </summary>
    public sealed class RepFootworkTracker
    {
        /// <summary>Longer than any footwork window; keeps the buffer small if a Rep is never judged.</summary>
        public const double MaxBufferedSeconds = 10.0;

        private readonly List<HeadSample> _samples = new List<HeadSample>();
        private double _cueSeconds;

        public int SampleCount => _samples.Count;

        /// <summary>A Rep was cued at <paramref name="cueSeconds"/>: forget the previous Rep's motion.</summary>
        public void BeginRep(double cueSeconds)
        {
            _cueSeconds = cueSeconds;
            _samples.Clear();
        }

        /// <summary>Adds a frame. Samples must come in time order; an older or same-time sample, or one before the cue, is ignored.</summary>
        public void AddSample(HeadSample sample)
        {
            if (sample.TimeSeconds < _cueSeconds || (_samples.Count > 0 && sample.TimeSeconds <= _samples[_samples.Count - 1].TimeSeconds))
            {
                return;
            }

            _samples.Add(sample);
            var expired = 0;
            while (expired < _samples.Count && _samples[expired].TimeSeconds < sample.TimeSeconds - MaxBufferedSeconds)
            {
                expired++;
            }

            _samples.RemoveRange(0, expired);
        }

        /// <summary>The Rep's footwork so far, for a Fencer of <paramref name="fencerHeightMeters"/>.</summary>
        public FootworkReading Read(float fencerHeightMeters)
        {
            return FootworkClassifier.Classify(_samples, _cueSeconds, fencerHeightMeters);
        }
    }
}
