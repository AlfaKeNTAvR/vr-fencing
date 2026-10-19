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
    /// reaches <see cref="TouchDepthMeters"/> into the front of the Dummy's body Target Zone.
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

        /// <summary>Front foot to front foot, in meters, for a Fencer (and Dummy) of <paramref name="heightMeters"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The height is not a positive number, or the preset is unknown.</exception>
        public static float GetDistanceMeters(DummyDistance preset, float heightMeters)
        {
            DummyLayout.RequirePositiveHeight(heightMeters);

            var extension = GetExtensionDistanceMeters(heightMeters);
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

        private static float GetExtensionDistanceMeters(float heightMeters)
        {
            // Point ahead of the Fencer's front foot: shoulder offset, straight arm to the grip, then the blade.
            var pointReach = ((EnGardePose.SwordShoulder.Z + EnGardePose.ShoulderToGrip) * heightMeters) + BladeLengthMeters;

            // The front of the Dummy's body is behind its front foot, so it is that much farther from the Fencer.
            var bodyBehindFrontFoot = -EnGardePose.BodyFrontAlongForward * heightMeters;
            return pointReach - bodyBehindFrontFoot - TouchDepthMeters;
        }
    }
}
