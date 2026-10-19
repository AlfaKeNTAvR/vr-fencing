using System.Collections.Generic;

namespace VrFencing.Tracking
{
    /// <summary>An app or headset lifecycle change that decides whether body tracking may run.</summary>
    public enum LifecycleEvent
    {
        AppPaused,
        AppResumed,
        FocusLost,
        FocusRegained,
        HeadsetRemoved,
        HeadsetPutOn,
    }

    /// <summary>What the scene should do to body tracking after a <see cref="LifecycleEvent"/>.</summary>
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
    /// Decides when Inside-Out Body Tracking runs (PER-43). It runs all the time the app is active, and stops while the
    /// app is paused (the headset sleeping, the app in the background), while it has lost input focus (the system menu
    /// or another overlay is up), or while the headset is off the head. Each condition is tracked on its own, so tracking
    /// restarts only once all three have cleared. Repeated or out-of-order events are harmless: the decision only
    /// compares what should run with what is running.
    /// </summary>
    public sealed class BodyTrackingLifecycle
    {
        private bool _isAppPaused;
        private bool _hasLostFocus;
        private bool _isHeadsetOff;

        /// <summary>True when nothing is holding body tracking off. A fresh lifecycle starts active.</summary>
        public bool ShouldRun => !_isAppPaused && !_hasLostFocus && !_isHeadsetOff;

        /// <summary>
        /// Records <paramref name="lifecycleEvent"/> and returns whether to start or stop body tracking, given whether it
        /// is running now. Start is returned whenever it should run and is not running, so a start that failed earlier
        /// (for example no permission yet) is retried on the next event.
        /// </summary>
        public BodyTrackingDecision Apply(LifecycleEvent lifecycleEvent, bool isRunning)
        {
            switch (lifecycleEvent)
            {
                case LifecycleEvent.AppPaused:
                    _isAppPaused = true;
                    break;
                case LifecycleEvent.AppResumed:
                    _isAppPaused = false;
                    break;
                case LifecycleEvent.FocusLost:
                    _hasLostFocus = true;
                    break;
                case LifecycleEvent.FocusRegained:
                    _hasLostFocus = false;
                    break;
                case LifecycleEvent.HeadsetRemoved:
                    _isHeadsetOff = true;
                    break;
                case LifecycleEvent.HeadsetPutOn:
                    _isHeadsetOff = false;
                    break;
            }

            var reason = DescribeEvent(lifecycleEvent);
            if (ShouldRun && !isRunning)
            {
                return new BodyTrackingDecision(BodyTrackingAction.Start, reason);
            }

            if (!ShouldRun && isRunning)
            {
                return new BodyTrackingDecision(BodyTrackingAction.Stop, reason);
            }

            return new BodyTrackingDecision(BodyTrackingAction.None, reason);
        }

        /// <summary>The conditions holding body tracking off, for the device log ("none" when it should run).</summary>
        public string DescribeHolds()
        {
            var holds = new List<string>();
            if (_isAppPaused)
            {
                holds.Add("app paused");
            }

            if (_hasLostFocus)
            {
                holds.Add("input focus lost");
            }

            if (_isHeadsetOff)
            {
                holds.Add("headset off");
            }

            return holds.Count == 0 ? "none" : string.Join(", ", holds);
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
                    return "input focus lost";
                case LifecycleEvent.FocusRegained:
                    return "input focus regained";
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
