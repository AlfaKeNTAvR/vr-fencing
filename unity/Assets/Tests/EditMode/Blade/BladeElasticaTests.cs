// The pressed blade's shape (PER-50): the elastica of a blade clamped at the guard and pinned at the point. One
// smooth curve, no curvature at the point (never a tip hook), the blade's full length, bulging to the side the hand's
// blade points to from the line to the point (so it curves over toward the point), the set deciding a straight push.
// Meters and seconds; the Fencer faces +z.
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Blade;

namespace VrFencing.Tests.Blade
{
    public class BladeElasticaTests
    {
        private const float FrameSeconds = 1.0f / 72.0f;

        // A wall facing the Fencer at z = 1.
        private static readonly IReadOnlyList<BladeObstacle> Wall = new[] { BladeObstacle.FromPlane(new Vector3(0.0f, 0.0f, 1.0f), -Vector3.UnitZ) };
        private static readonly IReadOnlyList<BladeObstacle> FloorOnly = new[] { BladeObstacle.Floor };

        [Test]
        public void APressedBladeHasNoCurvatureAtThePointAndBendsMostTowardTheGuard()
        {
            var drawn = PressStraightIntoTheWall(0.12f, Vector3.UnitY);

            var curvature = MeasureCurvature(drawn);
            var largest = curvature.Max();
            var largestAt = curvature.IndexOf(largest);
            Assert.That(largest, Is.GreaterThan(1.0f), "Setup: the blade is bent.");
            Assert.That(ExtrapolateToThePoint(curvature), Is.LessThan(largest * 0.1f), "No curvature at the point.");
            Assert.That(largestAt, Is.LessThan(curvature.Count - 4), "The sharpest bend is not at the point.");
            Assert.That(MeasureLength(drawn), Is.EqualTo(BladePose.BladeLengthMeters).Within(BladePose.BladeLengthMeters * 0.01f));
            Assert.That(drawn.GetCenterlinePoint(1.0f).Z, Is.EqualTo(1.0f - BladeContactBend.PointRadiusMeters).Within(1e-3f), "The point stays on the wall.");
        }

        [Test]
        public void TheFloorThreePoseBowsInOneArcAboveTheFloorWithNoHook()
        {
            // Nikita's floor-3 screenshot: the guard about 0.6 m above the floor, the blade pointing down and ahead, the
            // point pressed into the floor; it drew straight with a question-mark hook at the tip.
            var direction = Vector3.Normalize(new Vector3(0.0f, -0.6f, 0.55f));
            var gripUp = Vector3.Normalize(new Vector3(0.0f, 0.55f, 0.6f));
            var touchdown = new Vector3(0.0f, BladeContactBend.PointRadiusMeters, 0.5f);
            var drawn = new DrawnBlade();
            for (var push = -0.05f; push <= 0.10f + 1e-4f; push += 0.01f)
            {
                var guard = touchdown - (direction * (BladePose.BladeLengthMeters - push));
                drawn.Step(new BladeSegment(guard, guard + (direction * BladePose.BladeLengthMeters)), FloorOnly, gripUp, FrameSeconds);
            }

            Assert.That(drawn.GetSample(0).Y, Is.InRange(0.55f, 0.70f), "Setup: the guard is about 0.6 m up.");
            Assert.That(drawn.Contact.IsPinned, Is.True, "Setup: pressed into the floor.");
            Assert.That(drawn.IsDecoupled, Is.False, "10 cm of push is within the bend.");
            for (var index = 0; index <= DrawnBlade.SampleCount; index++)
            {
                Assert.That(drawn.GetSample(index).Y, Is.GreaterThan(0.0f), $"Point {index} is below the floor.");
            }

            var curvature = MeasureCurvature(drawn);
            Assert.That(ExtrapolateToThePoint(curvature), Is.LessThan(curvature.Max() * 0.1f), "No hook: no curvature at the point.");
            var chord = Vector3.Normalize(drawn.GetSample(DrawnBlade.SampleCount) - drawn.GetSample(0));
            var lastPiece = Vector3.Normalize(drawn.GetSample(DrawnBlade.SampleCount) - drawn.GetSample(DrawnBlade.SampleCount - 1));
            Assert.That(Vector3.Dot(lastPiece, chord), Is.GreaterThan(0.3f), "The point is not turned back.");
            Assert.That(MeasureLength(drawn), Is.EqualTo(BladePose.BladeLengthMeters).Within(BladePose.BladeLengthMeters * 0.01f));
        }

