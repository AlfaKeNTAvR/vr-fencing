using System;
using System.Collections.Generic;
using VrFencing.Session;
using VrFencing.Settings;

namespace VrFencing.Tracking
{
    /// <summary>
    /// What a Fencer recording says about itself (PER-79): written once at the start of the file as JSON, so a person can
    /// read it with any text viewer and a later format can add fields without breaking older readers.
    /// </summary>
    public sealed class FencerRecordingHeader
    {
        /// <summary>The joint set the multiplayer plan sends (PER-55): OVRPlugin's Full Body, legs included.</summary>
        public const string FullBodyJointSet = "FullBody";

        /// <summary>Joints in OVRPlugin's Full Body set: the 70 upper-body joints in the same order, then 14 for the legs.</summary>
        public const int FullBodyJointCount = 84;

        public FencerRecordingHeader(
            int formatVersion,
            string jointSet,
            int jointCount,
            float fencerHeightMeters,
            Hand hand,
            float? stripLengthMeters,
            string appVersion,
            DateTimeOffset startedUtc)
        {
            if (jointCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(jointCount), jointCount, "A recording cannot have a negative joint count.");
            }

            FormatVersion = formatVersion;
            JointSet = jointSet ?? string.Empty;
            JointCount = jointCount;
            FencerHeightMeters = fencerHeightMeters;
            Hand = hand;
            StripLengthMeters = stripLengthMeters;
            AppVersion = appVersion ?? string.Empty;
            StartedUtc = startedUtc;
        }

        /// <summary>The binary layout of the records after the header; a reader refuses versions newer than it knows.</summary>
        public int FormatVersion { get; }

        public string JointSet { get; }

        /// <summary>How many joint poses each frame and each bind pose holds; fixes the size of every record.</summary>
        public int JointCount { get; }

        public float FencerHeightMeters { get; }

        /// <summary>The sword hand. Poses are recorded true, never mirrored; this says which controller the sword controller is.</summary>
        public Hand Hand { get; }

        /// <summary>The Strip's full length, run-back included, or null when no Strip was placed (poses then use the room origin).</summary>
        public float? StripLengthMeters { get; }

        public string AppVersion { get; }

        public DateTimeOffset StartedUtc { get; }
    }

    /// <summary>
    /// One rendered frame of the Fencer, in the Fencer's Strip frame (+x the Fencer's right, +y up, +z toward the far
    /// end, origin on the floor at the En Garde Line). Every pose is null when the headset did not track it that frame.
    /// </summary>
    public sealed class FencerFrame
    {
        /// <param name="joints">
        /// One entry per joint of the recording's joint set. Not copied: a recorder can reuse one array every frame,
        /// so the frame is valid only until the array is next filled.
        /// </param>
        public FencerFrame(
            double timeSeconds,
            TrackedPose? head,
            TrackedPose? swordController,
            BodyTrackingFidelity? bodyFidelity,
            IReadOnlyList<TrackedPose?> joints)
        {
            TimeSeconds = timeSeconds;
            Head = head;
            SwordController = swordController;
            BodyFidelity = bodyFidelity;
            Joints = joints ?? throw new ArgumentNullException(nameof(joints));
        }

        /// <summary>Seconds since the recording started. Jumps where the app was paused (the headset asleep).</summary>
        public double TimeSeconds { get; }

        public TrackedPose? Head { get; }

        /// <summary>The sword-hand controller anchor, from which the blade pose is built the same way as live.</summary>
        public TrackedPose? SwordController { get; }

        /// <summary>Null when body tracking gave no body this frame (not running, or no valid state).</summary>
        public BodyTrackingFidelity? BodyFidelity { get; }

        public IReadOnlyList<TrackedPose?> Joints { get; }
    }

    /// <summary>
    /// The body tracker's skeleton in its rest pose (the "T-pose" Meta's retargeter calibrates against), in the
    /// skeleton's own space, with each joint's parent index (-1 for the root). It changes when the runtime re-measures
    /// the Fencer, so a recording can hold several.
    /// </summary>
    public sealed class FencerBindPose
    {
        public FencerBindPose(IReadOnlyList<TrackedPose> joints, IReadOnlyList<int> parentIndices)
        {
            Joints = joints ?? throw new ArgumentNullException(nameof(joints));
            ParentIndices = parentIndices ?? throw new ArgumentNullException(nameof(parentIndices));
            if (joints.Count != parentIndices.Count)
            {
                throw new ArgumentException($"A bind pose needs one parent index per joint ({joints.Count} joints, {parentIndices.Count} parents).");
            }
        }

        public IReadOnlyList<TrackedPose> Joints { get; }

        public IReadOnlyList<int> ParentIndices { get; }
    }

    /// <summary>A bind pose and the first frame it applies to.</summary>
    public readonly struct FencerBindPoseChange
    {
        public FencerBindPoseChange(int firstFrameIndex, FencerBindPose bindPose)
        {
            FirstFrameIndex = firstFrameIndex;
            BindPose = bindPose;
        }

        public int FirstFrameIndex { get; }

        public FencerBindPose BindPose { get; }
    }

    /// <summary>A whole Fencer recording as read back from a file.</summary>
    public sealed class FencerRecording
    {
        public FencerRecording(
            FencerRecordingHeader header,
            IReadOnlyList<FencerFrame> frames,
            IReadOnlyList<FencerBindPoseChange> bindPoses,
            bool isTruncated)
        {
            Header = header ?? throw new ArgumentNullException(nameof(header));
            Frames = frames ?? throw new ArgumentNullException(nameof(frames));
            BindPoses = bindPoses ?? throw new ArgumentNullException(nameof(bindPoses));
            IsTruncated = isTruncated;
        }

        public FencerRecordingHeader Header { get; }

        /// <summary>In time order, as recorded.</summary>
        public IReadOnlyList<FencerFrame> Frames { get; }

        /// <summary>In file order; empty when body tracking never gave a skeleton during the recording.</summary>
        public IReadOnlyList<FencerBindPoseChange> BindPoses { get; }

        /// <summary>The last record was cut short, as when the app is killed mid-write; everything before it is kept.</summary>
        public bool IsTruncated { get; }
    }
}
