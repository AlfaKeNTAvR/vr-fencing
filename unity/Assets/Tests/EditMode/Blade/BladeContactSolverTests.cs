// The blade contact solver (PER-122): two hands' blades in, two drawn blades out. Neither hilt leaves its hand, the
// blades never pass through each other (crossing or nearly parallel), the give is shared by compliance so the foible
// gives to the forte, a press past every limit slides the weaker blade off toward its point, and the rule is symmetric
// and deterministic. Meters, seconds; blades are 90 cm.
using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Blade;
using VrFencing.Settings;

namespace VrFencing.Tests.Blade
{
    public class BladeContactSolverTests
    {
        private const float FrameSeconds = 1.0f / 72.0f;
        private const float Length = 0.9f;
        private static readonly float Stiffness = BladeContactSolver.NormalBendStiffness;

        [Test]
        public void ApartTheBladesAreDrawnAsTheHandsHoldThem()
        {
            var solver = new BladeContactSolver();
            var a = Hold(new Vector3(-0.3f, 1.2f, 1.0f), Vector3.UnitX);
            var b = Hold(new Vector3(0.0f, 1.0f, 1.6f), -Vector3.UnitZ);

            solver.Solve(a, b, FrameSeconds);

            Assert.That(solver.IsInContact, Is.False);
            Assert.That(solver.GiveA.Chord.TipPosition, Is.EqualTo(a.Chord.TipPosition));
            Assert.That(solver.GiveB.Chord.TipPosition, Is.EqualTo(b.Chord.TipPosition));
            Assert.That(solver.Side.Value.Y, Is.GreaterThan(0.99f), "A is above B.");
        }

        [Test]
        public void AForteOnAFoibleBendsTheFoibleAndAFoibleOnAForteBendsItsOwn()
        {
            // A presses 2 cm down on B where they cross: at 15% of A and 85% of B, then the other way round.
            var forteOnFoible = PressAcross(fractionA: 0.15f, fractionB: 0.85f, 0.02f);
            var foibleOnForte = PressAcross(fractionA: 0.85f, fractionB: 0.15f, 0.02f);

            TestContext.WriteLine($"Forte on foible: A {forteOnFoible.A * 100.0f:0.0} cm, B {forteOnFoible.B * 100.0f:0.0} cm; foible on forte: A {foibleOnForte.A * 100.0f:0.0} cm, B {foibleOnForte.B * 100.0f:0.0} cm.");
            Assert.That(forteOnFoible.B, Is.GreaterThan(3.0f * forteOnFoible.A), "The foible gives to the forte.");
            Assert.That(foibleOnForte.A, Is.GreaterThan(3.0f * foibleOnForte.B), "A foible on a forte gives itself.");
            Assert.That(forteOnFoible.A + forteOnFoible.B, Is.EqualTo(0.02f + BladeContactSolver.ContactMeters).Within(0.002f), "Together they take the whole press.");
        }

        [Test]
        public void TheGripTurnsAtMostFiveDegrees()
        {
            var solver = new BladeContactSolver();
            for (var depth = -0.05f; depth < 0.06f; depth += 0.005f)
            {
                SolveAcross(solver, 0.3f, 0.3f, depth);
            }

            Assert.That(solver.IsInContact, Is.True);
            Assert.That(solver.GiveA.TurnDegrees, Is.LessThanOrEqualTo(BladeContactSolver.GripTurnMaxDegrees + 0.05f));
            Assert.That(solver.GiveB.TurnDegrees, Is.LessThanOrEqualTo(BladeContactSolver.GripTurnMaxDegrees + 0.05f));
            Assert.That(solver.GiveA.BowMeters + solver.GiveB.BowMeters, Is.GreaterThan(0.005f), "The blades bow for the rest.");
        }

