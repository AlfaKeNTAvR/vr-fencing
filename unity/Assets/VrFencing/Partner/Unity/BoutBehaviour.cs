using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using VrFencing.Blade;
using VrFencing.Session;
using VrFencing.Settings;
using VrFencing.Strip;

namespace VrFencing.Partner
{
    /// <summary>
    /// The bout on this headset (PER-82 and PER-83, decided in PER-56, PER-57, PER-58, PER-72, PER-74 and PER-75), while
    /// connected to a partner:
    /// <list type="bullet">
    /// <item>Detects this Fencer's Touches on the opponent as drawn here (<see cref="OpponentTouchSensor"/>, with this
    /// Fencer's Touch rule and the Host's Touch pressure) and claims them; glances and flat contacts only play their
    /// haptic and flash here (a glance also leaves a faint mark, and counts in Paired Practice), they never light.</item>
    /// <item>Reads this Fencer's stance (behind their bout En Garde Line, off their own Strip) and, on the Host, how far
    /// apart the two bodies are, for the bout flow.</item>
    /// <item>Runs <see cref="BoutLink"/> (plain C#): pings for the synced clock, claims, the judge and the flow on the
    /// Host, the lights, the calls and the Paired Practice tally.</item>
    /// <item>Draws the scoring box above the bout Strip's centre with the call, the halt's reason and the prompt or
    /// tally; speaks the calls; moves the drawn En Garde Lines to the bout's; buzzes both controllers when this Fencer
    /// is touched; in Paired Practice fades the opponent at corps-a-corps; saves each ended tally to the Session log.</item>
    /// </list>
    /// Its messages travel as their own Netcode named message (<see cref="MessageName"/>), beside the body and blade
    /// streams. Every claim, verdict, light and flow change logs a <c>[Bout]</c> line.
    /// </summary>
    [RequireComponent(typeof(PartnerSessionBehaviour))]
    // After the opponent view has drawn this frame's opponent (110).
    [DefaultExecutionOrder(120)]
    public sealed class BoutBehaviour : MonoBehaviour
    {
        /// <summary>The Netcode named message the judge's messages go through; the first byte says which.</summary>
        public const string MessageName = "VrFencing.Bout";

        private const string LogTag = "[Bout]";

        // Room for the largest message (the flow, 47 bytes) with its length prefix.
        private const int WriterCapacityBytes = 64;

        [SerializeField]
        [Tooltip("This Fencer's blade: plays the contact haptics and flashes, and the being-touched buzz. Found in the scene if empty; none in the PC Editor's replay scene.")]
        private BladeBehaviour _blade;

        [SerializeField]
        [Tooltip("This Fencer's Strip: the frame the box is drawn in, the off-Strip rule, and the drawn En Garde Lines. Found in the scene if empty.")]
        private StripBehaviour _strip;

        [SerializeField]
        [Tooltip("The Session log a Paired Practice tally is saved to when it ends. Found in the scene if empty; without one, nothing is saved.")]
        private SessionRecorderBehaviour _session;

        private readonly OpponentTouchSensor _sensor = new OpponentTouchSensor();
        private readonly TallyNumbering _tallyNumbering = new TallyNumbering();

        private PartnerSessionBehaviour _partner;
        private NetworkManager _network;
        private OpponentLinkBehaviour _opponentLink;
        private OpponentViewBehaviour _opponentView;
        private CustomMessagingManager _registeredWith;
        private TrainerSettings _settings;
        private BoutLink _link;
        private ScoringBoxView _box;
        private BoutVoice _voice;
        private bool _wasConnected;
        private bool _isOffOwnStrip;
        private bool _isAwaitingFirstFlow;
        private int _loggedRejectedMessages;

        /// <summary>The judge's protocol while connected (null otherwise), for the PC Editor's Partner window.</summary>
        public BoutLink Link => _link;

        /// <summary>Takes changed Settings: the Touch rule and pressure, the double window (as Host), the off-strip tolerance.</summary>
        public void ApplySettings(TrainerSettings settings)
        {
            _settings = settings.Clone();
        }

        /// <summary>Either Fencer resets the Paired Practice tally (the Partner page's Reset tally); it is saved first.</summary>
        public void ResetTally()
        {
            if (_link == null)
            {
                return;
            }

            _link.ResetTally();
            FlushLog();
            SaveEndedTallies();
        }

