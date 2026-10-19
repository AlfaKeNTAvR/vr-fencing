using System;
using System.Collections.Generic;
using System.Linq;

namespace VrFencing.Tracking
{
    /// <summary>What <see cref="StaleXrTextureCleanup.Evaluate"/> asks the caller to do after one scan.</summary>
    public enum StaleXrTextureAction
    {
        /// <summary>Keep watching: no complete new set yet, or it has not rendered long enough.</summary>
        Wait,

        /// <summary>Destroy the render textures in <see cref="StaleXrTextureVerdict.StaleIds"/> now.</summary>
        Destroy,

        /// <summary>The scan after a destroy: log the counts that are left. The watch is over.</summary>
        ReportAfterDestroy,

        /// <summary>No XR texture from before the pause is alive (or none was recorded). The watch is over.</summary>
        NothingStale,

        /// <summary>The watch window ran out without a new set that rendered. Nothing destroyed; the watch is over.</summary>
        Expired,
    }

    /// <summary>The result of one scan: the action plus the counts the log line reports.</summary>
    public readonly struct StaleXrTextureVerdict
    {
        public StaleXrTextureVerdict(StaleXrTextureAction action, IReadOnlyList<int> staleIds, int currentCount, int newCount)
        {
            Action = action;
            StaleIds = staleIds ?? Array.Empty<int>();
            CurrentCount = currentCount;
            NewCount = newCount;
        }

        public StaleXrTextureAction Action { get; }

        /// <summary>Instance IDs of the XR textures from before the pause that are still alive, sorted.</summary>
        public IReadOnlyList<int> StaleIds { get; }

        /// <summary>Every live XR texture at this scan, the stale ones included.</summary>
        public int CurrentCount { get; }

        /// <summary>The live XR textures that did not exist before the pause: the set the resume created.</summary>
        public int NewCount { get; }
    }

    /// <summary>
    /// Workaround for PER-93: every app pause and resume makes Unity's XR display (OpenXR 1.18 on Quest 3) allocate a
    /// new set of "XR Texture" eye buffers without freeing the previous set, about 600 MB each at 1680x1760, MSAA 4.
    /// This decides when the previous set can be destroyed; the Unity side scans and destroys.
    /// <para>
    /// At the pause the caller records the instance IDs of the live XR textures. After the resume it calls
    /// <see cref="IsScanDue"/> every frame (cheap) and, every <see cref="ScanIntervalSeconds"/> for up to
    /// <see cref="WatchWindowSeconds"/>, scans the live XR textures and passes their IDs to <see cref="Evaluate"/>.
    /// The pre-pause set is destroyed only once a new set exists, its size was the same on two scans in a row (so the
    /// provider has finished creating it), and at least <see cref="MinFramesRenderedByNewSet"/> frames have run since
    /// it appeared (so the provider renders into it, not the old one). The scan after the destroy reports what is left.
    /// </para>
    /// <para>
    /// A pause during the watch cancels it (<see cref="Disarm"/>); the next pause records every live XR texture
    /// again, the old set included, so the next resume cleans both. If no new set appears, nothing is destroyed:
    /// the provider then reuses the old set, which must not be touched.
    /// </para>
    /// </summary>
    public sealed class StaleXrTextureCleanup
    {
        /// <summary>
        /// Whether the cleanup runs when the experiments file does not set <c>destroyStaleXrTexturesOnResume</c>
        /// (or there is no file). Off while it is proven on the headset; flip to true to make it the default.
        /// </summary>
        public const bool IsOnByDefault = false;

        /// <summary>How often the live XR textures are scanned while watching; each scan walks every loaded object.</summary>
        public const double ScanIntervalSeconds = 0.5;

        /// <summary>How long after a resume the watch lasts; the session restart takes about a second.</summary>
        public const double WatchWindowSeconds = 20.0;

        /// <summary>Frames the new set must have existed before the old one is destroyed: a few rendered frames.</summary>
        public const int MinFramesRenderedByNewSet = 5;

        private HashSet<int> _idsBeforePause = new HashSet<int>();
        private double _resumeSeconds;
        private double _nextScanSeconds;
        private int _newSetFirstSeenFrame;
        private int _lastNewCount;
        private bool _hasDestroyed;

        /// <summary>True between a resume and the end of the watch.</summary>
        public bool IsArmed { get; private set; }

        /// <summary>
        /// Starts the watch at a resume, with the instance IDs of the XR textures that were alive at the pause.
        /// The first scan is one interval later.
        /// </summary>
        public void Arm(IEnumerable<int> idsBeforePause, double resumeSeconds)
        {
            _idsBeforePause = new HashSet<int>(idsBeforePause ?? Array.Empty<int>());
            _resumeSeconds = resumeSeconds;
            _nextScanSeconds = resumeSeconds + ScanIntervalSeconds;
            _newSetFirstSeenFrame = 0;
            _lastNewCount = 0;
            _hasDestroyed = false;
            IsArmed = true;
        }

        /// <summary>Stops the watch, for example when the app pauses again before the cleanup ran.</summary>
        public void Disarm() => IsArmed = false;

        /// <summary>True when the caller should scan the live XR textures now. Cheap enough for every frame.</summary>
        public bool IsScanDue(double realtimeSeconds) => IsArmed && realtimeSeconds >= _nextScanSeconds;

        /// <summary>
        /// Decides what to do with the XR textures alive now (their instance IDs) at frame <paramref name="frame"/>.
        /// Every action except <see cref="StaleXrTextureAction.Wait"/> and <see cref="StaleXrTextureAction.Destroy"/>
        /// ends the watch.
        /// </summary>
        public StaleXrTextureVerdict Evaluate(IReadOnlyCollection<int> currentIds, double realtimeSeconds, int frame)
        {
            var current = currentIds ?? Array.Empty<int>();
            var staleIds = current.Where(_idsBeforePause.Contains).OrderBy(id => id).ToList();
            var newCount = current.Count - staleIds.Count;
            _nextScanSeconds = realtimeSeconds + ScanIntervalSeconds;

            if (_hasDestroyed)
            {
                return Finish(StaleXrTextureAction.ReportAfterDestroy, staleIds, current.Count, newCount);
            }

            if (staleIds.Count == 0)
            {
                return Finish(StaleXrTextureAction.NothingStale, staleIds, current.Count, newCount);
            }

            if (realtimeSeconds - _resumeSeconds > WatchWindowSeconds)
            {
                return Finish(StaleXrTextureAction.Expired, staleIds, current.Count, newCount);
            }

            if (newCount == 0 || newCount != _lastNewCount)
            {
                // No new set yet, or it is still growing: (re)start counting its frames from here.
                _lastNewCount = newCount;
                _newSetFirstSeenFrame = frame;
                return new StaleXrTextureVerdict(StaleXrTextureAction.Wait, staleIds, current.Count, newCount);
            }

            if (frame - _newSetFirstSeenFrame < MinFramesRenderedByNewSet)
            {
                return new StaleXrTextureVerdict(StaleXrTextureAction.Wait, staleIds, current.Count, newCount);
            }

            _hasDestroyed = true;
            return new StaleXrTextureVerdict(StaleXrTextureAction.Destroy, staleIds, current.Count, newCount);
        }

        private StaleXrTextureVerdict Finish(StaleXrTextureAction action, IReadOnlyList<int> staleIds, int currentCount, int newCount)
        {
            IsArmed = false;
            return new StaleXrTextureVerdict(action, staleIds, currentCount, newCount);
        }
    }
}
