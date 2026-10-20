using System;
using System.IO;
using System.Numerics;
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
    /// stays on the drawn hilt. The Unity layer only moves bytes and draws.
    /// </summary>
    public sealed class OpponentLink
    {
        private readonly RemoteClock _clock = new RemoteClock();
        private readonly HandStream _hand = new HandStream();
        private readonly BodyStream _body = new BodyStream();
        private StripExtent _myStrip = StripExtent.Unplaced;
        private FencerInfo? _partner;

        public OpponentLink()
        {
        }

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
                        return true;
                    case PartnerMessages.BodyKind:
                        var body = PartnerMessages.DecodeBody(message);
                        _clock.Observe(body.SenderTimeSeconds, localSeconds);
                        _body.Add(body);
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
            _partner = null;
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

            var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)Math.PI);
            return new OpponentView(
                _partner.Value,
                bout,
                body?.Transform(bout.ToMyStripFrame, turn),
                _body.IsStale(senderNow.Value),
                controller.HasValue ? bout.ToMyStripFrame(controller.Value) : (TrackedPose?)null,
                _hand.Newest);
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
            return new FencerBody(joints, body.HeadRotation, body.Fidelity);
        }
    }
}
