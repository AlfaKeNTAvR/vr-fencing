using System.Collections.Generic;

namespace VrFencing.Partner
{
    /// <summary>One message out of <see cref="ExtraLinkDelay"/>, and whether it must arrive.</summary>
    public readonly struct DelayedMessage
    {
        public DelayedMessage(byte[] message, bool isReliable)
        {
            Message = message;
            IsReliable = isReliable;
        }

        public byte[] Message { get; }

        public bool IsReliable { get; }
    }

    /// <summary>
    /// The PC Editor's slower link to a real partner (PER-145): every message this app sends or receives waits
    /// <see cref="DelaySeconds"/> more, each way, in the order it came, so a headset sees the Editor's Fencer over a link
    /// that much slower and the round trip grows by twice it (both legs alike, so the synced clock stays right). The
    /// Unity side puts what it would send or receive in, and sends or receives what comes out due. Nothing while the
    /// delay is 0: messages pass straight through.
    /// </summary>
    public sealed class ExtraLinkDelay
    {
        private readonly MirrorLoopback _reliable = new MirrorLoopback(0.0);
        private readonly MirrorLoopback _unreliable = new MirrorLoopback(0.0);
        private readonly MirrorLoopback _incoming = new MirrorLoopback(0.0);
        private readonly List<DelayedMessage> _due = new List<DelayedMessage>();

        /// <summary>The extra delay each way, seconds.</summary>
        public double DelaySeconds => _incoming.DelaySeconds;

        /// <summary>True while messages wait: a delay is set, or some are still on their way.</summary>
        public bool IsActive => DelaySeconds > 0.0 || _reliable.InFlightCount + _unreliable.InFlightCount + _incoming.InFlightCount > 0;

        /// <summary>A new delay each way; the messages on their way are dropped when it changes (as a new link).</summary>
        public void SetDelay(double delaySeconds)
        {
            _reliable.SetDelay(delaySeconds);
            _unreliable.SetDelay(delaySeconds);
            _incoming.SetDelay(delaySeconds);
        }

        /// <summary>A message this app sends at <paramref name="nowSeconds"/>.</summary>
        public void Send(byte[] message, bool isReliable, double nowSeconds)
        {
            (isReliable ? _reliable : _unreliable).Send(message, nowSeconds);
        }

        /// <summary>The messages due out by <paramref name="nowSeconds"/>: the reliable ones first, each in its order.</summary>
        public IReadOnlyList<DelayedMessage> TakeDueOutgoing(double nowSeconds)
        {
            _due.Clear();
            foreach (var message in _reliable.TakeDue(nowSeconds))
            {
                _due.Add(new DelayedMessage(message, true));
            }

            foreach (var message in _unreliable.TakeDue(nowSeconds))
            {
                _due.Add(new DelayedMessage(message, false));
            }

            return _due;
        }

        /// <summary>A message the partner sent, arrived here at <paramref name="nowSeconds"/>.</summary>
        public void Receive(byte[] message, double nowSeconds)
        {
            _incoming.Send(message, nowSeconds);
        }

        /// <summary>The received messages due in by <paramref name="nowSeconds"/>, in their order.</summary>
        public List<byte[]> TakeDueIncoming(double nowSeconds)
        {
            return _incoming.TakeDue(nowSeconds);
        }

        /// <summary>Drops everything on its way (the link changed or ended).</summary>
        public void Clear()
        {
            _reliable.Clear();
            _unreliable.Clear();
            _incoming.Clear();
        }
    }
}
