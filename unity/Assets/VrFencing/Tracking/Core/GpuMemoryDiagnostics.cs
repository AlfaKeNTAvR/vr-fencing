using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace VrFencing.Tracking
{
    /// <summary>
    /// What the app holds on the GPU at one moment, as far as Unity can see it (PER-93: GPU memory roughly doubles on
    /// every sleep/wake). Logged right before a pause and a few seconds after a resume, never every second, because
    /// the object scans behind it are expensive. Comparing a "before-pause" line with the next "after-resume" line
    /// shows which count grew: cameras, render textures (and among them the XR eye textures), passthrough layers,
    /// overlays or XR displays. Memory that grows in the "GL mtrack" row of <c>dumpsys meminfo</c> but in none of these
    /// is held natively, outside Unity's objects (the OpenXR swapchains or the runtime itself).
    /// </summary>
    public readonly struct GpuMemorySnapshot
    {
        public GpuMemorySnapshot(
            int allCamerasCount,
            int cameraObjectCount,
            int enabledCameraCount,
            RenderTextureTotals renderTextures,
            RenderTextureTotals xrTextures,
            long textureMemoryCurrentBytes,
            long textureMemoryTotalBytes,
            long graphicsDriverBytes,
            int passthroughLayerCount,
            int overlayCount,
            int xrDisplayCount,
            int runningXrDisplayCount)
        {
            AllCamerasCount = allCamerasCount;
            CameraObjectCount = cameraObjectCount;
            EnabledCameraCount = enabledCameraCount;
            RenderTextures = renderTextures;
            XrTextures = xrTextures;
            TextureMemoryCurrentBytes = textureMemoryCurrentBytes;
            TextureMemoryTotalBytes = textureMemoryTotalBytes;
            GraphicsDriverBytes = graphicsDriverBytes;
            PassthroughLayerCount = passthroughLayerCount;
            OverlayCount = overlayCount;
            XrDisplayCount = xrDisplayCount;
            RunningXrDisplayCount = runningXrDisplayCount;
        }

        /// <summary>Unity's <c>Camera.allCamerasCount</c>: the cameras that are currently enabled.</summary>
        public int AllCamerasCount { get; }

        /// <summary>Every Camera component, including inactive ones; a growing number means cameras are duplicated.</summary>
        public int CameraObjectCount { get; }

        /// <summary>Camera components that are active and enabled.</summary>
        public int EnabledCameraCount { get; }

        /// <summary>Every live RenderTexture object, the XR eye textures included.</summary>
        public RenderTextureTotals RenderTextures { get; }

        /// <summary>The RenderTextures Unity names "XR Texture", which wrap the XR eye buffers.</summary>
        public RenderTextureTotals XrTextures { get; }

        /// <summary>Unity's <c>Texture.currentTextureMemory</c>.</summary>
        public long TextureMemoryCurrentBytes { get; }

        /// <summary>Unity's <c>Texture.totalTextureMemory</c>.</summary>
        public long TextureMemoryTotalBytes { get; }

        /// <summary>
        /// <c>Profiler.GetAllocatedMemoryForGraphicsDriver</c>: Unity's estimate of what the graphics driver holds for
        /// it. Zero outside Development builds.
        /// </summary>
        public long GraphicsDriverBytes { get; }

        public int PassthroughLayerCount { get; }

        /// <summary>OVROverlay components; each passthrough layer adds one of its own.</summary>
        public int OverlayCount { get; }

        public int XrDisplayCount { get; }

        public int RunningXrDisplayCount { get; }
    }

    /// <summary>
    /// A count of render textures and their memory: <see cref="EstimatedBytes"/> from their size and formats (see
    /// <see cref="RenderTextureMemory.EstimateBytes"/>), <see cref="ProfilerBytes"/> as Unity's profiler reports
    /// them (zero outside Development builds).
    /// </summary>
    public readonly struct RenderTextureTotals
    {
        public RenderTextureTotals(int count, long estimatedBytes, long profilerBytes)
        {
            Count = count;
            EstimatedBytes = estimatedBytes;
            ProfilerBytes = profilerBytes;
        }

        public int Count { get; }

        public long EstimatedBytes { get; }

        public long ProfilerBytes { get; }
    }

    /// <summary>One live render texture as the diagnostics see it: its name and estimated memory.</summary>
    public readonly struct RenderTextureEntry
    {
        public RenderTextureEntry(string name, long estimatedBytes, long profilerBytes)
        {
            Name = name ?? string.Empty;
            EstimatedBytes = estimatedBytes;
            ProfilerBytes = profilerBytes;
        }

        public string Name { get; }

        public long EstimatedBytes { get; }

        public long ProfilerBytes { get; }
    }

    /// <summary>Render textures that share a name once instance numbers are dropped, for example all "XR Texture [n]".</summary>
    public readonly struct RenderTextureGroup
    {
        public RenderTextureGroup(string name, int count, long estimatedBytes)
        {
            Name = name;
            Count = count;
            EstimatedBytes = estimatedBytes;
        }

        public string Name { get; }

        public int Count { get; }

        public long EstimatedBytes { get; }
    }

    /// <summary>Render texture memory estimates and summaries for the GPU diagnostics (PER-93).</summary>
    public static class RenderTextureMemory
    {
        /// <summary>The name prefix Unity gives the RenderTextures that wrap the XR eye buffers.</summary>
        public const string XrTextureNamePrefix = "XR Texture";

        private static readonly Regex InstanceSuffix = new Regex(@"\s*\[\d+\]$", RegexOptions.CultureInvariant);

        /// <summary>
        /// An approximate GPU size in bytes. A multisampled texture keeps every sample plus a single-sample resolve
        /// copy of the color; depth keeps every sample. Mip maps add a third to the color. Samples a memoryless
        /// attachment keeps only in tile memory should be passed as zero bytes per pixel or one sample by the caller.
        /// Compression, padding and lazy allocation on the Quest's Adreno GPU make the real number differ, so this is
        /// for comparing before and after, not for absolute totals.
        /// </summary>
        public static long EstimateBytes(
            int width,
            int height,
            int volumeDepth,
            int sampleCount,
            int colorBytesPerPixel,
            int depthBytesPerPixel,
            bool hasMipMaps)
        {
            if (width <= 0 || height <= 0)
            {
                return 0;
            }

            var pixels = (long)width * height * Math.Max(1, volumeDepth);
            var samples = Math.Max(1, sampleCount);
            var colorCopies = samples > 1 ? samples + 1 : 1;
            var colorBytes = pixels * Math.Max(0, colorBytesPerPixel) * colorCopies;
            if (hasMipMaps)
            {
                colorBytes += colorBytes / 3;
            }

            var depthBytes = pixels * Math.Max(0, depthBytesPerPixel) * samples;
            return colorBytes + depthBytes;
        }

        /// <summary>True for the RenderTextures that wrap the XR eye buffers.</summary>
        public static bool IsXrTexture(string name) =>
            name != null && name.StartsWith(XrTextureNamePrefix, StringComparison.Ordinal);

        /// <summary>The count and total memory of the given render textures.</summary>
        public static RenderTextureTotals Sum(IEnumerable<RenderTextureEntry> textures)
        {
            var list = (textures ?? Array.Empty<RenderTextureEntry>()).ToList();
            return new RenderTextureTotals(
                list.Count,
                list.Sum(texture => texture.EstimatedBytes),
                list.Sum(texture => texture.ProfilerBytes));
        }

        /// <summary>
        /// The <paramref name="maxGroups"/> largest groups of render textures by estimated memory, grouped by name
        /// with a trailing instance number such as " [7]" removed, largest first (ties by name). Unnamed textures
        /// group as "(unnamed)".
        /// </summary>
        public static IReadOnlyList<RenderTextureGroup> SummarizeByName(IEnumerable<RenderTextureEntry> textures, int maxGroups)
        {
            return (textures ?? Array.Empty<RenderTextureEntry>())
                .GroupBy(texture => NormalizeName(texture.Name))
                .Select(group => new RenderTextureGroup(group.Key, group.Count(), group.Sum(texture => texture.EstimatedBytes)))
                .OrderByDescending(group => group.EstimatedBytes)
                .ThenBy(group => group.Name, StringComparer.Ordinal)
                .Take(Math.Max(0, maxGroups))
                .ToList();
        }

        private static string NormalizeName(string name)
        {
            var trimmed = InstanceSuffix.Replace(name ?? string.Empty, string.Empty).Trim();
            return trimmed.Length == 0 ? "(unnamed)" : trimmed;
        }
    }

    /// <summary>
    /// When the next GPU snapshot is due (PER-93). The scans are too expensive for every heartbeat, so a snapshot is
    /// scheduled for a moment, such as a few seconds after a resume when the XR session has rebuilt its buffers, and
    /// taken once. Only one is pending at a time: scheduling another replaces it, and <see cref="Cancel"/> drops it,
    /// for example when the app pauses again before it was due.
    /// </summary>
    public sealed class GpuSnapshotSchedule
    {
        private string _pendingMoment;
        private double _dueSeconds;

        public bool HasPending => _pendingMoment != null;

        /// <summary>Schedules the snapshot named <paramref name="moment"/> for <paramref name="dueSeconds"/> of realtime.</summary>
        public void Schedule(string moment, double dueSeconds)
        {
            _pendingMoment = moment ?? throw new ArgumentNullException(nameof(moment));
            _dueSeconds = dueSeconds;
        }

        public void Cancel() => _pendingMoment = null;

        /// <summary>True once when the pending snapshot is due, with its moment name; it is then no longer pending.</summary>
        public bool TryTakeDue(double realtimeSeconds, out string moment)
        {
            moment = null;
            if (_pendingMoment == null || realtimeSeconds < _dueSeconds)
            {
                return false;
            }

            moment = _pendingMoment;
            _pendingMoment = null;
            return true;
        }
    }
}
