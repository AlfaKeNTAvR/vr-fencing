using System;
using System.IO;
using System.Numerics;
using VrFencing.Session;
using VrFencing.Settings;

namespace VrFencing.Partner
{
    /// <summary>
    /// What a Fencer tells the partner once and on every change (PER-81): their own placed Strip, so both can build the
    /// same bout Strip, and the height and sword hand the opponent's rigged body is built for.
    /// </summary>
    public readonly struct FencerInfo
    {
        public FencerInfo(StripExtent strip, float heightMeters, Hand hand)
        {
            Strip = strip;
            HeightMeters = heightMeters;
            Hand = hand;
        }

        public StripExtent Strip { get; }

        public float HeightMeters { get; }

        public Hand Hand { get; }
    }

    /// <summary>
    /// The sword hand for one moment, as sent at 60 to 72 per second (PER-59): the controller anchor's pose with its
    /// velocity, and the grip settings the blade is drawn with (grip, angles, set, flex). The receiver draws the blade
    /// with the same bend, droop and flex code as its own. Poses in the sender's Strip frame; time on the sender's clock.
    /// </summary>
    public readonly struct HandSample
    {
        public HandSample(
            double senderTimeSeconds,
            TrackedPose controller,
            Vector3 velocity,
            Vector3 angularVelocity,
            Hand hand,
            BladeMount mount,
            GripAngles angles,
            float setSagittaMeters,
            BladeFlexSoftness flex)
        {
            SenderTimeSeconds = senderTimeSeconds;
            Controller = controller;
            Velocity = velocity;
            AngularVelocity = angularVelocity;
            Hand = hand;
            Mount = mount;
            Angles = angles;
            SetSagittaMeters = setSagittaMeters;
            Flex = flex;
        }

        public double SenderTimeSeconds { get; }

        /// <summary>The sword-hand controller anchor, from which the blade is built as from a live controller.</summary>
        public TrackedPose Controller { get; }

        /// <summary>Meters per second.</summary>
        public Vector3 Velocity { get; }

        /// <summary>Radians per second about this axis (its length), in the same frame as the pose.</summary>
        public Vector3 AngularVelocity { get; }

        public Hand Hand { get; }

        public BladeMount Mount { get; }

        /// <summary>The grip's pitch and yaw in use (right-hand convention), so a calibrated blade is drawn as its owner sees it.</summary>
        public GripAngles Angles { get; }

        public float SetSagittaMeters { get; }

        public BladeFlexSoftness Flex { get; }

        /// <summary>The grip settings as Blade settings, for the same mount, droop and flex code the Fencer's own blade uses.</summary>
        public BladeSettings ToBladeSettings()
        {
            var settings = new BladeSettings { Mount = Mount, SetSagittaMeters = SetSagittaMeters, Flex = Flex };
            var saved = settings.SavedAngles.For(Mount);
            saved.PitchDegrees = Angles.PitchDegrees;
            saved.YawDegrees = Angles.YawDegrees;
            return settings;
        }

        /// <summary>This sample turned into another frame by <paramref name="movePoint"/> and <paramref name="turn"/>.</summary>
        public HandSample Transform(Func<Vector3, Vector3> movePoint, Quaternion turn)
        {
            var controller = new TrackedPose(movePoint(Controller.Position), Quaternion.Normalize(turn * Controller.Rotation));
            return new HandSample(
                SenderTimeSeconds,
                controller,
                Vector3.Transform(Velocity, turn),
                Vector3.Transform(AngularVelocity, turn),
                Hand,
                Mount,
                Angles,
                SetSagittaMeters,
                Flex);
        }
    }

    /// <summary>A body as sent at 30 per second (PER-59), with the time it was read on the sender's clock.</summary>
    public readonly struct BodySample
    {
        public BodySample(double senderTimeSeconds, FencerBody body)
        {
            SenderTimeSeconds = senderTimeSeconds;
            Body = body ?? throw new ArgumentNullException(nameof(body));
        }

        public double SenderTimeSeconds { get; }

        public FencerBody Body { get; }
    }

