using System;
using System.IO;
using UnityEngine;
using VrFencing.Settings;

namespace VrFencing.Strip
{
    /// <summary>
    /// The Strip in the room: two-point placement with the off-hand controller (the Fencer's En Garde Line, then the
    /// Dummy's), the per-room spatial anchor, the markings, and the warnings (Warning Zone buzz and glow, off-strip
    /// signal). Rules and geometry are Core (<see cref="StripPlacement"/>, <see cref="StripGeometry"/>,
    /// <see cref="StripMonitor"/>, <see cref="SavedStrips"/>); this reads the controllers and head, renders, and raises
    /// events for Drills and the UI.
    ///
    /// At launch the Strip saved for this room comes back by itself. In a room with no saved Strip, when another room's
    /// Strip was found, it asks "New Strip here?" (<see cref="IsAskingForNewStrip"/>, answered on the menu with
    /// <see cref="PlaceNewStrip"/> or <see cref="KeepLastStrip"/>); when no Strip was found at all, placement starts.
    ///
    /// <see cref="EnGardeLine"/> is the Strip frame (origin at the Fencer's En Garde Line on the centerline,
    /// +x the Fencer's right, +z toward the Dummy). The Dummy and the markings sit under it, so they move with it.
    ///
    /// Off-hand controls (the left controller for a right-handed Fencer, the right one for a left-handed Fencer):
    /// trigger places the two points, the upper face button (Y, or B) re-places the Strip or cancels re-placing,
    /// the lower face button (X, or A) cycles the off-strip tolerance 0 / 0.2 / 0.4 m and saves it to Settings.
    /// </summary>
    public sealed class StripBehaviour : MonoBehaviour
    {
        private const float ShortMessageSeconds = 3.0f;
        private const string OffStripMessage = "Off the Strip: step back on";

        [SerializeField]
        [Tooltip("The Strip frame. The Dummy and the markings go under it. Created at start if empty.")]
        private Transform _enGardeLine;

        private readonly StripAnchorStore _anchors = new StripAnchorStore();

        private TrainerSettings _settings;
        private OVRCameraRig _cameraRig;
        private StripMarkings _markings;
        private StripStatusText _status;
        private WarningZoneBuzz _buzz;
        private StripMonitor _monitor;
        private StripPlacement _placement;
        private string _placementPrompt;
        private PlacedStrip? _placedStrip;
        private bool _isBusy;
        private SavedStrip _lastStripElsewhere;
        private int _placementStartedFrame = -1;

        /// <summary>The Fencer's head passed an end or a side by more than the off-strip tolerance, or the Strip is being re-placed. Drills pause.</summary>
        public event Action LeftStrip;

        /// <summary>The Fencer is back on the Strip. Drills resume.</summary>
        public event Action BackOnStrip;

        /// <summary>The Fencer stepped into a Warning Zone (after the buzz).</summary>
        public event Action<StripEnd> EnteredWarningZone;

        /// <summary>A Strip is in place, placed now or restored from its anchor. Carries its geometry (for the Drill picker).</summary>
        public event Action<StripGeometry> Placed;

        /// <summary>The Strip changed and saved a Setting itself (the off-strip tolerance button). Carries the saved settings.</summary>
        public event Action<TrainerSettings> SettingsChanged;

        /// <summary>The Strip frame; parent of the Dummy.</summary>
        public Transform EnGardeLine => _enGardeLine;

        /// <summary>The current Strip's geometry, or null before one is placed or restored.</summary>
        public StripGeometry Geometry => _monitor?.Geometry;

        /// <summary>A Strip is in place and not being re-placed.</summary>
        public bool IsPlaced => _monitor != null && _placement == null;

        /// <summary>Two-point placement is running: the off-hand trigger belongs to it.</summary>
        public bool IsPlacing => _placement != null;

        /// <summary>
        /// "New Strip here?" waits for an answer (PER-91): no saved Strip is in this room, but another room's Strip was
        /// found. The menu shows the question; <see cref="PlaceNewStrip"/> or <see cref="KeepLastStrip"/> answers it.
        /// </summary>
        public bool IsAskingForNewStrip => _lastStripElsewhere != null;

