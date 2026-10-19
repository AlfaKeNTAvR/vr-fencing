using System.Numerics;

namespace VrFencing.Dummy
{
    /// <summary>
    /// A segment with a radius: every point within <see cref="RadiusMeters"/> of the segment from
    /// <see cref="Start"/> to <see cref="End"/>. Start equal to End is a sphere.
    /// The Dummy is built from these, so the Blade's Touch detector can test the point against them as plain data.
    /// </summary>
    public readonly struct Capsule
    {
        public Capsule(Vector3 start, Vector3 end, float radiusMeters)
        {
            Start = start;
            End = end;
            RadiusMeters = radiusMeters;
        }

        public Vector3 Start { get; }

        public Vector3 End { get; }

        public float RadiusMeters { get; }

        public Vector3 Center => (Start + End) * 0.5f;

        /// <summary>Length of the segment between the two end centers, without the rounded ends.</summary>
        public float SegmentLengthMeters => Vector3.Distance(Start, End);
    }

    /// <summary>One Target Zone's shape.</summary>
    public readonly struct ZoneCapsule
    {
        public ZoneCapsule(TargetZone zone, Capsule shape)
        {
            Zone = zone;
            Shape = shape;
        }

        public TargetZone Zone { get; }

        public Capsule Shape { get; }
    }
}
