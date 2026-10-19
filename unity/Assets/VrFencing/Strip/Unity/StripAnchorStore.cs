using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using VrFencing.Settings;

namespace VrFencing.Strip
{
    /// <summary>
    /// Saves the Strip per room as a Meta spatial anchor at the Fencer's En Garde Line, and restores it on launch.
    /// Which saved Strip belongs to this room is decided in the Core (<see cref="SavedStrips.ChooseForRoom"/>): Meta
    /// keeps one map for the whole home, so other rooms' anchors localize too, at their real places, and only the
    /// distance from the Fencer tells the rooms apart (PER-91). This only talks to the anchors API and the saved Strips
    /// file under persistentDataPath. Every step is logged with a "[Strip]" tag, since anchor persistence can only be
    /// checked on the headset (adb logcat -s Unity).
    ///
    /// Needs anchor support in the Meta project config and the com.oculus.permission.USE_ANCHOR_API permission,
    /// both already on (Assets/Oculus/OculusProjectConfig.asset, Assets/Plugins/Android/AndroidManifest.xml).
    /// </summary>
    internal sealed class StripAnchorStore
    {
        /// <summary>How long one saved anchor may take to localize before it counts as not found.</summary>
        private const double LocalizeTimeoutSeconds = 10.0;

        private OVRSpatialAnchor _anchor;

        // The current anchor is this room's Strip (restored as this room's, or placed here), so re-placing replaces it.
        // False for another room's Strip the Fencer chose to keep: placing then adds this room instead.
        private bool _isThisRoomsStrip;

        private OVRSpatialAnchor.UnboundAnchor? _lastStripAnchor;

        public static string FilePath => Path.Combine(Application.persistentDataPath, SavedStrips.FileName);

        /// <summary>The anchor holding the current Strip's En Garde Line pose, or null.</summary>
        public Transform AnchorTransform => _anchor != null ? _anchor.transform : null;

        /// <summary>
        /// Loads every saved Strip's anchor, localizes them, and picks this room's Strip by where each was found
        /// relative to the Fencer. This room's Strip is bound at once; for <see cref="StripRoomMatch.OtherRoom"/> the
        /// last Strip's anchor waits for <see cref="BindLastStrip"/>.
        /// </summary>
        /// <param name="getFencerOnFloor">
        /// The Fencer's head dropped to the floor (y = 0), in the room; read after the anchors are found, when head
        /// tracking has settled.
        /// </param>
        public async Task<StripRoomChoice> RestoreAsync(Func<Vector3> getFencerOnFloor)
        {
            var notFound = new StripRoomChoice(StripRoomMatch.NotFound, null);
            var saved = LoadSavedStrips();
            if (saved.Strips.Count == 0)
            {
                Debug.Log("[Strip] No saved Strips yet.");
                return notFound;
            }

            var unboundAnchors = new List<OVRSpatialAnchor.UnboundAnchor>();
            var loadResult = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(saved.Strips.Select(strip => strip.AnchorId).ToList(), unboundAnchors);
            if (!loadResult.Success || !loadResult.TryGetValue(out var loadedAnchors))
            {
                Debug.LogWarning($"[Strip] Loading {saved.Strips.Count} saved anchor(s) failed: {loadResult.Status}.");
                return notFound;
            }

            Debug.Log($"[Strip] Loaded {loadedAnchors.Count} of {saved.Strips.Count} saved anchor(s); localizing.");

            // Start every localization first so they run together, then collect them.
            var localizations = loadedAnchors
                .Select(anchor => (Anchor: anchor, Task: anchor.Localized ? OVRTask.FromResult(true) : anchor.LocalizeAsync(LocalizeTimeoutSeconds)))
                .ToList();
            var localized = new Dictionary<Guid, OVRSpatialAnchor.UnboundAnchor>();
            var positions = new Dictionary<Guid, System.Numerics.Vector3>();
            foreach (var (anchor, task) in localizations)
            {
                if (!await task || !anchor.TryGetPose(out var pose))
                {
                    Debug.Log($"[Strip] Anchor {anchor.Uuid}: not found.");
                    continue;
                }

                localized[anchor.Uuid] = anchor;
                positions[anchor.Uuid] = pose.position.ToNumerics();
            }

            var fencerOnFloor = getFencerOnFloor();
            var fencer = fencerOnFloor.ToNumerics();
            foreach (var (anchorId, position) in positions)
            {
                var alongFloor = new Vector2(position.X - fencerOnFloor.x, position.Z - fencerOnFloor.z).magnitude;
                Debug.Log(
                    $"[Strip] Anchor {anchorId}: found {alongFloor:0.0} m from the Fencer along the floor, {position.Y - fencerOnFloor.y:+0.0;-0.0} m in height; " +
                    (StripRoomCheck.IsInFencersRoom(position, fencer) ? "this room." : "another room."));
            }

            var choice = saved.ChooseForRoom(positions, fencer);
            switch (choice.Match)
            {
                case StripRoomMatch.ThisRoom:
                    Bind(localized[choice.Strip.AnchorId]);
                    _isThisRoomsStrip = true;
                    Debug.Log($"[Strip] Restored this room's Strip {DescribeStrip(choice.Strip)}.");
                    break;

                case StripRoomMatch.OtherRoom:
                    _lastStripAnchor = localized[choice.Strip.AnchorId];
                    Debug.Log($"[Strip] No saved Strip in this room; the last Strip found is {DescribeStrip(choice.Strip)}.");
                    break;

                default:
                    Debug.Log("[Strip] No saved Strip found.");
                    break;
            }

            return choice;
        }

