using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using VrFencing.Blade;
using VrFencing.Drills;
using VrFencing.Dummy;
using VrFencing.Partner;
using VrFencing.Session;
using VrFencing.Settings;
using VrFencing.Strip;

namespace VrFencing.UI
{
    /// <summary>
    /// The menus (PER-36, decided in PER-27): world-space panels in front of the Fencer, worked with a laser from the
    /// off-hand controller and its trigger. Home holds the Drill picker and the flow buttons, then Settings, Results
    /// (after each Drill) and History; during a Drill a small Running panel holds pause, cancel and flag. Placing the
    /// Strip hides every panel, because placement uses the same trigger. The left controller's Menu button (three
    /// lines) folds the panels away (Home, Running and the rest) so they do not block the view of the Dummy, and opens
    /// them again; it is always the left controller's, whichever the sword hand (PER-50). The folded state is
    /// remembered between sessions (PlayerPrefs); a finished Drill opens the panels on its results without changing it.
    ///
    /// The panels follow the head until the Fencer drags one by the grab bar under it (PER-86): on release every panel
    /// is pinned at that spot, kept relative to the Strip (<see cref="PanelPlacement"/>) and saved, so it comes back
    /// after a restart and moves along with a re-placed Strip. The pin button beside the bar toggles pinned and
    /// following; folding and opening the panels keeps the pin.
    ///
    /// Start first shows the Drill's task line above the Dummy and on the Running panel for a few seconds
    /// (<see cref="DrillIntro"/>, PER-92), then starts the run on the runner, so the Fencer knows what to do before the
    /// first cue. Cancel in that time cancels the start.
    ///
    /// The Partner page (PER-80) hosts or joins a session through the scene's <see cref="PartnerSessionBehaviour"/>.
    /// While in a session, solo Drills cannot start and the Dummy is hidden, so it never stands where the opponent
    /// stands (PER-58).
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

        // The left controller's Menu button. It is not on the right controller (that one is the system button) and is
        // no Strip button (X, Y, A, B) or trigger, so it never competes with them.
        private const OVRInput.RawButton MenuButton = OVRInput.RawButton.Start;

        private const string CollapsedPrefKey = "VrFencing.MenuCollapsed";

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

        [SerializeField]
        private PartnerSessionBehaviour _partner;

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
        private PanelGrabBar _mainGrabBar;
        private PanelGrabBar _runningGrabBar;
        private PanelPlacement _placement = PanelPlacement.Following;
        private OffHandPointer _pointer;
        private HomeScreen _home;
        private SettingsScreen _settingsScreen;
        private ResultsScreen _results;
        private HistoryScreen _history;
        private RunningPanel _runningPanel;
        private DummyCueDisplay _cueDisplay;
        private GripScreen _gripScreen;
        private NewStripScreen _newStripScreen;
        private PartnerScreen _partnerScreen;
        private CalibrationTargetView _calibrationTarget;
        private ThrustCalibration _calibration;
        private int _calibrationStartFrame;
        private string _gripMessage = string.Empty;
        private DrillIntro _intro;

        private MenuScreen _shownScreen = MenuScreen.Hidden;
        private bool _isDirty = true;
        private string _homeMessage = string.Empty;
        private string _settingsMessage = string.Empty;
        private DrillRunRecord _lastRecord;
        private string _lastDrillName = string.Empty;

        private DrillRunner Runner => _drillRunner.Runner;

