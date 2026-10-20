using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using VrFencing.Drills;

namespace VrFencing.UI
{
    /// <summary>
    /// The home screen: the Drill picker (easy to hard, each Drill's task line under its name, greyed out with the
    /// required Strip length when a Drill does not fit) and the flow buttons: start the selected Drill, the Partner page,
    /// settings, history, re-place the Strip, and the off-strip tolerance (the same as the off-hand lower face button).
    /// While connected to a partner, Start is off: solo Drills wait until the Partner session is left (PER-58).
    /// </summary>
    internal sealed class HomeScreen
    {
        // Each row has two lines: the Drill's name (and note), then its task line (PER-92). Four fit above the page buttons.
        private const int RowsPerPage = 4;
        private const float RowHeightMeters = 0.08f;
        private const float RowStepMeters = 0.09f;
        private const float FirstRowCenterY = 0.155f;

        private readonly GameObject _root;
        private readonly TextMeshPro _stripText;
        private readonly TextMeshPro _messageText;
        private readonly TextMeshPro _emptyText;
        private readonly TextMeshPro _pageText;
        private readonly UiButton[] _rows = new UiButton[RowsPerPage];
        private readonly UiButton _previousPage;
        private readonly UiButton _nextPage;
        private readonly UiButton _start;
        private readonly UiButton _tolerance;
        private readonly UiButton _partner;

        private IReadOnlyList<DrillPickerEntry> _entries = Array.Empty<DrillPickerEntry>();
        private int _pageIndex;
        private bool _areSoloDrillsAllowed = true;

        public HomeScreen(
            Transform panel,
            Action<string> startDrill,
            Action openPartner,
            Action openSettings,
            Action openHistory,
            Action replaceStrip,
            Action cycleTolerance)
        {
            _root = new GameObject("Home");
            _root.transform.SetParent(panel, false);
            var root = _root.transform;

            UiKit.CreateText(root, "Title", new Vector2(-0.13f, 0.235f), new Vector2(0.46f, 0.06f), 1, UiKit.TitleFontSize,
                TextAlignmentOptions.MidlineLeft, UiKit.TextColor).text = "Drills";
            _stripText = UiKit.CreateText(root, "Strip", new Vector2(0.24f, 0.235f), new Vector2(0.26f, 0.06f), 1, UiKit.SmallFontSize,
                TextAlignmentOptions.MidlineRight, UiKit.DimTextColor);

            for (var row = 0; row < RowsPerPage; row++)
            {
                var rowIndex = row;
                _rows[row] = UiButton.Create(root, $"Drill Row {row}", new Vector2(-0.13f, FirstRowCenterY - (row * RowStepMeters)),
                    new Vector2(0.46f, RowHeightMeters), UiKit.SmallFontSize, () => SelectRow(rowIndex), hasNote: true, hasDetail: true);
            }

            _emptyText = UiKit.CreateText(root, "No Drills", new Vector2(-0.13f, 0.02f), new Vector2(0.44f, 0.2f), 1, UiKit.BodyFontSize,
                TextAlignmentOptions.Center, UiKit.DimTextColor);
            _previousPage = UiButton.Create(root, "Previous Page", new Vector2(-0.31f, -0.19f), new Vector2(0.08f, 0.05f), UiKit.SmallFontSize,
                () => ChangePage(-1));
            _previousPage.SetLabel("<");
            _pageText = UiKit.CreateText(root, "Page", new Vector2(-0.13f, -0.19f), new Vector2(0.2f, 0.05f), 1, UiKit.SmallFontSize,
                TextAlignmentOptions.Center, UiKit.DimTextColor);
            _nextPage = UiButton.Create(root, "Next Page", new Vector2(0.05f, -0.19f), new Vector2(0.08f, 0.05f), UiKit.SmallFontSize,
                () => ChangePage(1));
            _nextPage.SetLabel(">");

            var buttonSize = new Vector2(0.21f, 0.06f);
            _start = UiButton.Create(root, "Start", new Vector2(0.255f, 0.17f), buttonSize, UiKit.BodyFontSize, () =>
            {
                if (SelectedDrillId != null && _areSoloDrillsAllowed)
                {
                    startDrill(SelectedDrillId);
                }
            });
            _start.SetLabel("Start");
            _partner = UiButton.Create(root, "Partner", new Vector2(0.255f, 0.10f), buttonSize, UiKit.BodyFontSize, openPartner);
            _partner.SetLabel("Partner");
            CreateButton(root, "Settings", new Vector2(0.255f, 0.03f), buttonSize, "Settings", openSettings);
            CreateButton(root, "History", new Vector2(0.255f, -0.04f), buttonSize, "History", openHistory);
            CreateButton(root, "Re-place Strip", new Vector2(0.255f, -0.11f), buttonSize, "Re-place Strip", replaceStrip);
            _tolerance = UiButton.Create(root, "Tolerance", new Vector2(0.255f, -0.18f), buttonSize, UiKit.SmallFontSize, cycleTolerance);

            _messageText = UiKit.CreateText(root, "Message", new Vector2(0.0f, -0.25f), new Vector2(0.72f, 0.045f), 1, UiKit.SmallFontSize,
                TextAlignmentOptions.Center, UiKit.NoteTextColor);
        }

