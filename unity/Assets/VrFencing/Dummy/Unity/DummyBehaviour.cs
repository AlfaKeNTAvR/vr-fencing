using System;
using System.Collections.Generic;
using UnityEngine;
using VrFencing.Settings;

namespace VrFencing.Dummy
{
    /// <summary>
    /// Draws the translucent Dummy from the Core <see cref="DummyLayout"/> and shows its Target Zones lit, dimmed or
    /// flashing (<see cref="TargetZoneLights"/>). All geometry comes from the Core; this only builds renderers.
    ///
    /// Placement: the parent transform is the Strip frame (origin at the Fencer's En Garde Line on the Strip's
    /// centerline, +z along the Strip toward the opponent). The Dummy puts itself <see cref="DistanceMeters"/> along
    /// +z, facing back toward the Fencer. With no parent, the scene origin stands in for the En Garde Line.
    ///
    /// Height, hand and the Fencer's measured reach come from <see cref="Configure"/>; if nobody calls it before Start,
    /// they are read from the settings file. The reach (from Calibrate grip, PER-92) sets the distance presets; without
    /// one they come from height.
    ///
    /// The sword arm is a hierarchy of joint transforms (Shoulder, Elbow, Wrist) carrying the arm Target Zones, so
    /// <see cref="SetArmPose"/> moves the zones by turning the joints, without rebuilding. A blade for a later armed
    /// Dummy goes under <see cref="GetJointTransform"/>(<see cref="ArmJoint.Wrist"/>).
    ///
    /// Body (PER-78): with a Humanoid body model set (the scene's is Movement SDK's <c>Armature.fbx</c>), the rest of
    /// the silhouette is that rigged body, posed on the same en garde joints and drawn in the translucent silhouette
    /// color; the Target Zones stay capsules on top of it, so they light and flash as before. Without a model, or if
    /// the model is not a usable Humanoid, the capsule silhouette is drawn. Touches are judged on the Core capsules
    /// either way, never on the mesh.
    ///
    /// Look (PER-102): <see cref="SetLook"/> sets the body's opacity, the dim zones' opacity, whether dim zones are
    /// drawn at all, and how thick the zones are drawn as a fraction of their judged radius. Lit and flashing zones
    /// are always drawn, at their own brighter colors, and on top of the body so a cue or a hit always shows.
    /// </summary>
    public sealed class DummyBehaviour : MonoBehaviour
    {
        private const string MaterialResourcePath = "VrFencingDummy";

