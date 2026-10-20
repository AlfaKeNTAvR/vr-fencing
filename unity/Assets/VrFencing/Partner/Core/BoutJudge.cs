using System;
using VrFencing.Dummy;

namespace VrFencing.Partner
{
    /// <summary>Which Fencer of a two-headset session: the Host (who runs the judge) or the Partner who joined.</summary>
    public enum BoutSide
    {
        Host,
        Partner,
    }

    /// <summary>
    /// "I touched the opponent" (PER-82, decided in PER-56): the attacker's own headset detects its Touches against the
    /// opponent as drawn there and claims them. The contact time is when the point was pressed (when a real box would
    /// register it), on the Host's clock.
    /// </summary>
    public readonly struct TouchClaim
    {
        public TouchClaim(int sequence, BoutSide attacker, TargetZone zone, double contactHostSeconds, bool isOffStrip)
        {
            Sequence = sequence;
            Attacker = attacker;
            Zone = zone;
            ContactHostSeconds = contactHostSeconds;
            IsOffStrip = isOffStrip;
        }

        /// <summary>Counts up per attacker, for the logs.</summary>
        public int Sequence { get; }

        public BoutSide Attacker { get; }

        /// <summary>Where on the opponent the point landed.</summary>
        public TargetZone Zone { get; }

        public double ContactHostSeconds { get; }

        /// <summary>The attacker had both feet off the side or past the rear end of their own Strip: never lights.</summary>
        public bool IsOffStrip { get; }
    }

    /// <summary>What the judge lit.</summary>
    public enum LightKind
    {
        /// <summary>Only the Host's Touch.</summary>
        HostTouch,

        /// <summary>Only the Partner's Touch.</summary>
        PartnerTouch,

        /// <summary>Both, within the double window of each other.</summary>
        Double,
    }

    /// <summary>A light decided by the judge (both headsets show it on their scoring box).</summary>
    public readonly struct BoutLight
    {
        public BoutLight(int sequence, LightKind kind, TargetZone? hostZone, TargetZone? partnerZone)
        {
            Sequence = sequence;
            Kind = kind;
            HostZone = hostZone;
            PartnerZone = partnerZone;
        }

        /// <summary>Counts up per light. A late double keeps its single's number (it upgrades that light).</summary>
        public int Sequence { get; }

        public LightKind Kind { get; }

        /// <summary>Where the Host's Touch landed, if it lit.</summary>
        public TargetZone? HostZone { get; }

        /// <summary>Where the Partner's Touch landed, if it lit.</summary>
        public TargetZone? PartnerZone { get; }

        /// <summary>True when <paramref name="side"/>'s lamp is lit by this light.</summary>
        public bool Lights(BoutSide side)
        {
            return Kind == LightKind.Double || Kind == (side == BoutSide.Host ? LightKind.HostTouch : LightKind.PartnerTouch);
        }
    }

    /// <summary>What the judge did with a claim.</summary>
    public enum ClaimVerdict
    {
        /// <summary>Waiting for the light delay; it may light.</summary>
        Accepted,

        /// <summary>It came in after its side's light, but within the double window of the other Touch: that light is now a double.</summary>
        MadeDouble,

        /// <summary>Older than <see cref="BoutJudge.StaleClaimSeconds"/> when it arrived: too late to judge fairly.</summary>
        Stale,

        /// <summary>Its contact time is more than <see cref="BoutJudge.StaleClaimSeconds"/> ahead of the Host's clock: a broken clock.</summary>
        FromTheFuture,

        /// <summary>The attacker was off their own Strip.</summary>
        OffStrip,

        /// <summary>
        /// Outside a phrase: before "Allez!", after a halt, or after the phrase's light (in a Bout nothing counts until
        /// the next "Allez!"; in Paired Practice the next Touch counts again right after a light).
        /// </summary>
        LockedOut,

        /// <summary>The same attacker already has a Touch waiting in this phrase: first one wins.</summary>
        AlreadyClaimed,
    }

    /// <summary>
    /// The virtual judge of a Bout (PER-82, rules decided in PER-56), run on the Host only, on the Host's monotonic clock.
    /// Plain C#: the Unity layer feeds it claims and the time and shows what it lights.
    /// <list type="bullet">
    /// <item>First claim wins; the other Fencer's claim within the double window of it (either way) makes a double.</item>
    /// <item>It waits <see cref="LightDelaySeconds"/> after the earliest Touch before lighting (the double window plus
    /// <see cref="LagAllowanceSeconds"/> for the other claim's one-way trip and clock error), so a double's second claim
    /// is normally in by then. If one still arrives in time (not stale) after a single lit, the light becomes a double.</item>
    /// <item>Claims older than <see cref="StaleClaimSeconds"/> on arrival are rejected, as are claims made off the
    /// attacker's own Strip while <see cref="RefusesOffStrip"/> (a Bout; Paired Practice lights them, PER-72).</item>
    /// <item>Touches count only inside the phrase: from <see cref="OpenPhrase"/> ("Allez!") until <see cref="ClosePhrase"/>
    /// (a halt) or the phrase's light. With <see cref="KeepsPhraseClosedAfterLight"/> (a Bout, PER-83) nothing counts
    /// after a light until the bout flow opens the next phrase; without it (Paired Practice: no lock-out) the next
    /// Touch counts right after the light. A new judge has its phrase open.</item>
    /// </list>
    /// </summary>
    public sealed class BoutJudge
    {
        /// <summary>The double window by default: rule-faithful (FIE épée: 1/25 to 1/20 s).</summary>
        public const double DefaultDoubleWindowSeconds = 0.05;

