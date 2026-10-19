using System.IO;
using UnityEngine;

namespace VrFencing.Settings
{
    /// <summary>
    /// The app's settings file under <see cref="Application.persistentDataPath"/>.
    /// Thin wrapper: all reading and writing is <see cref="SettingsStorage"/> in the Core.
    /// </summary>
    public static class SettingsFile
    {
        public static string FilePath => Path.Combine(Application.persistentDataPath, SettingsStorage.FileName);

        /// <summary>Loads the settings, or defaults if the file is missing or unreadable (the problem is logged, not thrown).</summary>
        public static TrainerSettings Load()
        {
            try
            {
                return SettingsStorage.Load(FilePath);
            }
            catch (SettingsFormatException error)
            {
                Debug.LogWarning($"[Settings] {error.Message} Using defaults.");
                return new TrainerSettings();
            }
            catch (IOException error)
            {
                Debug.LogWarning($"[Settings] Could not read '{FilePath}': {error.Message} Using defaults.");
                return new TrainerSettings();
            }
        }

        public static void Save(TrainerSettings settings)
        {
            SettingsStorage.Save(settings, FilePath);
        }
    }
}
