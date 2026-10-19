// The Fencer recording file (PER-79): what the headset writes reads back the same on the PC, a file cut short by a
// killed app keeps its complete frames, and the checked-in clip stays readable as the format moves on.
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Session;
using VrFencing.Settings;
using VrFencing.Tracking;

namespace VrFencing.Tests.Tracking
{
    public class FencerRecordingFileTests
    {
        private static FencerRecording ReadBack(byte[] bytes)
        {
            using var stream = new MemoryStream(bytes);
            return FencerRecordingFile.Read(stream);
        }

        private static void AssertSamePose(TrackedPose? actual, TrackedPose? expected, string what)
        {
            Assert.That(actual.HasValue, Is.EqualTo(expected.HasValue), $"{what}: tracked or not");
            if (!expected.HasValue)
            {
                return;
            }

            Assert.That(actual.Value.Position, Is.EqualTo(expected.Value.Position), $"{what}: position");
            Assert.That(actual.Value.Rotation, Is.EqualTo(expected.Value.Rotation), $"{what}: rotation");
        }

        private static void AssertSameFrame(FencerFrame actual, FencerFrame expected, int index)
        {
            Assert.That(actual.TimeSeconds, Is.EqualTo(expected.TimeSeconds), $"frame {index} time");
            Assert.That(actual.BodyFidelity, Is.EqualTo(expected.BodyFidelity), $"frame {index} fidelity");
            AssertSamePose(actual.Head, expected.Head, $"frame {index} head");
            AssertSamePose(actual.SwordController, expected.SwordController, $"frame {index} sword controller");
            Assert.That(actual.Joints.Count, Is.EqualTo(expected.Joints.Count), $"frame {index} joint count");
            for (var joint = 0; joint < expected.Joints.Count; joint++)
            {
                AssertSamePose(actual.Joints[joint], expected.Joints[joint], $"frame {index} joint {joint}");
            }
        }

        [Test]
        public void HeaderReadsBackWithEveryField()
        {
            var header = ReadBack(FencerRecordingTestData.WriteFixture()).Header;

            Assert.That(header.FormatVersion, Is.EqualTo(FencerRecordingFile.CurrentFormatVersion));
            Assert.That(header.JointSet, Is.EqualTo("FullBody"));
            Assert.That(header.JointCount, Is.EqualTo(84));
            Assert.That(header.FencerHeightMeters, Is.EqualTo(1.65f));
            Assert.That(header.Hand, Is.EqualTo(Hand.Right));
            Assert.That(header.StripLengthMeters, Is.EqualTo(6.3f));
            Assert.That(header.AppVersion, Is.EqualTo("0.1.0-test"));
            Assert.That(header.StartedUtc, Is.EqualTo(FencerRecordingTestData.FixtureStartedUtc));
        }

        [Test]
        public void HeaderWithoutAStripReadsBackWithoutAStripLength()
        {
            var memory = new MemoryStream();
            using (var writer = new FencerRecordingWriter(memory, FencerRecordingTestData.CreateHeader(stripLengthMeters: null)))
            {
                writer.WriteFrame(FencerRecordingTestData.CreateFrame(0, 0.0, isBodyTracked: false));
            }

            Assert.That(ReadBack(memory.ToArray()).Header.StripLengthMeters, Is.Null);
        }

        [Test]
        public void HeaderIsJsonAPersonCanRead()
        {
            var bytes = FencerRecordingTestData.WriteFixture();
            var headerLength = BitConverter.ToInt32(bytes, 8);
            var text = System.Text.Encoding.UTF8.GetString(bytes, 12, headerLength);

            Assert.That(System.Text.Encoding.ASCII.GetString(bytes, 0, 8), Is.EqualTo("VRFENCER"));
            Assert.That(text, Does.StartWith("{\"formatVersion\":1,"));
            Assert.That(text, Does.Contain("\"fencerHeightMeters\":1.65,"));
            Assert.That(text, Does.Contain("\"hand\":\"Right\""));
        }

        [Test]
        public void FramesReadBackExactly()
        {
            var expected = FencerRecordingTestData.CreateFixtureFrames();

            var recording = ReadBack(FencerRecordingTestData.WriteFixture());

            Assert.That(recording.IsTruncated, Is.False);
            Assert.That(recording.Frames, Has.Count.EqualTo(expected.Count));
            for (var index = 0; index < expected.Count; index++)
            {
                AssertSameFrame(recording.Frames[index], expected[index], index);
            }
        }

        [Test]
        public void AFrameWithoutABodyReadsBackWithEveryJointUntracked()
        {
            var joints = Enumerable.Repeat((TrackedPose?)new TrackedPose(Vector3.One, Quaternion.Identity), 84).ToArray();
            var memory = new MemoryStream();
            using (var writer = new FencerRecordingWriter(memory, FencerRecordingTestData.CreateHeader()))
            {
                writer.WriteFrame(new FencerFrame(0.0, null, null, null, joints));
            }

            var frame = ReadBack(memory.ToArray()).Frames.Single();

            Assert.That(frame.BodyFidelity, Is.Null);
            Assert.That(frame.Head, Is.Null);
            Assert.That(frame.SwordController, Is.Null);
            Assert.That(frame.Joints.All(joint => !joint.HasValue), Is.True);
        }

