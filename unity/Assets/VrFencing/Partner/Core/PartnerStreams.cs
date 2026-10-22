using System;
using System.Collections.Generic;
using System.Numerics;
using VrFencing.Settings;

namespace VrFencing.Partner
{
    /// <summary>
    /// The partner's clock as seen here (PER-81): every stream message carries the sender's time, and this keeps the
    /// smallest gap seen between arrival time here and send time there. That gap is the clock difference plus the
    /// quickest trip, so "the partner's now" is read as the send time of a message that would arrive right now by the
    /// quickest trip. The gap may creep up slowly (<see cref="DriftSecondsPerSecond"/>) so a clock that drifts, or a
    /// route that got slower for good, is followed; a jump larger than <see cref="ResyncSeconds"/> starts over.
    /// </summary>
    public sealed class RemoteClock
    {
        /// <summary>How fast the kept gap may grow: 1 ms per second, far above any crystal's drift.</summary>
        public const double DriftSecondsPerSecond = 0.001;

        /// <summary>Messages this much later than the kept gap say the partner's clock jumped (a restart): start over.</summary>
        public const double ResyncSeconds = 1.0;

        private double? _offsetSeconds;
        private double _lastLocalSeconds;

        public bool IsSynced => _offsetSeconds.HasValue;

        /// <summary>A message sent at <paramref name="senderSeconds"/> arrived at <paramref name="localSeconds"/>.</summary>
        public void Observe(double senderSeconds, double localSeconds)
        {
            var gap = localSeconds - senderSeconds;
            if (!_offsetSeconds.HasValue || gap - _offsetSeconds.Value > ResyncSeconds)
            {
                _offsetSeconds = gap;
            }
            else
            {
                var crept = _offsetSeconds.Value + (Math.Max(0.0, localSeconds - _lastLocalSeconds) * DriftSecondsPerSecond);
                _offsetSeconds = Math.Min(crept, gap);
            }

            _lastLocalSeconds = localSeconds;
        }

        /// <summary>The partner's time now, or null before the first message.</summary>
        public double? ToSenderTime(double localSeconds)
        {
            return localSeconds - _offsetSeconds;
        }

        public void Reset()
        {
            _offsetSeconds = null;
        }
    }

    /// <summary>
    /// The partner's sword hand as drawn here (PER-81, decided in PER-59): interpolation plus a small, view-only forward
    /// prediction. Samples arrive 60 to 72 per second with their velocity; the hand is drawn
    /// <see cref="BufferSeconds"/> behind the partner's now, Hermite-interpolated between the two samples around that time
    /// (so a late message rarely leaves a gap), then carried <see cref="PredictionSeconds"/> ahead along its velocity, at
    /// most <see cref="MaxPredictionMeters"/> and <see cref="MaxPredictionDegrees"/>. When messages stop coming the last
    /// sample is held and predicted, and after <see cref="StaleSeconds"/> the hand counts as lost. Never used to judge a
    /// Touch: the attacker's own headset judges its own Touches.
    /// </summary>
    public sealed class HandStream
    {
        /// <summary>
        /// How far behind the partner's now the hand is interpolated: about 3 samples at 72 per second, enough to ride out
        /// home Wi-Fi jitter.
        /// </summary>
        public const double BufferSeconds = 0.04;

        /// <summary>How far ahead the drawn hand is carried along its velocity (PER-59: up to about 50 ms).</summary>
        public const double PredictionSeconds = 0.05;

        /// <summary>The prediction never moves the hand further than this (PER-59: capped at 15 cm).</summary>
        public const float MaxPredictionMeters = 0.15f;

        /// <summary>The prediction never turns the hand further than this, so a fast wrist turn cannot fling the point.</summary>
        public const float MaxPredictionDegrees = 30.0f;

        /// <summary>With no message for this long, the hand is lost (the controller not tracked, or the link dropped).</summary>
        public const double StaleSeconds = 0.5;

        /// <summary>Samples kept: about half a second at 72 per second.</summary>
        public const int Capacity = 36;

        private readonly List<HandSample> _samples = new List<HandSample>(Capacity);

        public int Count => _samples.Count;

        /// <summary>The newest sample, for its grip settings; null before the first.</summary>
        public HandSample? Newest => _samples.Count > 0 ? _samples[_samples.Count - 1] : (HandSample?)null;

        /// <summary>Keeps a sample; one older than the newest (it came late, out of order) is dropped.</summary>
        public void Add(HandSample sample)
        {
            if (_samples.Count > 0 && sample.SenderTimeSeconds <= _samples[_samples.Count - 1].SenderTimeSeconds)
            {
                return;
            }

            if (_samples.Count == Capacity)
            {
                _samples.RemoveAt(0);
            }

            _samples.Add(sample);
        }

