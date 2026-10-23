using System;
using System.Collections.Generic;

namespace VrFencing.Partner
{
    /// <summary>
    /// The delay a "Mirror me" session (PER-140) puts between sending this Fencer's frame and receiving it back as the
    /// opponent: a pretend network link. The Partner page's delay button cycles through <see cref="Options"/>.
    /// </summary>
    public static class MirrorDelay
    {
        /// <summary>A typical home-to-home link: what the opponent looks like with a real partner.</summary>
        public const double DefaultSeconds = 0.1;

        /// <summary>The delays offered, in the order the button cycles them: none, typical, slow, very slow.</summary>
        public static readonly IReadOnlyList<double> Options = new[] { 0.0, 0.1, 0.2, 0.5 };

        /// <summary>The option after <paramref name="currentSeconds"/>, wrapping round; an unknown value goes back to the first.</summary>
        public static double Next(double currentSeconds)
        {
            for (var index = 0; index < Options.Count; index++)
            {
                if (Math.Abs(Options[index] - currentSeconds) < 1e-9)
                {
                    return Options[(index + 1) % Options.Count];
                }
            }

            return Options[0];
        }

        /// <summary>The delay button's label: "Delay 0.1 s".</summary>
        public static string FormatLabel(double seconds)
        {
            return FormattableString.Invariant($"Delay {seconds:0.0} s");
        }
    }

    /// <summary>How rough a "Mirror me" link is besides its delay (PER-145): a home Wi-Fi's jitter, lost packets, or both.</summary>
    public enum MirrorNoise
    {
        /// <summary>A perfect link with a fixed delay (PER-140).</summary>
        Clean,

        /// <summary>Each message up to <see cref="MirrorLinkNoise.JitterSeconds"/> later than the delay.</summary>
        Jitter,

        /// <summary><see cref="MirrorLinkNoise.LossFraction"/> of the hand and body messages lost.</summary>
        Loss,

        /// <summary>Both.</summary>
        JitterAndLoss,
    }

    /// <summary>The link-noise button of a "Mirror me" session (PER-145): cycles <see cref="MirrorNoise"/>.</summary>
    public static class MirrorLinkNoise
    {
        /// <summary>Home Wi-Fi jitter: about the HandStream's 40 ms buffer's half.</summary>
        public const double JitterSeconds = 0.02;

        /// <summary>A poor link's loss of unreliable messages (the research's test plan: 2%).</summary>
        public const double LossFraction = 0.02;

        /// <summary>The next option, wrapping round.</summary>
        public static MirrorNoise Next(MirrorNoise current)
        {
            return (MirrorNoise)(((int)current + 1) % 4);
        }

        public static double GetJitterSeconds(MirrorNoise noise)
        {
            return noise == MirrorNoise.Jitter || noise == MirrorNoise.JitterAndLoss ? JitterSeconds : 0.0;
        }

        public static double GetLossFraction(MirrorNoise noise)
        {
            return noise == MirrorNoise.Loss || noise == MirrorNoise.JitterAndLoss ? LossFraction : 0.0;
        }

        /// <summary>The button's label: "Link clean", "Jitter 20 ms", "Loss 2%", "Jitter + loss".</summary>
        public static string FormatLabel(MirrorNoise noise)
        {
            switch (noise)
            {
                case MirrorNoise.Jitter:
                    return FormattableString.Invariant($"Jitter {JitterSeconds * 1000.0:0} ms");
                case MirrorNoise.Loss:
                    return FormattableString.Invariant($"Loss {LossFraction * 100.0:0}%");
                case MirrorNoise.JitterAndLoss:
                    return "Jitter + loss";
                default:
                    return "Link clean";
            }
        }
    }

    /// <summary>
    /// The pretend network of a "Mirror me" session (PER-140): every partner message this app would send goes in, and
    /// comes out <see cref="DelaySeconds"/> later, in the order it went in, to be received by this app's own
    /// <see cref="OpponentLink"/> as if the partner had sent it. Each message is copied going in, so what comes out is
    /// only the bytes, like a real link. With jitter (PER-145) each message comes out up to <see cref="JitterSeconds"/>
    /// later still, never before one sent earlier (bunched, as a Wi-Fi burst delivers them); with loss, that share of the
    /// messages that may be lost (sent unreliable) never comes out. Random but repeatable: a fixed seed. Also the PC
    /// Editor's extra delay on what it sends to a real partner.
    /// </summary>
    public sealed class MirrorLoopback
    {
        /// <summary>The jitter and loss draws start from this seed, so a test or a session repeats exactly.</summary>
        public const int DefaultSeed = 145;

