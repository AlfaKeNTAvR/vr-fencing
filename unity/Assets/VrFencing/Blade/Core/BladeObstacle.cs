using System;
using System.Numerics;
using VrFencing.Dummy;

namespace VrFencing.Blade
{
    /// <summary>
    /// Something the drawn blade cannot pass (PER-48, PER-49): a capsule (a part of the Dummy, another blade later)
    /// or a plane with solid space under it (the floor). <see cref="BladeContactBend"/> and <see cref="DrawnBlade"/>
    /// only ask an obstacle how far a point is from its surface, which way is out, where a moving point meets it and
    /// where a point rests on it, so both kinds follow the same rule.
    /// Positions in any one frame (the Strip frame in the app), meters.
    /// </summary>
    public readonly struct BladeObstacle
    {
        private readonly bool _isPlane;
        private readonly Capsule _capsule;
        private readonly Vector3 _planePoint;
        private readonly Vector3 _planeNormal;

        private BladeObstacle(bool isPlane, Capsule capsule, Vector3 planePoint, Vector3 planeNormal)
        {
            _isPlane = isPlane;
            _capsule = capsule;
            _planePoint = planePoint;
            _planeNormal = planeNormal;
        }

        /// <summary>
        /// The floor: everything below y = 0. In the Strip frame (and the room frame) y = 0 is the tracking floor,
        /// since tracking is floor-level and the En Garde Line lies on the floor.
        /// </summary>
        public static BladeObstacle Floor => FromPlane(Vector3.Zero, Vector3.UnitY);

        /// <summary>True for a plane (solid on one side); planes always hold where they overlap a capsule.</summary>
        public bool IsPlane => _isPlane;

        public static BladeObstacle FromCapsule(Capsule capsule)
        {
            return new BladeObstacle(false, capsule, Vector3.Zero, Vector3.UnitY);
        }

        /// <summary>A plane through <paramref name="point"/>; the side <paramref name="outwardNormal"/> points to is free, the other solid.</summary>
        /// <exception cref="ArgumentException">The normal has no direction.</exception>
        public static BladeObstacle FromPlane(Vector3 point, Vector3 outwardNormal)
        {
            if (outwardNormal.LengthSquared() < 1e-12f)
            {
                throw new ArgumentException("A plane needs a normal with a direction.", nameof(outwardNormal));
            }

            return new BladeObstacle(true, default, point, Vector3.Normalize(outwardNormal));
        }

        public static implicit operator BladeObstacle(Capsule capsule)
        {
            return FromCapsule(capsule);
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
        /// A point closer than <paramref name="marginMeters"/> to the surface moved out to that margin; any other point
        /// as it is. A capsule pushes it out toward <paramref name="previous"/> (the side it was on), a plane straight up
        /// its normal.
        /// </summary>
        public Vector3 PushOut(Vector3 point, float marginMeters, Vector3? previous)
        {
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
