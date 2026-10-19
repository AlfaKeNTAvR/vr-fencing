using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;

namespace VrFencing.Tracking
{
    /// <summary>
    /// Device-log diagnostics for headset sleep/wake (PER-71), all tagged <c>[Life]</c> (<c>adb logcat -s Unity</c>):
    /// a once-per-second heartbeat with the frame count, the runtime's focus and headset state (a gap means the frame
    /// loop stalled) and the eye buffer size (PER-93), every Unity and OVRManager lifecycle callback with its frame and
    /// time, and on resume the passthrough initialization state plus the passthrough layer's "resumed" event.
    /// Sits on the camera rig next to the passthrough layer.
    /// <para>
    /// PER-93 (GPU memory roughly doubles on every wake): a GPU snapshot (<see cref="GpuMemorySnapshot"/>) is logged
    /// 5 s after launch, right before each pause and 5 s after each resume, and the optional experiments file
    /// (<see cref="GpuExperiments"/>) can switch on steps around the pause and resume to bisect the cause on the
    /// headset without a rebuild. With no experiments file nothing here changes the app; the snapshots are logging only.
    /// </para>
    /// <para>
    /// The PER-93 workaround, <c>destroyStaleXrTexturesOnResume</c>: from each resume on, the XR eye textures are
    /// scanned every half second (<see cref="StaleXrTextureCleanup"/>), and once the resume's new set has rendered a
    /// few frames, the set from before the pause is released and destroyed. Every step is logged as
    /// <c>[Life] xr-cleanup</c>.
    /// </para>
    /// </summary>
    public sealed class LifecycleLogBehaviour : MonoBehaviour
    {
        /// <summary>Long enough after launch or resume for the XR session to have rebuilt its eye buffers.</summary>
        private const double SnapshotDelaySeconds = 5.0;

        /// <summary>Lets a recreated URP pipeline render a few frames, so its new targets exist when measured.</summary>
        private const double CleanupSnapshotDelaySeconds = 2.0;

        /// <summary>Render texture groups listed per snapshot; enough to show the eye buffers and URP's targets.</summary>
        private const int RenderTextureGroupsLogged = 6;

        private const double BytesPerMegabyte = 1024.0 * 1024.0;

        private const string LaunchMoment = "launch";
        private const string BeforePauseMoment = "before-pause";
        private const string AfterResumeMoment = "after-resume";
        private const string AfterCleanupMoment = "after-cleanup";

        [SerializeField]
        [Tooltip("The passthrough layer whose resume is logged. Found on this object or in the scene if empty.")]
        private OVRPassthroughLayer _passthroughLayer;

        private readonly LifecycleHeartbeat _heartbeat = new LifecycleHeartbeat();
        private readonly GpuSnapshotSchedule _snapshotSchedule = new GpuSnapshotSchedule();

        private readonly StaleXrTextureCleanup _staleXrTextureCleanup = new StaleXrTextureCleanup();

        private GpuExperiments _experiments = GpuExperiments.Default;

        /// <summary>Instance IDs of the "XR Texture" render textures alive at the last pause, for the stale cleanup.</summary>
        private HashSet<int> _xrTextureIdsBeforePause = new HashSet<int>();

        /// <summary>The XR textures the cleanup destroyed, kept for the report on the scan after.</summary>
        private int _destroyedXrTextureCount;

        /// <summary>Whether the passthrough experiment disabled the layer at the last pause, so only it re-enables it.</summary>
        private bool _isPassthroughDisabledForPause;

        private void Awake()
        {
            if (_passthroughLayer == null)
            {
                _passthroughLayer = GetComponent<OVRPassthroughLayer>();
            }

            if (_passthroughLayer == null)
            {
                _passthroughLayer = FindFirstObjectByType<OVRPassthroughLayer>();
            }

            _experiments = GpuDiagnosticsReader.LoadExperiments(Application.persistentDataPath);
            Debug.Log($"[Life] gpu experiments: {_experiments.Describe()}");
            ApplyUrpMsaaExperiment();
            _snapshotSchedule.Schedule(LaunchMoment, Time.realtimeSinceStartupAsDouble + SnapshotDelaySeconds);
        }

        private void OnEnable()
        {
            OVRManager.InputFocusLost += OnInputFocusLost;
            OVRManager.InputFocusAcquired += OnInputFocusAcquired;
            OVRManager.VrFocusLost += OnVrFocusLost;
            OVRManager.VrFocusAcquired += OnVrFocusAcquired;
            OVRManager.HMDUnmounted += OnHeadsetUnmounted;
            OVRManager.HMDMounted += OnHeadsetMounted;
            OVRManager.HMDLost += OnHmdLost;
            OVRManager.HMDAcquired += OnHmdAcquired;
            if (_passthroughLayer != null)
            {
                _passthroughLayer.passthroughLayerResumed.AddListener(OnPassthroughLayerResumed);
            }
            else
            {
                Debug.LogWarning("[Life] No passthrough layer found; its resume will not be logged.");
            }
        }

