using System.Collections.Generic;
using TMPro;
using UnityEngine;
using VrFencing.Session;

namespace VrFencing.UI
{
    /// <summary>
    /// The "last 10 sessions" trend as a bar chart: one bar per session, its height the success rate, with the
    /// percentage above and the date below. Used by the results panel and the history view.
    /// </summary>
    internal sealed class TrendChart
    {
        private const float BarGapMeters = 0.006f;
        private const float MinimumBarHeightMeters = 0.003f;
        private const float LabelHeightMeters = 0.025f;

        private readonly Renderer[] _bars = new Renderer[SessionHistory.TrendSessionCount];
        private readonly TextMeshPro[] _values = new TextMeshPro[SessionHistory.TrendSessionCount];
        private readonly TextMeshPro[] _dates = new TextMeshPro[SessionHistory.TrendSessionCount];
        private readonly TextMeshPro _emptyText;
        private readonly float _barAreaBottom;
        private readonly float _barAreaHeight;
        private readonly float _barWidth;

        /// <param name="center">Center of the whole chart, captions included, in panel space.</param>
        public TrendChart(Transform parent, Vector2 center, Vector2 size)
        {
            var left = center.x - (size.x * 0.5f);
            var top = center.y + (size.y * 0.5f);
            UiKit.CreateText(parent, "Trend Caption", new Vector2(center.x, top - (LabelHeightMeters * 0.5f)), new Vector2(size.x, LabelHeightMeters), 1,
                UiKit.SmallFontSize, TextAlignmentOptions.Center, UiKit.DimTextColor).text = "Success, last 10 sessions";

            _barAreaBottom = center.y - (size.y * 0.5f) + LabelHeightMeters;
            _barAreaHeight = size.y - (3.0f * LabelHeightMeters);
            _barWidth = (size.x - (BarGapMeters * (_bars.Length - 1))) / _bars.Length;
            UiKit.CreateQuad(parent, "Trend Baseline", new Vector2(center.x, _barAreaBottom), new Vector2(size.x, 0.002f), 1, UiKit.DimTextColor);

            for (var i = 0; i < _bars.Length; i++)
            {
                var x = left + (_barWidth * 0.5f) + (i * (_barWidth + BarGapMeters));
                _bars[i] = UiKit.CreateQuad(parent, $"Trend Bar {i}", new Vector2(x, _barAreaBottom), new Vector2(_barWidth, MinimumBarHeightMeters), 2,
                    UiKit.BarColor);
                _values[i] = UiKit.CreateText(parent, $"Trend Value {i}", new Vector2(x, _barAreaBottom), new Vector2(_barWidth + BarGapMeters, LabelHeightMeters), 2,
                    UiKit.TinyFontSize, TextAlignmentOptions.Center, UiKit.TextColor);
                _dates[i] = UiKit.CreateText(parent, $"Trend Date {i}", new Vector2(x, _barAreaBottom - (LabelHeightMeters * 0.5f)),
                    new Vector2(_barWidth + BarGapMeters, LabelHeightMeters), 2, UiKit.TinyFontSize, TextAlignmentOptions.Center, UiKit.DimTextColor);
                _values[i].overflowMode = TextOverflowModes.Overflow;
                _values[i].textWrappingMode = TextWrappingModes.NoWrap;
                _dates[i].overflowMode = TextOverflowModes.Overflow;
                _dates[i].textWrappingMode = TextWrappingModes.NoWrap;
            }

            _emptyText = UiKit.CreateText(parent, "Trend Empty", new Vector2(center.x, _barAreaBottom + (_barAreaHeight * 0.5f)),
                new Vector2(size.x, LabelHeightMeters * 2.0f), 2, UiKit.SmallFontSize, TextAlignmentOptions.Center, UiKit.DimTextColor);
        }

        public void Show(IReadOnlyList<TrendBar> bars)
        {
            for (var i = 0; i < _bars.Length; i++)
            {
                var isShown = i < bars.Count;
                _bars[i].gameObject.SetActive(isShown);
                _values[i].gameObject.SetActive(isShown);
                _dates[i].gameObject.SetActive(isShown);
                if (!isShown)
                {
                    continue;
                }

                var height = Mathf.Max(MinimumBarHeightMeters, Mathf.Clamp01(bars[i].SuccessFraction) * _barAreaHeight);
                var barTransform = _bars[i].transform;
                barTransform.localScale = new Vector3(_barWidth, height, 1.0f);
                barTransform.localPosition = new Vector3(barTransform.localPosition.x, _barAreaBottom + (height * 0.5f), barTransform.localPosition.z);

                var valueTransform = _values[i].rectTransform;
                valueTransform.localPosition = new Vector3(valueTransform.localPosition.x, _barAreaBottom + height + (LabelHeightMeters * 0.5f),
                    valueTransform.localPosition.z);
                UiKit.SetText(_values[i], bars[i].ValueLabel);
                // Dates are wider than a bar: only the oldest and the newest are labelled.
                UiKit.SetText(_dates[i], i == 0 || i == bars.Count - 1 ? bars[i].DateLabel : string.Empty);
            }

            UiKit.SetText(_emptyText, bars.Count == 0 ? "No saved runs yet" : string.Empty);
        }
    }
}
