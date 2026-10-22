using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using VrFencing.Settings;

namespace VrFencing.Partner
{
    /// <summary>
    /// The two Fencers' poses over the Partner session (PER-81, decided in PER-59 and PER-57): while connected, this
    /// Fencer's body goes out 30 times a second and the sword hand with its velocity and grip settings 72 times a
    /// second, both unreliable (a late pose is worth nothing), in their own Strip frame, true and unmirrored; their
    /// Strip, height and hand go reliably on every change and once a second. What the partner sends goes into
    /// <see cref="Link"/> (plain C#), which keeps their clock and streams and turns them into this Fencer's Strip frame
    /// through the bout Strip. The opponent view draws from it.
    /// <para>
    /// Netcode named messages carry the bytes (<see cref="MessageName"/>); no network objects or prefabs are spawned.
    /// The body is the Dummy rig's joints (<see cref="FencerBody"/>, about 4 KB/s), not Movement SDK's
    /// <c>NetworkCharacterRetargeter</c> stream: the rig is posed on joints (PER-78), and this keeps the format in the
    /// Core where it is tested.
    /// </para>
    /// <para>
    /// "Mirror me" (PER-140): with no partner, the same messages go into a <see cref="MirrorLoopback"/> instead of the
    /// network and come back after the chosen delay into <see cref="Link"/>, so this Fencer sees their own body and blade
    /// exactly as a partner's headset would draw them. Only the transport differs; the packing, unpacking, clock,
    /// streams, legs and the Strip's turn are the partner path's own.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(PartnerSessionBehaviour))]
    // After the camera rig and body tracking have moved this frame (body tracking runs at -50).
    [DefaultExecutionOrder(100)]
    public sealed class OpponentLinkBehaviour : MonoBehaviour
    {
        /// <summary>The Netcode named message every partner stream goes through; the first byte says which stream.</summary>
        public const string MessageName = "VrFencing.Opponent";

        /// <summary>Body samples per second (PER-59: 30, overriding the Movement SDK's 12).</summary>
        public const double BodyRatePerSecond = 30.0;

        /// <summary>Hand samples per second (PER-59: 60 to 72); the Quest renders at 72.</summary>
        public const double HandRatePerSecond = 72.0;

        /// <summary>The info goes again this often even unchanged, so a partner who missed it (a rejoin) gets it soon.</summary>
        public const double InfoResendSeconds = 1.0;

        private const string LogTag = "[Opponent]";

        // Room for the largest message with its length prefix.
        private const int WriterCapacityBytes = 512;

        private readonly OpponentLink _link = new OpponentLink();
        private readonly LocalFencerReader _reader = new LocalFencerReader();
        private readonly SendSchedule _bodySchedule = new SendSchedule(BodyRatePerSecond);
        private readonly SendSchedule _handSchedule = new SendSchedule(HandRatePerSecond);
        private readonly HandMotion _handMotion = new HandMotion();
        private readonly FootworkLookAhead _footwork = new FootworkLookAhead();
        private readonly MirrorLoopback _loopback = new MirrorLoopback(MirrorDelay.DefaultSeconds);

        private PartnerSessionBehaviour _partner;
        private NetworkManager _network;
        private CustomMessagingManager _registeredWith;
        private FencerInfo? _sentInfo;
        private double _infoSentAt = double.NegativeInfinity;
        private StreamRoute _route = StreamRoute.None;
        private bool _hasLoggedFirstPose;
        private int _loggedRejectedMessages;
        private double _holdSendingUntilSeconds = double.NegativeInfinity;

        /// <summary>The partner's streams, turned into this Fencer's Strip frame.</summary>
        public OpponentLink Link => _link;

        /// <summary>This Fencer this frame, as sent (null when there is nothing to read).</summary>
        public LocalFencerFrame Local { get; private set; }

        /// <summary>Connected to the partner: the streams run.</summary>
        public bool IsConnected => _partner != null && _partner.State.Phase == PartnerPhase.Connected;

        /// <summary>"Mirror me" (PER-140): the streams run through the loopback, back to this app.</summary>
        public bool IsMirroring => _partner != null && _partner.State.IsMirroring;

        /// <summary>The streams run (to a partner or mirrored): <see cref="Link"/> has an opponent to draw.</summary>
        public bool IsStreaming => _route != StreamRoute.None;

        /// <summary>
        /// This Fencer's footwork (PER-112), sent with each body: the feet <see cref="FootworkLookAhead.Seconds"/> before
        /// this frame, read with the hips since (PER-142).
        /// </summary>
        public FootworkState SentFootwork => _footwork.State;

        /// <summary>True when a Fencer replay in the scene is what goes out (the PC Editor as the partner).</summary>
        public bool IsSendingReplay => _reader.IsReplay;

        /// <summary>Takes changed Settings: height, hand and the blade's grip go out with the next messages.</summary>
        public void ApplySettings(TrainerSettings settings)
        {
            _reader.ApplySettings(settings.Clone());
        }

