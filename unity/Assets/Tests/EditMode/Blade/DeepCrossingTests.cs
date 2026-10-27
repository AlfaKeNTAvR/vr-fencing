// PER-147: two Fencers' blades pushed deep through each other, as "Mirror me" at 100 ms showed on the headset: both
// hands closing at 3 to 7 m/s and 50 cm or more past each other, both with a Fencer's grip, no hand ever moved. Past
// the through depth (all the give at the contact, the slide's span and a margin) the pair is through: the press and the
// side latch are dropped and both drawn blades swing back to their hands over ReleaseSeconds. Every turn stays within
// MaxReleaseDegrees, no drawn point jumps more than 10 cm a frame beyond its own hand's move, and the drawn points never
// shake back and forth. A slow bind under 15 cm still holds without sliding off. Meters, seconds, degrees.
using System;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Blade;
using VrFencing.Settings;

namespace VrFencing.Tests.Blade
{
    public class DeepCrossingTests
    {
        private const float FrameSeconds = 1.0f / 72.0f;

        /// <summary>Each hand's guard 65 cm from the middle, the blades rising a little toward each other: their foibles cross.</summary>
        private const float GuardReachMeters = 0.65f;

        /// <summary>
        /// The deepest the through depth can be anywhere along the blades: both bows and both grip turns at a full blade's
        /// lever, the slide's span and the margin. Past it the pair must be let go.
        /// </summary>
        public static readonly float MostThroughMeters =
            (2.0f * BladeContactSolver.MaxBowMeters)
            + (2.0f * BladeContactSolver.GripTurnMaxDegrees * (float)Math.PI / 180.0f * BladePose.BladeLengthMeters)
            + BladeContactSolver.ReleaseSpanMeters
            + BladeContactSolver.ThroughMarginMeters;

        private static readonly BladeSettings Settings = new BladeSettings();

        [Test]
        public void ADeepFastCrossingPassesThroughWithBoundedTurnsAndNoShake([Values(3.0f, 5.0f, 7.0f)] float closingMetersPerSecond)
        {
            // A starts 12 cm to B's right; the hands close at the speed until A is 60 cm past B, then hold.
            var run = Crossing.Run(relative: t => Math.Max(-0.60f, 0.12f - (closingMetersPerSecond * (float)t)), seconds: 1.0);

            run.Trace.AssertBounded(run.ToString());
            Assert.That(run.LetGoSeconds, Is.LessThanOrEqualTo(run.PastThroughSeconds + 1e-4), $"Let go by the time the hands are past the deepest through depth. {run}");

            // The bows spring out first (at most ReleaseSeconds for the largest), then the turn eases out over ReleaseSeconds.
            Assert.That(run.GoneAfterThroughSeconds, Is.LessThanOrEqualTo((2.0 * BladeContactSolver.ReleaseSeconds) + FrameSeconds + 1e-4),
                $"Back in the hands within the release once past the through depth. {run}");
        }

        [Test]
        public void ADeepCrossingThatComesBackStaysBoundedBothWays([Values(3.0f, 7.0f)] float closingMetersPerSecond)
        {
            // Through and straight back: 55 cm past, then back to where it started.
            var travel = 0.67f / closingMetersPerSecond;
            var run = Crossing.Run(relative: t => 0.12f - (closingMetersPerSecond * (float)Math.Min(t, (2.0 * travel) - t)), seconds: 1.0);

            run.Trace.AssertBounded(run.ToString());
        }

