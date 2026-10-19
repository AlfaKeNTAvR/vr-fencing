using System.Globalization;

namespace VrFencing.Tracking
{
    /// <summary>
    /// The once-per-interval <c>[Life]</c> heartbeat line in the device log (PER-71). A gap in the heartbeat is a frozen
    /// frame loop; its fields show what the XR runtime reported just before. Times are realtime seconds since launch, so
    /// they keep counting while the app is paused.
    /// </summary>
    public sealed class LifecycleHeartbeat
    {
        public const double DefaultIntervalSeconds = 1.0;

        private readonly double _intervalSeconds;
        private double? _lastBeatSeconds;

        public LifecycleHeartbeat(double intervalSeconds = DefaultIntervalSeconds)
        {
            _intervalSeconds = intervalSeconds;
        }

        /// <summary>True on the first call and then whenever the interval has passed since the last beat (which it records).</summary>
        public bool TryBeat(double realtimeSeconds)
        {
            if (_lastBeatSeconds.HasValue && realtimeSeconds - _lastBeatSeconds.Value < _intervalSeconds)
            {
                return false;
            }

            _lastBeatSeconds = realtimeSeconds;
            return true;
        }

        /// <summary>The heartbeat line, for example "[Life] frame 1234 t 56.78 s vrFocus 1 inputFocus 1 hmd 1 user 1".</summary>
        public static string Format(long frameCount, double realtimeSeconds, RuntimeReadiness readiness)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "[Life] frame {0} t {1:0.00} s vrFocus {2} inputFocus {3} hmd {4} user {5}",
                frameCount,
                realtimeSeconds,
                ToFlag(readiness.HasVrFocus),
                ToFlag(readiness.HasInputFocus),
                ToFlag(readiness.IsHmdPresent),
                ToFlag(readiness.IsUserPresent));
        }

        private static int ToFlag(bool value) => value ? 1 : 0;
    }
}
