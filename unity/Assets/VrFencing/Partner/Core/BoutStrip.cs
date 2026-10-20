using System;
using System.Numerics;
using VrFencing.Settings;
using VrFencing.Strip;

namespace VrFencing.Partner
{
    /// <summary>
    /// One Fencer's placed Strip as the other headset needs it (PER-81): its whole length, run-backs included, and where
    /// its rear end lies in that Fencer's own Strip frame (z along the Strip, the En Garde Line at z = 0).
    /// </summary>
    public readonly struct StripExtent
    {
        public StripExtent(float lengthMeters, float rearEndZ)
        {
            if (!(lengthMeters > 0.0f) || float.IsInfinity(lengthMeters))
            {
                throw new ArgumentOutOfRangeException(nameof(lengthMeters), lengthMeters, "A Strip needs a positive length.");
            }

            if (float.IsNaN(rearEndZ) || float.IsInfinity(rearEndZ))
            {
                throw new ArgumentOutOfRangeException(nameof(rearEndZ), rearEndZ, "The rear end must be a number.");
            }

            LengthMeters = lengthMeters;
            RearEndZ = rearEndZ;
        }

        /// <summary>End to end, both run-backs included (<see cref="StripGeometry.LengthMeters"/>).</summary>
        public float LengthMeters { get; }

        /// <summary>The rear end (behind the Fencer's En Garde Line) in the Fencer's own Strip frame; negative.</summary>
        public float RearEndZ { get; }

        /// <summary>
        /// What a Fencer with no Strip placed sends: the room origin stands in for the En Garde Line (as everywhere
        /// else without a Strip), with <see cref="UnplacedLengthMeters"/> of room assumed, <see cref="UnplacedRunBackMeters"/>
        /// of it behind the origin.
        /// </summary>
        public static StripExtent Unplaced => new StripExtent(UnplacedLengthMeters, -UnplacedRunBackMeters);

        /// <summary>Room assumed without a Strip: a 4 m En Garde gap with 1 m to retreat on each side.</summary>
        public const float UnplacedLengthMeters = 6.0f;

        /// <summary>Room assumed behind the origin without a Strip.</summary>
        public const float UnplacedRunBackMeters = 1.0f;

        public static StripExtent FromGeometry(StripGeometry geometry)
        {
            if (geometry == null)
            {
                throw new ArgumentNullException(nameof(geometry));
            }

            return new StripExtent(geometry.LengthMeters, geometry.RearEndZ);
        }

        /// <summary>
        /// The Strip a Fencer Recording was made on, from the whole length its header keeps (the Editor replay standing in
        /// for a partner): the gap between the En Garde Lines whose Strip is that long, for that Fencer's height and the
        /// Warning Zone settings. Null length (recorded with no Strip): <see cref="Unplaced"/>.
        /// </summary>
        public static StripExtent FromRecordedLength(float? lengthMeters, float fencerHeightMeters, WarningZoneSettings warningZone)
        {
            if (!lengthMeters.HasValue || !(lengthMeters.Value > 0.0f))
            {
                return Unplaced;
            }

            // The length is gap + 2 run-backs, and the run-back grows with the gap: halve the search range until it pins it.
            var low = StripGeometry.MinimumPlacedLengthMeters;
            var high = Math.Max(low, lengthMeters.Value);
            for (var step = 0; step < 40; step++)
            {
                var middle = (low + high) * 0.5f;
                if (StripGeometry.Create(middle, fencerHeightMeters, warningZone).LengthMeters < lengthMeters.Value)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }

            var geometry = StripGeometry.Create(low, fencerHeightMeters, warningZone);
            return new StripExtent(lengthMeters.Value, geometry.RearEndZ);
        }
    }

