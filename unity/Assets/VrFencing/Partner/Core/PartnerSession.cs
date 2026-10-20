using System;

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

    /// <summary>Why the last session ended for this app (PER-84).</summary>
    public enum PartnerEnd
    {
        /// <summary>No session has ended yet.</summary>
        None,

        /// <summary>This Fencer pressed Leave.</summary>
        Left,

        /// <summary>Could not start, join or connect, or the version was refused.</summary>
        Failed,

        /// <summary>On the Partner: the Host left on purpose.</summary>
        HostLeft,

        /// <summary>On the Partner: the connection to the Host ended without a word (the Host slept, or a network drop).</summary>
        LostHost,

        /// <summary>This headset slept for <see cref="PartnerSession.SleepEndsSessionSeconds"/> or more.</summary>
        Slept,

        /// <summary>On the Host: the Partner dropped and did not rejoin within <see cref="PartnerSession.RejoinWindowSeconds"/>.</summary>
        PartnerDidNotReturn,
    }

    /// <summary>What a frame of <see cref="PartnerSession.Tick"/> asks of the Unity layer.</summary>
    public enum PartnerTick
    {
        Nothing,

        /// <summary>The rejoin countdown's shown second changed: redraw the page.</summary>
        CountdownChanged,

        /// <summary>The rejoin window is over: end the session (<see cref="PartnerEnd.PartnerDidNotReturn"/>).</summary>
        RejoinWindowOver,
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
    ///
    /// Drops (PER-84, decided in PER-73): when the Partner drops or sleeps, the Host keeps the session and its code open
    /// for <see cref="RejoinWindowSeconds"/> and then ends it; a rejoin with the same code in time carries the session on.
    /// When the Host leaves or sleeps, the session ends for both (no host migration): the Partner goes back to the solo
    /// menu, with the code kept for a rejoin in case the Host is still waiting. A headset asleep for
    /// <see cref="SleepEndsSessionSeconds"/> or more ends its session on waking. Times are seconds on one monotonic clock
    /// the caller picks; the sleep is measured on the wall clock, which keeps running while the headset sleeps.
    /// </summary>
    public sealed class PartnerSession
    {
        /// <summary>How long the Host keeps the session and its code open for a dropped Partner to rejoin: 5 minutes (PER-73).</summary>
        public const double RejoinWindowSeconds = 300.0;

        /// <summary>
        /// A sleep this long or longer ends the session on waking: about Relay's idle timeout (10 s), after which the
        /// connection is gone anyway. A shorter blink (the headset lifted for a moment) keeps it.
        /// </summary>
        public const double SleepEndsSessionSeconds = 10.0;

        /// <summary>
        /// The reason the Host gives its Partner when it leaves on purpose (Netcode's disconnect reason), so the Partner
        /// can tell "host left" from a dropped connection.
        /// </summary>
        public const string HostLeftReason = "The host left";

        /// <summary>On the Partner: why the connection to the Host ended, from Netcode's disconnect reason.</summary>
        public static PartnerEnd GetHostLossEnd(string disconnectReason)
        {
            return disconnectReason == HostLeftReason ? PartnerEnd.HostLeft : PartnerEnd.LostHost;
        }

        private double? _rejoinDeadlineSeconds;
        private double? _pausedAtWallSeconds;
        private int _shownRejoinSeconds = -1;
        private PartnerEnd _leavingFor = PartnerEnd.Left;

        public PartnerPhase Phase { get; private set; } = PartnerPhase.Idle;

        public PartnerRole Role { get; private set; } = PartnerRole.None;

        /// <summary>The session's join code while hosting or joining, else empty.</summary>
        public string Code { get; private set; } = string.Empty;

        public PartnerMode Mode { get; private set; } = PartnerMode.Bout;

        /// <summary>The last thing worth telling the Fencer (an error, a refusal, the Partner leaving); empty when none.</summary>
        public string Message { get; private set; } = string.Empty;

        /// <summary>Why the last session ended (<see cref="PartnerEnd.None"/> before any has).</summary>
        public PartnerEnd LastEnd { get; private set; } = PartnerEnd.None;

        /// <summary>Counts the ends, so a page can tell a new end from one it already handled.</summary>
        public int EndCount { get; private set; }

        /// <summary>
        /// On the Partner, after the Host was lost or this headset slept: the code to rejoin with (the keypad starts with
        /// it). Empty when there is nothing to rejoin.
        /// </summary>
        public string RejoinCode { get; private set; } = string.Empty;

        /// <summary>The last end took a connected Partner out of the Host's session: the menu goes back to solo practice (Home).</summary>
        public bool IsBackToSoloMenu { get; private set; }

        /// <summary>On the Host: its Partner dropped, and the session waits for them to rejoin with the same code.</summary>
        public bool IsWaitingForRejoin => Role == PartnerRole.Host && Phase == PartnerPhase.WaitingForPartner && _rejoinDeadlineSeconds.HasValue;

        /// <summary>Whole seconds left to rejoin, as of the last <see cref="Tick"/>; null when not waiting for a rejoin.</summary>
        public int? RejoinSecondsLeft => IsWaitingForRejoin && _shownRejoinSeconds >= 0 ? _shownRejoinSeconds : (int?)null;

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

        /// <summary>
        /// On the Host: the Partner is connected (its version was accepted). Returns true when it is a rejoin within the
        /// rejoin window: the session carries on, the Paired Practice tally too.
        /// </summary>
        public bool OnPartnerConnected(double nowSeconds)
        {
            if (Role != PartnerRole.Host || Phase != PartnerPhase.WaitingForPartner)
            {
                return false;
            }

            var isRejoin = _rejoinDeadlineSeconds.HasValue && nowSeconds < _rejoinDeadlineSeconds.Value;
            Phase = PartnerPhase.Connected;
            Message = string.Empty;
            _rejoinDeadlineSeconds = null;
            _shownRejoinSeconds = -1;
            return isRejoin;
        }

        /// <summary>
        /// On the Host: the Partner left, dropped or was refused. The session stays up with the same code; a Partner who
        /// was connected may rejoin within <see cref="RejoinWindowSeconds"/> of <paramref name="nowSeconds"/>, after which
        /// <see cref="Tick"/> says to end it. A refused join does not restart a window already running.
        /// </summary>
        public void OnPartnerGone(string message, double nowSeconds)
        {
            if (Role != PartnerRole.Host || (Phase != PartnerPhase.Connected && Phase != PartnerPhase.WaitingForPartner))
            {
                return;
            }

            if (Phase == PartnerPhase.Connected)
            {
                _rejoinDeadlineSeconds = nowSeconds + RejoinWindowSeconds;
                _shownRejoinSeconds = -1;
            }

            Phase = PartnerPhase.WaitingForPartner;
            Message = message ?? string.Empty;
        }

        /// <summary>
        /// Once a frame: counts down the rejoin window on the Host. Says when the page should redraw the countdown, and
        /// when the window is over (the Unity layer then ends the session).
        /// </summary>
        public PartnerTick Tick(double nowSeconds)
        {
            if (!IsWaitingForRejoin)
            {
                return PartnerTick.Nothing;
            }

            var left = _rejoinDeadlineSeconds.Value - nowSeconds;
            if (left <= 0.0)
            {
                return PartnerTick.RejoinWindowOver;
            }

            var shown = (int)Math.Ceiling(left);
            if (shown == _shownRejoinSeconds)
            {
                return PartnerTick.Nothing;
            }

            _shownRejoinSeconds = shown;
            return PartnerTick.CountdownChanged;
        }

        /// <summary>The app paused (the headset sleeps or was taken off), at <paramref name="wallSeconds"/> on the wall clock.</summary>
        public void OnPaused(double wallSeconds)
        {
            if (Phase == PartnerPhase.Connected || Phase == PartnerPhase.WaitingForPartner)
            {
                _pausedAtWallSeconds = wallSeconds;
            }
        }

        /// <summary>
        /// The app resumed at <paramref name="wallSeconds"/>. Returns true when the sleep was long enough to end the
        /// session (<see cref="PartnerEnd.Slept"/>): the Host's code went with its connection, and the Partner rejoins.
        /// </summary>
        public bool OnResumed(double wallSeconds)
        {
            var pausedAt = _pausedAtWallSeconds;
            _pausedAtWallSeconds = null;
            return pausedAt.HasValue
                   && (Phase == PartnerPhase.Connected || Phase == PartnerPhase.WaitingForPartner)
                   && wallSeconds - pausedAt.Value >= SleepEndsSessionSeconds;
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

        /// <summary>
        /// Leave pressed, or the session is ending for <paramref name="end"/> (a lost Host, a sleep, the rejoin window
        /// over). Returns false when not in a session to leave, or already leaving.
        /// </summary>
        public bool BeginLeave(PartnerEnd end = PartnerEnd.Left)
        {
            if (!CanLeave)
            {
                return false;
            }

            Phase = PartnerPhase.Leaving;
            _leavingFor = end;
            return true;
        }

        /// <summary>
        /// The connection to the other app just went: whether this side expects it back (PER-84). The Host does while it
        /// waits for its dropped Partner; the Partner does unless the Host said it left (<paramref name="disconnectReason"/>,
        /// Netcode's) or this Fencer left on purpose. A Partner that lost the Host or slept keeps its rejoin code.
        /// </summary>
        public bool IsRejoinExpected(string disconnectReason)
        {
            switch (Phase)
            {
                case PartnerPhase.WaitingForPartner:
                    return IsWaitingForRejoin;
                case PartnerPhase.Connected:
                    return Role == PartnerRole.Guest && GetHostLossEnd(disconnectReason) == PartnerEnd.LostHost;
                case PartnerPhase.Leaving:
                    return Role == PartnerRole.Guest && (_leavingFor == PartnerEnd.LostHost || _leavingFor == PartnerEnd.Slept);
                default:
                    return !string.IsNullOrEmpty(RejoinCode) && (LastEnd == PartnerEnd.LostHost || LastEnd == PartnerEnd.Slept);
            }
        }

        /// <summary>
        /// The session is over for this app, for the reason <paramref name="end"/>: left, failed to start or join,
        /// refused, ended by the other side, slept, or the Partner did not return. Back to Idle with
        /// <paramref name="message"/> shown (empty after a plain Leave). A Partner that lost the Host or slept keeps the
        /// code to rejoin with; a failed join keeps the one it had.
        /// </summary>
        public void OnEnded(PartnerEnd end, string message)
        {
            var wasGuest = Role == PartnerRole.Guest;
            var mayRejoin = wasGuest && (end == PartnerEnd.LostHost || end == PartnerEnd.Slept);
            RejoinCode = mayRejoin ? Code : end == PartnerEnd.Failed ? RejoinCode : string.Empty;
            IsBackToSoloMenu = wasGuest && (end == PartnerEnd.HostLeft || end == PartnerEnd.LostHost || end == PartnerEnd.Slept);
            LastEnd = end;
            EndCount++;
            _rejoinDeadlineSeconds = null;
            _pausedAtWallSeconds = null;
            _shownRejoinSeconds = -1;
            Phase = PartnerPhase.Idle;
            Role = PartnerRole.None;
            Code = string.Empty;
            Mode = PartnerMode.Bout;
            Message = message ?? string.Empty;
        }
    }
}
