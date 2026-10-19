using System;
using System.Numerics;

namespace VrFencing.Blade
{
    /// <summary>
    /// The shape of a blade held at the guard and pressed at the point (PER-48 side hits): one smooth planar curve of
    /// the blade's full length that leaves the guard along the hand and ends on the pinned point. Its curvature changes
    /// linearly from the guard to the point, so it has no kinks and at most one gentle inflection:
    ///
    /// <list type="bullet">
    /// <item>tangent angle a (2x - x^2) + b (x - 1.5 x^2) along the blade (x from 0 at the guard to 1 at the point);</item>
    /// <item>the a part bends the blade one way, like a cantilever pushed sideways at its point (pinned off the hand's
    /// line), curvature highest at the guard and zero at the point;</item>
    /// <item>the b part bows it out and back onto the line, like a blade pushed straight in (an épée buckling).</item>
    /// </list>
    ///
    /// The two amounts are solved so the curve ends on the point. The curvature is capped at
    /// <see cref="MaxCurvaturePerMeter"/>: the bow of a straight push of <see cref="BladeContactBend.MaxPushMeters"/>,
    /// so "bent as far as it goes" means the same in front and side hits.
    /// </summary>
    public static class BladeArc
    {
        // Integration steps along the blade: two per drawn piece.
        private const int IntegrationSteps = DrawnBlade.SampleCount * 2;
        private const int SolveIterations = 30;
        private const float SolvedWithinMeters = 2e-4f;
        private const float DerivativeStep = 1e-4f;
        private const float MaxSolveStep = 0.5f;
        private const float MaxModeAmount = 8.0f;

        // A little slack over the straight-push bow, so the push limit and the curvature limit agree.
        private const float CapSlack = 1.002f;

        /// <summary>The most the drawn blade curves anywhere along it, 1/m: the bow of the deepest straight push.</summary>
        public static readonly float MaxCurvaturePerMeter = FindStraightPushCurvature(BladeContactBend.MaxPushMeters) * CapSlack;

        /// <summary>
        /// Solves the two amounts so the curve from the guard ends <paramref name="along"/> down the hand's blade and
        /// <paramref name="across"/> to its side, starting from the given amounts (the last solution: shapes stay
        /// continuous frame to frame). False when no curve of the blade's length reaches the point.
        /// </summary>
        public static bool TrySolve(float along, float across, ref float a, ref float b)
        {
            var length = BladePose.BladeLengthMeters;
            if ((along * along) + (across * across) > length * length)
            {
                return false;
            }

            double modeA = a;
            double modeB = b;
            for (var iteration = 0; iteration < SolveIterations; iteration++)
            {
                FindEnd(modeA, modeB, out var endAlong, out var endAcross);
                var errorAlong = endAlong - along;
                var errorAcross = endAcross - across;
                if (Math.Sqrt((errorAlong * errorAlong) + (errorAcross * errorAcross)) < SolvedWithinMeters)
                {
                    a = (float)modeA;
                    b = (float)modeB;
                    return true;
                }

                FindEnd(modeA + DerivativeStep, modeB, out var alongA, out var acrossA);
                FindEnd(modeA, modeB + DerivativeStep, out var alongB, out var acrossB);
                var j11 = (alongA - endAlong) / DerivativeStep;
                var j21 = (acrossA - endAcross) / DerivativeStep;
                var j12 = (alongB - endAlong) / DerivativeStep;
                var j22 = (acrossB - endAcross) / DerivativeStep;

                // Damped least squares: well behaved where the straight blade makes the push direction flat.
                const double damping = 1e-6;
                var m11 = (j11 * j11) + (j21 * j21) + damping;
                var m12 = (j11 * j12) + (j21 * j22);
                var m22 = (j12 * j12) + (j22 * j22) + damping;
                var r1 = -((j11 * errorAlong) + (j21 * errorAcross));
                var r2 = -((j12 * errorAlong) + (j22 * errorAcross));
                var determinant = (m11 * m22) - (m12 * m12);
                if (Math.Abs(determinant) < 1e-18)
                {
                    return false;
                }

                var stepA = ((m22 * r1) - (m12 * r2)) / determinant;
                var stepB = ((m11 * r2) - (m12 * r1)) / determinant;
                var stepLength = Math.Sqrt((stepA * stepA) + (stepB * stepB));
                if (stepLength > MaxSolveStep)
                {
                    stepA *= MaxSolveStep / stepLength;
                    stepB *= MaxSolveStep / stepLength;
                }

                modeA = Math.Max(-MaxModeAmount, Math.Min(MaxModeAmount, modeA + stepA));
                modeB = Math.Max(-MaxModeAmount, Math.Min(MaxModeAmount, modeB + stepB));
            }

            return false;
        }

        /// <summary>The largest curvature along the curve, 1/m. It is linear along the blade, so the larger end wins.</summary>
        public static float FindMaxCurvature(float a, float b)
        {
            return Math.Max(Math.Abs((2.0f * a) + b), Math.Abs(2.0f * b)) / BladePose.BladeLengthMeters;
        }

        /// <summary>
        /// Writes the curve's <paramref name="samples"/> (evenly along it, guard first) from <paramref name="guard"/>,
        /// leaving along <paramref name="startDirection"/> and bending toward <paramref name="across"/> (perpendicular
        /// to it). The last sample is the curve's end.
        /// </summary>
        public static void FillSamples(Vector3 guard, Vector3 startDirection, Vector3 across, float a, float b, Vector3[] samples)
        {
            var pieces = samples.Length - 1;
            var stepsPerPiece = IntegrationSteps / pieces;
            var stepLength = BladePose.BladeLengthMeters / (pieces * stepsPerPiece);
            var position = guard;
            samples[0] = guard;
            for (var piece = 0; piece < pieces; piece++)
            {
                for (var step = 0; step < stepsPerPiece; step++)
                {
                    var x = ((piece * stepsPerPiece) + step + 0.5f) / (pieces * stepsPerPiece);
                    var angle = FindTangentAngle(a, b, x);
                    position += ((float)Math.Cos(angle) * startDirection + ((float)Math.Sin(angle) * across)) * stepLength;
                }

                samples[piece + 1] = position;
            }
        }

        private static void FindEnd(double a, double b, out double along, out double across)
        {
            along = 0.0;
            across = 0.0;
            var stepLength = (double)BladePose.BladeLengthMeters / IntegrationSteps;
            for (var step = 0; step < IntegrationSteps; step++)
            {
                var x = (step + 0.5) / IntegrationSteps;
                var angle = (a * ((2.0 * x) - (x * x))) + (b * (x - (1.5 * x * x)));
                along += Math.Cos(angle) * stepLength;
                across += Math.Sin(angle) * stepLength;
            }
        }

        private static float FindTangentAngle(float a, float b, float x)
        {
            return (a * ((2.0f * x) - (x * x))) + (b * (x - (1.5f * x * x)));
        }

        private static float FindStraightPushCurvature(float pushMeters)
        {
            var a = 0.0f;
            var b = 1.0f;
            if (!TrySolve(BladePose.BladeLengthMeters - pushMeters, 0.0f, ref a, ref b))
            {
                throw new InvalidOperationException("The straight-push bow has no solution.");
            }

            return FindMaxCurvature(a, b);
        }
    }
}
