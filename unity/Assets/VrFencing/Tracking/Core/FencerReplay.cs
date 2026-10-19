using System;
using System.Numerics;
using VrFencing.Settings;

namespace VrFencing.Tracking
{
    /// <summary>
    /// Plays a <see cref="FencerRecording"/> back on its own clock (PER-79): the pose at any playback time, blended
    /// between the two recorded frames around it, so a replay at the Editor's frame rate moves as smoothly as the
    /// headset's 72 Hz did. Playback time 0 is the first frame. Plain C# so the clock is tested without a scene.
    /// </summary>
    public sealed class FencerReplay
    {
        /// <summary>
        /// Frames further apart than this are not blended: the gap is a pause (the headset asleep) or a dropped stretch,
        /// and sliding the body across it would show motion that never happened. The earlier frame holds instead.
        /// </summary>
        public const double MaxBlendGapSeconds = 0.25;

        private readonly FencerRecording _recording;
        private readonly double _firstFrameSeconds;

        public FencerReplay(FencerRecording recording)
        {
            _recording = recording ?? throw new ArgumentNullException(nameof(recording));
            if (recording.Frames.Count == 0)
            {
                throw new ArgumentException("The recording has no frames to play.", nameof(recording));
            }

            _firstFrameSeconds = recording.Frames[0].TimeSeconds;
            DurationSeconds = recording.Frames[recording.Frames.Count - 1].TimeSeconds - _firstFrameSeconds;
        }

        public FencerRecording Recording => _recording;

        /// <summary>From the first frame to the last.</summary>
        public double DurationSeconds { get; }

        /// <summary>Where <paramref name="playbackSeconds"/> lands in the recording: wrapped when looping, else held at either end.</summary>
        public double ToRecordingTime(double playbackSeconds, bool isLooping)
        {
            if (DurationSeconds <= 0.0)
            {
                return 0.0;
            }

            if (isLooping)
            {
                var wrapped = playbackSeconds % DurationSeconds;
                return wrapped < 0.0 ? wrapped + DurationSeconds : wrapped;
            }

            return Math.Max(0.0, Math.Min(DurationSeconds, playbackSeconds));
        }

        /// <summary>The Fencer at <paramref name="playbackSeconds"/>, with <see cref="FencerFrame.TimeSeconds"/> the recording time it came from.</summary>
        public FencerFrame Sample(double playbackSeconds, bool isLooping)
        {
            var frames = _recording.Frames;
            var recordingSeconds = ToRecordingTime(playbackSeconds, isLooping) + _firstFrameSeconds;
            var before = FindFrameAtOrBefore(recordingSeconds);
            if (before >= frames.Count - 1)
            {
                return frames[frames.Count - 1];
            }

            var earlier = frames[before];
            var later = frames[before + 1];
            var gapSeconds = later.TimeSeconds - earlier.TimeSeconds;
            if (gapSeconds <= 0.0 || gapSeconds > MaxBlendGapSeconds)
            {
                return earlier;
            }

            var weight = (float)Math.Max(0.0, Math.Min(1.0, (recordingSeconds - earlier.TimeSeconds) / gapSeconds));
            var joints = new TrackedPose?[earlier.Joints.Count];
            for (var joint = 0; joint < joints.Length; joint++)
            {
                joints[joint] = Blend(earlier.Joints[joint], later.Joints[joint], weight);
            }

            var nearer = weight < 0.5f ? earlier : later;
            return new FencerFrame(
                recordingSeconds,
                Blend(earlier.Head, later.Head, weight),
                Blend(earlier.SwordController, later.SwordController, weight),
                nearer.BodyFidelity,
                joints);
        }

        /// <summary>
        /// Index of the bind pose in effect at <paramref name="playbackSeconds"/> in <see cref="FencerRecording.BindPoses"/>,
        /// or -1 when the recording has none. A change of index means the retargeter must re-calibrate.
        /// </summary>
        public int GetBindPoseIndex(double playbackSeconds, bool isLooping)
        {
            var bindPoses = _recording.BindPoses;
            if (bindPoses.Count == 0)
            {
                return -1;
            }

            var frame = FindFrameAtOrBefore(ToRecordingTime(playbackSeconds, isLooping) + _firstFrameSeconds);
            var index = -1;
            for (var change = 0; change < bindPoses.Count && bindPoses[change].FirstFrameIndex <= frame; change++)
            {
                index = change;
            }

            // Before the first skeleton arrived, the first one recorded is still the best rest pose to show.
            return index < 0 ? 0 : index;
        }

        /// <summary>The last frame at or before <paramref name="recordingSeconds"/> (binary search; frames are in time order).</summary>
        private int FindFrameAtOrBefore(double recordingSeconds)
        {
            var frames = _recording.Frames;
            var low = 0;
            var high = frames.Count - 1;
            while (low < high)
            {
                var middle = (low + high + 1) / 2;
                if (frames[middle].TimeSeconds <= recordingSeconds)
                {
                    low = middle;
                }
                else
                {
                    high = middle - 1;
                }
            }

            return low;
        }

        /// <summary>Blended when both frames tracked the pose; otherwise the nearer frame's pose, tracked or not.</summary>
        private static TrackedPose? Blend(TrackedPose? earlier, TrackedPose? later, float weight)
        {
            if (earlier.HasValue && later.HasValue)
            {
                return new TrackedPose(
                    Vector3.Lerp(earlier.Value.Position, later.Value.Position, weight),
                    Quaternion.Slerp(earlier.Value.Rotation, later.Value.Rotation, weight));
            }

            return weight < 0.5f ? earlier : later;
        }
    }
}
