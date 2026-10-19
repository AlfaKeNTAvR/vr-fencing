// Synthetic Fencer recordings for the recorder and replay tests, and the source of the checked-in fixture clip
// (Fixtures/synthetic-lunge.fencer) until a real clip comes from the headset.
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using VrFencing.Session;
using VrFencing.Settings;
using VrFencing.Tracking;

namespace VrFencing.Tests.Tracking
{
    internal static class FencerRecordingTestData
    {
        public const double FrameSeconds = 1.0 / 72.0;
        public const int JointCount = FencerRecordingHeader.FullBodyJointCount;

        /// <summary>The synthetic clip: 72 frames, the body untracked for the first 6, a 1 s pause after frame 47.</summary>
        public const int FixtureFrameCount = 72;
        public const int FixtureUntrackedBodyFrames = 6;
        public const int FixturePauseAfterFrame = 47;
        public const double FixturePauseSeconds = 1.0;

        private const string FixtureFileName = "synthetic-lunge.fencer";

        public static readonly DateTimeOffset FixtureStartedUtc = new DateTimeOffset(2026, 10, 19, 21, 30, 5, TimeSpan.Zero);

        public static FencerRecordingHeader CreateHeader(float? stripLengthMeters = 6.3f)
        {
            return new FencerRecordingHeader(
                FencerRecordingFile.CurrentFormatVersion,
                FencerRecordingHeader.FullBodyJointSet,
                JointCount,
                1.65f,
                Hand.Right,
                stripLengthMeters,
                "0.1.0-test",
                FixtureStartedUtc);
        }

        /// <summary>A rest pose: joints stacked up a vertical line, each the child of the one before.</summary>
        public static FencerBindPose CreateBindPose(float scale = 1.0f)
        {
            var joints = new TrackedPose[JointCount];
            var parents = new int[JointCount];
            for (var joint = 0; joint < JointCount; joint++)
            {
                joints[joint] = new TrackedPose(new Vector3(0.0f, joint * 0.02f * scale, 0.0f), Quaternion.Identity);
                parents[joint] = joint - 1;
            }

            return new FencerBindPose(joints, parents);
        }

        /// <summary>
        /// A lunge along the Strip: the head and every joint glide forward 0.6 m over the clip, the sword controller
        /// ahead of the head, each joint at its own height and turned a little about the vertical.
        /// </summary>
        public static FencerFrame CreateFrame(int index, double timeSeconds, bool isBodyTracked)
        {
            var progress = (float)index / FixtureFrameCount;
            var forward = 0.6f * progress;
            var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.3f * progress);
            var joints = new TrackedPose?[JointCount];
            if (isBodyTracked)
            {
                for (var joint = 0; joint < JointCount; joint++)
                {
                    // Every 13th joint untracked, so per-joint validity is exercised.
                    joints[joint] = joint % 13 == 5
                        ? (TrackedPose?)null
                        : new TrackedPose(new Vector3(0.01f * (joint % 7), 0.02f * joint, forward), turn);
                }
            }

            return new FencerFrame(
                timeSeconds,
                new TrackedPose(new Vector3(0.0f, 1.55f, forward), turn),
                index % 10 == 9 ? (TrackedPose?)null : new TrackedPose(new Vector3(0.25f, 1.1f, forward + 0.4f), turn),
                isBodyTracked ? (index % 2 == 0 ? BodyTrackingFidelity.High : BodyTrackingFidelity.Low) : (BodyTrackingFidelity?)null,
                joints);
        }

        /// <summary>The fixture clip's frames, in order.</summary>
        public static IReadOnlyList<FencerFrame> CreateFixtureFrames()
        {
            var frames = new List<FencerFrame>();
            for (var index = 0; index < FixtureFrameCount; index++)
            {
                var pause = index > FixturePauseAfterFrame ? FixturePauseSeconds : 0.0;
                frames.Add(CreateFrame(index, (index * FrameSeconds) + pause, index >= FixtureUntrackedBodyFrames));
            }

            return frames;
        }

        /// <summary>The fixture clip as file bytes: the bind pose arrives with the first tracked body frame.</summary>
        public static byte[] WriteFixture()
        {
            // The writer closes the stream; a closed MemoryStream still hands out its bytes.
            var memory = new MemoryStream();
            using (var writer = new FencerRecordingWriter(memory, CreateHeader()))
            {
                var frames = CreateFixtureFrames();
                for (var index = 0; index < frames.Count; index++)
                {
                    if (index == FixtureUntrackedBodyFrames)
                    {
                        writer.WriteBindPose(CreateBindPose());
                    }

                    writer.WriteFrame(frames[index]);
                }
            }

            return memory.ToArray();
        }

        /// <summary>The checked-in clip, found next to this source file in the Unity project and in the dotnet Core tests alike.</summary>
        public static string GetFixturePath([CallerFilePath] string sourcePath = "")
        {
            var besideSource = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(sourcePath)), "Fixtures", FixtureFileName);
            // Unity runs Edit-mode tests from the project folder, in case its compiler maps source paths elsewhere.
            var inProject = Path.GetFullPath(Path.Combine("Assets", "Tests", "EditMode", "Tracking", "Fixtures", FixtureFileName));
            return File.Exists(besideSource) || !File.Exists(inProject) ? besideSource : inProject;
        }
    }
}
