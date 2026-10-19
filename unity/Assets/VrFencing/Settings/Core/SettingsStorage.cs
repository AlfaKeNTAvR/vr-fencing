using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace VrFencing.Settings
{
    /// <summary>
    /// Saves and loads <see cref="TrainerSettings"/> as JSON. The caller supplies the path,
    /// so the Unity layer can pass a file under persistentDataPath and tests a temp folder.
    /// </summary>
    public static class SettingsStorage
    {
        public const string FileName = "settings.json";

        private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            Converters = { new StringEnumConverter() },
            // Replace nested objects instead of merging into the defaults' instances.
            ObjectCreationHandling = ObjectCreationHandling.Replace,
        };

        public static void Save(TrainerSettings settings, string path)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, JsonConvert.SerializeObject(settings, SerializerSettings));
        }

        /// <summary>
        /// Returns defaults when the file does not exist yet. Values missing from the file
        /// (written by an older version) keep their defaults.
        /// </summary>
        /// <exception cref="SettingsFormatException">The file exists but is not valid settings JSON.</exception>
        public static TrainerSettings Load(string path)
        {
            if (!File.Exists(path))
            {
                return new TrainerSettings();
            }

            var json = File.ReadAllText(path);
            try
            {
                return JsonConvert.DeserializeObject<TrainerSettings>(json, SerializerSettings) ?? new TrainerSettings();
            }
            catch (JsonException error)
            {
                throw new SettingsFormatException($"Settings file '{path}' is not valid: {error.Message}", error);
            }
        }
    }

    /// <summary>The settings file exists but could not be read as settings.</summary>
    public sealed class SettingsFormatException : Exception
    {
        public SettingsFormatException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
