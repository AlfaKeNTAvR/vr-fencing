using VrFencing.Drills;

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
        /// The label above the Dummy for a Rep's cue: the Target Zone's name for a named-zone cue ("Upper arm"), empty
        /// for a lit-zone cue, whose light on the Dummy is the cue.
        /// </summary>
        public static string FormatDummyLabel(RepCue cue)
        {
            return cue.CueType == CueType.NamedTargetZone ? TargetZoneNames.Get(cue.TargetZone) : string.Empty;
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
                    return runner.Drill.CueType == CueType.LitTargetZone
                        ? LitZoneText
                        : $"Touch the {TargetZoneNames.Get(runner.CurrentTargetZone).ToLowerInvariant()}";
                case DrillState.Rest:
                    return RestText;
                default:
                    return string.Empty;
            }
        }
    }
}
