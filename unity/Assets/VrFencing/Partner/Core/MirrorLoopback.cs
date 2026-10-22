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

    /// <summary>
    /// The pretend network of a "Mirror me" session (PER-140): every partner message this app would send goes in, and
    /// comes out <see cref="DelaySeconds"/> later, in the order it went in, to be received by this app's own
    /// <see cref="OpponentLink"/> as if the partner had sent it. Each message is copied going in, so what comes out is
    /// only the bytes, like a real link. Nothing is lost or reordered (a perfect link with a fixed delay).
    /// </summary>
    public sealed class MirrorLoopback
    {
        private readonly Queue<InFlight> _inFlight = new Queue<InFlight>();

        public MirrorLoopback(double delaySeconds)
        {
            DelaySeconds = CheckDelay(delaySeconds);
        }

        /// <summary>How long each message takes to come back.</summary>
        public double DelaySeconds { get; private set; }

        /// <summary>Messages sent and not yet come back.</summary>
        public int InFlightCount => _inFlight.Count;

        /// <summary>Sends a copy of <paramref name="message"/> at <paramref name="nowSeconds"/>; it comes back <see cref="DelaySeconds"/> later.</summary>
        public void Send(byte[] message, double nowSeconds)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            _inFlight.Enqueue(new InFlight((byte[])message.Clone(), nowSeconds + DelaySeconds));
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
            _inFlight.Clear();
            return true;
        }

        /// <summary>Drops the messages on their way (the mirror ended).</summary>
        public void Clear()
        {
            _inFlight.Clear();
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
