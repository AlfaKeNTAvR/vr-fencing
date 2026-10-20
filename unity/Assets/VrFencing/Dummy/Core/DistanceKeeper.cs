using System;

namespace VrFencing.Dummy
{
    /// <summary>
    /// Where the Dummy's front foot may stand along the Strip (Strip-frame z), so its whole stance stays on the Strip:
    /// its back heel no further than the far end, its front toe no nearer than the Fencer's end.
    /// </summary>
    public readonly struct DummyStripLimits
    {
        public DummyStripLimits(float nearestFrontFootZ, float farthestFrontFootZ)
        {
            if (float.IsNaN(nearestFrontFootZ) || float.IsNaN(farthestFrontFootZ) || nearestFrontFootZ > farthestFrontFootZ)
            {
                throw new ArgumentOutOfRangeException(nameof(farthestFrontFootZ), farthestFrontFootZ, "Must not be nearer than the nearest limit.");
            }

            NearestFrontFootZ = nearestFrontFootZ;
            FarthestFrontFootZ = farthestFrontFootZ;
        }

        /// <summary>No Strip yet: the Dummy may stand anywhere in front of the Fencer's En Garde Line.</summary>
        public static DummyStripLimits Unlimited => new DummyStripLimits(0.0f, float.PositiveInfinity);

        public float NearestFrontFootZ { get; }

        public float FarthestFrontFootZ { get; }

        /// <summary>
        /// The limits on a Strip whose ends are at <paramref name="rearEndZ"/> (behind the Fencer) and
        /// <paramref name="farEndZ"/> (behind the Dummy), for a Dummy of <paramref name="heightMeters"/>: its back heel
        /// stays on the far end and its front toe on the rear end, never behind the Fencer's En Garde Line (z = 0, where
        /// the Dummy's distance is measured from). A Strip too short for the stance pins the Dummy at the far limit.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The height is not a positive number.</exception>
        public static DummyStripLimits ForStrip(float rearEndZ, float farEndZ, float heightMeters)
        {
            DummyLayout.RequirePositiveHeight(heightMeters);

            // The Dummy faces the Fencer (-z on the Strip): its back heel is behind its front foot, its toe in front.
            var farthest = Math.Max(0.0f, farEndZ + (EnGardePose.BackHeel.Z * heightMeters));
            var nearest = Math.Max(0.0f, rearEndZ + (EnGardePose.FrontToe.Z * heightMeters));
            return new DummyStripLimits(Math.Min(nearest, farthest), farthest);
        }

        public float Clamp(float frontFootZ)
        {
            return Math.Min(Math.Max(frontFootZ, NearestFrontFootZ), FarthestFrontFootZ);
        }
    }

    /// <summary>What the distance keeper is doing.</summary>
    public enum DistanceKeeperState
    {
        /// <summary>At its distance: the Fencer has not moved past the tolerance since it last set it.</summary>
        Holding,

        /// <summary>The Fencer moved; the Dummy has not reacted yet (its reaction delay is running).</summary>
        Reacting,

        /// <summary>Reacted: the target follows the Fencer as seen one reaction delay ago, until it catches up.</summary>
        Following,
    }

    /// <summary>
    /// The moving Dummy's first brain (PER-105, stage 1 of PER-44): it keeps the Dummy at its Dummy Distance from the
    /// Fencer as the Fencer advances and retreats, with a human reaction delay, inside the Strip's ends.
    ///
    /// Its output is an intent, <see cref="TargetFrontFootZ"/>: where the Dummy's front foot should stand on the Strip
    /// (Strip-frame z, from the Fencer's En Garde Line toward the Dummy). Something else carries the body there: the
    /// rigid <see cref="DummyGlide"/> for now, stepping legs later (PER-110). Its input, every frame, is where the
    /// Fencer's front foot is along the Strip.
    ///
    /// Rules:
    /// - Holding: the Fencer's front foot moving within <see cref="DistanceKeeperTuning.ToleranceMeters"/> of where
    ///   the Dummy last set the distance (sway, an extension) changes nothing.
    /// - Past it, a reaction starts: its delay is drawn from the difficulty's <see cref="ReactionTiming"/>, and nothing
    ///   moves until it is over.
    /// - Following: the target follows the Fencer as seen that delay ago, so the Dummy copies the Fencer's footwork one
    ///   reaction late (an advance, a retreat, even a check) and never sees the future. Once the target is within
    ///   <see cref="DistanceKeeperTuning.SettledMeters"/> of where the Fencer now asks for, it holds again, at the
    ///   exact distance.
    /// - The target never leaves <see cref="DummyStripLimits"/>: at its end of the Strip the Dummy stays put, and the
    ///   Fencer can close the distance on it.
    /// - Frames with no Fencer position (no headset pose) change nothing.
    /// </summary>
    public sealed class DistanceKeeper
    {
        // At least the longest reaction (Easy: 0.72 s) at 120 Hz with room to spare; older samples are overwritten.
        private const int HistoryCapacity = 256;

        private readonly double[] _sampleSeconds = new double[HistoryCapacity];
        private readonly float[] _sampleFrontFootZ = new float[HistoryCapacity];
        private readonly Random _random;
        private int _newestIndex = -1;
        private int _sampleCount;
        private double _reactionStartedAtSeconds;

