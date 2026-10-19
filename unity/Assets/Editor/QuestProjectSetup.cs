// One-shot project setup for Meta Quest 3 (passthrough MR, OpenXR, URP).
// Run headless with scripts/setup-quest-project.sh, or from the Editor menu
// "VR Fencing > Apply Quest project setup". Safe to re-run: every step checks
// the current state and only changes what is missing.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Meta.XR.Editor.Id;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Management;

namespace VrFencing.EditorTools
{
    public static class QuestProjectSetup
    {
        public const string PassthroughScenePath = "Assets/Scenes/Passthrough.unity";

        private const string CompanyName = "AlfaKeNTAvR";
        private const string ProductName = "VR Fencing";
        private const string AndroidPackageId = "com.alfakentavr.vrfencing";
        // Horizon OS requires Android 12L (API 32) as the minimum for current Meta XR SDKs.
        private const AndroidSdkVersions MinAndroidSdk = AndroidSdkVersions.AndroidApiLevel32;
        private const int QuestMsaaSamples = 4;

        private const string UrpAssetPath = "Assets/Settings/URP-Quest.asset";
        private const string UrpRendererPath = "Assets/Settings/URP-Quest-Renderer.asset";
        private const string XrSettingsFolder = "Assets/XR";
        private const string XrGeneralSettingsPath = XrSettingsFolder + "/XRGeneralSettingsPerBuildTarget.asset";
        private const string OpenXrLoaderType = "UnityEngine.XR.OpenXR.OpenXRLoader";
        private const string MetaXrFeatureSetId = "com.meta.openxr.featureset.metaxr";
        private const double SetupToolTimeoutSeconds = 300;

        // Project Setup Tool fixes that must never run unattended. Uids come from the
        // tool's JSON report (Builds/logs/project-setup-report.json).
        private static readonly HashSet<string> ExcludedFixTaskUids = new()
        {
            // "Newest Meta XR Simulator not installed": downloads a ~1 GB installer into
            // the user's Downloads folder and opens Explorer. Installing is Nikita's call.
            "5c7289ec557af63ae9fe7dc8bb186ecb",
        };

        [MenuItem("VR Fencing/Apply Quest project setup")]
        public static void ApplyFromMenu()
        {
            ApplyStaticSettings();
            Debug.Log("[QuestProjectSetup] Settings applied. Run Meta > Tools > Project Setup Tool > Fix All next.");
        }

        /// <summary>
        /// Batchmode entry point. Do not pass -quit: the method exits the Editor itself
        /// after Meta's Project Setup Tool has finished its asynchronous fixes.
        /// </summary>
        public static void ApplyInBatchmode()
        {
            try
            {
                ApplyStaticSettings();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
                return;
            }

            RunMetaProjectSetupToolThenExit();
        }

        private static void ApplyStaticSettings()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            ConfigurePlayerSettings();
            ConfigureUniversalRenderPipeline();
            EnableOpenXrLoader(BuildTargetGroup.Android);
            EnableOpenXrLoader(BuildTargetGroup.Standalone);
            EnableMetaXrFeatureSet(BuildTargetGroup.Android);
            EnableMetaXrFeatureSet(BuildTargetGroup.Standalone);
            ConfigureMetaProjectConfig();
            PassthroughSceneBuilder.CreateOrUpdateScene(PassthroughScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(PassthroughScenePath, true) };
            AssetDatabase.SaveAssets();
        }

