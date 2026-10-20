using TMPro;
using UnityEngine;

namespace VrFencing.Partner
{
    /// <summary>
    /// The connection icon (PER-84, decided in PER-73): while no data comes from the partner for
    /// <see cref="OpponentLink.SignalLostSeconds"/>, an amber "no signal" sign floats above the opponent's head (its last
    /// pose), turned to face this Fencer's eyes. It lives under the opponent's root, and goes with it.
    /// </summary>
    internal sealed class ConnectionIconView
    {
        /// <summary>How far above the opponent's head the sign floats.</summary>
        public const float AboveHeadMeters = 0.35f;

        private const string Text = "<b>(!)</b> No signal";
        private const string OutlineMaterialResourcePath = "Fonts & Materials/LiberationSans SDF - Outline";

        // TextMeshPro world text: one font size unit is 0.1 m of em height, so about 5 cm letters.
        private const float FontSize = 0.7f;
        private const float WidthMeters = 1.0f;

        private static readonly Color IconColor = new Color(1.0f, 0.7f, 0.15f, 1.0f);

        private readonly GameObject _root;

        public ConnectionIconView(Transform parent)
        {
            _root = new GameObject("Connection Icon", typeof(RectTransform));
            _root.transform.SetParent(parent, false);
            if (TMP_Settings.defaultFontAsset == null)
            {
                Debug.LogError("[Opponent] No TextMeshPro default font (TMP Essentials missing); the connection icon is not drawn.");
            }
            else
            {
                var text = _root.AddComponent<TextMeshPro>();
                text.font = TMP_Settings.defaultFontAsset;
                text.fontSize = FontSize;
                text.rectTransform.sizeDelta = new Vector2(WidthMeters, FontSize * 0.2f);
                text.alignment = TextAlignmentOptions.Center;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Overflow;
                text.color = IconColor;
                text.text = Text;
                var outline = Resources.Load<Material>(OutlineMaterialResourcePath);
                if (outline != null)
                {
                    text.fontSharedMaterial = outline;
                }
            }

            _root.SetActive(false);
        }

        public bool IsVisible => _root.activeSelf;

        /// <summary>Shows the sign above <paramref name="headInRoom"/> facing <paramref name="eyesInRoom"/>, or hides it.</summary>
        public void Draw(bool isVisible, Vector3 headInRoom, Vector3 eyesInRoom)
        {
            if (_root.activeSelf != isVisible)
            {
                _root.SetActive(isVisible);
            }

            if (!isVisible)
            {
                return;
            }

            var position = headInRoom + (Vector3.up * AboveHeadMeters);

            // TextMeshPro reads from its -z side: point +z away from the eyes.
            var away = position - eyesInRoom;
            away.y = 0.0f;
            var rotation = away.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(away.normalized, Vector3.up) : Quaternion.identity;
            _root.transform.SetPositionAndRotation(position, rotation);
        }
    }
}
