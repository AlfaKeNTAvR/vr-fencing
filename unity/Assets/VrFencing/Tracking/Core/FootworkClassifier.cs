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
    /// (<c>back</c>), where it is now, how far it went down (the head drop), and the steps: each continuous move of the
    /// head along the Strip, split where the speed dips to under <see cref="StepValleyFraction"/> of its peak and rises
    /// again, so quick advances in a row count one by one (PER-88). In order:
    /// </para>
    /// <list type="number">
    /// <item>A lunge: one forward step at least <see cref="LungeMinTravelPerHeight"/> long, as fast as
    /// <see cref="LungeStart.MinPeakSpeedMetersPerSecond"/>, with the head down at least
    /// <see cref="LungeMinHeadDropPerHeight"/>. A step-lunge if a step of its own came before it, or fwd reached
    /// <see cref="StepLungeMinTravelPerHeight"/>. A recovery afterwards does not change it. Speed alone never makes a
    /// lunge: a fast advance is a short step, and keeps the head up.</item>
    /// <item>A retreat: back reached <see cref="StepMinTravelPerHeight"/> and is at least fwd.</item>
    /// <item>A check: fwd reached <see cref="CheckMinTravelPerHeight"/> and the head came back at least
    /// <see cref="CheckReturnFraction"/> of the way.</item>
    /// <item>An advance: fwd reached <see cref="StepMinTravelPerHeight"/> and the head stayed forward, in one or more
    /// steps.</item>
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
        /// Length of one forward step that can be a lunge: 0.25 of the height (41 cm for 1.65 m). Above an advance
        /// (about 0.2), below a lunge's head travel (estimated 0.3 to 0.45: the lunge moves the front foot 0.6 of the
        /// height, the hips and head roughly half to two thirds of that). Tune on the headset with the logged travel.
        /// </summary>
        public const float LungeMinTravelPerHeight = 0.25f;

        /// <summary>
        /// Forward head travel that makes a lunge a step-lunge even when its advance did not show as a step of its own:
        /// 0.5 of the height (82 cm for 1.65 m), between a lunge's estimated head travel and a lunge plus an advance
        /// (about 0.5 to 0.65).
        /// </summary>
        public const float StepLungeMinTravelPerHeight = 0.5f;

        /// <summary>
        /// How far the head must go down for a lunge: 0.04 of the height (7 cm for 1.65 m). A lunge lowers the head
        /// 10 to 25 cm as the front knee bends and the back leg drives; an advance keeps it within a few centimeters.
        /// </summary>
        public const float LungeMinHeadDropPerHeight = 0.04f;

        /// <summary>
        /// Forward head travel a check needs: 0.03 of the height (5 cm for 1.65 m), a short half step. Lowered from 0.04
        /// after the 2026-10-19 headset test, where checks read as no step (PER-88).
        /// </summary>
        public const float CheckMinTravelPerHeight = 0.03f;

        /// <summary>A check comes back at least this share of its forward travel: it ends at most half as far forward as it got.</summary>
        public const float CheckReturnFraction = 0.5f;

        /// <summary>
        /// The head has moved off its place once it is this far from it: 0.02 of the height (3 cm for 1.65 m), above
        /// en garde sway. Its time is the footwork's onset (the reaction time of a footwork call).
        /// </summary>
        public const float MovedOffPerHeight = 0.02f;

        /// <summary>
        /// One step ends where the head's speed along the Strip dips under this share of the step's peak and then rises
        /// again by <see cref="StepReboundMetersPerSecond"/>: the next step of a series. A lunge's speed rises and falls once.
        /// </summary>
        public const float StepValleyFraction = 0.5f;

        /// <summary>The rise after a dip that starts the next step; keeps tracking jitter in one step from splitting it.</summary>
        public const float StepReboundMetersPerSecond = 0.3f;

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

            var speeds = LungeStart.MeasureForwardSpeeds(forwardTrack);
            var peakForwardSpeed = 0.0f;
            foreach (var speed in speeds)
            {
                peakForwardSpeed = Math.Max(peakForwardSpeed, speed ?? 0.0f);
            }

            var motion = new HeadMotion(
                forwardTravel,
                backwardTravel,
                finalForward: track[track.Count - 1].HeadInStrip.Z - start.Z,
                headDrop,
                FindSteps(forwardTrack, speeds));
            var action = ReadAction(motion, fencerHeightMeters);
            var stepCount = CountSteps(action, motion.Steps, fencerHeightMeters);
            return new FootworkReading(action, onsetSeconds, forwardTravel, backwardTravel, peakForwardSpeed, headDrop, stepCount);
        }

        private static FootworkAction ReadAction(HeadMotion motion, float heightMeters)
        {
            var stepTravel = StepMinTravelPerHeight * heightMeters;
            var lungeIndex = FindLungeStep(motion, heightMeters);
            if (lungeIndex >= 0)
            {
                var hasStepBefore = false;
                for (var i = 0; i < lungeIndex; i++)
                {
                    hasStepBefore |= motion.Steps[i].LengthMeters >= stepTravel;
                }

                return hasStepBefore || motion.ForwardTravel >= StepLungeMinTravelPerHeight * heightMeters
                    ? FootworkAction.StepLunge
                    : FootworkAction.Lunge;
            }

            if (motion.BackwardTravel >= stepTravel && motion.BackwardTravel >= motion.ForwardTravel)
            {
                return FootworkAction.Retreat;
            }

            if (motion.ForwardTravel >= CheckMinTravelPerHeight * heightMeters
                && motion.FinalForward <= motion.ForwardTravel * (1.0f - CheckReturnFraction))
            {
                return FootworkAction.Check;
            }

            return motion.ForwardTravel >= stepTravel ? FootworkAction.Advance : FootworkAction.None;
        }

        /// <summary>The first forward step long, fast and low enough for a lunge, or -1.</summary>
        private static int FindLungeStep(HeadMotion motion, float heightMeters)
        {
            if (motion.HeadDrop < LungeMinHeadDropPerHeight * heightMeters)
            {
                return -1;
            }

            for (var i = 0; i < motion.Steps.Count; i++)
            {
                var step = motion.Steps[i];
                if (step.LengthMeters >= LungeMinTravelPerHeight * heightMeters && step.PeakSpeedMetersPerSecond >= LungeStart.MinPeakSpeedMetersPerSecond)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Advances or retreats counted for the read: the steps at least a step long in the action's direction, and at
        /// least one. Zero for the other actions.
        /// </summary>
        private static int CountSteps(FootworkAction action, IReadOnlyList<HeadStep> steps, float heightMeters)
        {
            if (action != FootworkAction.Advance && action != FootworkAction.Retreat)
            {
                return 0;
            }

            var direction = action == FootworkAction.Advance ? 1.0f : -1.0f;
            var count = 0;
            foreach (var step in steps)
            {
                count += step.LengthMeters * direction >= StepMinTravelPerHeight * heightMeters ? 1 : 0;
            }

            return Math.Max(count, 1);
        }

        /// <summary>
        /// Splits the head's motion along the Strip into steps: a step starts when the speed reaches
        /// <see cref="LungeStart.OnsetSpeedMetersPerSecond"/> one way, and ends when it falls back under it, turns
        /// around, or dips under <see cref="StepValleyFraction"/> of its peak and rises again (then the next step starts
        /// at the dip).
        /// </summary>
        private static List<HeadStep> FindSteps(IReadOnlyList<ForwardPosition> track, float?[] speeds)
        {
            var steps = new List<HeadStep>();
            var stepStart = -1;
            var direction = 0.0f;
            var peak = 0.0f;
            var valleyIndex = -1;
            var valleySpeed = 0.0f;
            for (var i = 0; i < track.Count; i++)
            {
                if (!speeds[i].HasValue)
                {
                    continue;
                }

                var speed = speeds[i].Value;
                if (stepStart < 0)
                {
                    if (Math.Abs(speed) >= LungeStart.OnsetSpeedMetersPerSecond)
                    {
                        stepStart = Math.Max(i - 1, 0);
                        direction = Math.Sign(speed);
                        peak = Math.Abs(speed);
                        valleyIndex = -1;
                    }

                    continue;
                }

                var speedAlong = speed * direction;
                if (speedAlong < LungeStart.OnsetSpeedMetersPerSecond)
                {
                    steps.Add(new HeadStep(track[i].ForwardMeters - track[stepStart].ForwardMeters, peak));
                    stepStart = -1;
                    continue;
                }

                if (valleyIndex < 0)
                {
                    if (speedAlong > peak)
                    {
                        peak = speedAlong;
                    }
                    else if (speedAlong < peak * StepValleyFraction)
                    {
                        valleyIndex = i;
                        valleySpeed = speedAlong;
                    }
                }
                else if (speedAlong < valleySpeed)
                {
                    valleyIndex = i;
                    valleySpeed = speedAlong;
                }
                else if (speedAlong >= valleySpeed + StepReboundMetersPerSecond)
                {
                    steps.Add(new HeadStep(track[valleyIndex].ForwardMeters - track[stepStart].ForwardMeters, peak));
                    stepStart = valleyIndex;
                    peak = speedAlong;
                    valleyIndex = -1;
                }
            }

            if (stepStart >= 0)
            {
                steps.Add(new HeadStep(track[track.Count - 1].ForwardMeters - track[stepStart].ForwardMeters, peak));
            }

            return steps;
        }

        /// <summary>One continuous move of the head along the Strip: signed length (+ toward the Dummy) and peak speed.</summary>
        private readonly struct HeadStep
        {
            public HeadStep(float lengthMeters, float peakSpeedMetersPerSecond)
            {
                LengthMeters = lengthMeters;
                PeakSpeedMetersPerSecond = peakSpeedMetersPerSecond;
            }

            public float LengthMeters { get; }

            public float PeakSpeedMetersPerSecond { get; }
        }

        /// <summary>What the classifier reads the action from, all measured from the head's place at the cue.</summary>
        private readonly struct HeadMotion
        {
            public HeadMotion(float forwardTravel, float backwardTravel, float finalForward, float headDrop, IReadOnlyList<HeadStep> steps)
            {
                ForwardTravel = forwardTravel;
                BackwardTravel = backwardTravel;
                FinalForward = finalForward;
                HeadDrop = headDrop;
                Steps = steps;
            }

            public float ForwardTravel { get; }

            public float BackwardTravel { get; }

            public float FinalForward { get; }

            public float HeadDrop { get; }

            public IReadOnlyList<HeadStep> Steps { get; }
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