    /// <summary>
    /// The bytes of the three partner streams (PER-81). Each message starts with its kind and the format version, so a
    /// build that reads another layout drops the message instead of drawing garbage (the version check refuses such
    /// builds anyway). Body joints go as millimeters in 16 bits (plus or minus 32 m); the hand goes as full floats, since
    /// the blade's point is 1 m out from it.
    /// </summary>
    public static class PartnerMessages
    {
        /// <summary>
        /// Bumped whenever a layout below changes; the app's Version must be bumped with it. 2: the body carries the
        /// footwork state (PER-112, Version 1.4). 3: the body carries the back wrist's turn, and its footwork state is
        /// the one <see cref="FootworkLookAhead.Seconds"/> before the body (PER-142, Version 1.5).
        /// </summary>
        public const byte FormatVersion = 3;

        public const byte InfoKind = 1;
        public const byte HandKind = 2;
        public const byte BodyKind = 3;

        private const float MetersPerUnit = 0.001f;
        private const float RadiansPerPitchUnit = (float)(Math.PI / 180.0);
        private const float ConfidencePerUnit = 1.0f / byte.MaxValue;
        private const byte NoFidelity = 0;
        private const byte LowFidelity = 1;
        private const byte HighFidelity = 2;

        public static byte[] EncodeInfo(FencerInfo info)
        {
            return Write(InfoKind, writer =>
            {
                writer.Write(info.Strip.LengthMeters);
                writer.Write(info.Strip.RearEndZ);
                writer.Write(info.HeightMeters);
                writer.Write((byte)info.Hand);
            });
        }

        public static byte[] EncodeHand(HandSample sample)
        {
            return Write(HandKind, writer =>
            {
                writer.Write(sample.SenderTimeSeconds);
                WritePose(writer, sample.Controller);
                WriteVector(writer, sample.Velocity);
                WriteVector(writer, sample.AngularVelocity);
                writer.Write((byte)sample.Hand);
                writer.Write((byte)sample.Mount);
                writer.Write(sample.Angles.PitchDegrees);
                writer.Write(sample.Angles.YawDegrees);
                writer.Write(sample.SetSagittaMeters);
                writer.Write((byte)sample.Flex);
            });
        }

        public static byte[] EncodeBody(BodySample sample)
        {
            return Write(BodyKind, writer =>
            {
                var body = sample.Body;
                writer.Write(sample.SenderTimeSeconds);
                writer.Write(EncodeFidelity(body.Fidelity));

                // One bit per joint that follows, then the head's turn bit.
                uint present = 0;
                for (var index = 0; index < FencerBody.JointCount; index++)
                {
                    if (body.Get(index).HasValue)
                    {
                        present |= 1u << index;
                    }
                }

                if (body.HeadRotation.HasValue)
                {
                    present |= 1u << FencerBody.JointCount;
                }

                if (body.Footwork.HasValue)
                {
                    present |= 1u << (FencerBody.JointCount + 1);
                }

                if (body.BackWristRotation.HasValue)
                {
                    present |= 1u << (FencerBody.JointCount + 2);
                }

                writer.Write(present);
                for (var index = 0; index < FencerBody.JointCount; index++)
                {
                    var joint = body.Get(index);
                    if (joint.HasValue)
                    {
                        writer.Write(ToUnits(joint.Value.X));
                        writer.Write(ToUnits(joint.Value.Y));
                        writer.Write(ToUnits(joint.Value.Z));
                    }
                }

                if (body.HeadRotation.HasValue)
                {
                    WriteRotation(writer, body.HeadRotation.Value);
                }

                if (body.Footwork.HasValue)
                {
                    WriteFootwork(writer, body.Footwork.Value);
                }

                if (body.BackWristRotation.HasValue)
                {
                    WriteRotation(writer, body.BackWristRotation.Value);
                }
            });
        }

        /// <summary>The message's kind, or 0 when it is empty or from another format version.</summary>
        public static byte ReadKind(byte[] message)
        {
            return message != null && message.Length >= 2 && message[1] == FormatVersion ? message[0] : (byte)0;
        }

