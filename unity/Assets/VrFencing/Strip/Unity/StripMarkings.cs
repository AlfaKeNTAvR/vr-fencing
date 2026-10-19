using System.Collections.Generic;
using UnityEngine;

namespace VrFencing.Strip
{
    /// <summary>
    /// Draws the Strip's piste-like floor markings from a Core <see cref="StripGeometry"/>, under a parent transform
    /// that is the Strip frame (the En Garde Line): side and end lines, the center line, the En Garde Line, and a
    /// translucent Warning Zone at each end that glows while the Fencer is in it. Lines are thin flat boxes just above
    /// the floor, solid (opaque material) and lying fully above the Warning Zones, so the pulsing zones never show
    /// through or fight with them. All dimensions come from the Core; this only builds and colors renderers.
    /// </summary>
    internal sealed class StripMarkings
    {
        /// <summary>Translucent material of the Warning Zones.</summary>
        public const string MaterialResourcePath = "VrFencingStrip";

        /// <summary>Opaque material of the lines.</summary>
        public const string LineMaterialResourcePath = "VrFencingStripLine";

        /// <summary>Piste lines are 5 cm wide.</summary>
        private const float LineWidthMeters = 0.05f;

        // Lifted off the floor so tracking jitter does not sink the markings into it. The zones fill 1-3 mm above the
        // floor and the lines 4-8 mm, so a line is always on top of a zone. The En Garde Line sits 1 mm higher still,
        // where it crosses the side lines.
        private const float ZoneThicknessMeters = 0.002f;
        private const float ZoneHeightMeters = 0.002f;
        private const float LineThicknessMeters = 0.004f;
        private const float LineHeightMeters = 0.006f;
        private const float EnGardeLineRaiseMeters = 0.001f;

        private const float GlowPulsesPerSecond = 2.0f;

