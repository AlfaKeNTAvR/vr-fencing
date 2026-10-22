using System;
using System.Numerics;
using VrFencing.Dummy;

namespace VrFencing.Blade
{
    /// <summary>
    /// Something the drawn blade cannot pass (PER-48, PER-49): a capsule (a part of the Dummy), a plane with solid
    /// space under it (the floor), a piece of another blade (the Armed Dummy's, PER-104), or another blade's bell guard
    /// (PER-120). <see cref="BladeContactBend"/> and <see cref="DrawnBlade"/>
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
        /// Friction of the point on another blade: dry steel on steel (PER-116, up from 0.2), so a point pressed on the
        /// Armed Dummy's blade holds unless pushed more than about 19 degrees off its normal, instead of sliding off its end.
        /// </summary>
        public const float BladeFriction = 0.35f;

        /// <summary>
        /// Half the width of the Armed Dummy's blade: its centerline and the drawn blade's keep this plus
        /// <see cref="DrawnBlade.BladeRadiusMeters"/> apart.
        /// </summary>
        public const float BladeRadiusMeters = 0.004f;

        /// <summary>
        /// How far to either side of a blade piece the strip behind it reaches (<see cref="FromBladePiece"/>): touching
        /// plus 2 cm, so a fast move that also slips a little sideways is still caught.
        /// </summary>
        public const float StripHalfWidthMeters = DrawnBlade.BladeRadiusMeters + BladeRadiusMeters + 0.02f;

        // The bell's bounding sphere about its center: the rim's edge.
        private static readonly float BellBoundingRadiusMeters = (float)Math.Sqrt((BellGuard.RadiusMeters * BellGuard.RadiusMeters) + (BellGuard.DepthMeters * BellGuard.DepthMeters * 0.25f));

        private readonly ObstacleShape _shape;
        private readonly Capsule _capsule;

        // A plane's point and outward normal; a bell's center and the blade's direction through it (its front face is
        // toward the point).
        private readonly Vector3 _planePoint;
        private readonly Vector3 _planeNormal;
        private readonly float _friction;
        private readonly Vector3 _side;
        private readonly int _joints;

        private BladeObstacle(ObstacleShape shape, Capsule capsule, Vector3 planePoint, Vector3 planeNormal, float friction)
            : this(shape, capsule, planePoint, planeNormal, friction, Vector3.Zero, 0)
        {
        }

        private BladeObstacle(ObstacleShape shape, Capsule capsule, Vector3 planePoint, Vector3 planeNormal, float friction, Vector3 side, int joints)
        {
            _shape = shape;
            _capsule = capsule;
            _planePoint = planePoint;
            _planeNormal = planeNormal;
            _friction = friction;
            _side = side;
            _joints = joints;
        }

        private enum ObstacleShape
        {
            Capsule,
            Plane,
            BladePiece,
            Bell,
        }


        /// <summary>
        /// The floor: everything below y = 0. In the Strip frame (and the room frame) y = 0 is the tracking floor,
        /// since tracking is floor-level and the En Garde Line lies on the floor.
        /// </summary>
        public static BladeObstacle Floor => FromPlane(Vector3.Zero, Vector3.UnitY);

        /// <summary>True for a plane (solid on one side); planes always hold where they overlap a capsule.</summary>
        public bool IsPlane => _shape == ObstacleShape.Plane;

        /// <summary>
        /// True for a piece of another blade (<see cref="FromBladePiece"/>): thin, so it is tested against the drawn
        /// blade's pieces, not only its points, and the drawn blade keeps to its side of it.
        /// </summary>
        public bool IsBlade => _shape == ObstacleShape.BladePiece;

        /// <summary>True for another blade's bell guard (<see cref="FromBell"/>).</summary>
        public bool IsBell => _shape == ObstacleShape.Bell;

        /// <summary>
        /// True for another blade's piece or bell (steel, not a body): pressed past the bend limit, the drawn blade slides
        /// off it instead of wedging (PER-121).
        /// </summary>
        public bool IsSteel => IsBlade || IsBell;

