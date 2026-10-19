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
    /// Height and hand come from <see cref="Configure"/>; if nobody calls it before Start, they are read from the
    /// settings file.
    ///
    /// The sword arm is a hierarchy of joint transforms (Shoulder, Elbow, Wrist) carrying the arm Target Zones, so
    /// <see cref="SetArmPose"/> moves the zones by turning the joints, without rebuilding. A blade for a later armed
    /// Dummy goes under <see cref="GetJointTransform"/>(<see cref="ArmJoint.Wrist"/>).
    /// </summary>
    public sealed class DummyBehaviour : MonoBehaviour
    {
        private const string MaterialResourcePath = "VrFencingDummy";

        // Translucent so the room stays visible through the Dummy; lit zones bright amber, a Touch flashes white.
        private static readonly Color SilhouetteColor = new Color(0.85f, 0.9f, 1.0f, 0.15f);
        private static readonly Color DimZoneColor = new Color(0.55f, 0.8f, 1.0f, 0.3f);
        private static readonly Color LitZoneColor = new Color(1.0f, 0.75f, 0.1f, 0.8f);
        private static readonly Color FlashZoneColor = new Color(1.0f, 1.0f, 1.0f, 0.95f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField]
        [Tooltip("Distance used until a Drill sets one.")]
        private DummyDistance _distancePreset = DummyDistance.Lunge;

        private readonly TargetZoneLights _lights = new TargetZoneLights();
        private readonly Dictionary<TargetZone, Renderer> _zoneRenderers = new Dictionary<TargetZone, Renderer>();
        private readonly Dictionary<TargetZone, ZoneLight> _shownLights = new Dictionary<TargetZone, ZoneLight>();
        private readonly Dictionary<ArmJoint, Transform> _jointTransforms = new Dictionary<ArmJoint, Transform>();

        // The serialized preset as the scene set it, kept the first time anything moves the Dummy.
        private DummyDistance? _sceneDistancePreset;
        private float? _customDistanceMeters;
        private MaterialPropertyBlock _propertyBlock;
        private Transform _partsRoot;

        /// <summary>The Dummy's shape, in its own frame. Null until configured.</summary>
        public DummyLayout Layout { get; private set; }

        /// <summary>Front foot to front foot along the Strip.</summary>
        public float DistanceMeters =>
            _customDistanceMeters ?? DistancePresets.GetDistanceMeters(_distancePreset, Layout?.HeightMeters ?? new TrainerSettings().FencerHeightMeters);

        /// <summary>Rebuilds the Dummy for a Fencer's height (the Dummy is as tall) and sword hand.</summary>
        public void Configure(float fencerHeightMeters, Hand hand)
        {
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

        /// <summary>
        /// Moves the sword arm, and with it the hand, wrist, forearm and upper arm zones, by turning the joint
        /// transforms. Nothing is rebuilt. The MVP Dummy stays in <see cref="SwordArmPose.ArmInLine"/>.
        /// </summary>
        /// <exception cref="InvalidOperationException">The Dummy is not configured yet.</exception>
        public void SetArmPose(SwordArmPose armPose)
        {
            if (Layout == null)
            {
                throw new InvalidOperationException("Configure the Dummy before posing its arm.");
            }

            Layout = Layout.WithArmPose(armPose);
            PoseJoints();
        }

        /// <summary>The transform of a sword arm joint, or null before the Dummy is configured.</summary>
        public Transform GetJointTransform(ArmJoint joint)
        {
            return _jointTransforms.TryGetValue(joint, out var jointTransform) ? jointTransform : null;
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
            if (Layout == null)
            {
                var settings = SettingsFile.Load();
                Configure(settings.FencerHeightMeters, settings.Hand);
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

                SetColor(zoneRenderer.Value, ColorFor(zoneLight));
                _shownLights[zoneRenderer.Key] = zoneLight;
            }
        }

        private static Color ColorFor(ZoneLight light)
        {
            switch (light)
            {
                case ZoneLight.Lit:
                    return LitZoneColor;
                case ZoneLight.Flash:
                    return FlashZoneColor;
                default:
                    return DimZoneColor;
            }
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
                var zoneRenderer = SwordArm.IsArmZone(zone.Zone)
                    ? CreateCapsuleRenderer($"Zone {zone.Zone}", Layout.Arm.GetZoneInJointFrame(zone.Zone), _jointTransforms[SwordArm.GetAttachedJoint(zone.Zone)], material)
                    : CreateCapsuleRenderer($"Zone {zone.Zone}", zone.Shape, _partsRoot, material);
                SetColor(zoneRenderer, DimZoneColor);
                _zoneRenderers[zone.Zone] = zoneRenderer;
                _shownLights[zone.Zone] = ZoneLight.Dim;
            }

            foreach (var part in Layout.SilhouetteParts)
            {
                SetColor(CreateCapsuleRenderer("Silhouette", part, _partsRoot, material), SilhouetteColor);
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
        }

        private void DestroyRenderers()
        {
            _zoneRenderers.Clear();
            _shownLights.Clear();
            _jointTransforms.Clear();
            if (_partsRoot != null)
            {
                DestroyPart(_partsRoot.gameObject);
                _partsRoot = null;
            }
        }

        /// <summary>
        /// Unity's capsule primitive is 2 m tall and 1 m wide along its local y; it is stretched so its axis runs
        /// from the capsule's start to its end and its width matches the radius. Rounded ends squash slightly on
        /// short capsules, which is fine for a silhouette.
        /// </summary>
        private Renderer CreateCapsuleRenderer(string partName, Capsule capsule, Transform parent, Material material)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            part.name = partName;

            // Touches are judged on the Core capsules, not by physics.
            DestroyPart(part.GetComponent<Collider>());

            var start = capsule.Start.ToUnity();
            var end = capsule.End.ToUnity();
            var axis = end - start;
            var segmentLength = axis.magnitude;
            var diameter = capsule.RadiusMeters * 2.0f;

            part.transform.SetParent(parent, false);
            part.transform.localPosition = (start + end) * 0.5f;
            part.transform.localRotation = segmentLength > 1e-5f ? Quaternion.FromToRotation(Vector3.up, axis / segmentLength) : Quaternion.identity;
            part.transform.localScale = new Vector3(diameter, (segmentLength + diameter) * 0.5f, diameter);

            var partRenderer = part.GetComponent<MeshRenderer>();
            partRenderer.sharedMaterial = material;
            partRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            partRenderer.receiveShadows = false;
            return partRenderer;
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
