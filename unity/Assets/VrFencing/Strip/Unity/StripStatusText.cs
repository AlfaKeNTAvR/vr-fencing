using UnityEngine;

namespace VrFencing.Strip
{
    /// <summary>
    /// A line of text that floats in front of the Fencer, a little below eye level, and follows the head gently:
    /// placement prompts, the off-strip notice, and short confirmations such as the new off-strip tolerance.
    /// A short message shows for a few seconds over the standing one, then the standing one returns.
    /// Uses Unity's built-in TextMesh and font, so no font asset has to be imported.
    /// </summary>
    internal sealed class StripStatusText
    {
        private const float DistanceAheadMeters = 1.1f;
        private const float BelowEyeMeters = 0.3f;
        private const float FollowSharpness = 4.0f;
        private const int FontSize = 64;
        private const float CharacterSizeMeters = 0.006f;
        private const string BuiltinFontName = "LegacyRuntime.ttf";

        private readonly TextMesh _text;

        private string _standingMessage = string.Empty;
        private string _shortMessage;
        private double _shortMessageUntilSeconds;
        private bool _hasPose;

        public StripStatusText()
        {
            var textObject = new GameObject("Strip Status Text");
            _text = textObject.AddComponent<TextMesh>();
            _text.font = Resources.GetBuiltinResource<Font>(BuiltinFontName);
            _text.fontSize = FontSize;
            _text.characterSize = CharacterSizeMeters;
            _text.anchor = TextAnchor.MiddleCenter;
            _text.alignment = TextAlignment.Center;
            _text.color = new Color(1.0f, 1.0f, 1.0f, 0.95f);
            if (_text.font != null)
            {
                textObject.GetComponent<MeshRenderer>().sharedMaterial = _text.font.material;
            }
            else
            {
                Debug.LogError($"[Strip] Built-in font '{BuiltinFontName}' not found; status text is not drawn.");
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

            // TextMesh faces -z, so its forward points away from the reader.
            textTransform.rotation = Quaternion.LookRotation(textTransform.position - head.position, Vector3.up);
            _hasPose = true;
        }
    }
}