        /// <exception cref="InvalidDataException">Not an info message of this format, or cut short.</exception>
        public static FencerInfo DecodeInfo(byte[] message)
        {
            return Read(message, InfoKind, reader =>
            {
                var length = reader.ReadSingle();
                var rearEnd = reader.ReadSingle();
                var height = reader.ReadSingle();
                var hand = ReadEnum<Hand>(reader.ReadByte());
                if (!(height > 0.0f) || float.IsInfinity(height))
                {
                    throw new InvalidDataException($"A partner's height must be positive, not {height}.");
                }

                try
                {
                    return new FencerInfo(new StripExtent(length, rearEnd), height, hand);
                }
                catch (ArgumentOutOfRangeException error)
                {
                    throw new InvalidDataException(error.Message, error);
                }
            });
        }

        /// <exception cref="InvalidDataException">Not a hand message of this format, or cut short.</exception>
        public static HandSample DecodeHand(byte[] message)
        {
            return Read(message, HandKind, reader => new HandSample(
                ReadTime(reader),
                ReadPose(reader),
                ReadVector(reader),
                ReadVector(reader),
                ReadEnum<Hand>(reader.ReadByte()),
                ReadEnum<BladeMount>(reader.ReadByte()),
                new GripAngles(ReadFinite(reader), ReadFinite(reader)),
                ReadFinite(reader),
                ReadEnum<BladeFlexSoftness>(reader.ReadByte())));
        }

        /// <exception cref="InvalidDataException">Not a body message of this format, or cut short.</exception>
        public static BodySample DecodeBody(byte[] message)
        {
            return Read(message, BodyKind, reader =>
            {
                var time = ReadTime(reader);
                var fidelity = DecodeFidelity(reader.ReadByte());
                var present = reader.ReadUInt32();
                var joints = new Vector3?[FencerBody.JointCount];
                for (var index = 0; index < FencerBody.JointCount; index++)
                {
                    if ((present & (1u << index)) != 0)
                    {
                        joints[index] = new Vector3(FromUnits(reader.ReadInt16()), FromUnits(reader.ReadInt16()), FromUnits(reader.ReadInt16()));
                    }
                }

                var head = (present & (1u << FencerBody.JointCount)) != 0 ? ReadRotation(reader) : (Quaternion?)null;
                var footwork = (present & (1u << (FencerBody.JointCount + 1))) != 0 ? ReadFootwork(reader) : (FootworkState?)null;
                var backWrist = (present & (1u << (FencerBody.JointCount + 2))) != 0 ? ReadRotation(reader) : (Quaternion?)null;
                return new BodySample(time, new FencerBody(joints, head, fidelity, footwork, backWrist));
            });
        }

        private static byte[] Write(byte kind, Action<BinaryWriter> writeBody)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(kind);
                    writer.Write(FormatVersion);
                    writeBody(writer);
                }

