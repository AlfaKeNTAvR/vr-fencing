using TMPro;
using UnityEngine;

namespace VrFencing.Tracking
{
    /// <summary>
    /// A small red "REC 1:23" fixed in the upper left of the Fencer's view while the Fencer recorder runs, so the Fencer
    /// knows a press started or stopped it. TextMeshPro, as the other world text, so it renders in both eyes.
    /// </summary>
    internal sealed class RecordingIndicator
    {
        // In the head's frame: ahead, up and to the left, so it sits near the edge of view without covering the Dummy.
        private static readonly Vector3 OffsetFromHeadMeters = new Vector3(-0.22f, 0.16f, 0.8f);
        // TextMeshPro world text: one font size unit is 0.1 m of em height, so 0.3 is a 3 cm em.
        private const float FontSize = 0.3f;
        private const float BoxWidthMeters = 0.4f;
        private const float BoxHeightMeters = 0.1f;

        private readonly TextMeshPro _text;
        private int _shownSeconds = -1;

        public RecordingIndicator(Transform head)
        {
            var textObject = new GameObject("Recording Indicator", typeof(RectTransform));
            textObject.transform.SetParent(head, false);
            textObject.transform.localPosition = OffsetFromHeadMeters;
            textObject.transform.localRotation = Quaternion.identity;
            _text = textObject.AddComponent<TextMeshPro>();
            if (TMP_Settings.defaultFontAsset == null)
            {
                Debug.LogError("[Recorder] No TextMeshPro default font (TMP Essentials missing); the REC indicator is not drawn.");
            }
            else
            {
                _text.font = TMP_Settings.defaultFontAsset;
            }

            _text.fontSize = FontSize;
            _text.rectTransform.sizeDelta = new Vector2(BoxWidthMeters, BoxHeightMeters);
            _text.alignment = TextAlignmentOptions.Left;
            _text.color = new Color(1.0f, 0.15f, 0.15f, 1.0f);
            textObject.SetActive(false);
        }

        /// <summary>Shows the elapsed time while recording; hides the text otherwise.</summary>
        public void Show(bool isRecording, double elapsedSeconds)
        {
            if (_text == null)
            {
                return;
            }

            _text.gameObject.SetActive(isRecording);
            var seconds = isRecording ? (int)elapsedSeconds : -1;
            if (seconds == _shownSeconds)
            {
                return;
            }

            _shownSeconds = seconds;
            if (isRecording)
            {
                _text.text = $"REC {seconds / 60}:{seconds % 60:00}";
            }
        }

        public void Destroy()
        {
            if (_text != null)
            {
                Object.Destroy(_text.gameObject);
            }
        }
    }
}
