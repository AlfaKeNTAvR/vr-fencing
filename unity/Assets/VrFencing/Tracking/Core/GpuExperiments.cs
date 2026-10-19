using System.Collections.Generic;

namespace VrFencing.Tracking
{
    /// <summary>
    /// Runtime experiment toggles for bisecting the sleep/wake GPU memory growth on the headset without a rebuild
    /// (PER-93). Read once at startup from the optional file <see cref="FileName"/> in <c>Application.persistentDataPath</c>
    /// (on the Quest <c>/sdcard/Android/data/com.alfakentavr.vrfencing/files/</c>, put there with <c>adb push</c>, then
    /// relaunch the app). No file means every toggle is off, which is the normal behavior. Every key is optional:
    /// <code>
    /// {
    ///   "passthroughOffAcrossPause": true,
    ///   "urpMsaa": 1,
    ///   "unloadUnusedAssetsOnResume": true,
    ///   "recreateUrpPipelineOnResume": true,
    ///   "destroyStaleXrTexturesOnResume": true
    /// }
    /// </code>
    /// <list type="bullet">
    /// <item><c>passthroughOffAcrossPause</c>: disable the OVRPassthroughLayer on pause (which destroys its OVROverlay)
    /// and enable it again on resume, so a passthrough layer leaked by the session restart shows up or goes away.</item>
    /// <item><c>urpMsaa</c>: the URP MSAA sample count (1, 2, 4 or 8), applied at startup; 0 or absent keeps the
    /// asset's 4x. Shows how much of the memory is multisampled buffers.</item>
    /// <item><c>unloadUnusedAssetsOnResume</c>: 5 s after a resume, call <c>Resources.UnloadUnusedAssets()</c> and
    /// <c>GC.Collect()</c>, so objects that are only unreferenced, not leaked natively, are freed.</item>
    /// <item><c>recreateUrpPipelineOnResume</c>: 5 s after a resume, switch to a copy of the active URP asset. Unity
    /// then disposes the old URP pipeline with all its camera targets and render graph textures and builds a new
    /// one, which is the only public way to release URP's camera targets.</item>
    /// <item><c>destroyStaleXrTexturesOnResume</c>: 5 s after a resume, if the resume created new "XR Texture" render
    /// textures, destroy the ones that existed before the pause. A workaround reported on Unity Discussions for this
    /// exact growth; unsafe if the XR provider still uses the old ones (a crash or black view then).</item>
    /// </list>
    /// The resume steps run after the "after-resume" snapshot, and an "after-cleanup" snapshot follows them, so one
    /// wake shows the state before and after the experiment. Restarting the XR display or loader is deliberately not a
    /// toggle: OVRPlugin shares the OpenXR session with Unity's loader, and tearing it down by hand around the Android
    /// pause is not supported by either SDK.
    /// </summary>
    public sealed class GpuExperiments
    {
        public const string FileName = "gpu-experiments.json";

        public GpuExperiments(
            bool passthroughOffAcrossPause,
            int? urpMsaaSampleCount,
            bool unloadUnusedAssetsOnResume,
            bool recreateUrpPipelineOnResume,
            bool destroyStaleXrTexturesOnResume)
        {
            PassthroughOffAcrossPause = passthroughOffAcrossPause;
            UrpMsaaSampleCount = urpMsaaSampleCount;
            UnloadUnusedAssetsOnResume = unloadUnusedAssetsOnResume;
            RecreateUrpPipelineOnResume = recreateUrpPipelineOnResume;
            DestroyStaleXrTexturesOnResume = destroyStaleXrTexturesOnResume;
        }

        /// <summary>Every toggle off: today's behavior, used when there is no experiments file.</summary>
        public static GpuExperiments None { get; } = new GpuExperiments(false, null, false, false, false);

        public bool PassthroughOffAcrossPause { get; }

        /// <summary>The URP MSAA sample count to apply at startup, or null to keep the asset's.</summary>
        public int? UrpMsaaSampleCount { get; }

        public bool UnloadUnusedAssetsOnResume { get; }

        public bool RecreateUrpPipelineOnResume { get; }

        public bool DestroyStaleXrTexturesOnResume { get; }

        /// <summary>True when any step is to run 5 s after a resume.</summary>
        public bool HasResumeSteps =>
            UnloadUnusedAssetsOnResume || RecreateUrpPipelineOnResume || DestroyStaleXrTexturesOnResume;

        /// <summary>
        /// The URP MSAA value from the file as a sample count: 1, 2, 4 or 8 is used as is; 0 means keep the asset's
        /// (null). Anything else is not a valid sample count and is also null, with <paramref name="isValid"/> false so
        /// the caller can warn.
        /// </summary>
        public static int? ParseUrpMsaa(int value, out bool isValid)
        {
            isValid = value == 0 || value == 1 || value == 2 || value == 4 || value == 8;
            return value == 0 || !isValid ? (int?)null : value;
        }

        /// <summary>The toggles that are on, for the startup log line, for example "passthroughOffAcrossPause urpMsaa 1", or "none".</summary>
        public string Describe()
        {
            var parts = new List<string>();
            if (PassthroughOffAcrossPause)
            {
                parts.Add("passthroughOffAcrossPause");
            }

            if (UrpMsaaSampleCount.HasValue)
            {
                parts.Add($"urpMsaa {UrpMsaaSampleCount.Value}");
            }

            if (UnloadUnusedAssetsOnResume)
            {
                parts.Add("unloadUnusedAssetsOnResume");
            }

            if (RecreateUrpPipelineOnResume)
            {
                parts.Add("recreateUrpPipelineOnResume");
            }

            if (DestroyStaleXrTexturesOnResume)
            {
                parts.Add("destroyStaleXrTexturesOnResume");
            }

            return parts.Count == 0 ? "none" : string.Join(" ", parts);
        }
    }
}
