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

        // Kept from the solve so they need not be integrated again: the angle between the blade at the guard and the
        // load's line (radians), and the largest curvature for a unit length.
        internal float GuardAngle;
        internal float UnitMaxCurvature;
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

        // From a table seed, a Newton step from the third on that does not at least halve the miss means it is not
        // closing in: give that seed up (a warm start from the last shape always gets its full run).
        private const int StallIterations = 2;
        private const double StallRatio = 0.5;
        private const double SolvedWithin = 2e-5;
        private const double DerivativeStep = 1e-5;
        private const double MaxSolveStep = 0.5;

        // A shape this close to the last (load, and tip angle in radians) is the same shape followed on.
        private const float NearLoad = 1.0f;
        private const float NearTipAngle = 0.5f;

        // A blade pushed this far straight in sets the most it may curve anywhere; a little slack so the push limit and
        // the curvature limit agree.
        private const float CapSlack = 1.002f;

        // Running integrals along the blade, reused (the blade is solved on one thread).
        private static readonly double[] Scratch = new double[(IntegrationSteps + 1) * 2];

        private static readonly TableEntry[] Table = BuildTable();

        // The side push: square to the blade at the contact; loads up to 3 bend the pushed stretch well past the
        // curvature limit. Solved by Newton's method from the last load (to 1e-5 rad of the contact's angle), else by
        // bisection.
        private static readonly double SideLoadTipAngle = FindSideLoadTipAngle();
        private const double SideLoadMaxLoad = 3.0;
        private const int SideLoadSearchSteps = 20;
        private const int SideLoadNewtonIterations = 5;
        private const double SideLoadSolvedWithin = 1e-5;

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

            if (shape.IsSolved && TryNewton(targetX, targetY, shape.Load, shape.TipAngle, false, ref shape))
            {
                return true;
            }

            foreach (var seed in FindNearestSeeds(targetX, targetY))
            {
                if (TryNewton(targetX, targetY, seed.Load, seed.TipAngle, true, ref shape))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Like <see cref="TrySolve"/>, but only a shape near <paramref name="shape"/> (which must be solved): it follows
        /// that one shape as the point moves a little, and does not jump to another shape to the same point (a seed from
        /// the table counts only if it lands near).
        /// </summary>
        public static bool TrySolveNear(float along, float across, ref ElasticaShape shape)
        {
            if (!shape.IsSolved)
            {
                return false;
            }

            var near = shape;
            var solved = TrySolve(along, across, ref shape);
            return solved
                && Math.Abs(shape.Load - near.Load) <= NearLoad
                && Math.Abs(shape.TipAngle - near.TipAngle) <= NearTipAngle;
        }

        /// <summary>The largest curvature along the blade, 1/m.</summary>
        public static float FindMaxCurvature(ElasticaShape shape)
        {
            return FindMaxCurvature(shape, BladePose.BladeLengthMeters);
        }

        /// <summary>The largest curvature along a bent stretch <paramref name="lengthMeters"/> long, 1/m.</summary>
        public static float FindMaxCurvature(ElasticaShape shape, float lengthMeters)
        {
            var unitCurvature = shape.IsSolved ? shape.UnitMaxCurvature : Integrate(shape.Load, shape.TipAngle, IntegrationSteps, null).MaxCurvature;
            return (float)(unitCurvature / lengthMeters);
        }

        /// <summary>
        /// The blade's side pressed on a body (PER-69): a cantilever clamped at the guard and pushed at one point of its
        /// length, the contact, square to the blade there (the body cannot pull, and a smooth contact pushes along its
        /// normal). Between the guard and the contact it is the elastica of that stretch with no curvature at the
        /// contact; past it the free part carries no load and runs straight on. Solves the load and the stretch's length
        /// so the contact is <paramref name="along"/> down the hand's blade and <paramref name="across"/> (positive) to
        /// its side, pushed toward +across. False when no stretch of a sensible bend reaches it.
        /// </summary>
        /// <param name="shape">The solved load (its tip angle is the square push); a solved one starts the search.</param>
        /// <param name="contactMeters">How far along the blade from the guard the contact is.</param>
        public static bool TrySolveSideLoad(float along, float across, ref ElasticaShape shape, out float contactMeters)
        {
            contactMeters = 0.0f;
            if (!(across > 0.0f) || !(along > 0.0f))
            {
                return false;
            }

            // The angle the contact sits off the hand's line grows with the load: Newton's method on it from the last
            // load, or a bisection when there is none or it does not settle.
            var target = Math.Atan2(across, along);
            if (!(shape.IsSolved && shape.TipAngle == (float)SideLoadTipAngle && TryNewtonSideLoad(target, shape.Load, out var load)))
            {
                var low = 0.0;
                var high = SideLoadMaxLoad;
                if (FindSideLoadAngle(high) < target)
                {
                    return false;
                }

                for (var step = 0; step < SideLoadSearchSteps; step++)
                {
                    var middle = (low + high) * 0.5;
                    if (FindSideLoadAngle(middle) < target)
                    {
                        low = middle;
                    }
                    else
                    {
                        high = middle;
                    }
                }

                load = (low + high) * 0.5;
            }
            var end = Integrate(load, SideLoadTipAngle, IntegrationSteps, null);
            var unitReach = Math.Sqrt((end.X * end.X) + (end.Y * end.Y));
            contactMeters = (float)(Math.Sqrt((along * along) + (across * across)) / unitReach);
            shape.Load = (float)load;
            shape.TipAngle = (float)SideLoadTipAngle;
            shape.IsSolved = true;
            shape.GuardAngle = (float)end.GuardAngle;
            shape.UnitMaxCurvature = (float)end.MaxCurvature;
            return true;
        }

        /// <summary>
        /// Writes the side-pressed blade's <paramref name="samples"/> (evenly along the whole blade, guard first): the
        /// elastica from <paramref name="guard"/> along <paramref name="startDirection"/> to the contact
        /// <paramref name="contactMeters"/> along it, pushed toward <paramref name="across"/>, then straight on to the
        /// point. Returns the blade's direction at the contact (and past it).
        /// </summary>
        public static Vector3 FillSideLoad(Vector3 guard, Vector3 startDirection, Vector3 across, ElasticaShape shape, float contactMeters, Vector3[] samples)
        {
            var end = Integrate(shape.Load, shape.TipAngle, IntegrationSteps, Scratch);
            var tangentAngle = shape.TipAngle - end.GuardAngle;
            var tangent = ((float)Math.Cos(tangentAngle) * startDirection) + ((float)Math.Sin(tangentAngle) * across);
            var contact = guard + (((float)end.X * startDirection) + ((float)end.Y * across)) * contactMeters;
            var pieces = samples.Length - 1;
            for (var piece = 0; piece <= pieces; piece++)
            {
                var alongBlade = BladePose.BladeLengthMeters * piece / pieces;
                if (alongBlade >= contactMeters)
                {
                    samples[piece] = contact + (tangent * (alongBlade - contactMeters));
                    continue;
                }

                // Nodes count from the contact (node 0) back to the guard (node IntegrationSteps).
                var fromContact = (1.0 - (alongBlade / contactMeters)) * IntegrationSteps;
                var node = Math.Min((int)fromContact, IntegrationSteps - 1);
                var blend = fromContact - node;
                ToGuardFrame(end, Scratch[node * 2], Scratch[(node * 2) + 1], out var x0, out var y0);
                ToGuardFrame(end, Scratch[(node + 1) * 2], Scratch[((node + 1) * 2) + 1], out var x1, out var y1);
                var x = x0 + ((x1 - x0) * blend);
                var y = y0 + ((y1 - y0) * blend);
                samples[piece] = guard + (((float)x * startDirection) + ((float)y * across)) * contactMeters;
            }

            return tangent;
        }

        private static bool TryNewtonSideLoad(double target, double start, out double load)
        {
            load = start;
            for (var iteration = 0; iteration < SideLoadNewtonIterations; iteration++)
            {
                var angle = FindSideLoadAngle(load);
                var miss = angle - target;
                if (Math.Abs(miss) < SideLoadSolvedWithin)
                {
                    return true;
                }

                var slope = (FindSideLoadAngle(load + DerivativeStep) - angle) / DerivativeStep;
                if (!(slope > 1e-9))
                {
                    return false;
                }

                load = Math.Max(0.0, Math.Min(SideLoadMaxLoad, load - (miss / slope)));
            }

            return false;
        }

        private static double FindSideLoadAngle(double load)
        {
            var end = Integrate(load, SideLoadTipAngle, IntegrationSteps, null);
            return Math.Atan2(end.Y, end.X);
        }

        private static double FindSideLoadTipAngle()
        {
            // The square push's sign that deflects the contact toward +across.
            return Integrate(1.0, Math.PI * 0.5, IntegrationSteps, null).Y > 0.0 ? Math.PI * 0.5 : -Math.PI * 0.5;
        }

        /// <summary>
        /// Which way the blade pushes on what holds its end (the load's line, from the guard's side toward the point), in
        /// the guard's frame: X along the hand's blade, Y toward +across (the side the shape bulges to). The pinned point
        /// takes this push; it is not along the line from the guard to the point once the blade is bent, since the
        /// clamp at the guard pushes sideways too.
        /// </summary>
        public static Vector2 FindLoadDirection(ElasticaShape shape)
        {
            var guardAngle = shape.IsSolved ? shape.GuardAngle : Integrate(shape.Load, shape.TipAngle, IntegrationSteps, null).GuardAngle;
            return new Vector2((float)Math.Cos(guardAngle), -(float)Math.Sin(guardAngle));
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

        private static bool TryNewton(double targetX, double targetY, double load, double tipAngle, bool givesUpOnStall, ref ElasticaShape shape)
        {
            var lastError = double.MaxValue;
            for (var iteration = 0; iteration < SolveIterations; iteration++)
            {
                var end = Integrate(load, tipAngle, IntegrationSteps, null);
                var errorX = end.X - targetX;
                var errorY = end.Y - targetY;
                var error = Math.Sqrt((errorX * errorX) + (errorY * errorY));
                if (givesUpOnStall && iteration >= StallIterations && error > lastError * StallRatio)
                {
                    // Not closing in (no shape there, or another one far off): give up early, the search is costly.
                    return false;
                }

                lastError = error;
                if (error < SolvedWithin)
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
                    shape.GuardAngle = (float)end.GuardAngle;
                    shape.UnitMaxCurvature = (float)end.MaxCurvature;
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