        private static void ConfigurePlayerSettings()
        {
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, AndroidPackageId);
            PlayerSettings.Android.minSdkVersion = MinAndroidSdk;
            // Meta Quest Store requires target API 34 (Project Setup Tool rule).
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)34;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            // Meta XR Operator's in-app MCP server needs network access (Development Builds only).
            PlayerSettings.Android.forceInternetPermission = true;
        }

        private static void ConfigureUniversalRenderPipeline()
        {
            var pipelineAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
            if (pipelineAsset == null)
            {
                EnsureFolder("Assets/Settings");
                var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, UrpRendererPath);
                pipelineAsset = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(pipelineAsset, UrpAssetPath);
            }

            // Quest guidance: no HDR, 4x MSAA, no depth/opaque texture copies.
            pipelineAsset.supportsHDR = false;
            pipelineAsset.msaaSampleCount = QuestMsaaSamples;
            pipelineAsset.supportsCameraDepthTexture = false;
            pipelineAsset.supportsCameraOpaqueTexture = false;
            EditorUtility.SetDirty(pipelineAsset);

            GraphicsSettings.defaultRenderPipeline = pipelineAsset;
            for (var level = 0; level < QualitySettings.names.Length; level++)
            {
                QualitySettings.SetQualityLevel(level, false);
                QualitySettings.renderPipeline = pipelineAsset;
            }
        }

        private static void EnableOpenXrLoader(BuildTargetGroup buildTargetGroup)
        {
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey,
                    out XRGeneralSettingsPerBuildTarget perBuildTarget) || perBuildTarget == null)
            {
                EnsureFolder(XrSettingsFolder);
                perBuildTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perBuildTarget, XrGeneralSettingsPath);
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perBuildTarget, true);
            }

            if (!perBuildTarget.HasSettingsForBuildTarget(buildTargetGroup))
                perBuildTarget.CreateDefaultSettingsForBuildTarget(buildTargetGroup);
            if (!perBuildTarget.HasManagerSettingsForBuildTarget(buildTargetGroup))
                perBuildTarget.CreateDefaultManagerSettingsForBuildTarget(buildTargetGroup);

            var generalSettings = perBuildTarget.SettingsForBuildTarget(buildTargetGroup);
            generalSettings.InitManagerOnStart = true;
            var managerSettings = generalSettings.AssignedSettings;
            var assigned = XRPackageMetadataStore.AssignLoader(managerSettings, OpenXrLoaderType, buildTargetGroup);
            if (!assigned && !managerSettings.activeLoaders.Any(loader => loader.GetType().FullName == OpenXrLoaderType))
                throw new InvalidOperationException($"Could not enable the OpenXR loader for {buildTargetGroup}.");

            EditorUtility.SetDirty(perBuildTarget);
            EditorUtility.SetDirty(managerSettings);
        }

        private static void EnableMetaXrFeatureSet(BuildTargetGroup buildTargetGroup)
        {
            var featureSet = OpenXRFeatureSetManager.GetFeatureSetWithId(buildTargetGroup, MetaXrFeatureSetId);
            if (featureSet == null)
                throw new InvalidOperationException($"Meta XR OpenXR feature set not found for {buildTargetGroup}.");

            featureSet.isEnabled = true;
            OpenXRFeatureSetManager.SetFeaturesFromEnabledFeatureSets(buildTargetGroup);
        }

        private static void ConfigureMetaProjectConfig()
        {
            var projectConfig = OVRProjectConfig.CachedProjectConfig;
            projectConfig.targetDeviceTypes = new() { OVRProjectConfig.DeviceType.Quest3, OVRProjectConfig.DeviceType.Quest3S };
            projectConfig.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Required;
            // IOBT arm logging (Tracking module): adds the BODY_TRACKING permission and the optional
            // body_tracking feature to the manifest. Supported, not Required: the app runs without it.
            projectConfig.bodyTrackingSupport = OVRProjectConfig.FeatureSupport.Supported;
            // Adds the com.oculus.experimental.enabled manifest feature that Meta XR Operator
            // needs on Horizon OS v205+. Turn this off before any store submission.
            projectConfig.experimentalFeaturesEnabled = true;
            OVRProjectConfig.CommitProjectConfig(projectConfig);
        }

        /// <summary>
        /// Same as the Project Setup Tool's "Fix All" button, minus the fixes in
        /// <see cref="ExcludedFixTaskUids"/>. Meta only exposes an unfiltered FixAllAsync,
        /// so the filtered internal entry point is reached by reflection; if a future SDK
        /// renames it, this throws instead of silently running the unfiltered version.
        /// </summary>
        private static Task StartFixAllExceptExcluded(BuildTargetGroup buildTargetGroup)
        {
            const BindingFlags InternalStatic = BindingFlags.NonPublic | BindingFlags.Static;
            var fixTasksAsync = typeof(OVRProjectSetup).GetMethod("FixTasksAsync", InternalStatic)
                ?? throw new MissingMethodException(nameof(OVRProjectSetup), "FixTasksAsync");
            var taskType = typeof(OVRProjectSetup).Assembly.GetType("OVRConfigurationTask")
                ?? throw new TypeLoadException("OVRConfigurationTask not found in the Meta XR Core SDK.");

            var filterType = typeof(Func<,>).MakeGenericType(
                typeof(IEnumerable<>).MakeGenericType(taskType), typeof(List<>).MakeGenericType(taskType));
            var filterMethod = typeof(QuestProjectSetup).GetMethod(nameof(ExcludeTasks), InternalStatic)!
                .MakeGenericMethod(taskType);
            var filter = Delegate.CreateDelegate(filterType, filterMethod);

            var logMessagesType = fixTasksAsync.GetParameters()[2].ParameterType;
            var logChangedFixes = Enum.Parse(logMessagesType, "Changed");
            return (Task)fixTasksAsync.Invoke(null, new[] { buildTargetGroup, filter, logChangedFixes });
        }

        private static List<TTask> ExcludeTasks<TTask>(IEnumerable<TTask> tasks)
        {
            return tasks.Where(task => !ExcludedFixTaskUids.Contains(((IIdentified)task).Id)).ToList();
        }

        private static void RunMetaProjectSetupToolThenExit()
        {
            var fixAll = StartFixAllExceptExcluded(BuildTargetGroup.Android);
            var startTime = EditorApplication.timeSinceStartup;

            void WaitForFixes()
            {
                var timedOut = EditorApplication.timeSinceStartup - startTime > SetupToolTimeoutSeconds;
                if (!fixAll.IsCompleted && !timedOut)
                    return;

                EditorApplication.update -= WaitForFixes;
                if (timedOut)
                    Debug.LogError("[QuestProjectSetup] Project Setup Tool did not finish in time.");
                else if (fixAll.IsFaulted)
                    Debug.LogException(fixAll.Exception);

                // The manifest "update" fix asks for confirmation, which batchmode cancels;
                // apply it silently so Assets/Plugins/Android/AndroidManifest.xml matches the config.
                OVRManifestPreprocessor.GenerateOrUpdateAndroidManifest(silentMode: true);

                // Fixes can touch the open scene (e.g. OVRManager settings); keep them.
                EditorSceneManager.SaveOpenScenes();
                AssetDatabase.SaveAssets();
                WriteSetupReportThenExit(timedOut || fixAll.IsFaulted ? 1 : 0);
            }

            EditorApplication.update += WaitForFixes;
        }

        private static void WriteSetupReportThenExit(int exitCode)
        {
            // Meta's own CLI hook: re-evaluates every rule (blocking) and writes a JSON
            // report to the path given by the "-reportFile" command-line argument.
            OVRProjectSetupCLI.GenerateProjectSetupReport();
            EditorApplication.Exit(exitCode);
        }

        private static void EnsureFolder(string assetFolderPath)
        {
            if (AssetDatabase.IsValidFolder(assetFolderPath))
                return;

            var parent = System.IO.Path.GetDirectoryName(assetFolderPath)!.Replace('\\', '/');
            var leaf = System.IO.Path.GetFileName(assetFolderPath);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
