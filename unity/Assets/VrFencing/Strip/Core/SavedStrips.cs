using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace VrFencing.Strip
{
    /// <summary>
    /// One saved Strip: the spatial anchor at its En Garde Line, and its placed length. The anchor holds the pose in
    /// the room; the Strip's other dimensions follow from the length and the current settings.
    /// </summary>
    public sealed class SavedStrip
    {
        [JsonConstructor]
        public SavedStrip(Guid anchorId, float placedLengthMeters, DateTime savedAtUtc)
        {
            AnchorId = anchorId;
            PlacedLengthMeters = placedLengthMeters;
            SavedAtUtc = savedAtUtc;
        }

        /// <summary>The persisted spatial anchor's UUID (Meta anchors API).</summary>
        public Guid AnchorId { get; }

        public float PlacedLengthMeters { get; }

        public DateTime SavedAtUtc { get; }
    }

    /// <summary>
    /// The Strips saved per room, newest first. A spatial anchor only localizes in the room (space) it was made in,
    /// so on launch every saved anchor is tried and the newest one that localizes is this room's Strip; none
    /// localizing means this room has no Strip yet, or the headset lost it, and the Fencer places it again.
    /// Immutable: changes return a new list.
    /// </summary>
    public sealed class SavedStrips
    {
        public const string FileName = "strip-anchors.json";

        /// <summary>Rooms remembered; the oldest is dropped beyond this. Also keeps anchor loading under Meta's 50-per-query limit.</summary>
        public const int MaxSavedStrips = 10;

        private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
        };

        public SavedStrips(IEnumerable<SavedStrip> strips)
        {
            Strips = (strips ?? throw new ArgumentNullException(nameof(strips))).Where(strip => strip != null).ToList();
        }

        public static SavedStrips Empty { get; } = new SavedStrips(Array.Empty<SavedStrip>());

        /// <summary>Newest first.</summary>
        public IReadOnlyList<SavedStrip> Strips { get; }

        /// <summary>
        /// Adds a newly saved Strip as the newest. <paramref name="replacedAnchorId"/> is the anchor of the Strip it
        /// replaces in this room (the one restored at launch), which is removed; the list is then trimmed to
        /// <see cref="MaxSavedStrips"/>.
        /// </summary>
        public SavedStrips WithSaved(SavedStrip strip, Guid? replacedAnchorId)
        {
            if (strip == null)
            {
                throw new ArgumentNullException(nameof(strip));
            }

            var kept = Strips.Where(saved => saved.AnchorId != strip.AnchorId && saved.AnchorId != replacedAnchorId);
            return new SavedStrips(new[] { strip }.Concat(kept).Take(MaxSavedStrips));
        }

        /// <summary>The newest saved Strip whose anchor localized in this room, or null if none did.</summary>
        public SavedStrip ChooseRestored(IEnumerable<Guid> localizedAnchorIds)
        {
            var localized = new HashSet<Guid>(localizedAnchorIds ?? Array.Empty<Guid>());
            return Strips.FirstOrDefault(strip => localized.Contains(strip.AnchorId));
        }

        public void Save(string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, JsonConvert.SerializeObject(new SavedStripsFile { Strips = Strips.ToList() }, SerializerSettings));
        }

        /// <summary>Returns <see cref="Empty"/> when the file does not exist yet.</summary>
        /// <exception cref="SavedStripsFormatException">The file exists but is not a valid saved Strips file.</exception>
        public static SavedStrips Load(string path)
        {
            if (!File.Exists(path))
            {
                return Empty;
            }

            try
            {
                var file = JsonConvert.DeserializeObject<SavedStripsFile>(File.ReadAllText(path), SerializerSettings);
                return new SavedStrips(file?.Strips ?? new List<SavedStrip>());
            }
            catch (JsonException error)
            {
                throw new SavedStripsFormatException($"Saved Strips file '{path}' is not valid: {error.Message}", error);
            }
        }

        private sealed class SavedStripsFile
        {
            public List<SavedStrip> Strips { get; set; }
        }
    }

    /// <summary>The saved Strips file exists but could not be read.</summary>
    public sealed class SavedStripsFormatException : Exception
    {
        public SavedStripsFormatException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
