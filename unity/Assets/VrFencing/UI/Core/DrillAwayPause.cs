namespace VrFencing.UI
{
    /// <summary>Why the Fencer is away from the app (PER-106), as a Unity or OVRManager callback reports it.</summary>
    public enum AwayReason
    {
        /// <summary>The app was paused: the headset went to sleep.</summary>
        AppPaused,

        /// <summary>The app lost focus: the Meta system menu or another overlay is on top.</summary>
        FocusLost,

        /// <summary>The headset came off the head (proximity sensor) or was lost.</summary>
        HeadsetRemoved,
    }

    /// <summary>What the menu does to the Drill when the Fencer goes away.</summary>
    public enum AwayAction
    {
        /// <summary>No Drill is on, or the Fencer already paused it.</summary>
        None,

        /// <summary>A start still shows its task line: drop it, no Rep has run yet.</summary>
        DropIntro,

        /// <summary>Pause the run as the Fencer's Pause button would, and open the panels so Resume is on view.</summary>
        PauseRun,
    }

    /// <summary>
    /// Auto-pause (PER-106): when the headset sleeps, the Meta menu opens or the headset comes off, a running Drill
    /// pauses as if the Fencer pressed Pause, and waits for Resume, so no Rep times out while the Fencer is away. The
    /// Meta menu does not stop Unity's frames, so without this the open Rep and the ones after it would time out.
    /// A start still showing its task line is dropped instead, since its timer would run out and start the first Rep
    /// while the Fencer is away. Time is not involved, so this runs headless.
    /// </summary>
    public static class DrillAwayPause
    {
        /// <param name="isIntroShowing">A Drill start shows its task line (<see cref="DrillIntro"/>); the run has not started.</param>
        /// <param name="isRunning">The runner runs a Drill.</param>
        /// <param name="isPausedByFencer">The run is already held by the Fencer's pause, so nothing more is needed.</param>
        public static AwayAction Decide(bool isIntroShowing, bool isRunning, bool isPausedByFencer)
        {
            if (isIntroShowing)
            {
                return AwayAction.DropIntro;
            }

            return isRunning && !isPausedByFencer ? AwayAction.PauseRun : AwayAction.None;
        }

        /// <summary>The reason as the device log words it, for example "headset asleep".</summary>
        public static string Describe(AwayReason reason)
        {
            switch (reason)
            {
                case AwayReason.AppPaused:
                    return "headset asleep";
                case AwayReason.FocusLost:
                    return "app lost focus, system menu";
                case AwayReason.HeadsetRemoved:
                    return "headset off";
                default:
                    return reason.ToString();
            }
        }
    }
}
