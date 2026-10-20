using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Partner
{
    /// <summary>One bout message to send, and how.</summary>
    public readonly struct BoutOutgoing
    {
        public BoutOutgoing(byte[] message, bool isReliable)
        {
            Message = message;
            IsReliable = isReliable;
        }

        public byte[] Message { get; }

        /// <summary>Claims, lights and rules must arrive; a lost ping is just skipped.</summary>
        public bool IsReliable { get; }
    }

    /// <summary>
    /// One headset's side of the judge's protocol (PER-82, decided in PER-56): the clock sync, this Fencer's Touch
    /// claims, the judge (on the Host only) and the scoring box. Plain C#: the Unity layer gives it the time, this
    /// Fencer's Touches and the partner's messages, sends what <see cref="TakeOutgoing"/> returns, prints
    /// <see cref="TakeLog"/>, draws <see cref="Box"/> and buzzes on <see cref="TakeBeingTouched"/>. Both apps use the
    /// same monotonic clock as the streams (<c>Time.realtimeSinceStartupAsDouble</c>).
    /// <list type="bullet">
    /// <item>Both apps ping every <see cref="ClockSync.PingIntervalSeconds"/>, so both know the round trip; the Partner's
    /// offset puts its claims on the Host's clock.</item>
    /// <item>The Host sends its rules (Touch pressure, double window) on change and every <see cref="RulesResendSeconds"/>.</item>
    /// <item>A Touch claim from the Partner goes to the Host reliably; the Host's own goes straight into its judge.</item>
    /// <item>Each light the judge decides shows on the Host's box and goes to the Partner's.</item>
    /// </list>
    /// </summary>
    public sealed class BoutLink
    {
        /// <summary>The rules go again this often even unchanged, so a Partner that missed them (a rejoin) gets them soon.</summary>
        public const double RulesResendSeconds = 1.0;

        private const string LogTag = "[Bout]";

        private readonly List<BoutOutgoing> _outgoing = new List<BoutOutgoing>();
        private readonly List<string> _log = new List<string>();
        private double _nextPingSeconds = double.NegativeInfinity;
        private double _rulesSentAt = double.NegativeInfinity;
        private bool _areRulesChanged = true;
        private BoutRules _myRules = new BoutRules(TouchPressure.Realistic, BoutJudge.DefaultDoubleWindowSeconds);
        private BoutRules? _hostRules;
        private int _nextClaimSequence = 1;
        private bool _isTouched;

        public BoutLink(BoutSide me)
        {
            Me = me;
            Box = new ScoringBox(me);
            Judge = me == BoutSide.Host ? new BoutJudge() : null;
        }

        public BoutSide Me { get; }

        public ClockSync Clock { get; } = new ClockSync();

        /// <summary>The judge, on the Host only (null on the Partner).</summary>
        public BoutJudge Judge { get; }

        public ScoringBox Box { get; }

        /// <summary>
        /// The rules in force: the Host's own; on the Partner, the Host's as last received, or its own Settings until
        /// they come.
        /// </summary>
        public BoutRules Rules => Me == BoutSide.Host ? _myRules : _hostRules ?? _myRules;

        /// <summary>True on the Partner once the Host's rules arrived (always true on the Host).</summary>
        public bool HasHostRules => Me == BoutSide.Host || _hostRules.HasValue;

        /// <summary>Messages dropped as unreadable or wrong for this side since the start.</summary>
        public int RejectedMessages { get; private set; }

        /// <summary>
        /// This Fencer's own Settings for the bout (Touch pressure, double window). On the Host they are the bout's
        /// rules and go to the Partner; on the Partner they only stand in until the Host's arrive.
        /// </summary>
        public void SetMyRules(BoutRules rules)
        {
            var clamped = new BoutRules(rules.Pressure, BoutJudge.ClampDoubleWindow(rules.DoubleWindowSeconds));
            _areRulesChanged |= clamped.Pressure != _myRules.Pressure || clamped.DoubleWindowSeconds != _myRules.DoubleWindowSeconds;
            _myRules = clamped;
            if (Judge != null)
            {
                Judge.DoubleWindowSeconds = clamped.DoubleWindowSeconds;
            }
        }

        /// <summary>Sends what is due at <paramref name="nowSeconds"/> (ping, rules) and, on the Host, shows a light the judge decided.</summary>
        public void Tick(double nowSeconds)
        {
            if (nowSeconds >= _nextPingSeconds)
            {
                _outgoing.Add(new BoutOutgoing(BoutMessages.EncodePing(nowSeconds), false));
                _nextPingSeconds = nowSeconds + ClockSync.PingIntervalSeconds;
            }

            if (Judge == null)
            {
                return;
            }

            if (_areRulesChanged || nowSeconds - _rulesSentAt >= RulesResendSeconds)
            {
                if (_areRulesChanged)
                {
                    _log.Add($"{LogTag} Bout rules: Touch pressure {_myRules.Pressure}, double window {FormatMilliseconds(_myRules.DoubleWindowSeconds)}.");
                }

                _outgoing.Add(new BoutOutgoing(BoutMessages.EncodeRules(_myRules), true));
                _rulesSentAt = nowSeconds;
                _areRulesChanged = false;
            }

            var light = Judge.Step(nowSeconds);
            if (light.HasValue)
            {
                Show(light.Value, nowSeconds);
                _outgoing.Add(new BoutOutgoing(BoutMessages.EncodeLight(light.Value), true));
            }
        }

        /// <summary>
        /// This Fencer touched the opponent in <paramref name="zone"/>, pressed at <paramref name="contactLocalSeconds"/>
        /// on this app's clock. The Host judges it at once; the Partner sends it on the Host's clock, or drops it (false)
        /// while the clock is not synced yet.
        /// </summary>
        public bool Claim(TargetZone zone, double contactLocalSeconds, bool isOffStrip, double nowSeconds)
        {
            var sequence = _nextClaimSequence++;
            if (Me == BoutSide.Host)
            {
                var claim = new TouchClaim(sequence, BoutSide.Host, zone, contactLocalSeconds, isOffStrip);
                LogVerdict(claim, Judge.Submit(claim, nowSeconds), nowSeconds);
                return true;
            }

            var offset = Clock.OffsetSeconds;
            if (!offset.HasValue)
            {
                _log.Add($"{LogTag} Touch {sequence} ({zone}) not claimed: the clock is not synced with the host yet.");
                return false;
            }

            var hostClaim = new TouchClaim(sequence, BoutSide.Partner, zone, contactLocalSeconds + offset.Value, isOffStrip);
            _outgoing.Add(new BoutOutgoing(BoutMessages.EncodeClaim(hostClaim), true));
            _log.Add($"{LogTag} Touch {sequence} ({zone}{(isOffStrip ? ", off the Strip" : string.Empty)}) claimed to the host.");
            return true;
        }

        /// <summary>
        /// Takes one message from the partner, received at <paramref name="nowSeconds"/>. Returns false (and counts it)
        /// when it cannot be read or is not for this side.
        /// </summary>
        public bool Receive(byte[] message, double nowSeconds)
        {
            try
            {
                switch (BoutMessages.ReadKind(message))
                {
                    case BoutMessages.PingKind:
                        _outgoing.Add(new BoutOutgoing(BoutMessages.EncodePong(BoutMessages.DecodePing(message), nowSeconds), false));
                        return true;
                    case BoutMessages.PongKind:
                        var pong = BoutMessages.DecodePong(message);
                        return Clock.AddRoundTrip(pong.EchoedSenderSeconds, pong.ReplierSeconds, nowSeconds) || Reject();
                    case BoutMessages.ClaimKind:
                        return Me == BoutSide.Host ? ReceiveClaim(BoutMessages.DecodeClaim(message), nowSeconds) : Reject();
                    case BoutMessages.LightMessageKind:
                        if (Me == BoutSide.Host)
                        {
                            return Reject();
                        }

                        Show(BoutMessages.DecodeLight(message), nowSeconds);
                        return true;
                    case BoutMessages.RulesKind:
                        return Me == BoutSide.Partner ? ReceiveRules(BoutMessages.DecodeRules(message)) : Reject();
                    default:
                        return Reject();
                }
            }
            catch (InvalidDataException)
            {
                return Reject();
            }
        }

        /// <summary>The messages to send since the last call, in order.</summary>
        public IReadOnlyList<BoutOutgoing> TakeOutgoing()
        {
            var taken = _outgoing.ToArray();
            _outgoing.Clear();
            return taken;
        }

        /// <summary>The log lines since the last call, in order.</summary>
        public IReadOnlyList<string> TakeLog()
        {
            var taken = _log.ToArray();
            _log.Clear();
            return taken;
        }

        /// <summary>True once after a light that touched this Fencer: buzz both controllers.</summary>
        public bool TakeBeingTouched()
        {
            var isTouched = _isTouched;
            _isTouched = false;
            return isTouched;
        }

        /// <summary>Forgets the partner (they left, or the session ended), so a rejoin starts clean.</summary>
        public void Clear()
        {
            Clock.Reset();
            Judge?.Reset();
            Box.Clear();
            _outgoing.Clear();
            _hostRules = null;
            _nextPingSeconds = double.NegativeInfinity;
            _rulesSentAt = double.NegativeInfinity;
            _areRulesChanged = true;
            _isTouched = false;
        }

        private bool ReceiveClaim(TouchClaim claim, double nowSeconds)
        {
            if (claim.Attacker != BoutSide.Partner)
            {
                return Reject();
            }

            LogVerdict(claim, Judge.Submit(claim, nowSeconds), nowSeconds);
            return true;
        }

        private bool ReceiveRules(BoutRules rules)
        {
            if (!_hostRules.HasValue || _hostRules.Value.Pressure != rules.Pressure || _hostRules.Value.DoubleWindowSeconds != rules.DoubleWindowSeconds)
            {
                _log.Add($"{LogTag} The host's bout rules: Touch pressure {rules.Pressure}, double window {FormatMilliseconds(rules.DoubleWindowSeconds)}.");
            }

            _hostRules = rules;
            return true;
        }

        private void Show(BoutLight light, double nowSeconds)
        {
            _isTouched |= Box.Show(light, nowSeconds);
            _log.Add($"{LogTag} Light {light.Sequence}: {ScoringBox.Describe(light, Me)}.");
        }

        private void LogVerdict(TouchClaim claim, ClaimVerdict verdict, double nowSeconds)
        {
            var who = claim.Attacker == Me ? "this Fencer" : "the partner";
            var age = FormatMilliseconds(nowSeconds - claim.ContactHostSeconds);
            _log.Add($"{LogTag} Touch {claim.Sequence} by {who} ({claim.Zone}, {age} old): {verdict}.");
        }

        private bool Reject()
        {
            RejectedMessages++;
            return false;
        }

        private static string FormatMilliseconds(double seconds)
        {
            return Math.Round(seconds * 1000.0).ToString("0", CultureInfo.InvariantCulture) + " ms";
        }
    }
}
