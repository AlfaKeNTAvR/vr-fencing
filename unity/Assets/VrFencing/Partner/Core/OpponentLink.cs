using System;
using System.IO;
using System.Numerics;
using VrFencing.Blade;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Partner
{
    /// <summary>The opponent as drawn this frame, in this Fencer's Strip frame (PER-81).</summary>
    public sealed class OpponentView
    {
        public OpponentView(FencerInfo info, BoutStrip bout, FencerBody body, bool isBodyStale, TrackedPose? controller, HandSample? grip)
        {
            Info = info;
            Bout = bout;
            Body = body;
            IsBodyStale = isBodyStale;
            Controller = controller;
            Grip = grip;
        }

        /// <summary>The partner's height and sword hand, for their rigged body.</summary>
        public FencerInfo Info { get; }

        public BoutStrip Bout { get; }

        /// <summary>The body to pose the rig on, or null before the first body message.</summary>
        public FencerBody Body { get; }

        /// <summary>The body stopped coming: the rig holds its last pose, faded.</summary>
        public bool IsBodyStale { get; }

        /// <summary>The sword-hand controller anchor to build the blade from, or null when the hand is lost.</summary>
        public TrackedPose? Controller { get; }

        /// <summary>The newest hand sample, for its grip settings (grip, angles, set, flex).</summary>
        public HandSample? Grip { get; }
    }

    /// <summary>
    /// The receiving end of the partner streams (PER-81): takes every message from the partner, keeps their clock, their
    /// Strip, height and hand, and their hand and body samples (in their own Strip frame), and says what to draw each
    /// frame in this Fencer's Strip frame through the bout Strip's 180-degree turn. The body's sword hand is lined up with
    /// the blade's hand, which is drawn later in the partner's time (it is predicted, the body is not), so the drawn hand
    /// stays on the drawn hilt. The body's legs are built on the partner's footwork (<see cref="RemoteFencerLegs"/>,
    /// PER-112) instead of their generated ones. The Unity layer only moves bytes and draws.
    /// </summary>
    public sealed class OpponentLink : IPartnerReplaySource
    {
        private readonly RemoteClock _clock = new RemoteClock();
        private readonly HandStream _hand = new HandStream();
        private readonly BodyStream _body = new BodyStream();
        private readonly RemoteFencerLegs _legs = new RemoteFencerLegs();
        private StripExtent _myStrip = StripExtent.Unplaced;
        private FencerInfo? _partner;
        private double? _lastArrivalSeconds;

        public OpponentLink()
        {
        }

        /// <summary>
        /// No stream message (hand or body) for this long is a lost signal (PER-84, decided in PER-73): the opponent holds
        /// its last pose, faded (<see cref="BodyStream.StaleSeconds"/>), and the connection icon shows.
        /// </summary>
        public const double SignalLostSeconds = 0.5;

        /// <summary>The partner's last info message, or null before the first.</summary>
        public FencerInfo? Partner => _partner;

        /// <summary>The bout Strip, once the partner's Strip is known.</summary>
        public BoutStrip Bout => _partner.HasValue ? BoutStrip.Create(_myStrip, _partner.Value.Strip) : null;

        /// <summary>Messages dropped as unreadable since the start (another format, cut short, garbage).</summary>
        public int RejectedMessages { get; private set; }

        /// <summary>This Fencer's own placed Strip (or <see cref="StripExtent.Unplaced"/>), for the bout Strip.</summary>
        public void SetMyStrip(StripExtent strip)
        {
            _myStrip = strip;
        }

        /// <summary>
        /// Takes one message from the partner, received at <paramref name="localSeconds"/> on this app's clock. Returns
        /// false (and counts it) when it cannot be read; nothing changes then.
        /// </summary>
        public bool Receive(byte[] message, double localSeconds)
        {
            try
            {
                switch (PartnerMessages.ReadKind(message))
                {
                    case PartnerMessages.InfoKind:
                        _partner = PartnerMessages.DecodeInfo(message);
                        return true;
                    case PartnerMessages.HandKind:
                        var hand = PartnerMessages.DecodeHand(message);
                        _clock.Observe(hand.SenderTimeSeconds, localSeconds);
                        _hand.Add(hand);
                        _lastArrivalSeconds = localSeconds;
                        return true;
                    case PartnerMessages.BodyKind:
                        var body = PartnerMessages.DecodeBody(message);
                        _clock.Observe(body.SenderTimeSeconds, localSeconds);
                        _body.Add(body);
                        _lastArrivalSeconds = localSeconds;
                        return true;
                    default:
                        RejectedMessages++;
                        return false;
                }
            }
            catch (InvalidDataException)
            {
                RejectedMessages++;
                return false;
            }
        }

        /// <summary>Forgets the partner (they left, or the session ended), so a rejoin starts clean.</summary>
        public void Clear()
        {
            _clock.Reset();
            _hand.Clear();
            _body.Clear();
            _legs.Reset();
            _partner = null;
            _lastArrivalSeconds = null;
        }

        /// <summary>
        /// Seconds since the last hand or body message at <paramref name="localSeconds"/>, or null before the first one.
        /// </summary>
        public double? GetSilenceSeconds(double localSeconds)
        {
            return _lastArrivalSeconds.HasValue ? localSeconds - _lastArrivalSeconds.Value : (double?)null;
        }

        /// <summary>The streams went silent for <see cref="SignalLostSeconds"/> after having come: show the connection icon.</summary>
        public bool IsSignalLost(double localSeconds)
        {
            var silence = GetSilenceSeconds(localSeconds);
            return silence.HasValue && silence.Value >= SignalLostSeconds;
        }

        /// <summary>
        /// What to draw at <paramref name="localSeconds"/>; null until the partner's info and at least one stream message
        /// have come.
        /// </summary>
        public OpponentView Sample(double localSeconds)
        {
            var bout = Bout;
            var senderNow = _clock.ToSenderTime(localSeconds);
            if (bout == null || !senderNow.HasValue)
            {
                return null;
            }

            TrackedPose? controller = null;
            if (_hand.TrySample(senderNow.Value, out var handPose))
            {
                controller = handPose;
            }

            var body = _body.Sample(senderNow.Value);
            if (body != null && controller.HasValue && _hand.TryInterpolate(senderNow.Value - BodyStream.BufferSeconds, out var handAtBodyTime))
            {
                body = MoveSwordHandOntoBlade(body, handAtBodyTime, controller.Value);
            }

            if (body != null)
            {
                body = _legs.Apply(body, _partner.Value.HeightMeters, _partner.Value.Hand, localSeconds);
            }

            var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)Math.PI);
            return new OpponentView(
                _partner.Value,
                bout,
                body?.Transform(bout.ToMyStripFrame, turn),
                _body.IsStale(senderNow.Value),
                controller.HasValue ? bout.ToMyStripFrame(controller.Value) : (TrackedPose?)null,
                _hand.Newest);
        }

        /// <summary>The partner's clock when the newest hand sample was sent; null before the first.</summary>
        public double? NewestHandSenderSeconds => _hand.Newest?.SenderTimeSeconds;

        /// <summary>
        /// The partner's newest report of their own view of the blades' contact (PER-145), its side turned into this
        /// Fencer's Strip frame (from this Fencer's blade toward the partner's); null before one came, or when the hand
        /// is lost.
        /// </summary>
        public BladeContactReport? GetPartnerContactReport(double localSeconds)
        {
            var bout = Bout;
            var senderNow = _clock.ToSenderTime(localSeconds);
            var newest = _hand.Newest;
            if (bout == null || !senderNow.HasValue || !newest.HasValue || !newest.Value.Contact.HasValue
                || senderNow.Value - newest.Value.SenderTimeSeconds > HandStream.StaleSeconds)
            {
                return null;
            }

            var report = newest.Value.Contact.Value;
            return new BladeContactReport(
                report.IsInContact, report.PressMeters, bout.DirectionToMyStripFrame(report.Side), report.IsSenderSliding, report.IsReceiverSliding);
        }

        /// <summary>
        /// The partner's blade as their hand held it at <paramref name="senderSeconds"/> on their clock (interpolated
        /// only, never predicted), in this Fencer's Strip frame, with their grip: what the Host's replay judge solves
        /// (PER-145). False before the bout Strip is known, or when that moment is not inside the samples kept (older
        /// than the oldest, or not arrived yet).
        /// </summary>
        public bool TryGetPartnerBladeAt(double senderSeconds, out HeldBlade blade)
        {
            blade = default;
            var bout = Bout;
            var oldest = _hand.Oldest;
            var newest = _hand.Newest;
            if (bout == null || !oldest.HasValue || !newest.HasValue
                || senderSeconds < oldest.Value.SenderTimeSeconds || senderSeconds > newest.Value.SenderTimeSeconds
                || !_hand.TryInterpolate(senderSeconds, out var controller))
            {
                return false;
            }

            var grip = newest.Value;
            blade = HeldBlade.FromController(bout.ToMyStripFrame(controller), grip.ToBladeSettings(), grip.Hand);
            return true;
        }

        /// <summary>
        /// The partner's body as sent for <paramref name="senderSeconds"/> on their clock (interpolated only, their
        /// tracked legs), in this Fencer's Strip frame, with their height: the Host's replay judge's target (PER-145).
        /// Null before the bout Strip and a body are known.
        /// </summary>
        public (FencerBody Body, float HeightMeters)? GetPartnerBodyAt(double senderSeconds)
        {
            var bout = Bout;
            var body = _body.SampleAt(senderSeconds);
            if (bout == null || body == null)
            {
                return null;
            }

            var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)Math.PI);
            return (body.Transform(bout.ToMyStripFrame, turn), _partner.Value.HeightMeters);
        }

        /// <summary>
        /// The body with its sword wrist and hand carried from where the controller was when the body was read
        /// (<paramref name="handAtBodyTime"/>) to where it is drawn (<paramref name="drawnHand"/>), keeping their place in
        /// the hand's frame.
        /// </summary>
        public static FencerBody MoveSwordHandOntoBlade(FencerBody body, TrackedPose handAtBodyTime, TrackedPose drawnHand)
        {
            var wrist = body.Get(SkeletonJoint.SwordWrist);
            var handEnd = body.Get(SkeletonJoint.SwordHandEnd);
            if (!wrist.HasValue || !handEnd.HasValue)
            {
                return body;
            }

            var fromBodyTime = Quaternion.Inverse(handAtBodyTime.Rotation);
            Vector3 Carry(Vector3 point) =>
                drawnHand.Position + Vector3.Transform(Vector3.Transform(point - handAtBodyTime.Position, fromBodyTime), drawnHand.Rotation);

            var joints = new Vector3?[FencerBody.JointCount];
            for (var index = 0; index < FencerBody.JointCount; index++)
            {
                joints[index] = body.Get(index);
            }

            joints[(int)SkeletonJoint.SwordWrist] = Carry(wrist.Value);
            joints[(int)SkeletonJoint.SwordHandEnd] = Carry(handEnd.Value);
            return body.WithJoints(joints);
        }
    }
}
