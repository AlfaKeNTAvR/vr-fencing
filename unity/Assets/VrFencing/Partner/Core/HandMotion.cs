using System;
using System.Collections.Generic;
using System.Numerics;
using VrFencing.Settings;

namespace VrFencing.Partner
{
    /// <summary>
    /// The sender's estimate of its sword hand's velocity and angular velocity (PER-81), sent with every hand sample for
    /// the receiver's interpolation and prediction. Taken over the last <see cref="WindowSeconds"/>, not one frame: a
    /// 72 Hz frame is 14 ms, so the tracker's few millimeters of jitter alone would read as half a meter per second
    /// (the same reason the Touch detector looks back 40 ms, PER-22).
    /// </summary>
    public sealed class HandMotion
    {
        /// <summary>How far back the velocity is measured.</summary>
        public const double WindowSeconds = 0.04;

        /// <summary>Poses kept: enough for the window at any frame rate up to about 400 per second.</summary>
        private const int Capacity = 16;

        /// <summary>A gap longer than this (the controller lost, the app paused) starts over at rest.</summary>
        private const double GapSeconds = 0.2;

        private readonly List<(double TimeSeconds, TrackedPose Pose)> _history = new List<(double, TrackedPose)>(Capacity);

        public Vector3 Velocity { get; private set; }

        /// <summary>Radians per second about this axis (its length).</summary>
        public Vector3 AngularVelocity { get; private set; }

        /// <summary>Takes this frame's hand pose and updates the velocities.</summary>
        public void Add(double timeSeconds, TrackedPose pose)
        {
            if (_history.Count > 0 && (timeSeconds - _history[_history.Count - 1].TimeSeconds > GapSeconds || timeSeconds <= _history[_history.Count - 1].TimeSeconds))
            {
                _history.Clear();
            }

            if (_history.Count == Capacity)
            {
                _history.RemoveAt(0);
            }

            _history.Add((timeSeconds, pose));

            // The oldest pose still inside the window, or the oldest kept.
            var start = 0;
            while (start < _history.Count - 2 && timeSeconds - _history[start + 1].TimeSeconds >= WindowSeconds)
            {
                start++;
            }

            var from = _history[start];
            var span = timeSeconds - from.TimeSeconds;
            if (span <= 1e-6)
            {
                Velocity = Vector3.Zero;
                AngularVelocity = Vector3.Zero;
                return;
            }

            Velocity = (pose.Position - from.Pose.Position) / (float)span;
            AngularVelocity = GetRotationVector(from.Pose.Rotation, pose.Rotation) / (float)span;
        }

        public void Reset()
        {
            _history.Clear();
            Velocity = Vector3.Zero;
            AngularVelocity = Vector3.Zero;
        }

        /// <summary>The turn from <paramref name="from"/> to <paramref name="to"/> (applied on the left, in the world) as axis times angle in radians.</summary>
        public static Vector3 GetRotationVector(Quaternion from, Quaternion to)
        {
            var delta = Quaternion.Normalize(to * Quaternion.Inverse(from));
            if (delta.W < 0.0f)
            {
                // The short way round.
                delta = new Quaternion(-delta.X, -delta.Y, -delta.Z, -delta.W);
            }

            var axis = new Vector3(delta.X, delta.Y, delta.Z);
            var sinHalf = axis.Length();
            if (sinHalf < 1e-7f)
            {
                return Vector3.Zero;
            }

            var angle = 2.0f * (float)Math.Atan2(sinHalf, delta.W);
            return axis / sinHalf * angle;
        }
    }
}
