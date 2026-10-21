// The drawn blade never gets stuck on a body (PER-121): past what it bends, it lags the hand a little, shows the ghost
// hilt only past 3 cm, and pops back to the hand on the leash once the hand's blade is clear; a body stays solid for the
// point; steel (a bell) lets a point slip off instead of wedging. Since PER-122 the Armed Dummy's blade is not an
// obstacle at all (BladeContactSolver): the leash and the ghost only ever come from bodies, the floor and a bell. The
// leash, ghost and friction values are named constants (the PER-121 "Blade feel" Settings are gone). Meters, seconds.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Blade;
using VrFencing.Dummy;

namespace VrFencing.Tests.Blade
{
    public class BladeLeashTests
    {
        private const float FrameSeconds = 1.0f / 72.0f;
        private const float WallPinnedZ = 1.0f - BladeContactBend.PointRadiusMeters;

        [Test]
        public void ThePopNeverGoesIntoABody()
        {
            // The point pressed into a wall and on, 30 cm past the most the blade bends, held a second: the hand's blade
            // is in the wall, so the point stays pinned and the drawn hilt lags.
            var drawn = new DrawnBlade();
            var wall = WallObstacles();
            var pops = 0;
            for (var frame = 0; frame <= 120; frame++)
            {
                drawn.Step(BladeAt(0.0f, Math.Min(0.5f, frame * 0.01f)), wall, Vector3.UnitY, FrameSeconds);
                pops += drawn.HasPopped ? 1 : 0;
            }

            Assert.That(pops, Is.EqualTo(0));
            Assert.That(drawn.Contact.IsPinned, Is.True, "The body stays solid for the point.");
            Assert.That(drawn.IsGhostShown, Is.True);
        }

        [Test]
        public void AFlatSweptRightThroughAThinArmPopsToTheHandOnTheLeash()
        {
            // The flat swept 40 cm across a forearm-thin upright capsule in one frame: past the leash at once, it pops to
            // the hand. Only drawn: no Touch.
            var arm = new BladeObstacle[] { new Capsule(new Vector3(0.0f, 0.5f, 0.45f), new Vector3(0.0f, 1.5f, 0.45f), 0.03f) };
            var drawn = new DrawnBlade();
            drawn.Step(BladeAt(-0.2f, 0.0f), arm, Vector3.UnitY, FrameSeconds);

            drawn.Step(BladeAt(0.2f, 0.0f), arm, Vector3.UnitY, FrameSeconds);

            Assert.That(drawn.HasPopped, Is.True, "40 cm is past the leash at once.");
            Assert.That(drawn.GuardPosition.X, Is.EqualTo(0.2f).Within(1e-4f), "The hilt is in the hand; the blade springs from where it was.");
            Assert.That(drawn.LagMeters, Is.EqualTo(0.0f));
        }

        [Test]
        public void APointPushedSquareOntoSteelSlidesRoundItInsteadOfWedging()
        {
            // A thrust square onto the middle of a steel piece lying across it, pushed on 50 cm. A body of the same size
            // held the point; steel lets it slip off and round, so the drawn blade stays within a few centimeters of the
            // hand instead of 30 cm behind it.
            var steel = new[]
            {
                BladeObstacle.FromBladePiece(new Vector3(-0.45f, 1.0f, 1.0f), new Vector3(0.45f, 1.0f, 1.0f), -Vector3.UnitZ, false, false),
                BladeObstacle.Floor,
            };
            var drawn = new DrawnBlade();
            var mostLag = 0.0f;
            for (var frame = 0; frame < 144; frame++)
            {
                drawn.Step(BladeAt(0.0f, Math.Min(0.5f, frame * 0.01f)), steel, Vector3.UnitY, FrameSeconds);
                mostLag = Math.Max(mostLag, drawn.LagMeters);
            }

            Assert.That(mostLag, Is.LessThan(DrawnBlade.LeashMeters), "Never further off than the leash.");
            Assert.That(drawn.Contact.IsPinned, Is.False, "The point slipped off.");
        }

        [Test]
        public void TheGhostShowsOnlyPastItsThreshold()
        {
            // Pushed 1 cm past the most the blade bends into the wall: the drawn hilt lags 1 cm (the logic's lag), with no
            // ghost; 5 cm past, the ghost shows.
            var wall = WallObstacles();
            var mostBend = WallPinnedZ - BladePose.BladeLengthMeters + BladeContactBend.MaxPushMeters;

            var drawn = PushIntoWall(wall, mostBend + 0.01f);
            Assert.That(drawn.IsDecoupled, Is.True, "Setup: the drawn hilt lags.");
            Assert.That(drawn.LagMeters, Is.EqualTo(0.01f).Within(0.002f));
            Assert.That(drawn.IsGhostShown, Is.False);

            Assert.That(PushIntoWall(wall, mostBend + 0.05f).IsGhostShown, Is.True);
        }

        [Test]
        public void HigherFrictionHoldsThePointLonger()
        {
            // The point pinned on a ball, the hand moving sideways 1 cm a frame: how far the hand gets before the point
            // starts to slide.
            var slips = new List<float>();
            foreach (var friction in new[] { 0.2f, 1.2f })
            {
                var center = new Vector3(0.0f, 1.0f, 1.2f);
                var ball = new[] { BladeObstacle.FromCapsule(new Capsule(center, center, 0.2f), friction) };
                var drawn = new DrawnBlade();
                drawn.Step(BladeAt(0.0f, 0.0f), ball, Vector3.UnitY, FrameSeconds);
                drawn.Step(BladeAt(0.0f, 0.2f), ball, Vector3.UnitY, FrameSeconds);
                var x = 0.0f;
                while (drawn.Contact.IsPinned && Math.Abs(drawn.Contact.PinnedPoint.X) < 0.005f && x < 0.5f)
                {
                    x += 0.01f;
                    drawn.Step(BladeAt(x, 0.2f), ball, Vector3.UnitY, FrameSeconds);
                }

                slips.Add(x);
            }

            Assert.That(slips[1], Is.GreaterThan(slips[0]));
        }

        private static IReadOnlyList<BladeObstacle> WallObstacles()
        {
            return new BladeObstacle[] { new Capsule(new Vector3(-2.0f, 1.0f, 1.2f), new Vector3(2.0f, 1.0f, 1.2f), 0.2f) };
        }

        /// <summary>The hand's guard pushed in at 1 cm a frame to <paramref name="guardZ"/>, the point pinned on the wall.</summary>
        private static DrawnBlade PushIntoWall(IReadOnlyList<BladeObstacle> wall, float guardZ)
        {
            var drawn = new DrawnBlade();
            for (var z = 0.0f; z < guardZ; z += 0.01f)
            {
                drawn.Step(BladeAt(0.0f, z), wall, Vector3.UnitY, FrameSeconds);
            }

            drawn.Step(BladeAt(0.0f, guardZ), wall, Vector3.UnitY, FrameSeconds);
            return drawn;
        }

        private static BladeSegment BladeAt(float x, float guardZ)
        {
            return new BladeSegment(new Vector3(x, 1.0f, guardZ), new Vector3(x, 1.0f, guardZ + BladePose.BladeLengthMeters));
        }
    }
}