        /// <summary>The Drill the Start button runs, or null when none is available.</summary>
        public string SelectedDrillId { get; private set; }

        public void SetVisible(bool isVisible)
        {
            _root.SetActive(isVisible);
        }

        /// <param name="partnerLabel">The Partner button's label, naming the session's state.</param>
        /// <param name="areSoloDrillsAllowed">False while in a Partner session: Start is off.</param>
        public void Refresh(
            IReadOnlyList<DrillPickerEntry> entries, string stripText, float toleranceMeters, string message, string partnerLabel,
            bool areSoloDrillsAllowed)
        {
            _entries = entries;
            _areSoloDrillsAllowed = areSoloDrillsAllowed;
            _partner.SetLabel(partnerLabel);
            UiKit.SetText(_stripText, stripText);
            UiKit.SetText(_messageText, message);
            _tolerance.SetLabel($"Off-strip: {toleranceMeters:0.0} m");
            RedrawList();
        }

        private void RedrawList()
        {
            var entries = _entries;
            SelectedDrillId = DrillPicker.ResolveSelection(entries, SelectedDrillId);
            _start.SetInteractable(SelectedDrillId != null && _areSoloDrillsAllowed);

            var page = PageWindow.For(entries.Count, RowsPerPage, _pageIndex);
            _pageIndex = page.PageIndex;
            for (var row = 0; row < RowsPerPage; row++)
            {
                var isShown = row < page.ItemCount;
                _rows[row].SetVisible(isShown);
                if (!isShown)
                {
                    continue;
                }

                var entry = entries[page.FirstItem + row];
                _rows[row].SetLabel(entry.Drill.Name);
                _rows[row].SetNote(entry.Note);
                _rows[row].SetDetail(entry.Drill.TaskLine);
                _rows[row].SetInteractable(entry.IsAvailable);
                _rows[row].SetSelected(entry.Drill.Id == SelectedDrillId);
            }

            UiKit.SetText(_emptyText, entries.Count == 0 ? "No Drills yet" : string.Empty);
            var hasPages = page.PageCount > 1;
            _previousPage.SetVisible(hasPages);
            _nextPage.SetVisible(hasPages);
            _previousPage.SetInteractable(page.HasPrevious);
            _nextPage.SetInteractable(page.HasNext);
            UiKit.SetText(_pageText, hasPages ? $"{page.PageIndex + 1} / {page.PageCount}" : string.Empty);
        }

        private void SelectRow(int row)
        {
            var index = PageWindow.For(_entries.Count, RowsPerPage, _pageIndex).FirstItem + row;
            if (index < _entries.Count && _entries[index].IsAvailable)
            {
                SelectedDrillId = _entries[index].Drill.Id;
                RedrawList();
            }
        }

        private void ChangePage(int step)
        {
            _pageIndex += step;
            RedrawList();
        }

        private static void CreateButton(Transform root, string name, Vector2 center, Vector2 size, string label, Action onClick)
        {
            UiButton.Create(root, name, center, size, UiKit.BodyFontSize, onClick).SetLabel(label);
        }
    }
}