        /// <summary>
        /// The point's friction coefficient on this surface: the pressed point holds while the part of the blade's push
        /// along the surface is at most this times the part into it.
        /// </summary>
        public float FrictionCoefficient => _friction;

        /// <summary>A capsule (a part of the Dummy), with the jacket's friction unless given.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The friction is negative.</exception>
        public static BladeObstacle FromCapsule(Capsule capsule, float friction = JacketFriction)
        {
            return new BladeObstacle(ObstacleShape.Capsule, capsule, Vector3.Zero, Vector3.UnitY, CheckFriction(friction));
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

            return new BladeObstacle(ObstacleShape.Plane, default, point, Vector3.Normalize(outwardNormal), CheckFriction(friction));
        }

        /// <summary>
        /// A straight piece of another blade (the Armed Dummy's, PER-104), <see cref="BladeRadiusMeters"/> thick, from
        /// <paramref name="start"/> (toward its guard) to <paramref name="end"/>. The drawn blade must stay on
        /// <paramref name="side"/> of it, the side it was on before the two met: any of it found behind this blade
        /// (alongside it, past its axis, within <see cref="StripHalfWidthMeters"/>) got there through it, so a fast move
        /// between frames cannot carry it through. With no side (zero), it only keeps its distance. Since PER-122 the
        /// Armed Dummy's blade is met through <see cref="BladeContactSolver"/> instead; nothing in the app adds these now.
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
            return new BladeObstacle(ObstacleShape.BladePiece, new Capsule(start, end, BladeRadiusMeters), Vector3.Zero, Vector3.UnitY, CheckFriction(friction), unitSide, joints);
        }

        /// <summary>
        /// Another blade's bell guard (<see cref="BellGuard"/>, the Armed Dummy's, PER-120): its front face on
        /// <paramref name="guard"/>, square to <paramref name="bladeDirection"/>. Solid steel, with a blade's friction
        /// unless given.
        /// </summary>
        /// <exception cref="ArgumentException">The blade direction has no direction.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The friction is negative.</exception>
        public static BladeObstacle FromBell(Vector3 guard, Vector3 bladeDirection, float friction = BladeFriction)
        {
            if (bladeDirection.LengthSquared() < 1e-12f)
            {
                throw new ArgumentException("A bell needs the blade's direction.", nameof(bladeDirection));
            }

            var axis = Vector3.Normalize(bladeDirection);
            return new BladeObstacle(ObstacleShape.Bell, default, BellGuard.FindCenter(guard, axis), axis, CheckFriction(friction));
        }

        /// <summary>
        /// True when the drawn blade's piece from <paramref name="start"/> to <paramref name="end"/> keeps
        /// <paramref name="marginMeters"/> out of this blade piece and is nowhere behind it (<see cref="FromBladePiece"/>).
        /// True for any obstacle that is not a blade piece.
        /// </summary>
        /// <param name="joints">Whether the drawn piece's start (bit 0) and end (bit 1) join more of the drawn blade.</param>
        public bool IsPieceClear(Vector3 start, Vector3 end, int joints, float marginMeters)
        {
            if (!IsBlade)
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
                && BladeCrossing.TryFindSideAtCrossing(start, end, joints, _capsule.Start, _capsule.End, _joints, _side, StripHalfWidthMeters, out var sideAtCrossing, out var behind, out var behindFraction)
                && sideAtCrossing < 0.0f)
            {
                // Behind this blade (past its axis, in the strip behind it): passed through it, by this much.
                closest = behind;
                fraction = behindFraction;
                return sideAtCrossing - _capsule.RadiusMeters;
            }

            return separation;
        }

        /// <summary>
        /// Which way is out of this obstacle on the side <paramref name="point"/> is on: for another blade, the side the
        /// drawn blade keeps to (<see cref="FromBladePiece"/>) when it has one; otherwise out of the surface nearest the
        /// point. The way a drawn blade pressed on it slides off to its near side (PER-121).
        /// </summary>
        public Vector3 FindNearSide(Vector3 point)
        {
            return IsBlade && _side != Vector3.Zero ? _side : FindOutwardNormal(point);
        }

        /// <summary>The unit direction a blade piece runs in, or a bell's axis (the blade's direction through it); zero for any other obstacle.</summary>
        public Vector3 FindSteelAxis()
        {
            if (IsBell)
            {
                return _planeNormal;
            }

            var axis = _capsule.End - _capsule.Start;
            return IsBlade && axis.LengthSquared() > 1e-12f ? Vector3.Normalize(axis) : Vector3.Zero;
        }

        /// <summary>
        /// True when the straight blade from <paramref name="start"/> to <paramref name="end"/> lies across this steel on
        /// its far side from the drawn blade, at any distance: a drawn blade put there would have gone through it
        /// (PER-121). For a blade piece, across it on the far side of the side the drawn blade keeps to
        /// (<see cref="FromBladePiece"/>); past the piece's end (round the blade's point) it is not across. For a bell,
        /// through the bell's depth with the bell between it and <paramref name="drawnNear"/>; wholly in front of or
        /// behind the bell (round it) it is not. False for any other obstacle, or a piece with no side.
        /// </summary>
        /// <param name="drawnNear">The drawn blade's point nearest this obstacle.</param>
        public bool IsAcrossOnFarSide(Vector3 start, Vector3 end, Vector3 drawnNear)
        {
            if (IsBell)
            {
                return IsAcrossBell(start, end, drawnNear);
            }

            if (!IsBlade || _side == Vector3.Zero)
            {
                return false;
            }

            var isAcross = BladeCrossing.FindClosest(start, end, 0, _capsule.Start, _capsule.End, _joints, out var onGiven, out var onThis, out _, out _);
            return isAcross && Vector3.Dot(onGiven - onThis, _side) < 0.0f;
        }

        /// <summary>
        /// The bell's part of <see cref="IsAcrossOnFarSide"/>: the straight blade passes within the bell's depth, and the
        /// line from <paramref name="drawnNear"/> to its point there nearest the bell's axis runs into the bell.
        /// </summary>
        private bool IsAcrossBell(Vector3 start, Vector3 end, Vector3 drawnNear)
        {
            var step = end - start;
            var low = 0.0f;
            var high = 1.0f;
            var halfDepth = BellGuard.DepthMeters * 0.5f;
            if (!BladeCrossing.ClipToSlab(Vector3.Dot(start - _planePoint, _planeNormal), Vector3.Dot(step, _planeNormal), -halfDepth, halfDepth, ref low, ref high))
            {
                return false;
            }

            var radialStart = (start - _planePoint) - (Vector3.Dot(start - _planePoint, _planeNormal) * _planeNormal);
            var radialStep = step - (Vector3.Dot(step, _planeNormal) * _planeNormal);
            var fraction = radialStep.LengthSquared() > 1e-10f
                ? -Vector3.Dot(radialStart, radialStep) / radialStep.LengthSquared()
                : (low + high) * 0.5f;
            var nearAxis = start + (step * Math.Clamp(fraction, low, high));
            return FindDistance(nearAxis) < 0.0f || TryFindEntry(drawnNear, nearAxis, 0.0f, out _);
        }

        public static implicit operator BladeObstacle(Capsule capsule)
        {
            return FromCapsule(capsule);
        }

        /// <summary>
        /// A point in the bell moved out of the face or rim <paramref name="previous"/> was off (the side it came from),
        /// <paramref name="marginMeters"/> clear: a blade pressed on its front face stays in front.
        /// </summary>
        private Vector3 PushOutOfBell(Vector3 point, float marginMeters, Vector3 previous)
        {
            BellGuard.FindDistance(previous, _planePoint, _planeNormal, out var cameFrom);
            var offset = point - _planePoint;
            var along = Vector3.Dot(offset, _planeNormal);
            var isOffFace = Math.Abs(Vector3.Dot(cameFrom, _planeNormal)) > 0.5f;
            if (isOffFace)
            {
                var face = Vector3.Dot(cameFrom, _planeNormal) > 0.0f ? _planeNormal : -_planeNormal;
                var halfDepth = BellGuard.DepthMeters * 0.5f;
                return point + (face * (halfDepth + marginMeters - Vector3.Dot(offset, face)));
            }

            var radial = offset - (along * _planeNormal);
            var outward = radial.LengthSquared() > 1e-12f ? Vector3.Normalize(radial) : cameFrom;
            return _planePoint + (along * _planeNormal) + (outward * (BellGuard.RadiusMeters + marginMeters));
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
        /// <paramref name="marginMeters"/> of a capsule, blade piece or bell (its bounding sphere misses the box): a cheap test
        /// that skips far bodies without changing any answer. Always true for a plane.
        /// </summary>
        public bool IsNearBox(Vector3 boxMin, Vector3 boxMax, float marginMeters)
        {
            if (IsPlane)
            {
                return true;
            }

            var center = IsBell ? _planePoint : (_capsule.Start + _capsule.End) * 0.5f;
            var reach = IsBell
                ? BellBoundingRadiusMeters + marginMeters
                : (Vector3.Distance(_capsule.Start, _capsule.End) * 0.5f) + _capsule.RadiusMeters + marginMeters;
            var nearest = Vector3.Clamp(center, boxMin, boxMax);
            return Vector3.DistanceSquared(center, nearest) <= reach * reach;
        }

        /// <summary>Distance from a point to the surface: negative inside.</summary>
        public float FindDistance(Vector3 point)
        {
            switch (_shape)
            {
                case ObstacleShape.Plane:
                    return Vector3.Dot(point - _planePoint, _planeNormal);
                case ObstacleShape.Bell:
                    return BellGuard.FindDistance(point, _planePoint, _planeNormal, out _);
                default:
                    return SegmentMath.FindDistanceToCapsule(point, _capsule);
            }
        }

        /// <summary>Unit vector out of the surface nearest <paramref name="point"/>.</summary>
        public Vector3 FindOutwardNormal(Vector3 point)
        {
            switch (_shape)
            {
                case ObstacleShape.Plane:
                    return _planeNormal;
                case ObstacleShape.Bell:
                    BellGuard.FindDistance(point, _planePoint, _planeNormal, out var outward);
                    return outward;
                default:
                    return SegmentMath.FindOutwardNormal(point, _capsule);
            }
        }

        /// <summary>The point moved along the surface normal to rest <paramref name="marginMeters"/> off the surface, on the side it is on (a plane has one free side).</summary>
        public Vector3 ProjectOntoSurface(Vector3 point, float marginMeters)
        {
            if (IsPlane)
            {
                return point + ((marginMeters - FindDistance(point)) * _planeNormal);
            }

            if (IsBell)
            {
                var distance = BellGuard.FindDistance(point, _planePoint, _planeNormal, out var outward);
                return point + ((marginMeters - distance) * outward);
            }

            var onAxis = SegmentMath.FindClosestPointOnSegment(point, _capsule.Start, _capsule.End);
            return onAxis + (SegmentMath.FindOutwardNormal(point, _capsule) * (_capsule.RadiusMeters + marginMeters));
        }

        /// <summary>
        /// Where a blade running along <paramref name="tangent"/> touches the surface, on the side of
        /// <paramref name="near"/>, <paramref name="marginMeters"/> out of it: the spot whose normal is square to the
        /// blade (PER-69, the blade's side pressed on a body). A plane or a bell's face has no such single spot: the
        /// point is just moved onto it.
        /// </summary>
        public Vector3 FindTangentPoint(Vector3 near, Vector3 tangent, float marginMeters)
        {
            if (IsPlane || IsBell)
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
        /// its normal, a bell out of the face or rim <paramref name="previous"/> was off.
        /// </summary>
        public Vector3 PushOut(Vector3 point, float marginMeters, Vector3? previous)
        {
            if (IsBlade && _side != Vector3.Zero)
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

            if (IsPlane || !previous.HasValue)
            {
                return ProjectOntoSurface(point, marginMeters);
            }

            if (IsBell)
            {
                return PushOutOfBell(point, marginMeters, previous.Value);
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
            if (IsBell)
            {
                return BellGuard.TryFindEntry(start, end, _planePoint, _planeNormal, marginMeters, out entryFraction);
            }

            if (!IsPlane)
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
