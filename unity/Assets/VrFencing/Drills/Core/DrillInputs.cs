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

    /// <summary>
    /// What the Fencer's feet did, as read from the head's motion along the Strip (the Tracking module's footwork
    /// classifier). Rough by nature: the headset sees the head, not the feet.
    /// </summary>
    public enum FootworkAction
    {
        /// <summary>No step: the head stayed within a few centimeters of where it was at the cue (an extension, or nothing).</summary>
        None,

        /// <summary>A step toward the Dummy, held there (or several: see <see cref="FootworkReading.StepCount"/>).</summary>
        Advance,

        /// <summary>A step away from the Dummy (or several: see <see cref="FootworkReading.StepCount"/>).</summary>
        Retreat,

        /// <summary>A fast, long forward burst of the head.</summary>
        Lunge,

        /// <summary>An advance and a lunge: a fast forward burst covering about a lunge plus a step.</summary>
        StepLunge,

        /// <summary>A short move toward the Dummy that comes back again: a step started and checked.</summary>
        Check,
    }

    /// <summary>
    /// The footwork of the current Rep so far, from its cue: what was read, and the measurements it was read from (for
    /// the rep log, so the thresholds can be tuned). The Tracking module reports one every frame while a Rep that needs
    /// footwork is open; the runner judges with the latest.
    /// </summary>
    public readonly struct FootworkReading
    {
        public FootworkReading(
            FootworkAction action,
            double? onsetSecondsAfterCue,
            float forwardTravelMeters,
            float backwardTravelMeters,
            float peakForwardSpeedMetersPerSecond,
            float headDropMeters,
            int stepCount = 0)
        {
            Action = action;
            OnsetSecondsAfterCue = onsetSecondsAfterCue;
            ForwardTravelMeters = forwardTravelMeters;
            BackwardTravelMeters = backwardTravelMeters;
            PeakForwardSpeedMetersPerSecond = peakForwardSpeedMetersPerSecond;
            HeadDropMeters = headDropMeters;
            StepCount = stepCount;
        }

        public FootworkAction Action { get; }

        /// <summary>When the head first moved off its place at the cue, from the cue; null while it has not moved.</summary>
        public double? OnsetSecondsAfterCue { get; }

        /// <summary>Farthest the head got toward the Dummy (Strip +z) from where it was at the cue.</summary>
        public float ForwardTravelMeters { get; }

        /// <summary>Farthest the head got away from the Dummy from where it was at the cue.</summary>
        public float BackwardTravelMeters { get; }

        /// <summary>Fastest forward head speed along the Strip; zero while there are too few samples to measure it.</summary>
        public float PeakForwardSpeedMetersPerSecond { get; }

        /// <summary>How far the head went down from its height at the cue. A lunge lowers it; an advance does not.</summary>
        public float HeadDropMeters { get; }

        /// <summary>
        /// For an advance or a retreat, how many steps the head took that way (quick advances in a row read as one
        /// advance with several steps); zero for the other actions.
        /// </summary>
        public int StepCount { get; }
    }
}