        /// <summary>What the "New Strip here?" question tells the Fencer about the choice.</summary>
        public string NewStripQuestionDetails =>
            "No Strip is saved for this room yet.\n" +
            "Place a new one for this room (your other rooms keep theirs),\nor keep using the last Strip where it is.";

        /// <summary>The off-strip tolerance in use.</summary>
        public float OffStripToleranceMeters => _settings?.WarningZone.OffStripToleranceMeters ?? 0.0f;

        /// <summary>False between <see cref="LeftStrip"/> and <see cref="BackOnStrip"/>.</summary>
        public bool IsFencerOnStrip { get; private set; } = true;

        /// <summary>The Warning Zone the Fencer is in, or null.</summary>
        public StripEnd? CurrentWarningZone => IsPlaced ? _monitor.CurrentWarningZone : null;

        /// <summary>Starts two-point placement (also from a UI button later). Ignored while the anchor is being restored or saved.</summary>
        public void BeginPlacement(string reason)
        {
            if (_isBusy || _placement != null)
            {
                return;
            }

            var buttons = OffHandButtons.For(_settings.Hand);
            _placement = new StripPlacement();
            _lastStripElsewhere = null;

            // A UI button may start placement with the same trigger press; that press must not set the En Garde Line.
            _placementStartedFrame = Time.frameCount;
            _placementPrompt =
                $"{reason}\nStand en garde on your En Garde Line.\n" +
                $"Hold the {buttons.SideName} controller over your front foot and pull its trigger." +
                (_placedStrip.HasValue ? $"\n({buttons.ReplaceButtonName}: keep the old Strip)" : string.Empty);
            _markings.SetVisible(false);
            SetFencerOnStrip(false);
            Debug.Log($"[Strip] Placement started: {reason}");
        }

        /// <summary>Answers "New Strip here?" with a new Strip: placement starts, and the Strip is saved as this room's.</summary>
        public void PlaceNewStrip()
        {
            if (!IsAskingForNewStrip)
            {
                return;
            }

            Debug.Log("[Strip] New Strip here: placing one for this room.");
            BeginPlacement("New room: let's place its Strip.");
        }

        /// <summary>
        /// Answers "New Strip here?" by keeping the last Strip, found in another room, where it is. It stays that room's
        /// Strip; the question comes back at the next launch here.
        /// </summary>
        public void KeepLastStrip()
        {
            var kept = _lastStripElsewhere;
            if (kept == null)
            {
                return;
            }

            _lastStripElsewhere = null;
            if (!_anchors.BindLastStrip())
            {
                BeginPlacement("The last Strip is gone. Let's place one here.");
                return;
            }

            Debug.Log("[Strip] New Strip here: keeping the last Strip.");
            UseSavedStrip(kept, "Kept the last Strip");
        }

        /// <summary>Moves the off-strip tolerance to its next step (0, 0.2, 0.4 m) and saves it to Settings.</summary>
        public void CycleOffStripTolerance()
        {
            var settings = SettingsFile.Load();
            settings.WarningZone.OffStripToleranceMeters = OffStripToleranceSteps.GetNext(_settings.WarningZone.OffStripToleranceMeters);
            _settings = settings;
            var tolerance = settings.WarningZone.OffStripToleranceMeters;
            try
            {
                SettingsFile.Save(settings);
                Debug.Log($"[Strip] Off-strip tolerance set to {tolerance:0.0} m and saved.");
                _status.ShowShortMessage($"Off-strip tolerance: {tolerance:0.0} m", Time.timeAsDouble, ShortMessageSeconds);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Debug.LogError($"[Strip] Off-strip tolerance set to {tolerance:0.0} m, but saving Settings failed: {error.Message}");
                _status.ShowShortMessage($"Off-strip tolerance: {tolerance:0.0} m (not saved)", Time.timeAsDouble, ShortMessageSeconds);
            }

            SettingsChanged?.Invoke(settings.Clone());
        }

        /// <summary>
        /// Uses changed Settings (from the settings screen): sword hand for the off-hand buttons, and height, Warning
        /// Zone and tolerance for the geometry and warnings, rebuilt around the same two En Garde Lines.
        /// </summary>
        public void ApplySettings(TrainerSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            _settings = settings.Clone();
            if (_placement == null && _placedStrip.HasValue && _markings != null)
            {
                UseStrip(_placedStrip.Value);
            }
        }

