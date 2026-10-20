using UnityEngine;
using VrFencing.Drills;
using VrFencing.Settings;
using VrFencing.Strip;

namespace VrFencing.Tracking
{
    /// <summary>
    /// Scene host for the Core <see cref="RepFootworkTracker"/> and <see cref="StartMarkWatch"/>. While a Drill that
    /// judges footwork runs (distance choice, footwork calls), it feeds the head's position in the Strip frame to the
    /// tracker every frame, restarts the tracker on each Rep cue, and reports the footwork read so far to the Drill
    /// runner, which judges with the latest. While a Drill where the Fencer moves runs (Drills 2 to 6), it shows the
    /// Start Mark and reports every frame whether the Fencer is back at it and still, so the runner waits for that
    /// between Reps (PER-89).
    /// Each judged Rep's footwork, and each wait for the Start Mark, is printed to the device log with a <c>[Tracking]</c> tag.
    /// All rules live in the Core (<see cref="FootworkClassifier"/>, <see cref="StartMark"/>); this only reads the
    /// headset and forwards.
    /// </summary>
    public sealed class FootworkTrackingBehaviour : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The runner whose Reps are tracked and told the footwork. Found in the scene if empty.")]
        private DrillRunnerBehaviour _drillRunner;

        [SerializeField]
        [Tooltip("Its En Garde Line is the Strip frame the head is measured in. Found in the scene if empty.")]
        private StripBehaviour _strip;

        private readonly RepFootworkTracker _tracker = new RepFootworkTracker();
        private readonly StartMarkWatch _startMarkWatch = new StartMarkWatch();
        private OVRCameraRig _cameraRig;
        private DrillRunner _subscribedRunner;
        private StartMarkVisual _startMarkVisual;
        private float _fencerHeightMeters = new TrainerSettings().FencerHeightMeters;
        private float _startMarkSideToleranceMeters = new TrainerSettings().StartMarkSideToleranceMeters;
        private bool _isWaitingForStartMark;
        private double _returnStartedAtSeconds;

        private void Awake()
        {
            _drillRunner = _drillRunner != null ? _drillRunner : FindFirstObjectByType<DrillRunnerBehaviour>();
            _strip = _strip != null ? _strip : FindFirstObjectByType<StripBehaviour>();
            _cameraRig = FindFirstObjectByType<OVRCameraRig>();
        }

        // Start, not OnEnable: the runner is created in DrillRunnerBehaviour.Awake, which may run after this OnEnable.
        private void Start()
        {
            if (_drillRunner == null || _strip == null || _cameraRig == null)
            {
                Debug.LogWarning("[Tracking] Missing the Drill runner, Strip or camera rig; footwork will not be read and Drills will not wait at the Start Mark.");
                return;
            }

            _startMarkVisual = new StartMarkVisual(_strip.EnGardeLine);
            _subscribedRunner = _drillRunner.Runner;
            _subscribedRunner.Started += OnRunStarted;
            _subscribedRunner.RepCued += OnRepCued;
            _subscribedRunner.RepJudged += OnRepJudged;
            _subscribedRunner.StateChanged += OnStateChanged;
            _subscribedRunner.ReturnPrompted += OnReturnPrompted;
            _subscribedRunner.Paused += OnPaused;
            _subscribedRunner.Finished += OnRunFinished;
        }

        private void Update()
        {
            var runner = _subscribedRunner;
            if (runner == null || !runner.IsRunning || runner.IsPaused)
            {
                return;
            }

            var usesFootwork = runner.Drill.UsesFootwork;
            var waitsAtStartMark = runner.Drill.WaitsAtStartMark(runner.IsShrunk);
            if (!usesFootwork && !waitsAtStartMark)
            {
                return;
            }

            var enGardeLine = new Pose(_strip.EnGardeLine.position, _strip.EnGardeLine.rotation).ToTrackedPose();
            var headInStrip = StripFrame.ToStripFrame(enGardeLine, _cameraRig.centerEyeAnchor.position.ToNumerics());
            var sample = new HeadSample(Time.timeAsDouble, headInStrip);
            if (usesFootwork)
            {
                _tracker.AddSample(sample);
                runner.ReportFootwork(_tracker.Read(_fencerHeightMeters));
            }

            if (waitsAtStartMark)
            {
                _startMarkWatch.AddSample(sample);
                runner.ReportAtStartMark(_startMarkWatch.IsBackAndStill(_fencerHeightMeters, _startMarkSideToleranceMeters));
            }
        }

