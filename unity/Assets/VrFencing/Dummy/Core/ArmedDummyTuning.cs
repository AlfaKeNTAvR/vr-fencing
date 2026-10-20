namespace VrFencing.Dummy
{
    /// <summary>
    /// Every timing, speed, pose and joint-limit number of the Armed Dummy's attack script (PER-96), in one place so
    /// they can be retuned from the headset feel test (PER-95) without hunting through the code. Spec: the PER-23
    /// resolution (wind-up, random 1 to 3 s delay, slow 0.5, medium 1.5, real 2.5 to 3 m/s). The values here are
    /// the prototype's starting points; none has been confirmed on the headset yet.
    /// </summary>
    public static class ArmedDummyTuning
    {
        // ---- Timing ----

        /// <summary>The random delay before the wind-up starts, at least (PER-23: 1 to 3 s).</summary>
        public const float MinimumDelaySeconds = 1.0f;

        /// <inheritdoc cref="MinimumDelaySeconds"/>
        public const float MaximumDelaySeconds = 3.0f;

        /// <summary>The wind-up: short, but long enough to read as the cue to parry.</summary>
        public const float WindUpSeconds = 0.35f;

        /// <summary>How long the point stays at the aim before recovering.</summary>
        public const float HoldSeconds = 0.3f;

        /// <summary>Back to en garde after the hold, or after a Parry cut the thrust short.</summary>
        public const float RecoverSeconds = 0.6f;

        /// <summary>
        /// The body's peak speed back in a lunge's recovery, at most (PER-114): 4.5 cm in a 72 Hz frame, 10% under the
        /// 5 cm the Blade sweeps (a Dummy that moves further in a frame restarts the Blade's sweep, and a Touch can be
        /// missed). A Dummy taller than about 1.73 m lunges so far that it would come back faster than this in
        /// <see cref="RecoverSeconds"/>, so its recovery takes longer instead (see <see cref="RecoverUpperBodySpeedFactor"/>).
        /// </summary>
        public const float MaximumRecoverLungeSpeedMetersPerSecond = 3.25f;

        /// <summary>
        /// In a lunge's recovery the sword arm moves up to this much faster than the body over the floor (PER-115): the
        /// torso straightens and rises off the front leg as the body comes back, carrying the shoulder up and back while
        /// the arm draws back. The recovery is timed so that this fastest part stays under
        /// <see cref="MaximumRecoverLungeSpeedMetersPerSecond"/>: 0.6 s up to about 1.5 m, 0.72 s at 1.8 m, 0.84 s at 2.1 m.
        /// </summary>
        public const float RecoverUpperBodySpeedFactor = 1.15f;

        /// <summary>The shortest a thrust may take, so a very short path still reads as a move.</summary>
        public const float MinimumThrustSeconds = 0.05f;

        // ---- Speeds: the point's peak speed in the thrust ----

        /// <summary>Slow: for learning the parry.</summary>
        public const float SlowPointSpeedMetersPerSecond = 0.5f;

        /// <summary>Medium: a real challenge.</summary>
        public const float MediumPointSpeedMetersPerSecond = 1.5f;

        /// <summary>Real: a real attack (PER-23 says 2.5 to 3 m/s; taken mid-range).</summary>
        public const float RealPointSpeedMetersPerSecond = 2.75f;

        // ---- Poses ----

        /// <summary>
        /// En garde between attacks (the blade held in line): the point this far short of full reach along the
        /// in-line direction, by a bent elbow. At extension distance the straight arm in line already reaches the
        /// Fencer's body, so a real en garde keeps the arm bent and the thrust straightens it.
        /// </summary>
        public const float GuardDrawBackMeters = 0.15f;

        /// <summary>The wind-up draws the point this much further back than en garde, along the in-line direction.</summary>
        public const float WindUpDrawBackMeters = 0.12f;

        /// <summary>The hand aim: this far behind the front of the Fencer's guard, along their blade.</summary>
        public const float HandBehindGuardMeters = 0.06f;

        /// <summary>The forearm aim: this far behind the front of the Fencer's guard, about the forearm's middle.</summary>
        public const float ForearmBehindGuardMeters = 0.2f;

        // ---- Distance ----

        /// <summary>
        /// For a thrust at the hand the Dummy stands this much further back than the Drill's Dummy Distance (PER-100,
        /// Nikita's call: 0.6 to 0.75 m, as real fencers attack the arm from further away). At extension distance the
        /// hand is nearer than the arm and a straight-wristed 90 cm blade can fold, so the thrust would end past it.
        /// </summary>
        public const float HandAttackStandBackMeters = 0.75f;

        /// <summary>As <see cref="HandAttackStandBackMeters"/>, for a thrust at the forearm, which sits a little further back.</summary>
        public const float ForearmAttackStandBackMeters = 0.6f;

        // ---- Joint limits ----

        /// <summary>The elbow bends from straight (0) up to this, never past straight.</summary>
        public const float MaximumElbowBendDegrees = 120.0f;

        /// <summary>The upper arm turns at most this far from the in-line direction.</summary>
        public const float MaximumShoulderSwingDegrees = 130.0f;

        /// <summary>
        /// Aims are taken no further than this from the in-line direction, seen from the shoulder: a thrust goes
        /// forward at the Fencer, never sideways or behind. Part of what keeps the shoulder within its limit.
        /// </summary>
        public const float MaximumAimOffLineDegrees = 40.0f;

        /// <summary>The wrist stays straight; the blade is in line with the forearm.</summary>
        public const float MaximumWristBendDegrees = 0.0f;

        // ---- Knocked arm (PER-116) ----

        /// <summary>
        /// The upper body turns with a knocked arm this much per meter the arm is knocked aside at the point: a beat that
        /// carries the arm 40 cm turns the torso 6 degrees, the sword shoulder going a few centimeters with the arm.
        /// </summary>
        public const float KnockedTorsoTurnDegreesPerMeter = 15.0f;

        /// <summary>The most a knocked arm turns the upper body.</summary>
        public const float MaximumKnockedTorsoTurnDegrees = 6.0f;
    }
}
