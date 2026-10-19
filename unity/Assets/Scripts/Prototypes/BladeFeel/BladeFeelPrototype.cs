// PER-22 on-device prototype: a virtual épée on the right Touch controller, a few
// static Targets, swept tip Touch detection, haptics, live preset switching and a
// JSONL rep log. Throwaway code; the Touch math it calls (VrFencing.Blade) is not.
//
// Spawns itself into any scene that has an OVRCameraRig, so the scene file stays as is.
//
// Controls:
//   Left X            cycle blade mount preset
//   Left Y            cycle haptic preset
//   Left stick click  toggle Touch rule strict / loose
//   Left stick up/dn  blade pitch +/- 5 degrees (-90..+90)
//   Left stick l/r    blade yaw -/+ 5 degrees (-90..+90)
//   Mount, pitch and yaw persist in persistentDataPath/blade-tuning.json.
//   Left trigger      re-place the Targets in front of where the Fencer stands now
//   Right A / B       preview the Touch / flat haptic pattern
//   Right stick click toggle haptic backend (Unity XR impulse / Meta Haptics SDK)
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VrFencing.Blade;

namespace VrFencing.Prototypes.BladeFeel
{
    public sealed class BladeFeelPrototype : MonoBehaviour
    {
        private const float StatsWindowSeconds = 5f;
        private const float FlatHapticCooldownSeconds = 0.15f;
        private const string LogTag = "[BladeFeel]";
        private const string LitMaterialResourcePath = "BladeFeel/BladeFeelLit";

        private OVRCameraRig _cameraRig;
        private Transform _bladeMount;
        private Transform _tipMarker;
        private Renderer _tipRenderer;
        private Text _hudText;
        private Transform _hud;
        private Material _litMaterial;

        private readonly List<Renderer> _targetRenderers = new();
        private readonly List<float> _targetFlashEndTimes = new();
        private TouchDetector _detector;
        private bool _isPlacementPending;
        private bool _isPlacementForced;
        private bool _hasPreviousBlade;
        private BladeSegment _previousBlade;

        private BladeTuning _tuning;
        private int _hapticPresetIndex;
        private bool _isStrict = true;

        private ControllerHapticPlayer _haptics;
        private float _lastFlatHapticTime = float.NegativeInfinity;
        private RepLog _repLog;
        private float _tipFlashEndTime;

        private int _touchCount;
        private int _flatCount;
        private int _glanceCount;
        private string _lastEventText = "-";

        private float _statsWindowStart;
        private float _statsMaxFrameSeconds;
        private float _statsMaxTipSpeed;
        private int _statsFrames;
        private readonly List<float> _statsFrameSeconds = new();

        private const float JitterWindowSeconds = 1f;
        // Below this mean tip speed over the jitter window the hand counts as held still.
        private const float StillTipSpeedMetersPerSecond = 0.05f;
        private readonly Queue<JitterSample> _jitterSamples = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void SpawnIntoScene()
        {
            var cameraRig = FindFirstObjectByType<OVRCameraRig>();
            if (cameraRig == null)
                return;
            new GameObject("BladeFeelPrototype").AddComponent<BladeFeelPrototype>()._cameraRig = cameraRig;
        }

        private void Start()
        {
            _litMaterial = LoadLitMaterial();
            _tuning = BladeTuningStore.Load();
            _tuning.mountIndex = Mathf.Clamp(_tuning.mountIndex, 0, BladeFeelPresets.BladeMounts.Length - 1);
            _haptics = new ControllerHapticPlayer();
            _repLog = new RepLog("reps.jsonl");
            _repLog.Write("time", System.DateTime.UtcNow.ToString("o"), "event", "session_start",
                "display_hz", OVRPlugin.systemDisplayFrequency);
            BuildBlade(_cameraRig.rightControllerAnchor);
            BuildHud(_cameraRig.leftControllerAnchor);
            ApplyBladeMount();
            _isPlacementPending = true; // Placed on the first frame with a tracked head.
            // One Touch pulse shortly after launch: the log shows whether the haptics call works
            // without anyone pressing a button, and the Fencer feels whether it arrives.
            Invoke(nameof(PlayStartupHapticCheck), 2f);
            _statsWindowStart = Time.unscaledTime;
        }

