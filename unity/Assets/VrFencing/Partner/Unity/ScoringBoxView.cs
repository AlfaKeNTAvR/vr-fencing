using System;
using TMPro;
using UnityEngine;

namespace VrFencing.Partner
{
    /// <summary>
    /// Draws the bout's scoring box (PER-82, decided in PER-74): a dark box floating <see cref="HeightMeters"/> above the
    /// bout Strip's centre, facing this Fencer, with two lamps side by side. This Fencer's lamp (green) is on their left
    /// as they look at it, the opponent's (red) on the right; unlit lamps show dim. The round-trip time is small text
    /// under it. Also draws the faint mark a glance on the opponent leaves (seen only by the attacker).
    /// </summary>
    internal sealed class ScoringBoxView : IDisposable
    {
        /// <summary>The box's centre above the floor: above head height, so it never hides the opponent.</summary>
        public const float HeightMeters = 2.3f;

        /// <summary>How long a glance's faint mark stays, fading out.</summary>
        public const float GlanceMarkSeconds = 1.0f;

        private const string MaterialResourcePath = "VrFencingBlade";
        private const string MarkMaterialResourcePath = "VrFencingDummy";
        private const string OutlineMaterialResourcePath = "Fonts & Materials/LiberationSans SDF - Outline";

        private const float BoxWidthMeters = 0.7f;
        private const float BoxHeightMeters = 0.3f;
        private const float BoxDepthMeters = 0.08f;
        private const float LampWidthMeters = 0.26f;
        private const float LampHeightMeters = 0.2f;
        private const float LampSideOffsetMeters = 0.17f;
        private const float GlanceMarkDiameterMeters = 0.04f;
        private const float GlanceMarkAlpha = 0.35f;

        // TextMeshPro world text: one font size unit is 0.1 m of em height.
        private const float PingFontSize = 0.6f;
        private const float PingBelowBoxMeters = 0.09f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly Color BoxColor = new Color(0.08f, 0.08f, 0.1f, 1.0f);
        private static readonly Color MyLampLit = new Color(0.1f, 1.0f, 0.2f, 1.0f);
        private static readonly Color MyLampDim = new Color(0.05f, 0.2f, 0.07f, 1.0f);
        private static readonly Color OpponentLampLit = new Color(1.0f, 0.1f, 0.08f, 1.0f);
        private static readonly Color OpponentLampDim = new Color(0.22f, 0.05f, 0.05f, 1.0f);
        private static readonly Color GlanceMarkColor = new Color(0.4f, 0.7f, 1.0f, GlanceMarkAlpha);

        private readonly MaterialPropertyBlock _propertyBlock = new MaterialPropertyBlock();
        private readonly GameObject _root;
        private readonly Renderer _myLamp;
        private readonly Renderer _opponentLamp;
        private readonly TextMeshPro _ping;
        private readonly Renderer _glanceMark;
        private bool? _isMyLampLit;
        private bool? _isOpponentLampLit;
        private float _glanceMarkEndsAt = float.NegativeInfinity;

        public ScoringBoxView()
        {
            var material = Resources.Load<Material>(MaterialResourcePath);
            if (material == null)
            {
                Debug.LogError($"[Bout] Material 'Resources/{MaterialResourcePath}' is missing; the scoring box is not drawn.");
            }

            _root = new GameObject("Scoring Box");
            CreatePart("Box", material, BoxColor, Vector3.zero, new Vector3(BoxWidthMeters, BoxHeightMeters, BoxDepthMeters));

            // The front (-z) faces this Fencer, who looks along +z: their left is the box's -x.
            var lampDepth = -BoxDepthMeters * 0.5f;
            var lampScale = new Vector3(LampWidthMeters, LampHeightMeters, 0.02f);
            _myLamp = CreatePart("Your Lamp", material, MyLampDim, new Vector3(-LampSideOffsetMeters, 0.0f, lampDepth), lampScale);
            _opponentLamp = CreatePart("Opponent Lamp", material, OpponentLampDim, new Vector3(LampSideOffsetMeters, 0.0f, lampDepth), lampScale);

            _ping = CreatePingText(_root.transform);

            var markMaterial = Resources.Load<Material>(MarkMaterialResourcePath);
            _glanceMark = CreatePrimitive(PrimitiveType.Sphere, "Glance Mark", null, markMaterial, GlanceMarkColor, Vector3.zero, Vector3.one * GlanceMarkDiameterMeters);
            _glanceMark.gameObject.SetActive(false);
            _root.SetActive(false);
        }

