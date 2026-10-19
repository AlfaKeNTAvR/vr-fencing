using System;
using System.Numerics;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Blade
{
    /// <summary>
    /// What counts as a Touch. There is no force sensing in VR, so the épée's "point pressed along its axis past
    /// 750 g" is stood in for by direction limits (the tip must move into the surface, and the blade must point into it)
    /// and, with the realistic Touch pressure (PER-50), by how far the point is pushed in or how fast it arrives.
    ///
    /// The 750 g estimate: a real épée blade is far stiffer along its axis than 750 g (about 7.4 N). From the FIE
    /// flexibility test (4.5-7 cm of tip bend under 200 g, clamped 70 cm from the point) its bending stiffness EI is about
    /// 4 N m^2, so a blade clamped at the guard and pinned at the point buckles at 20.19 EI / L^2, about 100 N. At 7.4 N
    /// it has not bent at all: the 750 g is taken by the point's own spring, which closes after about 1 mm of travel.
    /// Tracked hands jitter by about 3.5 mm, so the point pushed <see cref="RealisticPressDepthMeters"/> into the
    /// surface stands for it. A fast arrival presses it at once: the épée's mass at the point (about 0.15 kg, a fifth of
    /// a 770 g weapon) stopped by the point's spring in about 10 ms gives 7.4 N at <see cref="RealisticImpactMetersPerSecond"/>.
    /// </summary>
    public readonly struct TouchRules
    {
        /// <summary>The point pushed this far into the surface stands for 750 g on a real épée's point (see the summary).</summary>
        public const float RealisticPressDepthMeters = 0.005f;

        /// <summary>A point arriving this fast into the surface presses past 750 g on impact (see the summary).</summary>
        public const float RealisticImpactMetersPerSecond = 0.5f;

        public TouchRules(
            float tipRadiusMeters,
            float bladeRadiusMeters,
            float tipZoneLengthMeters,
            float minNormalSpeedMetersPerSecond,
            float maxApproachAngleDegrees,
            float maxBladeAxisAngleDegrees,
            float pressDepthMeters = 0.0f,
            float impactMetersPerSecond = 0.0f)
        {
            PressDepthMeters = pressDepthMeters;
            ImpactMetersPerSecond = impactMetersPerSecond;
            TipRadiusMeters = tipRadiusMeters;
            BladeRadiusMeters = bladeRadiusMeters;
            TipZoneLengthMeters = tipZoneLengthMeters;
            MinNormalSpeedMetersPerSecond = minNormalSpeedMetersPerSecond;
            MaxApproachAngleDegrees = maxApproachAngleDegrees;
            MaxBladeAxisAngleDegrees = maxBladeAxisAngleDegrees;
        }

        /// <summary>Strict (PER-22): motion within 40 degrees and blade within 45 degrees of straight in; scores on first contact.</summary>
        public static TouchRules Strict => new TouchRules(0.004f, 0.006f, 0.03f, 0.05f, 40.0f, 45.0f);

        /// <summary>Loose (PER-22): motion within 70 degrees and blade within 75 degrees of straight in; scores on first contact.</summary>
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

        /// <summary>
        /// How far past touching the surface the point must be pushed before a point-first arrival scores; 0 scores on
        /// first contact.
        /// </summary>
        public float PressDepthMeters { get; }

        /// <summary>A point-first arrival at least this fast into the surface scores at once, pressed by the impact. Unused when <see cref="PressDepthMeters"/> is 0.</summary>
        public float ImpactMetersPerSecond { get; }

        /// <summary>True when a Touch needs the point pressed (<see cref="PressDepthMeters"/> above 0), not just a first contact.</summary>
        public bool NeedsPressure => PressDepthMeters > 0.0f;

        /// <summary>The rule's limits with the given Touch pressure.</summary>
        public TouchRules WithPressure(TouchPressure pressure)
        {
            var isRealistic = pressure == TouchPressure.Realistic;
            return new TouchRules(TipRadiusMeters, BladeRadiusMeters, TipZoneLengthMeters, MinNormalSpeedMetersPerSecond,
                MaxApproachAngleDegrees, MaxBladeAxisAngleDegrees,
                isRealistic ? RealisticPressDepthMeters : 0.0f,
                isRealistic ? RealisticImpactMetersPerSecond : 0.0f);
        }

        public static TouchRules For(TouchRule rule, TouchPressure pressure)
        {
            return For(rule).WithPressure(pressure);
        }

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
        /// <summary>A valid Touch: the point arrived first, moving into the zone with the blade pointing in, and (with the realistic Touch pressure) pressed.</summary>
        Touch,

        /// <summary>The point reached the zone but sideways, with the blade not pointing in, too slowly, or (realistic Touch pressure) left without being pressed. Not a Touch.</summary>
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
            float distanceFromZoneCenterMeters,
            double timeSeconds = 0.0,
            double firstContactSeconds = 0.0)
        {
            TimeSeconds = timeSeconds;
            FirstContactSeconds = firstContactSeconds;
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

        /// <summary>When the event counts, in the detector's clock: for a pressed Touch, when the pressure was reached.</summary>
        public double TimeSeconds { get; }

        /// <summary>When the point (or the blade body) first met the zone; earlier than <see cref="TimeSeconds"/> for a pressed Touch.</summary>
        public double FirstContactSeconds { get; }
    }
}