        [Test]
        public void ThePointBelowTheHandsLineBendsTheBladeDownTowardIt([Values(1.0f, -1.0f)] float side)
        {
            // Pinned straight ahead, then the hand rises (side 1) or drops (side -1) 6 cm and pushes 8 cm on, level: the
            // point is below (above) the hand's line. The blade leaves the guard level and curves down (up) to the point,
            // bulging up (down) from the line between them.
            var drawn = new DrawnBlade();
            drawn.Step(LevelBladeWithGuardAt(new Vector3(0.0f, 1.0f, 0.05f)), Wall, Vector3.UnitY, FrameSeconds);
            drawn.Step(LevelBladeWithGuardAt(new Vector3(0.0f, 1.0f, 0.12f)), Wall, Vector3.UnitY, FrameSeconds);
            Assert.That(drawn.Contact.IsPinned, Is.True);
            for (var step = 1; step <= 6; step++)
            {
                drawn.Step(LevelBladeWithGuardAt(new Vector3(0.0f, 1.0f + (side * step * 0.01f), 0.12f + (step * 0.01f))), Wall, Vector3.UnitY, FrameSeconds);
            }

            Assert.That(drawn.IsDecoupled, Is.False);
            var guard = drawn.GetSample(0);
            var tip = drawn.GetSample(DrawnBlade.SampleCount);
            Assert.That((tip.Y - guard.Y) * side, Is.LessThan(-0.05f), "Setup: the point is off the hand's line.");
            var lastPiece = tip - drawn.GetSample(DrawnBlade.SampleCount - 1);
            Assert.That(lastPiece.Y * side, Is.LessThan(0.0f), "The blade turns toward the point.");
            Assert.That(FindBulge(drawn, Vector3.UnitY) * side, Is.GreaterThan(0.01f), "It bulges on the hand's side of the line to the point.");
        }

        [Test]
        public void AStraightPushBulgesTowardTheGripsPalmSideAndTurnsWithTheWrist()
        {
            // The same straight push with the wrist rolled 0, 90 and 180 degrees: the set turns with the grip.
            Assert.That(FindBulge(PressStraightIntoTheWall(0.08f, Vector3.UnitY), Vector3.UnitY), Is.LessThan(-0.05f), "Thumb up: bulges down.");
            Assert.That(FindBulge(PressStraightIntoTheWall(0.08f, Vector3.UnitX), Vector3.UnitX), Is.LessThan(-0.05f), "Rolled 90: bulges the other way across.");
            Assert.That(FindBulge(PressStraightIntoTheWall(0.08f, -Vector3.UnitY), Vector3.UnitY), Is.GreaterThan(0.05f), "Rolled 180: bulges up.");
        }

        [Test]
        public void RollingTheWristDuringAStraightPressTurnsTheBulgeWithIt()
        {
            var drawn = PressStraightIntoTheWall(0.08f, Vector3.UnitY);
            var held = LevelBladeWithGuardAt(new Vector3(0.0f, 1.0f, 0.18f));
            for (var degrees = 10; degrees <= 90; degrees += 10)
            {
                var radians = degrees * (float)System.Math.PI / 180.0f;
                drawn.Step(held, Wall, new Vector3((float)System.Math.Sin(radians), (float)System.Math.Cos(radians), 0.0f), FrameSeconds);
            }

            Assert.That(FindBulge(drawn, Vector3.UnitX), Is.LessThan(-0.05f), "The bulge rolled a quarter turn with the grip.");
        }

        [Test]
        public void SmallJitterDuringAStraightPressNeverFlipsTheBulge()
        {
            var drawn = PressStraightIntoTheWall(0.08f, Vector3.UnitY);
            var random = new System.Random(50);
            for (var frame = 0; frame < 200; frame++)
            {
                // Up to 1.5 cm of hand offset either way across the line: inside the flip band.
                var jitter = new Vector3((float)(random.NextDouble() - 0.5), (float)(random.NextDouble() - 0.5), 0.0f) * 0.03f;
                drawn.Step(LevelBladeWithGuardAt(new Vector3(0.0f, 1.0f, 0.18f) + jitter), Wall, Vector3.UnitY, FrameSeconds);
                Assert.That(FindBulge(drawn, Vector3.UnitY), Is.LessThan(0.0f), $"Frame {frame}: the bulge flipped.");
            }
        }