        // Lit zones bright amber, a Touch flashes white. The body's and dim zones' opacity come from the look.
        private static readonly Color SilhouetteColor = new Color(0.85f, 0.9f, 1.0f);
        private static readonly Color DimZoneColor = new Color(0.55f, 0.8f, 1.0f);
        private static readonly Color LitZoneColor = new Color(1.0f, 0.75f, 0.1f, 0.8f);
        private static readonly Color FlashZoneColor = new Color(1.0f, 1.0f, 1.0f, 0.95f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // The material writes no depth, so transparent parts draw in sort order: zones after the body stay on top of it.
        private const int ZoneSortingOrder = 1;

        [SerializeField]
        [Tooltip("Distance used until a Drill sets one.")]
        private DummyDistance _distancePreset = DummyDistance.Lunge;

        [SerializeField]
        [Tooltip("Humanoid model drawn as the Dummy's body. Empty: the capsule silhouette.")]
        private GameObject _bodyModel;

        private readonly TargetZoneLights _lights = new TargetZoneLights();
        private readonly Dictionary<TargetZone, Renderer> _zoneRenderers = new Dictionary<TargetZone, Renderer>();
        private readonly Dictionary<TargetZone, ZoneLight> _shownLights = new Dictionary<TargetZone, ZoneLight>();
        private readonly Dictionary<ArmJoint, Transform> _jointTransforms = new Dictionary<ArmJoint, Transform>();
        private readonly List<Renderer> _bodyRenderers = new List<Renderer>();

        // The serialized preset as the scene set it, kept the first time anything moves the Dummy.
        private DummyDistance? _sceneDistancePreset;
        private float? _customDistanceMeters;
        private float? _measuredReachMeters;
        private MaterialPropertyBlock _propertyBlock;
        private Transform _partsRoot;
        private DummyBodyRig _bodyRig;
        private DummyLookSettings _look = new DummyLookSettings();
        private bool _isLookSet;

        /// <summary>The Dummy's shape, in its own frame. Null until configured.</summary>
        public DummyLayout Layout { get; private set; }

        /// <summary>Front foot to front foot along the Strip.</summary>
        public float DistanceMeters =>
            _customDistanceMeters
            ?? DistancePresets.GetDistanceMeters(_distancePreset, Layout?.HeightMeters ?? new TrainerSettings().FencerHeightMeters, _measuredReachMeters);

        /// <summary>
        /// Rebuilds the Dummy for a Fencer's height (the Dummy is as tall) and sword hand, and stands it at its preset
        /// distance for the Fencer's measured reach (null: estimated from height).
        /// </summary>
        public void Configure(float fencerHeightMeters, Hand hand, float? measuredReachMeters = null)
        {
            _measuredReachMeters = measuredReachMeters;
            try
            {
                Layout = DummyLayout.Create(fencerHeightMeters, hand);
            }
            catch (ArgumentOutOfRangeException error)
            {
                var defaultHeight = new TrainerSettings().FencerHeightMeters;
                Debug.LogWarning($"[Dummy] {error.Message} Using the default height of {defaultHeight} m.");
                Layout = DummyLayout.Create(defaultHeight, hand);
            }

            BuildRenderers();
            PlaceOnStrip();
        }

        /// <summary>Stands the Dummy at a preset distance, scaled to its height.</summary>
        public void SetDistance(DummyDistance preset)
        {
            _sceneDistancePreset ??= _distancePreset;
            _distancePreset = preset;
            _customDistanceMeters = null;
            PlaceOnStrip();
        }

        /// <summary>Stands the Dummy back at the scene's preset distance, e.g. when a Drill run ends.</summary>
        public void ResetDistance()
        {
            SetDistance(_sceneDistancePreset ?? _distancePreset);
        }

        /// <summary>Stands the Dummy at an exact distance, e.g. a shortened Drill on a short Strip.</summary>
        public void SetDistanceMeters(float distanceMeters)
        {
            if (!(distanceMeters >= 0.0f) || float.IsInfinity(distanceMeters))
            {
                throw new ArgumentOutOfRangeException(nameof(distanceMeters), distanceMeters, "Must be a non-negative number of meters.");
            }

            _customDistanceMeters = distanceMeters;
            PlaceOnStrip();
        }

        /// <summary>The Target Zones in the Strip frame at the current distance, for the Blade's Touch detector.</summary>
        public IReadOnlyList<ZoneCapsule> GetZonesOnStrip()
        {
            return Layout?.GetZonesOnStrip(DistanceMeters) ?? Array.Empty<ZoneCapsule>();
        }

        /// <summary>Every capsule of the Dummy in the Strip frame at the current distance: what the drawn blade point stops against.</summary>
        public IReadOnlyList<Capsule> GetBodyOnStrip()
        {
            return Layout?.GetBodyOnStrip(DistanceMeters) ?? Array.Empty<Capsule>();
        }

        /// <summary>
        /// Moves the sword arm, and with it the hand, wrist, forearm and upper arm zones, by turning the joint
        /// transforms. Nothing is rebuilt and nothing is allocated: the layout's arm moves in place, as the Armed Dummy
        /// re-poses it every frame. The MVP Dummy stays in <see cref="SwordArmPose.ArmInLine"/>.
        /// </summary>
        /// <exception cref="InvalidOperationException">The Dummy is not configured yet.</exception>
        public void SetArmPose(SwordArmPose armPose)
        {
            if (Layout == null)
            {
                throw new InvalidOperationException("Configure the Dummy before posing its arm.");
            }

            Layout.PoseArm(armPose);
            PoseJoints();
        }

        /// <summary>The transform of a sword arm joint, or null before the Dummy is configured.</summary>
        public Transform GetJointTransform(ArmJoint joint)
        {
            return _jointTransforms.TryGetValue(joint, out var jointTransform) ? jointTransform : null;
        }

        /// <summary>
        /// Redraws the body and Target Zones with <paramref name="look"/> (PER-102), without rebuilding: posed joints
        /// and anything hung on them stay. Only the drawing changes; the zones Touches are judged on do not.
        /// </summary>
        public void SetLook(DummyLookSettings look)
        {
            if (look == null)
            {
                throw new ArgumentNullException(nameof(look));
            }

            _look = look.Clone();
            _isLookSet = true;
            if (Layout == null)
            {
                return;
            }

            foreach (var bodyRenderer in _bodyRenderers)
            {
                SetColor(bodyRenderer, GetBodyColor());
            }

            foreach (var zoneRenderer in _zoneRenderers)
            {
                FitCapsule(zoneRenderer.Value.transform, GetZoneInParentFrame(zoneRenderer.Key), GetZoneDrawnRadius(zoneRenderer.Key));
            }

            _shownLights.Clear();
            ShowZoneLights(Time.timeAsDouble);
        }

        public void LightZone(TargetZone zone)
        {
            _lights.Light(zone);
        }

        public void DimZone(TargetZone zone)
        {
            _lights.Dim(zone);
        }

        public void DimAllZones()
        {
            _lights.DimAll();
        }

        /// <summary>Flashes a zone briefly, e.g. on a Touch; it then returns to lit or dim.</summary>
        public void FlashZone(TargetZone zone)
        {
            _lights.Flash(zone, Time.timeAsDouble);
        }

        private void Start()
        {
            if (Layout != null && _isLookSet)
            {
                return;
            }

            var settings = SettingsFile.Load();
            if (!_isLookSet)
            {
                SetLook(settings.DummyLook);
            }

            if (Layout == null)
            {
                Configure(settings.FencerHeightMeters, settings.Hand, settings.Blade.GetMeasuredReachMeters());
            }
        }

        private void Update()
        {
            ShowZoneLights(Time.timeAsDouble);
        }

        private void PlaceOnStrip()
        {
            transform.localPosition = new Vector3(0.0f, 0.0f, DistanceMeters);
            transform.localRotation = Quaternion.Euler(0.0f, 180.0f, 0.0f);
        }

        private void ShowZoneLights(double nowSeconds)
        {
            foreach (var zoneRenderer in _zoneRenderers)
            {
                var zoneLight = _lights.GetLight(zoneRenderer.Key, nowSeconds);
                if (_shownLights.TryGetValue(zoneRenderer.Key, out var shown) && shown == zoneLight)
                {
                    continue;
                }

                // Hiding the zones hides only dim ones: a cue or a hit still shows.
                zoneRenderer.Value.enabled = zoneLight != ZoneLight.Dim || _look.ShowTargetZones;
                SetColor(zoneRenderer.Value, ColorFor(zoneLight));
                _shownLights[zoneRenderer.Key] = zoneLight;
            }
        }

        private Color ColorFor(ZoneLight light)
        {
            switch (light)
            {
                case ZoneLight.Lit:
                    return LitZoneColor;
                case ZoneLight.Flash:
                    return FlashZoneColor;
                default:
                    return WithAlpha(DimZoneColor, _look.ZoneOpacity);
            }
        }

        private Color GetBodyColor()
        {
            return WithAlpha(SilhouetteColor, _look.BodyOpacity);
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            return new Color(color.r, color.g, color.b, Mathf.Clamp01(alpha));
        }

        /// <summary>The zone's capsule in the frame of the transform its part hangs under: its arm joint, or the parts root.</summary>
        private Capsule GetZoneInParentFrame(TargetZone zone)
        {
            if (SwordArm.IsArmZone(zone))
            {
                return Layout.Arm.GetZoneInJointFrame(zone);
            }

            foreach (var zoneCapsule in Layout.Zones)
            {
                if (zoneCapsule.Zone == zone)
                {
                    return zoneCapsule.Shape;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(zone), zone, "The Dummy has no such Target Zone.");
        }

        /// <summary>The radius the zone is drawn at: a fraction of the radius it is judged on, never more.</summary>
        private float GetZoneDrawnRadius(TargetZone zone)
        {
            return GetZoneInParentFrame(zone).RadiusMeters * Mathf.Clamp01(_look.ZoneThicknessFraction);
        }

        private void BuildRenderers()
        {
            DestroyRenderers();

            _partsRoot = new GameObject("Parts").transform;
            _partsRoot.SetParent(transform, false);
            BuildJoints();

            // The default primitive material is not URP: it renders magenta on the Quest. Never fall back to it.
            var material = Resources.Load<Material>(MaterialResourcePath);
            if (material == null)
            {
                Debug.LogError($"[Dummy] Material 'Resources/{MaterialResourcePath}' is missing; the Dummy is not drawn.");
                return;
            }

            _propertyBlock ??= new MaterialPropertyBlock();
            foreach (var zone in Layout.Zones)
            {
                // Arm zones ride on their joint, so posing the arm moves them; the others stay with the stance.
                var parent = SwordArm.IsArmZone(zone.Zone) ? _jointTransforms[SwordArm.GetAttachedJoint(zone.Zone)] : _partsRoot;
                var zoneRenderer = CreateCapsuleRenderer($"Zone {zone.Zone}", GetZoneInParentFrame(zone.Zone), GetZoneDrawnRadius(zone.Zone), parent, material);
                zoneRenderer.sortingOrder = ZoneSortingOrder;
                zoneRenderer.enabled = _look.ShowTargetZones;
                SetColor(zoneRenderer, ColorFor(ZoneLight.Dim));
                _zoneRenderers[zone.Zone] = zoneRenderer;
                _shownLights[zone.Zone] = ZoneLight.Dim;
            }

            _bodyRig = _bodyModel != null ? DummyBodyRig.TryCreate(_bodyModel, _partsRoot, material, Layout) : null;
            if (_bodyRig != null)
            {
                _bodyRenderers.AddRange(_bodyRig.Renderers);
            }
            else
            {
                foreach (var part in Layout.SilhouetteParts)
                {
                    _bodyRenderers.Add(CreateCapsuleRenderer("Silhouette", part, part.RadiusMeters, _partsRoot, material));
                }
            }

            foreach (var bodyRenderer in _bodyRenderers)
            {
                SetColor(bodyRenderer, GetBodyColor());
            }
        }

        /// <summary>Shoulder under the Dummy, Elbow under Shoulder, Wrist under Elbow.</summary>
        private void BuildJoints()
        {
            var parent = _partsRoot;
            foreach (ArmJoint joint in Enum.GetValues(typeof(ArmJoint)))
            {
                var jointTransform = new GameObject(joint.ToString()).transform;
                jointTransform.SetParent(parent, false);
                _jointTransforms[joint] = jointTransform;
                parent = jointTransform;
            }

            PoseJoints();
        }

        private void PoseJoints()
        {
            foreach (var jointTransform in _jointTransforms)
            {
                var localPose = Layout.Arm.GetLocalPose(jointTransform.Key);
                jointTransform.Value.SetLocalPositionAndRotation(localPose.Position.ToUnity(), localPose.Rotation.ToUnity());
            }

            _bodyRig?.Pose(Layout);
        }

        private void DestroyRenderers()
        {
            _zoneRenderers.Clear();
            _shownLights.Clear();
            _jointTransforms.Clear();
            _bodyRenderers.Clear();
            _bodyRig = null;
            if (_partsRoot != null)
            {
                DestroyPart(_partsRoot.gameObject);
                _partsRoot = null;
            }
        }

        /// <summary>A capsule part along <paramref name="capsule"/>'s axis, <paramref name="radiusMeters"/> wide (see <see cref="FitCapsule"/>).</summary>
        private Renderer CreateCapsuleRenderer(string partName, Capsule capsule, float radiusMeters, Transform parent, Material material)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            part.name = partName;

            // Touches are judged on the Core capsules, not by physics.
            DestroyPart(part.GetComponent<Collider>());

            part.transform.SetParent(parent, false);
            FitCapsule(part.transform, capsule, radiusMeters);

            var partRenderer = part.GetComponent<MeshRenderer>();
            partRenderer.sharedMaterial = material;
            partRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            partRenderer.receiveShadows = false;
            return partRenderer;
        }

        /// <summary>
        /// Unity's capsule primitive is 2 m tall and 1 m wide along its local y; it is stretched so its axis runs
        /// from the capsule's start to its end and its width is <paramref name="radiusMeters"/> (the capsule's own,
        /// or less to draw a zone thinner). Rounded ends squash slightly on short capsules, which is fine for a silhouette.
        /// </summary>
        private static void FitCapsule(Transform part, Capsule capsule, float radiusMeters)
        {
            var start = capsule.Start.ToUnity();
            var end = capsule.End.ToUnity();
            var axis = end - start;
            var segmentLength = axis.magnitude;
            var diameter = radiusMeters * 2.0f;

            part.localPosition = (start + end) * 0.5f;
            part.localRotation = segmentLength > 1e-5f ? Quaternion.FromToRotation(Vector3.up, axis / segmentLength) : Quaternion.identity;
            part.localScale = new Vector3(diameter, (segmentLength + diameter) * 0.5f, diameter);
        }

        private void SetColor(Renderer partRenderer, Color color)
        {
            _propertyBlock.SetColor(BaseColorId, color);
            partRenderer.SetPropertyBlock(_propertyBlock);
        }

        private static void DestroyPart(UnityEngine.Object target)
        {
            // Edit-mode tests and editor tools build the Dummy outside Play mode, where Destroy is not allowed.
            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}
