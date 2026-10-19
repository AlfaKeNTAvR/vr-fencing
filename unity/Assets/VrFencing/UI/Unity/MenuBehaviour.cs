using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using VrFencing.Blade;
using VrFencing.Drills;
using VrFencing.Dummy;
using VrFencing.Session;
using VrFencing.Settings;
using VrFencing.Strip;

namespace VrFencing.UI
{
    /// <summary>
    /// The menus (PER-36, decided in PER-27): world-space panels in front of the Fencer, worked with a laser from the
    /// off-hand controller and its trigger. Home holds the Drill picker and the flow buttons, then Settings, Results
    /// (after each Drill) and History; during a Drill a small Running panel holds pause, cancel and flag. Placing the
    /// Strip hides every panel, because placement uses the same trigger.
    ///
    /// This is the scene glue: it starts, pauses and cancels Drills on the one <see cref="DrillRunnerBehaviour"/>,
    /// flags Reps and reads results through <see cref="SessionRecorderBehaviour"/>, re-places the Strip, and saves
    /// Settings and hands them to the Strip, the Dummy and the Blade. What the screens say is decided in the UI Core.
    /// </summary>
    public sealed class MenuBehaviour : MonoBehaviour
    {
        // The main panel sits 1.1 m ahead, a little below eye level: close enough to read and point at comfortably.
        private const float MainPanelDistanceMeters = 1.1f;
        private const float MainPanelBelowEyeMeters = 0.12f;
        private const float MainPanelTurnDegrees = 40.0f;

        // The Running panel sits low and to the off-hand side, below the line of sight to the Dummy.
        private const float RunningPanelDistanceMeters = 0.85f;
        private const float RunningPanelBelowEyeMeters = 0.45f;
        private const float RunningPanelSideMeters = 0.3f;
        private const float RunningPanelTurnDegrees = 35.0f;

        private static readonly Vector2 MainPanelSize = new Vector2(0.78f, 0.58f);

        [SerializeField]
        private DrillRunnerBehaviour _drillRunner;

        [SerializeField]
        private SessionRecorderBehaviour _sessionRecorder;

        [SerializeField]
        private StripBehaviour _strip;

        [SerializeField]
        private DummyBehaviour _dummy;

        [SerializeField]
        private BladeBehaviour _blade;

        private readonly MenuNavigator _navigator = new MenuNavigator();
        private readonly PanelFollower _mainFollower =
            new PanelFollower(MainPanelDistanceMeters, MainPanelBelowEyeMeters, sideMeters: 0.0f, MainPanelTurnDegrees);
        private readonly PanelFollower _runningFollower =
            new PanelFollower(RunningPanelDistanceMeters, RunningPanelBelowEyeMeters, RunningPanelSideMeters, RunningPanelTurnDegrees);

        private TrainerSettings _settings;
        private DrillRegistry _registry;
        private OVRCameraRig _cameraRig;
        private Transform _mainPanel;
        private Transform _runningPanelRoot;
        private OffHandPointer _pointer;
        private HomeScreen _home;
        private SettingsScreen _settingsScreen;
        private ResultsScreen _results;
        private HistoryScreen _history;
        private RunningPanel _runningPanel;
        private DummyCueLights _cueLights;

        private MenuScreen _shownScreen = MenuScreen.Hidden;
        private bool _isDirty = true;
        private string _homeMessage = string.Empty;
        private string _settingsMessage = string.Empty;
        private DrillRunRecord _lastRecord;
        private string _lastDrillName = string.Empty;

        private DrillRunner Runner => _drillRunner.Runner;

        private bool IsOffHandLeft => _settings.Hand == Hand.Right;

        // Start, not Awake: the runner and recorder are created in their Awake.
        private void Start()
        {
            if (_drillRunner == null || _sessionRecorder == null || _strip == null)
            {
                Debug.LogError("[UI] The menu needs the Drill runner, the session recorder and the Strip; it is off.");
                enabled = false;
                return;
            }

            _settings = SettingsFile.Load();
            _registry = DrillCatalog.CreateDefault(includeTestDrills: Debug.isDebugBuild);
            _cameraRig = FindFirstObjectByType<OVRCameraRig>();
            if (_cameraRig == null)
            {
                Debug.LogError("[UI] No OVRCameraRig in the scene; the menu cannot follow the head or read the controllers.");
            }

            BuildPanels();
            _strip.Placed += OnStripPlaced;
            _strip.SettingsChanged += OnStripSettingsChanged;
            Runner.Finished += OnDrillFinished;
            _sessionRecorder.DrillRunRecorded += OnDrillRunRecorded;
            if (_dummy != null)
            {
                _cueLights = new DummyCueLights(Runner, _dummy);
            }
        }

