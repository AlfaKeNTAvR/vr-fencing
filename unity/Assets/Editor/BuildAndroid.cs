// Headless APK build, called by scripts/build-apk.sh:
//   Unity.exe -batchmode -quit -projectPath unity -buildTarget Android
//             -executeMethod VrFencing.EditorTools.BuildAndroid.BuildDevelopmentApk
//             -apkPath <output.apk>
// Development Build so Meta XR Operator's in-app layer and the profiler are included.
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace VrFencing.EditorTools
{
    public static class BuildAndroid
    {
        private const string ApkPathArgument = "-apkPath";
        private const string DefaultApkPath = "../Builds/Android/vr-fencing-dev.apk";

        public static void BuildDevelopmentApk()
        {
            var apkPath = ReadArgument(ApkPathArgument) ?? DefaultApkPath;
            var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("[BuildAndroid] No enabled scenes in Build Settings.");
                EditorApplication.Exit(1);
                return;
            }

            EditorUserBuildSettings.buildAppBundle = false;
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = apkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.Development,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"[BuildAndroid] Result={summary.result} Errors={summary.totalErrors} " +
                      $"Path={summary.outputPath} Time={summary.totalTime}");

            // -quit alone would return exit code 0 even for a failed build.
            EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        private static string ReadArgument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
    }
}