        private void Update()
        {
            var realtimeSeconds = Time.realtimeSinceStartupAsDouble;
            if (_heartbeat.TryBeat(realtimeSeconds))
            {
                Debug.Log(LifecycleHeartbeat.Format(
                    Time.frameCount, realtimeSeconds, RuntimeReadinessReader.Read(), ReadEyeBuffer()));
            }

            if (_staleXrTextureCleanup.IsScanDue(realtimeSeconds))
            {
                ScanForStaleXrTextures(realtimeSeconds);
            }

            if (_snapshotSchedule.TryTakeDue(realtimeSeconds, out var moment))
            {
                LogGpuSnapshot(moment);
                if (moment == AfterResumeMoment && _experiments.HasResumeSteps)
                {
                    RunResumeExperiments();
                    _snapshotSchedule.Schedule(AfterCleanupMoment, realtimeSeconds + CleanupSnapshotDelaySeconds);
                }
            }
        }

        private void OnDisable()
        {
            OVRManager.InputFocusLost -= OnInputFocusLost;
            OVRManager.InputFocusAcquired -= OnInputFocusAcquired;
            OVRManager.VrFocusLost -= OnVrFocusLost;
            OVRManager.VrFocusAcquired -= OnVrFocusAcquired;
            OVRManager.HMDUnmounted -= OnHeadsetUnmounted;
            OVRManager.HMDMounted -= OnHeadsetMounted;
            OVRManager.HMDLost -= OnHmdLost;
            OVRManager.HMDAcquired -= OnHmdAcquired;
            if (_passthroughLayer != null)
            {
                _passthroughLayer.passthroughLayerResumed.RemoveListener(OnPassthroughLayerResumed);
            }
        }

        private void OnApplicationPause(bool isPaused)
        {
            LogEvent(isPaused ? "OnApplicationPause(true)" : "OnApplicationPause(false)");
            if (isPaused)
            {
                _snapshotSchedule.Cancel();
                CancelStaleXrTextureWatch();
                var renderTextures = LogGpuSnapshot(BeforePauseMoment);
                _xrTextureIdsBeforePause = new HashSet<int>(GpuDiagnosticsReader.FindXrTextures(renderTextures)
                    .Select(texture => texture.GetInstanceID()));
                DisablePassthroughForPause();
            }
            else
            {
                ReenablePassthroughAfterPause();
                LogPassthroughState();
                _snapshotSchedule.Schedule(AfterResumeMoment, Time.realtimeSinceStartupAsDouble + SnapshotDelaySeconds);
                ArmStaleXrTextureWatch();
            }
        }

        private void OnApplicationFocus(bool hasFocus) => LogEvent($"OnApplicationFocus({hasFocus.ToString().ToLowerInvariant()})");

        private void OnInputFocusLost() => LogEvent("OVRManager.InputFocusLost");

        private void OnInputFocusAcquired() => LogEvent("OVRManager.InputFocusAcquired");

        private void OnVrFocusLost() => LogEvent("OVRManager.VrFocusLost");

        private void OnVrFocusAcquired() => LogEvent("OVRManager.VrFocusAcquired");

        private void OnHeadsetUnmounted() => LogEvent("OVRManager.HMDUnmounted");

        private void OnHeadsetMounted() => LogEvent("OVRManager.HMDMounted");

        private void OnHmdLost() => LogEvent("OVRManager.HMDLost");

        private void OnHmdAcquired() => LogEvent("OVRManager.HMDAcquired");

        private void OnPassthroughLayerResumed(OVRPassthroughLayer layer) => LogEvent("passthroughLayerResumed");

        private static void LogEvent(string eventName)
        {
            Debug.Log($"[Life] {eventName} at frame {Time.frameCount}, t {Time.realtimeSinceStartupAsDouble:0.00} s.");
        }

        /// <summary>
        /// Logs the GPU snapshot and its largest render texture groups (two lines), and returns the render textures it
        /// found, so the caller does not scan again.
        /// </summary>
        private static RenderTexture[] LogGpuSnapshot(string moment)
        {
            var renderTextures = GpuDiagnosticsReader.FindRenderTextures();
            var snapshot = GpuDiagnosticsReader.Read(renderTextures, out var entries);
            Debug.Log(LifecycleHeartbeat.FormatGpuSnapshot(moment, Time.frameCount, Time.realtimeSinceStartupAsDouble, snapshot));
            Debug.Log(LifecycleHeartbeat.FormatRenderTextureGroups(
                moment, RenderTextureMemory.SummarizeByName(entries, RenderTextureGroupsLogged)));
            return renderTextures;
        }

