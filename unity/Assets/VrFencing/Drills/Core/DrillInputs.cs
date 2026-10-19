using VrFencing.Dummy;

namespace VrFencing.Drills
{
    /// <summary>Monotonic time in seconds. The Unity layer reads <c>Time</c>; tests advance a fake by hand.</summary>
    public interface IClock
    {
        double NowSeconds { get; }
    }

    /// <summary>How the blade met the Target Zone. In épée only the point scores.</summary>
    public enum ContactKind
    {
        /// <summary>The point landed first: a Touch.</summary>
        PointFirst,

        /// <summary>The side of the blade hit the zone: never a Touch.</summary>
        Flat,
    }

    /// <summary>
    /// One blade contact with the Dummy, as plain data. The Blade's Touch detector produces these; the runner judges them.
    /// Tip speed and distance from the zone center are copied into the <see cref="RepResult"/> for the rep log.
    /// </summary>
    public readonly struct BladeContact
    {
        public BladeContact(TargetZone zone, ContactKind kind, float tipSpeedMetersPerSecond, float distanceFromZoneCenterMeters)
        {
            Zone = zone;
            Kind = kind;
            TipSpeedMetersPerSecond = tipSpeedMetersPerSecond;
            DistanceFromZoneCenterMeters = distanceFromZoneCenterMeters;
        }

        public TargetZone Zone { get; }

        public ContactKind Kind { get; }

        public float TipSpeedMetersPerSecond { get; }

        public float DistanceFromZoneCenterMeters { get; }
    }
}
