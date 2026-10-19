using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VrFencing.Session;
using VrFencing.Settings;

namespace VrFencing.Tracking
{
    /// <summary>
    /// The Fencer recording file (PER-79, decided in PER-55): an 8-byte magic, a JSON header, then fixed-size binary
    /// records, little-endian float32 poses (position x, y, z then rotation x, y, z, w; 28 bytes).
    /// <para>
    /// Binary because JSON Lines would be about 26 MB per minute against about 10.5 MB for Full Body at 72 Hz, and slower
    /// to read. Fixed-size records because a file cut short by a killed app then loses only its last record.
    /// </para>
    /// <list type="bullet">
    /// <item><c>F</c>, a frame: time (float64 seconds), flags (bit 0 head, bit 1 sword controller, bit 2 body), body
    /// fidelity (0 none, 1 Low, 2 High), head pose, sword controller pose, one validity bit per joint (least significant
    /// bit first), then every joint's pose. Untracked poses are written as zeros. About 2.4 KB with 84 joints.</item>
    /// <item><c>B</c>, a bind pose: per joint its parent index (int16) and its pose. It applies to the frames after it.</item>
    /// </list>
    /// </summary>
    public static class FencerRecordingFile
    {
        /// <summary>The layout this code writes and the newest it reads.</summary>
        public const int CurrentFormatVersion = 1;

        /// <summary>The recordings' extension; the headset writes them to the app's <c>files/recordings/</c> folder.</summary>
        public const string Extension = ".fencer";

        internal const byte FrameRecord = (byte)'F';
        internal const byte BindPoseRecord = (byte)'B';
        internal const byte HeadTrackedFlag = 1;
        internal const byte SwordControllerTrackedFlag = 2;
        internal const byte BodyTrackedFlag = 4;
        internal const int PoseBytes = 7 * sizeof(float);

        // Far above any real header; stops a damaged length from allocating gigabytes.
        internal const int MaxHeaderBytes = 64 * 1024;

        internal static readonly byte[] Magic = Encoding.ASCII.GetBytes("VRFENCER");

        /// <summary>Bytes of one frame record, type byte included, for a recording with <paramref name="jointCount"/> joints.</summary>
        public static int GetFrameRecordBytes(int jointCount)
        {
            return 1 + sizeof(double) + 2 + (2 * PoseBytes) + GetValidityBytes(jointCount) + (jointCount * PoseBytes);
        }

        /// <summary>A file name that sorts by start time, for example <c>fencer-20261019-213005.fencer</c>.</summary>
        public static string GetFileName(DateTimeOffset startedUtc)
        {
            return "fencer-" + startedUtc.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + Extension;
        }

        /// <summary>Reads a whole recording.</summary>
        /// <exception cref="InvalidDataException">Not a Fencer recording, a newer format, or a damaged header or record type.</exception>
        public static FencerRecording Read(Stream stream)
        {
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            var header = ReadHeader(reader);
            var frames = new List<FencerFrame>();
            var bindPoses = new List<FencerBindPoseChange>();
            var isTruncated = false;
            while (true)
            {
                var recordType = stream.ReadByte();
                if (recordType < 0)
                {
                    break;
                }

                try
                {
                    switch (recordType)
                    {
                        case FrameRecord:
                            frames.Add(ReadFrame(reader, header.JointCount));
                            break;
                        case BindPoseRecord:
                            bindPoses.Add(new FencerBindPoseChange(frames.Count, ReadBindPose(reader, header.JointCount)));
                            break;
                        default:
                            throw new InvalidDataException($"Unknown record type 0x{recordType:X2} after {frames.Count} frames.");
                    }
                }
                catch (EndOfStreamException)
                {
                    isTruncated = true;
                    break;
                }
            }

            return new FencerRecording(header, frames, bindPoses, isTruncated);
        }

        /// <summary>Reads a whole recording from a file.</summary>
        public static FencerRecording Read(string path)
        {
            using var stream = File.OpenRead(path);
            return Read(stream);
        }

        internal static int GetValidityBytes(int jointCount)
        {
            return (jointCount + 7) / 8;
        }

        internal static string WriteHeaderJson(FencerRecordingHeader header)
        {
            // Decimal keeps floats short in the text (1.65, not 1.64999997615814).
            var json = new JObject
            {
                ["formatVersion"] = header.FormatVersion,
                ["jointSet"] = header.JointSet,
                ["jointCount"] = header.JointCount,
                ["fencerHeightMeters"] = (decimal)header.FencerHeightMeters,
                ["hand"] = header.Hand.ToString(),
                ["stripLengthMeters"] = header.StripLengthMeters.HasValue ? new JValue((decimal)header.StripLengthMeters.Value) : JValue.CreateNull(),
                ["appVersion"] = header.AppVersion,
                ["startedUtc"] = header.StartedUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
            };
            return json.ToString(Formatting.None);
        }

