using System;
using System.Collections.Generic;

namespace VrFencing.Tracking
{
    /// <summary>An app or headset lifecycle change, reported by a Unity or OVRManager callback.</summary>
    public enum LifecycleEvent
    {
        AppPaused,
        AppResumed,
        FocusLost,
        FocusRegained,
        HeadsetRemoved,
        HeadsetPutOn,
    }

    /// <summary>What the scene should do to body tracking now.</summary>
    public enum BodyTrackingAction
    {
        None,
        Start,
        Stop,
    }

    /// <summary>The action to take and a short reason for the device log (for example "app paused").</summary>
    public readonly struct BodyTrackingDecision
    {
        public BodyTrackingDecision(BodyTrackingAction action, string reason)
        {
            Action = action;
            Reason = reason;
        }

        public BodyTrackingAction Action { get; }

        public string Reason { get; }
    }

    /// <summary>
    /// The XR runtime's state, read once per frame (OVRManager / OVRPlugin): the app has VR focus (its frames are shown),
    /// input focus (no system menu or overlay on top), the headset is connected, and the user is wearing it.
    /// </summary>
    public readonly struct RuntimeReadiness
    {
        public RuntimeReadiness(bool hasVrFocus, bool hasInputFocus, bool isHmdPresent, bool isUserPresent)
        {
            HasVrFocus = hasVrFocus;
            HasInputFocus = hasInputFocus;
            IsHmdPresent = isHmdPresent;
            IsUserPresent = isUserPresent;
        }

        public bool HasVrFocus { get; }

        public bool HasInputFocus { get; }

        public bool IsHmdPresent { get; }

        public bool IsUserPresent { get; }

        public bool IsReady => HasVrFocus && HasInputFocus && IsHmdPresent && IsUserPresent;
    }

    /// <summary>
    /// Decides when Inside-Out Body Tracking runs (PER-43, PER-71). It runs while the app is active, and stops at once when
    /// the app is paused (headset asleep), loses focus (system menu, lock screen), or the headset comes off the head.
    /// <para>
    /// Restarting is deferred (PER-71): starting body tracking from inside the pause and focus callbacks, while the lock
    /// screen and its interstitial were still up, left a black frozen view after a sleep/wake. So lifecycle events only
    /// stop tracking and restart the settle window; tracking starts from the frame loop (<see cref="Tick"/>) once the app
    /// is not paused and the runtime has reported itself ready (<see cref="RuntimeReadiness"/>) continuously for the
    /// settle time since the last lifecycle event.
    /// </para>
    /// <para>
    /// Each condition has one source of truth: "paused" comes only from the app pause event, everything else only from
    /// the runtime state read each frame. Focus and headset events are notifications: they stop tracking immediately and
    /// restart the settle window, but never hold state themselves, so two callbacks reporting the same focus change
    /// cannot disagree.
    /// </para>
    /// </summary>
    public sealed class BodyTrackingLifecycle
    {
        /// <summary>How long the runtime must stay ready, with no lifecycle event, before tracking restarts.</summary>
        public const double DefaultSettleSeconds = 1.5;

        private const string LaunchReason = "launch";

        private readonly double _settleSeconds;
        private bool _isAppPaused;
        private RuntimeReadiness _lastReadiness;
        private bool _hasReadiness;
        private double? _readySinceSeconds;
        private bool _isSettled;
        private bool _isStartIssued;
        private string _lastEventReason = LaunchReason;

        public BodyTrackingLifecycle(double settleSeconds = DefaultSettleSeconds)
        {
            _settleSeconds = settleSeconds;
        }

        /// <summary>
        /// True once the app is not paused and the runtime has been ready for the settle time since the last lifecycle
        /// event. A fresh lifecycle is not settled: it waits for its first ready frames like a wake does.
        /// </summary>
        public bool ShouldRun => !_isAppPaused && _isSettled;

