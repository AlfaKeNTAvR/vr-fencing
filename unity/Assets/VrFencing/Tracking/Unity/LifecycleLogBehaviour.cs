using UnityEngine;

namespace VrFencing.Tracking
{
    /// <summary>
    /// Device-log diagnostics for headset sleep/wake (PER-71), all tagged <c>[Life]</c> (<c>adb logcat -s Unity</c>):
    /// a once-per-second heartbeat with the frame count and the runtime's focus and headset state (a gap means the frame
    /// loop stalled), every Unity and OVRManager lifecycle callback with its frame and time, and on resume the passthrough
    /// initialization state plus the passthrough layer's "resumed" event. Logging only; nothing depends on it.
    /// Sits on the camera rig next to the passthrough layer.
    /// </summary>
    public sealed class LifecycleLogBehaviour : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The passthrough layer whose resume is logged. Found on this object or in the scene if empty.")]
        private OVRPassthroughLayer _passthroughLayer;

        private readonly LifecycleHeartbeat _heartbeat = new LifecycleHeartbeat();

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
                Debug.Log(LifecycleHeartbeat.Format(Time.frameCount, realtimeSeconds, RuntimeReadinessReader.Read()));
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
            if (!isPaused)
            {
                LogPassthroughState();
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
    }
}
