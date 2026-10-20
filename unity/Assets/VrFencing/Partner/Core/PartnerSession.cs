namespace VrFencing.Partner
{
    /// <summary>Where the Partner connection stands.</summary>
    public enum PartnerPhase
    {
        /// <summary>No session: solo Drills are on.</summary>
        Idle,

        /// <summary>Signing in and creating the Host's session.</summary>
        Starting,

        /// <summary>The Host's session is up and its code is shown; no Partner yet.</summary>
        WaitingForPartner,

        /// <summary>The Partner types the Host's code on the keypad; nothing is connected yet.</summary>
        EnteringCode,

        /// <summary>Signing in and joining the Host's session by its code.</summary>
        Joining,

        /// <summary>Both Fencers are connected.</summary>
        Connected,

        /// <summary>Leaving the session.</summary>
        Leaving,
    }

    /// <summary>This app's side of a session.</summary>
    public enum PartnerRole
    {
        None,

        /// <summary>Created the session (Nikita's headset): runs it and picks the mode.</summary>
        Host,

        /// <summary>Joined the Host's session by its code.</summary>
        Guest,
    }

    /// <summary>What the two Fencers do together, picked by the Host (PER-58, PER-72).</summary>
    public enum PartnerMode
    {
        Bout,
        PairedPractice,
    }

    /// <summary>The mode as the Host stores it on the session, so the Partner reads it on joining and on every switch.</summary>
    public static class PartnerModeProperty
    {
        public const string Key = "mode";

        public static string Encode(PartnerMode mode)
        {
            return mode == PartnerMode.PairedPractice ? "paired-practice" : "bout";
        }

        /// <summary>The stored mode; a missing or unknown value reads as a Bout.</summary>
        public static PartnerMode Decode(string value)
        {
            return value == "paired-practice" ? PartnerMode.PairedPractice : PartnerMode.Bout;
        }
    }

    /// <summary>
    /// The Partner page's state (PER-80, decided in PER-58): Host creates a session and shows its code, Join types a
    /// code on the keypad, the Host switches Bout / Paired Practice, Leave ends it. The Unity layer starts the network
    /// calls and reports back what happened; this decides what the page offers and says. While in a session (from
    /// starting or joining until it is left) solo Drills are off and the Dummy is hidden, so it never stands where the
    /// opponent stands.
    /// </summary>
    public sealed class PartnerSession
    {
        public PartnerPhase Phase { get; private set; } = PartnerPhase.Idle;

        public PartnerRole Role { get; private set; } = PartnerRole.None;

        /// <summary>The session's join code while hosting or joining, else empty.</summary>
        public string Code { get; private set; } = string.Empty;

        public PartnerMode Mode { get; private set; } = PartnerMode.Bout;

        /// <summary>The last thing worth telling the Fencer (an error, a refusal, the Partner leaving); empty when none.</summary>
        public string Message { get; private set; } = string.Empty;

        /// <summary>A session is starting, up, or being left: solo Drills wait, and the Dummy is hidden.</summary>
        public bool IsInSession => Phase != PartnerPhase.Idle && Phase != PartnerPhase.EnteringCode;

        public bool AreSoloDrillsAllowed => !IsInSession;

        public bool IsDummyHidden => IsInSession;

        /// <summary>A network call is on its way; the page waits for it.</summary>
        public bool IsBusy => Phase == PartnerPhase.Starting || Phase == PartnerPhase.Joining || Phase == PartnerPhase.Leaving;

        public bool CanHost => Phase == PartnerPhase.Idle;

        public bool CanJoin => Phase == PartnerPhase.Idle;

        public bool CanLeave => Phase == PartnerPhase.WaitingForPartner || Phase == PartnerPhase.Connected;

        /// <summary>Only the Host picks the mode, once its session is up.</summary>
        public bool CanSwitchMode =>
            Role == PartnerRole.Host && (Phase == PartnerPhase.WaitingForPartner || Phase == PartnerPhase.Connected);

        /// <summary>Host pressed: starts creating a session. Returns false (nothing changes) when not offered.</summary>
        public bool BeginHost()
        {
            if (!CanHost)
            {
                return false;
            }

            Phase = PartnerPhase.Starting;
            Role = PartnerRole.Host;
            Code = string.Empty;
            Mode = PartnerMode.Bout;
            Message = string.Empty;
            return true;
        }

        /// <summary>The Host's session is up with this code; the page shows it while waiting for the Partner.</summary>
        public void OnHostReady(string code)
        {
            if (Phase != PartnerPhase.Starting)
            {
                return;
            }

            Phase = PartnerPhase.WaitingForPartner;
            Code = code ?? string.Empty;
        }

        /// <summary>On the Host: the Partner is connected (its version was accepted).</summary>
        public void OnPartnerConnected()
        {
            if (Role == PartnerRole.Host && Phase == PartnerPhase.WaitingForPartner)
            {
                Phase = PartnerPhase.Connected;
                Message = string.Empty;
            }
        }

        /// <summary>
        /// On the Host: the Partner left or was refused. The session stays up with the same code, so the Partner can
        /// join again.
        /// </summary>
        public void OnPartnerGone(string message)
        {
            if (Role != PartnerRole.Host || (Phase != PartnerPhase.Connected && Phase != PartnerPhase.WaitingForPartner))
            {
                return;
            }

            Phase = PartnerPhase.WaitingForPartner;
            Message = message ?? string.Empty;
        }

        /// <summary>Join pressed: the keypad opens.</summary>
        public bool BeginEnteringCode()
        {
            if (!CanJoin)
            {
                return false;
            }

            Phase = PartnerPhase.EnteringCode;
            Role = PartnerRole.None;
            Code = string.Empty;
            Message = string.Empty;
            return true;
        }

        /// <summary>The keypad's Cancel: back to the page with nothing joined.</summary>
        public void CancelEnteringCode()
        {
            if (Phase == PartnerPhase.EnteringCode)
            {
                Phase = PartnerPhase.Idle;
            }
        }

        /// <summary>
        /// The keypad's Join (or the PC Editor's): starts joining when <paramref name="typed"/> is a valid code. An
        /// invalid code stays on the keypad with the reason in <see cref="Message"/>. Joining is also allowed straight
        /// from Idle, for the Editor panel and the command line.
        /// </summary>
        public bool BeginJoin(string typed)
        {
            if (Phase != PartnerPhase.EnteringCode && Phase != PartnerPhase.Idle)
            {
                return false;
            }

            var code = JoinCode.TryParse(typed, out var problem);
            if (code == null)
            {
                Message = problem;
                return false;
            }

            Phase = PartnerPhase.Joining;
            Role = PartnerRole.Guest;
            Code = code;
            Message = string.Empty;
            return true;
        }

        /// <summary>On the Partner: connected to the Host's session.</summary>
        public void OnJoined(PartnerMode hostMode)
        {
            if (Phase == PartnerPhase.Joining)
            {
                Phase = PartnerPhase.Connected;
                Mode = hostMode;
            }
        }

        /// <summary>The Host switches the mode; returns false when it may not.</summary>
        public bool SwitchMode()
        {
            if (!CanSwitchMode)
            {
                return false;
            }

            Mode = Mode == PartnerMode.Bout ? PartnerMode.PairedPractice : PartnerMode.Bout;
            return true;
        }

        /// <summary>On the Partner: the Host switched the mode.</summary>
        public void OnHostModeChanged(PartnerMode mode)
        {
            if (Role == PartnerRole.Guest)
            {
                Mode = mode;
            }
        }

        /// <summary>Leave pressed.</summary>
        public bool BeginLeave()
        {
            if (!CanLeave)
            {
                return false;
            }

            Phase = PartnerPhase.Leaving;
            return true;
        }

        /// <summary>
        /// The session is over for this app: left, failed to start or join, refused, or ended by the other side. Back
        /// to Idle with <paramref name="message"/> shown (empty after a plain Leave).
        /// </summary>
        public void OnEnded(string message)
        {
            Phase = PartnerPhase.Idle;
            Role = PartnerRole.None;
            Code = string.Empty;
            Mode = PartnerMode.Bout;
            Message = message ?? string.Empty;
        }
    }
}
