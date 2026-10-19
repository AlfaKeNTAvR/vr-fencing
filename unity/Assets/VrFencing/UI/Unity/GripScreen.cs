using System;
using TMPro;
using UnityEngine;
using VrFencing.Settings;

namespace VrFencing.UI
{
    /// <summary>
    /// The grip page (PER-52), opened from Settings: the chosen grip's computed angles next to the Fencer's saved ones,
    /// the old feel-test angles and the ones in use. Buttons: start the thrust calibration, reset the grip to its
    /// computed angles, back to Settings. The grip itself is chosen on the settings screen.
    /// </summary>
    internal sealed class GripScreen
    {
        private readonly GameObject _root;
        private readonly TextMeshPro _comparison;
        private readonly TextMeshPro _message;

        public GripScreen(Transform panel, Action calibrate, Action reset, Action back)
        {
            _root = new GameObject("Grip");
            _root.transform.SetParent(panel, false);
            var root = _root.transform;

            UiKit.CreateText(root, "Title", new Vector2(0.0f, 0.235f), new Vector2(0.72f, 0.06f), 1, UiKit.TitleFontSize,
                TextAlignmentOptions.MidlineLeft, UiKit.TextColor).text = "Grip angles";
            _comparison = UiKit.CreateText(root, "Comparison", new Vector2(0.0f, 0.06f), new Vector2(0.72f, 0.26f), 1, UiKit.SmallFontSize,
                TextAlignmentOptions.TopLeft, UiKit.TextColor);
            UiKit.CreateText(root, "How", new Vector2(0.0f, -0.115f), new Vector2(0.72f, 0.08f), 1, UiKit.TinyFontSize,
                TextAlignmentOptions.TopLeft, UiKit.DimTextColor).text =
                "Calibrate: thrust with a straight arm at each of 3 dots and hold still 1 s. The off-hand trigger cancels.";
            _message = UiKit.CreateText(root, "Message", new Vector2(0.0f, -0.175f), new Vector2(0.72f, 0.05f), 1, UiKit.TinyFontSize,
                TextAlignmentOptions.MidlineLeft, UiKit.NoteTextColor);

            var buttonSize = new Vector2(0.22f, 0.055f);
            UiButton.Create(root, "Calibrate", new Vector2(-0.24f, -0.245f), buttonSize, UiKit.BodyFontSize, calibrate).SetLabel("Calibrate");
            UiButton.Create(root, "Reset", new Vector2(0.0f, -0.245f), buttonSize, UiKit.SmallFontSize, reset).SetLabel("Reset to computed");
            UiButton.Create(root, "Back", new Vector2(0.24f, -0.245f), buttonSize, UiKit.BodyFontSize, back).SetLabel("Back");
        }

        public void SetVisible(bool isVisible)
        {
            _root.SetActive(isVisible);
        }

        public void Refresh(TrainerSettings settings, string message)
        {
            UiKit.SetText(_comparison, string.Join("\n", GripText.FormatComparison(settings.Blade)));
            UiKit.SetText(_message, message);
        }
    }
}
