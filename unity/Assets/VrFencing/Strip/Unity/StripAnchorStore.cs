using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace VrFencing.Strip
{
    /// <summary>
    /// Saves the Strip per room as a Meta spatial anchor at its En Garde Line, and restores it on launch.
    /// Which saved Strip belongs to this room is decided in the Core (<see cref="SavedStrips"/>); this only talks to
    /// the anchors API and the saved Strips file under persistentDataPath. Every step is logged with a "[Strip]" tag,
    /// since anchor persistence can only be checked on the headset (adb logcat -s Unity).
    ///
    /// Needs anchor support in the Meta project config and the com.oculus.permission.USE_ANCHOR_API permission,
    /// both already on (Assets/Oculus/OculusProjectConfig.asset, Assets/Plugins/Android/AndroidManifest.xml).
    /// </summary>
    internal sealed class StripAnchorStore
    {
        /// <summary>How long one saved anchor may take to localize before it counts as not in this room.</summary>
        private const double LocalizeTimeoutSeconds = 10.0;

        private OVRSpatialAnchor _anchor;

        public static string FilePath => Path.Combine(Application.persistentDataPath, SavedStrips.FileName);

        /// <summary>The anchor holding the current Strip's En Garde Line pose, or null.</summary>
        public Transform AnchorTransform => _anchor != null ? _anchor.transform : null;

        /// <summary>
        /// Loads every saved Strip's anchor, localizes them, and binds the newest one found in this room.
        /// Returns that Strip, or null when no saved Strip is found here (or none was ever saved).
        /// </summary>
        public async Task<SavedStrip> RestoreAsync()
        {
            var saved = LoadSavedStrips();
            if (saved.Strips.Count == 0)
            {
                Debug.Log("[Strip] No saved Strips yet.");
                return null;
            }

            var unboundAnchors = new List<OVRSpatialAnchor.UnboundAnchor>();
            var loadResult = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(saved.Strips.Select(strip => strip.AnchorId).ToList(), unboundAnchors);
            if (!loadResult.Success || !loadResult.TryGetValue(out var loadedAnchors))
            {
                Debug.LogWarning($"[Strip] Loading {saved.Strips.Count} saved anchor(s) failed: {loadResult.Status}.");
                return null;
            }

            Debug.Log($"[Strip] Loaded {loadedAnchors.Count} of {saved.Strips.Count} saved anchor(s); localizing.");

            // Start every localization first so they run together, then collect them.
            var localizations = loadedAnchors
                .Select(anchor => (Anchor: anchor, Task: anchor.Localized ? OVRTask.FromResult(true) : anchor.LocalizeAsync(LocalizeTimeoutSeconds)))
                .ToList();
            var localized = new Dictionary<Guid, OVRSpatialAnchor.UnboundAnchor>();
            foreach (var (anchor, task) in localizations)
            {
                var isLocalized = await task;
                Debug.Log($"[Strip] Anchor {anchor.Uuid}: {(isLocalized ? "found in this room" : "not found")}.");
                if (isLocalized)
                {
                    localized[anchor.Uuid] = anchor;
                }
            }

            var restored = saved.ChooseRestored(localized.Keys);
            if (restored == null)
            {
                Debug.Log("[Strip] No saved Strip found in this room.");
                return null;
            }

            var anchorObject = new GameObject("Strip Anchor");
            var spatialAnchor = anchorObject.AddComponent<OVRSpatialAnchor>();
            localized[restored.AnchorId].BindTo(spatialAnchor);
            _anchor = spatialAnchor;
            Debug.Log($"[Strip] Restored Strip {restored.AnchorId} ({restored.PlacedLengthMeters:0.00} m placed, saved {restored.SavedAtUtc:u}).");
            return restored;
        }

        /// <summary>
        /// Creates an anchor at <paramref name="placed"/>'s En Garde Line and saves it, replacing this room's previous
        /// Strip. Returns whether it was saved. If saving fails, the new anchor still holds the Strip for this session.
        /// </summary>
        public async Task<bool> SaveAsync(PlacedStrip placed, Pose enGardeLineInRoom)
        {
            var previousAnchor = _anchor;
            var previousAnchorId = previousAnchor != null && previousAnchor.Created ? previousAnchor.Uuid : (Guid?)null;

            var anchorObject = new GameObject("Strip Anchor");
            anchorObject.transform.SetPositionAndRotation(enGardeLineInRoom.position, enGardeLineInRoom.rotation);
            var spatialAnchor = anchorObject.AddComponent<OVRSpatialAnchor>();
            _anchor = spatialAnchor;

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

            var saved = LoadSavedStrips().WithSaved(new SavedStrip(spatialAnchor.Uuid, placed.PlacedLengthMeters, DateTime.UtcNow), previousAnchorId);
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

            Debug.Log($"[Strip] Saved Strip {spatialAnchor.Uuid} ({placed.PlacedLengthMeters:0.00} m placed); {saved.Strips.Count} room(s) remembered.");
            if (previousAnchorId.HasValue)
            {
                await EraseAsync(previousAnchor, previousAnchorId.Value);
            }
            else
            {
                DropAnchorObject(previousAnchor);
            }

            return true;
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
