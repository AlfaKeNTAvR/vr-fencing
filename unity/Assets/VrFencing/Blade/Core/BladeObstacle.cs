using System;
using System.Numerics;
using VrFencing.Dummy;

namespace VrFencing.Blade
{
    /// <summary>
    /// Something the drawn blade cannot pass (PER-48, PER-49): a capsule (a part of the Dummy), a plane with solid
    /// space under it (the floor), or a piece of another blade (the Armed Dummy's, PER-104). <see cref="BladeContactBend"/> and <see cref="DrawnBlade"/>
    /// only ask an obstacle how far a point is from its surface, which way is out, where a moving point meets it and
    /// where a point rests on it, so both kinds follow the same rule. Each has a friction coefficient for the pressed
    /// point (PER-69): the point holds while the blade's push on it stays inside the friction cone round the surface
    /// normal, and slides once the push leans further.
    /// Positions in any one frame (the Strip frame in the app), meters.
    /// </summary>
    public readonly struct BladeObstacle
    {
        /// <summary>
        /// Friction of the point on the floor: the point holds while the push leans less than about 19 degrees off
        /// straight into the floor, so a shallow press skids along it and a steep one pins.
        /// </summary>
        public const float FloorFriction = 0.35f;

        /// <summary>
        /// Friction of the point on the Dummy's jacket (every capsule): it grips, holding a push that leans up to about
        /// 35 degrees off the surface normal; a glancing press slides round the body.
        /// </summary>
        public const float JacketFriction = 0.7f;

        /// <summary>
        /// Friction of the point on another blade: steel on steel, low, so a point pressed on the Armed Dummy's blade
        /// slides along it unless pushed in nearly square (about 11 degrees off its normal or less).
        /// </summary>
        public const float BladeFriction = 0.2f;

        /// <summary>
        /// Half the width of the Armed Dummy's blade: its centerline and the drawn blade's keep this plus
        /// <see cref="DrawnBlade.BladeRadiusMeters"/> apart.
        /// </summary>
        public const float BladeRadiusMeters = 0.004f;

        private readonly bool _isPlane;
        private readonly bool _isBlade;
        private readonly Capsule _capsule;
        private readonly Vector3 _planePoint;
        private readonly Vector3 _planeNormal;
        private readonly float _friction;
        private readonly Vector3 _side;
        private readonly int _joints;

        private BladeObstacle(bool isPlane, Capsule capsule, Vector3 planePoint, Vector3 planeNormal, float friction)
            : this(isPlane, false, capsule, planePoint, planeNormal, friction, Vector3.Zero, 0)
        {
        }

        private BladeObstacle(bool isPlane, bool isBlade, Capsule capsule, Vector3 planePoint, Vector3 planeNormal, float friction, Vector3 side, int joints)
        {
            _isPlane = isPlane;
            _isBlade = isBlade;
            _capsule = capsule;
            _planePoint = planePoint;
            _planeNormal = planeNormal;
            _friction = friction;
            _side = side;
            _joints = joints;
        }

        /// <summary>
        /// The floor: everything below y = 0. In the Strip frame (and the room frame) y = 0 is the tracking floor,
        /// since tracking is floor-level and the En Garde Line lies on the floor.
        /// </summary>
        public static BladeObstacle Floor => FromPlane(Vector3.Zero, Vector3.UnitY);

        /// <summary>True for a plane (solid on one side); planes always hold where they overlap a capsule.</summary>
        public bool IsPlane => _isPlane;

        /// <summary>
        /// True for a piece of another blade (<see cref="FromBladePiece"/>): thin, so it is tested against the drawn
        /// blade's pieces, not only its points, and the drawn blade keeps to its side of it.
        /// </summary>
        public bool IsBlade => _isBlade;

        /// <summary>
        /// The point's friction coefficient on this surface: the pressed point holds while the part of the blade's push
        /// along the surface is at most this times the part into it.
        /// </summary>
        public float FrictionCoefficient => _friction;

        /// <summary>A capsule (a part of the Dummy), with the jacket's friction unless given.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The friction is negative.</exception>
        public static BladeObstacle FromCapsule(Capsule capsule, float friction = JacketFriction)
        {
            return new BladeObstacle(false, capsule, Vector3.Zero, Vector3.UnitY, CheckFriction(friction));
        }

        /// <summary>
        /// A plane through <paramref name="point"/>; the side <paramref name="outwardNormal"/> points to is free, the
        /// other solid. The floor's friction unless given.
        /// </summary>
        /// <exception cref="ArgumentException">The normal has no direction.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The friction is negative.</exception>
        public static BladeObstacle FromPlane(Vector3 point, Vector3 outwardNormal, float friction = FloorFriction)
        {
            if (outwardNormal.LengthSquared() < 1e-12f)
            {
                throw new ArgumentException("A plane needs a normal with a direction.", nameof(outwardNormal));
            }

            return new BladeObstacle(true, default, point, Vector3.Normalize(outwardNormal), CheckFriction(friction));
        }

        /// <summary>
        /// A straight piece of another blade (the Armed Dummy's, PER-104), <see cref="BladeRadiusMeters"/> thick, from
        /// <paramref name="start"/> (toward its guard) to <paramref name="end"/>. The drawn blade must stay on
        /// <paramref name="side"/> of it, the side it was on before the two met (see <see cref="BladeBind"/>): any of it
        /// found behind this blade (alongside it, past its axis, within <see cref="BladeBind.StripHalfWidthMeters"/>)
        /// got there through it, so a fast move between frames cannot carry it through. With no side (zero), it only
        /// keeps its distance.
        /// </summary>
        /// <param name="side">Which way from this blade the drawn blade is; made square to the piece.</param>
        /// <param name="startIsJoint">The start joins the blade's previous piece (it is not the blade's guard).</param>
        /// <param name="endIsJoint">The end joins the blade's next piece (it is not the blade's point).</param>
        /// <exception cref="ArgumentOutOfRangeException">The friction is negative.</exception>
        public static BladeObstacle FromBladePiece(Vector3 start, Vector3 end, Vector3 side, bool startIsJoint, bool endIsJoint, float friction = BladeFriction)
        {
            var axis = end - start;
            var square = axis.LengthSquared() > 1e-12f ? side - (Vector3.Dot(side, axis) / axis.LengthSquared() * axis) : side;
            var unitSide = square.LengthSquared() > 1e-10f ? Vector3.Normalize(square) : Vector3.Zero;
            var joints = (startIsJoint ? 1 : 0) | (endIsJoint ? 2 : 0);
            return new BladeObstacle(false, true, new Capsule(start, end, BladeRadiusMeters), Vector3.Zero, Vector3.UnitY, CheckFriction(friction), unitSide, joints);
        }

        /// <summary>
        /// True when the drawn blade's piece from <paramref name="start"/> to <paramref name="end"/> keeps
        /// <paramref name="marginMeters"/> out of this blade piece and is nowhere behind it (<see cref="FromBladePiece"/>).
        /// True for any obstacle that is not a blade piece.
        /// </summary>
        /// <param name="joints">Whether the drawn piece's start (bit 0) and end (bit 1) join more of the drawn blade.</param>
        public bool IsPieceClear(Vector3 start, Vector3 end, int joints, float marginMeters)
        {
            if (!_isBlade)
            {
                return true;
            }

            // A piece wholly before or past this one along its axis (by more than touching) is neither close nor
            // behind it: skip the closest-point work, which most of the drawn blade's pieces need not do.
            var axis = _capsule.End - _capsule.Start;
            var lengthSquared = axis.LengthSquared();
            if (lengthSquared > 1e-12f)
            {
                var length = (float)Math.Sqrt(lengthSquared);
                var startAlong = Vector3.Dot(start - _capsule.Start, axis) / length;
                var endAlong = Vector3.Dot(end - _capsule.Start, axis) / length;
                var reach = _capsule.RadiusMeters + marginMeters + (length * BladeCrossing.StripJointSlack);
                if (Math.Max(startAlong, endAlong) < -reach || Math.Min(startAlong, endAlong) > length + reach)
                {
                    return true;
                }
            }

            return FindPieceSeparation(start, end, joints, out _, out _) >= marginMeters - 1e-5f;
        }

        /// <summary>
        /// How far a straight piece from <paramref name="start"/> to <paramref name="end"/> is out of this blade piece's
        /// surface where the two come closest: negative when it is in it, or behind it (then by how far past its
        /// axis, plus its half-width). <paramref name="closest"/> is that spot on the given piece, at
        /// <paramref name="fraction"/> of the way from its start.
        /// </summary>
        /// <param name="joints">Whether the given piece's start (bit 0) and end (bit 1) join more of its blade.</param>
        public float FindPieceSeparation(Vector3 start, Vector3 end, int joints, out Vector3 closest, out float fraction)
        {
            BladeCrossing.FindClosest(start, end, joints, _capsule.Start, _capsule.End, _joints, out closest, out var onThis, out fraction, out _);
            var separation = Vector3.Distance(closest, onThis) - _capsule.RadiusMeters;
            if (_side != Vector3.Zero
                && BladeCrossing.TryFindLeastSide(start, end, _capsule.Start, _capsule.End, _joints, _side, BladeBind.StripHalfWidthMeters, out var leastSide, out var behind, out var behindFraction)
                && leastSide < 0.0f)
            {
                // Behind this blade (past its axis, in the strip behind it): passed through it, by this much.
                closest = behind;
                fraction = behindFraction;
                return leastSide - _capsule.RadiusMeters;
            }

            return separation;
        }

        public static implicit operator BladeObstacle(Capsule capsule)
        {
            return FromCapsule(capsule);
        }

        private static float CheckFriction(float friction)
        {
            if (!(friction >= 0.0f))
            {
                throw new ArgumentOutOfRangeException(nameof(friction), friction, "Friction cannot be negative.");
            }

            return friction;
        }

        /// <summary>
        /// False only when no point in the box from <paramref name="boxMin"/> to <paramref name="boxMax"/> can be within
        /// <paramref name="marginMeters"/> of a capsule or blade piece (its bounding sphere misses the box): a cheap test
        /// that skips far bodies without changing any answer. Always true for a plane.
        /// </summary>
        public bool IsNearBox(Vector3 boxMin, Vector3 boxMax, float marginMeters)
        {
            if (_isPlane)
            {
                return true;
            }

            var center = (_capsule.Start + _capsule.End) * 0.5f;
            var reach = (Vector3.Distance(_capsule.Start, _capsule.End) * 0.5f) + _capsule.RadiusMeters + marginMeters;
            var nearest = Vector3.Clamp(center, boxMin, boxMax);
            return Vector3.DistanceSquared(center, nearest) <= reach * reach;
        }

        /// <summary>Distance from a point to the surface: negative inside.</summary>
        public float FindDistance(Vector3 point)
        {
            return _isPlane
                ? Vector3.Dot(point - _planePoint, _planeNormal)
                : SegmentMath.FindDistanceToCapsule(point, _capsule);
        }

        /// <summary>Unit vector out of the surface nearest <paramref name="point"/>.</summary>
        public Vector3 FindOutwardNormal(Vector3 point)
        {
            return _isPlane ? _planeNormal : SegmentMath.FindOutwardNormal(point, _capsule);
        }

        /// <summary>The point moved along the surface normal to rest <paramref name="marginMeters"/> off the surface, on the side it is on (a plane has one free side).</summary>
        public Vector3 ProjectOntoSurface(Vector3 point, float marginMeters)
        {
            if (_isPlane)
            {
                return point + ((marginMeters - FindDistance(point)) * _planeNormal);
            }

            var onAxis = SegmentMath.FindClosestPointOnSegment(point, _capsule.Start, _capsule.End);
            return onAxis + (SegmentMath.FindOutwardNormal(point, _capsule) * (_capsule.RadiusMeters + marginMeters));
        }

        /// <summary>
        /// Where a blade running along <paramref name="tangent"/> touches the surface, on the side of
        /// <paramref name="near"/>, <paramref name="marginMeters"/> out of it: the spot whose normal is square to the
        /// blade (PER-69, the blade's side pressed on a body). A plane has no such single spot: the point is just moved
        /// onto it.
        /// </summary>
        public Vector3 FindTangentPoint(Vector3 near, Vector3 tangent, float marginMeters)
        {
            if (_isPlane)
            {
                return ProjectOntoSurface(near, marginMeters);
            }

            var onAxis = SegmentMath.FindClosestPointOnSegment(near, _capsule.Start, _capsule.End);
            var outward = SegmentMath.FindOutwardNormal(near, _capsule);
            var square = outward - (Vector3.Dot(outward, tangent) * tangent);
            var axis = _capsule.End - _capsule.Start;
            var isOnCylinder = axis.LengthSquared() > 1e-10f
                && Vector3.DistanceSquared(onAxis, _capsule.Start) > 1e-10f
                && Vector3.DistanceSquared(onAxis, _capsule.End) > 1e-10f;
            if (isOnCylinder)
            {
                // Round the cylinder only: the normal stays square to its axis.
                var axisDirection = Vector3.Normalize(axis);
                square -= Vector3.Dot(square, axisDirection) * axisDirection;
            }

            return square.LengthSquared() > 1e-10f
                ? onAxis + (Vector3.Normalize(square) * (_capsule.RadiusMeters + marginMeters))
                : ProjectOntoSurface(near, marginMeters);
        }

        /// <summary>
        /// A point closer than <paramref name="marginMeters"/> to the surface moved out to that margin; any other point
        /// as it is. A capsule pushes it out toward <paramref name="previous"/> (the side it was on), a plane straight up
        /// its normal.
        /// </summary>
        public Vector3 PushOut(Vector3 point, float marginMeters, Vector3? previous)
        {
            if (_isBlade && _side != Vector3.Zero)
            {
                // Another blade: out on the side the drawn blade keeps to, also from a point across it on the other side.
                var onBlade = SegmentMath.FindClosestPointOnSegment(point, _capsule.Start, _capsule.End);
                var isOnSide = Vector3.Dot(point - onBlade, _side) >= 0.0f;
                return isOnSide && FindDistance(point) >= marginMeters - 1e-5f
                    ? point
                    : onBlade + (_side * (_capsule.RadiusMeters + marginMeters));
            }

            if (FindDistance(point) >= marginMeters - 1e-5f)
            {
                return point;
            }

            if (_isPlane || !previous.HasValue)
            {
                return ProjectOntoSurface(point, marginMeters);
            }

            var onAxis = SegmentMath.FindClosestPointOnSegment(point, _capsule.Start, _capsule.End);
            var fromPrevious = previous.Value - onAxis;
            var outward = fromPrevious.LengthSquared() > 1e-10f
                ? Vector3.Normalize(fromPrevious)
                : SegmentMath.FindOutwardNormal(point, _capsule);
            return onAxis + (outward * (_capsule.RadiusMeters + marginMeters));
        }

        /// <summary>
        /// Where a point moving in a straight line from <paramref name="start"/> to <paramref name="end"/> first comes
        /// within <paramref name="marginMeters"/> of the surface. False when the start is already that close (not a new
        /// entry) or the path never gets that close. The whole path is tested, so a fast point cannot tunnel through.
        /// </summary>
        /// <param name="entryFraction">0 at start, 1 at end.</param>
        public bool TryFindEntry(Vector3 start, Vector3 end, float marginMeters, out float entryFraction)
        {
            if (!_isPlane)
            {
                return SegmentMath.TryFindCapsuleEntry(start, end, _capsule, marginMeters, out entryFraction);
            }

            entryFraction = 0.0f;
            var startDistance = FindDistance(start);
            var endDistance = FindDistance(end);
            if (startDistance <= marginMeters || endDistance > marginMeters)
            {
                return false;
            }

            entryFraction = (startDistance - marginMeters) / (startDistance - endDistance);
            return true;
        }
    }
}
