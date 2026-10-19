using System;
using System.Numerics;

namespace VrFencing.Strip
{
    /// <summary>
    /// Watches the Fencer's head on a placed Strip and turns it into the Strip's warnings: entering a Warning Zone
    /// (short haptic buzz, the zone glows) and leaving or coming back on the Strip (Drills pause and resume).
    ///
    /// The head is the proxy for the feet. Leaving: the head passes an end or a side by more than the off-strip
    /// tolerance. Coming back needs the head <see cref="HysteresisMeters"/> inside that line, and a Warning Zone is
    /// left only <see cref="HysteresisMeters"/> past its edge, so a head hovering on a line does not flicker events.
    /// </summary>
    public sealed class StripMonitor
    {
        /// <summary>How far back past a line the head must come before the state flips back.</summary>
        public const float HysteresisMeters = 0.05f;

        private bool _hasSample;

        public StripMonitor(StripGeometry geometry)
        {
            Geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        }

        /// <summary>The head entered a Warning Zone from the middle of the Strip. Not raised for the first sample or when stepping back on.</summary>
        public event Action<StripEnd> EnteredWarningZone;

        /// <summary>The head passed an end or a side by more than the tolerance.</summary>
        public event Action LeftStrip;

        /// <summary>The head is back on the Strip after <see cref="LeftStrip"/>.</summary>
        public event Action BackOnStrip;

        public StripGeometry Geometry { get; }

        public bool IsOnStrip { get; private set; } = true;

        /// <summary>The zone to light: the one the head is in, or null. Stays set while past that end within the tolerance.</summary>
        public StripEnd? CurrentWarningZone { get; private set; }

        /// <summary>Feeds one head position in the Strip frame, with the current off-strip tolerance from Settings.</summary>
        public void Update(Vector3 headInStrip, float offStripToleranceMeters)
        {
            var tolerance = Math.Max(0.0f, offStripToleranceMeters);
            var wasOnStrip = IsOnStrip;
            var isOnStrip = wasOnStrip || !_hasSample
                ? Geometry.Contains(headInStrip, tolerance)
                : Geometry.Contains(headInStrip, tolerance - HysteresisMeters);

            var previousZone = CurrentWarningZone;
            var zone = isOnStrip ? GetZoneWithHysteresis(headInStrip, previousZone) : null;

            IsOnStrip = isOnStrip;
            CurrentWarningZone = zone;
            var isFirstSample = !_hasSample;
            _hasSample = true;

            if (wasOnStrip && !isOnStrip)
            {
                LeftStrip?.Invoke();
                return;
            }

            if (!wasOnStrip && isOnStrip)
            {
                BackOnStrip?.Invoke();
                return;
            }

            if (!isFirstSample && zone.HasValue && previousZone != zone)
            {
                EnteredWarningZone?.Invoke(zone.Value);
            }
        }

        private StripEnd? GetZoneWithHysteresis(Vector3 headInStrip, StripEnd? previousZone)
        {
            var zone = Geometry.GetWarningZone(headInStrip);
            if (zone.HasValue || !previousZone.HasValue)
            {
                return zone;
            }

            // Just outside the zone it was in: keep it until the head is clearly out.
            return Geometry.GetWarningZone(headInStrip, HysteresisMeters) == previousZone ? previousZone : null;
        }
    }
}
