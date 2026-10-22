using System;
using System.IO;
using UnityEngine;
using VrFencing.Settings;
using VrFencing.Strip;

namespace VrFencing.Tracking
{
    /// <summary>
    /// Records the Fencer on the headset (PER-79) so the PC Editor can replay them later as a moving opponent: every
    /// rendered frame, the head, the sword controller and the 84 Full Body joints in the Strip frame, with fidelity and
    /// validity, written to <c>files/recordings/fencer-&lt;UTC&gt;.fencer</c> (format in <see cref="FencerRecordingFile"/>).
    /// <para>
    /// A press on the off-hand thumbstick starts and stops it; a red "REC" shows in the view meanwhile. It only reads
    /// what <see cref="BodyTrackingBehaviour"/> gives each frame and never starts body tracking itself, so the sleep/wake
    /// rules stay in one place; while body tracking is stopped the frames carry the head and controller only.
    /// </para>
    /// </summary>
    public sealed class FencerRecorderBehaviour : MonoBehaviour
    {
        /// <summary>About 105 MB at 72 Hz: a forgotten recording stops before it fills the headset.</summary>
        public const double MaxRecordingSeconds = 600.0;

        private const string RecordingsFolderName = "recordings";
        private const float BytesPerMegabyte = 1024.0f * 1024.0f;

        [SerializeField]
        [Tooltip("Runs body tracking and reads the body each frame. Found in the scene if empty.")]
        private BodyTrackingBehaviour _bodyTracking;

        [SerializeField]
        [Tooltip("Its En Garde Line is the Strip frame the poses are recorded in. Found in the scene if empty.")]
        private StripBehaviour _strip;

        private readonly TrackedPose?[] _joints = new TrackedPose?[FencerRecordingHeader.FullBodyJointCount];
        private OVRCameraRig _cameraRig;
        private TrainerSettings _settings;
        private RecordingIndicator _indicator;
        private FencerRecordingWriter _writer;
        private string _path;
        private double _startedRealtimeSeconds;
        private uint? _recordedSkeletonChangedCount;

        public bool IsRecording => _writer != null;

        /// <summary>
        /// Raised when a recording starts, with its UTC start (the files' stem): other logs written alongside it start
        /// then (PER-136, the blade-contact log).
        /// </summary>
        public event Action<DateTimeOffset> RecordingStarted;

        /// <summary>Raised when a recording stops, with why; logs written alongside it close then.</summary>
        public event Action<string> RecordingStopped;

        /// <summary>Seconds since the recording started (the time in every frame and log row); 0 while not recording.</summary>
        public double ElapsedSeconds => IsRecording ? Time.realtimeSinceStartupAsDouble - _startedRealtimeSeconds : 0.0;

        /// <summary>The button that starts and stops a recording, by name, for hints: the off-hand thumbstick.</summary>
        public string ToggleButtonName => _settings != null && _settings.Hand == Hand.Left ? "right thumbstick" : "left thumbstick";

        /// <summary>Where recordings go: on the Quest <c>/sdcard/Android/data/com.alfakentavr.vrfencing/files/recordings/</c>.</summary>
        public static string RecordingsFolder => Path.Combine(Application.persistentDataPath, RecordingsFolderName);

        private void Awake()
        {
            _bodyTracking = _bodyTracking != null ? _bodyTracking : FindFirstObjectByType<BodyTrackingBehaviour>();
            _strip = _strip != null ? _strip : FindFirstObjectByType<StripBehaviour>();
            _cameraRig = FindFirstObjectByType<OVRCameraRig>();
            _settings = SettingsFile.Load();
        }

        private void OnEnable()
        {
            if (_strip != null)
            {
                _strip.SettingsChanged += OnSettingsChanged;
            }
        }

        private void Start()
        {
            if (_cameraRig == null || _bodyTracking == null)
            {
                Debug.LogWarning("[Recorder] Missing the camera rig or body tracking; the Fencer cannot be recorded.");
                enabled = false;
                return;
            }

            _indicator = new RecordingIndicator(_cameraRig.centerEyeAnchor);
        }

        private void Update()
        {
            if (OVRInput.GetDown(GetToggleButton(_settings.Hand), OVRInput.Controller.Touch))
            {
                if (IsRecording)
                {
                    StopRecording("off-hand thumbstick pressed");
                }
                else
                {
                    StartRecording();
                }
            }
        }

        // LateUpdate: OVRCameraRig moves the head and controller anchors in Update; read them after that.
        private void LateUpdate()
        {
            _indicator?.UpdateNote(Time.realtimeSinceStartupAsDouble);
            if (!IsRecording)
            {
                _indicator?.Show(false, 0.0);
                return;
            }

            var elapsedSeconds = Time.realtimeSinceStartupAsDouble - _startedRealtimeSeconds;
            _indicator?.Show(true, elapsedSeconds);
            try
            {
                WriteFrame(elapsedSeconds);
            }
            catch (IOException exception)
            {
                Debug.LogError($"[Recorder] Writing {_path} failed ({exception.Message}); the recording so far is kept.");
                StopRecording("write failed");
                return;
            }

            if (elapsedSeconds >= MaxRecordingSeconds)
            {
                StopRecording($"reached the {MaxRecordingSeconds / 60.0:0}-minute limit");
            }
        }

        // The app may be killed while paused (headset asleep); push what is buffered to the file first.
        private void OnApplicationPause(bool isPaused)
        {
            if (isPaused && IsRecording)
            {
                try
                {
                    _writer.Flush();
                }
                catch (IOException exception)
                {
                    Debug.LogError($"[Recorder] Flushing {_path} on pause failed: {exception.Message}");
                }
            }
        }