        private void PlayStartupHapticCheck()
        {
            Debug.Log($"{LogTag} startup haptic check on {_haptics.Backend}");
            _haptics.Play(CurrentHaptics().Touch, Time.unscaledTime);
        }

        private void OnDestroy()
        {
            _haptics?.Dispose();
            _repLog?.Dispose();
        }

        private void Update()
        {
            HandleInput();
            _haptics.Tick(Time.unscaledTime);
        }

        // Anchors are updated by OVRCameraRig during Update; read the blade after that.
        private void LateUpdate()
        {
            var now = Time.unscaledTime;
            if (_isPlacementPending && (_isPlacementForced || IsHeadTracked()))
            {
                PlaceTargets();
                _isPlacementPending = false;
                _isPlacementForced = false;
            }

            var blade = new BladeSegment(_bladeMount.position, _tipMarker.position);
            var tipSpeed = 0f;
            if (_hasPreviousBlade && _detector != null)
            {
                var deltaTime = Time.unscaledDeltaTime;
                tipSpeed = Vector3.Distance(_previousBlade.TipPosition, blade.TipPosition) / Mathf.Max(deltaTime, 1e-4f);
                foreach (var bladeEvent in _detector.Step(_previousBlade, blade, deltaTime, CurrentRules()))
                    HandleBladeEvent(bladeEvent, now);
            }

            _previousBlade = blade;
            _hasPreviousBlade = true;

            UpdateFlashes(now);
            UpdateHud();
            UpdateStats(now, tipSpeed);
        }

        private static bool IsHeadTracked() =>
            OVRPlugin.GetNodePositionTracked(OVRPlugin.Node.Head)
            && OVRPlugin.GetNodeOrientationTracked(OVRPlugin.Node.Head);

        private TouchRules CurrentRules() => _isStrict ? TouchRules.Strict : TouchRules.Loose;

        private void HandleInput()
        {
            const OVRInput.Controller Left = OVRInput.Controller.LTouch;
            const OVRInput.Controller Right = OVRInput.Controller.RTouch;

            if (OVRInput.GetDown(OVRInput.Button.One, Left))
            {
                _tuning.mountIndex = (_tuning.mountIndex + 1) % BladeFeelPresets.BladeMounts.Length;
                OnBladeTuningChanged("blade_mount");
            }

            if (OVRInput.GetDown(OVRInput.Button.Two, Left))
            {
                _hapticPresetIndex = (_hapticPresetIndex + 1) % BladeFeelPresets.Haptics.Length;
                _haptics.Play(CurrentHaptics().Touch, Time.unscaledTime);
                LogSetting("haptic_preset");
            }

            if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick, Left))
            {
                _isStrict = !_isStrict;
                LogSetting("touch_rule");
            }

