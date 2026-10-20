using System;
using System.Globalization;

namespace VrFencing.Partner
{
    /// <summary>
    /// The scoring box as one headset shows it (PER-82, decided in PER-74): two lamps floating above the bout Strip's
    /// centre. In each headset this Fencer's lamp is green and on their side, the opponent's red; both light for a
    /// double; they stay lit <see cref="BoutJudge.LampSeconds"/>. Being touched (the opponent's lamp lighting) also
    /// buzzes both controllers. The round-trip time shows as small text under it.
    /// </summary>
    public sealed class ScoringBox
    {
        private double _litUntilSeconds = double.NegativeInfinity;
        private bool _isMineLit;
        private bool _isOpponentsLit;

        public ScoringBox(BoutSide me)
        {
            Me = me;
        }

        /// <summary>Which Fencer this headset is.</summary>
        public BoutSide Me { get; }

        /// <summary>The last light shown, or null.</summary>
        public BoutLight? LastLight { get; private set; }

        /// <summary>
        /// Shows <paramref name="light"/> from <paramref name="nowSeconds"/> on (this headset's clock). Returns true when
        /// it newly lights the opponent's lamp: this Fencer was touched, and should feel it. A late double that only adds
        /// the opponent's lamp to a light already showing counts too.
        /// </summary>
        public bool Show(BoutLight light, double nowSeconds)
        {
            var wasOpponentsLit = IsOpponentsLampLit(nowSeconds) && LastLight.HasValue && LastLight.Value.Sequence == light.Sequence;
            _isMineLit = light.Lights(Me);
            _isOpponentsLit = light.Lights(Other(Me));
            _litUntilSeconds = nowSeconds + BoutJudge.LampSeconds;
            LastLight = light;
            return _isOpponentsLit && !wasOpponentsLit;
        }

        /// <summary>This Fencer's own lamp (green): they touched.</summary>
        public bool IsMyLampLit(double nowSeconds)
        {
            return _isMineLit && nowSeconds < _litUntilSeconds;
        }

        /// <summary>The opponent's lamp (red): this Fencer was touched.</summary>
        public bool IsOpponentsLampLit(double nowSeconds)
        {
            return _isOpponentsLit && nowSeconds < _litUntilSeconds;
        }

        /// <summary>Turns both lamps off (the partner left).</summary>
        public void Clear()
        {
            _litUntilSeconds = double.NegativeInfinity;
            _isMineLit = false;
            _isOpponentsLit = false;
            LastLight = null;
        }

        public static BoutSide Other(BoutSide side)
        {
            return side == BoutSide.Host ? BoutSide.Partner : BoutSide.Host;
        }

        /// <summary>The small text under the box: the current round trip, or that it is still being measured.</summary>
        public static string FormatPing(double? roundTripSeconds)
        {
            if (!roundTripSeconds.HasValue)
            {
                return "Ping: measuring";
            }

            var milliseconds = Math.Round(roundTripSeconds.Value * 1000.0);
            return "Ping " + milliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms";
        }

        /// <summary>A light as the log and the Editor's Partner window say it, from <paramref name="me"/>'s side.</summary>
        public static string Describe(BoutLight light, BoutSide me)
        {
            switch (light.Kind)
            {
                case LightKind.Double:
                    return $"Double (host {light.HostZone}, partner {light.PartnerZone})";
                case LightKind.HostTouch:
                    return $"{(me == BoutSide.Host ? "Your" : "Opponent's")} Touch ({light.HostZone})";
                default:
                    return $"{(me == BoutSide.Partner ? "Your" : "Opponent's")} Touch ({light.PartnerZone})";
            }
        }
    }
}
