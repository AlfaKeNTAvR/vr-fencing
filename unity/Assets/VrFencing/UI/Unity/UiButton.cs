using System;
using TMPro;
using UnityEngine;

namespace VrFencing.UI
{
    /// <summary>
    /// A panel button the off-hand pointer can press: a quad with a label, and optionally a note on its right
    /// (the Drill picker's "Needs a 6.0 m Strip"). Greyed out when not interactable, highlighted when hovered or selected.
    /// </summary>
    internal sealed class UiButton : MonoBehaviour
    {
        private Renderer _background;
        private TextMeshPro _label;
        private TextMeshPro _note;
        private Action _onClick;
        private bool _isHovered;
        private bool _isSelected;

        public bool IsInteractable { get; private set; } = true;

        /// <summary>Holding the trigger on the button presses it again and again (the settings steps).</summary>
        public bool RepeatsWhileHeld { get; set; }

        public string Label => _label.text;

        /// <param name="hasNote">Splits the button into a left-aligned label and a right-aligned note.</param>
        public static UiButton Create(
            Transform parent, string name, Vector2 center, Vector2 size, float fontSize, Action onClick, bool hasNote = false)
        {
            var buttonObject = new GameObject(name);
            buttonObject.transform.SetParent(parent, false);
            buttonObject.transform.localPosition = new Vector3(center.x, center.y, 0.0f);

            var collider = buttonObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.center = new Vector3(0.0f, 0.0f, -UiKit.LayerStepMeters);
            collider.size = new Vector3(size.x, size.y, 0.01f);

            var button = buttonObject.AddComponent<UiButton>();
            button._onClick = onClick;
            button._background = UiKit.CreateQuad(buttonObject.transform, "Background", Vector2.zero, size, 1, UiKit.ButtonColor);

            var padding = 0.015f;
            if (hasNote)
            {
                var labelWidth = size.x * 0.55f;
                var noteWidth = size.x - labelWidth - (3.0f * padding);
                button._label = UiKit.CreateText(buttonObject.transform, "Label",
                    new Vector2((-size.x * 0.5f) + padding + (labelWidth * 0.5f), 0.0f), new Vector2(labelWidth, size.y), 2,
                    fontSize, TextAlignmentOptions.MidlineLeft, UiKit.TextColor);
                button._note = UiKit.CreateText(buttonObject.transform, "Note",
                    new Vector2((size.x * 0.5f) - padding - (noteWidth * 0.5f), 0.0f), new Vector2(noteWidth, size.y), 2,
                    fontSize * 0.8f, TextAlignmentOptions.MidlineRight, UiKit.NoteTextColor);
            }
            else
            {
                button._label = UiKit.CreateText(buttonObject.transform, "Label", Vector2.zero, new Vector2(size.x - (2.0f * padding), size.y), 2,
                    fontSize, TextAlignmentOptions.Center, UiKit.TextColor);
            }

            // One line per button: a long name ends in an ellipsis instead of spilling out of the button.
            button._label.textWrappingMode = TextWrappingModes.NoWrap;
            if (button._note != null)
            {
                button._note.textWrappingMode = TextWrappingModes.NoWrap;
            }

            return button;
        }

        public void SetLabel(string label)
        {
            UiKit.SetText(_label, label);
        }

        public void SetNote(string note)
        {
            if (_note != null)
            {
                UiKit.SetText(_note, note);
            }
        }

        public void SetInteractable(bool isInteractable)
        {
            if (IsInteractable == isInteractable)
            {
                return;
            }

            IsInteractable = isInteractable;
            UpdateLook();
        }

        public void SetSelected(bool isSelected)
        {
            if (_isSelected == isSelected)
            {
                return;
            }

            _isSelected = isSelected;
            UpdateLook();
        }

        public void SetHovered(bool isHovered)
        {
            if (_isHovered == isHovered)
            {
                return;
            }

            _isHovered = isHovered;
            UpdateLook();
        }

        public void SetVisible(bool isVisible)
        {
            if (gameObject.activeSelf != isVisible)
            {
                gameObject.SetActive(isVisible);
            }
        }

        /// <summary>Runs the button's action if it is interactable. Called by the pointer on a trigger press.</summary>
        public void Press()
        {
            if (IsInteractable && gameObject.activeInHierarchy)
            {
                _onClick?.Invoke();
            }
        }

        private void UpdateLook()
        {
            Color background;
            if (!IsInteractable)
            {
                background = UiKit.DisabledColor;
            }
            else if (_isHovered)
            {
                background = UiKit.HoverColor;
            }
            else
            {
                background = _isSelected ? UiKit.SelectedColor : UiKit.ButtonColor;
            }

            UiKit.SetColor(_background, background);
            _label.color = IsInteractable ? UiKit.TextColor : UiKit.DimTextColor;
        }
    }
}
