using System.Collections.Generic;

namespace VrFencing.Dummy
{
    /// <summary>How a Target Zone is shown right now.</summary>
    public enum ZoneLight
    {
        Dim,
        Lit,

        /// <summary>Briefly bright after a Touch, whatever the zone's lit or dim state underneath.</summary>
        Flash,
    }

    /// <summary>
    /// Which Target Zones are lit, dimmed or flashing. Every zone starts dim. A flash lasts
    /// <see cref="FlashSeconds"/> on top of the zone's lit or dim state, so a Drill can flash the touched zone and
    /// dim it in the same frame and the Fencer still sees the flash. Time is passed in, so this runs headless.
    /// </summary>
    public sealed class TargetZoneLights
    {
        public const double FlashSeconds = 0.3;

        private readonly HashSet<TargetZone> _litZones = new HashSet<TargetZone>();
        private readonly Dictionary<TargetZone, double> _flashEndsAtSeconds = new Dictionary<TargetZone, double>();

        public void Light(TargetZone zone)
        {
            _litZones.Add(zone);
        }

        public void Dim(TargetZone zone)
        {
            _litZones.Remove(zone);
        }

        public void DimAll()
        {
            _litZones.Clear();
        }

        /// <summary>Flashes <paramref name="zone"/> from <paramref name="nowSeconds"/>; flashing again restarts it.</summary>
        public void Flash(TargetZone zone, double nowSeconds)
        {
            _flashEndsAtSeconds[zone] = nowSeconds + FlashSeconds;
        }

        public ZoneLight GetLight(TargetZone zone, double nowSeconds)
        {
            if (_flashEndsAtSeconds.TryGetValue(zone, out var flashEndsAt) && nowSeconds < flashEndsAt)
            {
                return ZoneLight.Flash;
            }

            return _litZones.Contains(zone) ? ZoneLight.Lit : ZoneLight.Dim;
        }
    }
}
