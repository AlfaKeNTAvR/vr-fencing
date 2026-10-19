using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using VrFencing.Session;

namespace VrFencing.UI
{
    /// <summary>
    /// The history view (PER-27): every Drill with saved runs on the left; for the chosen one, its newest run's
    /// results and its last-10-sessions trend on the right.
    /// </summary>
    internal sealed class HistoryScreen
    {
        private const int RowsPerPage = 5;

        private readonly GameObject _root;
        private readonly UiButton[] _rows = new UiButton[RowsPerPage];
        private readonly UiButton _previousPage;
        private readonly UiButton _nextPage;
        private readonly TextMeshPro _emptyText;
        private readonly TextMeshPro _lastRunText;
        private readonly TrendChart _trend;

        private IReadOnlyList<HistoryEntry> _entries = Array.Empty<HistoryEntry>();
        private SessionHistory _history = new SessionHistory(Array.Empty<DrillRunRecord>());
        private TimeSpan _localOffset;
        private int _selected;
        private int _pageIndex;

        public HistoryScreen(Transform panel, Action back)
        {
            _root = new GameObject("History");
            _root.transform.SetParent(panel, false);
            var root = _root.transform;

            UiKit.CreateText(root, "Title", new Vector2(-0.15f, 0.235f), new Vector2(0.42f, 0.06f), 1, UiKit.TitleFontSize,
                TextAlignmentOptions.MidlineLeft, UiKit.TextColor).text = "History";

            for (var row = 0; row < RowsPerPage; row++)
            {
                var rowIndex = row;
                _rows[row] = UiButton.Create(root, $"History Row {row}", new Vector2(-0.2f, 0.16f - (row * 0.07f)), new Vector2(0.32f, 0.06f),
                    UiKit.SmallFontSize, () => Select(rowIndex));
            }

            _emptyText = UiKit.CreateText(root, "No History", new Vector2(-0.2f, 0.02f), new Vector2(0.3f, 0.2f), 1, UiKit.BodyFontSize,
                TextAlignmentOptions.Center, UiKit.DimTextColor);
            _previousPage = UiButton.Create(root, "Previous Page", new Vector2(-0.31f, -0.19f), new Vector2(0.08f, 0.05f), UiKit.SmallFontSize,
                () => ChangePage(-1));
            _previousPage.SetLabel("<");
            _nextPage = UiButton.Create(root, "Next Page", new Vector2(-0.09f, -0.19f), new Vector2(0.08f, 0.05f), UiKit.SmallFontSize,
                () => ChangePage(1));
            _nextPage.SetLabel(">");

            _lastRunText = UiKit.CreateText(root, "Last Run", new Vector2(0.18f, 0.135f), new Vector2(0.36f, 0.15f), 1, UiKit.TinyFontSize,
                TextAlignmentOptions.TopLeft, UiKit.TextColor);
            _trend = new TrendChart(root, new Vector2(0.18f, -0.08f), new Vector2(0.34f, 0.26f));
            UiButton.Create(root, "Back", new Vector2(0.27f, -0.25f), new Vector2(0.18f, 0.055f), UiKit.BodyFontSize, back).SetLabel("Back");
        }

        public void SetVisible(bool isVisible)
        {
            _root.SetActive(isVisible);
        }

        public void Refresh(SessionHistory history, IReadOnlyList<HistoryEntry> entries, TimeSpan localOffset)
        {
            _history = history;
            _entries = entries;
            _localOffset = localOffset;
            _selected = Math.Min(Math.Max(_selected, 0), Math.Max(entries.Count - 1, 0));
            Redraw();
        }

        private void Redraw()
        {
            var page = PageWindow.For(_entries.Count, RowsPerPage, _pageIndex);
            _pageIndex = page.PageIndex;
            for (var row = 0; row < RowsPerPage; row++)
            {
                var isShown = row < page.ItemCount;
                _rows[row].SetVisible(isShown);
                if (isShown)
                {
                    _rows[row].SetLabel(_entries[page.FirstItem + row].Label);
                    _rows[row].SetSelected(page.FirstItem + row == _selected);
                }
            }

            var hasPages = page.PageCount > 1;
            _previousPage.SetVisible(hasPages);
            _nextPage.SetVisible(hasPages);
            _previousPage.SetInteractable(page.HasPrevious);
            _nextPage.SetInteractable(page.HasNext);
            UiKit.SetText(_emptyText, _entries.Count == 0 ? "No saved runs yet" : string.Empty);

            if (_entries.Count == 0)
            {
                UiKit.SetText(_lastRunText, string.Empty);
                _trend.Show(Array.Empty<TrendBar>());
                return;
            }

            var entry = _entries[_selected];
            var lastRun = entry.LastRun;
            UiKit.SetText(_lastRunText,
                "Last run, " + lastRun.FinishedAt.ToOffset(_localOffset).ToString("M/d HH:mm", CultureInfo.InvariantCulture) + "\n" +
                ResultsScreen.FormatSummary(lastRun));
            _trend.Show(ResultsText.FormatTrend(_history.GetTrend(entry.DrillId, entry.IsShrunk), _localOffset));
        }

        private void Select(int row)
        {
            _selected = PageWindow.For(_entries.Count, RowsPerPage, _pageIndex).FirstItem + row;
            Redraw();
        }

        private void ChangePage(int step)
        {
            _pageIndex += step;
            Redraw();
        }
    }
}