        private readonly Queue<InFlight> _inFlight = new Queue<InFlight>();
        private readonly Random _random;
        private double _lastDueSeconds = double.NegativeInfinity;

        public MirrorLoopback(double delaySeconds, int seed = DefaultSeed)
        {
            DelaySeconds = CheckDelay(delaySeconds);
            _random = new Random(seed);
        }

        /// <summary>How long each message takes to come back.</summary>
        public double DelaySeconds { get; private set; }

        /// <summary>The most a message comes back later than <see cref="DelaySeconds"/>.</summary>
        public double JitterSeconds { get; private set; }

        /// <summary>The share of messages that may be lost which are.</summary>
        public double LossFraction { get; private set; }

        /// <summary>Messages sent and not yet come back.</summary>
        public int InFlightCount => _inFlight.Count;

        /// <summary>Messages lost since the start.</summary>
        public int LostCount { get; private set; }

        /// <summary>
        /// Sends a copy of <paramref name="message"/> at <paramref name="nowSeconds"/>; it comes back
        /// <see cref="DelaySeconds"/> later (plus jitter), unless <paramref name="mayBeLost"/> (an unreliable message)
        /// and it is lost.
        /// </summary>
        public void Send(byte[] message, double nowSeconds, bool mayBeLost = false)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            // Both draws every time, so the jitter of later messages does not depend on which ones may be lost.
            var jitter = _random.NextDouble() * JitterSeconds;
            var isLost = _random.NextDouble() < LossFraction;
            if (mayBeLost && isLost)
            {
                LostCount++;
                return;
            }

            var due = Math.Max(_lastDueSeconds, nowSeconds + DelaySeconds + jitter);
            _lastDueSeconds = due;
            _inFlight.Enqueue(new InFlight((byte[])message.Clone(), due));
        }

        /// <summary>The messages due back by <paramref name="nowSeconds"/>, oldest first; each comes back once.</summary>
        public List<byte[]> TakeDue(double nowSeconds)
        {
            var due = new List<byte[]>();
            while (_inFlight.Count > 0 && _inFlight.Peek().DueSeconds <= nowSeconds)
            {
                due.Add(_inFlight.Dequeue().Message);
            }

            return due;
        }

        /// <summary>
        /// A new delay. Returns true when it changed: the messages on their way are dropped, since a receiver that kept
        /// its clock and streams across a jump in the delay would draw from the wrong time. The caller starts the link
        /// over, as on a new connection.
        /// </summary>
        public bool SetDelay(double delaySeconds)
        {
            var checkedDelay = CheckDelay(delaySeconds);
            if (checkedDelay == DelaySeconds)
            {
                return false;
            }

            DelaySeconds = checkedDelay;
            Clear();
            return true;
        }

        /// <summary>
        /// New jitter and loss (PER-145). Returns true when they changed; the messages on their way keep their times.
        /// </summary>
        public bool SetNoise(double jitterSeconds, double lossFraction)
        {
            var jitter = CheckDelay(jitterSeconds);
            if (!(lossFraction >= 0.0 && lossFraction <= 1.0))
            {
                throw new ArgumentOutOfRangeException(nameof(lossFraction), lossFraction, "A loss is a share from 0 to 1.");
            }

            if (jitter == JitterSeconds && lossFraction == LossFraction)
            {
                return false;
            }

            JitterSeconds = jitter;
            LossFraction = lossFraction;
            return true;
        }

        /// <summary>Drops the messages on their way (the mirror ended).</summary>
        public void Clear()
        {
            _inFlight.Clear();
            _lastDueSeconds = double.NegativeInfinity;
        }

        private static double CheckDelay(double delaySeconds)
        {
            if (!(delaySeconds >= 0.0) || double.IsInfinity(delaySeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(delaySeconds), delaySeconds, "A delay must be zero or more seconds.");
            }

            return delaySeconds;
        }

        private readonly struct InFlight
        {
            public InFlight(byte[] message, double dueSeconds)
            {
                Message = message;
                DueSeconds = dueSeconds;
            }

            public byte[] Message { get; }

            public double DueSeconds { get; }
        }
    }
}