        public void Clear()
        {
            _samples.Clear();
        }

        /// <summary>
        /// The controller's pose to draw when the partner's clock reads <paramref name="senderNowSeconds"/>, or false
        /// when there is no sample or the newest is older than <see cref="StaleSeconds"/>.
        /// </summary>
        public bool TrySample(double senderNowSeconds, out TrackedPose pose)
        {
            pose = default;
            if (_samples.Count == 0 || senderNowSeconds - _samples[_samples.Count - 1].SenderTimeSeconds > StaleSeconds)
            {
                return false;
            }

            Interpolate(senderNowSeconds - BufferSeconds, out var basePose, out var velocity, out var angularVelocity);
            pose = Predict(basePose, velocity, angularVelocity, PredictionSeconds);
            return true;
        }

        /// <summary>
        /// The controller's pose at <paramref name="timeSeconds"/> on the partner's clock, interpolated only (held at the
        /// ends), e.g. to line the body's sword hand up with the blade. False with no sample.
        /// </summary>
        public bool TryInterpolate(double timeSeconds, out TrackedPose pose)
        {
            pose = default;
            if (_samples.Count == 0)
            {
                return false;
            }

            Interpolate(timeSeconds, out pose, out _, out _);
            return true;
        }

        /// <summary>
        /// <paramref name="pose"/> carried <paramref name="seconds"/> along its velocity and angular velocity, the move
        /// capped at <see cref="MaxPredictionMeters"/> and the turn at <see cref="MaxPredictionDegrees"/>.
        /// </summary>
        public static TrackedPose Predict(TrackedPose pose, Vector3 velocity, Vector3 angularVelocity, double seconds)
        {
            var move = velocity * (float)seconds;
            var moveLength = move.Length();
            if (moveLength > MaxPredictionMeters)
            {
                move *= MaxPredictionMeters / moveLength;
            }

            var turnRadians = angularVelocity.Length() * (float)seconds;
            var rotation = pose.Rotation;
            if (turnRadians > 1e-6f)
            {
                var cappedRadians = Math.Min(turnRadians, MaxPredictionDegrees * (float)(Math.PI / 180.0));
                var turn = Quaternion.CreateFromAxisAngle(Vector3.Normalize(angularVelocity), cappedRadians);
                rotation = Quaternion.Normalize(turn * rotation);
            }

            return new TrackedPose(pose.Position + move, rotation);
        }

        /// <summary>The pose and velocities at <paramref name="timeSeconds"/>, held at the first or last sample outside the buffer.</summary>
        private void Interpolate(double timeSeconds, out TrackedPose pose, out Vector3 velocity, out Vector3 angularVelocity)
        {
            var first = _samples[0];
            var last = _samples[_samples.Count - 1];
            if (timeSeconds <= first.SenderTimeSeconds || _samples.Count == 1)
            {
                Hold(timeSeconds <= first.SenderTimeSeconds ? first : last, out pose, out velocity, out angularVelocity);
                return;
            }

            if (timeSeconds >= last.SenderTimeSeconds)
            {
                Hold(last, out pose, out velocity, out angularVelocity);
                return;
            }

            var after = 1;
            while (_samples[after].SenderTimeSeconds < timeSeconds)
            {
                after++;
            }

            var a = _samples[after - 1];
            var b = _samples[after];
            var span = (float)(b.SenderTimeSeconds - a.SenderTimeSeconds);
            var t = (float)((timeSeconds - a.SenderTimeSeconds) / span);
            var position = Hermite(a.Controller.Position, a.Velocity * span, b.Controller.Position, b.Velocity * span, t);
            pose = new TrackedPose(position, Quaternion.Slerp(a.Controller.Rotation, b.Controller.Rotation, t));
            velocity = Vector3.Lerp(a.Velocity, b.Velocity, t);
            angularVelocity = Vector3.Lerp(a.AngularVelocity, b.AngularVelocity, t);
        }

        private static void Hold(HandSample sample, out TrackedPose pose, out Vector3 velocity, out Vector3 angularVelocity)
        {
            pose = sample.Controller;
            velocity = sample.Velocity;
            angularVelocity = sample.AngularVelocity;
        }

        /// <summary>Cubic Hermite between two points with their tangents (velocity times the span), t from 0 to 1.</summary>
        public static Vector3 Hermite(Vector3 start, Vector3 startTangent, Vector3 end, Vector3 endTangent, float t)
        {
            var t2 = t * t;
            var t3 = t2 * t;
            return (start * ((2.0f * t3) - (3.0f * t2) + 1.0f))
                + (startTangent * (t3 - (2.0f * t2) + t))
                + (end * ((-2.0f * t3) + (3.0f * t2)))
                + (endTangent * (t3 - t2));
        }
    }