        /// <summary>Shows or hides the box (not the glance mark).</summary>
        public void SetVisible(bool isVisible)
        {
            if (_root.activeSelf != isVisible)
            {
                _root.SetActive(isVisible);
            }
        }

        /// <summary>
        /// Places the box above <paramref name="centerInRoom"/> (the bout Strip's centre on the floor), facing back along
        /// <paramref name="towardOpponentInRoom"/> to this Fencer, and lights the lamps.
        /// </summary>
        public void Draw(Vector3 centerInRoom, Quaternion towardOpponentInRoom, bool isMyLampLit, bool isOpponentLampLit, string pingText)
        {
            _root.transform.SetPositionAndRotation(centerInRoom + (Vector3.up * HeightMeters), towardOpponentInRoom);
            if (_isMyLampLit != isMyLampLit)
            {
                _isMyLampLit = isMyLampLit;
                SetColor(_myLamp, isMyLampLit ? MyLampLit : MyLampDim);
            }

            if (_isOpponentLampLit != isOpponentLampLit)
            {
                _isOpponentLampLit = isOpponentLampLit;
                SetColor(_opponentLamp, isOpponentLampLit ? OpponentLampLit : OpponentLampDim);
            }

            if (_ping != null && _ping.text != pingText)
            {
                _ping.text = pingText;
            }
        }

        /// <summary>A faint mark where a glance met the opponent, fading over <see cref="GlanceMarkSeconds"/>.</summary>
        public void MarkGlance(Vector3 positionInRoom, float nowSeconds)
        {
            _glanceMark.transform.position = positionInRoom;
            _glanceMark.gameObject.SetActive(true);
            _glanceMarkEndsAt = nowSeconds + GlanceMarkSeconds;
        }

        /// <summary>Fades the glance mark; call every frame.</summary>
        public void Tick(float nowSeconds)
        {
            if (!_glanceMark.gameObject.activeSelf)
            {
                return;
            }

            var left = _glanceMarkEndsAt - nowSeconds;
            if (left <= 0.0f)
            {
                _glanceMark.gameObject.SetActive(false);
                return;
            }

            var color = GlanceMarkColor;
            color.a = GlanceMarkAlpha * (left / GlanceMarkSeconds);
            SetColor(_glanceMark, color);
        }

        public void Dispose()
        {
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
            }

            if (_glanceMark != null)
            {
                UnityEngine.Object.Destroy(_glanceMark.gameObject);
            }
        }

        private Renderer CreatePart(string partName, Material material, Color color, Vector3 localPosition, Vector3 localScale)
        {
            return CreatePrimitive(PrimitiveType.Cube, partName, _root.transform, material, color, localPosition, localScale);
        }

        private Renderer CreatePrimitive(PrimitiveType type, string partName, Transform parent, Material material, Color color, Vector3 localPosition, Vector3 localScale)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = partName;

            // Nothing here is touched by physics.
            UnityEngine.Object.Destroy(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            var partRenderer = part.GetComponent<MeshRenderer>();
            if (material != null)
            {
                partRenderer.sharedMaterial = material;
            }
            else
            {
                partRenderer.enabled = false;
            }

            partRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            partRenderer.receiveShadows = false;
            SetColor(partRenderer, color);
            return partRenderer;
        }

        private void SetColor(Renderer partRenderer, Color color)
        {
            _propertyBlock.SetColor(BaseColorId, color);
            partRenderer.SetPropertyBlock(_propertyBlock);
        }

        private static TextMeshPro CreatePingText(Transform parent)
        {
            if (TMP_Settings.defaultFontAsset == null)
            {
                Debug.LogError("[Bout] No TextMeshPro default font (TMP Essentials missing); the ping is not drawn.");
                return null;
            }

            var textObject = new GameObject("Ping", typeof(RectTransform));
            textObject.transform.SetParent(parent, false);

            // TextMeshPro reads from its -z side, the box's front: this Fencer's side.
            textObject.transform.SetLocalPositionAndRotation(
                new Vector3(0.0f, -(BoxHeightMeters * 0.5f) - PingBelowBoxMeters, -BoxDepthMeters * 0.5f),
                Quaternion.identity);
            var text = textObject.AddComponent<TextMeshPro>();
            text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = PingFontSize;
            text.rectTransform.sizeDelta = new Vector2(BoxWidthMeters, 0.12f);
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.color = Color.white;
            var outline = Resources.Load<Material>(OutlineMaterialResourcePath);
            if (outline != null)
            {
                text.fontSharedMaterial = outline;
            }

            return text;
        }
    }
}
