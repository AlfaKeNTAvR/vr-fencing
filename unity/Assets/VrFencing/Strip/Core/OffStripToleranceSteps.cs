using System.Collections.Generic;

namespace VrFencing.Strip
{
    /// <summary>
    /// The off-strip tolerance values the Fencer cycles through on the headset to find the one where the Drill pause
    /// feels fair (PER-31). The chosen value is saved to Settings.
    /// </summary>
    public static class OffStripToleranceSteps
    {
        private const float MatchEpsilonMeters = 0.001f;

        public static IReadOnlyList<float> StepsMeters { get; } = new[] { 0.0f, 0.2f, 0.4f };

        /// <summary>
        /// The step after <paramref name="currentMeters"/>, wrapping from the last back to the first.
        /// A value between steps (edited by hand in the settings file) moves to the next larger step.
        /// </summary>
        public static float GetNext(float currentMeters)
        {
            foreach (var step in StepsMeters)
            {
                if (step > currentMeters + MatchEpsilonMeters)
                {
                    return step;
                }
            }

            return StepsMeters[0];
        }
    }
}
