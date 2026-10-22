using TMPro;
using UnityEngine;

namespace VrFencing.Tracking
{
    /// <summary>
    /// A small red "REC 1:23" fixed in the upper left of the Fencer's view while the Fencer recorder runs, so the Fencer
    /// knows a press started or stopped it, and a short note under it (PER-136: a mark's "MARK n", or that REC is off).
    /// TextMeshPro, as the other world text, so it renders in both eyes.
    /// </summary>
    internal sealed class RecordingIndicator
    {
        // In the head's frame: ahead, up and to the left, so it sits near the edge of view without covering the Dummy.
        private static readonly Vector3 OffsetFromHeadMeters = new Vector3(-0.22f, 0.16f, 0.8f);
        // TextMeshPro world text: one font size unit is 0.1 m of em height, so 0.3 is a 3 cm em.
        private const float FontSize = 0.3f;
        private const float BoxWidthMeters = 0.4f;
        private const float BoxHeightMeters = 0.1f;

        // The note (PER-136: a mark's confirmation, or that REC is off) sits just under the REC line.
        private static readonly Vector3 NoteBelowMeters = new Vector3(0.0f, -0.05f, 0.0f);

        private readonly TextMeshPro _text;
        private readonly TextMeshPro _note;
        private int _shownSeconds = -1;
        private double _noteEndsAt = double.NegativeInfinity;

        public RecordingIndicator(Transform head)
        {
            _text = CreateText(head, "Recording Indicator", OffsetFromHeadMeters, new Color(1.0f, 0.15f, 0.15f, 1.0f));
            _note = CreateText(head, "Recording Note", OffsetFromHeadMeters + NoteBelowMeters, Color.white);
        }

        /// <summary>Shows <paramref name="text"/> under the REC line in <paramref name="color"/> until <paramref name="endsAt"/> (realtime seconds).</summary>
        public void ShowNote(string text, Color color, double endsAt)
        {
            if (_note == null)
            {
                return;
            }

            _note.text = text;
            _note.color = color;
            _note.gameObject.SetActive(true);
            _noteEndsAt = endsAt;
        }

        /// <summary>Hides the note once its time is up.</summary>
        public void UpdateNote(double now)
        {
            if (_note != null && _note.gameObject.activeSelf && now >= _noteEndsAt)
            {
                _note.gameObject.SetActive(false);
            }
        }

        private static TextMeshPro CreateText(Transform head, string name, Vector3 offsetFromHead, Color color)
        {
            var textObject = new GameObject(name, typeof(RectTransform));
            textObject.transform.SetParent(head, false);
            textObject.transform.localPosition = offsetFromHead;
            textObject.transform.localRotation = Quaternion.identity;
            var text = textObject.AddComponent<TextMeshPro>();
            if (TMP_Settings.defaultFontAsset == null)
            {
                Debug.LogError($"[Recorder] No TextMeshPro default font (TMP Essentials missing); the {name} is not drawn.");
            }
            else
            {
                text.font = TMP_Settings.defaultFontAsset;
            }

            text.fontSize = FontSize;
            text.rectTransform.sizeDelta = new Vector2(BoxWidthMeters, BoxHeightMeters);
            text.alignment = TextAlignmentOptions.Left;
            text.color = color;
            textObject.SetActive(false);
            return text;
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

            if (_note != null)
            {
                Object.Destroy(_note.gameObject);
            }
        }
    }
}