        /// <summary>The longest double window the Host may set (for beginners or a bad connection).</summary>
        public const double MaxDoubleWindowSeconds = 0.15;

        /// <summary>The shortest double window the Host may set: the rule's own.</summary>
        public const double MinDoubleWindowSeconds = 0.05;

        /// <summary>
        /// Added to the double window for the light delay: the other claim's one-way trip and the clock error (PER-56:
        /// about 120 ms in all at the default window).
        /// </summary>
        public const double LagAllowanceSeconds = 0.07;

        /// <summary>A claim older than this when it reaches the judge is rejected (PER-56).</summary>
        public const double StaleClaimSeconds = 0.2;

        /// <summary>How long the lamps stay lit (PER-74).</summary>
        public const double LampSeconds = 2.0;

        private double _doubleWindowSeconds = DefaultDoubleWindowSeconds;
        private TouchClaim? _hostPending;
        private TouchClaim? _partnerPending;
        private BoutLight? _upgrade;
        private BoutLight? _lastLight;
        private double _lastLightEarliestContact;
        private double _phraseOpenFromSeconds = double.NegativeInfinity;
        private double _phraseClosedFromSeconds = double.PositiveInfinity;
        private int _nextLightSequence = 1;

        /// <summary>The double window in use, kept within <see cref="MinDoubleWindowSeconds"/> to <see cref="MaxDoubleWindowSeconds"/>.</summary>
        public double DoubleWindowSeconds
        {
            get => _doubleWindowSeconds;
            set => _doubleWindowSeconds = ClampDoubleWindow(value);
        }

        /// <summary>How long after the earliest Touch the judge lights: the double window plus <see cref="LagAllowanceSeconds"/>.</summary>
        public double LightDelaySeconds => _doubleWindowSeconds + LagAllowanceSeconds;

        /// <summary>
        /// A Bout (true, the default): after a light nothing counts until <see cref="OpenPhrase"/>. Paired Practice
        /// (false): the next Touch after a light counts at once.
        /// </summary>
        public bool KeepsPhraseClosedAfterLight { get; set; } = true;

        /// <summary>A Bout (true, the default): a Touch made off the attacker's own Strip never lights. Paired Practice: it does.</summary>
        public bool RefusesOffStrip { get; set; } = true;

        /// <summary>True while a Touch waits for the light delay.</summary>
        public bool HasPendingTouch => _hostPending.HasValue || _partnerPending.HasValue;

        /// <summary><paramref name="seconds"/> kept within the Host Setting's range; not a number gives the default.</summary>
        public static double ClampDoubleWindow(double seconds)
        {
            if (double.IsNaN(seconds))
            {
                return DefaultDoubleWindowSeconds;
            }

            return Math.Max(MinDoubleWindowSeconds, Math.Min(MaxDoubleWindowSeconds, seconds));
        }

        /// <summary>True outside the phrase: a Touch made now is locked out.</summary>
        public bool IsLockedOut(double hostNowSeconds)
        {
            return !IsInPhrase(hostNowSeconds);
        }

        /// <summary>"Allez!": Touches made from <paramref name="hostSeconds"/> on count, until a halt or the phrase's light.</summary>
        public void OpenPhrase(double hostSeconds)
        {
            _phraseOpenFromSeconds = hostSeconds;
            _phraseClosedFromSeconds = double.PositiveInfinity;
        }

        /// <summary>
        /// A halt at <paramref name="hostSeconds"/>: Touches made from then on are locked out. One made before it and still
        /// on its way (or waiting for the light delay) still lights, as a real box registers a Touch before the "Halte!".
        /// </summary>
        public void ClosePhrase(double hostSeconds)
        {
            _phraseClosedFromSeconds = Math.Min(_phraseClosedFromSeconds, hostSeconds);
        }

