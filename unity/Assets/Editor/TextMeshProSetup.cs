// Imports TextMeshPro's essential resources (default SDF font, its material and shaders, TMP Settings) into
// Assets/TextMesh Pro, the same as the "Import TMP Essentials" button, but without a window, so batchmode can run it.
// World-space text uses TextMeshPro because its SDF shaders render in both eyes under single-pass instanced stereo.
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VrFencing.EditorTools
{
    public static class TextMeshProSetup
    {
        private const string UguiPackagePath = "Packages/com.unity.ugui";
        private const string EssentialsPackageName = "Package Resources/TMP Essential Resources.unitypackage";
        private const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

        /// <summary>
        /// Menu entry point, and batchmode entry point when run without -quit: the import finishes after this returns,
        /// so in batchmode the editor exits from the import callback. Does nothing when the essentials are already in.
        /// </summary>
        [MenuItem("VR Fencing/Import TextMeshPro essentials")]
        public static void ImportEssentialsIfMissing()
        {
            if (File.Exists(TmpSettingsPath))
            {
                Debug.Log("[TextMeshPro] Essentials already imported.");
                ExitIfBatchMode(0);
                return;
            }

            var ugui = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(UguiPackagePath);
            if (ugui == null)
                throw new System.InvalidOperationException($"Package {UguiPackagePath} not found; TextMeshPro ships in it.");

            var essentials = Path.Combine(ugui.resolvedPath, EssentialsPackageName);
            if (!File.Exists(essentials))
                throw new FileNotFoundException("TMP Essential Resources package not found.", essentials);

            AssetDatabase.importPackageCompleted += packageName =>
            {
                Debug.Log($"[TextMeshPro] Imported essentials ({packageName}).");
                ExitIfBatchMode(0);
            };
            AssetDatabase.importPackageFailed += (packageName, error) =>
            {
                Debug.LogError($"[TextMeshPro] Importing {packageName} failed: {error}");
                ExitIfBatchMode(1);
            };
            AssetDatabase.ImportPackage(essentials, false);
        }

        private static void ExitIfBatchMode(int exitCode)
        {
            if (Application.isBatchMode)
                EditorApplication.Exit(exitCode);
        }
    }
}