        private static FencerRecordingHeader ReadHeader(BinaryReader reader)
        {
            var magic = reader.ReadBytes(Magic.Length);
            if (magic.Length != Magic.Length || !AreEqual(magic, Magic))
            {
                throw new InvalidDataException("Not a Fencer recording (the file does not start with VRFENCER).");
            }

            var headerBytes = reader.ReadInt32();
            if (headerBytes <= 0 || headerBytes > MaxHeaderBytes)
            {
                throw new InvalidDataException($"Damaged Fencer recording header length ({headerBytes} bytes).");
            }

            var text = Encoding.UTF8.GetString(reader.ReadBytes(headerBytes));
            JObject json;
            try
            {
                using var textReader = new StringReader(text);
                using var jsonReader = new JsonTextReader(textReader) { DateParseHandling = DateParseHandling.None };
                json = JObject.Load(jsonReader);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("Damaged Fencer recording header: " + exception.Message, exception);
            }

            var formatVersion = json.Value<int?>("formatVersion")
                ?? throw new InvalidDataException("The Fencer recording header has no formatVersion.");
            if (formatVersion < 1 || formatVersion > CurrentFormatVersion)
            {
                throw new InvalidDataException($"Fencer recording format {formatVersion} is not supported (this app reads up to {CurrentFormatVersion}).");
            }

            var jointCount = json.Value<int?>("jointCount")
                ?? throw new InvalidDataException("The Fencer recording header has no jointCount.");
            var hand = Enum.TryParse<Hand>(json.Value<string>("hand"), out var parsedHand) ? parsedHand : Hand.Right;
            var startedText = json.Value<string>("startedUtc");
            var startedUtc = DateTimeOffset.TryParse(startedText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedStart)
                ? parsedStart
                : DateTimeOffset.MinValue;
            return new FencerRecordingHeader(
                formatVersion,
                json.Value<string>("jointSet"),
                jointCount,
                json.Value<float?>("fencerHeightMeters") ?? 0.0f,
                hand,
                json.Value<float?>("stripLengthMeters"),
                json.Value<string>("appVersion"),
                startedUtc);
        }

        private static FencerFrame ReadFrame(BinaryReader reader, int jointCount)
        {
            var timeSeconds = reader.ReadDouble();
            var flags = reader.ReadByte();
            var fidelityCode = reader.ReadByte();
            var head = ReadPose(reader);
            var swordController = ReadPose(reader);
            var validity = reader.ReadBytes(GetValidityBytes(jointCount));
            if (validity.Length != GetValidityBytes(jointCount))
            {
                throw new EndOfStreamException();
            }

            var joints = new TrackedPose?[jointCount];
            for (var joint = 0; joint < jointCount; joint++)
            {
                var pose = ReadPose(reader);
                if ((validity[joint / 8] & (1 << (joint % 8))) != 0)
                {
                    joints[joint] = pose;
                }
            }

            var isBodyTracked = (flags & BodyTrackedFlag) != 0;
            return new FencerFrame(
                timeSeconds,
                (flags & HeadTrackedFlag) != 0 ? head : (TrackedPose?)null,
                (flags & SwordControllerTrackedFlag) != 0 ? swordController : (TrackedPose?)null,
                isBodyTracked ? DecodeFidelity(fidelityCode) : null,
                joints);
        }

        private static FencerBindPose ReadBindPose(BinaryReader reader, int jointCount)
        {
            var joints = new TrackedPose[jointCount];
            var parents = new int[jointCount];
            for (var joint = 0; joint < jointCount; joint++)
            {
                parents[joint] = reader.ReadInt16();
                joints[joint] = ReadPose(reader);
            }

            return new FencerBindPose(joints, parents);
        }

        private static TrackedPose ReadPose(BinaryReader reader)
        {
            var position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            var rotation = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            return new TrackedPose(position, rotation);
        }

        internal static byte EncodeFidelity(BodyTrackingFidelity? fidelity)
        {
            return fidelity switch
            {
                BodyTrackingFidelity.Low => 1,
                BodyTrackingFidelity.High => 2,
                _ => 0,
            };
        }

        // A body frame with an unknown fidelity code (a newer runtime value) still counts as tracked, at the lower fidelity.
        private static BodyTrackingFidelity DecodeFidelity(byte code)
        {
            return code == 2 ? BodyTrackingFidelity.High : BodyTrackingFidelity.Low;
        }