        private void OnDestroy()
        {
            if (_strip != null)
            {
                _strip.Placed -= OnStripPlaced;
                _strip.SettingsChanged -= OnStripSettingsChanged;
            }

            if (_drillRunner != null && _drillRunner.Runner != null)
            {
                _drillRunner.Runner.Finished -= OnDrillFinished;
            }

            if (_sessionRecorder != null)
            {
                _sessionRecorder.DrillRunRecorded -= OnDrillRunRecorded;
            }

            _cueLights?.Dispose();
        }

        private void Update()
        {
            var head = _cameraRig != null ? _cameraRig.centerEyeAnchor : null;
            var screen = _navigator.GetVisibleScreen(_strip.IsPlacing, Runner.IsRunning);
            if (screen != _shownScreen)
            {
                ShowScreen(screen);
            }

            if (_isDirty)
            {
                _isDirty = false;
                RefreshPage(screen);
            }

            var nowSeconds = Time.timeAsDouble;
            if (screen == MenuScreen.Running)
            {
                _runningPanel.Refresh(RunStatusText.Format(Runner, _strip.IsFencerOnStrip), Runner.IsPausedByFencer, nowSeconds);
                _runningFollower.Tick(_runningPanelRoot, head, Time.deltaTime, IsOffHandLeft);
            }
            else if (screen != MenuScreen.Hidden)
            {
                _mainFollower.Tick(_mainPanel, head, Time.deltaTime, IsOffHandLeft);
            }

            _pointer.Tick(GetOffHandController(), IsOffHandLeft ? OVRInput.RawButton.LIndexTrigger : OVRInput.RawButton.RIndexTrigger,
                screen != MenuScreen.Hidden, nowSeconds);
        }

        private void BuildPanels()
        {
            _mainPanel = new GameObject("Menu Panel").transform;
            _mainPanel.SetParent(transform, false);
            UiSurface.Create(_mainPanel, "Background", MainPanelSize);
            _home = new HomeScreen(_mainPanel, StartDrill, () => OpenPage(MenuScreen.Settings), () => OpenPage(MenuScreen.History),
                ReplaceStrip, CycleTolerance);
            _settingsScreen = new SettingsScreen(_mainPanel, ChangeSetting, () => OpenPage(MenuScreen.Home));
            _results = new ResultsScreen(_mainPanel, RunAgain, () => OpenPage(MenuScreen.History), () => OpenPage(MenuScreen.Home));
            _history = new HistoryScreen(_mainPanel, () => OpenPage(MenuScreen.Home));

            _runningPanelRoot = new GameObject("Running Panel").transform;
            _runningPanelRoot.SetParent(transform, false);
            _runningPanel = new RunningPanel(_runningPanelRoot, TogglePause, () => Runner.Cancel(), () => _sessionRecorder.FlagRep());

            _pointer = new OffHandPointer(transform);
            ShowScreen(MenuScreen.Hidden);
        }

        private void ShowScreen(MenuScreen screen)
        {
            var wasMainShown = _shownScreen != MenuScreen.Hidden && _shownScreen != MenuScreen.Running;
            var isMainShown = screen != MenuScreen.Hidden && screen != MenuScreen.Running;
            if (isMainShown && !wasMainShown)
            {
                _mainFollower.Snap();
            }

            if (screen == MenuScreen.Running && _shownScreen != MenuScreen.Running)
            {
                _runningFollower.Snap();
            }

            _mainPanel.gameObject.SetActive(isMainShown);
            _runningPanelRoot.gameObject.SetActive(screen == MenuScreen.Running);
            _home.SetVisible(screen == MenuScreen.Home);
            _settingsScreen.SetVisible(screen == MenuScreen.Settings);
            _results.SetVisible(screen == MenuScreen.Results);
            _history.SetVisible(screen == MenuScreen.History);
            _shownScreen = screen;
            _isDirty = true;
        }

        private void RefreshPage(MenuScreen screen)
        {
            switch (screen)
            {
                case MenuScreen.Home:
                    _home.Refresh(BuildPickerEntries(), FormatStrip(), _strip.OffStripToleranceMeters, _homeMessage);
                    break;
                case MenuScreen.Settings:
                    _settingsScreen.Refresh(_settings, _settingsMessage);
                    break;
                case MenuScreen.Results:
                    RefreshResults();
                    break;
                case MenuScreen.History:
                    var history = _sessionRecorder.LoadHistory();
                    _history.Refresh(history, HistoryList.Build(history, BuildDrills()), GetLocalOffset());
                    break;
            }
        }

        private void RefreshResults()
        {
            if (_lastRecord == null)
            {
                _results.Refresh(_lastDrillName, null, Array.Empty<TrendBar>(), canRunAgain: false);
                return;
            }

            var results = _lastRecord.Results;
            var trend = _sessionRecorder.LoadHistory().GetTrend(results.DrillId, results.IsShrunk);
            var canRunAgain = BuildPickerEntries().Any(entry => entry.Drill.Id == results.DrillId && entry.IsAvailable);
            _results.Refresh(_lastDrillName, _lastRecord, ResultsText.FormatTrend(trend, GetLocalOffset()), canRunAgain);
        }

