using System;
using System.Numerics;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Blade
{
    /// <summary>
    /// What counts as a Touch. There is no force sensing in VR, so the épée's "point pressed along its axis past
    /// 750 g" is stood in for by direction limits: the tip must move into the surface, and the blade must point into it.
    /// </summary>
    public readonly struct TouchRules
    {
        public TouchRules(
            float tipRadiusMeters,
            float bladeRadiusMeters,
            float tipZoneLengthMeters,
            float minNormalSpeedMetersPerSecond,
            float maxApproachAngleDegrees,
            float maxBladeAxisAngleDegrees)
        {
            TipRadiusMeters = tipRadiusMeters;
            BladeRadiusMeters = bladeRadiusMeters;
            TipZoneLengthMeters = tipZoneLengthMeters;
            MinNormalSpeedMetersPerSecond = minNormalSpeedMetersPerSecond;
            MaxApproachAngleDegrees = maxApproachAngleDegrees;
            MaxBladeAxisAngleDegrees = maxBladeAxisAngleDegrees;
        }

        /// <summary>Strict (PER-22): motion within 40 degrees and blade within 45 degrees of straight in.</summary>
        public static TouchRules Strict => new TouchRules(0.004f, 0.006f, 0.03f, 0.05f, 40.0f, 45.0f);

        /// <summary>Loose (PER-22): motion within 70 degrees and blade within 75 degrees of straight in.</summary>
        public static TouchRules Loose => new TouchRules(0.004f, 0.006f, 0.03f, 0.02f, 70.0f, 75.0f);

        /// <summary>Radius of the point's contact sphere (a real point is 8 mm across).</summary>
        public float TipRadiusMeters { get; }

        /// <summary>Radius of the blade body, for flat contact.</summary>
        public float BladeRadiusMeters { get; }

        /// <summary>Length at the point end that counts as tip, not blade body, for flat contact.</summary>
        public float TipZoneLengthMeters { get; }

        /// <summary>Minimum tip speed along the inward surface normal at entry.</summary>
        public float MinNormalSpeedMetersPerSecond { get; }

        /// <summary>Largest angle between the tip's approach and the inward surface normal.</summary>
        public float MaxApproachAngleDegrees { get; }

        /// <summary>Largest angle between the blade (guard to point) and the inward surface normal.</summary>
        public float MaxBladeAxisAngleDegrees { get; }

        public static TouchRules For(TouchRule rule)
        {
            switch (rule)
            {
                case TouchRule.Strict:
                    return Strict;
                case TouchRule.Loose:
                    return Loose;
                default:
                    throw new ArgumentOutOfRangeException(nameof(rule), rule, "Not a Touch rule.");
            }
        }
    }

    public enum BladeEventType
    {
        /// <summary>A valid Touch: the point arrived first, moving into the zone with the blade pointing in.</summary>
        Touch,

        /// <summary>The point reached the zone but sideways, with the blade not pointing in, or too slowly. Not a Touch.</summary>
        TipGlance,

        /// <summary>The blade body (not the point) crossed the zone.</summary>
        FlatContact,
    }

    /// <summary>One blade contact with a Target Zone. Positions are in the frame the detector was fed (the Strip frame in the app).</summary>
    public readonly struct BladeEvent
    {
        public BladeEvent(
            BladeEventType type,
            TargetZone zone,
            Vector3 contactPoint,
            float tipSpeedMetersPerSecond,
            float approachAngleDegrees,
            float bladeAxisAngleDegrees,
            float distanceFromZoneCenterMeters)
        {
            Type = type;
            Zone = zone;
            ContactPoint = contactPoint;
            TipSpeedMetersPerSecond = tipSpeedMetersPerSecond;
            ApproachAngleDegrees = approachAngleDegrees;
            BladeAxisAngleDegrees = bladeAxisAngleDegrees;
            DistanceFromZoneCenterMeters = distanceFromZoneCenterMeters;
        }

        public BladeEventType Type { get; }

        public TargetZone Zone { get; }

        /// <summary>Where on the zone's surface the contact happened.</summary>
        public Vector3 ContactPoint { get; }

        /// <summary>The tip's speed over the approach window before the contact.</summary>
        public float TipSpeedMetersPerSecond { get; }

        /// <summary>Angle between the tip's approach and the inward surface normal; for a flat contact, the motion of the blade point nearest the contact. 180 with no motion, never NaN.</summary>
        public float ApproachAngleDegrees { get; }

        /// <summary>Angle between the blade (guard to point) and the inward surface normal, 0 to 180 (about 90 for a flat contact).</summary>
        public float BladeAxisAngleDegrees { get; }

        /// <summary>From the contact point to the middle of the zone's capsule axis.</summary>
        public float DistanceFromZoneCenterMeters { get; }
    }
}