        /// <summary>
        /// PC Editor only: sends no body or hand for <paramref name="seconds"/>, as a lag spike would, so the other app's
        /// connection icon and faded last pose can be seen (PER-84). The connection itself stays up.
        /// </summary>
        public void HoldSending(double seconds)
        {
            _holdSendingUntilSeconds = Time.realtimeSinceStartupAsDouble + seconds;
            Debug.LogWarning($"{LogTag} Simulating a lag spike: nothing sent for {seconds:0.0} s.");
        }

        private void Awake()
        {
            _partner = GetComponent<PartnerSessionBehaviour>();
            _network = GetComponent<NetworkManager>();
        }

        private void Start()
        {
            _reader.FindSources(SettingsFile.Load());
        }

        private void OnDestroy()
        {
            Unregister();
        }

        private void LateUpdate()
        {
            var now = Time.realtimeSinceStartupAsDouble;
            Local = _reader.Read(now);
            if (Local != null)
            {
                _link.SetMyStrip(Local.Strip);

                // Read every frame, at full rate, so a lunge is read on time; sent with the body.
                _footwork.Update(now, Local.Head?.Position, Local.Body.Get(VrFencing.Dummy.SkeletonJoint.HipCenter), Local.HeightMeters, Local.Hand);
            }

            var route = ChooseRoute();
            if (route != _route)
            {
                OnRouteChanged(route);
            }

            if (route == StreamRoute.None)
            {
                return;
            }

            if (route == StreamRoute.Network && _registeredWith != _network.CustomMessagingManager)
            {
                Register();
            }

            if (route == StreamRoute.Mirror && _loopback.SetDelay(_partner.State.MirrorDelaySeconds))
            {
                // A new delay is a new link: the receiver's clock and streams start over (the opponent blinks out for it).
                Debug.Log($"{LogTag} Mirror delay now {_loopback.DelaySeconds:0.0} s; the mirrored link starts over.");
                ResetStreams();
            }

            LogRejectedMessages();
            if (Local != null && now >= _holdSendingUntilSeconds)
            {
                SendInfoIfDue(now);
                SendHandIfDue(now);
                if (_bodySchedule.TakeIfDue(now))
                {
                    Send(PartnerMessages.EncodeBody(new BodySample(now, Local.Body.WithFootwork(_footwork.State))), NetworkDelivery.UnreliableSequenced, now);
                }
            }

            if (route == StreamRoute.Mirror)
            {
                foreach (var message in _loopback.TakeDue(now))
                {
                    Receive(message, now);
                }
            }
        }

        /// <summary>Where the streams go this frame: to the connected partner, round the mirror's loopback, or nowhere.</summary>
        private StreamRoute ChooseRoute()
        {
            if (IsMirroring)
            {
                return StreamRoute.Mirror;
            }

            var isConnected = IsConnected && _network != null && _network.IsListening && _network.CustomMessagingManager != null;
            return isConnected ? StreamRoute.Network : StreamRoute.None;
        }

        private void OnRouteChanged(StreamRoute route)
        {
            var wasNetwork = _route == StreamRoute.Network;
            _route = route;
            _loopback.Clear();
            ResetStreams();
            switch (route)
            {
                case StreamRoute.Network:
                    Debug.Log($"{LogTag} Streaming to the partner: body {BodyRatePerSecond} and hand {HandRatePerSecond} per second{(IsSendingReplay ? ", from the Fencer replay" : string.Empty)}.");
                    break;
                case StreamRoute.Mirror:
                    Debug.Log($"{LogTag} Mirroring this Fencer through the partner streams, {_partner.State.MirrorDelaySeconds:0.0} s delay{(IsSendingReplay ? ", from the Fencer replay" : string.Empty)}.");
                    break;
            }

            if (wasNetwork)
            {
                Unregister();
            }
        }

        /// <summary>Starts the streams over: the receiver forgets the opponent, and everything is sent afresh.</summary>
        private void ResetStreams()
        {
            _link.Clear();
            _sentInfo = null;
            _infoSentAt = double.NegativeInfinity;
            _bodySchedule.Reset();
            _handSchedule.Reset();
            _handMotion.Reset();
            _hasLoggedFirstPose = false;
        }

        private void SendInfoIfDue(double now)
        {
            var info = Local.Info;
            var isChanged = !_sentInfo.HasValue
                || _sentInfo.Value.Strip.LengthMeters != info.Strip.LengthMeters
                || _sentInfo.Value.Strip.RearEndZ != info.Strip.RearEndZ
                || _sentInfo.Value.HeightMeters != info.HeightMeters
                || _sentInfo.Value.Hand != info.Hand;
            if (!isChanged && now - _infoSentAt < InfoResendSeconds)
            {
                return;
            }

            if (isChanged)
            {
                Debug.Log($"{LogTag} Sending this Fencer: Strip {info.Strip.LengthMeters:0.00} m (rear end {info.Strip.RearEndZ:0.00} m), height {info.HeightMeters:0.00} m, {info.Hand}-handed.");
            }

            Send(PartnerMessages.EncodeInfo(info), NetworkDelivery.ReliableSequenced, now);
            _sentInfo = info;
            _infoSentAt = now;
        }