        private IReadOnlyList<DrillDefinition> BuildDrills()
        {
            try
            {
                return _registry.BuildAll(_settings);
            }
            catch (ArgumentException error)
            {
                // A Setting edited by hand in the file can be out of a Drill's range; the settings screen cannot produce one.
                Debug.LogError($"[UI] Building the Drills failed: {error.Message}");
                _homeMessage = "Drills could not be built from the Settings; check them";
                return Array.Empty<DrillDefinition>();
            }
        }

        private IReadOnlyList<DrillPickerEntry> BuildPickerEntries()
        {
            return DrillPicker.BuildEntries(BuildDrills(), _strip.IsPlaced ? _strip.Geometry.LengthMeters : (float?)null, _settings.ShrinkDrillsToFit);
        }

        private string FormatStrip()
        {
            return _strip.IsPlaced ? $"Strip: {_strip.Geometry.LengthMeters:0.0} m" : "No Strip placed";
        }

        private void OpenPage(MenuScreen page)
        {
            _navigator.Open(page);
            _homeMessage = string.Empty;
            _settingsMessage = string.Empty;
            _isDirty = true;
        }

        private void StartDrill(string drillId)
        {
            var drill = BuildDrills().FirstOrDefault(candidate => candidate.Id == drillId);
            if (drill == null || !_strip.IsPlaced || Runner.IsRunning)
            {
                return;
            }

            var availability = Runner.Start(drill, _strip.Geometry.LengthMeters, _settings.ShrinkDrillsToFit);
            if (!availability.IsAvailable)
            {
                _homeMessage = $"{drill.Name} needs a {availability.RequiredStripLengthMeters:0.0} m Strip";
                _isDirty = true;
                return;
            }

            _lastDrillName = drill.Name;
            _homeMessage = string.Empty;
            Debug.Log($"[UI] Started Drill '{drill.Id}'{(availability.IsShrunk ? " (shortened)" : string.Empty)}.");
        }

        private void RunAgain()
        {
            if (_lastRecord != null)
            {
                StartDrill(_lastRecord.Results.DrillId);
            }
        }

        private void TogglePause()
        {
            if (Runner.IsPausedByFencer)
            {
                Runner.ResumeByFencer();
            }
            else
            {
                Runner.PauseByFencer();
            }
        }

        private void ReplaceStrip()
        {
            _strip.BeginPlacement("Re-placing the Strip.");
        }

        private void CycleTolerance()
        {
            // The Strip saves it and raises SettingsChanged, which refreshes this menu.
            _strip.CycleOffStripTolerance();
        }

        private void ChangeSetting(SettingId id, int direction)
        {
            var changed = SettingsPage.Change(_settings, id, direction);
            _settings = changed;
            try
            {
                SettingsFile.Save(changed);
                _settingsMessage = string.Empty;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Debug.LogError($"[UI] Saving Settings failed: {error.Message}");
                _settingsMessage = "Not saved (see log); used until the app closes";
            }

            var impact = SettingsPage.GetImpact(id);
            if ((impact & SettingImpact.Dummy) != 0 && _dummy != null)
            {
                _dummy.Configure(changed.FencerHeightMeters, changed.Hand);
            }

            if ((impact & SettingImpact.Strip) != 0)
            {
                _strip.ApplySettings(changed);
            }

            if ((impact & SettingImpact.Blade) != 0 && _blade != null)
            {
                _blade.ApplySettings(changed);
            }

            _isDirty = true;
        }

        private void OnStripPlaced(StripGeometry geometry)
        {
            _isDirty = true;
        }

        private void OnStripSettingsChanged(TrainerSettings settings)
        {
            _settings = settings;
            _isDirty = true;
        }

        private void OnDrillFinished(DrillRunSummary summary)
        {
            if (summary.Results.Count == 0)
            {
                _lastRecord = null;
                _homeMessage = $"{summary.Drill.Name} cancelled before any Rep was judged";
            }

            _navigator.OnDrillFinished(hasResults: summary.Results.Count > 0);
            _isDirty = true;
        }

        private void OnDrillRunRecorded(DrillRunRecord record)
        {
            _lastRecord = record;
            _isDirty = true;
        }

        private Transform GetOffHandController()
        {
            if (_cameraRig == null)
            {
                return null;
            }

            return IsOffHandLeft ? _cameraRig.leftControllerAnchor : _cameraRig.rightControllerAnchor;
        }

        private static TimeSpan GetLocalOffset()
        {
            return TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow);
        }
    }
}
