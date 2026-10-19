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

        private NetworkManager _network;
        private ISession _session;

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
            }

            DetachSession();
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

        /// <summary>Leave: the Host's leaving ends the session for both.</summary>
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
                State.OnEnded($"Could not start a session: {DescribeError(error)}");
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
                var session = await MultiplayerService.Instance.JoinSessionByCodeAsync(State.Code, new JoinSessionOptions());
                AttachSession(session);
                if (!_network.IsConnectedClient)
                {
                    // The Host refused the connection (a different version), or the Relay connection failed.
                    var reason = _network.DisconnectReason;
                    Debug.LogWarning($"{LogTag} Joined the session but not connected: {(string.IsNullOrEmpty(reason) ? "no reason given" : reason)}.");
                    await LeaveQuietlyAsync();
                    State.OnEnded(VersionCheck.IsRefusal(reason) ? reason : "Could not connect to the host");
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
                State.OnEnded(VersionCheck.IsRefusal(reason) ? reason : $"Could not join {JoinCode.FormatForReading(State.Code)}: {DescribeError(error)}");
            }

            NotifyChanged();
        }

        /// <summary>Leaves the session; exposed for the online test.</summary>
        public async Task LeaveAsync()
        {
            if (!State.BeginLeave())
            {
                return;
            }

            NotifyChanged();
            Debug.Log($"{LogTag} Leaving the session.");
            await LeaveQuietlyAsync();
            State.OnEnded(string.Empty);
            NotifyChanged();
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

        /// <summary>Sets what this side sends in its connection request: its version.</summary>
        private void PrepareNetwork()
        {
            _network.NetworkConfig.ConnectionData = VersionCheck.EncodeRequest(AppVersion);
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
            State.OnPartnerGone(verdict.Reason);
            NotifyChanged();
        }

        private void OnClientConnected(ulong clientId)
        {
            if (State.Role == PartnerRole.Host && clientId != NetworkManager.ServerClientId)
            {
                Debug.Log($"{LogTag} Partner connected (client {clientId}).");
                State.OnPartnerConnected();
                NotifyChanged();
            }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (State.Role == PartnerRole.Host && clientId != NetworkManager.ServerClientId && State.Phase == PartnerPhase.Connected)
            {
                Debug.Log($"{LogTag} Partner disconnected (client {clientId}); the session stays up with the same code.");
                State.OnPartnerGone("Your partner left; they can join again with the same code");
                NotifyChanged();
            }
            else if (State.Role == PartnerRole.Guest && State.Phase == PartnerPhase.Connected && !_network.IsConnectedClient)
            {
                var reason = _network.DisconnectReason;
                Debug.LogWarning($"{LogTag} Lost the host: {(string.IsNullOrEmpty(reason) ? "no reason given" : reason)}.");
                _ = EndAsync("The connection to the host ended");
            }
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
            _ = EndAsync(State.Role == PartnerRole.Guest ? "The host ended the session" : "The session ended");
        }

        private async Task EndAsync(string message)
        {
            await LeaveQuietlyAsync();
            State.OnEnded(message);
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