        [Test]
        public void ASlowBindUnderFifteenCentimetersHoldsWithoutSlidingOff([Values(0.06f, 0.10f, 0.14f)] float pressMeters)
        {
            // From 5 cm apart into a press of up to 14 cm over a second, held for a second.
            var run = Crossing.Run(relative: t => 0.05f + BladeContactSolver.ContactMeters - ((0.05f + pressMeters) * (float)Math.Min(1.0, t)), seconds: 2.0);

            Assert.That(run.PressedFrames, Is.GreaterThan(72), $"Setup: pressed. {run}");
            Assert.That(run.SlidOffFrames, Is.EqualTo(0), $"Never slides off. {run}");
            Assert.That(run.Solver.IsInContact, Is.True, $"Still pressed at the end. {run}");
            Assert.That(Vector3.Dot(run.Solver.Side.Value, Vector3.UnitX), Is.GreaterThan(0.5f), $"On the side it started. {run}");
            run.Trace.AssertBounded(run.ToString());
        }

        [Test]
        public void AFastHitPressedOnPastEveryGiveIsLetGoWithinTheLatchThoughTheHandsKeepMoving()
        {
            // The press at which a slow press has slid all the way off (every give and the slide's span), and a little
            // more, short of the through depth.
            var heldDepth = Crossing.FindSlideOffDepth() + (0.5f * BladeContactSolver.ThroughMarginMeters);

            // A fast hit straight to that depth (in two frames, at 7 m/s), then held there while the partner's hand shivers
            // fast along its own blade (8 cm a frame, back and forth): engaged and moving fast all along.
            var hitSeconds = 2.0 * FrameSeconds;
            var run = Crossing.Run(
                relative: t => t < hitSeconds ? 0.06f - ((0.06f + heldDepth - BladeContactSolver.ContactMeters) * (float)(t / hitSeconds)) : BladeContactSolver.ContactMeters - heldDepth,
                seconds: 1.0,
                bAlong: t => (Math.Round(t / FrameSeconds) % 2.0) == 0.0 ? 0.0f : 0.08f);

            Assert.That(run.FirstPressSeconds, Is.Not.Null, $"Setup: the blades press. {run}");
            Assert.That(run.LetGoSeconds, Is.Not.Null, $"Let go at all. {run}");
            Assert.That(run.LetGoSeconds.Value - run.FirstPressSeconds.Value, Is.LessThanOrEqualTo(BladeContactSolver.LatchSeconds + (2.0 * FrameSeconds)),
                $"The latch runs from the hit only, not from every fast frame after it. {run}");
        }

        /// <summary>
        /// Two Fencers' blades crossing as in "Mirror me": A from its guard at z -0.65 toward +z, B mirrored from z +0.65
        /// toward -z, both rising a little so their foibles cross at the middle about 14 degrees apart. A is
        /// <c>relative</c> meters to B's right (+x), each hand moving half of it.
        /// </summary>
        private sealed class Crossing
        {
            public GiveTrace Trace { get; } = new GiveTrace();

            public BladeContactSolver Solver { get; } = new BladeContactSolver();

            public int PressedFrames { get; private set; }

            public int SlidOffFrames { get; private set; }

            public double? FirstPressSeconds { get; private set; }

            /// <summary>The first frame after pressing that the side followed the hands across (released or through).</summary>
            public double? LetGoSeconds { get; private set; }

            /// <summary>The first frame the hands were past <see cref="MostThroughMeters"/>.</summary>
            public double? PastThroughSeconds { get; private set; }

            /// <summary>From the first frame the hands were past <see cref="MostThroughMeters"/> to the first with no give and no press.</summary>
            public double GoneAfterThroughSeconds { get; private set; } = double.PositiveInfinity;

            public float DeepestPressMeters { get; private set; }

