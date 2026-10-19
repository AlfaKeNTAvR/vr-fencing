using System;
using TMPro;
using UnityEngine;
using VrFencing.Drills;
using VrFencing.Dummy;

namespace VrFencing.UI
{
    /// <summary>
    /// Shows each Rep's cue on the Dummy, where the Fencer is looking. A lit-zone cue lights the Target Zone; a
    /// named-zone cue shows the zone's name above the Dummy's head (the Running panel, low on the off-hand side, says
    /// it too). A compound lights its first zone with both names above the head, then, on the first Touch, flashes it
    /// and lights the second; a footwork call shows the action above the head. All go away when the Rep is judged,
    /// the run pauses or ends; a successful Touch flashes the zone.
    /// What the label says is decided in the UI Core (<see cref="RunStatusText.FormatDummyLabel"/>).
    /// </summary>
    internal sealed class DummyCueDisplay : IDisposable
    {
        // Above the head, so the label never covers a Target Zone.
        private const float LabelAboveHeadMeters = 0.15f;

        // At 2 to 3 m away, a 10 cm em reads at a glance.
        private const float LabelFontSize = 1.0f;
        private static readonly Vector2 LabelSize = new Vector2(1.4f, 0.3f);

        // The TMP Essentials' outline preset of the default font: a black outline keeps it readable over any room.
        private const string OutlineMaterialResourcePath = "Fonts & Materials/LiberationSans SDF - Outline";

        private readonly DrillRunner _runner;
        private readonly DummyBehaviour _dummy;
        private readonly TextMeshPro _label;

        public DummyCueDisplay(DrillRunner runner, DummyBehaviour dummy)
        {
            _runner = runner;
            _dummy = dummy;
            _label = CreateLabel(dummy.transform);
            _runner.RepCued += OnRepCued;
            _runner.SecondZoneCued += OnSecondZoneCued;
            _runner.RepJudged += OnRepJudged;
            _runner.StateChanged += OnStateChanged;
            _runner.Paused += HideCue;
        }

        public void Dispose()
        {
            _runner.RepCued -= OnRepCued;
            _runner.SecondZoneCued -= OnSecondZoneCued;
            _runner.RepJudged -= OnRepJudged;
            _runner.StateChanged -= OnStateChanged;
            _runner.Paused -= HideCue;
            if (_label != null)
            {
                UiKit.DestroyObject(_label.gameObject);
            }
        }

        private void OnRepCued(RepCue cue)
        {
            _dummy.DimAllZones();
            if (cue.CueType == CueType.LitTargetZone && cue.TargetZone.HasValue)
            {
                _dummy.LightZone(cue.TargetZone.Value);
            }

            ShowLabel(RunStatusText.FormatDummyLabel(cue));
        }

        // A compound's first Touch landed: flash it, light the second zone, and leave only the second's name up.
        private void OnSecondZoneCued(TargetZone secondZone)
        {
            var firstZone = _runner.CurrentCue.TargetZone;
            _dummy.DimAllZones();
            if (firstZone.HasValue)
            {
                _dummy.FlashZone(firstZone.Value);
            }

            _dummy.LightZone(secondZone);
            ShowLabel(TargetZoneNames.Get(secondZone));
        }

        private void OnRepJudged(RepResult result)
        {
            var touchedZone = result.RequestedSecondZone ?? result.RequestedZone;
            if (result.IsSuccess && touchedZone.HasValue)
            {
                _dummy.FlashZone(touchedZone.Value);
            }
        }

        private void OnStateChanged(DrillState state)
        {
            if (state == DrillState.Judge || state == DrillState.Done)
            {
                HideCue();
            }
        }

        private void HideCue()
        {
            _dummy.DimAllZones();
            ShowLabel(string.Empty);
        }

        private void ShowLabel(string text)
        {
            if (_label == null)
            {
                return;
            }

            // The Dummy is rebuilt when the Fencer's height changes, so the label follows its height on every cue.
            var heightMeters = _dummy.Layout?.HeightMeters ?? 0.0f;
            _label.transform.localPosition = new Vector3(0.0f, heightMeters + LabelAboveHeadMeters, 0.0f);
            UiKit.SetText(_label, text);
            _label.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }

        private static TextMeshPro CreateLabel(Transform dummyTransform)
        {
            var label = UiKit.CreateText(
                dummyTransform, "Cue Label", Vector2.zero, LabelSize, layer: 0, LabelFontSize, TextAlignmentOptions.Center, UiKit.NoteTextColor);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;

            // The Dummy faces the Fencer (turned 180 degrees on the Strip); turning the label back makes it read from the Fencer's side.
            label.transform.localRotation = Quaternion.Euler(0.0f, 180.0f, 0.0f);
            var outline = Resources.Load<Material>(OutlineMaterialResourcePath);
            if (outline != null)
            {
                label.fontSharedMaterial = outline;
            }

            label.gameObject.SetActive(false);
            return label;
        }
    }
}