    /// <summary>
    /// The partner's body as drawn here (PER-81): 30 samples per second, drawn <see cref="BufferSeconds"/> behind the
    /// partner's now, each joint interpolated in a straight line between the two samples around that time. No
    /// prediction: the body's joints are not judged and a wrong guess shows more on a body than a blade. Held at the
    /// newest sample when messages stop; lost after <see cref="StaleSeconds"/>.
    /// </summary>
    public sealed class BodyStream
    {
        /// <summary>About 3 samples at 30 per second.</summary>
        public const double BufferSeconds = 0.1;

        /// <summary>With no body message for this long the body is drawn as lost (faded, see <see cref="OpponentFade"/>).</summary>
        public const double StaleSeconds = 0.5;

        public const int Capacity = 16;

        private readonly List<BodySample> _samples = new List<BodySample>(Capacity);

        public int Count => _samples.Count;

        public void Add(BodySample sample)
        {
            if (_samples.Count > 0 && sample.SenderTimeSeconds <= _samples[_samples.Count - 1].SenderTimeSeconds)
            {
                return;
            }

            if (_samples.Count == Capacity)
            {
                _samples.RemoveAt(0);
            }

            _samples.Add(sample);
        }

        public void Clear()
        {
            _samples.Clear();
        }

        /// <summary>True when the newest body is older than <see cref="StaleSeconds"/> (or there is none).</summary>
        public bool IsStale(double senderNowSeconds)
        {
            return _samples.Count == 0 || senderNowSeconds - _samples[_samples.Count - 1].SenderTimeSeconds > StaleSeconds;
        }

        /// <summary>
        /// The body to draw when the partner's clock reads <paramref name="senderNowSeconds"/>; null before the first
        /// sample. Its footwork comes from the samples <see cref="FootworkLookAhead.Seconds"/> later, which carry the feet
        /// for this moment (PER-142); past the newest sample, the newest one's.
        /// </summary>
        public FencerBody Sample(double senderNowSeconds)
        {
            if (_samples.Count == 0)
            {
                return null;
            }

            var time = senderNowSeconds - BufferSeconds;
            var body = Interpolate(time);
            var footwork = Interpolate(time + FootworkLookAhead.Seconds).Footwork;
            return body.WithFootwork(footwork ?? body.Footwork);
        }

        private FencerBody Interpolate(double time)
        {
            var first = _samples[0];
            var last = _samples[_samples.Count - 1];
            if (time <= first.SenderTimeSeconds)
            {
                return first.Body;
            }

            if (time >= last.SenderTimeSeconds)
            {
                return last.Body;
            }

            var after = 1;
            while (_samples[after].SenderTimeSeconds < time)
            {
                after++;
            }

            var a = _samples[after - 1];
            var b = _samples[after];
            var t = (float)((time - a.SenderTimeSeconds) / (b.SenderTimeSeconds - a.SenderTimeSeconds));
            return FencerBody.Lerp(a.Body, b.Body, t);
        }
    }

    /// <summary>Sends a stream at most at its rate (PER-59: body 30 per second, blade 60 to 72), whatever the frame rate.</summary>
    public sealed class SendSchedule
    {
        /// <summary>
        /// A frame a little early still sends: at 72 frames per second and a 72 per second rate, frame-time jitter would
        /// otherwise skip every few frames.
        /// </summary>
        public const double EarlyFraction = 0.25;

        private readonly double _intervalSeconds;
        private double _nextDueSeconds = double.NegativeInfinity;

        public SendSchedule(double ratePerSecond)
        {
            if (!(ratePerSecond > 0.0))
            {
                throw new ArgumentOutOfRangeException(nameof(ratePerSecond), ratePerSecond, "A rate must be positive.");
            }

            _intervalSeconds = 1.0 / ratePerSecond;
        }

        /// <summary>
        /// True (and counted as sent) when a message is due at <paramref name="nowSeconds"/>. Due times step by the
        /// interval, so the average rate holds at any frame rate; after a long gap (a pause) they start again from now.
        /// </summary>
        public bool TakeIfDue(double nowSeconds)
        {
            if (nowSeconds < _nextDueSeconds - (_intervalSeconds * EarlyFraction))
            {
                return false;
            }

            var next = _nextDueSeconds + _intervalSeconds;
            _nextDueSeconds = next < nowSeconds ? nowSeconds + _intervalSeconds : next;
            return true;
        }

        public void Reset()
        {
            _nextDueSeconds = double.NegativeInfinity;
        }
    }
}