        private static readonly Color LineColor = Color.white;
        private static readonly Color EnGardeLineColor = new Color(0.35f, 1.0f, 0.55f, 1.0f);
        private static readonly Color ZoneColor = new Color(1.0f, 0.45f, 0.1f, 0.18f);
        private static readonly Color ZoneGlowDimColor = new Color(1.0f, 0.5f, 0.1f, 0.45f);
        private static readonly Color ZoneGlowBrightColor = new Color(1.0f, 0.6f, 0.15f, 0.75f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private readonly Transform _root;
        private readonly Material _material;
        private readonly Material _lineMaterial;
        private readonly MaterialPropertyBlock _propertyBlock = new MaterialPropertyBlock();
        private readonly Dictionary<StripEnd, Renderer> _zoneRenderers = new Dictionary<StripEnd, Renderer>();
        private readonly Dictionary<string, Renderer> _slabs = new Dictionary<string, Renderer>();

        private StripEnd? _glowingZone;

        /// <param name="parent">The Strip frame: the En Garde Line transform.</param>
        public StripMarkings(Transform parent)
        {
            _root = new GameObject("Strip Markings").transform;
            _root.SetParent(parent, false);

            // The default primitive material is not URP: it renders magenta on the Quest. Never fall back to it.
            _material = Resources.Load<Material>(MaterialResourcePath);
            _lineMaterial = Resources.Load<Material>(LineMaterialResourcePath);
            if (_material == null || _lineMaterial == null)
            {
                Debug.LogError(
                    $"[Strip] Material 'Resources/{MaterialResourcePath}' or 'Resources/{LineMaterialResourcePath}' is missing; the Strip markings are not drawn.");
            }
        }

        /// <summary>Lays every marking out for <paramref name="geometry"/>. Cheap enough to call every frame for the placement preview.</summary>
        public void Build(StripGeometry geometry)
        {
            if (_material == null || _lineMaterial == null)
            {
                return;
            }

            var halfWidth = StripGeometry.WidthMeters * 0.5f;
            var length = geometry.FarEndZ - geometry.RearEndZ;

            foreach (var end in new[] { StripEnd.Rear, StripEnd.Far })
            {
                var (startZ, endZ) = geometry.GetWarningZoneRange(end);
                _zoneRenderers[end] = LayOutSlab($"Warning Zone {end}", new Vector3(0.0f, ZoneHeightMeters, (startZ + endZ) * 0.5f),
                    new Vector3(StripGeometry.WidthMeters, ZoneThicknessMeters, endZ - startZ), ZoneColor, _material);
            }

            LayOutSlab("Side Line Left", new Vector3(-halfWidth, LineHeightMeters, geometry.CenterLineZ),
                new Vector3(LineWidthMeters, LineThicknessMeters, length), LineColor, _lineMaterial);
            LayOutSlab("Side Line Right", new Vector3(halfWidth, LineHeightMeters, geometry.CenterLineZ),
                new Vector3(LineWidthMeters, LineThicknessMeters, length), LineColor, _lineMaterial);
            LayOutCrossLine("Rear End Line", geometry.RearEndZ, LineHeightMeters, LineColor);
            LayOutCrossLine("Far End Line", geometry.FarEndZ, LineHeightMeters, LineColor);
            LayOutCrossLine("Center Line", geometry.CenterLineZ, LineHeightMeters, LineColor);
            LayOutCrossLine("En Garde Line", 0.0f, LineHeightMeters + EnGardeLineRaiseMeters, EnGardeLineColor);

            _glowingZone = null;
        }

        public void SetVisible(bool isVisible)
        {
            _root.gameObject.SetActive(isVisible);
        }

        /// <summary>Lights <paramref name="zone"/> with a soft pulse and returns the other zone to its resting color.</summary>
        public void ShowGlow(StripEnd? zone, double nowSeconds)
        {
            if (_glowingZone.HasValue && _glowingZone != zone && _zoneRenderers.TryGetValue(_glowingZone.Value, out var previous))
            {
                SetColor(previous, ZoneColor);
            }

            _glowingZone = zone;
            if (zone.HasValue && _zoneRenderers.TryGetValue(zone.Value, out var glowing))
            {
                var pulse = 0.5f + (0.5f * Mathf.Sin((float)(nowSeconds * GlowPulsesPerSecond * 2.0 * Mathf.PI)));
                SetColor(glowing, Color.Lerp(ZoneGlowDimColor, ZoneGlowBrightColor, pulse));
            }
        }

        private void LayOutCrossLine(string lineName, float z, float height, Color color)
        {
            LayOutSlab(lineName, new Vector3(0.0f, height, z),
                new Vector3(StripGeometry.WidthMeters + LineWidthMeters, LineThicknessMeters, LineWidthMeters), color, _lineMaterial);
        }

        /// <summary>Places the slab named <paramref name="slabName"/>, creating it the first time.</summary>
        private Renderer LayOutSlab(string slabName, Vector3 center, Vector3 size, Color color, Material material)
        {
            if (!_slabs.TryGetValue(slabName, out var slabRenderer))
            {
                var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                slab.name = slabName;
                DestroyObject(slab.GetComponent<Collider>());
                slab.transform.SetParent(_root, false);

                slabRenderer = slab.GetComponent<MeshRenderer>();
                slabRenderer.sharedMaterial = material;
                slabRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                slabRenderer.receiveShadows = false;
                _slabs[slabName] = slabRenderer;
            }

            slabRenderer.transform.localPosition = center;
            slabRenderer.transform.localScale = size;
            SetColor(slabRenderer, color);
            return slabRenderer;
        }

        private void SetColor(Renderer slabRenderer, Color color)
        {
            _propertyBlock.SetColor(BaseColorId, color);
            slabRenderer.SetPropertyBlock(_propertyBlock);
        }

        private static void DestroyObject(Object target)
        {
            // Edit-mode tests build markings outside Play mode, where Destroy is not allowed.
            if (Application.isPlaying)
            {
                Object.Destroy(target);
            }
            else
            {
                Object.DestroyImmediate(target);
            }
        }
    }
}
