using System;
using System.Globalization;

namespace VrFencing.Partner
{
    /// <summary>What the Partner page and the Home page say about the Partner session.</summary>
    public static class PartnerPageText
    {
        public const string SoloDrillsOffMessage = "Leave the Partner session to run solo Drills";

        /// <summary>The big line: the join code while the Host waits, or a short state.</summary>
        public static string FormatHeadline(PartnerSession session)
        {
            switch (session.Phase)
            {
                case PartnerPhase.Starting:
                    return "Starting...";
                case PartnerPhase.WaitingForPartner:
                    return JoinCode.FormatForReading(session.Code);
                case PartnerPhase.Joining:
                    return "Joining...";
                case PartnerPhase.Connected:
                    return "Connected";
                case PartnerPhase.Leaving:
                    return "Leaving...";
                default:
                    return "Not connected";
            }
        }

        /// <summary>The line under it: what to do next.</summary>
        public static string FormatStatus(PartnerSession session)
        {
            switch (session.Phase)
            {
                case PartnerPhase.Starting:
                    return "Signing in and creating a session";
                case PartnerPhase.WaitingForPartner:
                    return session.RejoinSecondsLeft.HasValue
                        ? $"Waiting for partner: rejoin with this code within {FormatMinutes(session.RejoinSecondsLeft.Value)}"
                        : "Read this code to your partner, then wait for them to join";
                case PartnerPhase.Joining:
                    return $"Joining {JoinCode.FormatForReading(session.Code)}";
                case PartnerPhase.Connected:
                    return session.Role == PartnerRole.Host
                        ? $"Your partner joined (code {JoinCode.FormatForReading(session.Code)})"
                        : $"You joined the host (code {JoinCode.FormatForReading(session.Code)})";
                case PartnerPhase.Leaving:
                    return "Closing the session";
                default:
                    return "Host a session, or Join your partner's with their code";
            }
        }

        /// <summary>On the Host, when its Partner dropped or left: the page's message while it waits for a rejoin (PER-84).</summary>
        public const string PartnerDroppedMessage = "Your partner dropped or left";

        /// <summary>
        /// What this app says when its session ended (PER-84, decided in PER-73), for the side it was on. Empty for a
        /// plain Leave; a failure carries its own reason instead.
        /// </summary>
        public static string FormatEndMessage(PartnerEnd end, PartnerRole role)
        {
            // One line on the Home and Partner pages: about 60 characters.
            var minutes = string.Format(CultureInfo.InvariantCulture, "{0:0} min", PartnerSession.RejoinWindowSeconds / 60.0);
            switch (end)
            {
                case PartnerEnd.HostLeft:
                    return "Host left. Back to solo practice";
                case PartnerEnd.LostHost:
                    return $"Host left or the link dropped. Rejoin: Partner > Join ({minutes})";
                case PartnerEnd.Slept:
                    return role == PartnerRole.Host
                        ? "Session ended while your headset slept. Host again"
                        : $"Session dropped while asleep. Rejoin: Partner > Join ({minutes})";
                case PartnerEnd.PartnerDidNotReturn:
                    return $"Partner did not rejoin within {minutes}. Session ended";
                default:
                    return string.Empty;
            }
        }

        /// <summary>Seconds as minutes and seconds, "4:05".</summary>
        public static string FormatMinutes(int seconds)
        {
            var clamped = Math.Max(0, seconds);
            return $"{clamped / 60}:{clamped % 60:00}";
        }

        /// <summary>The mode switch's label (the Host's) or the mode line (the Partner's).</summary>
        public static string FormatMode(PartnerSession session)
        {
            var mode = FormatModeName(session.Mode);
            return session.Role == PartnerRole.Host ? $"Mode: {mode}" : $"Mode: {mode} (the host picks)";
        }

        public static string FormatModeName(PartnerMode mode)
        {
            return mode == PartnerMode.PairedPractice ? "Paired Practice" : "Bout";
        }

        /// <summary>The Home page's Partner button: names the state when in a session.</summary>
        public static string FormatHomeButton(PartnerSession session)
        {
            return session.Phase == PartnerPhase.Connected ? "Partner: on" : session.IsInSession ? "Partner: ..." : "Partner";
        }
    }
}
