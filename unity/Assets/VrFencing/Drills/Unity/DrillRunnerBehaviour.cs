using UnityEngine;
using VrFencing.Dummy;
using VrFencing.Strip;

namespace VrFencing.Drills
{
    /// <summary>
    /// Scene host for the Core <see cref="DrillRunner"/>: ticks it every frame with Unity's game time.
    /// Other components subscribe to <see cref="Runner"/>'s events and forward their inputs to it
    /// (the Blade's contacts, the Strip's off-strip and back-on signals, the UI's start and cancel).
    /// When a run starts it stands the Dummy at the Drill's Dummy Distance (the shortened variant's when the run is
    /// shrunk), and in a Drill with a Dummy Distance per Rep (distance choice) moves it on every cue; when the run ends
    /// it stands the Dummy back where the scene had it. In a Drill whose Dummy keeps its distance (PER-105) it runs the
    /// moving Dummy every frame of the run (<see cref="DistanceKeeperDriver"/>), at each Rep's difficulty. In the
    /// footwork feel test (<see cref="DrillDefinition.ShowsFootwork"/>) it runs the Dummy through its footwork moves
    /// (<see cref="FootworkShowcaseDriver"/>).
    /// All rules live in the Core; this only supplies time and applies the Drill's data.
    /// </summary>
    public sealed class DrillRunnerBehaviour : MonoBehaviour
    {
        private sealed class UnityTimeClock : IClock
        {
            public double NowSeconds => Time.timeAsDouble;
        }

        [SerializeField]
        [Tooltip("The Strip whose off-strip signal pauses the run. Found in the scene if empty.")]
        private StripBehaviour _strip;

        [SerializeField]
        [Tooltip("The Dummy each Drill stands at its Dummy Distance. Found in the scene if empty.")]
        private DummyBehaviour _dummy;

        public DrillRunner Runner { get; private set; }

        private DistanceKeeperDriver _distanceKeeper;
        private FootworkShowcaseDriver _showcase;

        private void Awake()
        {
            Runner = new DrillRunner(new UnityTimeClock(), new System.Random());
            if (_strip == null)
            {
                _strip = FindFirstObjectByType<StripBehaviour>();
            }

            if (_dummy == null)
            {
                _dummy = FindFirstObjectByType<DummyBehaviour>();
            }

            var cameraRig = FindFirstObjectByType<OVRCameraRig>();
            _distanceKeeper = new DistanceKeeperDriver(_dummy, _strip, cameraRig != null ? cameraRig.centerEyeAnchor : null);
            _showcase = new FootworkShowcaseDriver(_dummy);
        }

        private void OnEnable()
        {
            Runner.Started += OnRunStarted;
            Runner.RepCued += OnRepCued;
            Runner.Finished += OnRunFinished;
            if (_strip == null)
            {
                return;
            }

            _strip.LeftStrip += Runner.ReportLeftStrip;
            _strip.BackOnStrip += Runner.ReportBackOnStrip;
            if (!_strip.IsFencerOnStrip)
            {
                Runner.ReportLeftStrip();
            }
        }

        private void Update()
        {
            Runner.Tick();
            if (Runner.IsRunning && !Runner.IsPaused)
            {
                _distanceKeeper.Step(Time.timeAsDouble, Time.deltaTime);
                _showcase.Step(Time.deltaTime);
            }
        }

        private void OnDisable()
        {
            if (_strip != null)
            {
                _strip.LeftStrip -= Runner.ReportLeftStrip;
                _strip.BackOnStrip -= Runner.ReportBackOnStrip;
            }

            if (Runner == null)
            {
                return;
            }

            // Cancel first, so the cancelled run still stands the Dummy back.
            Runner.Cancel();
            Runner.Started -= OnRunStarted;
            Runner.RepCued -= OnRepCued;
            Runner.Finished -= OnRunFinished;
        }

        private void OnRunStarted()
        {
            if (_dummy == null)
            {
                Debug.LogWarning("[Drills] No Dummy in the scene; the Drill runs without moving it.");
                return;
            }

            var distance = Runner.Drill.GetDummyDistance(Runner.IsShrunk);
            _dummy.SetDistance(distance);
            var variant = Runner.IsShrunk ? " (shortened)" : string.Empty;
            Debug.Log($"[Drills] {Runner.Drill.Name}{variant} started: Dummy at {distance} distance, {_dummy.DistanceMeters:0.00} m.");
            var difficulty = Runner.Drill.GetDummyDifficulty(0);
            if (difficulty.HasValue)
            {
                _distanceKeeper.Begin(difficulty.Value);
            }

            if (Runner.Drill.ShowsFootwork)
            {
                _showcase.Begin();
            }
        }

        // Distance choice: the Dummy appearing at the Rep's distance is the cue. Rest separates it from the last Touch,
        // so it does not jump under a lunge in progress; the Blade restarts its sweep when the Dummy moves.
        private void OnRepCued(RepCue cue)
        {
            if (cue.DummyDifficulty.HasValue)
            {
                _distanceKeeper.SetDifficulty(cue.DummyDifficulty.Value, cue.RepIndex);
            }

            if (_dummy == null || Runner.Drill.RepDummyDistances == null)
            {
                return;
            }

            _dummy.SetDistance(cue.DummyDistance);
            Debug.Log($"[Drills] Rep {cue.RepIndex + 1}: Dummy at {cue.DummyDistance} distance, {_dummy.DistanceMeters:0.00} m.");
        }

        private void OnRunFinished(DrillRunSummary summary)
        {
            _distanceKeeper.End();
            _showcase.End();
            if (_dummy != null)
            {
                _dummy.ResetDistance();
            }
        }
    }
}