        /// <summary>
        /// The eye texture size and scales as Unity's XR settings report them, plus the active URP asset's render scale
        /// and MSAA (PER-93). OVRManager's Dynamic Resolution, when on, raises both scales to its maximum at startup.
        /// </summary>
        private static EyeBufferState ReadEyeBuffer()
        {
            var urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            return new EyeBufferState(
                XRSettings.eyeTextureWidth,
                XRSettings.eyeTextureHeight,
                XRSettings.eyeTextureResolutionScale,
                XRSettings.renderViewportScale,
                urpAsset != null ? urpAsset.renderScale : (float?)null,
                urpAsset != null ? urpAsset.msaaSampleCount : (int?)null);
        }

        private void LogPassthroughState()
        {
            var initState = OVRManager.IsInsightPassthroughInitialized() ? "initialized"
                : OVRManager.IsInsightPassthroughInitPending() ? "pending"
                : OVRManager.HasInsightPassthroughInitFailed() ? "failed"
                : "not initialized";
            var layerState = _passthroughLayer == null ? "missing"
                : _passthroughLayer.isActiveAndEnabled ? "enabled"
                : "disabled";
            Debug.Log($"[Life] On resume: passthrough {initState}, passthrough layer {layerState}.");
        }

        /// <summary>
        /// Experiment <c>urpMsaa</c>: sets the active URP asset's MSAA at startup. The change lives only in the running
        /// player; the asset on disk is not touched.
        /// </summary>
        private void ApplyUrpMsaaExperiment()
        {
            if (!_experiments.UrpMsaaSampleCount.HasValue)
            {
                return;
            }

            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urpAsset))
            {
                Debug.LogWarning("[Life] gpu experiment urpMsaa: no URP asset is active; nothing changed.");
                return;
            }

