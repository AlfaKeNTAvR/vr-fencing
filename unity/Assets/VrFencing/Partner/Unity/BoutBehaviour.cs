using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using VrFencing.Blade;
using VrFencing.Settings;
using VrFencing.Strip;

namespace VrFencing.Partner
{
    /// <summary>
    /// The bout's judge on this headset (PER-82, decided in PER-56 and PER-74), while connected to a partner:
    /// <list type="bullet">
    /// <item>Detects this Fencer's Touches on the opponent as drawn here (<see cref="OpponentTouchSensor"/>, with this
    /// Fencer's Touch rule and the Host's Touch pressure) and claims them; glances and flat contacts only play their
    /// haptic and flash here (a glance also leaves a faint mark), they never light.</item>
    /// <item>Runs <see cref="BoutLink"/> (plain C#): pings for the synced clock and the ping text, claims, the judge on
    /// the Host, the lights.</item>
    /// <item>Draws the scoring box above the bout Strip's centre, and buzzes both controllers when this Fencer is
    /// touched.</item>
    /// </list>
    /// Its messages travel as their own Netcode named message (<see cref="MessageName"/>), beside the body and blade
    /// streams. Every claim, verdict and light logs a <c>[Bout]</c> line.
    /// </summary>
    [RequireComponent(typeof(PartnerSessionBehaviour))]
    // After the opponent view has drawn this frame's opponent (110).
    [DefaultExecutionOrder(120)]
    public sealed class BoutBehaviour : MonoBehaviour
    {
        /// <summary>The Netcode named message the judge's messages go through; the first byte says which.</summary>
        public const string MessageName = "VrFencing.Bout";

        private const string LogTag = "[Bout]";

        // Room for the largest message with its length prefix.
        private const int WriterCapacityBytes = 64;

        [SerializeField]
        [Tooltip("This Fencer's blade: plays the contact haptics and flashes, and the being-touched buzz. Found in the scene if empty; none in the PC Editor's replay scene.")]
        private BladeBehaviour _blade;

        [SerializeField]
        [Tooltip("This Fencer's Strip: the frame the box is drawn in, and the off-Strip rule. Found in the scene if empty.")]
        private StripBehaviour _strip;

        private readonly OpponentTouchSensor _sensor = new OpponentTouchSensor();

        private PartnerSessionBehaviour _partner;
        private NetworkManager _network;
        private OpponentLinkBehaviour _opponentLink;
        private OpponentViewBehaviour _opponentView;
        private CustomMessagingManager _registeredWith;
        private TrainerSettings _settings;
        private BoutLink _link;
        private ScoringBoxView _box;
        private bool _wasConnected;
        private int _loggedRejectedMessages;

        /// <summary>The judge's protocol while connected (null otherwise), for the PC Editor's Partner window.</summary>
        public BoutLink Link => _link;

        /// <summary>Takes changed Settings: the Touch rule and pressure, the double window (as Host).</summary>
        public void ApplySettings(TrainerSettings settings)
        {
            _settings = settings.Clone();
        }

        private void Awake()
        {
            _partner = GetComponent<PartnerSessionBehaviour>();
            _network = GetComponent<NetworkManager>();
            _opponentLink = GetComponent<OpponentLinkBehaviour>();
            _opponentView = GetComponent<OpponentViewBehaviour>();
            _blade = _blade != null ? _blade : FindFirstObjectByType<BladeBehaviour>();
            _strip = _strip != null ? _strip : FindFirstObjectByType<StripBehaviour>();
            _settings = SettingsFile.Load();
        }

        private void OnEnable()
        {
            if (_strip != null)
            {
                _strip.SettingsChanged += ApplySettings;
            }
        }

        private void OnDisable()
        {
            if (_strip != null)
            {
                _strip.SettingsChanged -= ApplySettings;
            }
        }

        private void OnDestroy()
        {
            Unregister();
            _box?.Dispose();
        }

        private void LateUpdate()
        {
            var now = Time.realtimeSinceStartupAsDouble;
            var isConnected = _partner.State.Phase == PartnerPhase.Connected
                && _network != null && _network.IsListening && _network.CustomMessagingManager != null;
            if (isConnected != _wasConnected)
            {
                OnConnectionChanged(isConnected);
            }

            if (!isConnected)
            {
                return;
            }

            if (_registeredWith != _network.CustomMessagingManager)
            {
                Register();
            }

            _box ??= new ScoringBoxView();
            _link.SetMyRules(new BoutRules(_settings.Blade.TouchPressure, _settings.BoutDoubleWindowSeconds));
            DetectTouches(now);
            _link.Tick(now);
            SendOutgoing();

            foreach (var line in _link.TakeLog())
            {
                Debug.Log(line);
            }

            if (_link.TakeBeingTouched() && _blade != null)
            {
                _blade.PlayBeingTouched();
            }

            LogRejectedMessages();
            DrawBox(now);
        }

