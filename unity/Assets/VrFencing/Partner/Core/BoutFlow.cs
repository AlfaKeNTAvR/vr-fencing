using System;
using System.Numerics;
using VrFencing.Dummy;

namespace VrFencing.Partner
{
    /// <summary>Where the bout flow stands (PER-83, decided in PER-56, PER-57, PER-58 and PER-72).</summary>
    public enum BoutPhase : byte
    {
        /// <summary>Paired Practice: no calls, no halts, no lock-out; the judge only lights.</summary>
        PairedPractice,

        /// <summary>A Bout between phrases: waiting until both Fencers stand behind their En Garde Lines.</summary>
        WaitingForEnGarde,

        /// <summary>"En garde! Prets? Allez!" is being called; the phrase opens on "Allez!".</summary>
        Calling,

        /// <summary>The phrase is on: Touches count, and leaving the Strip or corps-a-corps halts.</summary>
        Fencing,

        /// <summary>"Halte!": nothing counts; shown for <see cref="BoutFlow.HaltShownSeconds"/>, then back to waiting.</summary>
        Halted,
    }

    /// <summary>Why a Bout halted.</summary>
    public enum HaltReason : byte
    {
        None,

        /// <summary>The judge lit (a Touch or a double).</summary>
        Light,

        /// <summary>A Fencer left their own Strip (a side or the rear end).</summary>
        OffStrip,

        /// <summary>The two bodies came within <see cref="FencerStance.CorpsACorpsMeters"/>.</summary>
        CorpsACorps,
    }

    /// <summary>
    /// One Fencer's place for the bout flow, read by their own headset in their own Strip frame (with their own Strip and
    /// off-strip tolerance): behind their bout En Garde Line or not, and off their own Strip or not.
    /// </summary>
    public readonly struct FencerStance
    {
        /// <summary>
        /// How far past the En Garde Line the front foot may be and still count as behind it: tracking noise, and the toes
        /// reaching over the line in a normal stance.
        /// </summary>
        public const float EnGardeLineToleranceMeters = 0.1f;

        /// <summary>Corps-a-corps: the two bodies' centres (chest, else hips, else head) closer than this, across the floor (PER-57).</summary>
        public const float CorpsACorpsMeters = 0.5f;

        public FencerStance(bool isBehindLine, OffStripKind offStrip)
        {
            IsBehindLine = isBehindLine;
            OffStrip = offStrip;
        }

        /// <summary>The front foot (the head with no foot tracked) is behind this Fencer's bout En Garde Line.</summary>
        public bool IsBehindLine { get; }

        /// <summary>Off their own placed Strip, and which way.</summary>
        public OffStripKind OffStrip { get; }

        /// <summary>Ready for the next phrase: behind the line and on the Strip.</summary>
        public bool IsReady => IsBehindLine && OffStrip == OffStripKind.None;

        /// <summary>
        /// This Fencer's stance from their body in their own Strip frame. The front-most foot point (ankles and toes) must
        /// be behind <see cref="BoutStrip.MyEnGardeLineZ"/> (within <see cref="EnGardeLineToleranceMeters"/>); with no
        /// foot the head stands in; with neither, not behind. Off the Strip only with a placed Strip
        /// (<paramref name="hasPlacedStrip"/>): the PC Editor's replay never is.
        /// </summary>
        public static FencerStance Read(FencerBody body, Vector3? head, StripExtent ownStrip, BoutStrip bout, float offStripToleranceMeters, bool hasPlacedStrip)
        {
            if (bout == null)
            {
                throw new ArgumentNullException(nameof(bout));
            }

            var frontZ = FindFrontZ(body) ?? head?.Z;
            var isBehindLine = frontZ.HasValue && frontZ.Value <= bout.MyEnGardeLineZ + EnGardeLineToleranceMeters;
            var offStrip = hasPlacedStrip ? OwnStripCheck.FindOffStrip(body, head, ownStrip, offStripToleranceMeters) : OffStripKind.None;
            return new FencerStance(isBehindLine, offStrip);
        }

        /// <summary>
        /// How far apart the two bodies' centres are across the floor, both in this Fencer's Strip frame: the chest
        /// (shoulder centre), else the hips, else the head (this Fencer's headset for a body with no joints). Null when
        /// either has none.
        /// </summary>
        public static float? GetBodiesApartMeters(FencerBody mine, Vector3? myHead, FencerBody opponent)
        {
            var me = FindCentre(mine) ?? myHead;
            var them = FindCentre(opponent);
            if (!me.HasValue || !them.HasValue)
            {
                return null;
            }

            var across = new Vector2(me.Value.X - them.Value.X, me.Value.Z - them.Value.Z);
            return across.Length();
        }

        private static float? FindFrontZ(FencerBody body)
        {
            if (body == null)
            {
                return null;
            }

            float? front = null;
            foreach (var joint in new[] { SkeletonJoint.FrontToe, SkeletonJoint.FrontAnkle, SkeletonJoint.BackToe, SkeletonJoint.BackAnkle })
            {
                var point = body.Get(joint);
                if (point.HasValue && (!front.HasValue || point.Value.Z > front.Value))
                {
                    front = point.Value.Z;
                }
            }

            return front;
        }

        private static Vector3? FindCentre(FencerBody body)
        {
            return body?.Get(SkeletonJoint.ShoulderCenter) ?? body?.Get(SkeletonJoint.HipCenter) ?? body?.Get(SkeletonJoint.HeadCenter);
        }
    }

