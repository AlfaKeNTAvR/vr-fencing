using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Newtonsoft.Json;
using VrFencing.Settings;

namespace VrFencing.Strip
{
    /// <summary>What a saved Strip's second placement point was. Stored as a number in the saved Strips file.</summary>
    public enum SavedStripLayout
    {
        /// <summary>
        /// Saved before PER-90: the second point was the far end of the Strip. Files from then have no layout field,
        /// so a missing field reads as this.
        /// </summary>
        FarEnd = 0,

        /// <summary>The second point was the Dummy's En Garde Line (PER-90).</summary>
        EnGardeLines = 1,
    }

    /// <summary>
    /// One saved Strip: the spatial anchor at the Fencer's En Garde Line, and the placed length. The anchor holds the
    /// pose in the room; the Strip's other dimensions follow from the length and the current settings.
    /// </summary>
    public sealed class SavedStrip
    {
        [JsonConstructor]
        public SavedStrip(Guid anchorId, float placedLengthMeters, DateTime savedAtUtc, SavedStripLayout layout)
        {
            AnchorId = anchorId;
            PlacedLengthMeters = placedLengthMeters;
            SavedAtUtc = savedAtUtc;
            Layout = layout;
        }

        /// <summary>The persisted spatial anchor's UUID (Meta anchors API).</summary>
        public Guid AnchorId { get; }

        /// <summary>The distance between the two placement points; what the second point was depends on <see cref="Layout"/>.</summary>
        public float PlacedLengthMeters { get; }

        public DateTime SavedAtUtc { get; }

        public SavedStripLayout Layout { get; }

        /// <summary>
        /// The gap between the Fencer's and the Dummy's En Garde Lines. A Strip saved with the far end as its second
        /// point is converted so it keeps the same far end (<see cref="StripGeometry.ConvertFarEndLengthToGap"/>).
        /// </summary>
        public float GetEnGardeGapMeters(float fencerHeightMeters, WarningZoneSettings warningZone)
        {
            return Layout == SavedStripLayout.EnGardeLines
                ? PlacedLengthMeters
                : StripGeometry.ConvertFarEndLengthToGap(PlacedLengthMeters, fencerHeightMeters, warningZone);
        }
    }

    /// <summary>How a saved Strip relates to the room the Fencer is in at launch (PER-91).</summary>
    public enum StripRoomMatch
    {
        /// <summary>No saved Strip's anchor was found at all.</summary>
        NotFound,

        /// <summary>A saved Strip was found here: it is this room's Strip and comes back without asking.</summary>
        ThisRoom,

        /// <summary>Saved Strips were found, but all of them in other rooms: the Fencer is asked "New Strip here?".</summary>
        OtherRoom,
    }

    /// <summary>The saved Strip chosen at launch and how it relates to this room.</summary>
    public readonly struct StripRoomChoice
    {
        public StripRoomChoice(StripRoomMatch match, SavedStrip strip)
        {
            Match = match;
            Strip = strip;
        }

        public StripRoomMatch Match { get; }

        /// <summary>This room's Strip, or for <see cref="StripRoomMatch.OtherRoom"/> the last Strip (newest found); null when none was found.</summary>
        public SavedStrip Strip { get; }
    }

    /// <summary>
    /// Whether a found anchor is in the Fencer's room. Meta keeps one map for a whole home, so an anchor saved in
    /// another room, even on another floor, still localizes at its real place, and the headset exposes no room or
    /// space ID without a Space Setup scan (PER-91). An anchor counts as another room's when it is far from the
    /// Fencer along the floor, or at a different floor height.
    /// </summary>
    public static class StripRoomCheck
    {
        /// <summary>Farther than this along the floor from the Fencer: another room.</summary>
        public const float MaxHorizontalDistanceMeters = 10.0f;

        /// <summary>More than this above or below the Fencer's floor: another floor (a storey is about 2.7 to 3 m).</summary>
        public const float MaxHeightDifferenceMeters = 2.0f;

        /// <param name="anchorPosition">The found anchor's position in the room (it lies on the floor it was placed on).</param>
        /// <param name="fencerOnFloor">The Fencer's position dropped to the current floor (floor-level tracking: y = 0 is the floor).</param>
        public static bool IsInFencersRoom(Vector3 anchorPosition, Vector3 fencerOnFloor)
        {
            var alongFloor = new Vector2(anchorPosition.X - fencerOnFloor.X, anchorPosition.Z - fencerOnFloor.Z).Length();
            return alongFloor <= MaxHorizontalDistanceMeters
                && Math.Abs(anchorPosition.Y - fencerOnFloor.Y) <= MaxHeightDifferenceMeters;
        }
    }

    /// <summary>
    /// The Strips saved per room, newest first. On launch every saved anchor is tried; one found near the Fencer is
    /// this room's Strip (<see cref="ChooseForRoom"/>). None near means a room without a Strip yet, and placing one
    /// there adds a room instead of replacing another room's Strip.
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
        /// Adds a newly saved Strip as the newest. <paramref name="replacedAnchorId"/> is the anchor of this room's
        /// Strip it replaces, which is removed; null when the room had none, so every other room's Strip is kept. The
        /// list is then trimmed to <see cref="MaxSavedStrips"/>.
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

        /// <summary>
        /// Picks this room's Strip from the anchors found at launch. Of the saved Strips found in the Fencer's room
        /// (<see cref="StripRoomCheck"/>), the one nearest the Fencer wins (the newest on a tie), as Meta advises when
        /// several anchors localize. If every Strip found is in another room, the choice is the newest of them, as the
        /// last Strip the Fencer can keep.
        /// </summary>
        /// <param name="localizedAnchorPositions">Where each found anchor is in the room, by anchor UUID.</param>
        /// <param name="fencerOnFloor">The Fencer's position dropped to the current floor.</param>
        public StripRoomChoice ChooseForRoom(IReadOnlyDictionary<Guid, Vector3> localizedAnchorPositions, Vector3 fencerOnFloor)
        {
            var found = Strips
                .Where(strip => localizedAnchorPositions != null && localizedAnchorPositions.ContainsKey(strip.AnchorId))
                .ToList();
            if (found.Count == 0)
            {
                return new StripRoomChoice(StripRoomMatch.NotFound, null);
            }

            SavedStrip nearest = null;
            var nearestDistance = float.MaxValue;
            foreach (var strip in found)
            {
                var position = localizedAnchorPositions[strip.AnchorId];
                var distance = new Vector2(position.X - fencerOnFloor.X, position.Z - fencerOnFloor.Z).Length();
                if (StripRoomCheck.IsInFencersRoom(position, fencerOnFloor) && distance < nearestDistance)
                {
                    nearest = strip;
                    nearestDistance = distance;
                }
            }

            return nearest != null
                ? new StripRoomChoice(StripRoomMatch.ThisRoom, nearest)
                : new StripRoomChoice(StripRoomMatch.OtherRoom, found[0]);
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

        /// <summary>
        /// Returns <see cref="Empty"/> when the file does not exist yet. Files saved before PER-90 load too: their Strips
        /// read as <see cref="SavedStripLayout.FarEnd"/>.
        /// </summary>
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