        // A Drill is on: its intro shows the task line, or the runner runs it.
        private bool IsDrillOn => Runner.IsRunning || _intro != null;

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
            _navigator.SetCollapsed(LoadCollapsed());
            _placement = PanelPlacementFile.Load();
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
                _cueDisplay = new DummyCueDisplay(Runner, _dummy);
            }

            if (_partner != null)
            {
                _partner.Changed += OnPartnerChanged;
                OnPartnerChanged();
            }
            else
            {
                Debug.LogError("[UI] No Partner object; the Partner page cannot host or join.");
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

            if (_partner != null)
            {
                _partner.Changed -= OnPartnerChanged;
            }

            _cueDisplay?.Dispose();
        }

        private void Update()
        {
            var head = _cameraRig != null ? _cameraRig.centerEyeAnchor : null;
            var nowSeconds = Time.timeAsDouble;
            if (_intro != null)
            {
                TickIntro(nowSeconds);
            }

            var screen = _navigator.GetShownPanel(_strip.IsPlacing, IsDrillOn, _strip.IsAskingForNewStrip);
            if (screen != _shownScreen)
            {
                ShowScreen(screen);
            }

            if (_isDirty)
            {
                _isDirty = false;
                RefreshPage(screen);
            }

            if (_calibration != null)
            {
                TickCalibration(head, nowSeconds);
            }

            var pinnedPose = GetPinnedPose();
            if (screen == MenuScreen.Running)
            {
                var status = _intro != null ? _intro.Status : RunStatusText.Format(Runner, _strip.IsFencerOnStrip);
                _runningPanel.Refresh(status, Runner.IsPausedByFencer, nowSeconds);
                PlacePanel(_runningPanelRoot, _runningGrabBar, _runningFollower, pinnedPose, head);
            }
            else if (screen != MenuScreen.Hidden)
            {
                PlacePanel(_mainPanel, _mainGrabBar, _mainFollower, pinnedPose, head);
            }

            _pointer.Tick(GetOffHandController(), head, IsOffHandLeft ? OVRInput.RawButton.LIndexTrigger : OVRInput.RawButton.RIndexTrigger,
                _navigator.IsPointerShown(_strip.IsPlacing, IsDrillOn, _strip.IsAskingForNewStrip), _settings.LaserTiltDegrees, nowSeconds, Time.deltaTime);

            if (OVRInput.GetDown(MenuButton, OVRInput.Controller.Touch))
            {
                ToggleCollapsed();
            }
        }

        private void ToggleCollapsed()
        {
            _navigator.ToggleCollapsed();
            Debug.Log(_navigator.IsCollapsed ? "[UI] Menu folded away." : "[UI] Menu opened.");
            try
            {
                PlayerPrefs.SetInt(CollapsedPrefKey, _navigator.IsCollapsed ? 1 : 0);
                PlayerPrefs.Save();
            }
            catch (PlayerPrefsException error)
            {
                Debug.LogError($"[UI] Remembering the folded menu failed: {error.Message}");
            }
        }

        private static bool LoadCollapsed()
        {
            return PlayerPrefs.GetInt(CollapsedPrefKey, 0) == 1;
        }

        /// <summary>
        /// Where a pinned panel stands in the room now, or null to follow the head. A pin made before any Strip was
        /// placed moves into the Strip frame (and is saved) once a Strip is in place.
        /// </summary>
        private TrackedPose? GetPinnedPose()
        {
            var enGardeLine = GetEnGardeLinePose();
            if (enGardeLine.HasValue && _placement.IsPinned && !_placement.IsInStripFrame)
            {
                _placement = _placement.AttachToStrip(enGardeLine.Value);
                PanelPlacementFile.Save(_placement);
            }

            return _placement.GetPoseInRoom(enGardeLine);
        }

        /// <summary>The Strip frame's pose in the room, or null while no Strip is in place (or it is being re-placed).</summary>
        private TrackedPose? GetEnGardeLinePose()
        {
            if (!_strip.IsPlaced || _strip.EnGardeLine == null)
            {
                return null;
            }

            var enGardeLine = _strip.EnGardeLine;
            return new TrackedPose(enGardeLine.position.ToNumerics(), enGardeLine.rotation.ToNumerics());
        }

        /// <summary>Puts a shown panel at the pin, or lets it follow the head; a panel being dragged is left to the pointer.</summary>
        private void PlacePanel(Transform panel, PanelGrabBar grabBar, PanelFollower follower, TrackedPose? pinnedPose, Transform head)
        {
            if (grabBar.IsDragging)
            {
                return;
            }

            if (pinnedPose.HasValue)
            {
                panel.SetPositionAndRotation(pinnedPose.Value.Position.ToUnity(), pinnedPose.Value.Rotation.ToUnity());
                return;
            }

            follower.Tick(panel, head, Time.deltaTime, IsOffHandLeft);
        }

        /// <summary>A drag ended: every panel is pinned where the dragged one stands.</summary>
        private void PinPanels(PanelGrabBar grabBar)
        {
            var panel = grabBar.Panel;
            _placement = PanelPlacement.PinAt(new TrackedPose(panel.position.ToNumerics(), panel.rotation.ToNumerics()), GetEnGardeLinePose());
            PanelPlacementFile.Save(_placement);
            ShowPinned();
            Debug.Log(_placement.IsInStripFrame
                ? "[UI] Menu panels pinned to the Strip."
                : "[UI] Menu panels pinned in the room; no Strip yet, so the pin is kept once one is placed.");
        }

        /// <summary>The pin button: pins the panels where they stand, or sets them following the head again.</summary>
        private void TogglePinned(PanelGrabBar grabBar)
        {
            if (!_placement.IsPinned)
            {
                PinPanels(grabBar);
                return;
            }

            _placement = PanelPlacement.Following;
            PanelPlacementFile.Save(_placement);
            _mainFollower.Snap();
            _runningFollower.Snap();
            ShowPinned();
            Debug.Log("[UI] Menu panels follow the head again.");
        }

        private void ShowPinned()
        {
            _mainGrabBar.SetPinned(_placement.IsPinned);
            _runningGrabBar.SetPinned(_placement.IsPinned);
        }

        private void BuildPanels()
        {
            _mainPanel = new GameObject("Menu Panel").transform;
            _mainPanel.SetParent(transform, false);
            UiSurface.Create(_mainPanel, "Background", MainPanelSize);
            _home = new HomeScreen(_mainPanel, StartDrill, () => OpenPage(MenuScreen.Partner), () => OpenPage(MenuScreen.Settings),
                () => OpenPage(MenuScreen.History), ReplaceStrip, CycleTolerance);
            _partnerScreen = new PartnerScreen(_mainPanel, () => _partner?.Host(), () => _partner?.BeginEnteringCode(), code => _partner?.Join(code),
                () => _partner?.CancelEnteringCode(), () => _partner?.SwitchMode(), () => _partner?.Leave(), () => OpenPage(MenuScreen.Home));
            _settingsScreen = new SettingsScreen(_mainPanel, ChangeSetting, OpenSettingsGroup, () => OpenPage(MenuScreen.Grip), GoBackFromSettings);
            _gripScreen = new GripScreen(_mainPanel, StartCalibration, ResetGripAngles, () => OpenSettingsGroup(SettingGroup.Blade));
            _calibrationTarget = new CalibrationTargetView(transform);
            _results = new ResultsScreen(_mainPanel, RunAgain, () => OpenPage(MenuScreen.History), () => OpenPage(MenuScreen.Home));
            _history = new HistoryScreen(_mainPanel, () => OpenPage(MenuScreen.Home));
            _newStripScreen = new NewStripScreen(_mainPanel, () => _strip.PlaceNewStrip(), () => _strip.KeepLastStrip());

            _runningPanelRoot = new GameObject("Running Panel").transform;
            _runningPanelRoot.SetParent(transform, false);
            _runningPanel = new RunningPanel(_runningPanelRoot, TogglePause, CancelDrill, () => _sessionRecorder.FlagRep());

            _mainGrabBar = PanelGrabBar.Create(_mainPanel, MainPanelSize, PinPanels, TogglePinned);
            _runningGrabBar = PanelGrabBar.Create(_runningPanelRoot, RunningPanel.Size, PinPanels, TogglePinned);
            ShowPinned();

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
            _gripScreen.SetVisible(screen == MenuScreen.Grip);
            _newStripScreen.SetVisible(screen == MenuScreen.NewStripPrompt);
            _partnerScreen.SetVisible(screen == MenuScreen.Partner);
            _shownScreen = screen;
            _isDirty = true;
        }

        private void RefreshPage(MenuScreen screen)
        {
            switch (screen)
            {
                case MenuScreen.Home:
                    _home.Refresh(BuildPickerEntries(), FormatStrip(), _strip.OffStripToleranceMeters, FormatHomeMessage(),
                        _partner != null ? PartnerPageText.FormatHomeButton(_partner.State) : "Partner", AreSoloDrillsAllowed);
                    break;
                case MenuScreen.Partner:
                    if (_partner != null)
                    {
                        _partnerScreen.Refresh(_partner.State, _partner.AppVersion);
                    }

                    break;
                case MenuScreen.Settings:
                    _settingsScreen.Refresh(_settings, _navigator.SettingsGroup, _settingsMessage);
                    break;
                case MenuScreen.Results:
                    RefreshResults();
                    break;
                case MenuScreen.Grip:
                    _gripScreen.Refresh(_settings, _gripMessage);
                    break;
                case MenuScreen.NewStripPrompt:
                    _newStripScreen.Refresh(_strip.NewStripQuestionDetails);
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
            var canRunAgain = AreSoloDrillsAllowed && BuildPickerEntries().Any(entry => entry.Drill.Id == results.DrillId && entry.IsAvailable);
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

        // Solo Drills wait while in a Partner session (PER-58).
        private bool AreSoloDrillsAllowed => _partner == null || _partner.State.AreSoloDrillsAllowed;

        private string FormatHomeMessage()
        {
            return string.IsNullOrEmpty(_homeMessage) && !AreSoloDrillsAllowed ? PartnerPageText.SoloDrillsOffMessage : _homeMessage;
        }

        /// <summary>The Partner session changed: redraw, and hide the Dummy while in a session.</summary>
        private void OnPartnerChanged()
        {
            if (_dummy != null)
            {
                var isDummyShown = !_partner.State.IsDummyHidden;
                if (_dummy.gameObject.activeSelf != isDummyShown)
                {
                    _dummy.gameObject.SetActive(isDummyShown);
                    Debug.Log(isDummyShown ? "[UI] Dummy shown again: the Partner session ended." : "[UI] Dummy hidden while in a Partner session.");
                }
            }

            _isDirty = true;
        }

        private string FormatStrip()
        {
            return _strip.IsPlaced ? $"Strip: {_strip.Geometry.LengthMeters:0.0} m" : "No Strip placed";
        }

        private void OpenPage(MenuScreen page)
        {
            _navigator.Open(page);
            ClearMessages();
        }

        /// <summary>Shows one settings group's Settings (PER-70); also where the grip page's Back returns to.</summary>
        private void OpenSettingsGroup(SettingGroup group)
        {
            _navigator.OpenSettingsGroup(group);
            ClearMessages();
        }

        private void GoBackFromSettings()
        {
            _navigator.GoBackFromSettings();
            ClearMessages();
        }

        private void ClearMessages()
        {
            _homeMessage = string.Empty;
            _settingsMessage = string.Empty;
            _gripMessage = string.Empty;
            _isDirty = true;
        }

        /// <summary>Starts the thrust calibration for the chosen grip (PER-52): the panels go away and the first target shows.</summary>
        private void StartCalibration()
        {
            if (_cameraRig == null || IsDrillOn || _strip.IsPlacing)
            {
                return;
            }

            var head = _cameraRig.centerEyeAnchor;
            _calibration = new ThrustCalibration(new TrackedPose(head.position.ToNumerics(), head.rotation.ToNumerics()), _settings.Hand);
            _calibrationStartFrame = Time.frameCount;
            _navigator.SetCalibrating(true);
            _isDirty = true;
            Debug.Log($"[Blade] Grip calibration started for {_settings.Blade.Mount}, {_settings.Hand} hand.");
        }

        private void TickCalibration(Transform head, double nowSeconds)
        {
            var offHandTrigger = IsOffHandLeft ? OVRInput.RawButton.LIndexTrigger : OVRInput.RawButton.RIndexTrigger;
            var isCancelled = Time.frameCount > _calibrationStartFrame && OVRInput.GetDown(offHandTrigger, OVRInput.Controller.Touch);
            if (head == null || isCancelled || IsDrillOn || _strip.IsPlacing)
            {
                EndCalibration("Calibration cancelled; nothing changed");
                return;
            }

            var swordController = _settings.Hand == Hand.Left ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
            var swordAnchor = _settings.Hand == Hand.Left ? _cameraRig.leftControllerAnchor : _cameraRig.rightControllerAnchor;
            var headPose = new TrackedPose(head.position.ToNumerics(), head.rotation.ToNumerics());
            if (OVRInput.GetControllerPositionTracked(swordController) && OVRInput.GetControllerOrientationTracked(swordController))
            {
                var controllerPose = new TrackedPose(swordAnchor.position.ToNumerics(), swordAnchor.rotation.ToNumerics());
                if (_calibration.Step(nowSeconds, headPose, controllerPose))
                {
                    Debug.Log($"[Blade] Grip calibration: thrust {_calibration.Captures.Count} of {ThrustCalibration.TargetCount} taken.");
                }
            }

            if (_calibration.IsComplete)
            {
                FinishCalibration();
                return;
            }

            var target = _calibration.Targets[_calibration.CurrentTargetIndex].ToUnity();
            var prompt = GripText.FormatPrompt(_calibration.CurrentTargetIndex, ThrustCalibration.TargetCount, _calibration.IsAimed, _calibration.HoldFraction);
            _calibrationTarget.Show(target, head.position, _calibration.IsAimed, prompt);
        }

        private void FinishCalibration()
        {
            var fit = ThrustCalibration.Fit(_calibration.Captures, _settings.Blade, _settings.Hand);
            var computed = GripGeometry.GetComputedAngles(_settings.Blade.Mount);
            Debug.Log($"[Blade] Grip calibration for {_settings.Blade.Mount}: pitch {fit.Angles.PitchDegrees:0.0} yaw {fit.Angles.YawDegrees:0.0} deg, " +
                $"spread {fit.SpreadDegrees:0.0} deg (computed pitch {computed.PitchDegrees:0} yaw {computed.YawDegrees:0}).");
            // The reach sets the Dummy's distance (PER-92). An implausible one is not saved, so the last one (or height) stays.
            var isReachSaved = fit.ReachMeters.HasValue && DistancePresets.IsPlausibleReach(fit.ReachMeters.Value);
            Debug.Log(fit.ReachMeters.HasValue
                ? $"[Blade] Measured reach {fit.ReachMeters.Value:0.000} m{(isReachSaved ? string.Empty : ": implausible, not saved")}."
                : "[Blade] No reach measured.");
            var changed = SettingsPage.SaveGripAngles(_settings, fit.Angles, fit.ReachMeters);
            ApplyChangedSettings(changed, SettingImpact.Blade | SettingImpact.Dummy);
            EndCalibration(GripText.FormatResult(fit.Angles, fit.SpreadDegrees, isReachSaved ? changed.Blade.GetMeasuredReachMeters() : null));
        }

        private void EndCalibration(string message)
        {
            _calibration = null;
            _calibrationTarget.Hide();
            _navigator.SetCalibrating(false);
            _gripMessage = message;
            _isDirty = true;
        }

        private void ResetGripAngles()
        {
            ApplyChangedSettings(SettingsPage.ResetGripAngles(_settings), SettingImpact.Blade);
            _gripMessage = $"{SettingsPage.FormatMount(_settings.Blade.Mount)} is back on its computed angles";
        }

        /// <summary>
        /// Start: stands the Dummy where the Drill needs it and shows the task line (<see cref="DrillIntro"/>); the run
        /// starts on the runner when the intro is over (<see cref="TickIntro"/>).
        /// </summary>
        private void StartDrill(string drillId)
        {
            var drill = BuildDrills().FirstOrDefault(candidate => candidate.Id == drillId);
            if (drill == null || !_strip.IsPlaced || IsDrillOn || !AreSoloDrillsAllowed)
            {
                return;
            }

            var availability = DrillAvailability.Check(drill, _strip.Geometry.LengthMeters, _settings.ShrinkDrillsToFit);
            if (!ShowAvailability(drill, availability))
            {
                return;
            }

            _intro = new DrillIntro(drill, Time.timeAsDouble);
            if (_dummy != null)
            {
                _dummy.SetDistance(drill.GetDummyDistance(availability.IsShrunk));
            }

            _cueDisplay?.ShowTaskLine(drill.TaskLine);
            _isDirty = true;
            Debug.Log($"[UI] Drill '{drill.Id}' task line up: \"{drill.TaskLine}\".");
        }

        /// <summary>Starts the run once the intro is over; drops the intro if the Strip went away meanwhile.</summary>
        private void TickIntro(double nowSeconds)
        {
            if (_strip.IsPlacing || !_strip.IsPlaced)
            {
                CancelIntro();
                return;
            }

            if (!_intro.IsOver(nowSeconds))
            {
                return;
            }

            var drill = _intro.Drill;
            _intro = null;
            _cueDisplay?.ShowTaskLine(string.Empty);
            var availability = Runner.Start(drill, _strip.Geometry.LengthMeters, _settings.ShrinkDrillsToFit);
            if (!ShowAvailability(drill, availability))
            {
                if (_dummy != null)
                {
                    _dummy.ResetDistance();
                }

                return;
            }

            _lastDrillName = drill.Name;
            Debug.Log($"[UI] Started Drill '{drill.Id}'{(availability.IsShrunk ? " (shortened)" : string.Empty)}.");
        }

        /// <summary>Says on Home why a Drill cannot start; returns whether it can.</summary>
        private bool ShowAvailability(DrillDefinition drill, DrillAvailability availability)
        {
            _homeMessage = availability.IsAvailable ? string.Empty : $"{drill.Name} needs a {availability.RequiredStripLengthMeters:0.0} m Strip";
            _isDirty = true;
            return availability.IsAvailable;
        }

        /// <summary>The Running panel's Cancel: drops a start still showing its task line, or cancels the run.</summary>
        private void CancelDrill()
        {
            if (_intro != null)
            {
                CancelIntro();
                return;
            }

            Runner.Cancel();
        }

        private void CancelIntro()
        {
            Debug.Log($"[UI] Start of Drill '{_intro.Drill.Id}' cancelled before the first Rep.");
            _intro = null;
            _cueDisplay?.ShowTaskLine(string.Empty);
            if (_dummy != null)
            {
                _dummy.ResetDistance();
            }

            _isDirty = true;
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
            // Nothing runs yet while the task line shows; the first Rep starts once it is over.
            if (_intro != null)
            {
                return;
            }

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
            ApplyChangedSettings(SettingsPage.Change(_settings, id, direction), SettingsPage.GetImpact(id));
        }

        /// <summary>Saves changed Settings and hands them to the modules <paramref name="impact"/> names.</summary>
        private void ApplyChangedSettings(TrainerSettings changed, SettingImpact impact)
        {
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

            if ((impact & SettingImpact.Dummy) != 0 && _dummy != null)
            {
                _dummy.Configure(changed.FencerHeightMeters, changed.Hand, changed.Blade.GetMeasuredReachMeters());
            }

            if ((impact & SettingImpact.DummyLook) != 0 && _dummy != null)
            {
                _dummy.SetLook(changed.DummyLook);
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
