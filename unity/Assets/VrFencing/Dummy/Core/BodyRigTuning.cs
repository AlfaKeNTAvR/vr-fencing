namespace VrFencing.Dummy
{
    /// <summary>
    /// The numbers of the rigged body's posing (PER-142, <see cref="BodyRigPoser"/>). Estimates from the recording
    /// <c>fencer-20261022-164920</c>; none has been confirmed on the headset yet.
    /// </summary>
    public static class BodyRigTuning
    {
        /// <summary>
        /// An elbow or knee bent less than this has no bend plane to follow: the upper bone keeps the twist its parent
        /// carries it with (an arm), or turns its knee over the toes (a leg).
        /// </summary>
        public const float MinBendDegrees = 10.0f;

        /// <summary>From this bend on the elbow or knee hinges exactly in the tracked bend plane; between the two it blends.</summary>
        public const float FullBendDegrees = 25.0f;

        /// <summary>
        /// The share of the hand's roll (pronation and supination) the forearm bone takes. The rig has no forearm twist
        /// bone, so without it the whole roll would twist the wrist's skin.
        /// </summary>
        public const float ForearmTwistShare = 0.5f;

        /// <summary>A bone fitted to a tracked length takes the median of its lengths over the last this many samples.</summary>
        public const int LengthFitSamples = 30;

        /// <summary>A length sample is taken at most this often, so the window covers 3 s at any frame rate.</summary>
        public const double LengthFitSampleSeconds = 0.1;

        /// <summary>A fitted bone is never scaled below this share of its rest length (a tracking glitch).</summary>
        public const float MinLengthScale = 0.6f;

        /// <summary>A fitted bone is never scaled above this share of its rest length.</summary>
        public const float MaxLengthScale = 1.6f;
    }
}
