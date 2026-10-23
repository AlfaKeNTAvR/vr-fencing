using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using VrFencing.Blade;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Partner
{
    /// <summary>What the Host's replay judge says about one Touch claim (PER-145).</summary>
    public enum ParryRuling
    {
        /// <summary>The replay has not reached the claim's pressed time yet.</summary>
        NotYet,

        /// <summary>The Touch stands: no blade contact kept the point off the body.</summary>
        Touch,

        /// <summary>The defender's blade met the attacker's just before the press and kept the point off the body.</summary>
        Parried,
    }

    /// <summary>Rules on whether a parry stopped a claimed Touch; the judge waits for it before lighting (PER-145).</summary>
    public interface IParryReferee
    {
        ParryRuling Rule(TouchClaim claim);
    }

    /// <summary>The partner's side of the Host's replay: their hand and body as they really moved, on their clock.</summary>
    public interface IPartnerReplaySource
    {
        /// <summary>The partner's clock when the newest hand sample was sent; null before the first.</summary>
        double? NewestHandSenderSeconds { get; }

        /// <summary>The partner's blade as their hand held it at <paramref name="senderSeconds"/>, in the Host's Strip frame.</summary>
        bool TryGetPartnerBladeAt(double senderSeconds, out HeldBlade blade);

        /// <summary>The partner's body at <paramref name="senderSeconds"/>, in the Host's Strip frame, with their height.</summary>
        (FencerBody Body, float HeightMeters)? GetPartnerBodyAt(double senderSeconds);
    }

    /// <summary>
    /// The Host's time-aligned replay judge (PER-145, decided there): whether a parry stopped a Touch is decided once, on
    /// both real hands as they moved at the same moment, so the answer favors neither the attacker's view (where the
    /// defender's blade came late) nor the defender's (where the attacker's came late).
    /// <list type="bullet">
    /// <item>The Host keeps its own sword hand and body for the last <see cref="HostHistorySeconds"/>
    /// (<see cref="AddHostFrame"/>); the partner's come from the received streams, put on the Host's clock by
    /// <see cref="ClockSync"/>'s offset.</item>
    /// <item>A second <see cref="BladeContactSolver"/>, its own side memory and all, runs in fixed
    /// <see cref="StepSeconds"/> steps on both blades from the hands (interpolated only, never predicted), trailing the
    /// newest partner sample (<see cref="Advance"/>). It never draws anything.</item>
    /// <item>A claim is ruled once the replay has passed its pressed time: <see cref="ParryRuling.Parried"/> when the two
    /// blades touched within <see cref="WatchSeconds"/> before the press, the attacker's point as the hand held it
    /// reached the defender's body in the <see cref="ReachWindowSeconds"/> up to the press, and the point as the contact
    /// left it (turned, bowed, slid off) did not. Otherwise the Touch stands: a beat, or a contact that did not keep the
    /// point off, never changes a verdict, and when the replay has no data for the moment the claim is not refused.</item>
    /// </list>
    /// Host's clock, Host's Strip frame, meters, seconds. Log tag <c>[Bout]</c>.
    /// </summary>
    public sealed class ParryReplay : IParryReferee
    {
        /// <summary>One replay step: the headset's frame.</summary>
        public const double StepSeconds = 1.0 / 72.0;

        /// <summary>The Host's own hand and body are kept this long: past the slowest claim the judge waits for.</summary>
        public const double HostHistorySeconds = 1.0;

        /// <summary>Replay steps kept for rulings: past the watch and the reach window with room.</summary>
        public const double KeptSeconds = 0.6;

        /// <summary>A contact this long before the press can have parried it (<see cref="ParryJudge.WatchSeconds"/>, as on the Dummy).</summary>
        public const double WatchSeconds = ParryJudge.WatchSeconds;

        /// <summary>The point's path is checked against the body over this long up to the press (and one step after it).</summary>
        public const double ReachWindowSeconds = 0.1;

        /// <summary>A point this close to a body capsule's surface reaches it: the two views' bodies differ by a little.</summary>
        public const float ReachMarginMeters = 0.03f;

        /// <summary>The replay starts this far behind the newest partner sample, so a claim right after a start is ruled.</summary>
        public const double StartBackSeconds = 0.3;

        /// <summary>A hole longer than this in either hand starts the replay over (no side remembered across it).</summary>
        public const double MaxGapSeconds = 0.1;

        /// <summary>At most this many steps per frame (a frame hitch); further behind, the replay jumps ahead and starts over.</summary>
        public const int MaxStepsPerAdvance = 24;

        private const string LogTag = "[Bout]";

        private readonly BladeContactSolver _solver = new BladeContactSolver();
        private readonly List<HostFrame> _host = new List<HostFrame>();
        private readonly List<ReplayStep> _steps = new List<ReplayStep>();
        private readonly Dictionary<(BoutSide, int), ParryRuling> _rulings = new Dictionary<(BoutSide, int), ParryRuling>();
        private readonly List<Capsule> _capsules = new List<Capsule>(FencerBodyShape.CapsuleCount);
        private readonly List<string> _log = new List<string>();
        private IPartnerReplaySource _partner;
        private double? _offsetSeconds;
        private double? _replaySeconds;
        private int _restartCount;

        /// <summary>The Host's clock the replay has reached; null before it starts.</summary>
        public double? ReplaySeconds => _replaySeconds;

        /// <summary>Times the replay started over (a gap in a hand, a hitch).</summary>
        public int RestartCount => _restartCount;

        /// <summary>The replay's own contact this step: for the log and the tests.</summary>
        public BladeContactSolver Contact => _solver;

        /// <summary>
        /// This Host frame at <paramref name="hostSeconds"/>: the sword-hand controller (null while not tracked) with
        /// the Host's grip, and the Host's body with its height (null while none).
        /// </summary>
        public void AddHostFrame(double hostSeconds, TrackedPose? controller, BladeSettings blade, Hand hand, FencerBody body, float heightMeters)
        {
            if (blade == null)
            {
                throw new ArgumentNullException(nameof(blade));
            }

            if (_host.Count > 0 && hostSeconds <= _host[_host.Count - 1].Seconds)
            {
                return;
            }

            _host.Add(new HostFrame(hostSeconds, controller, blade, hand, body, heightMeters));
            while (_host.Count > 2 && _host[0].Seconds < hostSeconds - HostHistorySeconds)
            {
                _host.RemoveAt(0);
            }
        }

        /// <summary>
        /// Steps the replay as far as both hands are known at <paramref name="hostNowSeconds"/>: up to the newest
        /// partner sample (on the Host's clock by <paramref name="partnerClockOffsetSeconds"/>, the partner's clock minus
        /// the Host's) and the newest Host frame. Nothing before the clock is synced.
        /// </summary>
        public void Advance(double hostNowSeconds, double? partnerClockOffsetSeconds, IPartnerReplaySource partner)
        {
            _partner = partner ?? throw new ArgumentNullException(nameof(partner));
            _offsetSeconds = partnerClockOffsetSeconds;
            var newestPartner = partner.NewestHandSenderSeconds;
            if (!partnerClockOffsetSeconds.HasValue || !newestPartner.HasValue || _host.Count == 0)
            {
                return;
            }

            var target = Math.Min(Math.Min(newestPartner.Value - partnerClockOffsetSeconds.Value, _host[_host.Count - 1].Seconds), hostNowSeconds);
            if (!_replaySeconds.HasValue || target - _replaySeconds.Value > MaxStepsPerAdvance * StepSeconds)
            {
                Restart(target - StartBackSeconds);
            }

            var steps = 0;
            while (_replaySeconds.Value + StepSeconds <= target && steps < MaxStepsPerAdvance)
            {
                StepAt(_replaySeconds.Value + StepSeconds);
                steps++;
            }
        }

        /// <summary>Forgets everything (the partner left, a new session).</summary>
        public void Reset()
        {
            _solver.Reset();
            _host.Clear();
            _steps.Clear();
            _rulings.Clear();
            _replaySeconds = null;
            _offsetSeconds = null;
            _partner = null;
        }

        /// <summary>The ruling on <paramref name="claim"/>; once given it stays the same.</summary>
        public ParryRuling Rule(TouchClaim claim)
        {
            var key = (claim.Attacker, claim.Sequence);
            if (_rulings.TryGetValue(key, out var known))
            {
                return known;
            }

            var press = claim.ContactHostSeconds;
            if (!_replaySeconds.HasValue)
            {
                // No replay at all (no partner hand, or the clock is not synced yet): nothing to wait for, the Touch stands.
                return ParryRuling.Touch;
            }

            if (_replaySeconds.Value < press + StepSeconds)
            {
                return ParryRuling.NotYet;
            }

            var ruling = Judge(claim, out var reason);
            _rulings[key] = ruling;
            var who = claim.Attacker == BoutSide.Host ? "the host" : "the partner";
            _log.Add($"{LogTag} Replay judge on Touch {claim.Sequence} by {who} ({claim.Zone}): {ruling}, {reason}.");
            return ruling;
        }

        /// <summary>The log lines since the last call, in order.</summary>
        public IReadOnlyList<string> TakeLog()
        {
            var taken = _log.ToArray();
            _log.Clear();
            return taken;
        }

        private ParryRuling Judge(TouchClaim claim, out string reason)
        {
            var press = claim.ContactHostSeconds;
            var first = _steps.Count > 0 ? _steps[0].Seconds : double.PositiveInfinity;
            if (first > press - ReachWindowSeconds)
            {
                reason = "no replay of that moment (the Touch stands)";
                return ParryRuling.Touch;
            }

            var lastContact = double.NegativeInfinity;
            foreach (var step in _steps)
            {
                if (step.IsTouching && step.Seconds >= press - WatchSeconds && step.Seconds <= press)
                {
                    lastContact = step.Seconds;
                }
            }

            if (double.IsNegativeInfinity(lastContact))
            {
                reason = $"no blade contact in the {Milliseconds(WatchSeconds)} before the press";
                return ParryRuling.Touch;
            }

            if (!TryGetDefenderBody(claim.Attacker, press))
            {
                reason = "the defender's body is not known (the Touch stands)";
                return ParryRuling.Touch;
            }

            var isHost = claim.Attacker == BoutSide.Host;
            var handReach = FindReach(press, isHost, isSolved: false);
            var solvedReach = FindReach(press, isHost, isSolved: true);
            var contactAgo = Milliseconds(press - lastContact);
            if (handReach <= ReachMarginMeters && solvedReach > ReachMarginMeters)
            {
                reason = $"blades met {contactAgo} before the press and kept the point {Centimeters(solvedReach)} off the body (the hand's point reached it)";
                return ParryRuling.Parried;
            }

            reason = solvedReach <= ReachMarginMeters
                ? $"blades met {contactAgo} before the press but the point still reached the body"
                : $"blades met {contactAgo} before the press; the hand's point missed the body by {Centimeters(handReach)} too";
            return ParryRuling.Touch;
        }

        /// <summary>Fills the capsules with the defender's body at the press; false when it is not known.</summary>
        private bool TryGetDefenderBody(BoutSide attacker, double pressHostSeconds)
        {
            if (attacker == BoutSide.Partner)
            {
                var frame = FindHostFrame(pressHostSeconds);
                if (!frame.HasValue || frame.Value.Body == null)
                {
                    return false;
                }

                FencerBodyShape.GetCapsules(frame.Value.Body, frame.Value.HeightMeters, _capsules);
                return true;
            }

            var partnerBody = _offsetSeconds.HasValue ? _partner?.GetPartnerBodyAt(pressHostSeconds + _offsetSeconds.Value) : null;
            if (!partnerBody.HasValue)
            {
                return false;
            }

            FencerBodyShape.GetCapsules(partnerBody.Value.Body, partnerBody.Value.HeightMeters, _capsules);
            return true;
        }

        /// <summary>
        /// How far the attacker's point (as the hand held it, or as the contact left it) stayed from the body's surface at
        /// its closest, swept between steps over the reach window; below zero inside it.
        /// </summary>
        private float FindReach(double pressHostSeconds, bool isHostAttacking, bool isSolved)
        {
            var closest = float.MaxValue;
            Vector3? before = null;
            foreach (var step in _steps)
            {
                if (step.Seconds < pressHostSeconds - ReachWindowSeconds || step.Seconds > pressHostSeconds + StepSeconds + 1e-6)
                {
                    continue;
                }

                var point = isHostAttacking
                    ? (isSolved ? step.HostSolvedTip : step.HostHandTip)
                    : (isSolved ? step.PartnerSolvedTip : step.PartnerHandTip);
                var from = before ?? point;
                foreach (var capsule in _capsules)
                {
                    if (FencerBodyShape.IsParked(capsule))
                    {
                        continue;
                    }

                    var distance = SegmentMath.FindDistanceBetweenSegments(from, point, capsule.Start, capsule.End, out _) - capsule.RadiusMeters;
                    closest = Math.Min(closest, distance);
                }

                before = point;
            }

            return closest;
        }

        private void Restart(double fromHostSeconds)
        {
            _solver.Reset();
            _replaySeconds = fromHostSeconds;
            _restartCount++;
        }

        /// <summary>One replay step at <paramref name="hostSeconds"/>; a hole in either hand starts the contact over.</summary>
        private void StepAt(double hostSeconds)
        {
            _replaySeconds = hostSeconds;
            var host = FindHostBlade(hostSeconds);
            if (!host.HasValue || !_partner.TryGetPartnerBladeAt(hostSeconds + _offsetSeconds.Value, out var partner))
            {
                _solver.Reset();
                return;
            }

            _solver.Solve(host.Value.ToHold(), partner.ToHold(), (float)StepSeconds);
            var isTouching = _solver.IsInContact || _solver.IsReleasing || _solver.IsBOnHiltA || _solver.IsAOnHiltB;
            _steps.Add(new ReplayStep(
                hostSeconds,
                isTouching,
                host.Value.Chord.TipPosition,
                _solver.GiveA.Chord.TipPosition,
                partner.Chord.TipPosition,
                _solver.GiveB.Chord.TipPosition));
            while (_steps.Count > 0 && _steps[0].Seconds < hostSeconds - KeptSeconds)
            {
                _steps.RemoveAt(0);
            }
        }

        /// <summary>The Host's blade at <paramref name="hostSeconds"/>, its controller interpolated between the frames around it.</summary>
        private HeldBlade? FindHostBlade(double hostSeconds)
        {
            for (var index = _host.Count - 1; index > 0; index--)
            {
                var after = _host[index];
                var before = _host[index - 1];
                if (before.Seconds > hostSeconds)
                {
                    continue;
                }

                if (after.Seconds < hostSeconds || after.Seconds - before.Seconds > MaxGapSeconds
                    || !before.Controller.HasValue || !after.Controller.HasValue)
                {
                    return null;
                }

                var fraction = (float)((hostSeconds - before.Seconds) / (after.Seconds - before.Seconds));
                var controller = new TrackedPose(
                    Vector3.Lerp(before.Controller.Value.Position, after.Controller.Value.Position, fraction),
                    Quaternion.Slerp(before.Controller.Value.Rotation, after.Controller.Value.Rotation, fraction));
                return HeldBlade.FromController(controller, after.Blade, after.Hand);
            }

            return null;
        }

        /// <summary>The Host frame nearest <paramref name="hostSeconds"/>, or null with none.</summary>
        private HostFrame? FindHostFrame(double hostSeconds)
        {
            HostFrame? nearest = null;
            foreach (var frame in _host)
            {
                if (!nearest.HasValue || Math.Abs(frame.Seconds - hostSeconds) < Math.Abs(nearest.Value.Seconds - hostSeconds))
                {
                    nearest = frame;
                }
            }

            return nearest;
        }

        private static string Milliseconds(double seconds)
        {
            return Math.Round(seconds * 1000.0).ToString("0", CultureInfo.InvariantCulture) + " ms";
        }

        private static string Centimeters(float meters)
        {
            return (meters * 100.0f).ToString("0.0", CultureInfo.InvariantCulture) + " cm";
        }

        private readonly struct HostFrame
        {
            public HostFrame(double seconds, TrackedPose? controller, BladeSettings blade, Hand hand, FencerBody body, float heightMeters)
            {
                Seconds = seconds;
                Controller = controller;
                Blade = blade;
                Hand = hand;
                Body = body;
                HeightMeters = heightMeters;
            }

            public double Seconds { get; }

            public TrackedPose? Controller { get; }

            public BladeSettings Blade { get; }

            public Hand Hand { get; }

            public FencerBody Body { get; }

            public float HeightMeters { get; }
        }

        private readonly struct ReplayStep
        {
            public ReplayStep(double seconds, bool isTouching, Vector3 hostHandTip, Vector3 hostSolvedTip, Vector3 partnerHandTip, Vector3 partnerSolvedTip)
            {
                Seconds = seconds;
                IsTouching = isTouching;
                HostHandTip = hostHandTip;
                HostSolvedTip = hostSolvedTip;
                PartnerHandTip = partnerHandTip;
                PartnerSolvedTip = partnerSolvedTip;
            }

            public double Seconds { get; }

            /// <summary>The blades or a blade and a hilt pressed, or a blade was sliding off the other.</summary>
            public bool IsTouching { get; }

            public Vector3 HostHandTip { get; }

            public Vector3 HostSolvedTip { get; }

            public Vector3 PartnerHandTip { get; }

            public Vector3 PartnerSolvedTip { get; }
        }
    }
}
