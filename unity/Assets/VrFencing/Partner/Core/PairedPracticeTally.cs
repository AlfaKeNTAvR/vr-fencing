using VrFencing.Session;

namespace VrFencing.Partner
{
    /// <summary>
    /// The Paired Practice tally (PER-72, built in PER-83): per Fencer, their Touches that lit alone, the doubles (counted
    /// for both), and their glances. The Host counts (it sees every light, its own glances and the Partner's reported
    /// ones) and sends the counts to the Partner, who only shows them. Either Fencer can reset it. A late double that
    /// upgrades a single light moves that Touch to the doubles.
    /// </summary>
    public sealed class PairedPracticeTally
    {
        private int? _lastCountedSequence;
        private LightKind _lastCountedKind;

        public PairedPracticeCounts Host { get; private set; }

        public PairedPracticeCounts Partner { get; private set; }

        /// <summary>Nothing counted on either side.</summary>
        public bool IsEmpty => Host.IsEmpty && Partner.IsEmpty;

        public PairedPracticeCounts For(BoutSide side)
        {
            return side == BoutSide.Host ? Host : Partner;
        }

        /// <summary>Counts a light; the same light again (a late double) moves its single Touch to the doubles.</summary>
        public void CountLight(BoutLight light)
        {
            if (_lastCountedSequence == light.Sequence)
            {
                if (_lastCountedKind != LightKind.Double && light.Kind == LightKind.Double)
                {
                    var single = _lastCountedKind == LightKind.HostTouch ? BoutSide.Host : BoutSide.Partner;
                    Change(single, touches: -1);
                    Change(BoutSide.Host, doubles: 1);
                    Change(BoutSide.Partner, doubles: 1);
                    _lastCountedKind = LightKind.Double;
                }

                return;
            }

            _lastCountedSequence = light.Sequence;
            _lastCountedKind = light.Kind;
            switch (light.Kind)
            {
                case LightKind.Double:
                    Change(BoutSide.Host, doubles: 1);
                    Change(BoutSide.Partner, doubles: 1);
                    break;
                case LightKind.HostTouch:
                    Change(BoutSide.Host, touches: 1);
                    break;
                default:
                    Change(BoutSide.Partner, touches: 1);
                    break;
            }
        }

        public void CountGlance(BoutSide attacker)
        {
            Change(attacker, glances: 1);
        }

        /// <summary>Both back to zero. A light from before the reset that turns into a double counts as a new double.</summary>
        public void Reset()
        {
            Host = default;
            Partner = default;
            _lastCountedSequence = null;
        }

        /// <summary>Takes the Host's counts (on the Partner, from the Host's message).</summary>
        public void Replace(PairedPracticeCounts host, PairedPracticeCounts partner)
        {
            Host = host;
            Partner = partner;
        }

        private void Change(BoutSide side, int touches = 0, int doubles = 0, int glances = 0)
        {
            var counts = For(side);
            var changed = new PairedPracticeCounts(counts.Touches + touches, counts.Doubles + doubles, counts.Glances + glances);
            if (side == BoutSide.Host)
            {
                Host = changed;
            }
            else
            {
                Partner = changed;
            }
        }
    }
}