        private void OnConnectionChanged(bool isConnected)
        {
            _wasConnected = isConnected;
            _sensor.Reset();
            _loggedRejectedMessages = 0;
            if (isConnected)
            {
                var me = _partner.State.Role == PartnerRole.Host ? BoutSide.Host : BoutSide.Partner;
                _link = new BoutLink(me);
                Debug.Log($"{LogTag} Judging the bout as the {me}{(me == BoutSide.Host ? " (the judge runs here)" : string.Empty)}.");
            }
            else
            {
                _link = null;
                _box?.SetVisible(false);
                Unregister();
            }
        }

        /// <summary>This Fencer's blade against the opponent as drawn this frame; Touches are claimed, the rest only felt.</summary>
        private void DetectTouches(double now)
        {
            var local = _opponentLink != null ? _opponentLink.Local : null;
            var view = _opponentView != null ? _opponentView.DrawnView : null;
            BladeSegment? blade = local != null && local.SwordController.HasValue
                ? OpponentTouchSensor.GetBlade(local.SwordController.Value, local.Blade, local.Hand)
                : (BladeSegment?)null;
            var body = view != null && !view.IsBodyStale ? view.Body : null;
            var rules = TouchRules.For(_settings.Blade.TouchRule, _link.Rules.Pressure);
            foreach (var bladeEvent in _sensor.Step(now, blade, body, view?.Info.HeightMeters ?? 0.0f, rules))
            {
                Debug.Log($"{LogTag} {bladeEvent.Type} on the opponent's {bladeEvent.Zone}: tip {bladeEvent.TipSpeedMetersPerSecond:0.00} m/s, "
                          + $"approach {bladeEvent.ApproachAngleDegrees:0} deg, blade {bladeEvent.BladeAxisAngleDegrees:0} deg, pressure {_link.Rules.Pressure}.");
                if (_blade != null)
                {
                    _blade.PlayOpponentContact(bladeEvent);
                }

                switch (bladeEvent.Type)
                {
                    case BladeEventType.Touch:
                        _link.Claim(bladeEvent.Zone, bladeEvent.TimeSeconds, IsOffOwnStrip(local), now);
                        break;
                    case BladeEventType.TipGlance:
                        _box.MarkGlance(ToRoom(bladeEvent.ContactPoint), Time.time);
                        break;
                }
            }
        }

        /// <summary>The off-Strip rule needs a placed Strip; without one (the PC Editor's replay) a Touch is never off it.</summary>
        private bool IsOffOwnStrip(LocalFencerFrame local)
        {
            if (local == null || _strip == null || !_strip.IsPlaced)
            {
                return false;
            }

            return OwnStripCheck.IsOffOwnStrip(local.Body, local.Head?.Position, local.Strip, _strip.OffStripToleranceMeters);
        }

        private void DrawBox(double now)
        {
            var bout = _opponentLink != null ? _opponentLink.Link.Bout : null;
            _box.SetVisible(bout != null);
            _box.Tick(Time.time);
            if (bout == null)
            {
                return;
            }

            var stripInRoom = GetStripInRoom();
            _box.Draw(
                ToRoom(new System.Numerics.Vector3(0.0f, 0.0f, bout.CenterZ)),
                stripInRoom.rotation,
                _link.Box.IsMyLampLit(now),
                _link.Box.IsOpponentsLampLit(now),
                ScoringBox.FormatPing(_link.Clock.RoundTripSeconds));
        }

        private Vector3 ToRoom(System.Numerics.Vector3 pointInStrip)
        {
            var stripInRoom = GetStripInRoom();
            return stripInRoom.position + (stripInRoom.rotation * pointInStrip.ToUnity());
        }

        /// <summary>This Fencer's Strip frame in the room (the room's origin without a placed Strip), as the opponent is drawn.</summary>
        private Pose GetStripInRoom()
        {
            var enGardeLine = _strip != null && _strip.IsPlaced ? _strip.EnGardeLine : null;
            return enGardeLine != null ? new Pose(enGardeLine.position, enGardeLine.rotation) : Pose.identity;
        }

        private void SendOutgoing()
        {
            foreach (var outgoing in _link.TakeOutgoing())
            {
                Send(outgoing.Message, outgoing.IsReliable ? NetworkDelivery.ReliableSequenced : NetworkDelivery.Unreliable);
            }
        }

        private void Send(byte[] message, NetworkDelivery delivery)
        {
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

            if (_link == null)
            {
                return;
            }

            // A pong goes back at once: waiting for the next frame would add up to a frame to one leg of the round trip.
            _link.Receive(message, Time.realtimeSinceStartupAsDouble);
            SendOutgoing();
        }

        private void LogRejectedMessages()
        {
            if (_link.RejectedMessages == _loggedRejectedMessages)
            {
                return;
            }

            Debug.LogWarning($"{LogTag} Dropped {_link.RejectedMessages - _loggedRejectedMessages} unreadable bout message(s) ({_link.RejectedMessages} in all).");
            _loggedRejectedMessages = _link.RejectedMessages;
        }
    }
}