        /// <summary>
        /// Draws the Strip's two En Garde Lines where a Partner bout puts them (this Fencer's and the opponent's, z in
        /// the Strip frame, PER-83), or back on the Strip's own with null. Only the drawn lines move: the Strip frame,
        /// its ends and its warnings stay as placed.
        /// </summary>
        public void ShowBoutEnGardeLines((float FencerZ, float OpponentZ)? lines)
        {
            _markings?.MoveEnGardeLines(lines);
        }

        private void Awake()
        {
            if (_enGardeLine == null)
            {
                _enGardeLine = new GameObject("En Garde Line").transform;
                _enGardeLine.SetParent(transform, false);
            }
        }

        private void Start()
        {
            _settings = SettingsFile.Load();
            _cameraRig = FindFirstObjectByType<OVRCameraRig>();
            if (_cameraRig == null)
            {
                Debug.LogError("[Strip] No OVRCameraRig in the scene; the Strip cannot read the head or controllers.");
            }

            _markings = new StripMarkings(_enGardeLine);
            _markings.SetVisible(false);
            _status = new StripStatusText();
            _buzz = new WarningZoneBuzz();
            RestoreOnLaunch();
        }

        private void Update()
        {
            var nowSeconds = Time.timeAsDouble;
            var buttons = OffHandButtons.For(_settings.Hand);

            if (OVRInput.GetDown(buttons.ToleranceButton, OVRInput.Controller.Touch))
            {
                CycleOffStripTolerance();
            }

            if (OVRInput.GetDown(buttons.ReplaceButton, OVRInput.Controller.Touch))
            {
                if (_placement != null)
                {
                    CancelPlacement();
                }
                else
                {
                    BeginPlacement("Re-placing the Strip.");
                }
            }

            if (_placement != null)
            {
                UpdatePlacement(buttons);
            }
            else if (_monitor != null)
            {
                UpdateWarnings(nowSeconds);
            }

            _status.SetStandingMessage(GetStandingMessage());
            _status.Tick(_cameraRig != null ? _cameraRig.centerEyeAnchor : null, nowSeconds, Time.deltaTime);
        }

        private void LateUpdate()
        {
            // The anchor keeps the Strip fixed to the room as the headset's tracking corrects itself.
            var anchor = _anchors.AnchorTransform;
            if (_placement == null && anchor != null)
            {
                _enGardeLine.SetPositionAndRotation(anchor.position, anchor.rotation);
            }
        }

        private void OnDestroy()
        {
            _buzz?.Dispose();
        }

        private async void RestoreOnLaunch()
        {
            _isBusy = true;
            var choice = new StripRoomChoice(StripRoomMatch.NotFound, null);
            try
            {
                choice = await _anchors.RestoreAsync(GetFencerOnFloor);
            }
            catch (Exception error)
            {
                Debug.LogError($"[Strip] Restoring the Strip failed: {error}");
            }
            finally
            {
                _isBusy = false;
            }

            if (this == null)
            {
                return;
            }

            switch (choice.Match)
            {
                case StripRoomMatch.ThisRoom:
                    UseSavedStrip(choice.Strip, "Strip restored");
                    break;

                case StripRoomMatch.OtherRoom:
                    _lastStripElsewhere = choice.Strip;
                    break;

                default:
                    BeginPlacement("No Strip found in this room. Let's place it.");
                    break;
            }
        }

        /// <summary>Uses a saved Strip whose anchor is bound: the Strip frame follows the anchor, the gap comes from the saved Strip.</summary>
        private void UseSavedStrip(SavedStrip saved, string message)
        {
            var anchor = _anchors.AnchorTransform;
            _enGardeLine.SetPositionAndRotation(anchor.position, anchor.rotation);
            var gap = saved.GetEnGardeGapMeters(_settings.FencerHeightMeters, _settings.WarningZone);
            if (UseStrip(new PlacedStrip(GetEnGardeLinePose(), gap)))
            {
                _status.ShowShortMessage($"{message}: {Geometry.LengthMeters:0.0} m", Time.timeAsDouble, ShortMessageSeconds);
            }
        }

