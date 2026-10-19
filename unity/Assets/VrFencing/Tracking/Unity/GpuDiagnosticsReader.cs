using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Profiling;
using UnityEngine.XR;

namespace VrFencing.Tracking
{
    /// <summary>
    /// Reads the Unity side of the GPU snapshot for the Core <see cref="GpuMemorySnapshot"/> (PER-93). Every call
    /// scans all loaded objects, so it is only used at a few moments around a pause and resume, never per frame.
    /// </summary>
    public static class GpuDiagnosticsReader
    {
        private static readonly List<XRDisplaySubsystem> XrDisplays = new List<XRDisplaySubsystem>();

        /// <summary>Every live RenderTexture object, the XR eye textures included.</summary>
        public static RenderTexture[] FindRenderTextures() => Resources.FindObjectsOfTypeAll<RenderTexture>();

        /// <summary>The live "XR Texture" render textures, which wrap the XR eye buffers.</summary>
        public static List<RenderTexture> FindXrTextures(IEnumerable<RenderTexture> renderTextures) =>
            renderTextures.Where(texture => texture != null && RenderTextureMemory.IsXrTexture(texture.name)).ToList();

        /// <summary>
        /// One render texture for a log line: name, instance ID, size with slices, MSAA and estimated memory, for
        /// example "XR Texture [12] id -4242 1680x1760x2 msaa 4 203.0 MB".
        /// </summary>
        public static string Describe(RenderTexture texture)
        {
            var megabytes = ToEntry(texture).EstimatedBytes / (1024.0 * 1024.0);
            return $"{texture.name} id {texture.GetInstanceID()} {texture.width}x{texture.height}x{texture.volumeDepth}"
                + $" msaa {texture.antiAliasing} {megabytes:0.0} MB";
        }

        /// <summary>The count and estimated memory of the given render textures.</summary>
        public static RenderTextureTotals Sum(IEnumerable<RenderTexture> renderTextures) =>
            RenderTextureMemory.Sum(renderTextures.Where(texture => texture != null).Select(ToEntry));

        /// <summary>Takes a snapshot from the given render textures (from <see cref="FindRenderTextures"/>).</summary>
        public static GpuMemorySnapshot Read(IReadOnlyList<RenderTexture> renderTextures, out IReadOnlyList<RenderTextureEntry> entries)
        {
            entries = renderTextures.Where(texture => texture != null).Select(ToEntry).ToList();
            var cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            SubsystemManager.GetSubsystems(XrDisplays);
            return new GpuMemorySnapshot(
                Camera.allCamerasCount,
                cameras.Length,
                cameras.Count(camera => camera.isActiveAndEnabled),
                RenderTextureMemory.Sum(entries),
                RenderTextureMemory.Sum(entries.Where(entry => RenderTextureMemory.IsXrTexture(entry.Name))),
                (long)Texture.currentTextureMemory,
                (long)Texture.totalTextureMemory,
                Profiler.GetAllocatedMemoryForGraphicsDriver(),
                UnityEngine.Object.FindObjectsByType<OVRPassthroughLayer>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                UnityEngine.Object.FindObjectsByType<OVROverlay>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                XrDisplays.Count,
                XrDisplays.Count(display => display.running));
        }

        /// <summary>
        /// Reads <see cref="GpuExperiments.FileName"/> from <paramref name="directory"/>; <see cref="GpuExperiments.Default"/>
        /// when it does not exist. A file that cannot be read is logged as an error and also gives the defaults, so a
        /// typo never stops the app; misspelled keys are ignored, which the startup log line of the active toggles makes
        /// visible. A key the file leaves out keeps its default (<see cref="GpuExperimentsFile"/>).
        /// </summary>
        public static GpuExperiments LoadExperiments(string directory)
        {
            var path = Path.Combine(directory, GpuExperiments.FileName);
            if (!File.Exists(path))
            {
                return GpuExperiments.Default;
            }

            var file = new GpuExperimentsFile();
            try
            {
                // Overwrite, not FromJson: only the keys in the file replace the field defaults.
                JsonUtility.FromJsonOverwrite(File.ReadAllText(path), file);
            }
            catch (Exception error) when (error is ArgumentException || error is IOException)
            {
                Debug.LogError($"[Life] gpu experiments file '{path}' is not valid, ignoring it: {error.Message}");
                return GpuExperiments.Default;
            }

            var urpMsaa = GpuExperiments.ParseUrpMsaa(file.urpMsaa, out var isMsaaValid);
            if (!isMsaaValid)
            {
                Debug.LogWarning($"[Life] gpu experiments: urpMsaa {file.urpMsaa} is not 1, 2, 4 or 8; keeping the asset's MSAA.");
            }

            return new GpuExperiments(
                file.passthroughOffAcrossPause,
                urpMsaa,
                file.unloadUnusedAssetsOnResume,
                file.recreateUrpPipelineOnResume,
                file.destroyStaleXrTexturesOnResume);
        }

        private static RenderTextureEntry ToEntry(RenderTexture texture)
        {
            var memoryless = texture.memorylessMode;
            var storedSamples = (memoryless & RenderTextureMemoryless.MSAA) != 0 ? 1 : texture.antiAliasing;
            var colorBytes = (memoryless & RenderTextureMemoryless.Color) != 0 || texture.graphicsFormat == GraphicsFormat.None
                ? 0
                : (int)GraphicsFormatUtility.GetBlockSize(texture.graphicsFormat);
            var depthBytes = (memoryless & RenderTextureMemoryless.Depth) != 0 || texture.depthStencilFormat == GraphicsFormat.None
                ? 0
                : (int)GraphicsFormatUtility.GetBlockSize(texture.depthStencilFormat);
            var estimatedBytes = RenderTextureMemory.EstimateBytes(
                texture.width,
                texture.height,
                texture.volumeDepth,
                storedSamples,
                colorBytes,
                depthBytes,
                texture.useMipMap);
            return new RenderTextureEntry(texture.name, estimatedBytes, Profiler.GetRuntimeMemorySizeLong(texture));
        }

        /// <summary>
        /// The JSON shape of the experiments file for <c>JsonUtility</c>; see <see cref="GpuExperiments"/>. The field
        /// values are the defaults for keys the file leaves out.
        /// </summary>
        [Serializable]
        public sealed class GpuExperimentsFile
        {
            public bool passthroughOffAcrossPause;
            public int urpMsaa;
            public bool unloadUnusedAssetsOnResume;
            public bool recreateUrpPipelineOnResume;
            public bool destroyStaleXrTexturesOnResume = StaleXrTextureCleanup.IsOnByDefault;
        }
    }
}
