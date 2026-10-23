using System;
using System.IO;
using VrFencing.Dummy;
using VrFencing.Session;
using VrFencing.Settings;

namespace VrFencing.Partner
{
    /// <summary>The bout's rules the Host picks for both Fencers (PER-56): one Touch pressure rule and the double window.</summary>
    public readonly struct BoutRules
    {
        public BoutRules(TouchPressure pressure, double doubleWindowSeconds)
        {
            Pressure = pressure;
            DoubleWindowSeconds = doubleWindowSeconds;
        }

        public TouchPressure Pressure { get; }

        public double DoubleWindowSeconds { get; }
    }

    /// <summary>
    /// The bytes of the judge's messages (PER-82), on their own Netcode named message beside the body and blade streams.
    /// Each starts with its kind and the format version; an unreadable one is dropped and counted.
    /// <list type="bullet">
    /// <item>Ping: the sender's time. Pong: that time echoed, and the answering app's time (<see cref="ClockSync"/>).</item>
    /// <item>Claim (Partner to Host): a Touch on the Host's clock.</item>
    /// <item>Light (Host to Partner): what the judge lit, or a Parried verdict (PER-145).</item>
    /// <item>Rules (Host to Partner): the bout's Touch pressure rule and double window.</item>
    /// <item>Stance (Partner to Host, PER-83): behind the En Garde Line or not, off the Strip or not.</item>
    /// <item>Flow (Host to Partner, PER-83): the mode, the bout flow's phase and last halt, and the Paired Practice tally.</item>
    /// <item>Glance and Tally reset (Partner to Host, PER-83): for the Paired Practice tally.</item>
    /// </list>
    /// </summary>
    public static class BoutMessages
    {
        /// <summary>
        /// Bumped whenever a layout below changes; the app's Version must be bumped with it. 2: a light may be a
        /// <see cref="LightKind.Parried"/> verdict, its zone the parried attacker's (PER-145, Version 1.6).
        /// </summary>
        public const byte FormatVersion = 2;

        public const byte PingKind = 1;
        public const byte PongKind = 2;
        public const byte ClaimKind = 3;
        public const byte LightMessageKind = 4;
        public const byte RulesKind = 5;
        public const byte StanceKind = 6;
        public const byte FlowKind = 7;
        public const byte GlanceKind = 8;
        public const byte TallyResetKind = 9;

        private const byte NoZone = byte.MaxValue;

        // An optional side or light that is not there.
        private const byte NoValue = byte.MaxValue;

        public static byte[] EncodePing(double senderSeconds)
        {
            return Write(PingKind, writer => writer.Write(senderSeconds));
        }

        public static byte[] EncodePong(double echoedSenderSeconds, double replierSeconds)
        {
            return Write(PongKind, writer =>
            {
                writer.Write(echoedSenderSeconds);
                writer.Write(replierSeconds);
            });
        }

        public static byte[] EncodeClaim(TouchClaim claim)
        {
            return Write(ClaimKind, writer =>
            {
                writer.Write(claim.Sequence);
                writer.Write((byte)claim.Attacker);
                writer.Write((byte)claim.Zone);
                writer.Write(claim.ContactHostSeconds);
                writer.Write(claim.IsOffStrip);
            });
        }

        public static byte[] EncodeLight(BoutLight light)
        {
            return Write(LightMessageKind, writer =>
            {
                writer.Write(light.Sequence);
                writer.Write((byte)light.Kind);
                writer.Write(light.HostZone.HasValue ? (byte)light.HostZone.Value : NoZone);
                writer.Write(light.PartnerZone.HasValue ? (byte)light.PartnerZone.Value : NoZone);
            });
        }

        public static byte[] EncodeRules(BoutRules rules)
        {
            return Write(RulesKind, writer =>
            {
                writer.Write((byte)rules.Pressure);
                writer.Write(rules.DoubleWindowSeconds);
            });
        }

        public static byte[] EncodeStance(FencerStance stance)
        {
            return Write(StanceKind, writer =>
            {
                writer.Write(stance.IsBehindLine);
                writer.Write((byte)stance.OffStrip);
            });
        }

        public static byte[] EncodeFlow(BoutFlowState state, PairedPracticeCounts hostTally, PairedPracticeCounts partnerTally)
        {
            return Write(FlowKind, writer =>
            {
                writer.Write((byte)state.Mode);
                writer.Write((byte)state.Phase);
                writer.Write(state.Sequence);
                writer.Write((byte)state.Halt);
                writer.Write(state.HaltedBy.HasValue ? (byte)state.HaltedBy.Value : NoValue);
                writer.Write((byte)state.HaltOffStrip);
                writer.Write(state.HaltLight.HasValue ? (byte)state.HaltLight.Value : NoValue);
                writer.Write(state.CallStartHostSeconds);
                writer.Write(state.IsHostReady);
                writer.Write(state.IsPartnerReady);
                WriteCounts(writer, hostTally);
                WriteCounts(writer, partnerTally);
            });
        }

        public static byte[] EncodeGlance()
        {
            return Write(GlanceKind, writer => { });
        }

        public static byte[] EncodeTallyReset()
        {
            return Write(TallyResetKind, writer => { });
        }