        private void Awake()
        {
            _partner = GetComponent<PartnerSessionBehaviour>();
            _network = GetComponent<NetworkManager>();
            _opponentLink = GetComponent<OpponentLinkBehaviour>();
            _opponentView = GetComponent<OpponentViewBehaviour>();
            _blade = _blade != null ? _blade : FindFirstObjectByType<BladeBehaviour>();
            _strip = _strip != null ? _strip : FindFirstObjectByType<StripBehaviour>();
            _session = _session != null ? _session : FindFirstObjectByType<SessionRecorderBehaviour>();
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
            _voice?.Dispose();
        }

        private void LateUpdate()
        {
            var now = Time.realtimeSinceStartupAsDouble;
            var isConnected = _partner.State.Phase == PartnerPhase.Connected
                && _network != null && _network.IsListening && _network.CustomMessagingManager != null;
            if (isConnected != _wasConnected)
            {
                OnConnectionChanged(isConnected, now);
            }

            if (!isConnected)
            {
                TickWhileWaitingForRejoin(now);
                return;
            }

            if (_registeredWith != _network.CustomMessagingManager)
            {
                Register();
            }

            _box ??= new ScoringBoxView();
            _voice ??= new BoutVoice();
            if (_isAwaitingFirstFlow && _link.HasHostFlow)
            {
                // The Partner's first tally after (re)joining tells whether the Host carried an interrupted tally on.
                _isAwaitingFirstFlow = false;
                _tallyNumbering.OnReconnected(_link.Tally.IsEmpty);
            }

            _link.SetMyRules(new BoutRules(_settings.Blade.TouchPressure, _settings.BoutDoubleWindowSeconds));
            _link.SetMode(_partner.State.Mode, now);
            ReadStance();
            DetectTouches(now);
            _link.Tick(now);
            SendOutgoing();
            FlushLog();

            foreach (var call in _link.TakeCalls())
            {
                _voice.Say(call);
            }

            if (_link.TakeBeingTouched() && _blade != null)
            {
                _blade.PlayBeingTouched();
            }

            SaveEndedTallies();
            LogRejectedMessages();
            DrawBox(now);
            DrawLinesAndFade();
        }

        /// <summary>
        /// The headset is going to sleep: the Paired Practice tally so far is saved (Interrupted), in case the app never
        /// wakes (PER-84). It goes on if the session does.
        /// </summary>
        private void OnApplicationPause(bool isPaused)
        {
            if (!isPaused || _link == null)
            {
                return;
            }

            _link.SnapshotTally();
            FlushLog();
            SaveEndedTallies();
        }

        private void OnConnectionChanged(bool isConnected, double nowSeconds)
        {
            _wasConnected = isConnected;
            _sensor.Reset();
            _loggedRejectedMessages = 0;
            if (isConnected)
            {
                var me = _partner.State.Role == PartnerRole.Host ? BoutSide.Host : BoutSide.Partner;
                if (_link != null && _link.Me == me)
                {
                    // The Host kept its side through the Partner's drop: the tally carries on.
                    _tallyNumbering.OnReconnected(_link.Tally.IsEmpty);
                    Debug.Log($"{LogTag} The partner rejoined: the bout goes on{(_link.Tally.IsEmpty ? string.Empty : $", Paired Practice tally kept ({BoutCall.FormatCounts(_link.Tally.Host)} / {BoutCall.FormatCounts(_link.Tally.Partner)})")}.");
                    return;
                }

                _link = new BoutLink(me);
                _isAwaitingFirstFlow = me == BoutSide.Partner;
                Debug.Log($"{LogTag} Judging the bout as the {me}{(me == BoutSide.Host ? " (the judge and the bout flow run here)" : string.Empty)}.");
                return;
            }

            // PER-84: a Partner expected back keeps the Paired Practice tally (saved so far); otherwise it ends and is saved.
            var isRejoinExpected = _partner.State.IsRejoinExpected(_network != null ? _network.DisconnectReason : null);
            Debug.Log(isRejoinExpected
                ? $"{LogTag} The connection to the {(_link?.Me == BoutSide.Host ? "partner" : "host")} dropped: the Paired Practice tally so far is saved and kept for a rejoin."
                : $"{LogTag} The partner connection ended.");
            _link?.LosePartner(isRejoinExpected, nowSeconds);
            FlushLog();
            SaveEndedTallies();
            if (!isRejoinExpected || _link?.Me != BoutSide.Host)
            {
                // The Partner starts a new side on rejoining; the Host keeps its side while it waits.
                _link = null;
            }

            _box?.SetVisible(false);
            if (_strip != null)
            {
                _strip.ShowBoutEnGardeLines(null);
            }

            if (_opponentView != null)
            {
                _opponentView.IsFadedForCorpsACorps = false;
            }

            Unregister();
        }

