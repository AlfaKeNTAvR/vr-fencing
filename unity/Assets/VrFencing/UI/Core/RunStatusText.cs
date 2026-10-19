using VrFencing.Drills;
using VrFencing.Dummy;

namespace VrFencing.UI
{
    /// <summary>What the Running panel says: the Drill and Rep, and what the Fencer should do now.</summary>
    public readonly struct RunStatus
    {
        public RunStatus(string title, string instruction)
        {
            Title = title;
            Instruction = instruction;
        }

        /// <summary>"Lunge to target: Rep 3 of 10".</summary>
        public string Title { get; }

        /// <summary>The cue, rest, or why the Drill is paused.</summary>
        public string Instruction { get; }
    }

    /// <summary>The Running panel's text for the runner's current state.</summary>
    public static class RunStatusText
    {
        public const string OffStripText = "Paused: step back on the Strip";
        public const string PausedText = "Paused";
        public const string RestText = "Rest";
        public const string LitZoneText = "Touch the lit Target Zone";
        /// <param name="isFencerOnStrip">From the Strip; tells an off-strip pause from the Fencer's own.</param>
        public static RunStatus Format(DrillRunner runner, bool isFencerOnStrip)
        {
            var drill = runner.Drill;
            if (drill == null || !runner.IsRunning)
            {
                return new RunStatus(string.Empty, string.Empty);
            }

            var title = $"{drill.Name}: Rep {runner.CurrentRepIndex + 1} of {drill.RepCount}";
            return new RunStatus(title, FormatInstruction(runner, isFencerOnStrip));
        }

        /// <summary>
        /// The label above the Dummy for a Rep's cue: the Target Zone's name for a named-zone cue ("Upper arm"); for a
        /// compound, both zones in order ("Hand, then thigh") while the first is lit; the action, in capitals, for a
        /// footwork call ("ADVANCE"); empty for a single lit zone, whose light on the Dummy is the cue.
        /// </summary>
        public static string FormatDummyLabel(RepCue cue)
        {
            if (cue.FootworkCall.HasValue)
            {
                return FootworkNames.Get(cue.FootworkCall.Value).ToUpperInvariant();
            }

            if (cue.SecondTargetZone.HasValue && cue.TargetZone.HasValue)
            {
                return FormatCompound(cue.TargetZone.Value, cue.SecondTargetZone.Value);
            }

            return cue.CueType == CueType.NamedTargetZone && cue.TargetZone.HasValue ? TargetZoneNames.Get(cue.TargetZone.Value) : string.Empty;
        }

        private static string FormatCompound(TargetZone first, TargetZone second)
        {
            return $"{TargetZoneNames.Get(first)}, then {TargetZoneNames.Get(second).ToLowerInvariant()}";
        }

        private static string FormatInstruction(DrillRunner runner, bool isFencerOnStrip)
        {
            if (runner.IsPaused)
            {
                if (runner.IsPausedByFencer)
                {
                    return isFencerOnStrip ? PausedText : PausedText + " (off the Strip)";
                }

                return OffStripText;
            }

            switch (runner.State)
            {
                case DrillState.Cue:
                case DrillState.Window:
                    return FormatCueInstruction(runner);
                case DrillState.Return:
                    return runner.IsPromptingReturn ? RepReadText.BackToLineText : RestText;
                case DrillState.Rest:
                    return RestText;
                default:
                    return string.Empty;
            }
        }

        private static string FormatCueInstruction(DrillRunner runner)
        {
            var cue = runner.CurrentCue;
            if (cue.FootworkCall.HasValue)
            {
                return FootworkNames.Get(cue.FootworkCall.Value);
            }

            if (runner.IsAwaitingSecondTouch && cue.SecondTargetZone.HasValue)
            {
                return $"Now the {TargetZoneNames.Get(cue.SecondTargetZone.Value).ToLowerInvariant()}!";
            }

            if (cue.SecondTargetZone.HasValue && cue.TargetZone.HasValue)
            {
                return "Touch the " + FormatCompound(cue.TargetZone.Value, cue.SecondTargetZone.Value).ToLowerInvariant();
            }

            if (runner.Drill.CueType == CueType.LitTargetZone || !runner.CurrentTargetZone.HasValue)
            {
                return LitZoneText;
            }

            return $"Touch the {TargetZoneNames.Get(runner.CurrentTargetZone.Value).ToLowerInvariant()}";
        }
    }

    /// <summary>How footwork actions read in the cues and the results.</summary>
    public static class FootworkNames
    {
        public static string Get(FootworkAction action)
        {
            switch (action)
            {
                case FootworkAction.StepLunge:
                    return "Step-lunge";
                case FootworkAction.None:
                    return "No step";
                default:
                    return action.ToString();
            }
        }
    }
}
