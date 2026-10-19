using System;

namespace VrFencing.Dummy
{
    /// <summary>
    /// How far the Dummy stands from the Fencer, named by the action the Fencer needs to Touch its body
    /// (the farthest Target Zone): extension only, a lunge, or an advance then a lunge.
    /// </summary>
    public enum DummyDistance
    {
        Extension,
        Lunge,
        StepLunge,
    }

    /// <summary>
    /// The Dummy's distance for each <see cref="DummyDistance"/>, measured along the Strip from the Fencer's front
    /// foot on the En Garde Line to the Dummy's front foot. The Fencer is assumed to have the Dummy's proportions
    /// (the Dummy is the Fencer's height), so the presets grow with height; the blade does not.
    ///
    /// Extension: the Fencer's point, with the arm straight from the en garde shoulder and the blade along it,
    /// reaches <see cref="TouchDepthMeters"/> into the front of the Dummy's body Target Zone. The arm and blade's reach
    /// is the Fencer's own once Calibrate grip has measured it (PER-92), else estimated from height
    /// (<see cref="GetHeightBasedReachMeters"/>).
    /// Lunge: extension plus one lunge. Step-lunge: lunge plus one advance.
    /// Every other Target Zone is nearer than the body, so it is reachable at each preset too.
    /// </summary>
    public static class DistancePresets
    {
        /// <summary>Maximum épée blade length, guard to point (FIE Material Rules: at most 90 cm; the whole weapon at most 110 cm).</summary>
        public const float BladeLengthMeters = 0.90f;

        /// <summary>
        /// Lunge length as a fraction of height. Novice lunges measure 1.02 +/- 0.10 m (Gholipour et al. 2008;
        /// elite 1.17 m), which is about 0.6 of a 1.70 m adult; see docs/research/epee-rules-for-sim.md.
        /// The point is taken to travel as far as the front foot.
        /// </summary>
        public const float LungeLengthPerHeight = 0.6f;

        /// <summary>
        /// One advance as a fraction of height: 0.2, or 33 cm for a 1.65 m Fencer, a normal beginner step of a
        /// little over one foot length (foot 0.152 of height). Our estimate, not a measured value.
        /// </summary>
        public const float AdvanceLengthPerHeight = 0.2f;

        /// <summary>
        /// How far the point should reach into the Target Zone at the preset distance, so a Touch lands without the
        /// Fencer straining to the last centimeter.
        /// </summary>
        public const float TouchDepthMeters = 0.05f;

        /// <summary>
        /// A measured reach (shoulder to point) outside this range is taken for a bad measurement, and the height-based
        /// reach is used instead. The height-based reach runs from about 1.36 m (1.20 m tall) to 1.75 m (2.20 m).
        /// </summary>
        public const float MinimumMeasuredReachMeters = 1.0f;

        /// <inheritdoc cref="MinimumMeasuredReachMeters"/>
        public const float MaximumMeasuredReachMeters = 2.4f;

        /// <summary>Front foot to front foot, in meters, for a Fencer (and Dummy) of <paramref name="heightMeters"/>.</summary>
        /// <param name="measuredReachMeters">
        /// The Fencer's measured reach, shoulder to point with the arm straight (Calibrate grip), or null when not
        /// measured yet. A reach that fails <see cref="IsPlausibleReach"/> is ignored, so the height-based one is used.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">The height is not a positive number, or the preset is unknown.</exception>
        public static float GetDistanceMeters(DummyDistance preset, float heightMeters, float? measuredReachMeters = null)
        {
            DummyLayout.RequirePositiveHeight(heightMeters);

            var reach = measuredReachMeters.HasValue && IsPlausibleReach(measuredReachMeters.Value)
                ? measuredReachMeters.Value
                : GetHeightBasedReachMeters(heightMeters);
            var extension = GetExtensionDistanceMeters(heightMeters, reach);
            var lunge = extension + (LungeLengthPerHeight * heightMeters);
            switch (preset)
            {
                case DummyDistance.Extension:
                    return extension;
                case DummyDistance.Lunge:
                    return lunge;
                case DummyDistance.StepLunge:
                    return lunge + (AdvanceLengthPerHeight * heightMeters);
                default:
                    throw new ArgumentOutOfRangeException(nameof(preset), preset, "Not a distance preset.");
            }
        }

        /// <summary>
        /// The reach estimated from height, used until the Fencer's own is measured: the straight arm from the shoulder
        /// to the grip (standard segment fractions of height) plus a full-length blade.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The height is not a positive number.</exception>
        public static float GetHeightBasedReachMeters(float heightMeters)
        {
            DummyLayout.RequirePositiveHeight(heightMeters);
            return (EnGardePose.ShoulderToGrip * heightMeters) + BladeLengthMeters;
        }

        /// <summary>True for a measured reach that a real adult Fencer with an épée can have.</summary>
        public static bool IsPlausibleReach(float reachMeters)
        {
            return reachMeters >= MinimumMeasuredReachMeters && reachMeters <= MaximumMeasuredReachMeters;
        }

        private static float GetExtensionDistanceMeters(float heightMeters, float reachMeters)
        {
            // Point ahead of the Fencer's front foot: the en garde shoulder's offset, then the reach from the shoulder.
            var pointReach = (EnGardePose.SwordShoulder.Z * heightMeters) + reachMeters;

            // The front of the Dummy's body is behind its front foot, so it is that much farther from the Fencer.
            var bodyBehindFrontFoot = -EnGardePose.BodyFrontAlongForward * heightMeters;
            return pointReach - bodyBehindFrontFoot - TouchDepthMeters;
        }
    }
}
