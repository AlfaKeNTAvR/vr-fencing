using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using VrFencing.Settings;

namespace VrFencing.UI
{
    /// <summary>
    /// The settings screen (PER-70): it opens on the list of groups (<see cref="SettingGroups"/>); picking one shows that
    /// group's Settings, each with a step down and up, and Back returns to the list. The Blade group also opens the grip
    /// page. Holding the trigger on a step button repeats it. Changes are saved at once by the caller.
    /// </summary>
    internal sealed class SettingsScreen
    {
        private const int RowCount = SettingGroups.MaxSettingsPerGroup;
        private const float FirstRowY = 0.165f;
        private const float RowStep = 0.055f;

        // The group list: two columns of up to four large buttons (seven groups since PER-100), between the title (its
        // bottom at 0.205) and the Back button (its top at -0.2225).
        private const int GroupColumns = 2;
        private const float GroupColumnX = 0.18f;
        private const float FirstGroupRowY = 0.15f;
        private const float GroupRowStep = 0.1f;
        private static readonly Vector2 GroupButtonSize = new Vector2(0.34f, 0.085f);

        private readonly GameObject _root;
        private readonly Row[] _rows = new Row[RowCount];
        private readonly UiButton[] _groupButtons;
        private readonly TextMeshPro _titleText;
        private readonly TextMeshPro _messageText;
        private readonly UiButton _gripButton;
        private readonly Action<SettingId, int> _changeSetting;

        private TrainerSettings _settings = new TrainerSettings();
        private SettingGroup? _group;

        /// <param name="openGroup">Shows one group's Settings.</param>
        /// <param name="openGrip">Opens the grip page: the angles compared and the thrust calibration (PER-52).</param>
        /// <param name="back">From a group back to the list of groups, from the list back to Home.</param>
        public SettingsScreen(
            Transform panel, Action<SettingId, int> changeSetting, Action<SettingGroup> openGroup, Action openGrip, Action back)
        {
            _changeSetting = changeSetting;
            _root = new GameObject("Settings");
            _root.transform.SetParent(panel, false);
            var root = _root.transform;

            _titleText = UiKit.CreateText(root, "Title", new Vector2(-0.15f, 0.235f), new Vector2(0.42f, 0.06f), 1, UiKit.TitleFontSize,
                TextAlignmentOptions.MidlineLeft, UiKit.TextColor);
            _messageText = UiKit.CreateText(root, "Message", new Vector2(0.2f, 0.235f), new Vector2(0.32f, 0.06f), 1, UiKit.TinyFontSize,
                TextAlignmentOptions.MidlineRight, UiKit.NoteTextColor);

            for (var row = 0; row < RowCount; row++)
            {
                _rows[row] = new Row(root, row, FirstRowY - (row * RowStep), this);
            }

            _groupButtons = new UiButton[SettingGroups.All.Count];
            for (var index = 0; index < _groupButtons.Length; index++)
            {
                var group = SettingGroups.All[index];
                var x = index % GroupColumns == 0 ? -GroupColumnX : GroupColumnX;
                var y = FirstGroupRowY - ((index / GroupColumns) * GroupRowStep);
                _groupButtons[index] = UiButton.Create(root, $"Group {SettingGroups.GetLabel(group)}", new Vector2(x, y), GroupButtonSize,
                    UiKit.BodyFontSize, () => openGroup(group));
                _groupButtons[index].SetLabel(SettingGroups.GetLabel(group));
            }

            _gripButton = UiButton.Create(root, "Grip Angles", new Vector2(0.075f, -0.25f), new Vector2(0.19f, 0.055f), UiKit.SmallFontSize, openGrip);
            _gripButton.SetLabel("Calibrate grip");
            var backButton = UiButton.Create(root, "Back", new Vector2(0.27f, -0.25f), new Vector2(0.18f, 0.055f), UiKit.BodyFontSize, back);
            backButton.SetLabel("Back");
        }

        public void SetVisible(bool isVisible)
        {
            _root.SetActive(isVisible);
        }

        /// <param name="group">The group to show, or null for the list of groups.</param>
        public void Refresh(TrainerSettings settings, SettingGroup? group, string message)
        {
            _settings = settings;
            _group = group;
            UiKit.SetText(_messageText, message);
            Redraw();
        }

        private void Redraw()
        {
            var isGroupList = !_group.HasValue;
            UiKit.SetText(_titleText, isGroupList ? "Settings" : $"Settings: {SettingGroups.GetLabel(_group.Value)}");
            foreach (var button in _groupButtons)
            {
                button.SetVisible(isGroupList);
            }

            IReadOnlyList<SettingId> ids = isGroupList ? Array.Empty<SettingId>() : SettingGroups.GetSettings(_group.Value);
            for (var row = 0; row < RowCount; row++)
            {
                if (row < ids.Count)
                {
                    var id = ids[row];
                    _rows[row].ShowSetting(id, SettingsPage.GetLabel(id), SettingsPage.FormatValue(_settings, id));
                }
                else
                {
                    _rows[row].ShowNothing();
                }
            }

            _gripButton.SetVisible(!isGroupList && SettingGroups.HasGripCalibration(_group.Value));
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
