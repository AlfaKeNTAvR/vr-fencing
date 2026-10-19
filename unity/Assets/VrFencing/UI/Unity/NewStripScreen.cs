using System;
using TMPro;
using UnityEngine;

namespace VrFencing.UI
{
    /// <summary>
    /// "New Strip here?" (PER-91), asked at launch when no saved Strip is in this room but another room's Strip was
    /// found. Place a new Strip (it is added as this room's, the other rooms keep theirs), or keep the last Strip
    /// where it is for now.
    /// </summary>
    internal sealed class NewStripScreen
    {
        private readonly GameObject _root;
        private readonly TextMeshPro _details;

        public NewStripScreen(Transform panel, Action placeNew, Action keepLast)
        {
            _root = new GameObject("New Strip");
            _root.transform.SetParent(panel, false);
            var root = _root.transform;

            UiKit.CreateText(root, "Title", new Vector2(0.0f, 0.2f), new Vector2(0.72f, 0.07f), 1, UiKit.TitleFontSize,
                TextAlignmentOptions.Center, UiKit.TextColor).text = "New Strip here?";
            _details = UiKit.CreateText(root, "Details", new Vector2(0.0f, 0.05f), new Vector2(0.72f, 0.18f), 1, UiKit.SmallFontSize,
                TextAlignmentOptions.Center, UiKit.DimTextColor);

            var buttonSize = new Vector2(0.32f, 0.07f);
            UiButton.Create(root, "Place New", new Vector2(-0.18f, -0.15f), buttonSize, UiKit.BodyFontSize, placeNew).SetLabel("Place a new Strip");
            UiButton.Create(root, "Keep Last", new Vector2(0.18f, -0.15f), buttonSize, UiKit.BodyFontSize, keepLast).SetLabel("Keep the last Strip");
        }

        public void SetVisible(bool isVisible)
        {
            _root.SetActive(isVisible);
        }

        public void Refresh(string details)
        {
            UiKit.SetText(_details, details);
        }
    }
}