        /// <summary>The head dropped to the floor (floor-level tracking: y = 0), read once the saved anchors are found.</summary>
        private Vector3 GetFencerOnFloor()
        {
            var head = _cameraRig != null ? _cameraRig.centerEyeAnchor : null;
            return head != null ? new Vector3(head.position.x, 0.0f, head.position.z) : Vector3.zero;
        }

        private void UpdatePlacement(OffHandButtons buttons)
        {
            var controller = GetOffHandController(buttons);
            if (controller == null)
            {
                return;
            }

            var point = controller.position.ToNumerics();
            var preview = _placement.Preview(point);
            if (preview.HasValue && TryCreateGeometry(preview.Value.PlacedLengthMeters, out var previewGeometry))
            {
                SetEnGardeLinePose(preview.Value.EnGardeLine);
                _markings.Build(previewGeometry);
                _markings.SetVisible(true);
            }
            else
            {
                _markings.SetVisible(false);
            }

            if (Time.frameCount == _placementStartedFrame || !OVRInput.GetDown(buttons.PlaceButton, OVRInput.Controller.Touch))
            {
                return;
            }

            switch (_placement.Press(point))
            {
                case PlacementPressOutcome.EnGardeLineSet:
                    _placementPrompt =
                        "Your En Garde Line is set.\nNow the Dummy's: walk to where its front foot goes, " +
                        $"hold the {buttons.SideName} controller over that spot and pull the trigger.";
                    Debug.Log($"[Strip] En Garde Line point set at {_placement.EnGardePointOnFloor}.");
                    break;

                case PlacementPressOutcome.TooClose:
                    _status.ShowShortMessage(
                        $"Too close: the Dummy's En Garde Line must be at least {StripGeometry.MinimumPlacedLengthMeters:0.0} m from yours.",
                        Time.timeAsDouble,
                        ShortMessageSeconds);
                    break;

                case PlacementPressOutcome.Placed:
                    FinishPlacement(_placement.Result.Value);
                    break;
            }
        }

        private void FinishPlacement(PlacedStrip placed)
        {
            _placement = null;
            SetEnGardeLinePose(placed.EnGardeLine);
            if (!UseStrip(placed))
            {
                return;
            }

            Debug.Log($"[Strip] Placed: En Garde Lines {placed.PlacedLengthMeters:0.00} m apart, {Geometry.LengthMeters:0.00} m end to end.");
            _status.ShowShortMessage($"Strip placed: {Geometry.LengthMeters:0.0} m. Saving...", Time.timeAsDouble, ShortMessageSeconds);
            SaveAnchor(placed);
        }

        private void CancelPlacement()
        {
            if (!_placedStrip.HasValue)
            {
                return;
            }

            _placement = null;
            Debug.Log("[Strip] Re-placing cancelled; keeping the previous Strip.");
            UseStrip(_placedStrip.Value);
            SetEnGardeLinePose(_placedStrip.Value.EnGardeLine);
        }

        private async void SaveAnchor(PlacedStrip placed)
        {
            _isBusy = true;
            var isSaved = false;
            try
            {
                isSaved = await _anchors.SaveAsync(placed, new Pose(_enGardeLine.position, _enGardeLine.rotation));
            }
            catch (Exception error)
            {
                Debug.LogError($"[Strip] Saving the Strip anchor failed: {error}");
            }
            finally
            {
                _isBusy = false;
            }

            if (this == null)
            {
                return;
            }

            _status.ShowShortMessage(
                isSaved ? "Strip saved for this room." : "Strip placed, but not saved (see log). It is used until the app closes.",
                Time.timeAsDouble,
                ShortMessageSeconds);
        }

        /// <summary>Makes <paramref name="placed"/> the current Strip: geometry, warnings and markings. False if its length is invalid.</summary>
        private bool UseStrip(PlacedStrip placed)
        {
            if (!TryCreateGeometry(placed.PlacedLengthMeters, out var geometry))
            {
                BeginPlacement("The saved Strip is too short. Let's place it again.");
                return false;
            }

            if (_monitor != null)
            {
                _monitor.EnteredWarningZone -= OnEnteredWarningZone;
            }

            _monitor = new StripMonitor(geometry);
            _monitor.EnteredWarningZone += OnEnteredWarningZone;
            _placedStrip = placed;
            _markings.Build(geometry);
            _markings.SetVisible(true);
            Placed?.Invoke(geometry);
            return true;
        }

