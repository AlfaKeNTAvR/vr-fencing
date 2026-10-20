using System;
using System.Text;

namespace VrFencing.Partner
{
    /// <summary>The Host's answer to a joining Partner's app version.</summary>
    public readonly struct VersionVerdict
    {
        private VersionVerdict(bool isAccepted, string hostVersion, string partnerVersion)
        {
            IsAccepted = isAccepted;
            HostVersion = hostVersion;
            PartnerVersion = partnerVersion;
        }

        public bool IsAccepted { get; }

        public string HostVersion { get; }

        /// <summary>The joining side's version, or empty when its request carried none (an older or foreign app).</summary>
        public string PartnerVersion { get; }

        /// <summary>Why the join was refused, naming both versions; empty when accepted. Shown on both Partner pages.</summary>
        public string Reason => IsAccepted
            ? string.Empty
            : $"{VersionCheck.RefusalPrefix} host {Describe(HostVersion)}, partner {Describe(PartnerVersion)}. Install the same build on both";

        public static VersionVerdict Accept(string hostVersion, string partnerVersion)
        {
            return new VersionVerdict(true, hostVersion, partnerVersion);
        }

        public static VersionVerdict Refuse(string hostVersion, string partnerVersion)
        {
            return new VersionVerdict(false, hostVersion, partnerVersion);
        }

        private static string Describe(string version)
        {
            return string.IsNullOrEmpty(version) ? "unknown" : version;
        }
    }

    /// <summary>
    /// Join is refused across different app versions (PER-80): two builds that send different data would desync
    /// silently. The joining side puts its version in its connection request; the Host compares it with its own and
    /// refuses with a reason that names both. Versions must match exactly (the app's Version in Player Settings).
    /// </summary>
    public static class VersionCheck
    {
        /// <summary>How every refusal reason starts, so the joining side can tell it from the network's own messages.</summary>
        public const string RefusalPrefix = "Different app versions:";

        // Marks the request as this app's, so a request from anything else reads as "unknown" instead of as a version.
        private const string PayloadPrefix = "vrfencing/";

        /// <summary>
        /// Whether a disconnect reason is the Host's version refusal. The network also leaves its own reasons ("[Disconnect
        /// Event] ... The transport was shutdown"), even from an earlier session, which mean nothing to the Fencer.
        /// </summary>
        public static bool IsRefusal(string disconnectReason)
        {
            return disconnectReason != null && disconnectReason.StartsWith(RefusalPrefix, StringComparison.Ordinal);
        }

        /// <summary>The bytes a joining side sends with its connection request.</summary>
        public static byte[] EncodeRequest(string appVersion)
        {
            if (string.IsNullOrEmpty(appVersion))
            {
                throw new ArgumentException("The app version must not be empty.", nameof(appVersion));
            }

            return Encoding.UTF8.GetBytes(PayloadPrefix + appVersion);
        }

        /// <summary>The version in a connection request, or empty when the request is missing or not this app's.</summary>
        public static string DecodeRequest(byte[] payload)
        {
            if (payload == null || payload.Length == 0)
            {
                return string.Empty;
            }

            string text;
            try
            {
                text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(payload);
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }

            return text.StartsWith(PayloadPrefix, StringComparison.Ordinal) ? text.Substring(PayloadPrefix.Length) : string.Empty;
        }

        /// <summary>The Host's verdict on a connection request.</summary>
        public static VersionVerdict Judge(string hostVersion, byte[] requestPayload)
        {
            var partnerVersion = DecodeRequest(requestPayload);
            var isSame = partnerVersion.Length > 0 && string.Equals(hostVersion, partnerVersion, StringComparison.Ordinal);
            return isSame ? VersionVerdict.Accept(hostVersion, partnerVersion) : VersionVerdict.Refuse(hostVersion, partnerVersion);
        }
    }
}