                return stream.ToArray();
            }
        }

        private static T Read<T>(byte[] message, byte kind, Func<BinaryReader, T> readBody)
        {
            if (ReadKind(message) != kind)
            {
                throw new InvalidDataException($"Not a kind {kind} message of format {FormatVersion}.");
            }

            try
            {
                using (var reader = new BinaryReader(new MemoryStream(message, 2, message.Length - 2)))
                {
                    var value = readBody(reader);
                    if (reader.BaseStream.Position != reader.BaseStream.Length)
                    {
                        throw new InvalidDataException($"A kind {kind} message has {reader.BaseStream.Length - reader.BaseStream.Position} bytes too many.");
                    }

                    return value;
                }
            }
            catch (EndOfStreamException error)
            {
                throw new InvalidDataException($"A kind {kind} message was cut short.", error);
            }
        }

        private static float ReadFinite(BinaryReader reader)
        {
            var value = reader.ReadSingle();
            if (!float.IsFinite(value))
            {
                throw new InvalidDataException("A setting that is not a number.");
            }

            return value;
        }

        private static double ReadTime(BinaryReader reader)
        {
            var seconds = reader.ReadDouble();
            if (!double.IsFinite(seconds))
            {
                throw new InvalidDataException("A time that is not a number.");
            }

            return seconds;
        }

        private static T ReadEnum<T>(byte value) where T : struct, Enum
        {
            var asEnum = (T)Enum.ToObject(typeof(T), value);
            if (!Enum.IsDefined(typeof(T), asEnum))
            {
                throw new InvalidDataException($"{value} is not a {typeof(T).Name}.");
            }

            return asEnum;
        }

        private static byte EncodeFidelity(BodyTrackingFidelity? fidelity)
        {
            if (!fidelity.HasValue)
            {
                return NoFidelity;
            }

            return fidelity.Value == BodyTrackingFidelity.High ? HighFidelity : LowFidelity;
        }

        private static BodyTrackingFidelity? DecodeFidelity(byte value)
        {
            switch (value)
            {
                case NoFidelity:
                    return null;
                case LowFidelity:
                    return BodyTrackingFidelity.Low;
                case HighFidelity:
                    return BodyTrackingFidelity.High;
                default:
                    throw new InvalidDataException($"{value} is not a body fidelity.");
            }
        }

        private static short ToUnits(float meters)
        {
            var units = Math.Round(meters / MetersPerUnit);
            return (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, units));
        }

        private static float FromUnits(short units)
        {
            return units * MetersPerUnit;
        }

        private static void WriteVector(BinaryWriter writer, Vector3 vector)
        {
            writer.Write(vector.X);
            writer.Write(vector.Y);
            writer.Write(vector.Z);
        }

        private static Vector3 ReadVector(BinaryReader reader)
        {
            var vector = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            if (!float.IsFinite(vector.X) || !float.IsFinite(vector.Y) || !float.IsFinite(vector.Z))
            {
                throw new InvalidDataException("A position or velocity that is not a number.");
            }

            return vector;
        }

        private static void WriteRotation(BinaryWriter writer, Quaternion rotation)
        {
            writer.Write(rotation.X);
            writer.Write(rotation.Y);
            writer.Write(rotation.Z);
            writer.Write(rotation.W);
        }

        private static Quaternion ReadRotation(BinaryReader reader)
        {
            var rotation = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            if (!(rotation.LengthSquared() > 1e-6f))
            {
                throw new InvalidDataException("A rotation with no length.");
            }

            return Quaternion.Normalize(rotation);
        }

        /// <summary>The footwork state in 14 bytes: phase, confidence, then each foot's spot (mm), lift (mm) and pitch (degrees).</summary>
        private static void WriteFootwork(BinaryWriter writer, FootworkState footwork)
        {
            writer.Write((byte)footwork.Phase);
            writer.Write((byte)Math.Round(footwork.Confidence / ConfidencePerUnit));
            WriteFoot(writer, footwork.Front);
            WriteFoot(writer, footwork.Back);
        }

        private static FootworkState ReadFootwork(BinaryReader reader)
        {
            var phase = ReadEnum<FootworkPhase>(reader.ReadByte());
            var confidence = reader.ReadByte() * ConfidencePerUnit;
            return new FootworkState(phase, ReadFoot(reader), ReadFoot(reader), confidence);
        }

        private static void WriteFoot(BinaryWriter writer, FootPlacement foot)
        {
            writer.Write(ToUnits(foot.X));
            writer.Write(ToUnits(foot.Z));
            writer.Write((byte)Math.Max(0.0, Math.Min(byte.MaxValue, Math.Round(foot.LiftMeters / MetersPerUnit))));
            writer.Write((sbyte)Math.Max(sbyte.MinValue, Math.Min(sbyte.MaxValue, Math.Round(foot.PitchRadians / RadiansPerPitchUnit))));
        }

        private static FootPlacement ReadFoot(BinaryReader reader)
        {
            var x = FromUnits(reader.ReadInt16());
            var z = FromUnits(reader.ReadInt16());
            var lift = reader.ReadByte() * MetersPerUnit;
            var pitch = reader.ReadSByte() * RadiansPerPitchUnit;
            return new FootPlacement(x, z, lift, pitch);
        }

        private static void WritePose(BinaryWriter writer, TrackedPose pose)
        {
            WriteVector(writer, pose.Position);
            WriteRotation(writer, pose.Rotation);
        }

        private static TrackedPose ReadPose(BinaryReader reader)
        {
            return new TrackedPose(ReadVector(reader), ReadRotation(reader));
        }
    }
}
