using System.Numerics;
using VrFencing.Dummy;

namespace VrFencing.Blade
{
    /// <summary>
    /// Judges whether a blade-on-blade contact Beats the Armed Dummy's blade held in line (PER-23 resolution, PER-101):
    /// the contact must knock the Dummy's point at least <see cref="OutOfLineMeters"/> off its line. As in the
    /// <see cref="ParryJudge"/>, a clash opens a watch; each frame takes the larger of the swing's measured tip offset
    /// and the peak the clash's kick will reach (<see cref="BladeSwing.GetPeakMeters"/>, so a sharp Beat is judged at
    /// once). The watch stays open while the blades touch and for <see cref="ParryJudge.WatchSeconds"/> after the clash; a
    /// watch that ends short of the threshold was a touch, not a Beat. Meters, seconds.
    /// </summary>
    public sealed class BeatJudge
    {
        /// <summary>
        /// The point must be knocked this far off its line for a Beat: about a torso's width, so it no longer threatens
        /// the Fencer's body. A first guess, to tune on the headset: a clash at about 1 m/s near the Dummy's point reaches
        /// it (<see cref="BladeSwing.PeakMetersPerMeterPerSecond"/>).
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
            _kickMeters = BladeSwing.GetPeakMeters(clash.RelativeSpeedMetersPerSecond, clash.OpponentBladeFraction);
        }

        /// <summary>
        /// One frame of the watch. Returns the report once the point is out of line (then the watch closes); null while it
        /// is not, and once the watch ends without it.
        /// </summary>
        /// <param name="tipOffset">The Dummy's point off its line now (<see cref="BladeSwing.TipOffset"/>).</param>
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