        private void SendHandIfDue(double now)
        {
            if (!Local.SwordController.HasValue)
            {
                _handMotion.Reset();
                return;
            }

            // The velocity is measured every frame, sent at the hand's rate.
            _handMotion.Add(now, Local.SwordController.Value);
            if (!_handSchedule.TakeIfDue(now))
            {
                return;
            }

            var blade = Local.Blade;
            var sample = new HandSample(
                now,
                Local.SwordController.Value,
                _handMotion.Velocity,
                _handMotion.AngularVelocity,
                Local.Hand,
                blade.Mount,
                GripGeometry.GetActiveAngles(blade),
                blade.SetSagittaMeters,
                blade.Flex);
            Send(PartnerMessages.EncodeHand(sample), NetworkDelivery.UnreliableSequenced, now);
        }

        /// <summary>Sends one partner message: to the partner over the network, or into the mirror's loopback.</summary>
        private void Send(byte[] message, NetworkDelivery delivery, double nowSeconds)
        {
            if (_route == StreamRoute.Mirror)
            {
                _loopback.Send(message, nowSeconds);
                return;
            }

            var messaging = _network.CustomMessagingManager;
            using (var writer = new FastBufferWriter(WriterCapacityBytes, Allocator.Temp))
            {
                if (!writer.TryBeginWrite(sizeof(int) + message.Length))
                {
                    Debug.LogError($"{LogTag} A {message.Length}-byte message does not fit the writer; not sent.");
                    return;
                }

                writer.WriteValue(message.Length);
                writer.WriteBytes(message);
                if (_network.IsServer)
                {
                    foreach (var clientId in _network.ConnectedClientsIds)
                    {
                        if (clientId != NetworkManager.ServerClientId)
                        {
                            messaging.SendNamedMessage(MessageName, clientId, writer, delivery);
                        }
                    }
                }
                else
                {
                    messaging.SendNamedMessage(MessageName, NetworkManager.ServerClientId, writer, delivery);
                }
            }
        }

        private void Register()
        {
            Unregister();
            _registeredWith = _network.CustomMessagingManager;
            _registeredWith.RegisterNamedMessageHandler(MessageName, OnMessage);
        }

        private void Unregister()
        {
            if (_registeredWith == null)
            {
                return;
            }

            _registeredWith.UnregisterNamedMessageHandler(MessageName);
            _registeredWith = null;
        }

        private void OnMessage(ulong senderClientId, FastBufferReader reader)
        {
            // A broken frame counts as an unreadable message (an empty one), like a message of another format.
            var message = System.Array.Empty<byte>();
            if (reader.TryBeginRead(sizeof(int)))
            {
                reader.ReadValue(out int length);
                if (length > 0 && reader.TryBeginRead(length))
                {
                    message = new byte[length];
                    reader.ReadBytes(ref message, length);
                }
            }

            Receive(message, Time.realtimeSinceStartupAsDouble);
        }

        /// <summary>One message from the partner (or back from the mirror) into the link; the first readable pose is logged.</summary>
        private void Receive(byte[] message, double nowSeconds)
        {
            if (_link.Receive(message, nowSeconds) && !_hasLoggedFirstPose && _link.Partner.HasValue)
            {
                var partner = _link.Partner.Value;
                var bout = _link.Bout;
                Debug.Log($"{LogTag} Partner streams arriving: {partner.HeightMeters:0.00} m, {partner.Hand}-handed, their Strip {partner.Strip.LengthMeters:0.00} m. "
                          + $"Bout Strip {bout.LengthMeters:0.00} m, En Garde gap {bout.EnGardeGapMeters:0.00} m, {bout.RetreatMeters:0.00} m to retreat each.");
                _hasLoggedFirstPose = true;
            }
        }

        private void LogRejectedMessages()
        {
            if (_link.RejectedMessages == _loggedRejectedMessages)
            {
                return;
            }

            Debug.LogWarning($"{LogTag} Dropped {_link.RejectedMessages - _loggedRejectedMessages} unreadable partner message(s) ({_link.RejectedMessages} in all).");
            _loggedRejectedMessages = _link.RejectedMessages;
        }

        /// <summary>Where this app's partner messages go.</summary>
        private enum StreamRoute
        {
            None,

            /// <summary>To the connected partner, over Netcode.</summary>
            Network,

            /// <summary>"Mirror me": into the loopback and back into this app's own <see cref="OpponentLink"/>.</summary>
            Mirror,
        }
    }
}
