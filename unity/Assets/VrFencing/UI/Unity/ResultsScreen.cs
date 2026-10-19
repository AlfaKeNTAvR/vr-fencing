using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using VrFencing.Session;

namespace VrFencing.UI
{
    /// <summary>
    /// The results panel shown after each Drill (PER-27): success %, average reaction time and tip speed, the hit map
    /// per Target Zone, and the Drill's last-10-sessions trend. Buttons: run the Drill again, open the history, done.
    /// </summary>
    internal sealed class ResultsScreen
    {
        private readonly GameObject _root;
        private readonly TextMeshPro _title;
        private readonly TextMeshPro _summary;
        private readonly TextMeshPro _hitMap;
        private readonly TrendChart _trend;
        private readonly UiButton _runAgain;

        public ResultsScreen(Transform panel, Action runAgain, Action openHistory, Action done)
        {
            _root = new GameObject("Results");
            _root.transform.SetParent(panel, false);
            var root = _root.transform;

            _title = UiKit.CreateText(root, "Title", new Vector2(0.0f, 0.235f), new Vector2(0.72f, 0.06f), 1, UiKit.TitleFontSize,
                TextAlignmentOptions.MidlineLeft, UiKit.TextColor);
            _summary = UiKit.CreateText(root, "Summary", new Vector2(-0.175f, 0.09f), new Vector2(0.37f, 0.2f), 1, UiKit.SmallFontSize,
                TextAlignmentOptions.TopLeft, UiKit.TextColor);
            _hitMap = UiKit.CreateText(root, "Hit Map", new Vector2(-0.175f, -0.11f), new Vector2(0.37f, 0.19f), 1, UiKit.TinyFontSize,
                TextAlignmentOptions.TopLeft, UiKit.TextColor);
            _trend = new TrendChart(root, new Vector2(0.19f, -0.01f), new Vector2(0.33f, 0.36f));

            var buttonSize = new Vector2(0.2f, 0.055f);
            _runAgain = UiButton.Create(root, "Run Again", new Vector2(-0.24f, -0.245f), buttonSize, UiKit.BodyFontSize, runAgain);
            _runAgain.SetLabel("Run again");
            UiButton.Create(root, "History", new Vector2(0.0f, -0.245f), buttonSize, UiKit.BodyFontSize, openHistory).SetLabel("History");
            UiButton.Create(root, "Done", new Vector2(0.24f, -0.245f), buttonSize, UiKit.BodyFontSize, done).SetLabel("Done");
        }

        public void SetVisible(bool isVisible)
        {
            _root.SetActive(isVisible);
        }

        /// <param name="record">The run just finished, or null if none was saved.</param>
        /// <param name="canRunAgain">The Drill is still available on this Strip.</param>
        public void Refresh(string drillName, DrillRunRecord record, IReadOnlyList<TrendBar> trend, bool canRunAgain)
        {
            _runAgain.SetInteractable(canRunAgain);
            if (record == null)
            {
                UiKit.SetText(_title, "No results");
                UiKit.SetText(_summary, string.Empty);
                UiKit.SetText(_hitMap, string.Empty);
                _trend.Show(Array.Empty<TrendBar>());
                return;
            }

            UiKit.SetText(_title, $"{drillName}: results");
            UiKit.SetText(_summary, FormatSummary(record));
            UiKit.SetText(_hitMap, FormatHitMap(record.Results));
            _trend.Show(trend);
        }

        public static string FormatSummary(DrillRunRecord record)
        {
            return string.Join("\n", ResultsText.FormatSummary(record.Results, record.WasCancelled));
        }

        public static string FormatHitMap(DrillResults results)
        {
            var lines = ResultsText.FormatHitMap(results).Select(line => $"{line.ZoneName}: {line.CountText}");
            return "Hit map\n" + string.Join("\n", lines);
        }
    }
}