        private static bool AreEqual(byte[] left, byte[] right)
        {
            for (var index = 0; index < left.Length; index++)
            {
                if (left[index] != right[index])
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Writes a Fencer recording (layout in <see cref="FencerRecordingFile"/>) frame by frame, through a buffer, so a
    /// 72 Hz recorder on the headset costs one small write per frame and no per-frame allocation.
    /// </summary>
    public sealed class FencerRecordingWriter : IDisposable
    {
        private const int BufferBytes = 64 * 1024;

        private readonly BinaryWriter _writer;
        private readonly int _jointCount;
        private readonly byte[] _validity;

        /// <summary>Writes the magic and the header at once. The writer owns <paramref name="stream"/> and closes it on dispose.</summary>
        public FencerRecordingWriter(Stream stream, FencerRecordingHeader header)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            if (header == null)
            {
                throw new ArgumentNullException(nameof(header));
            }

            if (header.FormatVersion != FencerRecordingFile.CurrentFormatVersion)
            {
                throw new ArgumentException($"This writer writes format {FencerRecordingFile.CurrentFormatVersion}, not {header.FormatVersion}.", nameof(header));
            }

            _jointCount = header.JointCount;
            _validity = new byte[FencerRecordingFile.GetValidityBytes(_jointCount)];
            _writer = new BinaryWriter(new BufferedStream(stream, BufferBytes), Encoding.UTF8, leaveOpen: false);
            var headerBytes = Encoding.UTF8.GetBytes(FencerRecordingFile.WriteHeaderJson(header));
            _writer.Write(FencerRecordingFile.Magic);
            _writer.Write(headerBytes.Length);
            _writer.Write(headerBytes);
        }

        public int FramesWritten { get; private set; }

        /// <summary>The bind pose for the frames written after it.</summary>
        public void WriteBindPose(FencerBindPose bindPose)
        {
            if (bindPose == null)
            {
                throw new ArgumentNullException(nameof(bindPose));
            }

            RequireJointCount(bindPose.Joints.Count, "bind pose");
            _writer.Write(FencerRecordingFile.BindPoseRecord);
            for (var joint = 0; joint < _jointCount; joint++)
            {
                var parent = bindPose.ParentIndices[joint];
                _writer.Write((short)Math.Max(short.MinValue, Math.Min(short.MaxValue, parent)));
                WritePose(bindPose.Joints[joint]);
            }
        }

        public void WriteFrame(FencerFrame frame)
        {
            if (frame == null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            RequireJointCount(frame.Joints.Count, "frame");
            var isBodyTracked = frame.BodyFidelity.HasValue;
            var flags = (byte)((frame.Head.HasValue ? FencerRecordingFile.HeadTrackedFlag : 0)
                               | (frame.SwordController.HasValue ? FencerRecordingFile.SwordControllerTrackedFlag : 0)
                               | (isBodyTracked ? FencerRecordingFile.BodyTrackedFlag : 0));
            Array.Clear(_validity, 0, _validity.Length);
            for (var joint = 0; joint < _jointCount; joint++)
            {
                if (isBodyTracked && frame.Joints[joint].HasValue)
                {
                    _validity[joint / 8] |= (byte)(1 << (joint % 8));
                }
            }

            _writer.Write(FencerRecordingFile.FrameRecord);
            _writer.Write(frame.TimeSeconds);
            _writer.Write(flags);
            _writer.Write(FencerRecordingFile.EncodeFidelity(frame.BodyFidelity));
            WritePoseOrZeros(frame.Head);
            WritePoseOrZeros(frame.SwordController);
            _writer.Write(_validity);
            for (var joint = 0; joint < _jointCount; joint++)
            {
                WritePoseOrZeros(isBodyTracked ? frame.Joints[joint] : null);
            }

            FramesWritten++;
        }

        /// <summary>Pushes buffered frames to the file, as before the app may be killed (paused on the headset).</summary>
        public void Flush()
        {
            _writer.Flush();
        }

        public void Dispose()
        {
            _writer.Dispose();
        }

        private void RequireJointCount(int count, string what)
        {
            if (count != _jointCount)
            {
                throw new ArgumentException($"The recording has {_jointCount} joints, but the {what} has {count}.");
            }
        }

        private void WritePoseOrZeros(TrackedPose? pose)
        {
            if (pose.HasValue)
            {
                WritePose(pose.Value);
                return;
            }

            for (var component = 0; component < 7; component++)
            {
                _writer.Write(0.0f);
            }
        }

        private void WritePose(TrackedPose pose)
        {
            _writer.Write(pose.Position.X);
            _writer.Write(pose.Position.Y);
            _writer.Write(pose.Position.Z);
            _writer.Write(pose.Rotation.X);
            _writer.Write(pose.Rotation.Y);
            _writer.Write(pose.Rotation.Z);
            _writer.Write(pose.Rotation.W);
        }
    }
}
