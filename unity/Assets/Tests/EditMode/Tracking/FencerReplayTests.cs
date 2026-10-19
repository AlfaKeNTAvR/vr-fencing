// Replaying a Fencer recording on its own clock (PER-79): smooth between frames, looping or holding at the end,
// and never sliding the body across a pause.
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Session;
using VrFencing.Settings;
using VrFencing.Tracking;

namespace VrFencing.Tests.Tracking
{
    public class FencerReplayTests
    {
        private const int JointCount = 2;

        private static TrackedPose At(float z)
        {
            return new TrackedPose(new Vector3(0.0f, 1.5f, z), Quaternion.Identity);
        }

        private static FencerFrame Frame(double timeSeconds, float z, bool isBodyTracked = true, TrackedPose? swordController = null)
        {
            var joints = new TrackedPose?[JointCount];
            if (isBodyTracked)
            {
                for (var joint = 0; joint < JointCount; joint++)
                {
                    joints[joint] = At(z);
                }
            }

            return new FencerFrame(
                timeSeconds,
                At(z),
                swordController ?? At(z + 0.5f),
                isBodyTracked ? BodyTrackingFidelity.High : (BodyTrackingFidelity?)null,
                joints);
        }

        private static FencerReplay Replay(IReadOnlyList<FencerFrame> frames, params FencerBindPoseChange[] bindPoses)
        {
            var header = new FencerRecordingHeader(1, "Test", JointCount, 1.65f, Hand.Right, null, "test", default);
            return new FencerReplay(new FencerRecording(header, frames, bindPoses, isTruncated: false));
        }

        private static FencerBindPose BindPose()
        {
            return new FencerBindPose(new[] { At(0.0f), At(0.0f) }, new[] { -1, 0 });
        }

        [Test]
        public void PlaybackStartsAtTheFirstFrameAndEndsAtTheLast()
        {
            var replay = Replay(new[] { Frame(10.0, 0.0f), Frame(10.5, 0.1f), Frame(11.0, 0.2f) });

            Assert.That(replay.DurationSeconds, Is.EqualTo(1.0));
            Assert.That(replay.Sample(0.0, isLooping: false).Head.Value.Position.Z, Is.EqualTo(0.0f));
            Assert.That(replay.Sample(1.0, isLooping: false).Head.Value.Position.Z, Is.EqualTo(0.2f));
        }

        [Test]
        public void BetweenFramesThePoseIsBlended()
        {
            var replay = Replay(new[] { Frame(0.0, 0.0f), Frame(0.1, 0.2f) });

            var frame = replay.Sample(0.025, isLooping: false);

            Assert.That(frame.Head.Value.Position.Z, Is.EqualTo(0.05f).Within(1e-5f));
            Assert.That(frame.Joints[1].Value.Position.Z, Is.EqualTo(0.05f).Within(1e-5f));
            Assert.That(frame.SwordController.Value.Position.Z, Is.EqualTo(0.55f).Within(1e-5f));
            Assert.That(frame.TimeSeconds, Is.EqualTo(0.025).Within(1e-9));
        }

        [Test]
        public void RotationsAreBlendedAlongTheShortArc()
        {
            var quarterTurn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)(System.Math.PI / 2.0));
            var start = Frame(0.0, 0.0f, swordController: new TrackedPose(Vector3.Zero, Quaternion.Identity));
            var end = Frame(0.1, 0.0f, swordController: new TrackedPose(Vector3.Zero, quarterTurn));
            var replay = Replay(new[] { start, end });

            var halfway = replay.Sample(0.05, isLooping: false).SwordController.Value.Rotation;