            urpAsset.msaaSampleCount = _experiments.UrpMsaaSampleCount.Value;
            Debug.Log($"[Life] gpu experiment urpMsaa: URP MSAA set to {urpAsset.msaaSampleCount}.");
        }

        /// <summary>
        /// Experiment <c>passthroughOffAcrossPause</c>: disabling the layer destroys its OVROverlay (Meta SDK
        /// OVRPassthroughLayer.OnDisable), so the session restart has no passthrough layer to rebuild.
        /// </summary>
        private void DisablePassthroughForPause()
        {
            if (!_experiments.PassthroughOffAcrossPause || _passthroughLayer == null || !_passthroughLayer.enabled)
            {
                return;
            }

            _passthroughLayer.enabled = false;
            _isPassthroughDisabledForPause = true;
            Debug.Log("[Life] gpu experiment passthroughOffAcrossPause: passthrough layer disabled for the pause.");
        }

        private void ReenablePassthroughAfterPause()
        {
            if (!_isPassthroughDisabledForPause)
            {
                return;
            }

            _isPassthroughDisabledForPause = false;
            _passthroughLayer.enabled = true;
            Debug.Log("[Life] gpu experiment passthroughOffAcrossPause: passthrough layer enabled again.");
        }

        /// <summary>The experiments that run once the XR session is back after a resume, cheapest first.</summary>
        private void RunResumeExperiments()
        {
            if (_experiments.UnloadUnusedAssetsOnResume)
            {
                Resources.UnloadUnusedAssets();
                GC.Collect();
                Debug.Log("[Life] gpu experiment unloadUnusedAssetsOnResume: Resources.UnloadUnusedAssets and GC.Collect called.");
            }

            if (_experiments.RecreateUrpPipelineOnResume)
            {
                RecreateUrpPipeline();
            }
        }

        /// <summary>
        /// Starts watching for the resume's new XR texture set (<c>destroyStaleXrTexturesOnResume</c>), with the set
        /// recorded at the pause as the one to destroy.
        /// </summary>
        private void ArmStaleXrTextureWatch()
        {
            if (!_experiments.DestroyStaleXrTexturesOnResume)
            {
                return;
            }

            _destroyedXrTextureCount = 0;
            _staleXrTextureCleanup.Arm(_xrTextureIdsBeforePause, Time.realtimeSinceStartupAsDouble);
            Debug.Log($"[Life] xr-cleanup watching: {_xrTextureIdsBeforePause.Count} XR textures from before the pause"
                + $" (ids {string.Join(" ", _xrTextureIdsBeforePause.OrderBy(id => id))}), frame {Time.frameCount}.");
        }

        /// <summary>A pause during the watch ends it; the pause then records every live XR texture again.</summary>
        private void CancelStaleXrTextureWatch()
        {
            if (!_staleXrTextureCleanup.IsArmed)
            {
                return;
            }

            _staleXrTextureCleanup.Disarm();
            Debug.Log($"[Life] xr-cleanup cancelled: paused again before the cleanup finished, frame {Time.frameCount}.");
        }

        /// <summary>
        /// One scan of the watch: finds the live XR textures and does what <see cref="StaleXrTextureCleanup"/> decides.
        /// A destroy releases each stale texture's GPU memory and destroys the object; the next scan logs what is left.
        /// </summary>
        private void ScanForStaleXrTextures(double realtimeSeconds)
        {
            var xrTextures = GpuDiagnosticsReader.FindXrTextures(GpuDiagnosticsReader.FindRenderTextures());
            var verdict = _staleXrTextureCleanup.Evaluate(
                xrTextures.Select(texture => texture.GetInstanceID()).ToList(), realtimeSeconds, Time.frameCount);
            if (verdict.Action == StaleXrTextureAction.Wait)
            {
                return;
            }

            var totals = GpuDiagnosticsReader.Sum(xrTextures);
            var counts = $"xrTex {totals.Count} est {totals.EstimatedBytes / BytesPerMegabyte:0.0} MB"
                + $" ({verdict.StaleIds.Count} from before the pause, {verdict.NewCount} new), frame {Time.frameCount},"
                + $" t {realtimeSeconds:0.00} s";
            switch (verdict.Action)
            {
                case StaleXrTextureAction.Destroy:
                    DestroyStaleXrTextures(xrTextures, verdict.StaleIds, counts);
                    break;
                case StaleXrTextureAction.ReportAfterDestroy:
                    Debug.Log($"[Life] xr-cleanup after: destroyed {_destroyedXrTextureCount}, now {counts}.");
                    break;
                case StaleXrTextureAction.NothingStale:
                    Debug.Log($"[Life] xr-cleanup done: no XR texture from before the pause is alive; {counts}.");
                    break;
                case StaleXrTextureAction.Expired:
                    Debug.Log($"[Life] xr-cleanup gave up after {StaleXrTextureCleanup.WatchWindowSeconds:0} s:"
                        + $" no new set rendered, nothing destroyed; {counts}.");
                    break;
                default:
                    Debug.LogError($"[Life] xr-cleanup: unhandled action {verdict.Action}; nothing destroyed.");
                    _staleXrTextureCleanup.Disarm();
                    break;
            }
        }

        /// <summary>
        /// Releases the GPU memory of the XR textures from before the pause and destroys their objects, logging each
        /// one and the counts before. Only textures still alive in this scan are touched.
        /// </summary>
        private void DestroyStaleXrTextures(IReadOnlyList<RenderTexture> xrTextures, IReadOnlyList<int> staleIds, string counts)
        {
            var staleIdSet = new HashSet<int>(staleIds);
            var stale = xrTextures.Where(texture => staleIdSet.Contains(texture.GetInstanceID())).ToList();
            Debug.Log($"[Life] xr-cleanup before: {counts}. Destroying {stale.Count}: "
                + string.Join("; ", stale.Select(GpuDiagnosticsReader.Describe)) + ".");
            foreach (var texture in stale)
            {
                texture.Release();
                Destroy(texture);
            }

            _destroyedXrTextureCount = stale.Count;
        }

        /// <summary>
        /// Experiment <c>recreateUrpPipelineOnResume</c>: switches to a copy of the active URP asset. Unity disposes the
        /// old pipeline (its renderers, camera targets and render graph texture pool) and creates a new one on the
        /// next frame. Assigned where the project sets it: the quality level's pipeline, or the default one.
        /// </summary>
        private void RecreateUrpPipeline()
        {
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urpAsset))
            {
                Debug.LogWarning("[Life] gpu experiment recreateUrpPipelineOnResume: no URP asset is active; nothing changed.");
                return;
            }

            // The previous copy is not destroyed: the old pipeline may still read it while Unity disposes it, and
            // an asset copy holds no GPU memory.
            var pipelineCopy = Instantiate(urpAsset);
            pipelineCopy.name = urpAsset.name;
            if (QualitySettings.renderPipeline != null)
            {
                QualitySettings.renderPipeline = pipelineCopy;
            }
            else
            {
                GraphicsSettings.defaultRenderPipeline = pipelineCopy;
            }

            Debug.Log("[Life] gpu experiment recreateUrpPipelineOnResume: URP pipeline recreated from a copy of its asset.");
        }
    }
}