    /// <summary>
    /// The bout flow as the Host decided it, and what goes to the Partner (PER-83). <see cref="Sequence"/> counts every
    /// change of phase, so each headset plays a call once per change.
    /// </summary>
    public readonly struct BoutFlowState
    {
        public BoutFlowState(
            PartnerMode mode,
            BoutPhase phase,
            int sequence,
            HaltReason halt,
            BoutSide? haltedBy,
            OffStripKind haltOffStrip,
            LightKind? haltLight,
            double callStartHostSeconds,
            bool isHostReady,
            bool isPartnerReady)
        {
            Mode = mode;
            Phase = phase;
            Sequence = sequence;
            Halt = halt;
            HaltedBy = haltedBy;
            HaltOffStrip = haltOffStrip;
            HaltLight = haltLight;
            CallStartHostSeconds = callStartHostSeconds;
            IsHostReady = isHostReady;
            IsPartnerReady = isPartnerReady;
        }

        public PartnerMode Mode { get; }

        public BoutPhase Phase { get; }

        /// <summary>Counts up on every phase change (a new call, a new halt).</summary>
        public int Sequence { get; }

        /// <summary>Why the last halt came; <see cref="HaltReason.None"/> before the first.</summary>
        public HaltReason Halt { get; }

        /// <summary>The Fencer who left their Strip, for an off-Strip halt.</summary>
        public BoutSide? HaltedBy { get; }

        public OffStripKind HaltOffStrip { get; }

        /// <summary>What lit, for a halt on a light.</summary>
        public LightKind? HaltLight { get; }

        /// <summary>When "En garde!" started, on the Host's clock ("Allez!" comes <see cref="BoutCall.AllezAtSeconds"/> later).</summary>
        public double CallStartHostSeconds { get; }

        /// <summary>The Host stands behind their line on their Strip (as last read).</summary>
        public bool IsHostReady { get; }

        /// <summary>The Partner stands behind their line on their Strip (as last reported).</summary>
        public bool IsPartnerReady { get; }

        public bool IsReady(BoutSide side)
        {
            return side == BoutSide.Host ? IsHostReady : IsPartnerReady;
        }

        /// <summary>The state before anything is known: a Bout waiting for the En Garde Lines.</summary>
        public static BoutFlowState Initial => new BoutFlowState(PartnerMode.Bout, BoutPhase.WaitingForEnGarde, 0, HaltReason.None, null, OffStripKind.None, null, 0.0, false, false);
    }

