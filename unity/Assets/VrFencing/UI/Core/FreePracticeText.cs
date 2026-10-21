using System;
using VrFencing.Settings;

namespace VrFencing.UI
{
    /// <summary>
    /// The home screen's Free Practice row (PER-123): what the armed Dummy does outside any Drill. Each press cycles to
    /// the next behavior (<see cref="Drills.FreePractice.GetNext"/>).
    /// </summary>
    public static class FreePracticeText
    {
        /// <summary>The row's label, e.g. "Dummy: keep distance".</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="behavior"/> is not a behavior.</exception>
        public static string FormatButton(FreePracticeBehavior behavior)
        {
            return $"Dummy: {Describe(behavior)}";
        }

        /// <summary>The behavior in a few words, lower case.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="behavior"/> is not a behavior.</exception>
        public static string Describe(FreePracticeBehavior behavior)
        {
            switch (behavior)
            {
                case FreePracticeBehavior.Guard:
                    return "guard";
                case FreePracticeBehavior.KeepDistance:
                    return "keep distance";
                case FreePracticeBehavior.Footwork:
                    return "footwork";
                case FreePracticeBehavior.Attacks:
                    return "attacks";
                default:
                    throw new ArgumentOutOfRangeException(nameof(behavior), behavior, "Not a Free Practice behavior.");
            }
        }
    }
}