    /// <summary>
    /// The shared Strip of a two-headset session (PER-81, decided in PER-57 and PER-42 research section 6). Each Fencer
    /// placed their own Strip in their own room; the bout Strip is the shorter of the two, run-backs included, anchored
    /// at each Fencer's own rear end, so each Fencer's retreat limit is the wall they set up against and the front of a
    /// longer Strip goes unused.
    /// <para>
    /// Frames: each headset sends true, unmirrored poses in its own Strip frame. The receiver turns them 180 degrees
    /// about the vertical through the bout centre: the partner's bout centre lands on this Fencer's bout centre, the
    /// partner's forward (+z) becomes this Fencer's backward, and the partner's right (+x) this Fencer's left. A turn,
    /// not a mirror, so a right-hander stays a right-hander.
    /// </para>
    /// <para>
    /// En Garde Lines: <see cref="FullEnGardeGapMeters"/> apart about the centre, shrunk on a short Strip so each Fencer
    /// keeps at least <see cref="MinimumRetreatMeters"/> between their line and their rear end. The run-back counts as
    /// retreat room.
    /// </para>
    /// All positions are in this Fencer's Strip frame, meters.
    /// </summary>
    public sealed class BoutStrip
    {
        /// <summary>The En Garde Lines' gap on a long enough Strip (as on a piste, PER-57).</summary>
        public const float FullEnGardeGapMeters = 4.0f;

        /// <summary>Room each Fencer keeps behind their En Garde Line, to their rear end, on a short Strip (PER-57).</summary>
        public const float MinimumRetreatMeters = 1.0f;

        private static readonly Quaternion HalfTurn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)Math.PI);

        private readonly float _partnerCenterZ;

        private BoutStrip(float lengthMeters, float rearEndZ, float partnerCenterZ)
        {
            LengthMeters = lengthMeters;
            RearEndZ = rearEndZ;
            _partnerCenterZ = partnerCenterZ;
            EnGardeGapMeters = Math.Max(0.0f, Math.Min(FullEnGardeGapMeters, lengthMeters - (2.0f * MinimumRetreatMeters)));
        }

        /// <summary>The bout Strip's length: the shorter of the two placed Strips.</summary>
        public float LengthMeters { get; }

        /// <summary>This Fencer's rear end: the same as their own Strip's.</summary>
        public float RearEndZ { get; }

        /// <summary>The opponent's rear end, seen in this Fencer's room. The rest of a longer Strip lies past it, unused.</summary>
        public float FarEndZ => RearEndZ + LengthMeters;

        /// <summary>Midway between the two rear ends: the point the partner's poses turn about.</summary>
        public float CenterZ => RearEndZ + (LengthMeters * 0.5f);

        /// <summary>The gap between the two En Garde Lines: <see cref="FullEnGardeGapMeters"/>, or less on a short Strip.</summary>
        public float EnGardeGapMeters { get; }

        /// <summary>This Fencer's En Garde Line in the bout.</summary>
        public float MyEnGardeLineZ => CenterZ - (EnGardeGapMeters * 0.5f);

        /// <summary>The opponent's En Garde Line in the bout, seen in this Fencer's room.</summary>
        public float OpponentEnGardeLineZ => CenterZ + (EnGardeGapMeters * 0.5f);

        /// <summary>Room behind this Fencer's En Garde Line to the rear end (the same for the opponent).</summary>
        public float RetreatMeters => MyEnGardeLineZ - RearEndZ;

        /// <summary>The bout Strip for this Fencer's placed Strip and the partner's.</summary>
        public static BoutStrip Create(StripExtent mine, StripExtent partner)
        {
            var length = Math.Min(mine.LengthMeters, partner.LengthMeters);
            return new BoutStrip(length, mine.RearEndZ, partner.RearEndZ + (length * 0.5f));
        }

        /// <summary>A point the partner sent in their Strip frame, in this Fencer's Strip frame.</summary>
        public Vector3 ToMyStripFrame(Vector3 pointInPartnerStrip)
        {
            return new Vector3(-pointInPartnerStrip.X, pointInPartnerStrip.Y, CenterZ - (pointInPartnerStrip.Z - _partnerCenterZ));
        }

        /// <summary>A direction (a velocity, an axis) the partner sent in their Strip frame, in this Fencer's: turned only.</summary>
        public Vector3 DirectionToMyStripFrame(Vector3 directionInPartnerStrip)
        {
            return new Vector3(-directionInPartnerStrip.X, directionInPartnerStrip.Y, -directionInPartnerStrip.Z);
        }

        /// <summary>A pose the partner sent in their Strip frame, in this Fencer's Strip frame.</summary>
        public TrackedPose ToMyStripFrame(TrackedPose poseInPartnerStrip)
        {
            return new TrackedPose(
                ToMyStripFrame(poseInPartnerStrip.Position),
                Quaternion.Normalize(HalfTurn * poseInPartnerStrip.Rotation));
        }
    }
}
