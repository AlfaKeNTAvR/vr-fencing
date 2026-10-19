// Reads every value in a TrainerSettings by its path ("WarningZone.MinimumLengthMeters"), so tests notice a
// Setting added later that the settings screen or Clone forgot.
using System.Collections.Generic;
using System.Reflection;
using VrFencing.Settings;

namespace VrFencing.Tests.Settings
{
    internal static class SettingsLeaves
    {
        public static Dictionary<string, object> Read(TrainerSettings settings)
        {
            var leaves = new Dictionary<string, object>();
            Collect(settings, string.Empty, leaves);
            return leaves;
        }

        private static void Collect(object owner, string prefix, Dictionary<string, object> leaves)
        {
            foreach (var property in owner.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var value = property.GetValue(owner);
                var path = prefix + property.Name;
                if (property.PropertyType.IsClass && property.PropertyType != typeof(string))
                {
                    Collect(value, path + ".", leaves);
                }
                else
                {
                    leaves[path] = value;
                }
            }
        }
    }
}