        private void OnDisable()
        {
            if (_strip != null)
            {
                _strip.SettingsChanged -= OnSettingsChanged;
            }

            StopRecording("component disabled");
        }

        private void OnDestroy()
        {
            _indicator?.Destroy();
        }

        /// <summary>Shows a short note under the REC line for <paramref name="seconds"/> (a mark's confirmation, a hint).</summary>
        public void ShowNote(string text, Color color, double seconds)
        {
            _indicator?.ShowNote(text, color, Time.realtimeSinceStartupAsDouble + seconds);
        }

        private void OnSettingsChanged(TrainerSettings settings)
        {
            _settings = settings;
        }

        /// <summary>The off-hand thumbstick press: free on both controllers (the Strip, the menus and the Blade use none).</summary>
        private static OVRInput.RawButton GetToggleButton(Hand swordHand)
        {
            return swordHand == Hand.Left ? OVRInput.RawButton.RThumbstick : OVRInput.RawButton.LThumbstick;
        }

        private void StartRecording()
        {
            var startedUtc = DateTimeOffset.UtcNow;
            var path = Path.Combine(RecordingsFolder, FencerRecordingFile.GetFileName(startedUtc));
            var header = new FencerRecordingHeader(
                FencerRecordingFile.CurrentFormatVersion,
                FencerRecordingHeader.FullBodyJointSet,
                FencerRecordingHeader.FullBodyJointCount,
                _settings.FencerHeightMeters,
                _settings.Hand,
                _strip != null && _strip.IsPlaced ? _strip.Geometry?.LengthMeters : null,
                Application.version,
                startedUtc);
            try
            {
                Directory.CreateDirectory(RecordingsFolder);
                _writer = new FencerRecordingWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write), header);
            }
            catch (IOException exception)
            {
                Debug.LogError($"[Recorder] Could not start a recording at {path}: {exception.Message}");
                return;
            }
            catch (UnauthorizedAccessException exception)
            {
                Debug.LogError($"[Recorder] Could not start a recording at {path}: {exception.Message}");
                return;
            }

            _path = path;
            _startedRealtimeSeconds = Time.realtimeSinceStartupAsDouble;
            _recordedSkeletonChangedCount = null;
            var stripNote = header.StripLengthMeters.HasValue ? "in the Strip frame" : "with no Strip placed (room origin)";
            Debug.Log($"[Recorder] Recording the Fencer {stripNote} to {path}; press the off-hand thumbstick again to stop.");
            RecordingStarted?.Invoke(startedUtc);
        }

        private void StopRecording(string reason)
        {
            if (!IsRecording)
            {
                return;
            }

            var frames = _writer.FramesWritten;
            var seconds = Time.realtimeSinceStartupAsDouble - _startedRealtimeSeconds;
            try
            {
                _writer.Dispose();
            }
            catch (IOException exception)
            {
                Debug.LogError($"[Recorder] Closing {_path} failed: {exception.Message}");
            }

            _writer = null;
            _indicator?.Show(false, 0.0);
            var megabytes = File.Exists(_path) ? new FileInfo(_path).Length / BytesPerMegabyte : 0.0f;
            Debug.Log($"[Recorder] Recording stopped ({reason}): {frames} frames, {seconds:0.0} s, {megabytes:0.0} MB, {_path}.");
            RecordingStopped?.Invoke(reason);
        }

        private void WriteFrame(double elapsedSeconds)
        {
            var enGardeLine = _strip != null && _strip.EnGardeLine != null
                ? new Pose(_strip.EnGardeLine.position, _strip.EnGardeLine.rotation).ToTrackedPose()
                : new TrackedPose(System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity);
            var fidelity = _bodyTracking.Fidelity;
            if (fidelity.HasValue)
            {
                WriteBindPoseIfChanged();
            }

            for (var joint = 0; joint < _joints.Length; joint++)
            {
                var jointInRoom = fidelity.HasValue ? _bodyTracking.ReadJointInRoom(joint) : null;
                _joints[joint] = jointInRoom.HasValue ? StripFrame.ToStripFrame(enGardeLine, jointInRoom.Value) : (TrackedPose?)null;
            }

            var head = _cameraRig.centerEyeAnchor;
            var headInStrip = OVRManager.isHmdPresent
                ? StripFrame.ToStripFrame(enGardeLine, new Pose(head.position, head.rotation).ToTrackedPose())
                : (TrackedPose?)null;
            _writer.WriteFrame(new FencerFrame(elapsedSeconds, headInStrip, ReadSwordControllerInStrip(enGardeLine), fidelity, _joints));
        }

        /// <summary>A new bind pose when the runtime re-measured the Fencer (and the first one of the recording).</summary>
        private void WriteBindPoseIfChanged()
        {
            var changedCount = _bodyTracking.SkeletonChangedCount;
            if (_recordedSkeletonChangedCount == changedCount || !_bodyTracking.TryReadBindPose(out var bindPose))
            {
                return;
            }

            _writer.WriteBindPose(bindPose);
            _recordedSkeletonChangedCount = changedCount;
        }

        /// <summary>The sword-hand controller anchor, as the Blade reads it, or null when the controller is not tracked.</summary>
        private TrackedPose? ReadSwordControllerInStrip(TrackedPose enGardeLine)
        {
            var isLeft = _settings.Hand == Hand.Left;
            var controller = isLeft ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
            if (!OVRInput.GetControllerPositionTracked(controller) && !OVRInput.GetControllerOrientationTracked(controller))
            {
                return null;
            }

            var anchor = isLeft ? _cameraRig.leftControllerAnchor : _cameraRig.rightControllerAnchor;
            return StripFrame.ToStripFrame(enGardeLine, new Pose(anchor.position, anchor.rotation).ToTrackedPose());
        }
    }
}