        /// <summary>
        /// Binds the last Strip's anchor found by <see cref="RestoreAsync"/> in another room, so the Fencer keeps using
        /// it. It stays that room's Strip: placing a Strip here later adds this room. False if there is none.
        /// </summary>
        public bool BindLastStrip()
        {
            if (!_lastStripAnchor.HasValue)
            {
                return false;
            }

            Bind(_lastStripAnchor.Value);
            _lastStripAnchor = null;
            _isThisRoomsStrip = false;
            return true;
        }

        /// <summary>
        /// Creates an anchor at <paramref name="placed"/>'s En Garde Line and saves it as this room's Strip. It replaces
        /// this room's previous Strip; a Strip kept from another room is left saved for that room (PER-91). Returns
        /// whether it was saved. If saving fails, the new anchor still holds the Strip for this session.
        /// </summary>
        public async Task<bool> SaveAsync(PlacedStrip placed, Pose enGardeLineInRoom)
        {
            var previousAnchor = _anchor;
            var replacedAnchorId = _isThisRoomsStrip && previousAnchor != null && previousAnchor.Created ? previousAnchor.Uuid : (Guid?)null;
            _lastStripAnchor = null;

            var anchorObject = new GameObject("Strip Anchor");
            anchorObject.transform.SetPositionAndRotation(enGardeLineInRoom.position, enGardeLineInRoom.rotation);
            var spatialAnchor = anchorObject.AddComponent<OVRSpatialAnchor>();
            _anchor = spatialAnchor;
            _isThisRoomsStrip = true;

            if (!await spatialAnchor.WhenLocalizedAsync())
            {
                Debug.LogWarning("[Strip] Could not create a spatial anchor for the Strip; it is not saved.");
                DropAnchorObject(previousAnchor);
                return false;
            }

            var saveResult = await spatialAnchor.SaveAnchorAsync();
            if (!saveResult.Success)
            {
                Debug.LogWarning($"[Strip] Saving anchor {spatialAnchor.Uuid} failed: {saveResult.Status}. The Strip is not saved.");
                DropAnchorObject(previousAnchor);
                return false;
            }

            var newStrip = new SavedStrip(spatialAnchor.Uuid, placed.PlacedLengthMeters, DateTime.UtcNow, SavedStripLayout.EnGardeLines);
            var saved = LoadSavedStrips().WithSaved(newStrip, replacedAnchorId);
            try
            {
                saved.Save(FilePath);
            }
            catch (IOException error)
            {
                Debug.LogError($"[Strip] Anchor {spatialAnchor.Uuid} saved, but writing '{FilePath}' failed: {error.Message}");
                DropAnchorObject(previousAnchor);
                return false;
            }

            Debug.Log(
                $"[Strip] Saved Strip {spatialAnchor.Uuid} (En Garde Lines {placed.PlacedLengthMeters:0.00} m apart), " +
                $"{(replacedAnchorId.HasValue ? "replacing this room's previous Strip" : "as a new room")}; {saved.Strips.Count} room(s) remembered.");
            if (replacedAnchorId.HasValue)
            {
                await EraseAsync(previousAnchor, replacedAnchorId.Value);
            }
            else
            {
                DropAnchorObject(previousAnchor);
            }

            return true;
        }

        private void Bind(OVRSpatialAnchor.UnboundAnchor unbound)
        {
            var anchorObject = new GameObject("Strip Anchor");
            var spatialAnchor = anchorObject.AddComponent<OVRSpatialAnchor>();
            unbound.BindTo(spatialAnchor);
            DropAnchorObject(_anchor);
            _anchor = spatialAnchor;
        }

        private static string DescribeStrip(SavedStrip strip)
        {
            var second = strip.Layout == SavedStripLayout.EnGardeLines ? "between the En Garde Lines" : "to the far end (saved before PER-90)";
            return $"{strip.AnchorId} ({strip.PlacedLengthMeters:0.00} m {second}, saved {strip.SavedAtUtc:u})";
        }

        /// <summary>Removes an anchor from the scene. Its saved copy, if any, stays on the headset.</summary>
        private static void DropAnchorObject(OVRSpatialAnchor anchor)
        {
            if (anchor != null)
            {
                UnityEngine.Object.Destroy(anchor.gameObject);
            }
        }

        private static async Task EraseAsync(OVRSpatialAnchor anchor, Guid anchorId)
        {
            if (anchor == null)
            {
                return;
            }

            var eraseResult = await anchor.EraseAnchorAsync();
            Debug.Log(eraseResult.Success
                ? $"[Strip] Erased the replaced anchor {anchorId}."
                : $"[Strip] Could not erase the replaced anchor {anchorId}: {eraseResult.Status}. It stays on the headset, unused.");
            UnityEngine.Object.Destroy(anchor.gameObject);
        }

        private static SavedStrips LoadSavedStrips()
        {
            try
            {
                return SavedStrips.Load(FilePath);
            }
            catch (SavedStripsFormatException error)
            {
                Debug.LogWarning($"[Strip] {error.Message} Starting with no saved Strips.");
                return SavedStrips.Empty;
            }
            catch (IOException error)
            {
                Debug.LogWarning($"[Strip] Could not read '{FilePath}': {error.Message} Starting with no saved Strips.");
                return SavedStrips.Empty;
            }
        }
    }
}
