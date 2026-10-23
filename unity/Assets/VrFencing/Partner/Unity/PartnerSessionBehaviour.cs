using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace VrFencing.Partner
{
    /// <summary>
    /// Connects two Fencers (PER-80, decided in PER-54 and PER-58): Unity Services with an anonymous sign-in, a
    /// 2-player Relay session through Multiplayer Services (Sessions) with Netcode for GameObjects, joined by the
    /// session's code. The Host's headset runs the session; the Partner (a second headset, or the PC Editor) joins it.
    ///
    /// Join is refused across different app versions: the joining side sends its version in its connection request
    /// and the Host's connection approval refuses a different one with a reason naming both, which the joining side
    /// shows (<see cref="VersionCheck"/>).
    ///
    /// The Partner page's state lives in <see cref="State"/> (<see cref="PartnerSession"/>, plain C#); this only makes
    /// the network calls and reports what happened. <see cref="Changed"/> fires after every change. It sits on the
    /// scene's Partner object with the NetworkManager and its UnityTransport.
    ///
    /// Drops and sleep (PER-84, decided in PER-73): the Host keeps the session open for a dropped Partner to rejoin
    /// within <see cref="PartnerSession.RejoinWindowSeconds"/>, then ends it; the Host leaving tells the Partner so
    /// ("host left"); a sleep of <see cref="PartnerSession.SleepEndsSessionSeconds"/> or more ends this app's session on
    /// waking; a failed connection ends it. Each step logs a <c>[Partner]</c> line.
    ///
    /// Command line (PC Editor or a desktop player): <c>-partnerJoin FGR78N</c> joins at start, <c>-partnerHost</c>
    /// hosts at start.
    /// </summary>
    [RequireComponent(typeof(NetworkManager), typeof(UnityTransport))]
    public sealed class PartnerSessionBehaviour : MonoBehaviour
    {
        private const string LogTag = "[Partner]";
        private const int MaxPlayers = 2;
        private const string JoinArgument = "-partnerJoin";
        private const string HostArgument = "-partnerHost";

        /// <summary>
        /// After telling the Partner "host left", the Host waits this long before closing the session, so the message
        /// goes out before the connection does.
        /// </summary>
        private const int HostLeftFlushMilliseconds = 250;

        private NetworkManager _network;
        private ISession _session;

        // The last session this app joined as the Partner, to reconnect to it if a rejoin by code is refused.
        private string _lastJoinedCode;
        private string _lastJoinedSessionId;

        /// <summary>The Partner page's state.</summary>
        public PartnerSession State { get; } = new PartnerSession();

        /// <summary>Fires on the main thread after <see cref="State"/> changed.</summary>
        public event Action Changed;

        /// <summary>
        /// Stands in for the app version in the version check, to try a refused join without a second build (the PC
        /// Editor's Partner window sets it). Null or empty uses the real version.
        /// </summary>
        public string VersionOverride { get; set; }

        /// <summary>The version this app sends and checks: the app's Version (Player Settings), unless overridden.</summary>
        public string AppVersion => string.IsNullOrEmpty(VersionOverride) ? Application.version : VersionOverride;

        /// <summary>The scene's Partner object, or a new one (the PC Editor's Partner window in a scene without one).</summary>
        public static PartnerSessionBehaviour FindOrCreate()
        {
            var existing = FindFirstObjectByType<PartnerSessionBehaviour>();
            if (existing != null)
            {
                return existing;
            }

            // Built inactive: a NetworkManager added at runtime has no NetworkConfig until one is given, and its
            // OnEnable needs it.
            var partnerObject = new GameObject("Partner");
            partnerObject.SetActive(false);
            var transport = partnerObject.AddComponent<UnityTransport>();
            var network = partnerObject.AddComponent<NetworkManager>();
            network.NetworkConfig ??= new NetworkConfig();
            network.NetworkConfig.NetworkTransport = transport;
            var partner = partnerObject.AddComponent<PartnerSessionBehaviour>();

            // The partner's body and blade streams and their drawing (PER-81); the window gives the view its body model.
            partnerObject.AddComponent<OpponentLinkBehaviour>();
            partnerObject.AddComponent<OpponentViewBehaviour>();

            // The judge and the scoring box (PER-82).
            partnerObject.AddComponent<BoutBehaviour>();
            partnerObject.SetActive(true);
            return partner;
        }

        private void Awake()
        {
            _network = GetComponent<NetworkManager>();
            if (_network.NetworkConfig.NetworkTransport == null)
            {
                _network.NetworkConfig.NetworkTransport = GetComponent<UnityTransport>();
            }

            // The Host approves every connection, its own included, by the version in the request.
            _network.NetworkConfig.ConnectionApproval = true;
            _network.ConnectionApprovalCallback = ApproveConnection;
            _network.OnClientConnectedCallback += OnClientConnected;
            _network.OnClientDisconnectCallback += OnClientDisconnected;
            _network.OnTransportFailure += OnTransportFailure;
            _network.OnServerStarted += KeepOwnScene;
            _network.OnClientStarted += KeepOwnScene;
        }

        private void Start()
        {
            ReadCommandLine();
        }

        private void OnDestroy()
        {
            if (_network != null)
            {
                _network.ConnectionApprovalCallback = null;
                _network.OnClientConnectedCallback -= OnClientConnected;
                _network.OnClientDisconnectCallback -= OnClientDisconnected;
                _network.OnTransportFailure -= OnTransportFailure;
                _network.OnServerStarted -= KeepOwnScene;
                _network.OnClientStarted -= KeepOwnScene;
            }

            DetachSession();
        }

        private void Update()
        {
            switch (State.Tick(Time.realtimeSinceStartupAsDouble))
            {
                case PartnerTick.CountdownChanged:
                    NotifyChanged();
                    break;
                case PartnerTick.RejoinWindowOver:
                    Debug.Log($"{LogTag} The partner did not rejoin within {PartnerSession.RejoinWindowSeconds:0} s; ending the session.");
                    _ = EndAsync(PartnerEnd.PartnerDidNotReturn);
                    break;
            }
        }

        /// <summary>
        /// The headset sleeps (or is taken off) and wakes. A sleep long enough ends the session on waking: the Host's
        /// connection and code are gone, so the session ends for both; the Partner can rejoin with the code kept.
        /// </summary>
        private void OnApplicationPause(bool isPaused)
        {
            var wallSeconds = (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;
            if (isPaused)
            {
                State.OnPaused(wallSeconds);
                return;
            }

            if (State.OnResumed(wallSeconds))
            {
                Debug.LogWarning($"{LogTag} Woke after {PartnerSession.SleepEndsSessionSeconds:0} s or more asleep; the session ends.");
                _ = EndAsync(PartnerEnd.Slept);
            }
        }

        /// <summary>Host: signs in and creates the 2-player session; the code shows once it is up.</summary>
        public void Host()
        {
            _ = HostAsync();
        }

        /// <summary>Join with a typed code; an invalid code stays on the keypad with the reason.</summary>
        public void Join(string typedCode)
        {
            _ = JoinAsync(typedCode);
        }

        /// <summary>
        /// "Mirror me" (PER-140): this Fencer's own frame goes through the partner streams on a pretend link and comes back
        /// as the opponent. No sign-in, no Relay, no network; Leave ends it.
        /// </summary>
        public void Mirror()
        {
            if (!State.BeginMirror())
            {
                return;
            }

            Debug.Log($"{LogTag} Mirror me: showing this Fencer as the opponent, {State.MirrorDelaySeconds:0.0} s delay.");
            NotifyChanged();
        }

        /// <summary>While mirroring: the next delay (PER-140).</summary>
        public void CycleMirrorDelay()
        {
            if (!State.CycleMirrorDelay())
            {
                return;
            }

            Debug.Log($"{LogTag} Mirror delay set to {State.MirrorDelaySeconds:0.0} s.");
            NotifyChanged();
        }

        /// <summary>While mirroring: the next link noise, jitter and loss (PER-145).</summary>
        public void CycleMirrorNoise()
        {
            if (!State.CycleMirrorNoise())
            {
                return;
            }

            Debug.Log($"{LogTag} Mirror link now {MirrorLinkNoise.FormatLabel(State.MirrorNoise)}.");
            NotifyChanged();
        }

        /// <summary>
        /// PC Editor only (PER-145): extra seconds every partner message this app sends or receives waits, each way, so a
        /// headset sees this app's Fencer as over a slower link (the bout's pings, claims and lights too, so the synced
        /// clock stays right). 0 for none.
        /// </summary>
        public double ExtraLinkDelaySeconds { get; set; }

        /// <summary>Leave: the Host's leaving ends the session for both; leaving a mirror ends it here.</summary>
        public void Leave()
        {
            _ = LeaveAsync();
        }

        /// <summary>The Host switches Bout / Paired Practice; the Partner sees the switch.</summary>
        public void SwitchMode()
        {
            _ = SwitchModeAsync();
        }

        /// <summary>Join pressed: the keypad opens (nothing on the network yet).</summary>
        public void BeginEnteringCode()
        {
            if (State.BeginEnteringCode())
            {
                NotifyChanged();
            }
        }

        public void CancelEnteringCode()
        {
            State.CancelEnteringCode();
            NotifyChanged();
        }

        /// <summary>Signs in, creates the session and waits for it to be up; exposed for the online test.</summary>
        public async Task HostAsync()
        {
            if (!State.BeginHost())
            {
                return;
            }

            NotifyChanged();
            Debug.Log($"{LogTag} Hosting a session (app version {AppVersion}).");
            try
            {
                await SignInAsync();
                PrepareNetwork();
                var options = new SessionOptions
                {
                    MaxPlayers = MaxPlayers,
                    IsPrivate = true,
                    SessionProperties = new Dictionary<string, SessionProperty>
                    {
                        [PartnerModeProperty.Key] = new SessionProperty(PartnerModeProperty.Encode(State.Mode), VisibilityPropertyOptions.Member),
                    },
                }.WithRelayNetwork();
                var session = await MultiplayerService.Instance.CreateSessionAsync(options);
                AttachSession(session);
                State.OnHostReady(session.Code);
                Debug.Log($"{LogTag} Session up, code {session.Code}. Waiting for the partner.");
            }
            catch (Exception error) when (IsServiceError(error))
            {
                Debug.LogError($"{LogTag} Hosting failed: {error}");
                await LeaveQuietlyAsync();
                State.OnEnded(PartnerEnd.Failed, $"Could not start a session: {DescribeError(error)}");
            }

            NotifyChanged();
        }

        /// <summary>Signs in and joins by code; refused or failed joins end back on the page with the reason.</summary>
        public async Task JoinAsync(string typedCode)
        {
            if (!State.BeginJoin(typedCode))
            {
                NotifyChanged();
                return;
            }

            NotifyChanged();
            Debug.Log($"{LogTag} Joining {State.Code} (app version {AppVersion}).");
            try
            {
                await SignInAsync();
                PrepareNetwork();
                var session = await JoinOrReconnectAsync(State.Code);
                AttachSession(session);
                _lastJoinedCode = State.Code;
                _lastJoinedSessionId = session.Id;
                if (!_network.IsConnectedClient)
                {
                    // The Host refused the connection (a different version), or the Relay connection failed.
                    var reason = _network.DisconnectReason;
                    Debug.LogWarning($"{LogTag} Joined the session but not connected: {(string.IsNullOrEmpty(reason) ? "no reason given" : reason)}.");
                    await LeaveQuietlyAsync();
                    State.OnEnded(PartnerEnd.Failed, VersionCheck.IsRefusal(reason) ? reason : "Could not connect to the host");
                }
                else
                {
                    State.OnJoined(ReadMode(session));
                    Debug.Log($"{LogTag} Connected to the host, mode {State.Mode}.");
                }
            }
            catch (Exception error) when (IsServiceError(error))
            {
                // Usually a code with no session behind it (mistyped, or the Host left): a warning, not an app error.
                Debug.LogWarning($"{LogTag} Joining {State.Code} failed: {error.Message}");
                var reason = _network.DisconnectReason;
                await LeaveQuietlyAsync();
                State.OnEnded(PartnerEnd.Failed, VersionCheck.IsRefusal(reason) ? reason : $"Could not join {JoinCode.FormatForReading(State.Code)}: {DescribeError(error)}");
            }

            NotifyChanged();
        }

        /// <summary>Leaves the session; exposed for the online test. The Host tells its Partner it left first.</summary>
        public async Task LeaveAsync()
        {
            var isMirror = State.IsMirroring;
            if (!State.BeginLeave())
            {
                return;
            }

            if (isMirror)
            {
                // A mirror has no session or network to close (PER-140).
                State.OnEnded(PartnerEnd.Left, string.Empty);
                Debug.Log($"{LogTag} Mirror me ended.");
                NotifyChanged();
                return;
            }

            NotifyChanged();
            Debug.Log($"{LogTag} Leaving the session.");

            await TellPartnerHostLeftAsync();
            await LeaveQuietlyAsync();
            State.OnEnded(PartnerEnd.Left, string.Empty);
            NotifyChanged();
        }

        /// <summary>
        /// PC Editor only: drops this app's connection without leaving the session, as a lost network or a sleeping
        /// headset would, so a drop and a rejoin with the same code can be tried (PER-84).
        /// </summary>
        public void SimulateDrop()
        {
            if (State.Phase != PartnerPhase.Connected || _network == null || !_network.IsListening)
            {
                return;
            }

            // A shutdown by this app reports no disconnect for itself: end as a lost Host (or a dropped Host) would.
            Debug.LogWarning($"{LogTag} Simulating a dropped connection.");
            _network.Shutdown(true);
            _ = State.Role == PartnerRole.Guest
                ? EndAsync(PartnerEnd.LostHost)
                : EndAsync(PartnerEnd.Failed, "The connection dropped; Host again for a new code");
        }

        private async Task SwitchModeAsync()
        {
            if (!State.SwitchMode() || _session == null)
            {
                return;
            }

            NotifyChanged();
            Debug.Log($"{LogTag} Mode switched to {State.Mode}.");
            try
            {
                var host = _session.AsHost();
                host.SetProperty(PartnerModeProperty.Key, new SessionProperty(PartnerModeProperty.Encode(State.Mode), VisibilityPropertyOptions.Member));
                await host.SavePropertiesAsync();
            }
            catch (Exception error) when (IsServiceError(error))
            {
                // The Host's page already shows the new mode; the Partner gets it with the next successful save.
                Debug.LogError($"{LogTag} Saving the mode failed: {error}");
            }
        }

        /// <summary>
        /// Joins by code. A rejoin after a drop that never left the session (no network to say so) can be refused as
        /// already in it: then this reconnects to that session instead (PER-84).
        /// </summary>
        private async Task<ISession> JoinOrReconnectAsync(string code)
        {
            try
            {
                return await MultiplayerService.Instance.JoinSessionByCodeAsync(code, new JoinSessionOptions());
            }
            catch (SessionException error) when (code == _lastJoinedCode && !string.IsNullOrEmpty(_lastJoinedSessionId))
            {
                Debug.LogWarning($"{LogTag} Joining {code} again failed ({error.Message}); reconnecting to the session instead.");
                return await MultiplayerService.Instance.ReconnectToSessionAsync(_lastJoinedSessionId);
            }
        }

        private static async Task SignInAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                await UnityServices.InitializeAsync();
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                Debug.Log($"{LogTag} Signed in anonymously as {AuthenticationService.Instance.PlayerId}.");
            }
        }

        /// <summary>
        /// Each app keeps its own scene (PER-143, <see cref="PartnerScenes"/>): Netcode's scene sync would otherwise make
        /// the joining side load the Host's scene and unload its own (the PC Editor's Fencer replay). Netcode makes a new
        /// scene manager on every start, so this is set on each.
        /// </summary>
        private void KeepOwnScene()
        {
            if (_network.SceneManager != null)
            {
                _network.SceneManager.VerifySceneBeforeLoading = ShouldSyncScene;
            }
        }

        private static bool ShouldSyncScene(int buildIndex, string sceneName, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            var activeSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            var isSynced = PartnerScenes.ShouldSyncScene(buildIndex, sceneName, activeSceneName);
            if (!isSynced)
            {
                Debug.Log($"{LogTag} Keeping this app's scene ('{activeSceneName}'): scene '{sceneName}' is not synced.");
            }

            return isSynced;
        }

        /// <summary>Sets what this side sends in its connection request: its version.</summary>
        private void PrepareNetwork()
        {
            _network.NetworkConfig.ConnectionData = VersionCheck.EncodeRequest(AppVersion);
        }

        /// <summary>On the Host: tells a connected Partner "host left" and gives the message a moment to go out.</summary>
        private async Task TellPartnerHostLeftAsync()
        {
            if (State.Role != PartnerRole.Host || _network == null || !_network.IsServer)
            {
                return;
            }

            var partners = new List<ulong>();
            foreach (var clientId in _network.ConnectedClientsIds)
            {
                if (clientId != NetworkManager.ServerClientId)
                {
                    partners.Add(clientId);
                }
            }

            if (partners.Count == 0)
            {
                return;
            }

            foreach (var clientId in partners)
            {
                _network.DisconnectClient(clientId, PartnerSession.HostLeftReason);
            }

            Debug.Log($"{LogTag} Told the partner the host left.");
            await Task.Delay(HostLeftFlushMilliseconds);
        }

        private void ApproveConnection(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            var verdict = VersionCheck.Judge(AppVersion, request.Payload);
            response.Approved = verdict.IsAccepted;
            response.CreatePlayerObject = false;
            response.Pending = false;
            if (verdict.IsAccepted)
            {
                return;
            }

            response.Reason = verdict.Reason;
            Debug.LogWarning($"{LogTag} Refused a partner: {verdict.Reason}.");
            State.OnPartnerGone(verdict.Reason, Time.realtimeSinceStartupAsDouble);
            NotifyChanged();
        }

        private void OnClientConnected(ulong clientId)
        {
            if (State.Role == PartnerRole.Host && clientId != NetworkManager.ServerClientId)
            {
                var isRejoin = State.OnPartnerConnected(Time.realtimeSinceStartupAsDouble);
                Debug.Log(isRejoin
                    ? $"{LogTag} Partner rejoined (client {clientId}); the session carries on."
                    : $"{LogTag} Partner connected (client {clientId}).");
                NotifyChanged();
            }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (State.Role == PartnerRole.Host && clientId != NetworkManager.ServerClientId && State.Phase == PartnerPhase.Connected)
            {
                Debug.Log($"{LogTag} Partner dropped or left (client {clientId}); the session stays up with the same code for "
                          + $"{PartnerSession.RejoinWindowSeconds:0} s.");
                State.OnPartnerGone(PartnerPageText.PartnerDroppedMessage, Time.realtimeSinceStartupAsDouble);
                NotifyChanged();
            }
            else if (State.Role == PartnerRole.Guest && State.Phase == PartnerPhase.Connected && !_network.IsConnectedClient)
            {
                var reason = _network.DisconnectReason;
                var end = PartnerSession.GetHostLossEnd(reason);
                Debug.LogWarning($"{LogTag} Lost the host ({end}): {(string.IsNullOrEmpty(reason) ? "no reason given" : reason)}.");
                _ = EndAsync(end);
            }
        }

        /// <summary>The transport failed (Relay gone, network lost): the session cannot go on from this app.</summary>
        private void OnTransportFailure()
        {
            if (State.Phase != PartnerPhase.Connected && State.Phase != PartnerPhase.WaitingForPartner)
            {
                return;
            }

            Debug.LogWarning($"{LogTag} The network transport failed; the session ends.");
            _ = State.Role == PartnerRole.Guest
                ? EndAsync(PartnerEnd.LostHost)
                : EndAsync(PartnerEnd.Failed, "The connection failed; Host again for a new code");
        }

        private void AttachSession(ISession session)
        {
            DetachSession();
            _session = session;
            _session.SessionPropertiesChanged += OnSessionPropertiesChanged;
            _session.RemovedFromSession += OnRemovedFromSession;
            _session.Deleted += OnRemovedFromSession;
        }

        private void DetachSession()
        {
            if (_session == null)
            {
                return;
            }

            _session.SessionPropertiesChanged -= OnSessionPropertiesChanged;
            _session.RemovedFromSession -= OnRemovedFromSession;
            _session.Deleted -= OnRemovedFromSession;
            _session = null;
        }

        private void OnSessionPropertiesChanged()
        {
            if (_session != null && State.Role == PartnerRole.Guest)
            {
                State.OnHostModeChanged(ReadMode(_session));
                Debug.Log($"{LogTag} The host switched the mode to {State.Mode}.");
                NotifyChanged();
            }
        }

        private void OnRemovedFromSession()
        {
            if (State.Phase == PartnerPhase.Leaving || State.Phase == PartnerPhase.Idle)
            {
                return;
            }

            Debug.LogWarning($"{LogTag} The session ended.");
            _ = State.Role == PartnerRole.Guest ? EndAsync(PartnerEnd.HostLeft) : EndAsync(PartnerEnd.Failed, "The session ended");
        }

        /// <summary>
        /// Ends the session for <paramref name="end"/>, with its message (or <paramref name="message"/>). Only the first
        /// of several reports of one end acts: the others find the session already leaving.
        /// </summary>
        private async Task EndAsync(PartnerEnd end, string message = null)
        {
            var role = State.Role;
            if (!State.BeginLeave(end))
            {
                return;
            }

            NotifyChanged();
            if (end == PartnerEnd.PartnerDidNotReturn || end == PartnerEnd.Slept)
            {
                await TellPartnerHostLeftAsync();
            }

            await LeaveQuietlyAsync();
            State.OnEnded(end, message ?? PartnerPageText.FormatEndMessage(end, role));
            Debug.Log($"{LogTag} Session ended ({end}).");
            NotifyChanged();
        }

        /// <summary>Leaves the session, if any; a failure is logged, since the app is done with it either way.</summary>
        private async Task LeaveQuietlyAsync()
        {
            var session = _session;
            DetachSession();
            if (session == null)
            {
                return;
            }

            try
            {
                await session.LeaveAsync();
            }
            catch (Exception error) when (IsServiceError(error))
            {
                Debug.LogWarning($"{LogTag} Leaving the session failed (it times out on the service): {error.Message}");
            }
        }

        private static PartnerMode ReadMode(ISession session)
        {
            return session.Properties != null && session.Properties.TryGetValue(PartnerModeProperty.Key, out var property)
                ? PartnerModeProperty.Decode(property.Value)
                : PartnerMode.Bout;
        }

        /// <summary>The failures a network call can end in: the services, the session, or the network manager's state.</summary>
        private static bool IsServiceError(Exception error)
        {
            return error is RequestFailedException
                || error is SessionException
                || error is ServicesInitializationException
                || error is InvalidOperationException
                || error is TaskCanceledException;
        }

        private static string DescribeError(Exception error)
        {
            return Application.internetReachability == NetworkReachability.NotReachable ? "no internet connection" : error.Message;
        }

        private void NotifyChanged()
        {
            Changed?.Invoke();
        }

        private void ReadCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            var joinIndex = Array.IndexOf(args, JoinArgument);
            if (joinIndex >= 0 && joinIndex + 1 < args.Length)
            {
                Debug.Log($"{LogTag} Joining {args[joinIndex + 1]} from the command line.");
                Join(args[joinIndex + 1]);
            }
            else if (Array.IndexOf(args, HostArgument) >= 0)
            {
                Debug.Log($"{LogTag} Hosting from the command line.");
                Host();
            }
        }
    }
}