        [Test]
        public void TheShapeIsFoundWellWithinAFrameBudget()
        {
            // A press that moves every frame (push, sideways and back): the whole drawn-blade step, solver included.
            var drawn = PressStraightIntoTheWall(0.05f, Vector3.UnitY);
            var poses = Enumerable.Range(0, 400).Select(frame =>
            {
                var phase = frame * 0.05f;
                return LevelBladeWithGuardAt(new Vector3(0.06f * (float)System.Math.Sin(phase), 1.0f + (0.04f * (float)System.Math.Cos(phase * 0.7f)), 0.15f + (0.04f * (float)System.Math.Sin(phase * 1.3f))));
            }).ToList();
            foreach (var pose in poses.Take(50))
            {
                drawn.Step(pose, Wall, Vector3.UnitY, FrameSeconds);
            }

            var watch = Stopwatch.StartNew();
            foreach (var pose in poses)
            {
                drawn.Step(pose, Wall, Vector3.UnitY, FrameSeconds);
            }

            watch.Stop();
            var millisecondsPerFrame = watch.Elapsed.TotalMilliseconds / poses.Count;
            TestContext.WriteLine($"Drawn blade step while pressed: {millisecondsPerFrame:0.000} ms per frame (editor).");
            Assert.That(drawn.Contact.IsPinned, Is.True);
            Assert.That(millisecondsPerFrame, Is.LessThan(0.25), "Well under the 13.9 ms of a 72 Hz frame, with room for the Quest's slower CPU.");

            var solveWatch = Stopwatch.StartNew();
            var shape = default(ElasticaShape);
            for (var index = 0; index < 1000; index++)
            {
                BladeArc.TrySolve(0.80f + (0.0001f * (index % 50)), -0.02f, ref shape);
            }

            solveWatch.Stop();
            TestContext.WriteLine($"Warm elastica solve: {solveWatch.Elapsed.TotalMilliseconds / 1000.0 * 1000.0:0.0} us each (editor).");
            Assert.That(solveWatch.Elapsed.TotalMilliseconds / 1000.0, Is.LessThan(0.05));
        }

        /// <summary>Pinned on the wall straight ahead at y = 1, then pushed straight in by <paramref name="pushMeters"/>.</summary>
        private static DrawnBlade PressStraightIntoTheWall(float pushMeters, Vector3 gripUp)
        {
            var drawn = new DrawnBlade();
            var touching = 1.0f - BladeContactBend.PointRadiusMeters - BladePose.BladeLengthMeters;
            drawn.Step(LevelBladeWithGuardAt(new Vector3(0.0f, 1.0f, touching - 0.02f)), Wall, gripUp, FrameSeconds);
            for (var push = 0.01f; push <= pushMeters + 1e-4f; push += 0.01f)
            {
                drawn.Step(LevelBladeWithGuardAt(new Vector3(0.0f, 1.0f, touching + push)), Wall, gripUp, FrameSeconds);
            }

            Assert.That(drawn.Contact.IsPinned, Is.True, "Setup: pinned on the wall.");
            Assert.That(drawn.IsDecoupled, Is.False, "Setup: within the bend.");
            return drawn;
        }

        private static BladeSegment LevelBladeWithGuardAt(Vector3 guard)
        {
            return new BladeSegment(guard, guard + (Vector3.UnitZ * BladePose.BladeLengthMeters));
        }

        /// <summary>The farthest offset of the drawn blade from the line from its guard to its point, measured along <paramref name="axis"/>.</summary>
        private static float FindBulge(DrawnBlade drawn, Vector3 axis)
        {
            var guard = drawn.GetSample(0);
            var chord = Vector3.Normalize(drawn.GetSample(DrawnBlade.SampleCount) - guard);
            var farthest = 0.0f;
            for (var index = 0; index <= DrawnBlade.SampleCount; index++)
            {
                var offset = drawn.GetSample(index) - guard;
                var across = Vector3.Dot(offset - (Vector3.Dot(offset, chord) * chord), axis);
                if (System.Math.Abs(across) > System.Math.Abs(farthest))
                {
                    farthest = across;
                }
            }

            return farthest;
        }

        /// <summary>The curvature at every inner sample, 1/m: the turn between neighbouring pieces over the piece length.</summary>
        private static List<float> MeasureCurvature(DrawnBlade drawn)
        {
            var curvature = new List<float>();
            for (var index = 1; index < DrawnBlade.SampleCount; index++)
            {
                var before = drawn.GetSample(index) - drawn.GetSample(index - 1);
                var after = drawn.GetSample(index + 1) - drawn.GetSample(index);
                var radians = SegmentMath.FindAngleDegrees(before, after) * (float)System.Math.PI / 180.0f;
                curvature.Add(radians / (0.5f * (before.Length() + after.Length())));
            }

            return curvature;
        }

        /// <summary>The curvature at the point itself, extrapolated from the last two inner samples (one piece apart).</summary>
        private static float ExtrapolateToThePoint(List<float> curvature)
        {
            var last = curvature[curvature.Count - 1];
            var before = curvature[curvature.Count - 2];
            return System.Math.Abs(last + (last - before));
        }

        private static float MeasureLength(DrawnBlade drawn)
        {
            var length = 0.0f;
            for (var index = 1; index <= DrawnBlade.SampleCount; index++)
            {
                length += Vector3.Distance(drawn.GetSample(index - 1), drawn.GetSample(index));
            }

            return length;
        }
    }
}
