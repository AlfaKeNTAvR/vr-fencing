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
    /// it too), and lights the zone as well when the Drill does (<see cref="DrillDefinition.LightsCuedZone"/>). A
    /// compound lights its first zone with both names above the head, then, on the first Touch, flashes it and lights
    /// the second; a footwork call shows the action above the head. All go away when the Rep is judged,
    /// the run pauses or ends; a successful Touch flashes the zone. After a Rep that read footwork, the label says what was
    /// read ("read: step-lunge, 1.2 m") until the next cue, with "Back to the line" under it while the run waits for the
    /// Fencer at the Start Mark (PER-88, PER-89). Before the first Rep the label holds the Drill's task line
    /// (<see cref="ShowTaskLine"/>, PER-92).
    /// What the label says is decided in the UI Core (<see cref="RunStatusText.FormatDummyLabel"/>).
    /// </summary>
    internal sealed class DummyCueDisplay : IDisposable
    {
        // Above the head, so the label never covers a Target Zone.
        private const float LabelAboveHeadMeters = 0.15f;

        // At 2 to 3 m away, a 10 cm em reads at a glance.
        private const float LabelFontSize = 1.0f;

        // A task line is a sentence, not a word: half the size, wrapped onto two lines if it must be.
        private const float TaskLineFontSize = 0.5f;
        private static readonly Vector2 LabelSize = new Vector2(1.4f, 0.3f);

        // The TMP Essentials' outline preset of the default font: a black outline keeps it readable over any room.
        private const string OutlineMaterialResourcePath = "Fonts & Materials/LiberationSans SDF - Outline";

        private readonly DrillRunner _runner;
        private readonly DummyBehaviour _dummy;
        private readonly TextMeshPro _label;

        // What the last judged Rep's footwork read as; empty when it read none.
        private string _readLine = string.Empty;

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
            _runner.ReturnPrompted += OnReturnPrompted;
            _runner.Resumed += OnResumed;
        }

        public void Dispose()
        {
            _runner.RepCued -= OnRepCued;
            _runner.SecondZoneCued -= OnSecondZoneCued;
            _runner.RepJudged -= OnRepJudged;
            _runner.StateChanged -= OnStateChanged;
            _runner.Paused -= HideCue;
            _runner.ReturnPrompted -= OnReturnPrompted;
            _runner.Resumed -= OnResumed;
            if (_label != null)
            {
                UiKit.DestroyObject(_label.gameObject);
            }
        }

        private void OnRepCued(RepCue cue)
        {
            _readLine = string.Empty;
            _dummy.DimAllZones();
            if (_runner.Drill.LightsCuedZone && cue.TargetZone.HasValue)
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

            if (result.Footwork.HasValue)
            {
                _readLine = RepReadText.Format(result.Footwork.Value);
                ShowLabel(_readLine);
            }
        }

        private void OnReturnPrompted()
        {
            ShowLabel(RepReadText.Combine(_readLine, isPromptingReturn: true));
        }

        // A pause hid the label; still waiting for the Fencer, it comes back. A Rep in progress is cued again instead.
        private void OnResumed()
        {
            if (_runner.State == DrillState.Return || _runner.State == DrillState.Rest)
            {
                ShowLabel(RepReadText.Combine(_readLine, _runner.IsPromptingReturn));
            }
        }

        private void OnStateChanged(DrillState state)
        {
            if (state == DrillState.Judge || state == DrillState.Done)
            {
                HideCue();
            }
            else if (state == DrillState.Rest)
            {
                // Back at the Start Mark: the prompt goes, the read stays until the next cue.
                ShowLabel(_readLine);
            }
        }

        /// <summary>Shows a Drill's task line above the Dummy until the first cue replaces it; empty hides it.</summary>
        public void ShowTaskLine(string taskLine)
        {
            _dummy.DimAllZones();
            ShowLabel(taskLine);
            if (_label != null)
            {
                _label.fontSize = TaskLineFontSize;
                _label.textWrappingMode = TextWrappingModes.Normal;
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
            _label.fontSize = LabelFontSize;
            _label.textWrappingMode = TextWrappingModes.NoWrap;
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
