// Remembers the Fencer's blade alignment (mount preset, pitch, yaw) across app
// restarts in Application.persistentDataPath/blade-tuning.json.
using System;
using System.IO;
using UnityEngine;

namespace VrFencing.Prototypes.BladeFeel
{
    [Serializable]
    public sealed class BladeTuning
    {
        public int mountIndex;
        // Defaults: what Nikita chose in the first feel test (2026-10-19).
        public float pitchDegrees = 10f;
        public float yawDegrees = -10f;
    }

    public static class BladeTuningStore
    {
        private const string FileName = "blade-tuning.json";

        private static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        /// <summary>The saved tuning, or defaults when there is none or it cannot be read.</summary>
        public static BladeTuning Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new BladeTuning();
                var tuning = JsonUtility.FromJson<BladeTuning>(File.ReadAllText(FilePath));
                Debug.Log($"[BladeFeel] Loaded blade tuning from {FilePath}");
                return tuning ?? new BladeTuning();
            }
            catch (Exception exception)
            {
                Debug.LogError($"[BladeFeel] Cannot read {FilePath}, using defaults: {exception.Message}");
                return new BladeTuning();
            }
        }

        public static void Save(BladeTuning tuning)
        {
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(tuning));
            }
            catch (Exception exception)
            {
                Debug.LogError($"[BladeFeel] Cannot save blade tuning to {FilePath}: {exception.Message}");
            }
        }
    }
}
