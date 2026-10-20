namespace VrFencing.Dummy
{
    /// <summary>
    /// Every number of the Dummy's footwork (PER-110 stepping, PER-111 lunge, recovery, fleche and the life layer;
    /// stages 1 and 2 of PER-108), in one place for tuning against the filmed coach session. Lengths are fractions of
    /// the Dummy's height unless named in meters. None has been confirmed on the headset or against video yet.
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

        /// <summary>
        /// The hips dip this much at mid-step and rise again (PER-111): about 1.4 cm at 1.8 m. Only the hips: the
        /// shoulders, head and sword arm ride level, so the head stays level and the Armed Dummy's aim is untouched.
        /// </summary>
        public const float StepHipDipPerHeight = 0.008f;

        // ---- Variety (seeded, so tests repeat) ----

        /// <summary>Each step's duration is up to this fraction longer or shorter than its nominal one.</summary>
        public const float DurationVariationFraction = 0.05f;

        /// <summary>Each step's foot lift is up to this fraction higher or lower than nominal.</summary>
        public const float LiftVariationFraction = 0.15f;

        /// <summary>Each step's toe-up and heel-up pitch is up to this fraction more or less than nominal (PER-111).</summary>
        public const float PitchVariationFraction = 0.25f;

        /// <summary>Each step's hip dip is up to this fraction deeper or shallower than nominal (PER-111).</summary>
        public const float HipDipVariationFraction = 0.3f;

        /// <summary>
        /// When the lead foot lands moves up to this much earlier or later in the step (a fraction of the step, PER-111),
        /// so the rhythm of the two feet differs from step to step.
        /// </summary>
        public const float LeadFootLandsVariation = 0.04f;

        // ---- Life: breathing and weight shift (PER-111) ----

        /// <summary>One breath in en garde: about 17 a minute, an alert but resting athlete.</summary>
        public const float BreathSeconds = 3.5f;

        /// <summary>Each breath is up to this fraction longer or shorter than nominal.</summary>
        public const float BreathVariationFraction = 0.2f;

        /// <summary>
        /// The hips rise and settle this much with each breath (the knees straighten a little): about 7 mm at 1.8 m. The
        /// shoulders and head do not move (see <see cref="StepHipDipPerHeight"/>).
        /// </summary>
        public const float BreathHipRisePerHeight = 0.004f;

        /// <summary>
        /// The weight drifts between the front and back leg: the hips move up to this much forward or back, about
        /// 1.8 cm at 1.8 m, on two slow waves of unrelated periods with seeded phases, so the drift never repeats exactly.
        /// </summary>
        public const float WeightShiftPerHeight = 0.010f;

        /// <summary>The slower of the two weight-shift waves.</summary>
        public const float WeightShiftSlowSeconds = 7.3f;

        /// <summary>The faster of the two weight-shift waves.</summary>
        public const float WeightShiftFastSeconds = 3.1f;

        /// <summary>How much of the weight shift the faster wave carries; the slow wave carries the rest.</summary>
        public const float WeightShiftFastShare = 0.35f;

        // ---- Lunge and recovery (PER-111) ----

        /// <summary>
        /// The front foot kicks out ahead of the body by up to this much at mid-lunge (about 14 cm at 1.8 m) and lands
        /// as the body arrives, so the lunge reads as led by the front foot.
        /// </summary>
        public const float LungeFrontFootLeadPerHeight = 0.08f;

        /// <summary>The front foot rises this much at mid-lunge: higher than a step, about 7 cm at 1.8 m.</summary>
        public const float LungeFootLiftPerHeight = 0.04f;

        /// <summary>The front foot's toe is up this much at mid-lunge, so it lands heel first.</summary>
        public const float LungeFrontFootToeUpDegrees = 20.0f;

        /// <summary>
        /// In a full lunge the hips drop this much (from 0.50 to 0.33 of height, the front thigh near level) as the front knee bends and the back
        /// leg straightens.
        /// </summary>
        public const float LungeHipDropPerHeight = 0.17f;

        /// <summary>
        /// In a full lunge the hips stay this far behind the shoulders (the body leans forward about 25 degrees from the
        /// hips). The shoulders, head and sword arm ride with the Dummy frame exactly as before, so the Armed Dummy's
        /// aim, timing and judging do not change; only the hips, the legs and the bottom of the body Target Zone move.
        /// </summary>
        public const float LungeHipBackPerHeight = 0.22f;

        /// <summary>
        /// The back foot stays planted while the back leg can reach it at up to this fraction of its full length. Past
        /// that, in a lunge longer than the legs allow, it drags forward along the floor, only as much as needed.
        /// </summary>
        public const float LungeBackLegReachFraction = 0.98f;

        /// <summary>
        /// The front foot rises this much at mid-recovery (about 2 cm at 1.8 m), a low push back into en garde. Low on
        /// purpose: the Armed Dummy's recovery carries the body back at up to 4.5 cm a frame
        /// (<see cref="ArmedDummyTuning.MaximumRecoverLungeSpeedMetersPerSecond"/>), so a higher swing would move the foot zone past the Blade's 5 cm sweep.
        /// </summary>
        public const float RecoveryFootLiftPerHeight = 0.012f;

        /// <summary>
        /// How much ahead of the body the feet come back early in the recovery, settling as it slows (0: in step with
        /// it). At 0.3 the feet's top speed is about 15 % below the body's, so they stay inside the Blade's sweep.
        /// </summary>
        public const float RecoveryFootPacing = 0.3f;

        /// <summary>The front foot's toe is up this much at mid-recovery.</summary>
        public const float RecoveryFrontFootToeUpDegrees = 5.0f;

        /// <summary>A back foot that dragged forward in the lunge lifts this much as it steps back in the recovery.</summary>
        public const float RecoveryBackFootLiftPerHeight = 0.015f;

        // ---- Fleche (PER-111) ----

        /// <summary>
        /// The whole fleche, from en garde to en garde: the back foot crosses over in front, then the front foot runs
        /// past it. Slow enough that no part moves more than the Blade's 5 cm sweep in a 72 Hz frame.
        /// </summary>
        public const float FlecheSeconds = 1.2f;

        /// <summary>
        /// The crossing back foot lands this far in front of where the front foot stood, clear of its toe; the front foot then runs to
        /// en garde ahead of it. The fleche carries the Dummy the en garde stance plus this (about 83 cm at 1.8 m).
        /// </summary>
        public const float FlecheCrossAheadPerHeight = 0.20f;

        /// <summary>The back foot swings over this part of the fleche (start, then end, as fractions of it).</summary>
        public const float FlecheBackFootSwingStart = 0.05f;

        /// <inheritdoc cref="FlecheBackFootSwingStart"/>
        public const float FlecheBackFootSwingEnd = 0.55f;

        /// <summary>The front foot swings over this part of the fleche, overlapping the back foot's landing.</summary>
        public const float FlecheFrontFootSwingStart = 0.45f;

        /// <inheritdoc cref="FlecheFrontFootSwingStart"/>
        public const float FlecheFrontFootSwingEnd = 1.0f;

        /// <summary>A running foot rises this much at mid-swing, about 11 cm at 1.8 m.</summary>
        public const float FlecheFootLiftPerHeight = 0.06f;

        /// <summary>
        /// A running foot swings this far out to its own side at mid-swing, so it passes the planted foot instead of
        /// going through it.
        /// </summary>
        public const float FlecheFootSidestepPerHeight = 0.12f;

        /// <summary>The hips drop this much at mid-fleche, a running crouch.</summary>
        public const float FlecheHipDropPerHeight = 0.04f;
    }
}
