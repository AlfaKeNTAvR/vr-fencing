namespace VrFencing.Dummy
{
    /// <summary>
    /// Every number of the Dummy's stepping footwork (PER-110, stage 1 of PER-108), in one place for tuning against
    /// the filmed coach session. Lengths are fractions of the Dummy's height unless named in meters. None has been
    /// confirmed on the headset or against video yet.
    /// </summary>
    public static class FootworkTuning
    {
        // ---- Stride ----

        /// <summary>
        /// The longest single advance or retreat: about 33 cm at 1.8 m, the step PER-105 assumed. A longer move is
        /// several steps.
        /// </summary>
        public const float MaxStridePerHeight = 0.18f;

        /// <summary>
        /// Closer to the target than this, the Dummy does not step: the distance keeper settles within 2 cm, and a
        /// 1 cm shuffle would only look nervous.
        /// </summary>
        public const float MinStepMeters = 0.01f;

        // ---- Timing ----

        /// <summary>A full stride takes this long: a brisk advance or retreat (PER-105's rigid glide took about 0.3 s, but a real step lifts and plants each foot).</summary>
        public const float FullStepSeconds = 0.45f;

        /// <summary>A very short step (an adjustment) takes this long; durations in between scale with the stride.</summary>
        public const float ShortStepSeconds = 0.25f;

        /// <summary>
        /// The leading foot (the front foot on an advance, the back foot on a retreat) swings over the first part of the
        /// step and is planted again at this fraction of it.
        /// </summary>
        public const float LeadFootLandsAtFraction = 0.55f;

        /// <summary>
        /// The trailing foot lifts at this fraction of the step and lands at its end. A little before the lead foot
        /// lands, so the two swings overlap and the step flows.
        /// </summary>
        public const float TrailFootLiftsAtFraction = 0.45f;

        // ---- Swing shape ----

        /// <summary>How high a swinging foot rises at mid-swing: fencing steps stay low, about 5 cm at 1.8 m.</summary>
        public const float FootLiftPerHeight = 0.03f;

        /// <summary>The front foot's toe rises this much at mid-swing, so it lifts toe first and lands heel first.</summary>
        public const float FrontFootToeUpDegrees = 12.0f;

        /// <summary>The back foot's heel rises this much at mid-swing, so it pushes off and lands on the ball of the foot.</summary>
        public const float BackFootHeelUpDegrees = 8.0f;

        // ---- Variety (seeded, so tests repeat) ----

        /// <summary>Each step's duration is up to this fraction longer or shorter than its nominal one.</summary>
        public const float DurationVariationFraction = 0.05f;

        /// <summary>Each step's foot lift is up to this fraction higher or lower than nominal.</summary>
        public const float LiftVariationFraction = 0.15f;
    }
}
