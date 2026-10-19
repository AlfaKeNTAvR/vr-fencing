using System;
using System.Collections.Generic;
using System.Numerics;

namespace VrFencing.Blade
{
    /// <summary>
    /// One solved shape of the pressed blade (<see cref="BladeArc"/>): the load on the point, as the two numbers of the
    /// elastica. Kept from frame to frame so the next solve starts from it.
    /// </summary>
    public struct ElasticaShape
    {
        /// <summary>
        /// The load on the point, as k = sqrt(F L^2 / EI) (F the force, L the blade length, EI the bending stiffness): 0
        /// is a straight blade, about 4.49 the Euler buckling load of a blade clamped at the guard and pinned at the point.
        /// </summary>
        public float Load;

        /// <summary>The angle between the point's end of the blade and the load's line, radians.</summary>
        public float TipAngle;

        /// <summary>False until solved: the next solve starts from a table of shapes instead.</summary>
        public bool IsSolved;
    }

    /// <summary>
    /// The shape of a blade held at the guard and pressed at the point (PER-50): the large-deflection elastica of a
    /// slender rod clamped at the guard (it leaves the guard along the hand's blade) and pinned at the point (the point
    /// stays where it is and is free to turn, so no moment acts there). The only load is the force at the point, so the
    /// bending moment along the blade is that force times the distance to its line: the curvature is zero at the point
    /// and largest near the guard and mid-blade, and the whole blade is one smooth curve, never a hook at the tip.
    ///
    /// Measured from the point back to the guard (u from 0 to 1 of the blade's length), the angle psi between the blade
    /// and the force's line obeys the pendulum equation psi'' = -k^2 sin(psi), starting at rest (zero curvature at the
    /// point) at the tip angle. The two numbers (k, tip angle) are solved by Newton's method so the curve, turned to
    /// leave the guard along the hand's blade, ends on the pinned point. Every solve starts from the last shape, or the
    /// nearest of a small table of shapes; one or two Newton steps are usual.
    ///
    /// For a given point there are two shapes: the blade bulges to one side of the line from the guard to the point,
    /// or to the other. The solver returns the one bulging toward +across (the caller picks the side). The natural one
    /// bulges to the side the hand's blade points to from that line, curving over toward the point, like a real épée
    /// pressed against a target; when pushed straight in, the side is undecided and the caller's blade set decides.
    /// </summary>
    public static class BladeArc
    {
        // Steps along the blade: two per drawn piece. RK4 is accurate to a few micrometers at this step.
        private const int IntegrationSteps = DrawnBlade.SampleCount * 2;

        // The table of starting shapes: coarser, it only seeds Newton's method.
        private const int TableSteps = 16;
        private const int TableLoads = 19;
        private const float TableMaxLoad = 9.0f;
        private const int TableTipAngles = 48;
        private const int SeedsTried = 3;

        private const int SolveIterations = 12;
        private const double SolvedWithin = 2e-5;
        private const double DerivativeStep = 1e-5;
        private const double MaxSolveStep = 0.5;

        // A blade pushed this far straight in sets the most it may curve anywhere; a little slack so the push limit and
        // the curvature limit agree.
        private const float CapSlack = 1.002f;

        // Running integrals along the blade, reused (the blade is solved on one thread).
        private static readonly double[] Scratch = new double[(IntegrationSteps + 1) * 2];

        private static readonly TableEntry[] Table = BuildTable();

        /// <summary>The most the drawn blade curves anywhere along it, 1/m: as pushed <see cref="BladeContactBend.MaxPushMeters"/> straight in.</summary>
        public static readonly float MaxCurvaturePerMeter = FindStraightPushCurvature(BladeContactBend.MaxPushMeters) * CapSlack;