        private void OnDestroy()
        {
            if (_subscribedRunner == null)
            {
                return;
            }

            _subscribedRunner.Started -= OnRunStarted;
            _subscribedRunner.RepCued -= OnRepCued;
            _subscribedRunner.RepJudged -= OnRepJudged;
            _subscribedRunner.StateChanged -= OnStateChanged;
            _subscribedRunner.ReturnPrompted -= OnReturnPrompted;
            _subscribedRunner.Paused -= OnPaused;
            _subscribedRunner.Finished -= OnRunFinished;
        }

        // The height scales the classifier's distances and the Start Mark, and the Start Mark's side tolerance is a Setting
        // (PER-117); both are read when a run starts, so a change in the menu applies from the next run.
        private void OnRunStarted()
        {
            var settings = SettingsFile.Load();
            _fencerHeightMeters = settings.FencerHeightMeters;
            _startMarkSideToleranceMeters = settings.StartMarkSideToleranceMeters;
            _startMarkWatch.Clear();
            _isWaitingForStartMark = false;
            _startMarkVisual.Show(_subscribedRunner.Drill.WaitsAtStartMark(_subscribedRunner.IsShrunk));
        }

        private void OnRunFinished(DrillRunSummary summary)
        {
            _startMarkVisual.Show(false);
        }

        // The Fencer moves while paused; stillness must be seen again after it.
        private void OnPaused()
        {
            _startMarkWatch.Clear();
        }

        private void OnRepCued(RepCue cue)
        {
            _tracker.BeginRep(Time.timeAsDouble);
        }

        private void OnStateChanged(DrillState state)
        {
            if (state == DrillState.Return)
            {
                _isWaitingForStartMark = true;
                _returnStartedAtSeconds = Time.timeAsDouble;
                Debug.Log($"[Tracking] Waiting for the Start Mark: head {FormatOffset()}.");
            }
            else if (state == DrillState.Rest && _isWaitingForStartMark)
            {
                _isWaitingForStartMark = false;
                _startMarkVisual.SetPrompting(false);
                Debug.Log($"[Tracking] Back at the Start Mark after {Time.timeAsDouble - _returnStartedAtSeconds:0.0} s: head {FormatOffset()}.");
            }
        }

        private void OnReturnPrompted()
        {
            _startMarkVisual.SetPrompting(true);
            Debug.Log($"[Tracking] Asking the Fencer back to the line: head {FormatOffset()}.");
        }

        // Where the head is from its en garde place at the Start Mark, along the Strip (for tuning
        // StartMark.HeadBehindFrontFootPerHeight) and across it from the center line (PER-117), with the tolerances.
        private string FormatOffset()
        {
            var offset = _startMarkWatch.MeasureOffsetMeters(_fencerHeightMeters);
            var side = _startMarkWatch.MeasureSideOffsetMeters();
            return offset.HasValue && side.HasValue
                ? $"{offset.Value:+0.00;-0.00} m along the Strip from its place (within {StartMark.ToleranceMeters:0.00}), " +
                  $"{side.Value:+0.00;-0.00} m across from the center line (within {_startMarkSideToleranceMeters:0.00})"
                : "not seen yet";
        }

        private static void OnRepJudged(RepResult result)
        {
            if (!result.Footwork.HasValue)
            {
                return;
            }

            var footwork = result.Footwork.Value;
            var asked = result.RequestedFootwork.HasValue ? $"called {result.RequestedFootwork.Value}" : $"Dummy at {result.DummyDistance} distance";
            var onset = footwork.OnsetSecondsAfterCue.HasValue ? $"{footwork.OnsetSecondsAfterCue.Value:0.00} s" : "none";
            Debug.Log($"[Tracking] Footwork: {asked}, read {footwork.Action} x{footwork.StepCount} ({result.Outcome}); forward {footwork.ForwardTravelMeters:0.00} m, "
                + $"back {footwork.BackwardTravelMeters:0.00} m, peak {footwork.PeakForwardSpeedMetersPerSecond:0.0} m/s, "
                + $"head drop {footwork.HeadDropMeters:0.00} m, onset {onset}.");
        }
    }
}
