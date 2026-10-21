using UnityEngine;
using VrFencing.Dummy;
using VrFencing.Settings;
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
    /// Outside any Drill it runs Free Practice (PER-123, <see cref="FreePractice"/>): the same drivers, continuously, for
    /// the behavior picked on the home screen, while the Dummy is shown and a Strip is placed.
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

        /// <summary>
        /// What the Dummy does in Free Practice this frame (PER-123), or null while Free Practice is off (a Drill is on,
        /// the Dummy is hidden or not configured, or no Strip is ready). The Blade's Armed Dummy reads it to hold its blade
        /// and, for <see cref="FreePracticeBehavior.Attacks"/>, to attack.
        /// </summary>
        public FreePracticeBehavior? FreePracticeNow => _isFreePracticeOn ? _freePracticeBehavior : (FreePracticeBehavior?)null;

        private DistanceKeeperDriver _distanceKeeper;
        private FootworkShowcaseDriver _showcase;
        private FreePracticeBehavior _freePracticeBehavior = new TrainerSettings().FreePractice;
        private bool _isDrillStarting;
        private bool _isFreePracticeOn;
        private FreePracticeBehavior _drivenBehavior;
        private DummyFootwork _drivenFootwork;

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
            UpdateFreePractice();
            if ((Runner.IsRunning && !Runner.IsPaused) || _isFreePracticeOn)
            {
                _distanceKeeper.Step(Time.timeAsDouble, Time.deltaTime);
                _showcase.Step(Time.deltaTime);
            }
        }

        /// <summary>The Free Practice behavior picked on the home screen (PER-123); a running Free Practice switches to it.</summary>
        public void SetFreePracticeBehavior(FreePracticeBehavior behavior)
        {
            _freePracticeBehavior = behavior;
            UpdateFreePractice();
        }

        /// <summary>
        /// A Drill's task line is up before its run starts (or no longer is): the Drill has the Dummy, so Free Practice
        /// stops at once, before the Drill stands the Dummy where it needs it.
        /// </summary>
        public void SetDrillStarting(bool isStarting)
        {
            _isDrillStarting = isStarting;
            UpdateFreePractice();
        }

        /// <summary>
        /// Starts, stops or switches Free Practice (PER-123) as the Drills, the Dummy, the Strip and the picked behavior
        /// allow; a Dummy rebuilt for new Settings (a new footwork) starts its behavior over.
        /// </summary>
        private void UpdateFreePractice()
        {
            var isDummyReady = _dummy != null && _dummy.gameObject.activeInHierarchy && _dummy.Footwork != null;
            var isStripReady = _strip != null && _strip.IsPlaced && !_strip.IsPlacing;
            var isOn = FreePractice.IsOn(Runner.IsRunning || _isDrillStarting, isDummyReady, isStripReady);
            var isSame = isOn == _isFreePracticeOn
                && (!isOn || (_drivenBehavior == _freePracticeBehavior && _drivenFootwork == _dummy.Footwork));
            if (isSame)
            {
                return;
            }

            StopFreePractice();
            if (isOn)
            {
                StartFreePractice();
            }
        }

        // Stands the Dummy where the behavior's Drill does and starts its motion; Guard and Attacks need none here (the
        // Blade's Armed Dummy holds the blade and attacks).
        private void StartFreePractice()
        {
            _isFreePracticeOn = true;
            _drivenBehavior = _freePracticeBehavior;
            _drivenFootwork = _dummy.Footwork;
            _dummy.SetDistance(FreePractice.GetDummyDistance(_drivenBehavior));
            Debug.Log($"[Dummy] Free Practice: {_drivenBehavior}, Dummy at {_dummy.DistanceMeters:0.00} m.");
            switch (_drivenBehavior)
            {
                case FreePracticeBehavior.KeepDistance:
                    _distanceKeeper.Begin(FreePractice.KeepDistanceDifficulty);
                    break;
                case FreePracticeBehavior.Footwork:
                    _showcase.Begin();
                    break;
            }
        }

        // Leaves the Dummy where it is: a Drill stands it at its own distance, the next behavior at its own.
        private void StopFreePractice()
        {
            if (!_isFreePracticeOn)
            {
                return;
            }

            _isFreePracticeOn = false;
            if (!Runner.IsRunning)
            {
                _distanceKeeper.End();
                _showcase.End();
            }

            Debug.Log($"[Dummy] Free Practice off ({_drivenBehavior}).");
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
