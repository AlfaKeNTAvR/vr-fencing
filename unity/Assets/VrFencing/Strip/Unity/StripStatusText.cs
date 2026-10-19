using TMPro;
using UnityEngine;

namespace VrFencing.Strip
{
    /// <summary>
    /// A line of text that floats in front of the Fencer, a little below eye level, and follows the head gently:
    /// placement prompts, the off-strip notice, and short confirmations such as the new off-strip tolerance.
    /// A short message shows for a few seconds over the standing one, then the standing one returns.
    /// Drawn with TextMeshPro: its SDF shader renders in both eyes under single-pass instanced stereo, which the
    /// legacy TextMesh font shader does not reliably do. A dark outline keeps it readable over any room.
    /// </summary>
    internal sealed class StripStatusText
    {
        private const float DistanceAheadMeters = 1.1f;
        private const float BelowEyeMeters = 0.3f;
        private const float FollowSharpness = 4.0f;
        // TextMeshPro world text: one font size unit is 0.1 m of em height, so 0.4 is a 4 cm em (about 2.9 cm capitals).
        private const float FontSize = 0.4f;
        private const float WrapWidthMeters = 1.0f;
        private const float BoxHeightMeters = 0.4f;

        // The TMP Essentials' outline preset of the default font: a black outline, shared, so no material is instanced.
        private const string OutlineMaterialResourcePath = "Fonts & Materials/LiberationSans SDF - Outline";

        private readonly TextMeshPro _text;

        private string _standingMessage = string.Empty;
        private string _shortMessage;
        private double _shortMessageUntilSeconds;
        private bool _hasPose;

        public StripStatusText()
        {
            var textObject = new GameObject("Strip Status Text", typeof(RectTransform));
            _text = textObject.AddComponent<TextMeshPro>();
            if (TMP_Settings.defaultFontAsset == null)
            {
                Debug.LogError("[Strip] No TextMeshPro default font (TMP Essentials missing); status text is not drawn.");
                return;
            }

            _text.font = TMP_Settings.defaultFontAsset;
            _text.fontSize = FontSize;
            _text.rectTransform.sizeDelta = new Vector2(WrapWidthMeters, BoxHeightMeters);
            _text.alignment = TextAlignmentOptions.Center;
            _text.textWrappingMode = TextWrappingModes.Normal;
            _text.color = new Color(1.0f, 1.0f, 1.0f, 1.0f);
            var outline = Resources.Load<Material>(OutlineMaterialResourcePath);
            if (outline != null)
            {
                _text.fontSharedMaterial = outline;
            }
        }

        /// <summary>Text shown until replaced; empty hides it.</summary>
        public void SetStandingMessage(string message)
        {
            _standingMessage = message ?? string.Empty;
        }

        /// <summary>Text shown for <paramref name="seconds"/> over the standing message.</summary>
        public void ShowShortMessage(string message, double nowSeconds, float seconds)
        {
            _shortMessage = message;
            _shortMessageUntilSeconds = nowSeconds + seconds;
        }

        /// <summary>Updates the text and eases it toward its spot in front of <paramref name="head"/>.</summary>
        public void Tick(Transform head, double nowSeconds, float deltaSeconds)
        {
            var message = _shortMessage != null && nowSeconds < _shortMessageUntilSeconds ? _shortMessage : _standingMessage;
            if (_text.text != message)
            {
                _text.text = message;
            }

            _text.gameObject.SetActive(!string.IsNullOrEmpty(message));
            if (head == null)
            {
                return;
            }

            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f)
            {
                forward = Vector3.ProjectOnPlane(head.up, Vector3.up);
            }

            forward.Normalize();
            var target = head.position + (forward * DistanceAheadMeters) + (Vector3.down * BelowEyeMeters);
            var follow = _hasPose ? 1.0f - Mathf.Exp(-FollowSharpness * deltaSeconds) : 1.0f;
            var textTransform = _text.transform;
            textTransform.position = Vector3.Lerp(textTransform.position, target, follow);

            // TextMeshPro faces -z, so its forward points away from the reader.
            textTransform.rotation = Quaternion.LookRotation(textTransform.position - head.position, Vector3.up);
            _hasPose = true;
        }
    }
}
