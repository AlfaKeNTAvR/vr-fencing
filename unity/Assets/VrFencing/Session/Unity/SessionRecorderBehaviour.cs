using System;
using UnityEngine;
using VrFencing.Drills;

namespace VrFencing.Session
{
    /// <summary>
    /// Scene host for the Core <see cref="SessionRecorder"/>: one session per app launch, files under
    /// <see cref="Application.persistentDataPath"/> (on the Quest: /sdcard/Android/data/com.alfakentavr.vrfencing/files/).
    /// Listens to the <see cref="DrillRunnerBehaviour"/>'s runner, flushes the last Rep when the app pauses or closes,
    /// and logs file errors as warnings so a full disk never stops a Drill. The results UI listens to
    /// <see cref="DrillRunRecorded"/> and reads the trend from <see cref="LoadHistory"/>.
    /// </summary>
    public sealed class SessionRecorderBehaviour : MonoBehaviour
    {
        private sealed class SystemWallClock : IWallClock
        {
            public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
        }

        [SerializeField]
        private DrillRunnerBehaviour _drillRunner;

        private bool _isAttached;

        public SessionRecorder Recorder { get; private set; }

        /// <summary>
        /// Samples the IOBT arm pose when a Rep is judged. Set by the Tracking module when it starts; while unset
        /// (or returning null) the rep log gets no arm pose. Read at judging time, so it can be set after Awake.
        /// </summary>
        public Func<ArmPoseSnapshot> ArmPoseSource { get; set; }

        /// <summary>A Drill run finished: its results, for the results panel.</summary>
        public event Action<DrillRunRecord> DrillRunRecorded;

        /// <summary>The flag button: marks the Rep just judged, or the one in progress.</summary>
        public void FlagRep()
        {
            Recorder.FlagRep();
        }

        /// <summary>Reads every saved Drill run. Unreadable lines are skipped with a warning.</summary>
        public SessionHistory LoadHistory()
        {
            try
            {
                var read = SessionHistoryFile.Read(Recorder.HistoryPath);
                if (read.SkippedLineCount > 0)
                {
                    Debug.LogWarning($"[Session] Skipped {read.SkippedLineCount} unreadable line(s) in '{Recorder.HistoryPath}'.");
                }

                return new SessionHistory(read.Items);
            }
            catch (Exception error) when (error is System.IO.IOException || error is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[Session] Could not read '{Recorder.HistoryPath}': {error.Message} Showing no history.");
                return new SessionHistory(Array.Empty<DrillRunRecord>());
            }
        }

        private void Awake()
        {
            Recorder = new SessionRecorder(Application.persistentDataPath, Guid.NewGuid().ToString("N"), new SystemWallClock(), () => ArmPoseSource?.Invoke());
            Recorder.WriteFailed += error => Debug.LogWarning($"[Session] Could not write the session files: {error.Message}");
            Recorder.DrillRunRecorded += record => DrillRunRecorded?.Invoke(record);
        }

        // Start, not OnEnable: the runner is created in DrillRunnerBehaviour.Awake, which may run after this Awake.
        private void Start()
        {
            if (_drillRunner == null)
            {
                Debug.LogWarning("[Session] No DrillRunnerBehaviour assigned; nothing will be recorded.");
                return;
            }

            Recorder.Attach(_drillRunner.Runner);
            _isAttached = true;
        }

        private void OnApplicationPause(bool isPaused)
        {
            if (isPaused)
            {
                Recorder.Flush();
            }
        }

        private void OnDestroy()
        {
            if (_isAttached && _drillRunner != null)
            {
                Recorder.Detach(_drillRunner.Runner);
            }

            Recorder?.Flush();
        }
    }
}
