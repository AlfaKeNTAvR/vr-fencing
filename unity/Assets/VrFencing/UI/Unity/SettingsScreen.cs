using System;
using TMPro;
using UnityEngine;
using VrFencing.Settings;

namespace VrFencing.UI
{
    /// <summary>
    /// The settings screen: every Setting (<see cref="SettingsPage.All"/>) with a step down and up, two pages, and a
    /// reserved line for the Blade's per-event haptic choices, which arrive with the Blade (PER-33). Holding the trigger
    /// on a step button repeats it. Changes are saved at once by the caller.
    /// </summary>
    internal sealed class SettingsScreen
    {
        public const string BladeHapticsNote = "Blade haptics: coming with the Blade";

        private const int RowsPerPage = 7;
        private const float FirstRowY = 0.165f;
        private const float RowStep = 0.055f;

        private readonly GameObject _root;
        private readonly Row[] _rows = new Row[RowsPerPage];
        private readonly TextMeshPro _messageText;
        private readonly TextMeshPro _pageText;
        private readonly UiButton _previousPage;
        private readonly UiButton _nextPage;
        private readonly Action<SettingId, int> _changeSetting;

        private TrainerSettings _settings = new TrainerSettings();
        private int _pageIndex;

        public SettingsScreen(Transform panel, Action<SettingId, int> changeSetting, Action back)
        {
            _changeSetting = changeSetting;
            _root = new GameObject("Settings");
            _root.transform.SetParent(panel, false);
            var root = _root.transform;

            UiKit.CreateText(root, "Title", new Vector2(-0.15f, 0.235f), new Vector2(0.42f, 0.06f), 1, UiKit.TitleFontSize,
                TextAlignmentOptions.MidlineLeft, UiKit.TextColor).text = "Settings";
            _messageText = UiKit.CreateText(root, "Message", new Vector2(0.2f, 0.235f), new Vector2(0.32f, 0.06f), 1, UiKit.TinyFontSize,
                TextAlignmentOptions.MidlineRight, UiKit.NoteTextColor);

            for (var row = 0; row < RowsPerPage; row++)
            {
                _rows[row] = new Row(root, row, FirstRowY - (row * RowStep), this);
            }

            _previousPage = UiButton.Create(root, "Previous Page", new Vector2(-0.31f, -0.25f), new Vector2(0.08f, 0.05f), UiKit.SmallFontSize,
                () => ChangePage(-1));
            _previousPage.SetLabel("<");
            _pageText = UiKit.CreateText(root, "Page", new Vector2(-0.2f, -0.25f), new Vector2(0.12f, 0.05f), 1, UiKit.SmallFontSize,
                TextAlignmentOptions.Center, UiKit.DimTextColor);
            _nextPage = UiButton.Create(root, "Next Page", new Vector2(-0.09f, -0.25f), new Vector2(0.08f, 0.05f), UiKit.SmallFontSize,
                () => ChangePage(1));
            _nextPage.SetLabel(">");
            var backButton = UiButton.Create(root, "Back", new Vector2(0.27f, -0.25f), new Vector2(0.18f, 0.055f), UiKit.BodyFontSize, back);
            backButton.SetLabel("Back");
        }

        /// <summary>Settings rows, then the reserved Blade haptics line.</summary>
        private static int ItemCount => SettingsPage.All.Count + 1;

        public void SetVisible(bool isVisible)
        {
            _root.SetActive(isVisible);
        }

        public void Refresh(TrainerSettings settings, string message)
        {
            _settings = settings;
            UiKit.SetText(_messageText, message);
            Redraw();
        }

        private void Redraw()
        {
            var page = PageWindow.For(ItemCount, RowsPerPage, _pageIndex);
            _pageIndex = page.PageIndex;
            for (var row = 0; row < RowsPerPage; row++)
            {
                var item = page.FirstItem + row;
                if (row >= page.ItemCount)
                {
                    _rows[row].ShowNothing();
                }
                else if (item < SettingsPage.All.Count)
                {
                    var id = SettingsPage.All[item];
                    _rows[row].ShowSetting(id, SettingsPage.GetLabel(id), SettingsPage.FormatValue(_settings, id));
                }
                else
                {
                    _rows[row].ShowNote(BladeHapticsNote);
                }
            }

            _previousPage.SetInteractable(page.HasPrevious);
            _nextPage.SetInteractable(page.HasNext);
            UiKit.SetText(_pageText, $"{page.PageIndex + 1} / {page.PageCount}");
        }

        private void ChangePage(int step)
        {
            _pageIndex += step;
            Redraw();
        }

        /// <summary>One line: the Setting's name, a step down, its value, a step up.</summary>
        private sealed class Row
        {
            private readonly TextMeshPro _label;
            private readonly TextMeshPro _value;
            private readonly UiButton _down;
            private readonly UiButton _up;

            private SettingId? _setting;

            public Row(Transform root, int index, float y, SettingsScreen screen)
            {
                _label = UiKit.CreateText(root, $"Setting {index} Label", new Vector2(-0.165f, y), new Vector2(0.4f, 0.055f), 1,
                    UiKit.SmallFontSize, TextAlignmentOptions.MidlineLeft, UiKit.TextColor);
                _down = UiButton.Create(root, $"Setting {index} Down", new Vector2(0.08f, y), new Vector2(0.065f, 0.046f), UiKit.BodyFontSize,
                    () => Step(screen, -1));
                _down.SetLabel("-");
                _down.RepeatsWhileHeld = true;
                _value = UiKit.CreateText(root, $"Setting {index} Value", new Vector2(0.19f, y), new Vector2(0.14f, 0.055f), 1,
                    UiKit.SmallFontSize, TextAlignmentOptions.Center, UiKit.TextColor);
                _up = UiButton.Create(root, $"Setting {index} Up", new Vector2(0.3f, y), new Vector2(0.065f, 0.046f), UiKit.BodyFontSize,
                    () => Step(screen, 1));
                _up.SetLabel("+");
                _up.RepeatsWhileHeld = true;
            }

            public void ShowSetting(SettingId id, string label, string value)
            {
                _setting = id;
                _label.color = UiKit.TextColor;
                UiKit.SetText(_label, label);
                UiKit.SetText(_value, value);
                _down.SetVisible(true);
                _up.SetVisible(true);
            }

            public void ShowNote(string note)
            {
                _setting = null;
                _label.color = UiKit.DimTextColor;
                UiKit.SetText(_label, note);
                UiKit.SetText(_value, string.Empty);
                _down.SetVisible(false);
                _up.SetVisible(false);
            }

            public void ShowNothing()
            {
                ShowNote(string.Empty);
            }

            private void Step(SettingsScreen screen, int direction)
            {
                if (_setting.HasValue)
                {
                    screen._changeSetting(_setting.Value, direction);
                }
            }
        }
    }
}