    /// <summary>
    /// The bout flow (PER-83, decided in PER-56, PER-57, PER-58 and PER-72), run on the Host beside the judge, on the
    /// Host's clock. Plain C#: the Unity layer feeds it both Fencers' stances, how far apart their bodies are, and the
    /// judge's lights.
    /// <list type="bullet">
    /// <item>Bout: when both Fencers are behind their En Garde Lines and on their Strips for
    /// <see cref="EnGardeHoldSeconds"/>, "En garde! Prets? Allez!" is called and the phrase opens on "Allez!".</item>
    /// <item>A light, a Fencer leaving their own Strip (a side or the rear end), or corps-a-corps (bodies within
    /// <see cref="FencerStance.CorpsACorpsMeters"/>) halts: "Halte!" with the reason, nothing counts, and after
    /// <see cref="HaltShownSeconds"/> it waits for both to be back behind their lines.</item>
    /// <item>Paired Practice: no calls, no halts; the judge lights off-Strip Touches too and never locks out.</item>
    /// </list>
    /// </summary>
    public sealed class BoutFlow
    {
        /// <summary>Both Fencers stand behind their lines this long before the call (PER-58).</summary>
        public const double EnGardeHoldSeconds = 1.0;

        /// <summary>"Halte!" and its reason show this long (as long as the lamps) before the flow waits for the lines again.</summary>
        public const double HaltShownSeconds = BoutJudge.LampSeconds;

        private readonly BoutJudge _judge;
        private double? _readySince;
        private double _haltedAtSeconds;
        private bool _isHostReady;
        private bool _isPartnerReady;

        public BoutFlow(BoutJudge judge)
        {
            _judge = judge ?? throw new ArgumentNullException(nameof(judge));
            State = BoutFlowState.Initial;
            ApplyModeToJudge(PartnerMode.Bout, double.NegativeInfinity);
        }

        public BoutFlowState State { get; private set; }

        /// <summary>
        /// The Host switched the mode at <paramref name="nowSeconds"/>. A Bout starts waiting for the En Garde Lines with
        /// the phrase closed; Paired Practice opens the judge at once. Returns true when it changed.
        /// </summary>
        public bool SetMode(PartnerMode mode, double nowSeconds)
        {
            if (mode == State.Mode)
            {
                return false;
            }

            ApplyModeToJudge(mode, nowSeconds);
            _readySince = null;
            Enter(mode, mode == PartnerMode.Bout ? BoutPhase.WaitingForEnGarde : BoutPhase.PairedPractice);
            return true;
        }

        /// <summary>
        /// Advances to <paramref name="nowSeconds"/>. A null stance is unknown (not read yet, or the Partner's report is
        /// stale): never ready, never off. <paramref name="bodiesApartMeters"/> null: unknown, no corps-a-corps.
        /// </summary>
        public void Step(double nowSeconds, FencerStance? host, FencerStance? partner, float? bodiesApartMeters)
        {
            _isHostReady = host.HasValue && host.Value.IsReady;
            _isPartnerReady = partner.HasValue && partner.Value.IsReady;
            switch (State.Phase)
            {
                case BoutPhase.WaitingForEnGarde:
                    StepWaiting(nowSeconds);
                    break;
                case BoutPhase.Calling:
                    var allezAt = State.CallStartHostSeconds + BoutCall.AllezAtSeconds;
                    if (nowSeconds >= allezAt)
                    {
                        _judge.OpenPhrase(allezAt);
                        Enter(State.Mode, BoutPhase.Fencing);
                    }

                    break;
                case BoutPhase.Fencing:
                    StepFencing(nowSeconds, host, partner, bodiesApartMeters);
                    break;
                case BoutPhase.Halted:
                    if (nowSeconds - _haltedAtSeconds >= HaltShownSeconds)
                    {
                        _readySince = null;
                        Enter(State.Mode, BoutPhase.WaitingForEnGarde);
                    }

                    break;
            }

            RefreshReadiness();
        }

