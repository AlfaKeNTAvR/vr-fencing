using System.Collections.Generic;
using UnityEngine;

namespace VrFencing.Strip
{
    /// <summary>
    /// Draws the Strip's piste-like floor markings from a Core <see cref="StripGeometry"/>, under a parent transform
    /// that is the Strip frame (the En Garde Line): a faint translucent piste surface (the real floor shows through
    /// it), side and end lines, the center line, both En Garde Lines, and a hatched translucent Warning Zone at each end
    /// (PER-41: diagonal stripes, as the last two meters of a piste are marked) that glows while the Fencer is in it.
    /// Lines are thin flat boxes just above the floor, solid (opaque material) and lying fully above the Warning Zones,
    /// so the pulsing zones never show through or fight with them. All dimensions come from the Core; this only builds
    /// and colors renderers.
    /// </summary>
    internal sealed class StripMarkings
    {
        /// <summary>Translucent material of the piste surface and, with the hatch texture, of the Warning Zones.</summary>
        public const string MaterialResourcePath = "VrFencingStrip";

        /// <summary>Opaque material of the lines.</summary>
        public const string LineMaterialResourcePath = "VrFencingStripLine";

        /// <summary>The Warning Zones' diagonal stripes (built by tools/models/build_models.py), tiled every <see cref="HatchTileMeters"/>.</summary>
        public const string HatchTextureResourcePath = "StripHatch";

        /// <summary>Piste lines are 5 cm wide.</summary>
        private const float LineWidthMeters = 0.05f;

        /// <summary>One hatch tile holds four stripes, so a stripe repeats every 10 cm.</summary>
        private const float HatchTileMeters = 0.4f;

        // Lifted off the floor so tracking jitter does not sink the markings into it. The surface fills the first
        // millimeter, the zones 1-3 mm above the floor and the lines 4-8 mm, so a line is always on top of a zone. The
        // En Garde Line sits 1 mm higher still, where it crosses the side lines.
        private const float SurfaceThicknessMeters = 0.001f;
        private const float SurfaceHeightMeters = 0.0005f;
        private const float ZoneThicknessMeters = 0.002f;
        private const float ZoneHeightMeters = 0.002f;
        private const float LineThicknessMeters = 0.004f;
        private const float LineHeightMeters = 0.006f;
        private const float EnGardeLineRaiseMeters = 0.001f;

        // The surface draws before the zones and lines in the transparent queue, whatever the distance to the eye.
        private const int SurfaceSortingOrder = -1;

        private const float GlowPulsesPerSecond = 2.0f;

        private static readonly Color LineColor = Color.white;
        private static readonly Color EnGardeLineColor = new Color(0.35f, 1.0f, 0.55f, 1.0f);
        private static readonly Color SurfaceColor = new Color(0.55f, 0.6f, 0.68f, 0.2f);
        private static readonly Color ZoneColor = new Color(1.0f, 0.4f, 0.1f, 0.4f);
        private static readonly Color ZoneGlowDimColor = new Color(1.0f, 0.5f, 0.1f, 0.65f);
        private static readonly Color ZoneGlowBrightColor = new Color(1.0f, 0.65f, 0.2f, 0.95f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int BaseMapTilingId = Shader.PropertyToID("_BaseMap_ST");

        private readonly Transform _root;
        private readonly Material _material;
        private readonly Material _lineMaterial;
        private readonly Material _zoneMaterial;
        private readonly MaterialPropertyBlock _propertyBlock = new MaterialPropertyBlock();
        private readonly Dictionary<StripEnd, Renderer> _zoneRenderers = new Dictionary<StripEnd, Renderer>();
        private readonly Dictionary<string, Renderer> _slabs = new Dictionary<string, Renderer>();

        private StripEnd? _glowingZone;
        private StripGeometry _geometry;
        private (float FencerZ, float OpponentZ)? _movedEnGardeLines;

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
                return;
            }

            // The zones are the translucent material with the hatch on it; without the texture they stay plain.
            var hatch = Resources.Load<Texture2D>(HatchTextureResourcePath);
            if (hatch == null)
            {
                Debug.LogError($"[Strip] Texture 'Resources/{HatchTextureResourcePath}' is missing; the Warning Zones are drawn plain.");
                _zoneMaterial = _material;
                return;
            }