        private void UpdateWarnings(double nowSeconds)
        {
            var head = _cameraRig != null ? _cameraRig.centerEyeAnchor : null;
            if (head == null)
            {
                return;
            }

            var headInStrip = StripFrame.ToStripFrame(GetEnGardeLinePose(), head.position.ToNumerics());
            _monitor.Update(headInStrip, _settings.WarningZone.OffStripToleranceMeters);
            SetFencerOnStrip(_monitor.IsOnStrip);
            _markings.ShowGlow(_monitor.CurrentWarningZone, nowSeconds);
        }

        private void OnEnteredWarningZone(StripEnd end)
        {
            _buzz.Play();
            EnteredWarningZone?.Invoke(end);
        }

        private void SetFencerOnStrip(bool isOnStrip)
        {
            if (IsFencerOnStrip == isOnStrip)
            {
                return;
            }

            IsFencerOnStrip = isOnStrip;
            Debug.Log(isOnStrip ? "[Strip] Fencer back on the Strip." : "[Strip] Fencer off the Strip.");
            if (isOnStrip)
            {
                BackOnStrip?.Invoke();
            }
            else
            {
                LeftStrip?.Invoke();
            }
        }

        private string GetStandingMessage()
        {
            if (_placement != null)
            {
                return _placementPrompt;
            }

            if (_isBusy && _monitor == null)
            {
                return "Looking for your Strip...";
            }

            if (IsAskingForNewStrip)
            {
                return "New Strip here? Answer on the menu.";
            }

            return IsPlaced && !IsFencerOnStrip ? OffStripMessage : string.Empty;
        }

        private bool TryCreateGeometry(float placedLengthMeters, out StripGeometry geometry)
        {
            try
            {
                geometry = StripGeometry.Create(placedLengthMeters, _settings.FencerHeightMeters, _settings.WarningZone);
                return true;
            }
            catch (ArgumentOutOfRangeException error)
            {
                if (placedLengthMeters >= StripGeometry.MinimumPlacedLengthMeters)
                {
                    // The length is fine, so the Fencer height in the settings file is not: fall back to the default height.
                    Debug.LogWarning($"[Strip] {error.Message} Using the default Fencer height.");
                    geometry = StripGeometry.Create(placedLengthMeters, new TrainerSettings().FencerHeightMeters, _settings.WarningZone);
                    return true;
                }

                geometry = null;
                return false;
            }
        }

        private Transform GetOffHandController(OffHandButtons buttons)
        {
            if (_cameraRig == null)
            {
                return null;
            }

            return buttons.IsLeftController ? _cameraRig.leftControllerAnchor : _cameraRig.rightControllerAnchor;
        }

        private TrackedPose GetEnGardeLinePose()
        {
            return new TrackedPose(_enGardeLine.position.ToNumerics(), _enGardeLine.rotation.ToNumerics());
        }

        private void SetEnGardeLinePose(TrackedPose pose)
        {
            _enGardeLine.SetPositionAndRotation(pose.Position.ToUnity(), pose.Rotation.ToUnity());
        }

        /// <summary>The off-hand controller's buttons: the hand that does not hold the blade.</summary>
        private readonly struct OffHandButtons
        {
            private OffHandButtons(bool isLeftController)
            {
                IsLeftController = isLeftController;
            }

            public bool IsLeftController { get; }

            public string SideName => IsLeftController ? "left" : "right";

            public OVRInput.RawButton PlaceButton => IsLeftController ? OVRInput.RawButton.LIndexTrigger : OVRInput.RawButton.RIndexTrigger;

            public OVRInput.RawButton ToleranceButton => IsLeftController ? OVRInput.RawButton.X : OVRInput.RawButton.A;

            public OVRInput.RawButton ReplaceButton => IsLeftController ? OVRInput.RawButton.Y : OVRInput.RawButton.B;

            public string ReplaceButtonName => IsLeftController ? "Y" : "B";

            public static OffHandButtons For(Hand swordHand)
            {
                return new OffHandButtons(isLeftController: swordHand == Hand.Right);
            }
        }
    }
}
