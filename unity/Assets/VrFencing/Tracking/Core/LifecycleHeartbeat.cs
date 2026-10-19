using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace VrFencing.Tracking
{
    /// <summary>
    /// The once-per-interval <c>[Life]</c> heartbeat line in the device log (PER-71). A gap in the heartbeat is a frozen
    /// frame loop; its fields show what the XR runtime reported just before. Times are realtime seconds since launch, so
    /// they keep counting while the app is paused.
    /// </summary>
    public sealed class LifecycleHeartbeat
    {
        public const double DefaultIntervalSeconds = 1.0;

        private readonly double _intervalSeconds;
        private double? _lastBeatSeconds;

        public LifecycleHeartbeat(double intervalSeconds = DefaultIntervalSeconds)
        {
            _intervalSeconds = intervalSeconds;
        }

        /// <summary>True on the first call and then whenever the interval has passed since the last beat (which it records).</summary>
        public bool TryBeat(double realtimeSeconds)
        {
            if (_lastBeatSeconds.HasValue && realtimeSeconds - _lastBeatSeconds.Value < _intervalSeconds)
            {
                return false;
            }

            _lastBeatSeconds = realtimeSeconds;
            return true;
        }

        /// <summary>
        /// The heartbeat line, for example
        /// "[Life] frame 1234 t 56.78 s vrFocus 1 inputFocus 1 hmd 1 user 1 eye 2064x2208 scale 1.00 viewport 1.00
        /// urp 1.00 msaa 4" (one line).
        /// The eye buffer fields (PER-93) show in the device log how big the eye textures really are, since one sleep/wake
        /// was measured to add about 3.2 GB of GPU memory. "urp none" means no URP asset was active.
        /// </summary>
        public static string Format(
            long frameCount, double realtimeSeconds, RuntimeReadiness readiness, EyeBufferState eyeBuffer)
        {
            var urp = eyeBuffer.UrpRenderScale.HasValue && eyeBuffer.UrpMsaaSampleCount.HasValue
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "urp {0:0.00} msaa {1}",
                    eyeBuffer.UrpRenderScale.Value,
                    eyeBuffer.UrpMsaaSampleCount.Value)
                : "urp none";
            return string.Format(
                CultureInfo.InvariantCulture,
                "[Life] frame {0} t {1:0.00} s vrFocus {2} inputFocus {3} hmd {4} user {5} eye {6}x{7} scale {8:0.00} viewport {9:0.00} {10}",
                frameCount,
                realtimeSeconds,
                ToFlag(readiness.HasVrFocus),
                ToFlag(readiness.HasInputFocus),
                ToFlag(readiness.IsHmdPresent),
                ToFlag(readiness.IsUserPresent),
                eyeBuffer.EyeTextureWidth,
                eyeBuffer.EyeTextureHeight,
                eyeBuffer.EyeTextureResolutionScale,
                eyeBuffer.RenderViewportScale,
                urp);
        }

        /// <summary>
        /// The GPU snapshot line (PER-93), for example
        /// "[Life] gpu after-resume frame 900 t 75.20 s cams 1 objects 2 enabled 1 rt 14 est 812.5 MB prof 640.0 MB
        /// xrTex 3 est 600.0 MB prof 500.0 MB texCur 700.0 MB texTotal 900.0 MB driver 1200.0 MB ptLayers 1 overlays 1
        /// xrDisplays 1 running 1" (one line). <paramref name="moment"/> names when it was taken: "launch",
        /// "before-pause", "after-resume" or "after-cleanup". Memory is in MiB.
        /// </summary>
        public static string FormatGpuSnapshot(string moment, long frameCount, double realtimeSeconds, GpuMemorySnapshot snapshot)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "[Life] gpu {0} frame {1} t {2:0.00} s cams {3} objects {4} enabled {5} rt {6} xrTex {7}"
                + " texCur {8:0.0} MB texTotal {9:0.0} MB driver {10:0.0} MB ptLayers {11} overlays {12} xrDisplays {13} running {14}",
                moment,
                frameCount,
                realtimeSeconds,
                snapshot.AllCamerasCount,
                snapshot.CameraObjectCount,
                snapshot.EnabledCameraCount,
                FormatTotals(snapshot.RenderTextures),
                FormatTotals(snapshot.XrTextures),
                ToMebibytes(snapshot.TextureMemoryCurrentBytes),
                ToMebibytes(snapshot.TextureMemoryTotalBytes),
                ToMebibytes(snapshot.GraphicsDriverBytes),
                snapshot.PassthroughLayerCount,
                snapshot.OverlayCount,
                snapshot.XrDisplayCount,
                snapshot.RunningXrDisplayCount);
        }

        /// <summary>
        /// The largest render texture groups at a snapshot (PER-93), for example
        /// "[Life] gpu after-resume rt-top XR Texture x6 1200.0 MB; _CameraTargetAttachmentA x1 50.0 MB", so a doubled
        /// group is visible by name. "rt-top none" when there are no render textures.
        /// </summary>
        public static string FormatRenderTextureGroups(string moment, IReadOnlyList<RenderTextureGroup> groups)
        {
            var body = groups == null || groups.Count == 0
                ? "none"
                : string.Join("; ", groups.Select(group => string.Format(
                    CultureInfo.InvariantCulture, "{0} x{1} {2:0.0} MB", group.Name, group.Count, ToMebibytes(group.EstimatedBytes))));
            return $"[Life] gpu {moment} rt-top {body}";
        }

        private static string FormatTotals(RenderTextureTotals totals) => string.Format(
            CultureInfo.InvariantCulture,
            "{0} est {1:0.0} MB prof {2:0.0} MB",
            totals.Count,
            ToMebibytes(totals.EstimatedBytes),
            ToMebibytes(totals.ProfilerBytes));

        private static double ToMebibytes(long bytes) => bytes / (1024.0 * 1024.0);

        private static int ToFlag(bool value) => value ? 1 : 0;
    }

    /// <summary>
    /// The eye buffer size as Unity's XR settings and the active URP asset report it (PER-93). The eye texture is
    /// allocated at its full size times <see cref="EyeTextureResolutionScale"/>; <see cref="RenderViewportScale"/> only
    /// shrinks the part drawn each frame, so memory follows the texture size, not the viewport. URP pushes its render
    /// scale to the XR eye textures, and its MSAA multiplies their memory. The URP fields are null when no URP asset is
    /// active.
    /// </summary>
    public readonly struct EyeBufferState
    {
        public EyeBufferState(
            int eyeTextureWidth,
            int eyeTextureHeight,
            float eyeTextureResolutionScale,
            float renderViewportScale,
            float? urpRenderScale,
            int? urpMsaaSampleCount)
        {
            EyeTextureWidth = eyeTextureWidth;
            EyeTextureHeight = eyeTextureHeight;
            EyeTextureResolutionScale = eyeTextureResolutionScale;
            RenderViewportScale = renderViewportScale;
            UrpRenderScale = urpRenderScale;
            UrpMsaaSampleCount = urpMsaaSampleCount;
        }

        public int EyeTextureWidth { get; }

        public int EyeTextureHeight { get; }

        public float EyeTextureResolutionScale { get; }

        public float RenderViewportScale { get; }

        public float? UrpRenderScale { get; }

        public int? UrpMsaaSampleCount { get; }
    }
}
