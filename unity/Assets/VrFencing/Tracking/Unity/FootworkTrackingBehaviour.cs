using UnityEngine;
using VrFencing.Drills;
using VrFencing.Settings;
using VrFencing.Strip;

namespace VrFencing.Tracking
{
    /// <summary>
    /// Scene host for the Core <see cref="RepFootworkTracker"/>: while a Drill that judges footwork runs (distance
    /// choice, footwork calls), it feeds the head's position in the Strip frame to the tracker every frame, restarts the
    /// tracker on each Rep cue, and reports the footwork read so far to the Drill runner, which judges with the latest.
    /// Each judged Rep's footwork is printed to the device log with a <c>[Tracking]</c> tag.
    /// All rules live in the Core (<see cref="FootworkClassifier"/>); this only reads the headset and forwards.
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
        private OVRCameraRig _cameraRig;
        private DrillRunner _subscribedRunner;
        private float _fencerHeightMeters = new TrainerSettings().FencerHeightMeters;

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
                Debug.LogWarning("[Tracking] Missing the Drill runner, Strip or camera rig; footwork will not be read.");
                return;
            }

            _subscribedRunner = _drillRunner.Runner;
            _subscribedRunner.Started += OnRunStarted;
            _subscribedRunner.RepCued += OnRepCued;
            _subscribedRunner.RepJudged += OnRepJudged;
        }

        private void Update()
        {
            var runner = _subscribedRunner;
            if (runner == null || !runner.IsRunning || runner.IsPaused || !runner.Drill.UsesFootwork)
            {
                return;
            }

            var enGardeLine = new Pose(_strip.EnGardeLine.position, _strip.EnGardeLine.rotation).ToTrackedPose();
            var headInStrip = StripFrame.ToStripFrame(enGardeLine, _cameraRig.centerEyeAnchor.position.ToNumerics());
            _tracker.AddSample(new HeadSample(Time.timeAsDouble, headInStrip));
            runner.ReportFootwork(_tracker.Read(_fencerHeightMeters));
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
        }

        // The height scales the classifier's distances; it can only change from the menu between runs.
        private void OnRunStarted()
        {
            _fencerHeightMeters = SettingsFile.Load().FencerHeightMeters;
        }

        private void OnRepCued(RepCue cue)
        {
            _tracker.BeginRep(Time.timeAsDouble);
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
            Debug.Log($"[Tracking] Footwork: {asked}, read {footwork.Action} ({result.Outcome}); forward {footwork.ForwardTravelMeters:0.00} m, "
                + $"back {footwork.BackwardTravelMeters:0.00} m, peak {footwork.PeakForwardSpeedMetersPerSecond:0.0} m/s, "
                + $"head drop {footwork.HeadDropMeters:0.00} m, onset {onset}.");
        }
    }
}
