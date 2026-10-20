using System;
using System.Collections.Generic;
using System.Linq;

namespace VrFencing.Partner
{
    /// <summary>
    /// The other app's monotonic clock as seen from this one, from ping round trips (PER-82, decided in PER-56 and the
    /// PER-42 research): every <see cref="PingIntervalSeconds"/> this app sends its own time, the other app answers at
    /// once with the echo and its own time, and the answer's arrival closes the round trip. Assuming the two legs took
    /// the same time, the other clock read <c>remote</c> at <c>sent + roundTrip / 2</c> here.
    /// <para>
    /// Low round-trip filtering: of the last <see cref="WindowSize"/> round trips, the offset is taken from the quickest
    /// one, whose legs had the least room to differ, so a slow or lopsided trip (Wi-Fi retries, a queue) never moves the
    /// clock. Its error is at most half that round trip (<see cref="ClockErrorSeconds"/>). The round trip shown to the
    /// Fencer is the window's median, the time a message typically takes there and back.
    /// </para>
    /// Both apps run one: the Partner's offset puts its Touch claims on the Host's clock, and both show the ping.
    /// </summary>
    public sealed class ClockSync
    {
        /// <summary>A ping goes out this often (2 per second: a few bytes, and a fresh round trip well inside a phrase).</summary>
        public const double PingIntervalSeconds = 0.5;

        /// <summary>Round trips kept: the last 8 seconds at 2 per second, long enough to catch a quick one, short enough to follow a changed route.</summary>
        public const int WindowSize = 16;

        /// <summary>An answer slower than this is dropped: it says nothing useful about the clock (and is likely a stale echo).</summary>
        public const double MaxRoundTripSeconds = 2.0;

        private readonly List<Sample> _samples = new List<Sample>(WindowSize);

        /// <summary>True once one round trip has come back.</summary>
        public bool IsSynced => _samples.Count > 0;

        /// <summary>Round trips in the window.</summary>
        public int Count => _samples.Count;

        /// <summary>The other clock minus this one, from the quickest round trip in the window; null before the first.</summary>
        public double? OffsetSeconds => IsSynced ? Quickest().OffsetSeconds : (double?)null;

        /// <summary>The most the offset can be wrong by: half the quickest round trip. Null before the first.</summary>
        public double? ClockErrorSeconds => IsSynced ? Quickest().RoundTripSeconds * 0.5 : (double?)null;

        /// <summary>The window's median round trip, shown under the scoring box; null before the first.</summary>
        public double? RoundTripSeconds
        {
            get
            {
                if (!IsSynced)
                {
                    return null;
                }

                var sorted = _samples.Select(sample => sample.RoundTripSeconds).OrderBy(seconds => seconds).ToArray();
                var middle = sorted.Length / 2;
                return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) * 0.5;
            }
        }

        /// <summary>
        /// A ping sent at <paramref name="sentLocalSeconds"/> (this clock) was answered when the other clock read
        /// <paramref name="remoteSeconds"/>, and the answer arrived at <paramref name="receivedLocalSeconds"/>. Returns
        /// false (and keeps nothing) for an impossible or too slow round trip.
        /// </summary>
        public bool AddRoundTrip(double sentLocalSeconds, double remoteSeconds, double receivedLocalSeconds)
        {
            var roundTrip = receivedLocalSeconds - sentLocalSeconds;
            if (!double.IsFinite(roundTrip) || !double.IsFinite(remoteSeconds) || roundTrip < 0.0 || roundTrip > MaxRoundTripSeconds)
            {
                return false;
            }

            if (_samples.Count == WindowSize)
            {
                _samples.RemoveAt(0);
            }

            _samples.Add(new Sample(roundTrip, remoteSeconds + (roundTrip * 0.5) - receivedLocalSeconds));
            return true;
        }

        /// <summary>This clock's <paramref name="localSeconds"/> on the other clock; null before the first round trip.</summary>
        public double? ToRemoteTime(double localSeconds)
        {
            return localSeconds + OffsetSeconds;
        }

        /// <summary>Forgets every round trip (the partner left, or a new session).</summary>
        public void Reset()
        {
            _samples.Clear();
        }

        private Sample Quickest()
        {
            var quickest = _samples[0];
            foreach (var sample in _samples)
            {
                // The newer of two equally quick ones: it has seen the less drift.
                if (sample.RoundTripSeconds <= quickest.RoundTripSeconds)
                {
                    quickest = sample;
                }
            }

            return quickest;
        }

        private readonly struct Sample
        {
            public Sample(double roundTripSeconds, double offsetSeconds)
            {
                RoundTripSeconds = roundTripSeconds;
                OffsetSeconds = offsetSeconds;
            }

            public double RoundTripSeconds { get; }

            public double OffsetSeconds { get; }
        }
    }
}