        /// <summary>
        /// Solves the shape whose point is <paramref name="along"/> down the hand's blade from the guard and
        /// <paramref name="across"/> to its side, bulging toward +across (never crossing over to the other side of the
        /// line from the guard to the point), starting from <paramref name="shape"/> when it is solved. False when no
        /// such shape of the blade's length reaches the point.
        /// </summary>
        public static bool TrySolve(float along, float across, ref ElasticaShape shape)
        {
            var length = BladePose.BladeLengthMeters;
            var targetX = along / length;
            var targetY = across / length;
            if ((targetX * targetX) + (targetY * targetY) > 1.0f)
            {
                return false;
            }

            if (shape.IsSolved && TryNewton(targetX, targetY, shape.Load, shape.TipAngle, ref shape))
            {
                return true;
            }

            foreach (var seed in FindNearestSeeds(targetX, targetY))
            {
                if (TryNewton(targetX, targetY, seed.Load, seed.TipAngle, ref shape))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The largest curvature along the blade, 1/m.</summary>
        public static float FindMaxCurvature(ElasticaShape shape)
        {
            var end = Integrate(shape.Load, shape.TipAngle, IntegrationSteps, null);
            return (float)(end.MaxCurvature / BladePose.BladeLengthMeters);
        }

        /// <summary>
        /// Writes the shape's <paramref name="samples"/> (evenly along it, guard first) from <paramref name="guard"/>,
        /// leaving along <paramref name="startDirection"/> and bulging toward <paramref name="across"/> (perpendicular to
        /// it). The last sample is the point.
        /// </summary>
        public static void FillSamples(Vector3 guard, Vector3 startDirection, Vector3 across, ElasticaShape shape, Vector3[] samples)
        {
            var pieces = samples.Length - 1;
            var steps = pieces * Math.Max(1, IntegrationSteps / pieces);
            var nodes = steps == IntegrationSteps ? Scratch : new double[(steps + 1) * 2];
            var end = Integrate(shape.Load, shape.TipAngle, steps, nodes);
            var stepsPerPiece = steps / pieces;
            for (var piece = 0; piece <= pieces; piece++)
            {
                // Sample `piece` is (piece / pieces) of the way from the guard: node (steps - piece * stepsPerPiece) from the point.
                var node = steps - (piece * stepsPerPiece);
                ToGuardFrame(end, nodes[node * 2], nodes[(node * 2) + 1], out var x, out var y);
                samples[piece] = guard + (((float)x * startDirection) + ((float)y * across)) * BladePose.BladeLengthMeters;
            }
        }

        private static bool TryNewton(double targetX, double targetY, double load, double tipAngle, ref ElasticaShape shape)
        {
            for (var iteration = 0; iteration < SolveIterations; iteration++)
            {
                var end = Integrate(load, tipAngle, IntegrationSteps, null);
                var errorX = end.X - targetX;
                var errorY = end.Y - targetY;
                if (Math.Sqrt((errorX * errorX) + (errorY * errorY)) < SolvedWithin)
                {
                    var side = Integrate(load, tipAngle, IntegrationSteps, Scratch).Bulge;
                    if (end.Inflections > 1 || side < -1e-6)
                    {
                        // Converged on another shape: a higher buckling mode, or bulging to the other side.
                        return false;
                    }

                    shape.Load = (float)load;
                    shape.TipAngle = (float)tipAngle;
                    shape.IsSolved = true;
                    return true;
                }

                var byLoad = Integrate(load + DerivativeStep, tipAngle, IntegrationSteps, null);
                var byAngle = Integrate(load, tipAngle + DerivativeStep, IntegrationSteps, null);
                var j11 = (byLoad.X - end.X) / DerivativeStep;
                var j21 = (byLoad.Y - end.Y) / DerivativeStep;
                var j12 = (byAngle.X - end.X) / DerivativeStep;
                var j22 = (byAngle.Y - end.Y) / DerivativeStep;
                var determinant = (j11 * j22) - (j12 * j21);
                if (Math.Abs(determinant) < 1e-12)
                {
                    return false;
                }

                var stepLoad = -((j22 * errorX) - (j12 * errorY)) / determinant;
                var stepAngle = -((j11 * errorY) - (j21 * errorX)) / determinant;
                var stepLength = Math.Sqrt((stepLoad * stepLoad) + (stepAngle * stepAngle));
                if (stepLength > MaxSolveStep)
                {
                    stepLoad *= MaxSolveStep / stepLength;
                    stepAngle *= MaxSolveStep / stepLength;
                }

                // The load's sign is the tip angle's half turn: keep it positive.
                load = Math.Abs(load + stepLoad);
                tipAngle += stepAngle;
            }

            return false;
        }

        /// <summary>
        /// Integrates the pendulum equation from the point (u = 0) to the guard (u = 1), unit blade length. Returns where
        /// the point ends up from the guard in the guard's frame (x along the hand's blade, y across). With
        /// <paramref name="nodes"/>, also writes the running position integrals at every step for <see cref="ToGuardFrame"/>.
        /// </summary>
        private static Endpoint Integrate(double load, double tipAngle, int steps, double[] nodes)
        {
            var q = load * load;
            var h = 1.0 / steps;
            var psi = tipAngle;
            var rate = 0.0;
            var sumCos = 0.0;
            var sumSin = 0.0;
            var inflections = 0;
            var maxCurvature = 0.0;
            if (nodes != null)
            {
                nodes[0] = 0.0;
                nodes[1] = 0.0;
            }

            for (var step = 0; step < steps; step++)
            {
                var rate1 = rate;
                var accel1 = -q * Math.Sin(psi);
                var psi2 = psi + (0.5 * h * rate1);
                var rate2 = rate + (0.5 * h * accel1);
                var accel2 = -q * Math.Sin(psi2);
                var psi3 = psi + (0.5 * h * rate2);
                var rate3 = rate + (0.5 * h * accel2);
                var accel3 = -q * Math.Sin(psi3);
                var psi4 = psi + (h * rate3);
                var rate4 = rate + (h * accel3);
                var accel4 = -q * Math.Sin(psi4);

                sumCos += h / 6.0 * (Math.Cos(psi) + (2.0 * Math.Cos(psi2)) + (2.0 * Math.Cos(psi3)) + Math.Cos(psi4));
                sumSin += h / 6.0 * (Math.Sin(psi) + (2.0 * Math.Sin(psi2)) + (2.0 * Math.Sin(psi3)) + Math.Sin(psi4));
                var nextPsi = psi + (h / 6.0 * (rate1 + (2.0 * rate2) + (2.0 * rate3) + rate4));
                var nextRate = rate + (h / 6.0 * (accel1 + (2.0 * accel2) + (2.0 * accel3) + accel4));
                if (step > 0 && (nextRate > 0.0) != (rate > 0.0) && Math.Abs(nextRate) > 1e-9)
                {
                    inflections++;
                }

                psi = nextPsi;
                rate = nextRate;
                maxCurvature = Math.Max(maxCurvature, Math.Abs(rate));
                if (nodes != null)
                {
                    nodes[(step + 1) * 2] = sumCos;
                    nodes[((step + 1) * 2) + 1] = sumSin;
                }
            }

            var end = new Endpoint
            {
                GuardAngle = psi,
                SumCos = sumCos,
                SumSin = sumSin,
                Inflections = inflections,
                MaxCurvature = maxCurvature,
            };

            // The guard leaves along the hand's blade: turn everything by the guard's angle to the load.
            var cos = Math.Cos(psi);
            var sin = Math.Sin(psi);
            end.X = (sumCos * cos) + (sumSin * sin);
            end.Y = (sumSin * cos) - (sumCos * sin);
            end.Bulge = nodes != null ? FindBulge(end, steps, nodes) : 0.0;
            return end;
        }

        /// <summary>A blade point (its running integrals from the point back) in the guard's frame, from the guard.</summary>
        private static void ToGuardFrame(Endpoint end, double sumCosToHere, double sumSinToHere, out double x, out double y)
        {
            var fromGuardCos = end.SumCos - sumCosToHere;
            var fromGuardSin = end.SumSin - sumSinToHere;
            var cos = Math.Cos(end.GuardAngle);
            var sin = Math.Sin(end.GuardAngle);
            x = (fromGuardCos * cos) + (fromGuardSin * sin);
            y = (fromGuardSin * cos) - (fromGuardCos * sin);
        }

        /// <summary>
        /// The signed farthest offset of the blade from the line from the guard to the point, + toward +y; only known
        /// with the stored nodes (0 without).
        /// </summary>
        private static double FindBulge(Endpoint end, int steps, double[] nodes)
        {
            var chordLength = Math.Sqrt((end.X * end.X) + (end.Y * end.Y));
            if (chordLength < 1e-9)
            {
                return 0.0;
            }

            var farthest = 0.0;
            for (var node = 0; node <= steps; node++)
            {
                ToGuardFrame(end, nodes[node * 2], nodes[(node * 2) + 1], out var x, out var y);
                var offset = ((end.X * y) - (end.Y * x)) / chordLength;
                if (Math.Abs(offset) > Math.Abs(farthest))
                {
                    farthest = offset;
                }
            }

            return farthest;
        }

        private static TableEntry[] FindNearestSeeds(float targetX, float targetY)
        {
            var best = new TableEntry[SeedsTried];
            var bestDistance = new float[SeedsTried];
            for (var index = 0; index < SeedsTried; index++)
            {
                bestDistance[index] = float.MaxValue;
            }

            foreach (var entry in Table)
            {
                var dx = entry.X - targetX;
                var dy = entry.Y - targetY;
                var distance = (dx * dx) + (dy * dy);
                for (var rank = 0; rank < SeedsTried; rank++)
                {
                    if (distance < bestDistance[rank])
                    {
                        for (var move = SeedsTried - 1; move > rank; move--)
                        {
                            best[move] = best[move - 1];
                            bestDistance[move] = bestDistance[move - 1];
                        }

                        best[rank] = entry;
                        bestDistance[rank] = distance;
                        break;
                    }
                }
            }

            return best;
        }

        private static TableEntry[] BuildTable()
        {
            var entries = new List<TableEntry>();
            var nodes = new double[(TableSteps + 1) * 2];
            for (var loadIndex = 0; loadIndex < TableLoads; loadIndex++)
            {
                var load = TableMaxLoad * loadIndex / (TableLoads - 1);
                for (var angleIndex = 0; angleIndex < TableTipAngles; angleIndex++)
                {
                    var tipAngle = -Math.PI + (2.0 * Math.PI * angleIndex / TableTipAngles);
                    var end = Integrate(load, tipAngle, TableSteps, nodes);
                    if (end.Inflections <= 1 && end.Bulge > 1e-4)
                    {
                        entries.Add(new TableEntry { X = (float)end.X, Y = (float)end.Y, Load = (float)load, TipAngle = (float)tipAngle });
                    }
                }
            }

            return entries.ToArray();
        }

        private static float FindStraightPushCurvature(float pushMeters)
        {
            var shape = default(ElasticaShape);
            if (!TrySolve(BladePose.BladeLengthMeters - pushMeters, 0.0f, ref shape))
            {
                throw new InvalidOperationException("The straight-push bow has no solution.");
            }

            return FindMaxCurvature(shape);
        }

        private struct Endpoint
        {
            public double X;
            public double Y;
            public double GuardAngle;
            public double SumCos;
            public double SumSin;
            public int Inflections;
            public double MaxCurvature;
            public double Bulge;
        }

        private struct TableEntry
        {
            public float X;
            public float Y;
            public float Load;
            public float TipAngle;
        }
    }
}
