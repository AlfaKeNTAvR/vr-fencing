using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Oculus.Haptics;
using UnityEngine;
using VrFencing.Settings;
using VrFencing.Tracking;
using Debug = UnityEngine.Debug;

namespace VrFencing.Blade
{
    /// <summary>
    /// The blade-contact log on the headset (PER-136). While the Fencer recorder records (off-hand thumbstick, red REC),
    /// it writes <c>files/recordings/blade-&lt;UTC&gt;.csv</c> next to the <c>fencer-&lt;UTC&gt;.fencer</c> file, one row per
    /// rendered frame (<see cref="BladeContactLog"/>): both blades, their contact, the Dummy's hand, the sword hand's
    /// velocity and the frame's events. The off-hand grip is the "felt wrong" mark: a press writes a mark into the log,
    /// ticks the off-hand controller and shows "MARK n" under REC; while REC is off it only shows how to start REC.
    /// <para>
    /// It adds itself to the scene's Blade at start (when the scene also has a Fencer recorder), runs after the Blade
    /// each frame (<see cref="DefaultExecutionOrder"/>), writes through a 64 KB buffer and allocates nothing per frame;
    /// the stop log line gives the rows, the marks, the size and the time it took per frame.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class BladeContactRecorderBehaviour : MonoBehaviour
    {
        /// <summary>The CSV writer's buffer: a row is about 350 bytes, so it reaches the file a few times a second.</summary>
        public const int WriteBufferBytes = 64 * 1024;

        private const double MarkNoteSeconds = 1.0;
        private const double HintNoteSeconds = 2.5;
        private const float MarkTickAmplitude = 0.9f;
        private const float MarkTickFrequency = 0.9f;
        private const float MarkTickSeconds = 0.04f;

        private static readonly Color MarkColor = new Color(1.0f, 0.85f, 0.2f, 1.0f);
        private static readonly Color HintColor = Color.white;

        private readonly BladeContactLog _log = new BladeContactLog();
        private readonly Stopwatch _frameWatch = new Stopwatch();
        private BladeBehaviour _blade;
        private FencerRecorderBehaviour _fencerRecorder;
        private StreamWriter _writer;
        private string _path;
        private BladeContactSample _sample;
        private double _lastTimeSeconds;
        private long _mostFrameTicks;
        private BladeLogEvents _contactEvents;
        private HapticClip _markClip;
        private HapticClipPlayer _markPlayer;
        private bool _hasHapticsFailed;

        private bool IsLogging => _writer != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AttachToScene()
        {
            var blade = FindFirstObjectByType<BladeBehaviour>();
            var fencerRecorder = FindFirstObjectByType<FencerRecorderBehaviour>();
            if (blade == null || fencerRecorder == null || blade.GetComponent<BladeContactRecorderBehaviour>() != null)
            {
                return;
            }

            blade.gameObject.AddComponent<BladeContactRecorderBehaviour>();
        }

        private void Awake()
        {
            _blade = GetComponent<BladeBehaviour>();
            _fencerRecorder = FindFirstObjectByType<FencerRecorderBehaviour>();
        }

        private void OnEnable()
        {
            if (_fencerRecorder != null)
            {
                _fencerRecorder.RecordingStarted += OnRecordingStarted;
                _fencerRecorder.RecordingStopped += OnRecordingStopped;
            }

            if (_blade != null)
            {
                _blade.Contact += OnBladeContact;
            }
        }

        private void OnDisable()
        {
            if (_fencerRecorder != null)
            {
                _fencerRecorder.RecordingStarted -= OnRecordingStarted;
                _fencerRecorder.RecordingStopped -= OnRecordingStopped;
            }

            if (_blade != null)
            {
                _blade.Contact -= OnBladeContact;
            }

            Close("component disabled");
        }

        private void OnDestroy()
        {
            _markPlayer?.Dispose();
            if (_markClip != null)
            {
                Destroy(_markClip);
            }
        }

        private void Update()
        {
            if (_blade == null || _fencerRecorder == null)
            {
                return;
            }

            var offHandGrip = _blade.SwordHand == Hand.Left ? OVRInput.RawButton.RHandTrigger : OVRInput.RawButton.LHandTrigger;
            if (OVRInput.GetDown(offHandGrip, OVRInput.Controller.Touch))
            {
                OnMarkPressed();
            }
        }

        // After the Blade's LateUpdate (DefaultExecutionOrder): this frame's blades are solved and drawn.
        private void LateUpdate()
        {
            if (!IsLogging)
            {
                return;
            }

            _frameWatch.Start();
            var startTicks = _frameWatch.ElapsedTicks;
            try
            {
                _sample.TimeSeconds = _fencerRecorder.ElapsedSeconds;
                _sample.DeltaSeconds = (float)(_sample.TimeSeconds - _lastTimeSeconds);
                _lastTimeSeconds = _sample.TimeSeconds;
                _sample.Events = _blade.ReadForRecording(ref _sample) | _contactEvents;
                _contactEvents = BladeLogEvents.None;
                _log.WriteRow(_sample, _writer);
            }
            catch (IOException exception)
            {
                Debug.LogError($"[Recorder] Writing {_path} failed ({exception.Message}); the blade log so far is kept.");
                Close("write failed");
            }
            finally
            {
                _frameWatch.Stop();
                _mostFrameTicks = Math.Max(_mostFrameTicks, _frameWatch.ElapsedTicks - startTicks);
            }
        }

        // The app may be killed while paused (headset asleep); push what is buffered to the file first.
        private void OnApplicationPause(bool isPaused)
        {
            if (!isPaused || !IsLogging)
            {
                return;
            }

            try
            {
                _writer.Flush();
            }
            catch (IOException exception)
            {
                Debug.LogError($"[Recorder] Flushing {_path} on pause failed: {exception.Message}");
            }
        }

        private void OnRecordingStarted(DateTimeOffset startedUtc)
        {
            Close("a new recording started");
            var path = Path.Combine(FencerRecorderBehaviour.RecordingsFolder, BladeContactLog.GetFileName(startedUtc));
            try
            {
                var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                _writer = new StreamWriter(stream, new UTF8Encoding(false), WriteBufferBytes);
                _log.WriteHeader(_writer, Application.version, _blade.SwordHand, startedUtc);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogError($"[Recorder] Could not start the blade log at {path}: {exception.Message}");
                _writer?.Dispose();
                _writer = null;
                return;
            }

            _path = path;
            _lastTimeSeconds = 0.0;
            _contactEvents = BladeLogEvents.None;
            _frameWatch.Reset();
            _mostFrameTicks = 0;
            _blade.ForgetRecordingEvents();
            Debug.Log($"[Recorder] Blade log to {path}; the off-hand grip marks a moment that felt wrong.");
        }

        private void OnRecordingStopped(string reason)
        {
            Close(reason);
        }

        private void OnBladeContact(BladeEvent bladeEvent)
        {
            switch (bladeEvent.Type)
            {
                case BladeEventType.Touch:
                    _contactEvents |= BladeLogEvents.Touch;
                    break;
                case BladeEventType.TipGlance:
                    _contactEvents |= BladeLogEvents.Glance;
                    break;
                case BladeEventType.FlatContact:
                    _contactEvents |= BladeLogEvents.FlatContact;
                    break;
            }
        }

        /// <summary>The "felt wrong" button: a mark in the log, a tick and "MARK n" while REC is on; else how to start REC.</summary>
        private void OnMarkPressed()
        {
            if (!IsLogging)
            {
                _fencerRecorder.ShowNote($"REC is off: press the {_fencerRecorder.ToggleButtonName}", HintColor, HintNoteSeconds);
                return;
            }

            _log.AddEvents(BladeLogEvents.Mark);
            var number = _log.MarkCount + 1;
            _fencerRecorder.ShowNote($"MARK {number}", MarkColor, MarkNoteSeconds);
            PlayMarkTick();
            Debug.Log($"[Recorder] Mark {number} at {_fencerRecorder.ElapsedSeconds:0.00} s.");
        }

        /// <summary>A short tick on the off-hand controller, the hand that pressed.</summary>
        private void PlayMarkTick()
        {
            if (_hasHapticsFailed)
            {
                return;
            }

            try
            {
                if (_markPlayer == null)
                {
                    _markClip = ScriptableObject.CreateInstance<HapticClip>();
                    _markClip.json = BladeHaptics.BuildClipJson(new[] { new HapticSegment(MarkTickAmplitude, MarkTickFrequency, MarkTickSeconds) });
                    _markPlayer = new HapticClipPlayer(_markClip);
                }

                _markPlayer.Play(_blade.SwordHand == Hand.Left ? Controller.Right : Controller.Left);
            }
            catch (Exception error)
            {
                // The mark is in the log either way; only the tick is lost.
                _hasHapticsFailed = true;
                Debug.LogError($"[Recorder] The mark's haptic tick failed, off for this session: {error.Message}");
            }
        }

        private void Close(string reason)
        {
            if (!IsLogging)
            {
                return;
            }

            try
            {
                _writer.Dispose();
            }
            catch (IOException exception)
            {
                Debug.LogError($"[Recorder] Closing {_path} failed: {exception.Message}");
            }

            _writer = null;
            var rows = Math.Max(1, _log.RowCount);
            var averageMicroseconds = _frameWatch.Elapsed.TotalMilliseconds * 1000.0 / rows;
            var mostMicroseconds = _mostFrameTicks * 1e6 / Stopwatch.Frequency;
            var megabytes = File.Exists(_path) ? new FileInfo(_path).Length / (1024.0 * 1024.0) : 0.0;
            Debug.Log($"[Recorder] Blade log stopped ({reason}): {_log.RowCount} rows, {_log.MarkCount} marks, {megabytes:0.0} MB, " +
                $"{averageMicroseconds:0} us a frame (most {mostMicroseconds:0} us), {_path}.");
        }
    }
}