        /// <param name="gapMeters">The Dummy Distance to hold: Fencer's front foot to the Dummy's, along the Strip.</param>
        /// <param name="startFrontFootZ">Where the Dummy stands now; the first target (within the limits).</param>
        /// <param name="limits">Where the Dummy may stand on the Strip.</param>
        /// <param name="difficulty">Sets the reaction delay; <see cref="SetDifficulty"/> changes it later.</param>
        /// <param name="random">Draws each reaction's jitter; seed it for repeatable tests.</param>
        /// <exception cref="ArgumentOutOfRangeException">The gap or the start is not a finite number, or the gap is negative.</exception>
        public DistanceKeeper(float gapMeters, float startFrontFootZ, DummyStripLimits limits, DummyDifficulty difficulty, Random random)
        {
            if (!(gapMeters >= 0.0f) || float.IsInfinity(gapMeters))
            {
                throw new ArgumentOutOfRangeException(nameof(gapMeters), gapMeters, "Must be a non-negative number of meters.");
            }

            if (float.IsNaN(startFrontFootZ) || float.IsInfinity(startFrontFootZ))
            {
                throw new ArgumentOutOfRangeException(nameof(startFrontFootZ), startFrontFootZ, "Must be a finite number of meters.");
            }

            _random = random ?? throw new ArgumentNullException(nameof(random));
            GapMeters = gapMeters;
            Limits = limits;
            TargetFrontFootZ = limits.Clamp(startFrontFootZ);
            SetDifficulty(difficulty);
        }

        /// <summary>The Dummy Distance being held.</summary>
        public float GapMeters { get; }

        public DummyStripLimits Limits { get; }

        /// <summary>The intent: where the Dummy's front foot should stand on the Strip now (Strip-frame z).</summary>
        public float TargetFrontFootZ { get; private set; }

        public DistanceKeeperState State { get; private set; } = DistanceKeeperState.Holding;

        public DummyDifficulty Difficulty { get; private set; }

        /// <summary>The delay of the reaction under way (or the last one), jitter included; 0 before the first.</summary>
        public float ReactionDelaySeconds { get; private set; }

        /// <summary>
        /// Changes how fast the next reaction comes (e.g. per Rep). A reaction already under way keeps its delay.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Not a difficulty.</exception>
        public void SetDifficulty(DummyDifficulty difficulty)
        {
            Timing = DistanceKeeperTuning.GetReaction(difficulty);
            Difficulty = difficulty;
        }

        /// <summary>The reaction timing of the current difficulty.</summary>
        public ReactionTiming Timing { get; private set; }

        /// <summary>
        /// One frame: takes where the Fencer's front foot is along the Strip (Strip-frame z), or null without a pose,
        /// and updates <see cref="TargetFrontFootZ"/> and <see cref="State"/>.
        /// </summary>
        /// <exception cref="ArgumentException">Time went backwards.</exception>
        public void Step(double nowSeconds, float? fencerFrontFootZ)
        {
            if (_sampleCount > 0 && nowSeconds < _sampleSeconds[_newestIndex])
            {
                throw new ArgumentException("Time must not go backwards.", nameof(nowSeconds));
            }

            if (fencerFrontFootZ.HasValue && !float.IsNaN(fencerFrontFootZ.Value) && !float.IsInfinity(fencerFrontFootZ.Value))
            {
                AddSample(nowSeconds, fencerFrontFootZ.Value);
            }

            if (_sampleCount == 0)
            {
                return;
            }

            var wanted = GetTargetFor(_sampleFrontFootZ[_newestIndex]);
            if (State == DistanceKeeperState.Holding && Math.Abs(wanted - TargetFrontFootZ) > DistanceKeeperTuning.ToleranceMeters)
            {
                ReactionDelaySeconds = Math.Max(0.0f, Timing.DrawDelaySeconds(_random));
                _reactionStartedAtSeconds = nowSeconds;
                State = DistanceKeeperState.Reacting;
            }

            if (State == DistanceKeeperState.Reacting && nowSeconds - _reactionStartedAtSeconds >= ReactionDelaySeconds)
            {
                State = DistanceKeeperState.Following;
            }

            if (State != DistanceKeeperState.Following)
            {
                return;
            }

            TargetFrontFootZ = GetTargetFor(GetFencerFrontFootZAt(nowSeconds - ReactionDelaySeconds));
            if (Math.Abs(wanted - TargetFrontFootZ) <= DistanceKeeperTuning.SettledMeters)
            {
                // Caught up: hold the exact distance from where the Fencer now stands.
                TargetFrontFootZ = wanted;
                State = DistanceKeeperState.Holding;
            }
        }

        private float GetTargetFor(float fencerFrontFootZ)
        {
            return Limits.Clamp(fencerFrontFootZ + GapMeters);
        }

        private void AddSample(double nowSeconds, float frontFootZ)
        {
            _newestIndex = (_newestIndex + 1) % HistoryCapacity;
            _sampleSeconds[_newestIndex] = nowSeconds;
            _sampleFrontFootZ[_newestIndex] = frontFootZ;
            _sampleCount = Math.Min(_sampleCount + 1, HistoryCapacity);
        }

        /// <summary>
        /// Where the Fencer's front foot was at <paramref name="atSeconds"/>, between the two samples around it; the
        /// oldest kept sample before them, the newest after.
        /// </summary>
        private float GetFencerFrontFootZAt(double atSeconds)
        {
            var later = _newestIndex;
            for (var step = 1; step < _sampleCount; step++)
            {
                var earlier = (_newestIndex - step + HistoryCapacity) % HistoryCapacity;
                if (_sampleSeconds[earlier] <= atSeconds)
                {
                    var span = _sampleSeconds[later] - _sampleSeconds[earlier];
                    var fraction = span > 0.0 ? (float)((atSeconds - _sampleSeconds[earlier]) / span) : 1.0f;
                    fraction = Math.Min(Math.Max(fraction, 0.0f), 1.0f);
                    return _sampleFrontFootZ[earlier] + ((_sampleFrontFootZ[later] - _sampleFrontFootZ[earlier]) * fraction);
                }

                later = earlier;
            }

            return _sampleFrontFootZ[later];
        }
    }
}
