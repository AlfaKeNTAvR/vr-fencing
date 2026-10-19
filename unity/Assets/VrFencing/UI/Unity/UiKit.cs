using TMPro;
using UnityEngine;

namespace VrFencing.UI
{
    /// <summary>
    /// Builds the pieces of the world-space panels from code: flat quads on an opaque URP material (so text, which is
    /// transparent, always draws over them) and TextMeshPro text (SDF shader, renders in both eyes under single-pass
    /// instanced stereo). Panel space: meters, x right, y up, the reader on the -z side, nearer pieces at lower z.
    /// </summary>
    internal static class UiKit
    {
        public const string MaterialResourcePath = "VrFencingUiPanel";

        // TextMeshPro world text: one font size unit is 0.1 m of em height. At the menu's 1.1 m, a 3 cm em reads easily.
        public const float TitleFontSize = 0.40f;
        public const float BodyFontSize = 0.28f;
        public const float SmallFontSize = 0.22f;
        public const float TinyFontSize = 0.17f;

        /// <summary>How far each layer sits in front of the one behind it, so they never z-fight.</summary>
        public const float LayerStepMeters = 0.003f;

        public static readonly Color PanelColor = new Color(0.07f, 0.08f, 0.10f, 1.0f);
        public static readonly Color ButtonColor = new Color(0.19f, 0.22f, 0.27f, 1.0f);
        public static readonly Color HoverColor = new Color(0.27f, 0.38f, 0.52f, 1.0f);
        public static readonly Color SelectedColor = new Color(0.12f, 0.42f, 0.72f, 1.0f);
        public static readonly Color DisabledColor = new Color(0.11f, 0.12f, 0.14f, 1.0f);
        public static readonly Color TextColor = Color.white;
        public static readonly Color DimTextColor = new Color(0.55f, 0.57f, 0.6f, 1.0f);
        public static readonly Color NoteTextColor = new Color(1.0f, 0.72f, 0.35f, 1.0f);
        public static readonly Color BarColor = new Color(0.25f, 0.75f, 0.45f, 1.0f);
        public static readonly Color LaserColor = new Color(0.55f, 0.85f, 1.0f, 1.0f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private static Material _material;
        private static MaterialPropertyBlock _propertyBlock;
        private static bool _hasReportedMissingAssets;

        /// <summary>The opaque URP material for quads and the laser; null (logged once) if missing.</summary>
        public static Material Material
        {
            get
            {
                if (_material == null)
                {
                    // The default primitive material is not URP: it renders magenta on the Quest. Never fall back to it.
                    _material = Resources.Load<Material>(MaterialResourcePath);
                    ReportMissingAssets();
                }

                return _material;
            }
        }

        /// <summary>A flat quad centered at <paramref name="center"/>, <paramref name="layer"/> steps in front of the panel.</summary>
        public static Renderer CreateQuad(Transform parent, string name, Vector2 center, Vector2 size, int layer, Color color)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            DestroyObject(quad.GetComponent<Collider>());
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = new Vector3(center.x, center.y, -layer * LayerStepMeters);
            quad.transform.localScale = new Vector3(size.x, size.y, 1.0f);

            var quadRenderer = quad.GetComponent<MeshRenderer>();
            quadRenderer.sharedMaterial = Material;
            quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            quadRenderer.receiveShadows = false;
            SetColor(quadRenderer, color);
            return quadRenderer;
        }

        /// <summary>A text box centered at <paramref name="center"/>; text wraps inside <paramref name="size"/>.</summary>
        public static TextMeshPro CreateText(
            Transform parent, string name, Vector2 center, Vector2 size, int layer, float fontSize, TextAlignmentOptions alignment, Color color)
        {
            var textObject = new GameObject(name, typeof(RectTransform));
            textObject.transform.SetParent(parent, false);
            var text = textObject.AddComponent<TextMeshPro>();
            if (TMP_Settings.defaultFontAsset != null)
            {
                text.font = TMP_Settings.defaultFontAsset;
            }
            else
            {
                ReportMissingAssets();
            }

            text.rectTransform.sizeDelta = size;
            text.rectTransform.localPosition = new Vector3(center.x, center.y, -layer * LayerStepMeters);
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.color = color;
            text.text = string.Empty;
            return text;
        }

        public static void SetColor(Renderer quadRenderer, Color color)
        {
            _propertyBlock ??= new MaterialPropertyBlock();
            _propertyBlock.SetColor(BaseColorId, color);
            quadRenderer.SetPropertyBlock(_propertyBlock);
        }

        /// <summary>Sets text only when it changed, so per-frame refreshes do not rebuild the text mesh.</summary>
        public static void SetText(TMP_Text text, string value)
        {
            value ??= string.Empty;
            if (text.text != value)
            {
                text.text = value;
            }
        }

        public static void DestroyObject(Object target)
        {
            // Edit-mode tests build panels outside Play mode, where Destroy is not allowed.
            if (Application.isPlaying)
            {
                Object.Destroy(target);
            }
            else
            {
                Object.DestroyImmediate(target);
            }
        }

        private static void ReportMissingAssets()
        {
            if (_hasReportedMissingAssets || (_material != null && TMP_Settings.defaultFontAsset != null))
            {
                return;
            }

            _hasReportedMissingAssets = true;
            Debug.LogError(
                $"[UI] Missing 'Resources/{MaterialResourcePath}' material or the TextMeshPro default font (TMP Essentials); panels may not draw.");
        }
    }
}
