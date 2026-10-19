using System.Globalization;
using VrFencing.Drills;

namespace VrFencing.UI
{
    /// <summary>
    /// What the label above the Dummy says between Reps (PER-88, PER-89): the footwork the app read in the judged Rep,
    /// so a miss explains itself, and the prompt back to the Start Mark.
    /// </summary>
    public static class RepReadText
    {
        /// <summary>Shown when the Fencer is not back at the Start Mark 2 s after a Rep (PER-89).</summary>
        public const string BackToLineText = "Back to the line";

        /// <summary>Distances under this read in centimeters ("check, 6 cm"), longer ones in meters ("lunge, 0.6 m").</summary>
        private const float CentimetersBelowMeters = 0.3f;

        /// <summary>
        /// "read: step-lunge, 1.2 m", "read: 3 advances, 1.0 m", "read: check, 6 cm", "read: no step". The distance is how
        /// far the head went that way: forward, or back for a retreat.
        /// </summary>
        public static string Format(FootworkReading reading)
        {
            if (reading.Action == FootworkAction.None)
            {
                return "read: no step";
            }

            var meters = reading.Action == FootworkAction.Retreat ? reading.BackwardTravelMeters : reading.ForwardTravelMeters;
            return $"read: {FormatAction(reading)}, {FormatDistance(meters)}";
        }

        /// <summary>The read line, then the prompt back to the line when there is one; either may be empty.</summary>
        public static string Combine(string readLine, bool isPromptingReturn)
        {
            if (!isPromptingReturn)
            {
                return readLine ?? string.Empty;
            }

            return string.IsNullOrEmpty(readLine) ? BackToLineText : readLine + "\n" + BackToLineText;
        }

        private static string FormatAction(FootworkReading reading)
        {
            var name = FootworkNames.Get(reading.Action).ToLowerInvariant();
            if (reading.StepCount <= 1 || (reading.Action != FootworkAction.Advance && reading.Action != FootworkAction.Retreat))
            {
                return name;
            }

            return $"{reading.StepCount.ToString(CultureInfo.InvariantCulture)} {name}s";
        }

        private static string FormatDistance(float meters)
        {
            return meters < CentimetersBelowMeters
                ? (meters * 100.0f).ToString("0", CultureInfo.InvariantCulture) + " cm"
                : meters.ToString("0.0", CultureInfo.InvariantCulture) + " m";
        }
    }
}
