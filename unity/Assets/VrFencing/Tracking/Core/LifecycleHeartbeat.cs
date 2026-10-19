using System.Globalization;

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
