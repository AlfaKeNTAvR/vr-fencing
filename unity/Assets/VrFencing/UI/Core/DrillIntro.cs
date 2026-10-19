using System;
using VrFencing.Drills;

namespace VrFencing.UI
{
    /// <summary>
    /// The few seconds between pressing Start and a Drill's first Rep (PER-92): the Drill's task line stands above the
    /// Dummy and on the Running panel, so the Fencer knows what to do before the first cue. The Drill starts on the
    /// runner only once <see cref="IsOver"/>; the runner's own Rep timing is untouched. Time is passed in, so this runs
    /// headless.
    /// </summary>
    public sealed class DrillIntro
    {
        /// <summary>Long enough to read a one-line task, short enough not to feel like a wait.</summary>
        public const double Seconds = 3.0;

        private readonly double _startedAtSeconds;

        public DrillIntro(DrillDefinition drill, double startedAtSeconds)
        {
            Drill = drill ?? throw new ArgumentNullException(nameof(drill));
            _startedAtSeconds = startedAtSeconds;
        }

        public DrillDefinition Drill { get; }

        /// <summary>The Running panel's text during the intro: the Drill's name and its task line.</summary>
        public RunStatus Status => new RunStatus(Drill.Name, Drill.TaskLine);

        /// <summary>True once the task line has been up for <see cref="Seconds"/>: time to start the first Rep.</summary>
        public bool IsOver(double nowSeconds)
        {
            return nowSeconds - _startedAtSeconds >= Seconds;
        }
    }
}