            // Pitch is shown as "up positive"; Unity's +x rotation tips the blade down, hence the sign flip in ApplyBladeMount.
            if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstickUp, Left))
                NudgeAngle(ref _tuning.pitchDegrees, BladeFeelPresets.AngleStepDegrees, "blade_pitch");
            if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstickDown, Left))
                NudgeAngle(ref _tuning.pitchDegrees, -BladeFeelPresets.AngleStepDegrees, "blade_pitch");
            if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstickRight, Left))
                NudgeAngle(ref _tuning.yawDegrees, BladeFeelPresets.AngleStepDegrees, "blade_yaw");
            if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstickLeft, Left))
                NudgeAngle(ref _tuning.yawDegrees, -BladeFeelPresets.AngleStepDegrees, "blade_yaw");

            if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, Left))
            {
                // The Fencer pressed it while wearing the headset, so place now even if the tracked flag lags.
                _isPlacementPending = true;
                _isPlacementForced = true;
            }

            if (OVRInput.GetDown(OVRInput.Button.One, Right))
                _haptics.Play(CurrentHaptics().Touch, Time.unscaledTime);
            if (OVRInput.GetDown(OVRInput.Button.Two, Right))
                _haptics.Play(CurrentHaptics().Flat, Time.unscaledTime);
            if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick, Right))
            {
                _haptics.ToggleBackend();
                _haptics.Play(CurrentHaptics().Touch, Time.unscaledTime);
                LogSetting("haptic_backend");
            }
        }

        private void NudgeAngle(ref float angleDegrees, float deltaDegrees, string changed)
        {
            angleDegrees = Mathf.Clamp(angleDegrees + deltaDegrees, -BladeFeelPresets.MaxAngleDegrees,
                BladeFeelPresets.MaxAngleDegrees);
            OnBladeTuningChanged(changed);
        }

        private void OnBladeTuningChanged(string changed)
        {
            ApplyBladeMount();
            BladeTuningStore.Save(_tuning);
            LogSetting(changed);
        }

        private HapticPreset CurrentHaptics() => BladeFeelPresets.Haptics[_hapticPresetIndex];
        private BladeMountPreset CurrentMount() => BladeFeelPresets.BladeMounts[_tuning.mountIndex];

        private void ApplyBladeMount()
        {
            _bladeMount.localPosition = new Vector3(0f, 0f, CurrentMount().GuardForwardMeters);
            _bladeMount.localRotation = Quaternion.Euler(-_tuning.pitchDegrees, _tuning.yawDegrees, 0f);
            // The blade jumped; do not sweep the jump as a motion.
            _hasPreviousBlade = false;
        }

        private void HandleBladeEvent(BladeEvent bladeEvent, float now)
        {
            var targetId = _detector.Targets[bladeEvent.TargetIndex].Id;
            switch (bladeEvent.Type)
            {
                case BladeEventType.Touch:
                    _touchCount++;
                    _haptics.Play(CurrentHaptics().Touch, now);
                    Flash(bladeEvent.TargetIndex, BladeFeelPresets.TouchColor, now);
                    _tipFlashEndTime = now + BladeFeelPresets.FlashSeconds;
                    break;
                case BladeEventType.FlatContact:
                    _flatCount++;
                    if (now - _lastFlatHapticTime >= FlatHapticCooldownSeconds)
                    {
                        _haptics.Play(CurrentHaptics().Flat, now);
                        _lastFlatHapticTime = now;
                    }

                    Flash(bladeEvent.TargetIndex, BladeFeelPresets.FlatColor, now);
                    break;
                case BladeEventType.TipGlance:
                    // No haptic on a miss; a faint blue flash only so the log can be matched to what was seen.
                    _glanceCount++;
                    Flash(bladeEvent.TargetIndex, BladeFeelPresets.GlanceColor, now);
                    break;
            }

            var eventName = EventName(bladeEvent.Type);
            _lastEventText = $"{eventName} {targetId} {bladeEvent.TipSpeedMetersPerSecond:0.0} m/s" +
                             (float.IsNaN(bladeEvent.ApproachAngleDegrees)
                                 ? ""
                                 : $" {bladeEvent.ApproachAngleDegrees:0}°/{bladeEvent.BladeAxisAngleDegrees:0}°");
            Debug.Log($"{LogTag} {_lastEventText}");
            _repLog.Write(
                "time", System.DateTime.UtcNow.ToString("o"),
                "t_s", now,
                "event", eventName,
                "target", targetId,
                "tip_speed_mps", bladeEvent.TipSpeedMetersPerSecond,
                "approach_angle_deg", bladeEvent.ApproachAngleDegrees,
                "blade_axis_angle_deg", bladeEvent.BladeAxisAngleDegrees,
                "blade_mount", CurrentMount().Name,
                "guard_forward_m", CurrentMount().GuardForwardMeters,
                "blade_pitch_deg", _tuning.pitchDegrees, "blade_yaw_deg", _tuning.yawDegrees,
                "haptic_preset", CurrentHaptics().Name, "haptic_backend", _haptics.Backend.ToString(),
                "touch_rule", _isStrict ? "strict" : "loose");
        }

        private static string EventName(BladeEventType type) => type switch
        {
            BladeEventType.Touch => "touch",
            BladeEventType.FlatContact => "flat",
            _ => "glance",
        };

        private void LogSetting(string changed)
        {
            Debug.Log($"{LogTag} setting {changed}: {DescribeSettings().Replace("\n", " | ")}");
            _repLog.Write("time", System.DateTime.UtcNow.ToString("o"), "event", "setting", "changed", changed,
                "blade_mount", CurrentMount().Name, "blade_pitch_deg", _tuning.pitchDegrees, "blade_yaw_deg", _tuning.yawDegrees,
                "haptic_preset", CurrentHaptics().Name, "haptic_backend", _haptics.Backend.ToString(),
                "touch_rule", _isStrict ? "strict" : "loose");
        }

        // ---- Targets ----

        private void PlaceTargets()
        {
            foreach (var targetRenderer in _targetRenderers)
                Destroy(targetRenderer.gameObject);
            _targetRenderers.Clear();
            _targetFlashEndTimes.Clear();

            // Everything in world space: head pose from the centre-eye anchor, floor = world y of the
            // floor-level tracking space.
            var head = _cameraRig.centerEyeAnchor;
            var floorY = _cameraRig.trackingSpace.position.y;
            var targets = TargetPlacement.PlaceTargets(head.position, head.rotation, floorY, BladeFeelPresets.Targets);

            foreach (var target in targets)
            {
                var sphere = CreatePrimitive(PrimitiveType.Sphere, $"Target_{target.Id}", null,
                    BladeFeelPresets.TargetIdleColor);
                sphere.position = target.Center;
                sphere.localScale = Vector3.one * (2f * target.Radius);
                _targetRenderers.Add(sphere.GetComponent<Renderer>());
                _targetFlashEndTimes.Add(0f);
            }

            _detector = new TouchDetector(targets);
            _hasPreviousBlade = false;
            Debug.Log($"{LogTag} Targets placed: head {head.position} forward {head.forward} " +
                      $"facing {TargetPlacement.FindFacing(head.rotation)} torso at {targets[2].Center}");
        }

        private void Flash(int targetIndex, Color color, float now)
        {
            SetColor(_targetRenderers[targetIndex], color);
            _targetFlashEndTimes[targetIndex] = now + BladeFeelPresets.FlashSeconds;
        }

        private void UpdateFlashes(float now)
        {
            for (var index = 0; index < _targetRenderers.Count; index++)
            {
                if (_targetFlashEndTimes[index] > 0f && now >= _targetFlashEndTimes[index])
                {
                    SetColor(_targetRenderers[index], BladeFeelPresets.TargetIdleColor);
                    _targetFlashEndTimes[index] = 0f;
                }
            }

            SetColor(_tipRenderer, now < _tipFlashEndTime ? BladeFeelPresets.TouchColor : BladeFeelPresets.TipColor);
        }

        // ---- Blade, HUD, visuals ----

        private void BuildBlade(Transform controllerAnchor)
        {
            _bladeMount = new GameObject("BladeMount").transform;
            _bladeMount.SetParent(controllerAnchor, false);

            // Unity's cylinder primitive is 2 m tall along its local y; rotate it onto z (the blade axis).
            var alongBlade = Quaternion.Euler(90f, 0f, 0f);

            var guard = CreatePrimitive(PrimitiveType.Cylinder, "Guard", _bladeMount, BladeFeelPresets.BladeColor);
            guard.localRotation = alongBlade;
            guard.localPosition = new Vector3(0f, 0f, -BladeFeelPresets.GuardDepthMeters / 2f);
            guard.localScale = new Vector3(BladeFeelPresets.GuardDiameterMeters, BladeFeelPresets.GuardDepthMeters / 2f,
                BladeFeelPresets.GuardDiameterMeters);

            var blade = CreatePrimitive(PrimitiveType.Cylinder, "Blade", _bladeMount, BladeFeelPresets.BladeColor);
            blade.localRotation = alongBlade;
            blade.localPosition = new Vector3(0f, 0f, BladeFeelPresets.BladeLengthMeters / 2f);
            blade.localScale = new Vector3(BladeFeelPresets.BladeVisualDiameterMeters,
                BladeFeelPresets.BladeLengthMeters / 2f, BladeFeelPresets.BladeVisualDiameterMeters);

            _tipMarker = CreatePrimitive(PrimitiveType.Sphere, "Tip", _bladeMount, BladeFeelPresets.TipColor);
            _tipMarker.localPosition = new Vector3(0f, 0f, BladeFeelPresets.BladeLengthMeters);
            _tipMarker.localScale = Vector3.one * BladeFeelPresets.TipMarkerDiameterMeters;
            _tipRenderer = _tipMarker.GetComponent<Renderer>();
        }

        private void BuildHud(Transform leftControllerAnchor)
        {
            const float MetersPerCanvasUnit = 0.0005f;
            _hud = new GameObject("PresetHud", typeof(RectTransform), typeof(Canvas)).transform;
            _hud.SetParent(leftControllerAnchor, false);
            _hud.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var hudRect = (RectTransform)_hud;
            hudRect.sizeDelta = new Vector2(420f, 220f);
            hudRect.localPosition = new Vector3(0f, 0.12f, 0.02f);
            hudRect.localScale = Vector3.one * MetersPerCanvasUnit;

            var textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(_hud, false);
            var textRect = (RectTransform)textObject.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            _hudText = textObject.GetComponent<Text>();
            _hudText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _hudText.fontSize = 22;
            _hudText.color = Color.white;
            _hudText.alignment = TextAnchor.LowerLeft;
            _hudText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _hudText.verticalOverflow = VerticalWrapMode.Overflow;
        }

        private string DescribeSettings()
        {
            var rules = CurrentRules();
            return $"Blade: {CurrentMount().Name}\n" +
                   $"Pitch {_tuning.pitchDegrees:+0;-0;0}° (up +)  Yaw {_tuning.yawDegrees:+0;-0;0}° (right +)\n" +
                   $"Haptics: {CurrentHaptics().Name} via {_haptics.Backend}\n" +
                   $"Rule: {(_isStrict ? "STRICT" : "LOOSE")} (motion <={rules.MaxApproachAngleDegrees:0}°, " +
                   $"blade <={rules.MaxBladeAxisAngleDegrees:0}°)";
        }

        private void UpdateHud()
        {
            _hudText.text = DescribeSettings() +
                            $"\nTouch {_touchCount}  Flat {_flatCount}  Glance {_glanceCount}\nLast: {_lastEventText}";
            // Face the Fencer's eyes.
            var toHud = _hud.position - _cameraRig.centerEyeAnchor.position;
            if (toHud.sqrMagnitude > 1e-6f)
                _hud.rotation = Quaternion.LookRotation(toHud, Vector3.up);
        }

        private void UpdateStats(float now, float tipSpeed)
        {
            _statsFrames++;
            _statsFrameSeconds.Add(Time.unscaledDeltaTime);
            _statsMaxFrameSeconds = Mathf.Max(_statsMaxFrameSeconds, Time.unscaledDeltaTime);
            _statsMaxTipSpeed = Mathf.Max(_statsMaxTipSpeed, tipSpeed);
            RecordJitterSample(now);
            var elapsed = now - _statsWindowStart;
            if (elapsed < StatsWindowSeconds)
                return;

            _statsFrameSeconds.Sort();
            var p95FrameSeconds = _statsFrameSeconds[Mathf.Min(_statsFrameSeconds.Count - 1,
                Mathf.FloorToInt(0.95f * _statsFrameSeconds.Count))];
            Debug.Log($"{LogTag} stats window={elapsed:0.0}s frames={_statsFrames} fps={_statsFrames / elapsed:0.0} " +
                      $"p95_frame_ms={p95FrameSeconds * 1000f:0.0} max_frame_ms={_statsMaxFrameSeconds * 1000f:0.0} " +
                      $"max_tip_speed_mps={_statsMaxTipSpeed:0.00} display_hz={OVRPlugin.systemDisplayFrequency:0} " +
                      $"head_tracked={IsHeadTracked()} targets_placed={_detector != null}");
            LogJitter();
            _statsWindowStart = now;
            _statsFrames = 0;
            _statsFrameSeconds.Clear();
            _statsMaxFrameSeconds = 0f;
            _statsMaxTipSpeed = 0f;
        }

        // ---- Jitter: how much the blade moves over the last second while the hand is roughly still ----

        private readonly struct JitterSample
        {
            public readonly float Time;
            public readonly Vector3 TipPosition;
            public readonly Vector3 ControllerPosition;
            public readonly float AngularSpeedDegreesPerSecond;

            public JitterSample(float time, Vector3 tipPosition, Vector3 controllerPosition, float angularSpeed)
            {
                Time = time;
                TipPosition = tipPosition;
                ControllerPosition = controllerPosition;
                AngularSpeedDegreesPerSecond = angularSpeed;
            }
        }

        private void RecordJitterSample(float now)
        {
            var angularVelocity = OVRInput.GetLocalControllerAngularVelocity(OVRInput.Controller.RTouch);
            _jitterSamples.Enqueue(new JitterSample(now, _tipMarker.position, _cameraRig.rightControllerAnchor.position,
                angularVelocity.magnitude * Mathf.Rad2Deg));
            while (_jitterSamples.Count > 0 && now - _jitterSamples.Peek().Time > JitterWindowSeconds)
                _jitterSamples.Dequeue();
        }

        /// <summary>
        /// Std dev (RMS distance from the mean) of tip and controller position over the last
        /// second, mean angular speed, and mean tip speed. "still" = mean tip speed below
        /// <see cref="StillTipSpeedMetersPerSecond"/>, so the std devs are mostly tracking noise.
        /// Tip noise much larger than controller noise means the noise is rotational (lever arm).
        /// </summary>
        private void LogJitter()
        {
            if (_jitterSamples.Count < 2)
                return;

            var tipMean = Vector3.zero;
            var controllerMean = Vector3.zero;
            var angularSpeedSum = 0f;
            foreach (var sample in _jitterSamples)
            {
                tipMean += sample.TipPosition;
                controllerMean += sample.ControllerPosition;
                angularSpeedSum += sample.AngularSpeedDegreesPerSecond;
            }

            tipMean /= _jitterSamples.Count;
            controllerMean /= _jitterSamples.Count;

            var tipVariance = 0f;
            var controllerVariance = 0f;
            var tipPathMeters = 0f;
            JitterSample? previous = null;
            foreach (var sample in _jitterSamples)
            {
                tipVariance += (sample.TipPosition - tipMean).sqrMagnitude;
                controllerVariance += (sample.ControllerPosition - controllerMean).sqrMagnitude;
                if (previous.HasValue)
                    tipPathMeters += Vector3.Distance(previous.Value.TipPosition, sample.TipPosition);
                previous = sample;
            }

            var spanSeconds = Mathf.Max(_jitterSamples.Count > 1 ? previous.Value.Time - _jitterSamples.Peek().Time : 0f, 1e-3f);
            var meanTipSpeed = tipPathMeters / spanSeconds;
            Debug.Log($"{LogTag} jitter window=1s samples={_jitterSamples.Count} " +
                      $"still={meanTipSpeed < StillTipSpeedMetersPerSecond} mean_tip_speed_mps={meanTipSpeed:0.000} " +
                      $"tip_std_mm={Mathf.Sqrt(tipVariance / _jitterSamples.Count) * 1000f:0.0} " +
                      $"controller_std_mm={Mathf.Sqrt(controllerVariance / _jitterSamples.Count) * 1000f:0.0} " +
                      $"mean_angular_speed_dps={angularSpeedSum / _jitterSamples.Count:0.0} " +
                      $"display_hz={OVRPlugin.systemDisplayFrequency:0}");
        }

        private Transform CreatePrimitive(PrimitiveType type, string objectName, Transform parent, Color color)
        {
            var primitive = GameObject.CreatePrimitive(type);
            primitive.name = objectName;
            // Detection is our own swept math; physics colliders would only cost time.
            Destroy(primitive.GetComponent<Collider>());
            primitive.transform.SetParent(parent, false);
            var primitiveRenderer = primitive.GetComponent<Renderer>();
            primitiveRenderer.sharedMaterial = new Material(_litMaterial);
            primitiveRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            SetColor(primitiveRenderer, color);
            return primitive.transform;
        }

        private static void SetColor(Renderer targetRenderer, Color color)
        {
            // URP Lit uses _BaseColor; Material.color maps to it through [MainColor].
            targetRenderer.sharedMaterial.color = color;
        }

        /// <summary>
        /// URP Lit material asset under Resources/, made by the editor tool
        /// BladeFeelMaterialCreator. Being in Resources/ puts it and its shader in the
        /// build. Do not take the material of a runtime CreatePrimitive instead: that is
        /// the built-in Standard shader, which URP draws as magenta and which breaks
        /// single-pass stereo (objects appear doubled).
        /// </summary>
        private static Material LoadLitMaterial()
        {
            var material = Resources.Load<Material>(LitMaterialResourcePath);
            if (material == null)
                Debug.LogError($"{LogTag} Missing Resources/{LitMaterialResourcePath}.mat; " +
                               "run VR Fencing > Create blade feel material. Objects will render magenta.");
            return material;
        }
    }
}