        /// <summary>Takes a claim that reached the Host at <paramref name="hostNowSeconds"/>.</summary>
        public ClaimVerdict Submit(TouchClaim claim, double hostNowSeconds)
        {
            var age = hostNowSeconds - claim.ContactHostSeconds;
            if (age < -StaleClaimSeconds)
            {
                return ClaimVerdict.FromTheFuture;
            }

            if (age > StaleClaimSeconds)
            {
                return ClaimVerdict.Stale;
            }

            if (claim.IsOffStrip && RefusesOffStrip)
            {
                return ClaimVerdict.OffStrip;
            }

            if (!IsInPhrase(claim.ContactHostSeconds))
            {
                return TryMakeDouble(claim) ? ClaimVerdict.MadeDouble : ClaimVerdict.LockedOut;
            }

            if (claim.Attacker == BoutSide.Host)
            {
                if (_hostPending.HasValue)
                {
                    return ClaimVerdict.AlreadyClaimed;
                }

                _hostPending = claim;
            }
            else
            {
                if (_partnerPending.HasValue)
                {
                    return ClaimVerdict.AlreadyClaimed;
                }

                _partnerPending = claim;
            }

            return ClaimVerdict.Accepted;
        }

        /// <summary>
        /// The light to show now, once: when the light delay since the earliest waiting Touch has passed, or when a late
        /// claim made the last light a double. Null otherwise.
        /// </summary>
        public BoutLight? Step(double hostNowSeconds)
        {
            if (_upgrade.HasValue)
            {
                var upgrade = _upgrade.Value;
                _upgrade = null;
                return upgrade;
            }

            if (!HasPendingTouch)
            {
                return null;
            }

            var earliest = Earliest();
            if (hostNowSeconds < earliest.ContactHostSeconds + LightDelaySeconds)
            {
                return null;
            }

            var other = earliest.Attacker == BoutSide.Host ? _partnerPending : _hostPending;
            var isDouble = other.HasValue && other.Value.ContactHostSeconds - earliest.ContactHostSeconds <= _doubleWindowSeconds;
            BoutLight light;
            if (isDouble)
            {
                light = new BoutLight(_nextLightSequence++, LightKind.Double, _hostPending.Value.Zone, _partnerPending.Value.Zone);
            }
            else if (earliest.Attacker == BoutSide.Host)
            {
                light = new BoutLight(_nextLightSequence++, LightKind.HostTouch, earliest.Zone, null);
            }
            else
            {
                light = new BoutLight(_nextLightSequence++, LightKind.PartnerTouch, null, earliest.Zone);
            }

            _hostPending = null;
            _partnerPending = null;
            _lastLight = light;
            _lastLightEarliestContact = earliest.ContactHostSeconds;

            // A Touch after the window lost the race. In a Bout the phrase stays closed until the next "Allez!"; in Paired
            // Practice a Touch made after this light is the next one.
            if (KeepsPhraseClosedAfterLight)
            {
                _phraseClosedFromSeconds = Math.Min(_phraseClosedFromSeconds, earliest.ContactHostSeconds);
            }
            else
            {
                _phraseOpenFromSeconds = Math.Max(_phraseOpenFromSeconds, hostNowSeconds);
            }

            return light;
        }

        /// <summary>Forgets every waiting Touch and light (the partner left, a new session); the phrase is open again.</summary>
        public void Reset()
        {
            _hostPending = null;
            _partnerPending = null;
            _upgrade = null;
            _lastLight = null;
            _phraseOpenFromSeconds = double.NegativeInfinity;
            _phraseClosedFromSeconds = double.PositiveInfinity;
        }

        private bool IsInPhrase(double contactHostSeconds)
        {
            return contactHostSeconds >= _phraseOpenFromSeconds && contactHostSeconds < _phraseClosedFromSeconds;
        }

        private TouchClaim Earliest()
        {
            if (!_hostPending.HasValue)
            {
                return _partnerPending.Value;
            }

            if (!_partnerPending.HasValue)
            {
                return _hostPending.Value;
            }

            // A tie goes to neither: both are within the window anyway, so it is a double.
            return _partnerPending.Value.ContactHostSeconds < _hostPending.Value.ContactHostSeconds ? _partnerPending.Value : _hostPending.Value;
        }

        /// <summary>A claim from the side that did not light, within the window of the single that did: that light becomes a double.</summary>
        private bool TryMakeDouble(TouchClaim claim)
        {
            if (!_lastLight.HasValue || _lastLight.Value.Kind == LightKind.Double || _lastLight.Value.Lights(claim.Attacker))
            {
                return false;
            }

            if (Math.Abs(claim.ContactHostSeconds - _lastLightEarliestContact) > _doubleWindowSeconds)
            {
                return false;
            }

            var last = _lastLight.Value;
            var hostZone = claim.Attacker == BoutSide.Host ? claim.Zone : last.HostZone;
            var partnerZone = claim.Attacker == BoutSide.Partner ? claim.Zone : last.PartnerZone;
            var upgraded = new BoutLight(last.Sequence, LightKind.Double, hostZone, partnerZone);
            _lastLight = upgraded;
            _upgrade = upgraded;
            return true;
        }
    }
}