        /// <summary>
        /// On the Host while its dropped Partner may still rejoin: the kept side follows the mode (switching to a Bout
        /// still ends the tally). Once the session stops waiting (left, the window over, slept), the tally ends.
        /// </summary>
        private void TickWhileWaitingForRejoin(double nowSeconds)
        {
            if (_link == null)
            {
                return;
            }

            if (_partner.State.IsWaitingForRejoin)
            {
                _link.SetMode(_partner.State.Mode, nowSeconds);
            }
            else
            {
                Debug.Log($"{LogTag} The partner did not come back before the session ended.");
                _link.EndSession();
            }

            FlushLog();
            SaveEndedTallies();
            if (!_partner.State.IsWaitingForRejoin)
            {
                _link = null;
            }
        }

        /// <summary>This Fencer's place on the bout Strip, and on the Host how far apart the two bodies are drawn.</summary>
        private void ReadStance()
        {
            var local = _opponentLink != null ? _opponentLink.Local : null;
            var bout = _opponentLink != null ? _opponentLink.Link.Bout : null;
            if (local == null || bout == null)
            {
                _isOffOwnStrip = false;
                _link.SetMyStance(null, null);
                return;
            }

            var hasPlacedStrip = _strip != null && _strip.IsPlaced;
            var stance = FencerStance.Read(local.Body, local.Head?.Position, local.Strip, bout, OffStripToleranceMeters, hasPlacedStrip);
            _isOffOwnStrip = stance.OffStrip != OffStripKind.None;
            var view = _opponentView != null ? _opponentView.DrawnView : null;
            var opponentBody = view != null && !view.IsBodyStale ? view.Body : null;
            _link.SetMyStance(stance, FencerStance.GetBodiesApartMeters(local.Body, local.Head?.Position, opponentBody));
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
                        _link.ReportGlance();
                        break;
                }
            }
        }

        private float OffStripToleranceMeters => _strip != null ? _strip.OffStripToleranceMeters : 0.0f;

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
            var info = _link.HasHostFlow ? BoutCall.FormatInfo(_link.FlowState, _link.Me, _link.Tally, _isOffOwnStrip) : string.Empty;
            _box.DrawFlow(_link.Announcer.Word, _link.Announcer.Reason, info);
        }

        /// <summary>The bout's En Garde Lines drawn on this Fencer's Strip; in Paired Practice, the opponent faded at corps-a-corps.</summary>
        private void DrawLinesAndFade()
        {
            var bout = _opponentLink != null ? _opponentLink.Link.Bout : null;
            if (_strip != null)
            {
                _strip.ShowBoutEnGardeLines(bout != null ? (bout.MyEnGardeLineZ, bout.OpponentEnGardeLineZ) : ((float, float)?)null);
            }

            if (_opponentView == null)
            {
                return;
            }

            var local = _opponentLink != null ? _opponentLink.Local : null;
            var view = _opponentView.DrawnView;
            var apart = local != null && view != null ? FencerStance.GetBodiesApartMeters(local.Body, local.Head?.Position, view.Body) : null;
            _opponentView.IsFadedForCorpsACorps = _link.FlowState.Mode == PartnerMode.PairedPractice
                                                  && apart.HasValue && apart.Value < FencerStance.CorpsACorpsMeters;
        }

        private void SaveEndedTallies()
        {
            if (_link == null)
            {
                return;
            }

            foreach (var ended in _link.TakeEndedTallies())
            {
                var role = _link.Me.ToString();
                var number = _tallyNumbering.Number(ended);
                Debug.Log($"{LogTag} Paired Practice tally {number} {(ended.End == PairedPracticeEnd.Interrupted ? "saved so far" : "ended")} ({ended.End}): "
                          + $"you {BoutCall.FormatCounts(ended.Mine)}; opponent {BoutCall.FormatCounts(ended.Opponents)}.");
                var recorder = _session != null ? _session.Recorder : null;
                if (recorder == null)
                {
                    Debug.LogWarning($"{LogTag} No Session log in this scene; the tally is not saved.");
                    continue;
                }

                recorder.RecordPairedPractice(role, number, ended.End, ended.Mine, ended.Opponents);
            }
        }

        private void FlushLog()
        {
            if (_link == null)
            {
                return;
            }

            foreach (var line in _link.TakeLog())
            {
                Debug.Log(line);
            }
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
