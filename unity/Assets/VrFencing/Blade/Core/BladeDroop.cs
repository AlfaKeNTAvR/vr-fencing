using System;
using System.Numerics;
using VrFencing.Settings;

namespace VrFencing.Blade
{
    /// <summary>
    /// The blade's rest shape (PER-52, from the PER-51 research): a real épée is not straight but hangs a little, so its
    /// point sits about 3 to 8 cm below the forte's line at 90 cm. Two sources of about the same size add up:
    /// <list type="bullet">
    /// <item>the <b>set</b>, a deliberate uniform curve fixed to the grip, toward the palm side (away from the thumb on
    /// top of the grip, FIE m.16.2: under 1 cm of sagitta). A sagitta h over the blade drops the point 4h, shaped
    /// 4h (z/L)^2;</item>
    /// <item><b>gravity sag</b> of the cantilever clamped at the guard, about 2 to 4.5 cm for a horizontal blade
    /// (stiffness from the FIE flexibility test), shaped like a cantilever under its own weight, (6u^2 - 4u^3 + u^4) / 3
    /// with u = z/L, and scaled by gravity across the blade, so none with the point straight up or down.</item>
    /// </list>
    /// Touch detection uses the line from the guard to the real (drooped) point, the chord, so a Touch lands where the
    /// drawn point is. The drawn blade is that chord plus <see cref="GetChordOffset"/>, so it leaves the guard along the
    /// forte. Pushed straight in, the blade bends to the side it already hangs to (<see cref="TipDeflection"/>, the set
    /// plus the sag), the PER-50 bend side. The default value is no droop: a straight blade.
    /// </summary>
    public readonly struct BladeDroop
    {
        /// <summary>Gravity sag of a horizontal blade for each Blade flex Setting: a stiff blade sags less (PER-51).</summary>
        public const float StiffSagMeters = 0.02f;

        /// <inheritdoc cref="StiffSagMeters"/>
        public const float NormalSagMeters = 0.03f;

        /// <inheritdoc cref="StiffSagMeters"/>
        public const float SoftSagMeters = 0.045f;

        private BladeDroop(Vector3 setDeflection, Vector3 sagDeflection, Quaternion chordToForte)
        {
            SetDeflection = setDeflection;
            SagDeflection = sagDeflection;
            ChordToForte = chordToForte;
            HasShape = true;
        }

        /// <summary>False for the default value: a straight blade.</summary>
        public bool HasShape { get; }

        /// <summary>The set's drop of the point, perpendicular to the forte.</summary>
        public Vector3 SetDeflection { get; }

        /// <summary>Gravity's drop of the point, perpendicular to the forte.</summary>
        public Vector3 SagDeflection { get; }

        /// <summary>Where the point hangs from the forte's straight line: the set plus the sag.</summary>
        public Vector3 TipDeflection => SetDeflection + SagDeflection;

        /// <summary>Turns the chord's direction onto the forte's.</summary>
        public Quaternion ChordToForte { get; }

        /// <summary>Gravity sag of a horizontal blade for the Blade flex Setting.</summary>
        public static float GetSagMeters(BladeFlexSoftness flex)
        {
            switch (flex)
            {
                case BladeFlexSoftness.Stiff: return StiffSagMeters;
                case BladeFlexSoftness.Soft: return SoftSagMeters;
                default: return NormalSagMeters;
            }
        }

        /// <summary>The droop for the Blade Settings' set and flex.</summary>
        /// <param name="forteDirection">The forte's direction: the blade's straight line from the guard.</param>
        /// <param name="gripUp">The grip's up (the thumb's side); the set curves the other way.</param>
        /// <param name="down">Gravity's direction, in the same frame.</param>
        public static BladeDroop Create(Vector3 forteDirection, Vector3 gripUp, Vector3 down, BladeSettings blade)
        {
            if (blade == null)
            {
                throw new ArgumentNullException(nameof(blade));
            }

            return Create(forteDirection, gripUp, down, blade.SetSagittaMeters, GetSagMeters(blade.Flex));
        }

        /// <param name="setSagittaMeters">The set's sagitta: the point drops 4 times this.</param>
        /// <param name="sagMeters">Gravity's drop of the point with the blade horizontal.</param>
        public static BladeDroop Create(Vector3 forteDirection, Vector3 gripUp, Vector3 down, float setSagittaMeters, float sagMeters)
        {
            var forte = Vector3.Normalize(forteDirection);
            var palmSide = -(gripUp - (Vector3.Dot(gripUp, forte) * forte));
            var setDeflection = palmSide.LengthSquared() > 1e-10f ? Vector3.Normalize(palmSide) * (4.0f * setSagittaMeters) : Vector3.Zero;

            // Only gravity across the blade bends it: its length is the cosine of the blade's elevation.
            var across = down - (Vector3.Dot(down, forte) * forte);
            var sagDeflection = across * sagMeters;

            var chord = Vector3.Normalize((forte * BladePose.BladeLengthMeters) + setDeflection + sagDeflection);
            return new BladeDroop(setDeflection, sagDeflection, SegmentMath.FindRotationBetween(chord, forte));
        }

        /// <summary>
        /// The blade from the guard to the real, drooped point: the forte's line turned toward the droop, still a
        /// blade's length long (the curve's shortening is a few millimeters at most).
        /// </summary>
        public BladeSegment GetChord(BladeSegment forte)
        {
            if (!HasShape)
            {
                return forte;
            }

            var toTip = (forte.Direction * BladePose.BladeLengthMeters) + TipDeflection;
            return new BladeSegment(forte.GuardPosition, forte.GuardPosition + (Vector3.Normalize(toTip) * BladePose.BladeLengthMeters));
        }

        /// <summary>
        /// The rest curve's offset from the chord at <paramref name="fraction"/> of the way from the guard (0) to the
        /// point (1): zero at both ends, at most about a quarter of the point's drop in between (the curve sits on the
        /// thumb's side of the chord).
        /// </summary>
        public Vector3 GetChordOffset(float fraction)
        {
            if (!HasShape)
            {
                return Vector3.Zero;
            }

            var u = Math.Max(0.0f, Math.Min(1.0f, fraction));
            var setShape = u * u;
            var sagShape = ((6.0f * u * u) - (4.0f * u * u * u) + (u * u * u * u)) / 3.0f;
            return (SetDeflection * (setShape - u)) + (SagDeflection * (sagShape - u));
        }

        /// <summary>Which way the hilt points for a drawn blade whose guard leaves along <paramref name="chordDirection"/>.</summary>
        public Vector3 GetHiltDirection(Vector3 chordDirection)
        {
            return HasShape ? Vector3.Transform(chordDirection, ChordToForte) : chordDirection;
        }
    }
}
