using System;
using System.Numerics;

namespace VrFencing.Blade
{
    /// <summary>
    /// How a blade bows where another blade presses on it (PER-122): the small-deflection shape of a blade clamped at the
    /// guard with a side load at one spot (the linear elastica). Up to the load it curves like
    /// x^2 (3a - x) / (2 a^3), past it it runs on straight, so a press near the guard swings the whole foible and a press
    /// near the point bends only the point. Up to two loads add up (two spots of a long bind). The default is no bow.
    /// Deflections are square to the blade, meters; places are fractions of the blade from the guard (0) to the point (1).
    /// </summary>
    public readonly struct ContactBow
    {
        // A load closer to the guard than this share of the blade is taken as here: the shape's tip factor grows as
        // 1 / fraction.
        private const float MinLoadFraction = 0.02f;

        public ContactBow(Vector3 first, float firstFraction, Vector3 second = default, float secondFraction = 1.0f)
        {
            First = first;
            FirstFraction = Math.Clamp(firstFraction, MinLoadFraction, 1.0f);
            Second = second;
            SecondFraction = Math.Clamp(secondFraction, MinLoadFraction, 1.0f);
        }

        /// <summary>The first load's deflection at its spot.</summary>
        public Vector3 First { get; }

        public float FirstFraction { get; }

        /// <summary>The second load's deflection at its spot (zero for none).</summary>
        public Vector3 Second { get; }

        public float SecondFraction { get; }

        /// <summary>True when the blade bows at all.</summary>
        public bool HasBow => First != Vector3.Zero || Second != Vector3.Zero;

        /// <summary>How far the point moves sideways.</summary>
        public Vector3 TipDeflection => (First * FindShape(1.0f, FirstFraction)) + (Second * FindShape(1.0f, SecondFraction));

        /// <summary>
        /// The blade's sideways move at <paramref name="fraction"/> of the way from the guard (0) to the point (1), for a
        /// load whose own move at <paramref name="loadFraction"/> is 1: 0 at the guard, 1 at the load.
        /// </summary>
        public static float FindShape(float fraction, float loadFraction)
        {
            var at = Math.Clamp(fraction, 0.0f, 1.0f);
            var load = Math.Clamp(loadFraction, MinLoadFraction, 1.0f);
            return at <= load
                ? at * at * ((3.0f * load) - at) / (2.0f * load * load * load)
                : ((3.0f * at) - load) / (2.0f * load);
        }

        /// <summary>The bow's sideways move at <paramref name="fraction"/> of the way along the blade.</summary>
        public Vector3 GetDeflection(float fraction)
        {
            return (First * FindShape(fraction, FirstFraction)) + (Second * FindShape(fraction, SecondFraction));
        }

        /// <summary>
        /// The bow's offset at <paramref name="fraction"/> from the line from the guard to the bowed point: zero at both
        /// ends.
        /// </summary>
        public Vector3 GetChordOffset(float fraction)
        {
            var at = Math.Clamp(fraction, 0.0f, 1.0f);
            return GetDeflection(at) - (TipDeflection * at);
        }
    }
}