            _zoneMaterial = new Material(_material) { name = _material.name + " Hatch", mainTexture = hatch };
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

            var surface = LayOutSlab("Piste Surface", new Vector3(0.0f, SurfaceHeightMeters, geometry.CenterLineZ),
                new Vector3(StripGeometry.WidthMeters, SurfaceThicknessMeters, length), SurfaceColor, _material);
            surface.sortingOrder = SurfaceSortingOrder;

            foreach (var end in new[] { StripEnd.Rear, StripEnd.Far })
            {
                var (startZ, endZ) = geometry.GetWarningZoneRange(end);
                var zoneSize = new Vector3(StripGeometry.WidthMeters, ZoneThicknessMeters, endZ - startZ);
                _zoneRenderers[end] = LayOutSlab($"Warning Zone {end}", new Vector3(0.0f, ZoneHeightMeters, (startZ + endZ) * 0.5f),
                    zoneSize, ZoneColor, _zoneMaterial, new Vector2(zoneSize.x, zoneSize.z) / HatchTileMeters);
            }

            LayOutSlab("Side Line Left", new Vector3(-halfWidth, LineHeightMeters, geometry.CenterLineZ),
                new Vector3(LineWidthMeters, LineThicknessMeters, length), LineColor, _lineMaterial);
            LayOutSlab("Side Line Right", new Vector3(halfWidth, LineHeightMeters, geometry.CenterLineZ),
                new Vector3(LineWidthMeters, LineThicknessMeters, length), LineColor, _lineMaterial);
            LayOutCrossLine("Rear End Line", geometry.RearEndZ, LineHeightMeters, LineColor);
            LayOutCrossLine("Far End Line", geometry.FarEndZ, LineHeightMeters, LineColor);
            LayOutCrossLine("Center Line", geometry.CenterLineZ, LineHeightMeters, LineColor);
            _geometry = geometry;
            LayOutEnGardeLines();
            _glowingZone = null;
        }

        /// <summary>
        /// Draws the two En Garde Lines at <paramref name="lines"/> (z in the Strip frame) instead of the Strip's own, as
        /// a Partner bout places them (PER-83); null puts them back on the Strip's own.
        /// </summary>
        public void MoveEnGardeLines((float FencerZ, float OpponentZ)? lines)
        {
            if (_movedEnGardeLines == lines)
            {
                return;
            }

            _movedEnGardeLines = lines;
            LayOutEnGardeLines();
        }

        private void LayOutEnGardeLines()
        {
            if (_geometry == null || _material == null || _lineMaterial == null)
            {
                return;
            }

            var fencerZ = _movedEnGardeLines?.FencerZ ?? 0.0f;
            var opponentZ = _movedEnGardeLines?.OpponentZ ?? _geometry.DummyEnGardeLineZ;
            LayOutCrossLine("En Garde Line", fencerZ, LineHeightMeters + EnGardeLineRaiseMeters, EnGardeLineColor);
            LayOutCrossLine("Dummy En Garde Line", opponentZ, LineHeightMeters + EnGardeLineRaiseMeters, EnGardeLineColor);
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

        /// <summary>
        /// Places the slab named <paramref name="slabName"/>, creating it the first time. <paramref name="textureTiling"/>
        /// repeats the material's texture that many times across and along the slab (null: untextured).
        /// </summary>
        private Renderer LayOutSlab(string slabName, Vector3 center, Vector3 size, Color color, Material material, Vector2? textureTiling = null)
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
            SetColor(slabRenderer, color, textureTiling);
            return slabRenderer;
        }

        /// <summary>Colors the slab, keeping the texture tiling it was laid out with unless a new one is given.</summary>
        private void SetColor(Renderer slabRenderer, Color color, Vector2? textureTiling = null)
        {
            slabRenderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(BaseColorId, color);
            if (textureTiling.HasValue)
            {
                _propertyBlock.SetVector(BaseMapTilingId, new Vector4(textureTiling.Value.x, textureTiling.Value.y, 0.0f, 0.0f));
            }

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
