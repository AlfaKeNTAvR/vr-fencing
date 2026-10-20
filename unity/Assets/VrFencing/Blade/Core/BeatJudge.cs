using System.Numerics;
using VrFencing.Dummy;

namespace VrFencing.Blade
{
    /// <summary>
    /// Judges whether a blade-on-blade contact Beats the Armed Dummy's blade held in line (PER-23 resolution, PER-101):
    /// the contact must knock the Dummy's point at least <see cref="OutOfLineMeters"/> off its line. As in the
    /// <see cref="ParryJudge"/>, a clash opens a watch; each frame takes the larger of the measured tip offset (wrist and
    /// arm, <see cref="BladeDeflection.TipOffset"/>) and the deflection the clash's knock will reach
    /// (<see cref="BladeDeflection.GetKickMeters"/>, so a sharp Beat is judged at
    /// once). The watch stays open while the blades touch and for <see cref="ParryJudge.WatchSeconds"/> after the clash; a
    /// watch that ends short of the threshold was a touch, not a Beat. Meters, seconds.
    /// </summary>
    public sealed class BeatJudge
    {
        /// <summary>
        /// The point must be knocked this far off its line for a Beat: about a torso's width, so it no longer threatens
        /// the Fencer's body. A firm beat meets it easily (PER-116): 0.9 m/s on the Dummy's foible (0.8 of the way out)
        /// or 0.75 m/s at its point reaches it (<see cref="BladeDeflection.BeatMetersPerMeterPerSecond"/>), and the point
        /// then stays this far out for about 0.6 s after a 2 m/s beat.
        /// </summary>
        public const float OutOfLineMeters = 0.15f;

        private BladeClash? _clash;
        private float _kickMeters;

        /// <summary>True while a clash is being watched.</summary>
        public bool IsWatching => _clash.HasValue;

        /// <summary>Forget the watch: a new Rep, or the blades jumped.</summary>
        public void Reset()
        {
            _clash = null;
        }

        /// <summary>A clash on the Dummy's blade: opens (or renews) the watch on how far it knocks the point.</summary>
        public void ReportClash(BladeClash clash)
        {
            _clash = clash;
            _kickMeters = BladeDeflection.GetKickMeters(clash.RelativeSpeedMetersPerSecond, clash.OpponentBladeFraction);
        }

        /// <summary>
        /// One frame of the watch. Returns the report once the point is out of line (then the watch closes); null while it
        /// is not, and once the watch ends without it.
        /// </summary>
        /// <param name="tipOffset">The Dummy's point off its line now (<see cref="BladeDeflection.TipOffset"/>).</param>
        /// <param name="isInContact">The blades still touch or press, which keeps the watch open.</param>
        public BeatReport? Step(double nowSeconds, Vector3 tipOffset, bool isInContact)
        {
            if (!_clash.HasValue)
            {
                return null;
            }

            var clash = _clash.Value;
            var outOfLine = System.Math.Max(tipOffset.Length(), _kickMeters);
            if (outOfLine >= OutOfLineMeters)
            {
                _clash = null;
                return new BeatReport(outOfLine, clash.FencerBladeFraction, clash.OpponentBladeFraction);
            }

            if (!isInContact && nowSeconds > clash.TimeSeconds + ParryJudge.WatchSeconds)
            {
                _clash = null;
            }

            return null;
        }
    }
}
