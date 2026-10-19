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
//   Left stick up/dn  blade pitch +/- 5 degrees
//   Left trigger      re-place the Targets in front of where the Fencer stands now
//   Right A / B       preview the Touch / flat haptic pattern
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VrFencing.Blade;

namespace VrFencing.Prototypes.BladeFeel
{
    public sealed class BladeFeelPrototype : MonoBehaviour
    {
        private const float TargetPlacementDelaySeconds = 1.5f;
        private const float StatsWindowSeconds = 5f;
        private const float FlatHapticCooldownSeconds = 0.15f;
        private const string LogTag = "[BladeFeel]";

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
        private bool _hasPreviousBlade;
        private BladeSegment _previousBlade;

        private int _bladeMountIndex;
        private int _hapticPresetIndex;
        private bool _isStrict = true;
        private float _pitchDegrees;

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
            _litMaterial = FindDefaultLitMaterial();
            _haptics = new ControllerHapticPlayer(OVRInput.Controller.RTouch);
            _repLog = new RepLog("reps.jsonl");
            _repLog.Write("time", System.DateTime.UtcNow.ToString("o"), "event", "session_start",
                "display_hz", OVRPlugin.systemDisplayFrequency);
            BuildBlade(_cameraRig.rightControllerAnchor);
            BuildHud(_cameraRig.leftControllerAnchor);
            ApplyBladeMount();
            Invoke(nameof(PlaceTargets), TargetPlacementDelaySeconds);
            _statsWindowStart = Time.unscaledTime;
        }

        private void OnDestroy()
        {
            _haptics?.Stop();
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

        private TouchRules CurrentRules() => _isStrict ? TouchRules.Strict : TouchRules.Loose;

        private void HandleInput()
        {
            const OVRInput.Controller Left = OVRInput.Controller.LTouch;
            const OVRInput.Controller Right = OVRInput.Controller.RTouch;

            if (OVRInput.GetDown(OVRInput.Button.One, Left))
            {
                _bladeMountIndex = (_bladeMountIndex + 1) % BladeFeelPresets.BladeMounts.Length;
                ApplyBladeMount();
                LogSetting("blade_mount");
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

            if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstickUp, Left))
                NudgePitch(-BladeFeelPresets.PitchStepDegrees); // Negative x rotation tips the blade up.
            if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstickDown, Left))
                NudgePitch(BladeFeelPresets.PitchStepDegrees);

            if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, Left))
                PlaceTargets();

            if (OVRInput.GetDown(OVRInput.Button.One, Right))
                _haptics.Play(CurrentHaptics().Touch, Time.unscaledTime);
            if (OVRInput.GetDown(OVRInput.Button.Two, Right))
                _haptics.Play(CurrentHaptics().Flat, Time.unscaledTime);
        }

        private void NudgePitch(float deltaDegrees)
        {
            _pitchDegrees += deltaDegrees;
            ApplyBladeMount();
            LogSetting("blade_pitch");
        }

        private HapticPreset CurrentHaptics() => BladeFeelPresets.Haptics[_hapticPresetIndex];
        private BladeMountPreset CurrentMount() => BladeFeelPresets.BladeMounts[_bladeMountIndex];

        private void ApplyBladeMount()
        {
            _bladeMount.localPosition = new Vector3(0f, 0f, CurrentMount().GuardForwardMeters);
            _bladeMount.localRotation = Quaternion.Euler(_pitchDegrees, 0f, 0f);
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
                "blade_pitch_deg", _pitchDegrees,
                "haptic_preset", CurrentHaptics().Name,
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
                "blade_mount", CurrentMount().Name, "blade_pitch_deg", _pitchDegrees,
                "haptic_preset", CurrentHaptics().Name, "touch_rule", _isStrict ? "strict" : "loose");
        }

        // ---- Targets ----

        private void PlaceTargets()
        {
            foreach (var targetRenderer in _targetRenderers)
                Destroy(targetRenderer.gameObject);
            _targetRenderers.Clear();
            _targetFlashEndTimes.Clear();

            // Start pose: the head projected onto the floor (tracking origin is floor level), facing the head's yaw.
            var head = _cameraRig.centerEyeAnchor;
            var floorY = _cameraRig.trackingSpace.position.y;
            var origin = new Vector3(head.position.x, floorY, head.position.z);
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-6f)
                forward = Vector3.forward;
            var facing = Quaternion.LookRotation(forward.normalized, Vector3.up);

            var targets = new List<SphereTarget>();
            foreach (var layout in BladeFeelPresets.Targets)
            {
                var center = origin + facing * new Vector3(layout.RightMeters, layout.HeightMeters, layout.ForwardMeters);
                targets.Add(new SphereTarget(layout.Id, center, layout.RadiusMeters));
                var sphere = CreatePrimitive(PrimitiveType.Sphere, $"Target_{layout.Id}", null,
                    BladeFeelPresets.TargetIdleColor);
                sphere.position = center;
                sphere.localScale = Vector3.one * (2f * layout.RadiusMeters);
                _targetRenderers.Add(sphere.GetComponent<Renderer>());
                _targetFlashEndTimes.Add(0f);
            }

            _detector = new TouchDetector(targets);
            _hasPreviousBlade = false;
            Debug.Log($"{LogTag} Targets placed from start pose {origin} facing {forward.normalized}");
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
            return $"Blade: {CurrentMount().Name}, pitch {_pitchDegrees:+0;-0;0}°\n" +
                   $"Haptics: {CurrentHaptics().Name}\n" +
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
            _statsMaxFrameSeconds = Mathf.Max(_statsMaxFrameSeconds, Time.unscaledDeltaTime);
            _statsMaxTipSpeed = Mathf.Max(_statsMaxTipSpeed, tipSpeed);
            var elapsed = now - _statsWindowStart;
            if (elapsed < StatsWindowSeconds)
                return;

            Debug.Log($"{LogTag} stats window={elapsed:0.0}s frames={_statsFrames} fps={_statsFrames / elapsed:0.0} " +
                      $"max_frame_ms={_statsMaxFrameSeconds * 1000f:0.0} max_tip_speed_mps={_statsMaxTipSpeed:0.00} " +
                      $"display_hz={OVRPlugin.systemDisplayFrequency:0}");
            _statsWindowStart = now;
            _statsFrames = 0;
            _statsMaxFrameSeconds = 0f;
            _statsMaxTipSpeed = 0f;
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
        /// The render pipeline's default material (URP Lit). The scene's reference cube
        /// already uses it, so it and its shader are guaranteed to be in the build.
        /// </summary>
        private static Material FindDefaultLitMaterial()
        {
            var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var material = probe.GetComponent<Renderer>().sharedMaterial;
            Destroy(probe);
            return material;
        }
    }
}