            var expected = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)(System.Math.PI / 4.0));
            Assert.That(Quaternion.Dot(halfway, expected), Is.EqualTo(1.0f).Within(1e-5f));
        }

        [Test]
        public void LoopingWrapsAroundToTheStart()
        {
            var replay = Replay(new[] { Frame(0.0, 0.0f), Frame(0.1, 0.1f), Frame(0.2, 0.2f) });

            Assert.That(replay.Sample(0.3, isLooping: true).Head.Value.Position.Z, Is.EqualTo(0.1f).Within(1e-5f));
            Assert.That(replay.Sample(-0.1, isLooping: true).Head.Value.Position.Z, Is.EqualTo(0.1f).Within(1e-5f));
        }

        [Test]
        public void WithoutLoopingTheEndsHold()
        {
            var replay = Replay(new[] { Frame(0.0, 0.0f), Frame(0.1, 0.1f) });

            Assert.That(replay.Sample(5.0, isLooping: false).Head.Value.Position.Z, Is.EqualTo(0.1f));
            Assert.That(replay.Sample(-5.0, isLooping: false).Head.Value.Position.Z, Is.EqualTo(0.0f));
        }

        [Test]
        public void APauseHoldsTheLastFrameBeforeItInsteadOfSliding()
        {
            var replay = Replay(new[] { Frame(0.0, 0.0f), Frame(2.0, 1.0f) });

            Assert.That(replay.Sample(1.0, isLooping: false).Head.Value.Position.Z, Is.EqualTo(0.0f));
        }

        [Test]
        public void APoseTrackedInOnlyOneFrameComesFromTheNearerFrame()
        {
            var replay = Replay(new[] { Frame(0.0, 0.0f, isBodyTracked: false), Frame(0.1, 0.2f) });

            var early = replay.Sample(0.02, isLooping: false);
            var late = replay.Sample(0.08, isLooping: false);

            Assert.That(early.BodyFidelity, Is.Null);
            Assert.That(early.Joints[0], Is.Null);
            Assert.That(late.BodyFidelity, Is.EqualTo(BodyTrackingFidelity.High));
            Assert.That(late.Joints[0].Value.Position.Z, Is.EqualTo(0.2f));
        }

        [Test]
        public void TheBindPoseInEffectFollowsThePlaybackTime()
        {
            var frames = new[] { Frame(0.0, 0.0f), Frame(0.1, 0.0f), Frame(0.2, 0.0f), Frame(0.3, 0.0f) };
            var replay = Replay(frames, new FencerBindPoseChange(1, BindPose()), new FencerBindPoseChange(3, BindPose()));

            Assert.That(replay.GetBindPoseIndex(0.0, isLooping: false), Is.EqualTo(0), "Before the first skeleton, the first one recorded.");
            Assert.That(replay.GetBindPoseIndex(0.15, isLooping: false), Is.EqualTo(0));
            Assert.That(replay.GetBindPoseIndex(0.3, isLooping: false), Is.EqualTo(1));
            Assert.That(replay.GetBindPoseIndex(0.35, isLooping: true), Is.EqualTo(0), "Looping back to the start.");
        }

        [Test]
        public void ARecordingWithoutASkeletonHasNoBindPose()
        {
            var replay = Replay(new[] { Frame(0.0, 0.0f) });

            Assert.That(replay.GetBindPoseIndex(0.0, isLooping: false), Is.EqualTo(-1));
        }

        [Test]
        public void TheCheckedInClipPlaysAcrossItsPause()
        {
            var replay = new FencerReplay(FencerRecordingFile.Read(FencerRecordingTestData.GetFixturePath()));
            var pauseStarts = FencerRecordingTestData.FixturePauseAfterFrame * FencerRecordingTestData.FrameSeconds;

            var duringPause = replay.Sample(pauseStarts + 0.5, isLooping: false);

            Assert.That(replay.DurationSeconds, Is.EqualTo(((FencerRecordingTestData.FixtureFrameCount - 1) * FencerRecordingTestData.FrameSeconds) + FencerRecordingTestData.FixturePauseSeconds).Within(1e-9));
            Assert.That(duringPause.Head.Value.Position.Z, Is.EqualTo(replay.Recording.Frames[FencerRecordingTestData.FixturePauseAfterFrame].Head.Value.Position.Z));
        }
    }
}
