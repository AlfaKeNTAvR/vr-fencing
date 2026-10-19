using System;
using TMPro;
using UnityEngine;

namespace VrFencing.UI
{
    /// <summary>
    /// The small panel shown during a Drill, low and to the off-hand side so it stays out of the way of the Dummy:
    /// the Drill and Rep, the cue or why it is paused, and the flow buttons pause/resume, cancel and flag Rep.
    /// </summary>
    internal sealed class RunningPanel
    {
        public static readonly Vector2 Size = new Vector2(0.5f, 0.18f);

        private const float FlaggedFeedbackSeconds = 1.5f;

        private readonly TextMeshPro _title;
        private readonly TextMeshPro _instruction;
        private readonly UiButton _pause;
        private readonly UiButton _flag;

        private double _flaggedUntilSeconds;

        public RunningPanel(Transform panel, Action togglePause, Action cancel, Action flagRep)
        {
            UiSurface.Create(panel, "Background", Size);
            _title = UiKit.CreateText(panel, "Title", new Vector2(0.0f, 0.06f), new Vector2(0.47f, 0.04f), 1, UiKit.SmallFontSize,
                TextAlignmentOptions.Center, UiKit.DimTextColor);
            _instruction = UiKit.CreateText(panel, "Instruction", new Vector2(0.0f, 0.012f), new Vector2(0.47f, 0.05f), 1, UiKit.BodyFontSize,
                TextAlignmentOptions.Center, UiKit.NoteTextColor);

            var buttonSize = new Vector2(0.14f, 0.05f);
            _pause = UiButton.Create(panel, "Pause", new Vector2(-0.16f, -0.05f), buttonSize, UiKit.SmallFontSize, togglePause);
            UiButton.Create(panel, "Cancel", new Vector2(0.0f, -0.05f), buttonSize, UiKit.SmallFontSize, cancel).SetLabel("Cancel");
            _flag = UiButton.Create(panel, "Flag Rep", new Vector2(0.16f, -0.05f), buttonSize, UiKit.SmallFontSize, () =>
            {
                flagRep();
                _flaggedUntilSeconds = Time.timeAsDouble + FlaggedFeedbackSeconds;
            });
        }

        public void Refresh(RunStatus status, bool isPausedByFencer, double nowSeconds)
        {
            UiKit.SetText(_title, status.Title);
            UiKit.SetText(_instruction, status.Instruction);
            _pause.SetLabel(isPausedByFencer ? "Resume" : "Pause");
            _flag.SetLabel(nowSeconds < _flaggedUntilSeconds ? "Flagged" : "Flag Rep");
        }
    }
}
