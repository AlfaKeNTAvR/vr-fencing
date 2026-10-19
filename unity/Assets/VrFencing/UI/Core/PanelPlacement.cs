using System;
using System.IO;
using System.Numerics;
using Newtonsoft.Json;
using VrFencing.Settings;
using VrFencing.Strip;

namespace VrFencing.UI
{
    /// <summary>
    /// Where the menu panels go (PER-86): following the head (the default), or pinned at a spot the Fencer dragged them
    /// to. One spot holds every panel, the main menu and the Running panel alike, so nothing drifts during a Drill.
    ///
    /// A pin is kept in the Strip frame, so it survives a restart (the Strip's spatial anchor brings the frame back) and
    /// a re-placed Strip carries the panels along. A pin made before any Strip is placed can only be kept in the room
    /// for now: room coordinates reset every launch, so it is not saved, and it moves into the Strip frame, staying
    /// where it is, as soon as a Strip is in place. Immutable: changes return a new placement.
    /// </summary>
    public sealed class PanelPlacement
    {
        /// <summary>The file under the app's data folder; kept apart from settings.json because it is panel state, not a Setting.</summary>
        public const string FileName = "menu-placement.json";

        private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
        };

        private PanelPlacement(bool isPinned, bool isInStripFrame, TrackedPose pinnedPose)
        {
            IsPinned = isPinned;
            IsInStripFrame = isInStripFrame;
            PinnedPose = pinnedPose;
        }

        /// <summary>The panels follow the head: today's behaviour and the default.</summary>
        public static PanelPlacement Following { get; } = new PanelPlacement(false, false, new TrackedPose(Vector3.Zero, Quaternion.Identity));

        public bool IsPinned { get; }

        /// <summary>The pin is in the Strip frame (saved), not in the room (this launch only). False when following.</summary>
        public bool IsInStripFrame { get; }

        /// <summary>The pinned panel pose: in the Strip frame when <see cref="IsInStripFrame"/>, else in the room. Unused when following.</summary>
        public TrackedPose PinnedPose { get; }

        /// <summary>Pins the panels at <paramref name="panelInRoom"/>, in the Strip frame when a Strip is in place.</summary>
        /// <param name="enGardeLine">The Strip frame's pose in the room, or null when no Strip is placed.</param>
        public static PanelPlacement PinAt(TrackedPose panelInRoom, TrackedPose? enGardeLine)
        {
            var rotation = Quaternion.Normalize(panelInRoom.Rotation);
            var pose = new TrackedPose(panelInRoom.Position, rotation);
            return enGardeLine.HasValue
                ? new PanelPlacement(true, true, StripFrame.ToStripFrame(enGardeLine.Value, pose))
                : new PanelPlacement(true, false, pose);
        }

        /// <summary>
        /// Where a pinned panel stands in the room now, or null when the panels follow the head: unpinned, or pinned to a
        /// Strip that is not in place (still being restored at launch, or being re-placed).
        /// </summary>
        /// <param name="enGardeLine">The Strip frame's pose in the room, or null when no Strip is placed.</param>
        public TrackedPose? GetPoseInRoom(TrackedPose? enGardeLine)
        {
            if (!IsPinned)
            {
                return null;
            }

            if (!IsInStripFrame)
            {
                return PinnedPose;
            }

            if (!enGardeLine.HasValue)
            {
                return null;
            }

            var strip = enGardeLine.Value;
            return new TrackedPose(
                StripFrame.ToRoom(strip, PinnedPose.Position),
                Quaternion.Normalize(strip.Rotation * PinnedPose.Rotation));
        }

        /// <summary>
        /// Moves a room pin into the Strip frame without moving the panels, once a Strip is in place. Any other
        /// placement comes back unchanged.
        /// </summary>
        public PanelPlacement AttachToStrip(TrackedPose enGardeLine)
        {
            return IsPinned && !IsInStripFrame ? PinAt(PinnedPose, enGardeLine) : this;
        }

        /// <summary>
        /// Writes the placement. A room pin is written as following: its coordinates mean nothing after a restart.
        /// </summary>
        public void Save(string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var file = new PanelPlacementFile { IsPinned = IsPinned && IsInStripFrame };
            if (file.IsPinned)
            {
                var position = PinnedPose.Position;
                var rotation = PinnedPose.Rotation;
                file.PositionInStrip = new[] { position.X, position.Y, position.Z };
                file.RotationInStrip = new[] { rotation.X, rotation.Y, rotation.Z, rotation.W };
            }

            File.WriteAllText(path, JsonConvert.SerializeObject(file, SerializerSettings));
        }

        /// <summary>Returns <see cref="Following"/> when the file does not exist yet.</summary>
        /// <exception cref="PanelPlacementFormatException">The file exists but is not a valid placement file.</exception>
        public static PanelPlacement Load(string path)
        {
            if (!File.Exists(path))
            {
                return Following;
            }

            PanelPlacementFile file;
            try
            {
                file = JsonConvert.DeserializeObject<PanelPlacementFile>(File.ReadAllText(path), SerializerSettings);
            }
            catch (JsonException error)
            {
                throw new PanelPlacementFormatException($"Menu placement file '{path}' is not valid: {error.Message}", error);
            }

            if (file == null || !file.IsPinned)
            {
                return Following;
            }

            var position = ReadNumbers(file.PositionInStrip, 3, path, "positionInStrip");
            var rotation = ReadNumbers(file.RotationInStrip, 4, path, "rotationInStrip");
            var quaternion = new Quaternion(rotation[0], rotation[1], rotation[2], rotation[3]);
            if (quaternion.LengthSquared() < 1e-6f)
            {
                throw new PanelPlacementFormatException($"Menu placement file '{path}' has a zero rotation.", null);
            }

            var pose = new TrackedPose(new Vector3(position[0], position[1], position[2]), Quaternion.Normalize(quaternion));
            return new PanelPlacement(true, true, pose);
        }

        private static float[] ReadNumbers(float[] numbers, int count, string path, string name)
        {
            if (numbers == null || numbers.Length != count)
            {
                throw new PanelPlacementFormatException($"Menu placement file '{path}' needs {count} numbers in '{name}'.", null);
            }

            foreach (var number in numbers)
            {
                if (float.IsNaN(number) || float.IsInfinity(number))
                {
                    throw new PanelPlacementFormatException($"Menu placement file '{path}' has a number that is not finite in '{name}'.", null);
                }
            }

            return numbers;
        }

        private sealed class PanelPlacementFile
        {
            [JsonProperty("isPinned")]
            public bool IsPinned { get; set; }

            [JsonProperty("positionInStrip")]
            public float[] PositionInStrip { get; set; }

            [JsonProperty("rotationInStrip")]
            public float[] RotationInStrip { get; set; }
        }
    }

    /// <summary>The menu placement file exists but could not be read.</summary>
    public sealed class PanelPlacementFormatException : Exception
    {
        public PanelPlacementFormatException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
