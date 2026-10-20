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
                    return "Read this code to your partner, then wait for them to join";
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
