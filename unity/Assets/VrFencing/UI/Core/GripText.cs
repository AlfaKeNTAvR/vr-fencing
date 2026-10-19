using System.Collections.Generic;
using System.Globalization;
using VrFencing.Settings;

namespace VrFencing.UI
{
    /// <summary>
    /// What the grip screen and the thrust calibration say (PER-52): the chosen grip's computed angles, the Fencer's saved
    /// ones, the old feel-test angles to compare, the reach Calibrate grip measured (PER-92), and the calibration's
    /// prompts and result.
    /// </summary>
    public static class GripText
    {
        /// <summary>Thrusts whose single fits are further apart than this were probably not all straight-arm thrusts.</summary>
        public const float InconsistentSpreadDegrees = 8.0f;

        public static string FormatAngles(GripAngles angles)
        {
            return $"pitch {FormatDegrees(angles.PitchDegrees)}, yaw {FormatDegrees(angles.YawDegrees)}";
        }

        /// <summary>The grip screen's comparison lines, top to bottom.</summary>
        public static IReadOnlyList<string> FormatComparison(BladeSettings blade)
        {
            var saved = blade.SavedAngles.For(blade.Mount);
            var guardCentimeters = GripGeometry.GetGuardForwardMeters(blade.Mount) * 100.0f;
            var lines = new List<string>
            {
                $"Grip: {SettingsPage.FormatMount(blade.Mount)} (guard {guardCentimeters.ToString("0.#", CultureInfo.InvariantCulture)} cm ahead)",
                $"Computed: {FormatAngles(GripGeometry.GetComputedAngles(blade.Mount))}",
                saved.PitchDegrees.HasValue || saved.YawDegrees.HasValue
                    ? $"Yours: {FormatSaved(saved)}"
                    : "Yours: none yet (the computed angles are in use)",
                $"Feel test (old default): {FormatAngles(GripGeometry.FeelTestAngles)}",
                $"In use: {FormatAngles(GripGeometry.GetActiveAngles(blade))}",
                FormatReach(saved.ReachMeters),
            };
            return lines;
        }

        /// <summary>The prompt shown at the target during the calibration.</summary>
        /// <param name="targetIndex">Zero-based.</param>
        public static string FormatPrompt(int targetIndex, int targetCount, bool isAimed, float holdFraction)
        {
            var header = $"Target {targetIndex + 1} of {targetCount}";
            if (!isAimed)
            {
                return $"{header}\nThrust at the dot with a straight arm";
            }

            var percent = (holdFraction * 100.0f).ToString("0", CultureInfo.InvariantCulture);
            return $"{header}\nHold still... {percent}%";
        }

        /// <summary>The message once the thrusts are fitted and the angles (and the reach, when measured) saved.</summary>
        /// <param name="savedReachMeters">The reach saved with the angles, or null when none was measured.</param>
        public static string FormatResult(GripAngles angles, float spreadDegrees, float? savedReachMeters = null)
        {
            var spread = spreadDegrees.ToString("0", CultureInfo.InvariantCulture);
            var saved = savedReachMeters.HasValue ? $"{FormatAngles(angles)}, reach {FormatMeters(savedReachMeters.Value)}" : FormatAngles(angles);
            return spreadDegrees > InconsistentSpreadDegrees
                ? $"Saved {saved}, but the thrusts disagree by {spread} deg: try again"
                : $"Saved {saved} (thrusts agree within {spread} deg)";
        }

        /// <summary>The grip page's reach line: the measured reach, or that the Dummy's distance comes from height until then.</summary>
        public static string FormatReach(float? reachMeters)
        {
            return reachMeters.HasValue
                ? $"Reach: {FormatMeters(reachMeters.Value)} (sets the Dummy's distance)"
                : "Reach: not measured yet (the Dummy's distance uses your height)";
        }

        private static string FormatMeters(float meters)
        {
            return meters.ToString("0.00", CultureInfo.InvariantCulture) + " m";
        }

        private static string FormatSaved(GripAngleOverride saved)
        {
            var pitch = saved.PitchDegrees.HasValue ? FormatDegrees(saved.PitchDegrees.Value) : "computed";
            var yaw = saved.YawDegrees.HasValue ? FormatDegrees(saved.YawDegrees.Value) : "computed";
            return $"pitch {pitch}, yaw {yaw}";
        }

        private static string FormatDegrees(float degrees)
        {
            return degrees.ToString("+0;-0;0", CultureInfo.InvariantCulture);
        }
    }
}
