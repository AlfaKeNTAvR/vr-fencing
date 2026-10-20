namespace VrFencing.Partner
{
    /// <summary>
    /// Every number of the remote Fencer's legs (PER-112): how the sender reads its own footwork
    /// (<see cref="FootworkStateTracker"/>) and how the receiver poses the legs (<see cref="RemoteFencerLegs"/>). The
    /// swing heights, foot pitches and leg reach are the Dummy's (<see cref="VrFencing.Dummy.FootworkTuning"/>), so both
    /// bodies step alike. Lengths are fractions of the Fencer's height unless named in meters. None has been confirmed
    /// on the headset or against video yet.
    /// </summary>
    public static class RemoteFootworkTuning
    {
        // ---- Reading the body (sender) ----

        /// <summary>
        /// Speed along the Strip is measured over at least this span of frames (about 4 frames at 72 Hz), as the
        /// footwork classifier does, so tracking jitter does not read as motion.
        /// </summary>
        public const double SpeedWindowSeconds = 0.05;

        /// <summary>
        /// The hips moving along the Strip at least this fast start a step. En garde sway and breathing stay under it.
        /// The hips, not the head, carry the feet: leaning the torso to dodge a point moves the head but not the feet.
        /// </summary>
        public const float StepOnsetMetersPerSecond = 0.25f;

        /// <summary>
        /// And the hips must also be this far from where the planted feet carry them (about 3 cm at 1.8 m), so a short
        /// fast jiggle does not lift a foot.
        /// </summary>
        public const float StepMinTravelPerHeight = 0.015f;

        /// <summary>A step ends once the hips stay slower than this along the Strip for <see cref="StepEndSeconds"/>.</summary>
        public const float StepEndMetersPerSecond = 0.15f;

        /// <inheritdoc cref="StepEndMetersPerSecond"/>
        public const double StepEndSeconds = 0.06;

        /// <summary>
        /// The lead foot (front on an advance, back on a retreat) swings twice as far as the hips have moved, as in the
        /// first half of a smooth step, so it gets ahead of the body; it stops at the stride the step's top speed
        /// foretells (twice the hips' travel at that moment, a smooth step being symmetric) and lands there.
        /// </summary>
        public const float LeadFootTravelPerBodyTravel = 2.0f;

        /// <summary>
        /// The lead foot lands once the hips have passed their top speed and slowed to this share of it (about mid-step);
        /// the trailing foot then lifts and follows, landing in en garde when the hips stop.
        /// </summary>
        public const float LeadFootLandsAtSpeedFraction = 0.85f;

        /// <summary>A swinging foot rises to its full lift over this long: a quick pick-up, not a hop.</summary>
        public const double SwingRiseSeconds = 0.1;

        /// <summary>A foot lands (lowered and settled on its spot) over this long.</summary>
        public const double LandingSeconds = 0.08;

        /// <summary>
        /// A planted foot more than this far from its en garde place under the hips (about 13 cm at 1.8 m), with no step
        /// under way, steps there on its own: hips that drifted slowly, or a stance that ended uneven.
        /// </summary>
        public const float FootDriftPerHeight = 0.07f;

        /// <summary>Such a correcting step takes this long (the Dummy's short step).</summary>
        public const double AdjustStepSeconds = 0.25;

        // ---- Lunge and recovery (sender) ----

        /// <summary>
        /// The hips moving forward at least this fast with the head <see cref="LungeStartHeadDropPerHeight"/> lower than
        /// in en garde start a lunge. Same speed as the footwork classifier's lunge (<c>LungeStart</c>), measured on the hips.
        /// </summary>
        public const float LungeMetersPerSecond = 0.9f;

        /// <summary>
        /// The head lower than in en garde by this much (about 4 cm at 1.8 m) when the lunge is read: a fast advance keeps
        /// the head level, a lunge drops it 10 to 25 cm.
        /// </summary>
        public const float LungeStartHeadDropPerHeight = 0.02f;

        /// <summary>
        /// The front foot travels this much farther than the hips in a lunge: in the Dummy's full lunge the front foot
        /// goes 0.6 of height while the hips, dropping and staying back, go about 0.38.
        /// </summary>
        public const float LungeFrontFootTravelPerBodyTravel = 1.6f;

        /// <summary>The lunging front foot lands once the hips have slowed to this share of their top speed.</summary>
        public const float LungeFootLandsAtSpeedFraction = 0.35f;

        /// <summary>
        /// The recovery starts when the head has come back up by this share of the lunge's deepest drop, or the hips
        /// move back.
        /// </summary>
        public const float RecoveryStartRiseFraction = 0.25f;

        /// <summary>In the recovery a foot this far from its en garde place under the hips (about 5 cm at 1.8 m) steps back into it.</summary>
        public const float RecoveryMovesFootPerHeight = 0.03f;

        /// <summary>The recovery is done once the head has come back up by this share of the drop and the hips are still.</summary>
        public const float RecoveryDoneFraction = 0.9f;

        /// <summary>A recovery that has not finished after this long ends, the feet landing in en garde.</summary>
        public const double RecoveryMaxSeconds = 1.5;

        /// <summary>
        /// The head height of en garde follows the standing head this slowly (time constant), so a Fencer who stands up
        /// or sinks lower between Phrases keeps a true reference.
        /// </summary>
        public const double StandingHeadFollowSeconds = 1.0;

        /// <summary>The hips jumping farther than this in one frame is a tracking jump (recentering, a rejoin): the feet stand anew.</summary>
        public const float TrackingJumpMeters = 0.5f;

        // ---- Confidence (sender) ----

        /// <summary>The head and the hips are both tracked.</summary>
        public const float TrackedConfidence = 1.0f;

        /// <summary>Only the head: the hips are guessed from the head's last offset to them.</summary>
        public const float HeadOnlyConfidence = 0.6f;

        /// <summary>The hips move across the Strip faster than along it: walking, not fencing footwork.</summary>
        public const float SidewaysConfidence = 0.3f;

        /// <summary>Moving across the Strip faster than this, and faster than along it, reads as walking.</summary>
        public const float SidewaysMetersPerSecond = 0.4f;

        /// <summary>No head: nothing to read the feet from (headset off, asleep, not tracked).</summary>
        public const float NoConfidence = 0.0f;

        // ---- Posing the legs (receiver) ----

        /// <summary>
        /// Below this the receiver does not follow the sent feet: it keeps them planted where the sender was last sure,
        /// stepping a foot only when the hips leave it far behind (<see cref="HeldFootDriftPerHeight"/>).
        /// </summary>
        public const float MinConfidence = 0.5f;

        /// <summary>A held foot this far from its en garde place under the hips (about 22 cm at 1.8 m) steps there.</summary>
        public const float HeldFootDriftPerHeight = 0.12f;

        /// <summary>
        /// A foot whose target jumps farther than this in one frame (sure feet coming back, a rejoin, a tracking jump)
        /// steps there over <see cref="ReplantSeconds"/> instead of teleporting. Real feet move under 10 cm a frame.
        /// </summary>
        public const float ReplantJumpMeters = 0.12f;

        /// <inheritdoc cref="ReplantJumpMeters"/>
        public const double ReplantSeconds = 0.25;
    }
}