        /// <summary>The judge lit at <paramref name="nowSeconds"/>: in a Bout's phrase, that halts.</summary>
        public void OnLight(BoutLight light, double nowSeconds)
        {
            if (State.Mode == PartnerMode.Bout && State.Phase == BoutPhase.Fencing)
            {
                Halt(nowSeconds, HaltReason.Light, null, OffStripKind.None, light.Kind);
            }
        }

        private void StepWaiting(double nowSeconds)
        {
            if (!_isHostReady || !_isPartnerReady)
            {
                _readySince = null;
                return;
            }

            _readySince ??= nowSeconds;
            if (nowSeconds - _readySince.Value >= EnGardeHoldSeconds)
            {
                _readySince = null;
                State = new BoutFlowState(State.Mode, BoutPhase.Calling, State.Sequence + 1, State.Halt, State.HaltedBy, State.HaltOffStrip, State.HaltLight,
                    nowSeconds, _isHostReady, _isPartnerReady);
            }
        }

        private void StepFencing(double nowSeconds, FencerStance? host, FencerStance? partner, float? bodiesApartMeters)
        {
            if (host.HasValue && host.Value.OffStrip != OffStripKind.None)
            {
                Halt(nowSeconds, HaltReason.OffStrip, BoutSide.Host, host.Value.OffStrip, null);
            }
            else if (partner.HasValue && partner.Value.OffStrip != OffStripKind.None)
            {
                Halt(nowSeconds, HaltReason.OffStrip, BoutSide.Partner, partner.Value.OffStrip, null);
            }
            else if (bodiesApartMeters.HasValue && bodiesApartMeters.Value < FencerStance.CorpsACorpsMeters)
            {
                Halt(nowSeconds, HaltReason.CorpsACorps, null, OffStripKind.None, null);
            }
        }

        private void Halt(double nowSeconds, HaltReason reason, BoutSide? by, OffStripKind offStrip, LightKind? light)
        {
            // A light closed the phrase itself; any other halt closes it now (a Touch already made still lights).
            if (reason != HaltReason.Light)
            {
                _judge.ClosePhrase(nowSeconds);
            }

            _haltedAtSeconds = nowSeconds;
            State = new BoutFlowState(State.Mode, BoutPhase.Halted, State.Sequence + 1, reason, by, offStrip, light, State.CallStartHostSeconds, _isHostReady, _isPartnerReady);
        }

        private void Enter(PartnerMode mode, BoutPhase phase)
        {
            State = new BoutFlowState(mode, phase, State.Sequence + 1, State.Halt, State.HaltedBy, State.HaltOffStrip, State.HaltLight, State.CallStartHostSeconds,
                _isHostReady, _isPartnerReady);
        }

        private void RefreshReadiness()
        {
            if (State.IsHostReady == _isHostReady && State.IsPartnerReady == _isPartnerReady)
            {
                return;
            }

            State = new BoutFlowState(State.Mode, State.Phase, State.Sequence, State.Halt, State.HaltedBy, State.HaltOffStrip, State.HaltLight, State.CallStartHostSeconds,
                _isHostReady, _isPartnerReady);
        }

        private void ApplyModeToJudge(PartnerMode mode, double nowSeconds)
        {
            var isBout = mode == PartnerMode.Bout;
            _judge.KeepsPhraseClosedAfterLight = isBout;
            _judge.RefusesOffStrip = isBout;
            if (isBout)
            {
                _judge.ClosePhrase(nowSeconds);
            }
            else
            {
                _judge.OpenPhrase(nowSeconds);
            }
        }
    }
}