            public static Crossing Run(Func<double, float> relative, double seconds, Func<double, float> bAlong = null)
            {
                var run = new Crossing();
                var solver = run.Solver;
                double? pastThroughSeconds = null;
                Vector3? startSide = null;
                var frames = (int)Math.Round(seconds / FrameSeconds);
                for (var frame = 0; frame <= frames; frame++)
                {
                    var t = frame * FrameSeconds;
                    var apart = relative(t);
                    var a = Hold(new Vector3(0.5f * apart, 1.0f, -GuardReachMeters), new Vector3(0.0f, 0.12f, 1.0f));
                    var bDirection = Vector3.Normalize(new Vector3(0.0f, 0.12f, -1.0f));
                    var b = Hold(new Vector3(-0.5f * apart, 1.0f, GuardReachMeters) + (bDirection * (bAlong?.Invoke(t) ?? 0.0f)), bDirection);
                    solver.Solve(a, b, FrameSeconds);
                    run.Trace.Add(t, a.Chord, solver.GiveA, b.Chord, solver.GiveB, solver.IsInContact ? solver.PressMeters : 0.0f);

                    var wasPressing = run.PressedFrames > 0;
                    if (solver.IsInContact)
                    {
                        run.PressedFrames++;
                        run.FirstPressSeconds ??= t;
                        run.DeepestPressMeters = Math.Max(run.DeepestPressMeters, solver.PressMeters);
                    }

                    startSide ??= solver.Side;
                    if (wasPressing && !run.LetGoSeconds.HasValue && solver.Side.HasValue && Vector3.Dot(solver.Side.Value, startSide.Value) < 0.0f)
                    {
                        run.LetGoSeconds = t;
                    }

                    if (solver.SlidingBlade >= 0 || solver.ReturningBlade >= 0)
                    {
                        run.SlidOffFrames++;
                    }

                    if (!pastThroughSeconds.HasValue && BladeContactSolver.ContactMeters - apart >= MostThroughMeters)
                    {
                        pastThroughSeconds = t;
                        run.PastThroughSeconds = t;
                    }

                    var hasGive = solver.GiveA.TurnDegrees > 0.01f || solver.GiveB.TurnDegrees > 0.01f || solver.GiveA.BowMeters > 1e-4f || solver.GiveB.BowMeters > 1e-4f;
                    if (pastThroughSeconds.HasValue && double.IsPositiveInfinity(run.GoneAfterThroughSeconds) && !hasGive && !solver.IsInContact)
                    {
                        run.GoneAfterThroughSeconds = t - pastThroughSeconds.Value;
                    }
                }

                TestContext.WriteLine(run.ToString());
                return run;
            }

            /// <summary>The press at which a slow press (5 mm a frame) has the weaker blade slid all the way off.</summary>
            public static float FindSlideOffDepth()
            {
                var solver = new BladeContactSolver();
                for (var frame = 0; frame < 100; frame++)
                {
                    var depth = -0.05f + (0.005f * frame);
                    var apart = BladeContactSolver.ContactMeters - depth;
                    var a = Hold(new Vector3(0.5f * apart, 1.0f, -GuardReachMeters), new Vector3(0.0f, 0.12f, 1.0f));
                    var b = Hold(new Vector3(-0.5f * apart, 1.0f, GuardReachMeters), new Vector3(0.0f, 0.12f, -1.0f));
                    solver.Solve(a, b, FrameSeconds);
                    if (solver.ReturningBlade >= 0)
                    {
                        return depth;
                    }
                }

                Assert.Fail("Setup: a slow press slides off.");
                return 0.0f;
            }

            public override string ToString()
            {
                return FormattableString.Invariant(
                    $"{Trace} Pressed {PressedFrames} frames (deepest {DeepestPressMeters * 100:0.0} cm), slid off {SlidOffFrames}, first press {FirstPressSeconds:0.000} s, let go {LetGoSeconds:0.000} s, give gone {GoneAfterThroughSeconds:0.000} s after the deepest through depth {MostThroughMeters * 100:0.0} cm.");
            }

            private static BladeHold Hold(Vector3 guard, Vector3 direction)
            {
                var unit = Vector3.Normalize(direction);
                var forte = new BladeSegment(guard, guard + (unit * BladePose.BladeLengthMeters));
                var droop = BladeDroop.Create(unit, Vector3.UnitY, -Vector3.UnitY, Settings);
                return new BladeHold(droop.GetChord(forte), droop, BladeContactSolver.FencerGripStiffness, BladeContactSolver.GetBendStiffness(BladeFlexSoftness.Normal));
            }
        }
    }
}
