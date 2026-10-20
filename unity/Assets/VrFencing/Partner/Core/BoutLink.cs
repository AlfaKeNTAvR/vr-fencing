using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using VrFencing.Dummy;
using VrFencing.Session;
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

        /// <summary>Claims, lights, rules and the flow must arrive; a lost ping or stance is just skipped.</summary>
        public bool IsReliable { get; }
    }

    /// <summary>A Paired Practice tally that ended on this headset, to save to the Session log.</summary>
    public readonly struct EndedTally
    {
        public EndedTally(PairedPracticeEnd end, PairedPracticeCounts mine, PairedPracticeCounts opponents)
        {
            End = end;
            Mine = mine;
            Opponents = opponents;
        }

        public PairedPracticeEnd End { get; }

        public PairedPracticeCounts Mine { get; }

        public PairedPracticeCounts Opponents { get; }
    }

    /// <summary>
    /// One headset's side of the judge's protocol (PER-82, decided in PER-56) and the bout flow (PER-83): the clock
    /// sync, this Fencer's Touch claims, the judge and the flow (on the Host only), the scoring box, the calls and the
    /// Paired Practice tally. Plain C#: the Unity layer gives it the time, the mode, this Fencer's stance, Touches and
    /// glances, and the partner's messages; it sends what <see cref="TakeOutgoing"/> returns, prints
    /// <see cref="TakeLog"/>, plays <see cref="TakeCalls"/>, draws <see cref="Box"/> and <see cref="Announcer"/>, buzzes
    /// on <see cref="TakeBeingTouched"/> and saves <see cref="TakeEndedTallies"/>. Both apps use the same monotonic
    /// clock as the streams (<c>Time.realtimeSinceStartupAsDouble</c>).
    /// <list type="bullet">
    /// <item>Both apps ping every <see cref="ClockSync.PingIntervalSeconds"/>, so both know the round trip; the Partner's
    /// offset puts its claims on the Host's clock.</item>
    /// <item>The Host sends its rules (Touch pressure, double window) and the flow on change and every
    /// <see cref="RulesResendSeconds"/>; the Partner sends its stance every <see cref="StanceIntervalSeconds"/>.</item>
    /// <item>A Touch claim from the Partner goes to the Host reliably; the Host's own goes straight into its judge.</item>
    /// <item>Each light the judge decides shows on the Host's box and goes to the Partner's; in a Bout it halts.</item>
    /// </list>
    /// </summary>
    public sealed class BoutLink
    {
        /// <summary>The rules and the flow go again this often even unchanged, so a Partner that missed them (a rejoin) gets them soon.</summary>
        public const double RulesResendSeconds = 1.0;

        /// <summary>The Partner reports its stance this often (unreliable).</summary>
        public const double StanceIntervalSeconds = 0.1;

        /// <summary>A Partner stance older than this is unknown to the Host's flow: never ready, never off.</summary>
        public const double StanceStaleSeconds = 0.5;

        private const string LogTag = "[Bout]";

        private readonly List<BoutOutgoing> _outgoing = new List<BoutOutgoing>();
        private readonly List<string> _log = new List<string>();
        private readonly List<BoutCallClip> _calls = new List<BoutCallClip>();
        private readonly List<EndedTally> _endedTallies = new List<EndedTally>();
        private double _nextPingSeconds = double.NegativeInfinity;
        private double _nextStanceSeconds = double.NegativeInfinity;
        private double _rulesSentAt = double.NegativeInfinity;
        private double _flowSentAt = double.NegativeInfinity;
        private bool _areRulesChanged = true;
        private bool _isFlowChanged = true;
        private BoutRules _myRules = new BoutRules(TouchPressure.Realistic, BoutJudge.DefaultDoubleWindowSeconds);
        private BoutRules? _hostRules;
        private int _nextClaimSequence = 1;
        private bool _isTouched;
        private FencerStance? _myStance;
        private FencerStance? _partnerStance;
        private double _partnerStanceAtSeconds = double.NegativeInfinity;
        private float? _bodiesApartMeters;
        private double _callStartLocalSeconds;
        private BoutFlowState _receivedFlow = BoutFlowState.Initial;
        private bool _hasHostFlow;

        public BoutLink(BoutSide me)
        {
            Me = me;
            Box = new ScoringBox(me);
            Announcer = new BoutAnnouncer(me);
            Judge = me == BoutSide.Host ? new BoutJudge() : null;
            Flow = Judge != null ? new BoutFlow(Judge) : null;
        }

        public BoutSide Me { get; }

        public ClockSync Clock { get; } = new ClockSync();

        /// <summary>The judge, on the Host only (null on the Partner).</summary>
        public BoutJudge Judge { get; }

        /// <summary>The bout flow, on the Host only (null on the Partner).</summary>
        public BoutFlow Flow { get; private set; }

        public ScoringBox Box { get; }

        /// <summary>The call to write now, and the halt's reason.</summary>
        public BoutAnnouncer Announcer { get; }

        /// <summary>The Paired Practice tally: counted on the Host, as last received on the Partner.</summary>
        public PairedPracticeTally Tally { get; } = new PairedPracticeTally();

        /// <summary>The flow in force: the Host's own; on the Partner, the Host's as last received.</summary>
        public BoutFlowState FlowState => Flow != null ? Flow.State : _receivedFlow;

        /// <summary>True on the Partner once the Host's flow arrived (always true on the Host).</summary>
        public bool HasHostFlow => Flow != null || _hasHostFlow;

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

        /// <summary>
        /// The Host's mode switch (Bout / Paired Practice), on the Host; ignored on the Partner, which follows the Host's
        /// flow. Leaving Paired Practice ends its tally.
        /// </summary>
        public void SetMode(PartnerMode mode, double nowSeconds)
        {
            if (Flow == null || !Flow.SetMode(mode, nowSeconds))
            {
                return;
            }

            if (mode == PartnerMode.Bout)
            {
                EndTally(PairedPracticeEnd.SwitchedToBout);
            }

            _isFlowChanged = true;
            _log.Add($"{LogTag} Mode: {PartnerPageText.FormatModeName(mode)}.");
        }

        /// <summary>
        /// This Fencer's stance this frame (null: unknown), and how far apart the two bodies are as drawn here (the Host
        /// halts on corps-a-corps with it; the Partner's is not used).
        /// </summary>
        public void SetMyStance(FencerStance? stance, float? bodiesApartMeters)
        {
            _myStance = stance;
            _bodiesApartMeters = bodiesApartMeters;
        }

        /// <summary>Sends what is due at <paramref name="nowSeconds"/> (ping, rules, flow, stance), runs the judge and flow on the Host, and the calls.</summary>
        public void Tick(double nowSeconds)
        {
            if (nowSeconds >= _nextPingSeconds)
            {
                _outgoing.Add(new BoutOutgoing(BoutMessages.EncodePing(nowSeconds), false));
                _nextPingSeconds = nowSeconds + ClockSync.PingIntervalSeconds;
            }

            if (Judge == null)
            {
                SendStanceIfDue(nowSeconds);
                StepCalls(_callStartLocalSeconds, nowSeconds);
                return;
            }

            SendRulesIfDue(nowSeconds);
            var before = Flow.State;
            var light = Judge.Step(nowSeconds);
            if (light.HasValue)
            {
                Show(light.Value, nowSeconds);
                _outgoing.Add(new BoutOutgoing(BoutMessages.EncodeLight(light.Value), true));
                Flow.OnLight(light.Value, nowSeconds);
                if (FlowState.Mode == PartnerMode.PairedPractice)
                {
                    Tally.CountLight(light.Value);
                    _isFlowChanged = true;
                }
            }

            var partnerStance = nowSeconds - _partnerStanceAtSeconds <= StanceStaleSeconds ? _partnerStance : null;
            Flow.Step(nowSeconds, _myStance, partnerStance, _bodiesApartMeters);
            var after = Flow.State;
            if (after.Sequence != before.Sequence)
            {
                LogFlow(after);
            }

            _isFlowChanged |= after.Sequence != before.Sequence || after.IsHostReady != before.IsHostReady || after.IsPartnerReady != before.IsPartnerReady;
            SendFlowIfDue(nowSeconds);
            StepCalls(after.CallStartHostSeconds, nowSeconds);
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

        /// <summary>This Fencer's point glanced off the opponent: counted in Paired Practice (on the Host, or sent to it).</summary>
        public void ReportGlance()
        {
            if (FlowState.Mode != PartnerMode.PairedPractice)
            {
                return;
            }

            if (Me == BoutSide.Host)
            {
                Tally.CountGlance(BoutSide.Host);
                _isFlowChanged = true;
            }
            else
            {
                _outgoing.Add(new BoutOutgoing(BoutMessages.EncodeGlance(), true));
            }
        }

        /// <summary>
        /// Either Fencer resets the Paired Practice tally: the Host at once, the Partner by asking the Host. The tally
        /// as this headset knew it is saved first (<see cref="TakeEndedTallies"/>).
        /// </summary>
        public void ResetTally()
        {
            if (Me == BoutSide.Host)
            {
                EndTally(PairedPracticeEnd.Reset);
                _log.Add($"{LogTag} Paired Practice tally reset.");
                return;
            }

            EndTally(PairedPracticeEnd.Reset);
            _outgoing.Add(new BoutOutgoing(BoutMessages.EncodeTallyReset(), true));
            _log.Add($"{LogTag} Asked the host to reset the Paired Practice tally.");
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
                    case BoutMessages.StanceKind:
                        if (Me != BoutSide.Host)
                        {
                            return Reject();
                        }

                        _partnerStance = BoutMessages.DecodeStance(message);
                        _partnerStanceAtSeconds = nowSeconds;
                        return true;
                    case BoutMessages.FlowKind:
                        return Me == BoutSide.Partner ? ReceiveFlow(message, nowSeconds) : Reject();
                    case BoutMessages.GlanceKind:
                        if (Me != BoutSide.Host)
                        {
                            return Reject();
                        }

                        BoutMessages.DecodeGlance(message);
                        if (FlowState.Mode == PartnerMode.PairedPractice)
                        {
                            Tally.CountGlance(BoutSide.Partner);
                            _isFlowChanged = true;
                        }

                        return true;
                    case BoutMessages.TallyResetKind:
                        if (Me != BoutSide.Host)
                        {
                            return Reject();
                        }

                        BoutMessages.DecodeTallyReset(message);
                        EndTally(PairedPracticeEnd.Reset);
                        _log.Add($"{LogTag} The partner reset the Paired Practice tally.");
                        return true;
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

        /// <summary>The calls to speak since the last call, in order.</summary>
        public IReadOnlyList<BoutCallClip> TakeCalls()
        {
            var taken = _calls.ToArray();
            _calls.Clear();
            return taken;
        }

        /// <summary>The Paired Practice tallies that ended since the last call (non-empty ones), to save.</summary>
        public IReadOnlyList<EndedTally> TakeEndedTallies()
        {
            var taken = _endedTallies.ToArray();
            _endedTallies.Clear();
            return taken;
        }

        /// <summary>True once after a light that touched this Fencer: buzz both controllers.</summary>
        public bool TakeBeingTouched()
        {
            var isTouched = _isTouched;
            _isTouched = false;
            return isTouched;
        }

        /// <summary>
        /// Forgets the partner's connection (they dropped or left, or the session ended), so a rejoin starts clean: the
        /// clock, the rules and flow heard, the box, the calls. The Paired Practice tally is what differs (PER-84): with
        /// <paramref name="mayRejoin"/> it is saved so far (<see cref="PairedPracticeEnd.Interrupted"/>) and kept, so a
        /// rejoin carries it on; without, it ends (<see cref="PairedPracticeEnd.Disconnected"/>) and is reset. On the Host
        /// the mode stays as it was.
        /// </summary>
        public void LosePartner(bool mayRejoin, double nowSeconds)
        {
            var mode = FlowState.Mode;
            if (mayRejoin)
            {
                SnapshotTally();
            }
            else
            {
                EndSession();
            }

            Clock.Reset();
            Judge?.Reset();
            if (Judge != null)
            {
                Flow = new BoutFlow(Judge);
                Flow.SetMode(mode, nowSeconds);
            }

            Box.Clear();
            Announcer.Reset();
            _outgoing.Clear();
            _calls.Clear();
            _hostRules = null;
            _receivedFlow = BoutFlowState.Initial;
            _hasHostFlow = false;
            _partnerStance = null;
            _partnerStanceAtSeconds = double.NegativeInfinity;
            _nextPingSeconds = double.NegativeInfinity;
            _nextStanceSeconds = double.NegativeInfinity;
            _rulesSentAt = double.NegativeInfinity;
            _flowSentAt = double.NegativeInfinity;
            _areRulesChanged = true;
            _isFlowChanged = true;
            _isTouched = false;
        }

        /// <summary>
        /// Saves the Paired Practice tally so far without ending it (<see cref="PairedPracticeEnd.Interrupted"/>): the
        /// connection dropped, or the headset is going to sleep and the app may never wake.
        /// </summary>
        public void SnapshotTally()
        {
            AddEndedTally(PairedPracticeEnd.Interrupted, Tally.For(Me), Tally.For(ScoringBox.Other(Me)));
        }

        /// <summary>
        /// The session is over for good (left, ended, or the Partner did not rejoin in time): a tally still held ends
        /// (<see cref="PairedPracticeEnd.Disconnected"/>, saved) and is reset.
        /// </summary>
        public void EndSession()
        {
            EndTally(PairedPracticeEnd.Disconnected);
        }

        private void SendRulesIfDue(double nowSeconds)
        {
            if (!_areRulesChanged && nowSeconds - _rulesSentAt < RulesResendSeconds)
            {
                return;
            }

            if (_areRulesChanged)
            {
                _log.Add($"{LogTag} Bout rules: Touch pressure {_myRules.Pressure}, double window {FormatMilliseconds(_myRules.DoubleWindowSeconds)}.");
            }

            _outgoing.Add(new BoutOutgoing(BoutMessages.EncodeRules(_myRules), true));
            _rulesSentAt = nowSeconds;
            _areRulesChanged = false;
        }

        private void SendFlowIfDue(double nowSeconds)
        {
            if (!_isFlowChanged && nowSeconds - _flowSentAt < RulesResendSeconds)
            {
                return;
            }

            _outgoing.Add(new BoutOutgoing(BoutMessages.EncodeFlow(Flow.State, Tally.Host, Tally.Partner), true));
            _flowSentAt = nowSeconds;
            _isFlowChanged = false;
        }

        private void SendStanceIfDue(double nowSeconds)
        {
            if (!_myStance.HasValue || nowSeconds < _nextStanceSeconds)
            {
                return;
            }

            _outgoing.Add(new BoutOutgoing(BoutMessages.EncodeStance(_myStance.Value), false));
            _nextStanceSeconds = nowSeconds + StanceIntervalSeconds;
        }

        private void StepCalls(double callStartLocalSeconds, double nowSeconds)
        {
            if (!HasHostFlow)
            {
                return;
            }

            _calls.AddRange(Announcer.Step(FlowState, callStartLocalSeconds, nowSeconds));
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

        /// <summary>
        /// The Host's flow and tally. The call's start goes onto this clock with the synced offset, so "Allez!" sounds
        /// together on both headsets; before the clock is synced it starts on arrival.
        /// </summary>
        private bool ReceiveFlow(byte[] message, double nowSeconds)
        {
            var (state, hostTally, partnerTally) = BoutMessages.DecodeFlow(message);
            var previous = _receivedFlow;
            var wasHostFlow = _hasHostFlow;
            _receivedFlow = state;
            _hasHostFlow = true;
            if (!wasHostFlow || state.Sequence != previous.Sequence)
            {
                LogFlow(state);
            }

            var offset = Clock.OffsetSeconds;
            _callStartLocalSeconds = offset.HasValue ? state.CallStartHostSeconds - offset.Value : nowSeconds;

            // This headset saves its own view of a tally the Host ended: switched to a Bout, or reset there.
            var wasPairedPractice = wasHostFlow && previous.Mode == PartnerMode.PairedPractice;
            var isResetByHost = state.Mode == PartnerMode.PairedPractice && hostTally.IsEmpty && partnerTally.IsEmpty;
            if (wasPairedPractice && state.Mode == PartnerMode.Bout)
            {
                AddEndedTally(PairedPracticeEnd.SwitchedToBout, Tally.For(Me), Tally.For(ScoringBox.Other(Me)));
            }
            else if (wasPairedPractice && isResetByHost)
            {
                AddEndedTally(PairedPracticeEnd.Reset, Tally.For(Me), Tally.For(ScoringBox.Other(Me)));
            }

            Tally.Replace(hostTally, partnerTally);
            return true;
        }

        private void Show(BoutLight light, double nowSeconds)
        {
            _isTouched |= Box.Show(light, nowSeconds);
            _log.Add($"{LogTag} Light {light.Sequence}: {ScoringBox.Describe(light, Me)}.");
        }

        /// <summary>
        /// Saves this headset's view of the tally (when not empty) and zeroes it: on the Host the zeros go out with the
        /// flow; on the Partner they show until the Host's reply confirms them.
        /// </summary>
        private void EndTally(PairedPracticeEnd end)
        {
            AddEndedTally(end, Tally.For(Me), Tally.For(ScoringBox.Other(Me)));
            Tally.Reset();
            _isFlowChanged = true;
        }

        private void AddEndedTally(PairedPracticeEnd end, PairedPracticeCounts mine, PairedPracticeCounts opponents)
        {
            if (!mine.IsEmpty || !opponents.IsEmpty)
            {
                _endedTallies.Add(new EndedTally(end, mine, opponents));
            }
        }

        private void LogFlow(BoutFlowState state)
        {
            switch (state.Phase)
            {
                case BoutPhase.Halted:
                    _log.Add($"{LogTag} Halte! {BoutCall.FormatHaltReason(state, Me)}.");
                    break;
                case BoutPhase.Calling:
                    _log.Add($"{LogTag} Both behind their En Garde Lines: En garde! Prets? Allez!");
                    break;
                case BoutPhase.Fencing:
                    _log.Add($"{LogTag} Allez: the phrase is on.");
                    break;
                case BoutPhase.WaitingForEnGarde:
                    _log.Add($"{LogTag} Waiting for both Fencers behind their En Garde Lines.");
                    break;
                default:
                    _log.Add($"{LogTag} Paired Practice: lights with no lock-out, no halts.");
                    break;
            }
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