        /// <summary>
        /// Records <paramref name="lifecycleEvent"/> from a Unity or OVRManager callback. Returns Stop when the event takes
        /// tracking away and it is running; never Start (restarts come from <see cref="Tick"/>). Any event, losing or
        /// regaining, restarts the settle window.
        /// </summary>
        public BodyTrackingDecision RecordEvent(LifecycleEvent lifecycleEvent, bool isRunning)
        {
            if (lifecycleEvent == LifecycleEvent.AppPaused)
            {
                _isAppPaused = true;
            }
            else if (lifecycleEvent == LifecycleEvent.AppResumed)
            {
                _isAppPaused = false;
            }

            var reason = DescribeEvent(lifecycleEvent);
            _lastEventReason = reason;
            ResetSettleWindow();
            return IsLossEvent(lifecycleEvent) && isRunning
                ? new BodyTrackingDecision(BodyTrackingAction.Stop, reason)
                : new BodyTrackingDecision(BodyTrackingAction.None, reason);
        }

        /// <summary>
        /// Called once per frame with the runtime's state and the frame time in seconds. Returns Stop when tracking runs
        /// but the app is paused or the runtime is not ready, and Start once per settle window when the runtime has been
        /// ready for the settle time and tracking is not running. A start that fails (for example no permission yet) is
        /// not retried every frame: the next window, after the next lifecycle event or readiness drop, retries it.
        /// </summary>
        public BodyTrackingDecision Tick(double nowSeconds, RuntimeReadiness readiness, bool isRunning)
        {
            _lastReadiness = readiness;
            _hasReadiness = true;
            if (_isAppPaused || !readiness.IsReady)
            {
                ResetSettleWindow();
                return isRunning
                    ? new BodyTrackingDecision(BodyTrackingAction.Stop, $"not ready: {DescribeHolds()}")
                    : new BodyTrackingDecision(BodyTrackingAction.None, DescribeHolds());
            }

            _readySinceSeconds ??= nowSeconds;
            _isSettled = nowSeconds - _readySinceSeconds.Value >= _settleSeconds;
            if (!_isSettled || isRunning || _isStartIssued)
            {
                return new BodyTrackingDecision(BodyTrackingAction.None, _lastEventReason);
            }

            _isStartIssued = true;
            return new BodyTrackingDecision(
                BodyTrackingAction.Start,
                FormattableString.Invariant($"runtime ready for {_settleSeconds:0.0} s since {_lastEventReason}"));
        }

        /// <summary>The conditions holding body tracking off, for the device log ("none" when nothing does).</summary>
        public string DescribeHolds()
        {
            var holds = new List<string>();
            if (_isAppPaused)
            {
                holds.Add("app paused");
            }

            if (!_hasReadiness)
            {
                holds.Add("runtime state not read yet");
            }
            else
            {
                AddHoldIfFalse(holds, _lastReadiness.HasVrFocus, "no vr focus");
                AddHoldIfFalse(holds, _lastReadiness.HasInputFocus, "no input focus");
                AddHoldIfFalse(holds, _lastReadiness.IsHmdPresent, "no hmd");
                AddHoldIfFalse(holds, _lastReadiness.IsUserPresent, "headset off");
            }

            if (holds.Count == 0 && !_isSettled)
            {
                holds.Add(FormattableString.Invariant($"settling ({_settleSeconds:0.0} s)"));
            }

            return holds.Count == 0 ? "none" : string.Join(", ", holds);
        }

        private void ResetSettleWindow()
        {
            _readySinceSeconds = null;
            _isSettled = false;
            _isStartIssued = false;
        }

        private static void AddHoldIfFalse(List<string> holds, bool condition, string hold)
        {
            if (!condition)
            {
                holds.Add(hold);
            }
        }

        private static bool IsLossEvent(LifecycleEvent lifecycleEvent)
        {
            return lifecycleEvent == LifecycleEvent.AppPaused
                || lifecycleEvent == LifecycleEvent.FocusLost
                || lifecycleEvent == LifecycleEvent.HeadsetRemoved;
        }

        private static string DescribeEvent(LifecycleEvent lifecycleEvent)
        {
            switch (lifecycleEvent)
            {
                case LifecycleEvent.AppPaused:
                    return "app paused";
                case LifecycleEvent.AppResumed:
                    return "app resumed";
                case LifecycleEvent.FocusLost:
                    return "focus lost";
                case LifecycleEvent.FocusRegained:
                    return "focus regained";
                case LifecycleEvent.HeadsetRemoved:
                    return "headset removed";
                case LifecycleEvent.HeadsetPutOn:
                    return "headset put on";
                default:
                    return lifecycleEvent.ToString();
            }
        }
    }
}