        [Test]
        public void PressedPastEveryLimitTheWeakerBladeSlidesOffTowardItsPointAndNeverJumpsThrough()
        {
            // A's forte presses down across B's foible, on and on, 40 cm past B's line: more than every grip and bow takes.
            // Slowly (5 mm a frame), so B slides all the way off before the press is past the through depth (PER-147).
            var solver = new BladeContactSolver();
            var startSide = Vector3.UnitY;
            var wasReleasing = false;
            var wasThrough = false;
            var mostFractionB = 0.0f;
            var firstFractionB = float.NaN;
            var flips = 0;
            for (var depth = -0.05f; depth < 0.40f; depth += 0.005f)
            {
                var sideBefore = solver.Side;
                SolveAcross(solver, 0.2f, 0.7f, depth);
                wasReleasing |= solver.IsReleasing;
                wasThrough |= solver.IsThrough;
                if (solver.IsInContact)
                {
                    firstFractionB = float.IsNaN(firstFractionB) ? solver.FractionB : firstFractionB;
                    mostFractionB = Math.Max(mostFractionB, solver.FractionB);
                }

                var a = Shape(solver.GiveA);
                var b = Shape(solver.GiveB);
                if (sideBefore.HasValue && Vector3.Dot(sideBefore.Value, solver.Side.Value) < 0.0f)
                {
                    // The side changed over: only with the drawn blades apart (B turned aside past A), never through.
                    flips++;
                    Assert.That(BladePairScene.FindDistance(a, b), Is.GreaterThan(BladeContactSolver.ContactMeters - 0.0025f), $"Depth {depth:0.000}: changed sides through the other blade.");
                    Assert.That(solver.GiveB.TurnDegrees, Is.GreaterThan(BladeContactSolver.GripTurnMaxDegrees), "B turned aside past its grip's limit to slide off.");
                }
                else if (sideBefore.HasValue && (solver.IsInContact || solver.ReturningBlade < 0))
                {
                    // Pressed, sliding off, or apart and back in the hands; swinging back from the slide-off, the blades
                    // may cross as B straightens on the hands' side (PER-147).
                    Assert.That(BladePairScene.FindSeparation(a, b, sideBefore.Value), Is.GreaterThan(BladeContactSolver.ContactMeters - 0.0025f), $"Depth {depth:0.000}: through.");
                }
            }

            for (var frame = 0; frame < 36; frame++)
            {
                SolveAcross(solver, 0.2f, 0.7f, 0.40f);
            }

            Assert.That(wasReleasing, Is.True, "Past every limit the weaker blade slides off.");
            Assert.That(wasThrough, Is.False, "A slow press slides off; it is not pushed through.");
            Assert.That(flips, Is.EqualTo(1), "Once off its point, the side follows the hands.");
            Assert.That(solver.Side.Value.Y, Is.LessThan(-0.9f), "A is now below B, as the hands are.");
            Assert.That(solver.IsInContact, Is.False, "Released: it never holds.");
            Assert.That(solver.GiveB.TurnDegrees, Is.LessThan(0.1f), "And B swung back to its hand.");
            TestContext.WriteLine($"Contact on B from {firstFractionB:0.00} to {mostFractionB:0.00} of the blade.");
        }

        [Test]
        public void SwappingTheTwoBladesMirrorsTheResult([Values(0.2f, 0.5f, 0.8f)] float fractionA, [Values(0.3f, 0.7f)] float fractionB)
        {
            var forward = new BladeContactSolver();
            var swapped = new BladeContactSolver();
            for (var depth = -0.03f; depth < 0.04f; depth += 0.005f)
            {
                var (a, b) = Across(fractionA, fractionB, depth);
                forward.Solve(a, b, FrameSeconds);
                swapped.Solve(b, a, FrameSeconds);
                AssertSame(forward.GiveA, swapped.GiveB, 1e-4f, $"depth {depth:0.000}, A");
                AssertSame(forward.GiveB, swapped.GiveA, 1e-4f, $"depth {depth:0.000}, B");
                Assert.That(forward.PressMeters, Is.EqualTo(swapped.PressMeters).Within(1e-5f));
            }
        }

        [Test]
        public void TheSameInputsGiveTheSameOutputs()
        {
            var first = new BladeContactSolver();
            var second = new BladeContactSolver();
            var random = new Random(7);
            for (var frame = 0; frame < 200; frame++)
            {
                var depth = (float)((random.NextDouble() * 0.12) - 0.04);
                var (a, b) = Across(0.2f + (float)(random.NextDouble() * 0.6), 0.2f + (float)(random.NextDouble() * 0.6), depth);
                first.Solve(a, b, FrameSeconds);
                second.Solve(a, b, FrameSeconds);
                AssertSame(first.GiveA, second.GiveA, 0.0f, $"frame {frame}");
                AssertSame(first.GiveB, second.GiveB, 0.0f, $"frame {frame}");
            }
        }

        [Test]
        public void AStifferGripTurnsLess()
        {
            float TurnFor(float gripStiffness)
            {
                var solver = new BladeContactSolver();
                for (var depth = -0.03f; depth < 0.015f; depth += 0.005f)
                {
                    var (a, b) = Across(0.3f, 0.3f, depth);
                    solver.Solve(a, new BladeHold(b.Chord, b.Droop, gripStiffness, Stiffness), FrameSeconds);
                }

                return solver.GiveB.TurnDegrees;
            }

            Assert.That(TurnFor(ArmedDummyBlade.LightGripStiffness), Is.GreaterThan(1.5f * TurnFor(ArmedDummyBlade.StrongGripStiffness)));
        }

