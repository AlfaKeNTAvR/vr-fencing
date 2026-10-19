using System;
using System.Numerics;
using VrFencing.Settings;

namespace VrFencing.Strip
{
    /// <summary>One of the two ends of the Strip, each with its own Warning Zone.</summary>
    public enum StripEnd
    {
        /// <summary>Behind the Fencer's En Garde Line.</summary>
        Rear,

        /// <summary>Toward the Dummy, at the second placement point.</summary>
        Far,
    }

    /// <summary>
    /// The Strip's shape in the Strip frame: origin on the floor at the Fencer's En Garde Line on the centerline,
    /// +x the Fencer's right, +y up, +z along the Strip toward the Dummy (the same frame the Dummy is placed in).
    ///
    /// Along z the Strip runs from <see cref="RearEndZ"/> (behind the En Garde Line) to <see cref="FarEndZ"/>
    /// (the second placement point). The part behind the En Garde Line holds the Fencer's en garde stance
    /// (<see cref="StanceAllowancePerHeight"/>, back foot to front foot) and then the rear Warning Zone, so a Fencer
    /// standing en garde with the front foot on the line is on the Strip and outside both Warning Zones.
    /// Across, it is <see cref="WidthMeters"/> wide, centered on x = 0.
    /// </summary>
    public sealed class StripGeometry
    {
        /// <summary>Regulation piste width (FIE: 1.5 m to 2 m); the narrow end, as the room allows.</summary>
        public const float WidthMeters = 1.5f;

        /// <summary>Regulation piste length, the reference for scaling the Warning Zones.</summary>
        public const float PisteLengthMeters = 14.0f;

        /// <summary>
        /// Shortest distance from the En Garde Line to the far end that placement accepts. With the stance and the
        /// rear Warning Zone behind the line, the whole Strip is then at least about 2 m.
        /// </summary>
        public const float MinimumPlacedLengthMeters = 1.0f;

        /// <summary>
        /// Room for the en garde stance behind the En Garde Line, as a fraction of the Fencer's height: feet about
        /// two foot lengths apart, as in the Dummy's en garde pose (0.30 of height, 0.5 m for a 1.65 m Fencer).
        /// </summary>
        public const float StanceAllowancePerHeight = 0.30f;

        private StripGeometry(float placedLengthMeters, float stanceAllowanceMeters, float warningZoneLengthMeters)
        {
            PlacedLengthMeters = placedLengthMeters;
            StanceAllowanceMeters = stanceAllowanceMeters;
            WarningZoneLengthMeters = warningZoneLengthMeters;
        }

        /// <summary>From the En Garde Line to the far end: the distance between the two placement points.</summary>
        public float PlacedLengthMeters { get; }

        /// <summary>Behind the En Garde Line, before the rear Warning Zone starts.</summary>
        public float StanceAllowanceMeters { get; }

        /// <summary>Length of each Warning Zone along the Strip.</summary>
        public float WarningZoneLengthMeters { get; }

        /// <summary>How far the Strip extends behind the En Garde Line: the stance, then the rear Warning Zone.</summary>
        public float RearRunBackMeters => StanceAllowanceMeters + WarningZoneLengthMeters;

        /// <summary>The whole Strip, end to end. This is the length Drills compare with their required Strip length.</summary>
        public float LengthMeters => RearRunBackMeters + PlacedLengthMeters;

        public float RearEndZ => -RearRunBackMeters;

        public float FarEndZ => PlacedLengthMeters;

        /// <summary>Midway between the two ends, where the piste's center line is drawn.</summary>
        public float CenterLineZ => (RearEndZ + FarEndZ) * 0.5f;

        /// <summary>
        /// The Strip for a placement <paramref name="placedLengthMeters"/> long (En Garde Line to far end), a Fencer of
        /// <paramref name="fencerHeightMeters"/>, and the Warning Zone settings.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The length is below <see cref="MinimumPlacedLengthMeters"/>, or the height is not positive.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="warningZone"/> is null.</exception>
        public static StripGeometry Create(float placedLengthMeters, float fencerHeightMeters, WarningZoneSettings warningZone)
        {
            if (warningZone == null)
            {
                throw new ArgumentNullException(nameof(warningZone));
            }

            if (!(placedLengthMeters >= MinimumPlacedLengthMeters) || float.IsInfinity(placedLengthMeters))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(placedLengthMeters), placedLengthMeters, $"Must be at least {MinimumPlacedLengthMeters} m.");
            }

            if (!(fencerHeightMeters > 0.0f) || float.IsInfinity(fencerHeightMeters))
            {
                throw new ArgumentOutOfRangeException(nameof(fencerHeightMeters), fencerHeightMeters, "Must be a positive number of meters.");
            }

            return new StripGeometry(
                placedLengthMeters,
                StanceAllowancePerHeight * fencerHeightMeters,
                ScaleWarningZoneLength(placedLengthMeters, warningZone));
        }

        /// <summary>
        /// The Warning Zone length for a Strip of <paramref name="stripLengthMeters"/>: the piste's zone scaled by
        /// length (2 m on 14 m), never below the minimum and never above the piste's zone.
        /// The scale uses the placed length, so the zone does not depend on itself through the rear run-back.
        /// </summary>
        public static float ScaleWarningZoneLength(float stripLengthMeters, WarningZoneSettings warningZone)
        {
            if (warningZone == null)
            {
                throw new ArgumentNullException(nameof(warningZone));
            }

            var scaled = warningZone.LengthOnPisteMeters * stripLengthMeters / PisteLengthMeters;
            var atMost = Math.Max(warningZone.LengthOnPisteMeters, warningZone.MinimumLengthMeters);
            return Math.Min(Math.Max(scaled, warningZone.MinimumLengthMeters), atMost);
        }

        /// <summary>
        /// Whether a point in the Strip frame (the Fencer's head, as the proxy for the feet) is over the Strip,
        /// with <paramref name="toleranceMeters"/> allowed past every end and side. Height is ignored.
        /// </summary>
        public bool Contains(Vector3 pointInStrip, float toleranceMeters)
        {
            var halfWidth = (WidthMeters * 0.5f) + toleranceMeters;
            return Math.Abs(pointInStrip.X) <= halfWidth
                && pointInStrip.Z >= RearEndZ - toleranceMeters
                && pointInStrip.Z <= FarEndZ + toleranceMeters;
        }

        /// <summary>
        /// The Warning Zone a point in the Strip frame is in, or null between the zones. Only the position along the
        /// Strip counts; check <see cref="Contains"/> for the sides. A point past an end counts as in that end's zone,
        /// so the zone stays lit inside the off-strip tolerance. <paramref name="growMeters"/> widens both zones
        /// toward the middle (hysteresis for leaving a zone).
        /// </summary>
        public StripEnd? GetWarningZone(Vector3 pointInStrip, float growMeters = 0.0f)
        {
            if (pointInStrip.Z <= RearEndZ + WarningZoneLengthMeters + growMeters)
            {
                return StripEnd.Rear;
            }

            if (pointInStrip.Z >= FarEndZ - WarningZoneLengthMeters - growMeters)
            {
                return StripEnd.Far;
            }

            return null;
        }

        /// <summary>The z range of a Warning Zone in the Strip frame, lower z first.</summary>
        public (float StartZ, float EndZ) GetWarningZoneRange(StripEnd end)
        {
            return end == StripEnd.Rear
                ? (RearEndZ, RearEndZ + WarningZoneLengthMeters)
                : (FarEndZ - WarningZoneLengthMeters, FarEndZ);
        }
    }
}