        [Test]
        public void BindPosesKeepTheirParentsAndTheFrameTheyStartAt()
        {
            var recording = ReadBack(FencerRecordingTestData.WriteFixture());

            var change = recording.BindPoses.Single();
            Assert.That(change.FirstFrameIndex, Is.EqualTo(FencerRecordingTestData.FixtureUntrackedBodyFrames));
            var expected = FencerRecordingTestData.CreateBindPose();
            Assert.That(change.BindPose.ParentIndices, Is.EqualTo(expected.ParentIndices));
            Assert.That(change.BindPose.Joints.Select(joint => joint.Position), Is.EqualTo(expected.Joints.Select(joint => joint.Position)));
        }

        [Test]
        public void AFullBodyFrameIsAbout2Point4Kilobytes()
        {
            // About 10.5 MB per minute at 72 Hz (PER-55).
            Assert.That(FencerRecordingFile.GetFrameRecordBytes(84), Is.EqualTo(2430));
        }

        [Test]
        public void EveryFrameTakesTheSameNumberOfBytes()
        {
            var oneFrame = new MemoryStream();
            using (var writer = new FencerRecordingWriter(oneFrame, FencerRecordingTestData.CreateHeader()))
            {
                writer.WriteFrame(FencerRecordingTestData.CreateFrame(0, 0.0, isBodyTracked: false));
            }

            var twoFrames = new MemoryStream();
            using (var writer = new FencerRecordingWriter(twoFrames, FencerRecordingTestData.CreateHeader()))
            {
                writer.WriteFrame(FencerRecordingTestData.CreateFrame(0, 0.0, isBodyTracked: false));
                writer.WriteFrame(FencerRecordingTestData.CreateFrame(1, 0.1, isBodyTracked: true));
            }

            Assert.That(twoFrames.ToArray().Length - oneFrame.ToArray().Length, Is.EqualTo(FencerRecordingFile.GetFrameRecordBytes(84)));
        }

        [Test]
        public void AFileCutShortKeepsItsCompleteFrames()
        {
            var bytes = FencerRecordingTestData.WriteFixture();
            var cut = bytes.Take(bytes.Length - 1000).ToArray();

            var recording = ReadBack(cut);

            Assert.That(recording.IsTruncated, Is.True);
            Assert.That(recording.Frames, Has.Count.EqualTo(FencerRecordingTestData.FixtureFrameCount - 1));
            AssertSameFrame(recording.Frames.Last(), FencerRecordingTestData.CreateFixtureFrames()[FencerRecordingTestData.FixtureFrameCount - 2], FencerRecordingTestData.FixtureFrameCount - 2);
        }

        [Test]
        public void AFileThatIsNotARecordingIsRefused()
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes("{\"timestamp\":\"2026-10-19\"}\n");

            Assert.Throws<InvalidDataException>(() => ReadBack(bytes));
        }

        [Test]
        public void ANewerFormatIsRefused()
        {
            var bytes = FencerRecordingTestData.WriteFixture();
            var headerLength = BitConverter.ToInt32(bytes, 8);
            var text = System.Text.Encoding.UTF8.GetString(bytes, 12, headerLength).Replace("\"formatVersion\":1", "\"formatVersion\":9");
            var newer = bytes.Take(12).Concat(System.Text.Encoding.UTF8.GetBytes(text)).Concat(bytes.Skip(12 + headerLength)).ToArray();

            var error = Assert.Throws<InvalidDataException>(() => ReadBack(newer));
            Assert.That(error.Message, Does.Contain("format 9"));
        }

        [Test]
        public void AFrameWithTheWrongJointCountIsRefused()
        {
            using var writer = new FencerRecordingWriter(new MemoryStream(), FencerRecordingTestData.CreateHeader());

            Assert.Throws<ArgumentException>(() => writer.WriteFrame(new FencerFrame(0.0, null, null, BodyTrackingFidelity.High, new TrackedPose?[70])));
        }

        [Test]
        public void FileNamesSortByStartTime()
        {
            Assert.That(FencerRecordingFile.GetFileName(FencerRecordingTestData.FixtureStartedUtc), Is.EqualTo("fencer-20261019-213005.fencer"));
        }

        [Test]
        public void CheckedInClipReadsBackAsTheSyntheticLunge()
        {
            var path = FencerRecordingTestData.GetFixturePath();
            Assert.That(File.Exists(path), Is.True, $"Missing the fixture clip at {path}.");

            var recording = FencerRecordingFile.Read(path);

            // Equal bytes would break on any intended format change; equal content is what a reader must keep.
            var expected = FencerRecordingTestData.CreateFixtureFrames();
            Assert.That(recording.IsTruncated, Is.False);
            Assert.That(recording.Header.JointCount, Is.EqualTo(84));
            Assert.That(recording.Frames, Has.Count.EqualTo(expected.Count));
            for (var index = 0; index < expected.Count; index++)
            {
                AssertSameFrame(recording.Frames[index], expected[index], index);
            }

            Assert.That(recording.BindPoses, Has.Count.EqualTo(1));
        }

        /// <summary>Regenerates the checked-in clip; run by hand after an intended change to the synthetic lunge.</summary>
        [Test]
        [Explicit("Writes the checked-in fixture clip.")]
        public void WriteTheFixtureClip()
        {
            var path = FencerRecordingTestData.GetFixturePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, FencerRecordingTestData.WriteFixture());
        }
    }
}