        /// <summary>The message's kind, or 0 when it is empty or from another format version.</summary>
        public static byte ReadKind(byte[] message)
        {
            return message != null && message.Length >= 2 && message[1] == FormatVersion ? message[0] : (byte)0;
        }

        /// <exception cref="InvalidDataException">Not a ping of this format, or cut short.</exception>
        public static double DecodePing(byte[] message)
        {
            return Read(message, PingKind, ReadTime);
        }

        /// <exception cref="InvalidDataException">Not a pong of this format, or cut short.</exception>
        public static (double EchoedSenderSeconds, double ReplierSeconds) DecodePong(byte[] message)
        {
            return Read(message, PongKind, reader => (ReadTime(reader), ReadTime(reader)));
        }

        /// <exception cref="InvalidDataException">Not a claim of this format, or cut short.</exception>
        public static TouchClaim DecodeClaim(byte[] message)
        {
            return Read(message, ClaimKind, reader => new TouchClaim(
                reader.ReadInt32(),
                ReadEnum<BoutSide>(reader.ReadByte()),
                ReadEnum<TargetZone>(reader.ReadByte()),
                ReadTime(reader),
                reader.ReadBoolean()));
        }

        /// <exception cref="InvalidDataException">Not a light of this format, or cut short.</exception>
        public static BoutLight DecodeLight(byte[] message)
        {
            return Read(message, LightMessageKind, reader => new BoutLight(
                reader.ReadInt32(),
                ReadEnum<LightKind>(reader.ReadByte()),
                ReadZone(reader.ReadByte()),
                ReadZone(reader.ReadByte())));
        }

        /// <exception cref="InvalidDataException">Not a rules message of this format, or cut short.</exception>
        public static BoutRules DecodeRules(byte[] message)
        {
            return Read(message, RulesKind, reader => new BoutRules(
                ReadEnum<TouchPressure>(reader.ReadByte()),
                BoutJudge.ClampDoubleWindow(ReadTime(reader))));
        }

        /// <exception cref="InvalidDataException">Not a stance of this format, or cut short.</exception>
        public static FencerStance DecodeStance(byte[] message)
        {
            return Read(message, StanceKind, reader => new FencerStance(reader.ReadBoolean(), ReadEnum<OffStripKind>(reader.ReadByte())));
        }

        /// <exception cref="InvalidDataException">Not a flow message of this format, or cut short.</exception>
        public static (BoutFlowState State, PairedPracticeCounts HostTally, PairedPracticeCounts PartnerTally) DecodeFlow(byte[] message)
        {
            return Read(message, FlowKind, reader =>
            {
                var state = new BoutFlowState(
                    ReadEnum<PartnerMode>(reader.ReadByte()),
                    ReadEnum<BoutPhase>(reader.ReadByte()),
                    reader.ReadInt32(),
                    ReadEnum<HaltReason>(reader.ReadByte()),
                    ReadOptional<BoutSide>(reader.ReadByte()),
                    ReadEnum<OffStripKind>(reader.ReadByte()),
                    ReadOptional<LightKind>(reader.ReadByte()),
                    ReadTime(reader),
                    reader.ReadBoolean(),
                    reader.ReadBoolean());
                return (state, ReadCounts(reader), ReadCounts(reader));
            });
        }

        /// <exception cref="InvalidDataException">Not a glance of this format, or not empty.</exception>
        public static void DecodeGlance(byte[] message)
        {
            Read(message, GlanceKind, reader => true);
        }

        /// <exception cref="InvalidDataException">Not a tally reset of this format, or not empty.</exception>
        public static void DecodeTallyReset(byte[] message)
        {
            Read(message, TallyResetKind, reader => true);
        }

        private static void WriteCounts(BinaryWriter writer, PairedPracticeCounts counts)
        {
            writer.Write(counts.Touches);
            writer.Write(counts.Doubles);
            writer.Write(counts.Glances);
        }

        private static PairedPracticeCounts ReadCounts(BinaryReader reader)
        {
            var touches = reader.ReadInt32();
            var doubles = reader.ReadInt32();
            var glances = reader.ReadInt32();
            if (touches < 0 || doubles < 0 || glances < 0)
            {
                throw new InvalidDataException("A tally below zero.");
            }

            return new PairedPracticeCounts(touches, doubles, glances);
        }

        private static T? ReadOptional<T>(byte value) where T : struct, Enum
        {
            return value == NoValue ? (T?)null : ReadEnum<T>(value);
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
                throw new InvalidDataException($"Not a kind {kind} bout message of format {FormatVersion}.");
            }

            try
            {
                using (var reader = new BinaryReader(new MemoryStream(message, 2, message.Length - 2)))
                {
                    var value = readBody(reader);
                    if (reader.BaseStream.Position != reader.BaseStream.Length)
                    {
                        throw new InvalidDataException($"A kind {kind} bout message has {reader.BaseStream.Length - reader.BaseStream.Position} bytes too many.");
                    }

                    return value;
                }
            }
            catch (EndOfStreamException error)
            {
                throw new InvalidDataException($"A kind {kind} bout message was cut short.", error);
            }
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

        private static TargetZone? ReadZone(byte value)
        {
            return value == NoZone ? (TargetZone?)null : ReadEnum<TargetZone>(value);
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
    }
}