        [Test]
        public void TheBowBendsTheBladeOnlyWhereItIsPressedAndRunsOnStraight()
        {
            Assert.That(ContactBow.FindShape(0.0f, 0.5f), Is.EqualTo(0.0f));
            Assert.That(ContactBow.FindShape(0.5f, 0.5f), Is.EqualTo(1.0f).Within(1e-6f));
            Assert.That(ContactBow.FindShape(1.0f, 0.5f), Is.EqualTo(2.5f).Within(1e-5f), "Past the load the blade runs on straight: (3 - 0.5) / (2 x 0.5).");
            var bow = new ContactBow(Vector3.UnitY * 0.01f, 0.5f);
            Assert.That(bow.GetChordOffset(0.0f).Length(), Is.LessThan(1e-7f));
            Assert.That(bow.GetChordOffset(1.0f).Length(), Is.LessThan(1e-7f));
        }

        [Test]
        public void TheBendStiffnessFollowsTheBladeFlexSetting()
        {
            Assert.That(BladeContactSolver.GetBendStiffness(BladeFlexSoftness.Normal), Is.EqualTo(BladeContactSolver.NormalBendStiffness).Within(1e-6f));
            Assert.That(BladeContactSolver.GetBendStiffness(BladeFlexSoftness.Stiff), Is.GreaterThan(BladeContactSolver.NormalBendStiffness));
            Assert.That(BladeContactSolver.GetBendStiffness(BladeFlexSoftness.Soft), Is.LessThan(BladeContactSolver.NormalBendStiffness));
        }

        /// <summary>
        /// Brings A down onto B (crossing square, A at <paramref name="fractionA"/>, B at <paramref name="fractionB"/>) to
        /// <paramref name="depth"/> past B's line; returns how far each blade's contact spot moved.
        /// </summary>
        private static (float A, float B) PressAcross(float fractionA, float fractionB, float depth)
        {
            var solver = new BladeContactSolver();
            for (var step = -0.03f; step < depth; step += 0.005f)
            {
                SolveAcross(solver, fractionA, fractionB, step);
            }

            var (a, b) = SolveAcross(solver, fractionA, fractionB, depth);
            var givenA = Vector3.Lerp(solver.GiveA.Chord.GuardPosition, solver.GiveA.Chord.TipPosition, fractionA) + solver.GiveA.Droop.GetChordOffset(fractionA);
            var givenB = Vector3.Lerp(solver.GiveB.Chord.GuardPosition, solver.GiveB.Chord.TipPosition, fractionB) + solver.GiveB.Droop.GetChordOffset(fractionB);
            var restA = Vector3.Lerp(a.Chord.GuardPosition, a.Chord.TipPosition, fractionA);
            var restB = Vector3.Lerp(b.Chord.GuardPosition, b.Chord.TipPosition, fractionB);
            return (givenA.Y - restA.Y, restB.Y - givenB.Y);
        }

        private static (BladeHold A, BladeHold B) SolveAcross(BladeContactSolver solver, float fractionA, float fractionB, float depth)
        {
            var (a, b) = Across(fractionA, fractionB, depth);
            solver.Solve(a, b, FrameSeconds);
            return (a, b);
        }

        /// <summary>
        /// A along +x, B along -z, crossing square at the given fractions of each; A <paramref name="depth"/> below B's
        /// line (negative: above it).
        /// </summary>
        private static (BladeHold A, BladeHold B) Across(float fractionA, float fractionB, float depth)
        {
            var crossing = new Vector3(0.0f, 1.0f, 1.0f);
            var b = Hold(crossing + (Vector3.UnitZ * (fractionB * Length)), -Vector3.UnitZ);
            var a = Hold(crossing - (Vector3.UnitX * (fractionA * Length)) - (Vector3.UnitY * depth), Vector3.UnitX);
            return (a, b);
        }

        private static BladeHold Hold(Vector3 guard, Vector3 direction)
        {
            return new BladeHold(new BladeSegment(guard, guard + (direction * Length)), default, BladeContactSolver.FencerGripStiffness, Stiffness);
        }

        private static List<Vector3> Shape(BladeGive give)
        {
            var shape = new List<Vector3>();
            for (var index = 0; index <= 24; index++)
            {
                var fraction = index / 24.0f;
                shape.Add(Vector3.Lerp(give.Chord.GuardPosition, give.Chord.TipPosition, fraction) + give.Droop.GetChordOffset(fraction));
            }

            return shape;
        }

        private static void AssertSame(BladeGive expected, BladeGive actual, float within, string when)
        {
            for (var index = 0; index <= 8; index++)
            {
                var fraction = index / 8.0f;
                var e = Vector3.Lerp(expected.Chord.GuardPosition, expected.Chord.TipPosition, fraction) + expected.Droop.GetChordOffset(fraction);
                var a = Vector3.Lerp(actual.Chord.GuardPosition, actual.Chord.TipPosition, fraction) + actual.Droop.GetChordOffset(fraction);
                Assert.That(Vector3.Distance(e, a), Is.LessThanOrEqualTo(within), $"{when}: at {fraction:0.00} of the blade.");
            }
        }
    }
}
