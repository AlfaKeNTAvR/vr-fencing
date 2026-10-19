using System.Collections.Generic;
using VrFencing.Session;
using VrFencing.Settings;
using VrFencing.Strip;

namespace VrFencing.Tracking
{
    /// <summary>
    /// One frame of tracking, in the room (Unity world, floor-level origin). The arm joints are the blade arm's;
    /// each is null when the tracker did not report it. <see cref="Fidelity"/> is null when body tracking is not running.
    /// </summary>
    public readonly struct BodySample
    {
        public BodySample(
            double timeSeconds,
            System.Numerics.Vector3 headInRoom,
            BodyTrackingFidelity? fidelity,
            TrackedPose? shoulderInRoom,
            TrackedPose? elbowInRoom,
            TrackedPose? wristInRoom)
        {
            TimeSeconds = timeSeconds;
            HeadInRoom = headInRoom;
            Fidelity = fidelity;
            ShoulderInRoom = shoulderInRoom;
            ElbowInRoom = elbowInRoom;
            WristInRoom = wristInRoom;
        }

        public double TimeSeconds { get; }

        public System.Numerics.Vector3 HeadInRoom { get; }

        public BodyTrackingFidelity? Fidelity { get; }

        public TrackedPose? ShoulderInRoom { get; }

        public TrackedPose? ElbowInRoom { get; }

        public TrackedPose? WristInRoom { get; }

        public bool HasWholeArm => ShoulderInRoom.HasValue && ElbowInRoom.HasValue && WristInRoom.HasValue;
    }

    /// <summary>
    /// Collects the samples of the current Rep (from its cue) and, when the Rep is judged, turns them into the
    /// rep log's <see cref="ArmPoseSnapshot"/>: the arm at judging time, the tracking fidelity, and whether the arm
    /// was extended (<see cref="ArmExtension"/>) when the lunge started (<see cref="LungeStart"/>).
    /// Logged only: nothing is judged or shown from it.
    /// </summary>
    public sealed class RepArmTracker
    {
        /// <summary>Samples older than this are dropped, so a long wait between Reps keeps the buffer small.</summary>
        public const double MaxBufferedSeconds = 10.0;

        /// <summary>
        /// A sample counts for a moment only if it is at most this old (a few frames at 72 Hz). Older means the
        /// tracker stopped delivering, and the moment is logged as not tracked rather than with a stale pose.
        /// </summary>
        public const double MaxSampleAgeSeconds = 0.2;

        private readonly List<BodySample> _samples = new List<BodySample>();

        public int SampleCount => _samples.Count;

        /// <summary>A new Rep was cued: forget the previous Rep's motion, so its lunge cannot count for this one.</summary>
        public void BeginRep()
        {
            _samples.Clear();
        }

        /// <summary>Adds a frame. Samples must come in time order; an older or same-time sample is ignored.</summary>
        public void AddSample(BodySample sample)
        {
            if (_samples.Count > 0 && sample.TimeSeconds <= _samples[_samples.Count - 1].TimeSeconds)
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

        /// <summary>
        /// The rep log's arm pose for a Rep judged at <paramref name="judgedAtSeconds"/>, in the Strip frame of
        /// <paramref name="enGardeLine"/>. Null when body tracking was not running at judging time (no sample, a
        /// stale one, or one without a fidelity).
        /// </summary>
        public ArmPoseSnapshot SnapshotAtJudging(TrackedPose enGardeLine, double judgedAtSeconds)
        {
            var latestIndex = FindLatestSampleIndex(judgedAtSeconds, requireWholeArm: false);
            if (latestIndex < 0 || !_samples[latestIndex].Fidelity.HasValue)
            {
                return null;
            }

            var latest = _samples[latestIndex];
            var lunge = MeasureLunge(enGardeLine, latestIndex);
            return new ArmPoseSnapshot(
                ToStripFrame(enGardeLine, latest.ShoulderInRoom),
                ToStripFrame(enGardeLine, latest.ElbowInRoom),
                ToStripFrame(enGardeLine, latest.WristInRoom),
                latest.Fidelity,
                lunge.IsArmExtended,
                lunge.ElbowAngleDegrees,
                lunge.StartSeconds.HasValue ? judgedAtSeconds - lunge.StartSeconds.Value : (double?)null);
        }

        private (double? StartSeconds, bool? IsArmExtended, float? ElbowAngleDegrees) MeasureLunge(TrackedPose enGardeLine, int lastIndex)
        {
            var track = new List<ForwardPosition>(lastIndex + 1);
            for (var i = 0; i <= lastIndex; i++)
            {
                var head = StripFrame.ToStripFrame(enGardeLine, _samples[i].HeadInRoom);
                track.Add(new ForwardPosition(_samples[i].TimeSeconds, head.Z));
            }

            var startSeconds = LungeStart.FindStartSeconds(track);
            if (!startSeconds.HasValue)
            {
                return (null, null, null);
            }

            var armIndex = FindLatestSampleIndex(startSeconds.Value, requireWholeArm: true);
            if (armIndex < 0)
            {
                return (startSeconds, null, null);
            }

            var arm = _samples[armIndex];
            var shoulder = StripFrame.ToStripFrame(enGardeLine, arm.ShoulderInRoom.Value.Position);
            var elbow = StripFrame.ToStripFrame(enGardeLine, arm.ElbowInRoom.Value.Position);
            var wrist = StripFrame.ToStripFrame(enGardeLine, arm.WristInRoom.Value.Position);
            return (startSeconds, ArmExtension.IsExtended(shoulder, elbow, wrist), ArmExtension.ElbowAngleDegrees(shoulder, elbow, wrist));
        }

        /// <summary>The newest sample at or before <paramref name="atSeconds"/> and at most <see cref="MaxSampleAgeSeconds"/> older, or -1.</summary>
        private int FindLatestSampleIndex(double atSeconds, bool requireWholeArm)
        {
            for (var i = _samples.Count - 1; i >= 0; i--)
            {
                var sample = _samples[i];
                if (sample.TimeSeconds > atSeconds)
                {
                    continue;
                }

                if (atSeconds - sample.TimeSeconds > MaxSampleAgeSeconds)
                {
                    return -1;
                }

                if (!requireWholeArm || sample.HasWholeArm)
                {
                    return i;
                }
            }

            return -1;
        }

        private static TrackedPose? ToStripFrame(TrackedPose enGardeLine, TrackedPose? poseInRoom)
        {
            return poseInRoom.HasValue ? StripFrame.ToStripFrame(enGardeLine, poseInRoom.Value) : (TrackedPose?)null;
        }
    }
}
