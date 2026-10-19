// Value types shared by the Touch detector and its callers.
using UnityEngine;

namespace VrFencing.Blade
{
    /// <summary>The blade as a segment for one frame: front of the guard to the point, world space, metres.</summary>
    public readonly struct BladeSegment
    {
        public readonly Vector3 GuardPosition;
        public readonly Vector3 TipPosition;

        public BladeSegment(Vector3 guardPosition, Vector3 tipPosition)
        {
            GuardPosition = guardPosition;
            TipPosition = tipPosition;
        }

        public static BladeSegment Lerp(BladeSegment from, BladeSegment to, float fraction)
        {
            return new BladeSegment(
                Vector3.Lerp(from.GuardPosition, to.GuardPosition, fraction),
                Vector3.Lerp(from.TipPosition, to.TipPosition, fraction));
        }
    }

    /// <summary>A spherical Target, world space, metres.</summary>
    public readonly struct SphereTarget
    {
        public readonly string Id;
        public readonly Vector3 Center;
        public readonly float Radius;

        public SphereTarget(string id, Vector3 center, float radius)
        {
            Id = id;
            Center = center;
            Radius = radius;
        }
    }

    /// <summary>
    /// What counts as a Touch. There is no force sensing in VR, so the épée's
    /// "point pressed along its axis past 750 g" is stood in for by direction limits:
    /// the tip must move into the surface, and the blade must point into it.
    /// </summary>
    public readonly struct TouchRules
    {
        /// <summary>Radius of the point's contact sphere (real crown is 8 mm across).</summary>
        public readonly float TipRadiusMeters;
        /// <summary>Radius of the blade body for flat contact.</summary>
        public readonly float BladeRadiusMeters;
        /// <summary>Length at the point end that counts as tip, not blade body, for flat contact.</summary>
        public readonly float TipZoneLengthMeters;
        /// <summary>Minimum tip speed along the inward surface normal at entry.</summary>
        public readonly float MinNormalSpeedMetersPerSecond;
        /// <summary>Largest angle between tip motion and the inward surface normal.</summary>
        public readonly float MaxApproachAngleDegrees;
        /// <summary>Largest angle between the blade (guard to point) and the inward surface normal.</summary>
        public readonly float MaxBladeAxisAngleDegrees;

        public TouchRules(float tipRadiusMeters, float bladeRadiusMeters, float tipZoneLengthMeters,
            float minNormalSpeedMetersPerSecond, float maxApproachAngleDegrees, float maxBladeAxisAngleDegrees)
        {
            TipRadiusMeters = tipRadiusMeters;
            BladeRadiusMeters = bladeRadiusMeters;
            TipZoneLengthMeters = tipZoneLengthMeters;
            MinNormalSpeedMetersPerSecond = minNormalSpeedMetersPerSecond;
            MaxApproachAngleDegrees = maxApproachAngleDegrees;
            MaxBladeAxisAngleDegrees = maxBladeAxisAngleDegrees;
        }

        public static TouchRules Strict => new(0.004f, 0.006f, 0.03f, 0.05f, 40f, 45f);
        public static TouchRules Loose => new(0.004f, 0.006f, 0.03f, 0.02f, 70f, 75f);
    }

    public enum BladeEventType
    {
        /// <summary>Valid Touch: point first, moving into the Target.</summary>
        Touch,
        /// <summary>The tip entered the Target but sideways or with the blade not pointing in.</summary>
        TipGlance,
        /// <summary>The blade body (not the tip) crossed the Target.</summary>
        FlatContact,
    }

    public readonly struct BladeEvent
    {
        public readonly BladeEventType Type;
        public readonly int TargetIndex;
        public readonly Vector3 ContactPoint;
        public readonly float TipSpeedMetersPerSecond;
        /// <summary>Angle between tip motion and the inward normal; NaN for flat contact.</summary>
        public readonly float ApproachAngleDegrees;
        /// <summary>Angle between the blade and the inward normal; NaN for flat contact.</summary>
        public readonly float BladeAxisAngleDegrees;

        public BladeEvent(BladeEventType type, int targetIndex, Vector3 contactPoint, float tipSpeedMetersPerSecond,
            float approachAngleDegrees, float bladeAxisAngleDegrees)
        {
            Type = type;
            TargetIndex = targetIndex;
            ContactPoint = contactPoint;
            TipSpeedMetersPerSecond = tipSpeedMetersPerSecond;
            ApproachAngleDegrees = approachAngleDegrees;
            BladeAxisAngleDegrees = bladeAxisAngleDegrees;
        }
    }
}
