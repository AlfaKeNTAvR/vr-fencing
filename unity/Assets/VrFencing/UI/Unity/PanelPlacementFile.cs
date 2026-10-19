using System;
using System.IO;
using UnityEngine;

namespace VrFencing.UI
{
    /// <summary>
    /// The menu placement file (PER-86) under <see cref="Application.persistentDataPath"/>, next to settings.json and the
    /// saved Strips. Thin wrapper: reading and writing is <see cref="PanelPlacement"/> in the UI Core.
    /// </summary>
    internal static class PanelPlacementFile
    {
        public static string FilePath => Path.Combine(Application.persistentDataPath, PanelPlacement.FileName);

        /// <summary>Loads the placement, or following if the file is missing or unreadable (the problem is logged, not thrown).</summary>
        public static PanelPlacement Load()
        {
            try
            {
                return PanelPlacement.Load(FilePath);
            }
            catch (PanelPlacementFormatException error)
            {
                Debug.LogWarning($"[UI] {error.Message} The panels follow the head.");
                return PanelPlacement.Following;
            }
            catch (IOException error)
            {
                Debug.LogWarning($"[UI] Could not read '{FilePath}': {error.Message} The panels follow the head.");
                return PanelPlacement.Following;
            }
        }

        /// <summary>Saves the placement; a failure is logged, and the placement is still used until the app closes.</summary>
        public static void Save(PanelPlacement placement)
        {
            try
            {
                placement.Save(FilePath);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Debug.LogError($"[UI] Saving the menu placement failed: {error.Message}");
            }
        }
    }
}
