using System;
using System.Numerics;

namespace VrFencing.Dummy
{
    /// <summary>Rotation helpers for posing a rig (PER-142): shortest arcs, frames from two directions, twist about an axis.</summary>
    public static class RigMath
    {
        // Directions closer than this (as a dot product of unit vectors) are taken as the same or opposite.
        private const float ParallelDot = 0.999999f;

        private const float TwoPi = (float)(2.0 * Math.PI);

        /// <summary>
        /// The least rotation turning direction <paramref name="from"/> into <paramref name="to"/>. Opposite directions turn
        /// half a turn about an axis square to them (any; callers avoid relying on it by aiming from a nearby rest).
        /// </summary>
        public static Quaternion FromTo(Vector3 from, Vector3 to)
        {
            var a = Vector3.Normalize(from);
            var b = Vector3.Normalize(to);
            var dot = Vector3.Dot(a, b);
            if (dot >= ParallelDot)
            {
                return Quaternion.Identity;
            }

            if (dot <= -ParallelDot)
            {
                return Quaternion.CreateFromAxisAngle(GetPerpendicular(a), (float)Math.PI);
            }

            var axis = Vector3.Cross(a, b);
            return Quaternion.Normalize(new Quaternion(axis.X, axis.Y, axis.Z, 1.0f + dot));
        }

        /// <summary>
        /// The rotation whose +x is along <paramref name="xAxis"/> and whose +y is as near <paramref name="yHint"/> as it
        /// can be (square to +x); +z completes a right-handed set (x cross y).
        /// </summary>
        public static Quaternion FrameFromXY(Vector3 xAxis, Vector3 yHint)
        {
            var x = Vector3.Normalize(xAxis);
            var y = yHint - (x * Vector3.Dot(x, yHint));
            y = y.LengthSquared() > 1e-12f ? Vector3.Normalize(y) : GetPerpendicular(x);
            return FromBasis(x, y, Vector3.Cross(x, y));
        }

        /// <summary>
        /// The rotation whose +y is along <paramref name="up"/> and whose +x is as near <paramref name="right"/> as it can
        /// be; +z is right cross up (the body's forward when right and up are its own).
        /// </summary>
        public static Quaternion FrameFromRightUp(Vector3 right, Vector3 up)
        {
            var y = Vector3.Normalize(up);
            var z = Vector3.Cross(right, y);
            z = z.LengthSquared() > 1e-12f ? Vector3.Normalize(z) : GetPerpendicular(y);
            return FromBasis(Vector3.Cross(y, z), y, z);
        }

        /// <summary>The angle in radians, from -pi to pi, turning <paramref name="from"/> to <paramref name="to"/> about <paramref name="axis"/> (unit), each seen square to it.</summary>
        public static float GetSignedAngle(Vector3 from, Vector3 to, Vector3 axis)
        {
            var a = from - (axis * Vector3.Dot(axis, from));
            var b = to - (axis * Vector3.Dot(axis, to));
            return (float)Math.Atan2(Vector3.Dot(axis, Vector3.Cross(a, b)), Vector3.Dot(a, b));
        }

        /// <summary>The angle in radians, from -pi to pi, that <paramref name="rotation"/> turns about <paramref name="axis"/> (unit): its twist part.</summary>
        public static float GetTwist(Quaternion rotation, Vector3 axis)
        {
            var along = (rotation.X * axis.X) + (rotation.Y * axis.Y) + (rotation.Z * axis.Z);
            return WrapAngle(2.0f * (float)Math.Atan2(along, rotation.W));
        }

        /// <summary>The angle in radians wrapped into -pi to pi.</summary>
        public static float WrapAngle(float radians)
        {
            var wrapped = (float)Math.IEEERemainder(radians, TwoPi);
            return wrapped <= -Math.PI ? wrapped + TwoPi : wrapped;
        }

        /// <summary><paramref name="radians"/> moved by whole turns to lie nearest <paramref name="previousRadians"/>, so a twist that keeps turning never jumps a full turn back.</summary>
        public static float Unwrap(float radians, float previousRadians)
        {
            return previousRadians + WrapAngle(radians - previousRadians);
        }

        /// <summary>The angle in radians between two directions.</summary>
        public static float GetAngle(Vector3 a, Vector3 b)
        {
            var lengths = (float)Math.Sqrt(a.LengthSquared() * b.LengthSquared());
            return lengths > 1e-12f ? (float)Math.Acos(Math.Max(-1.0f, Math.Min(1.0f, Vector3.Dot(a, b) / lengths))) : 0.0f;
        }

        /// <summary>The rotation turning the unit axes onto <paramref name="x"/>, <paramref name="y"/> and <paramref name="z"/> (orthonormal, right-handed).</summary>
        public static Quaternion FromBasis(Vector3 x, Vector3 y, Vector3 z)
        {
            // System.Numerics transforms row vectors: each row is where that unit axis goes.
            var matrix = new Matrix4x4(x.X, x.Y, x.Z, 0.0f, y.X, y.Y, y.Z, 0.0f, z.X, z.Y, z.Z, 0.0f, 0.0f, 0.0f, 0.0f, 1.0f);
            return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(matrix));
        }

        /// <summary>A unit direction square to <paramref name="direction"/> (unit).</summary>
        public static Vector3 GetPerpendicular(Vector3 direction)
        {
            var other = Math.Abs(direction.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;
            return Vector3.Normalize(Vector3.Cross(direction, other));
        }
    }
}
