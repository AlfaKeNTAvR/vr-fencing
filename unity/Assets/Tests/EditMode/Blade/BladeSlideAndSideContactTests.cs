// PER-69: the point slides by friction from the blade's real push, the bend starts on the free side of the surface and
// never snaps over, the point never curls past perpendicular to the surface, and the blade's side pressed on the Dummy
// bends round it. Poses go through the rest shape (droop) the way the app does. Meters and seconds; +x ahead, +y up.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Blade;
using VrFencing.Dummy;

namespace VrFencing.Tests.Blade
{
    public class BladeSlideAndSideContactTests
    {
        private const float FrameSeconds = 1.0f / 72.0f;

        // The default Blade set (0.7 cm sagitta) and Normal flex sag (3 cm), as in the app.
        private const float SetSagittaMeters = 0.007f;
        private const float SagMeters = BladeDroop.NormalSagMeters;

        // The drawn point's direction may turn at most this much from one frame to the next while pressed.
        private const float MaxPointTurnDegrees = 5.0f;

        // Over the first centimeter of push the point lands and the bend sets in: the turn limit counts from there.
        private const float LandingPushMeters = 0.01f;

        private static readonly IReadOnlyList<BladeObstacle> FloorOnly = new[] { BladeObstacle.Floor };

        // A Dummy-sized torso standing at x = 0.8: upright capsule, radius 15 cm.
        private static readonly Capsule Torso = new Capsule(new Vector3(0.8f, 0.8f, 0.0f), new Vector3(0.8f, 1.5f, 0.0f), 0.15f);

        [Test]
        public void TheFloorIsSlipperyAndTheJacketGrips()
        {
            Assert.That(BladeObstacle.Floor.FrictionCoefficient, Is.EqualTo(BladeObstacle.FloorFriction));
            Assert.That(((BladeObstacle)Torso).FrictionCoefficient, Is.EqualTo(BladeObstacle.JacketFriction));
            Assert.That(BladeObstacle.FloorFriction, Is.LessThan(BladeObstacle.JacketFriction));
            Assert.Throws<ArgumentOutOfRangeException>(() => BladeObstacle.FromPlane(Vector3.Zero, Vector3.UnitY, -0.1f));
        }

        [Test]
        public void AFloorSweepNeverBulgesIntoTheFloorFlipsOrJumps([Values(12.0f, 20.0f, 30.0f, 45.0f)] float pitch, [Values] bool lowersTheHand)
        {
            // Thumb up (the set and sag hang toward the floor): pushed in along the blade, or in and down, then back out.
            var start = GuardTouchingTheFloor(pitch);
            var direction = Forward(pitch);
            var end = lowersTheHand ? start + (direction * 0.10f) - new Vector3(0.0f, 0.08f, 0.0f) : start + (direction * 0.22f);
            var path = Go(start, end, 60).Concat(Go(end, start, 60)).Select(guard => (guard, pitch)).ToList();
            var press = Run(path, 0.0f, FloorOnly);

            Assert.That(press.WasPinned, Is.True, "Setup: the point pressed into the floor.");
            Assert.That(press.LowestY, Is.GreaterThanOrEqualTo(DrawnBlade.BladeRadiusMeters - 1e-4f), "Never below the floor.");
            Assert.That(press.MostBowIntoSurface, Is.LessThanOrEqualTo(0.0f), "The bend never leans into the floor.");
            Assert.That(press.LargestBowTurnDegrees, Is.LessThan(60.0f), "The bend never changes side.");
            Assert.That(press.LargestPointTurnDegrees, Is.LessThan(MaxPointTurnDegrees), "The point turns smoothly.");
            Assert.That(press.Released, Is.True, "Back out, the point let go.");
        }

        [Test]
        public void PushingInAndBackOutRetracesTheSameShape()
        {
            // The same hand poses on the way in and on the way out draw the same side and nearly the same bend.
            const float pitch = 30.0f;
            var start = GuardTouchingTheFloor(pitch);
            var end = start + (Forward(pitch) * 0.18f) - new Vector3(0.0f, 0.04f, 0.0f);
            var drawn = new DrawnBlade();
            var inward = new List<(bool pinned, float bulge, Vector3 bow)>();
            foreach (var guard in Go(start, end, 60))
            {
                Step(drawn, guard, pitch, 0.0f, FloorOnly);
                inward.Add((drawn.Contact.IsPinned, MeasureBulge(drawn), drawn.Contact.BowDirection));
            }

            var outward = new List<(bool pinned, float bulge, Vector3 bow)>();
            foreach (var guard in Go(end, start, 60))
            {
                Step(drawn, guard, pitch, 0.0f, FloorOnly);
                outward.Add((drawn.Contact.IsPinned, MeasureBulge(drawn), drawn.Contact.BowDirection));
            }

            // Way out, frame k is the hand pose of way in, frame 58 - k.
            var compared = 0;
            for (var frame = 0; frame < inward.Count - 1; frame++)
            {
                var back = outward[inward.Count - 2 - frame];
                if (inward[frame].pinned && back.pinned)
                {
                    compared++;
                    Assert.That(Vector3.Dot(inward[frame].bow, back.bow), Is.GreaterThan(0.0f), $"Frame {frame}: the same side both ways.");
                }
            }

            Assert.That(compared, Is.GreaterThan(30), "Setup: pressed most of the way.");
            Assert.That(inward.Max(sample => sample.bulge), Is.GreaterThan(0.03f), "Setup: it bent.");
        }

        [Test]
        public void AShallowPressSkidsTheSteepPressPins()
        {
            // Shallow: the push leans far off the floor's normal, past the floor's friction, so the point skids along
            // and its end never curls back past straight down.
            const float shallow = 20.0f;
            var start = GuardTouchingTheFloor(shallow);
            var end = start + (Forward(shallow) * 0.12f) - new Vector3(0.0f, 0.10f, 0.0f);
            var drawn = new DrawnBlade();
            float? firstPin = null;
            foreach (var guard in Go(start, end, 60))
            {
                Step(drawn, guard, shallow, 0.0f, FloorOnly);
                if (!drawn.Contact.IsPinned)
                {
                    continue;
                }

                firstPin ??= drawn.Contact.PinnedPoint.X;
                var lastPiece = drawn.GetSample(DrawnBlade.SampleCount) - drawn.GetSample(DrawnBlade.SampleCount - 1);
                Assert.That(lastPiece.X, Is.GreaterThanOrEqualTo(0.0f), "The point never curls back past perpendicular to the floor.");
            }

            Assert.That(firstPin.HasValue, Is.True, "Setup: pressed.");
            Assert.That(drawn.Contact.PinnedPoint.X - firstPin.Value, Is.GreaterThan(0.03f), "The point skidded forward.");

            // Steep: nearly straight down, the push stays inside the friction cone and the point holds.
            const float steep = 80.0f;
            start = GuardTouchingTheFloor(steep);
            end = start + (Forward(steep) * 0.10f);
            drawn = new DrawnBlade();
            firstPin = null;
            foreach (var guard in Go(start, end, 60))
            {
                Step(drawn, guard, steep, 0.0f, FloorOnly);
                if (drawn.Contact.IsPinned)
                {
                    firstPin ??= drawn.Contact.PinnedPoint.X;
                }
            }

            Assert.That(drawn.Contact.IsPinned, Is.True);
            Assert.That(drawn.Contact.PinnedPoint.X - firstPin.Value, Is.EqualTo(0.0f).Within(0.002f), "The point held where it landed.");
            Assert.That(drawn.Contact.BowMeters, Is.GreaterThan(0.05f), "And the blade bowed.");
        }

        [Test]
        public void WithTheWristRolled90TheBendDoesNotFlicker([Values(20.0f, 30.0f, 45.0f)] float pitch)
        {
            var start = GuardTouchingTheFloor(pitch);
            var end = start + (Forward(pitch) * 0.10f) - new Vector3(0.0f, 0.08f, 0.0f);
            var path = Go(start, end, 60).Concat(Go(end, start, 60)).Select(guard => (guard, pitch)).ToList();
            var press = Run(path, 90.0f, FloorOnly);

            Assert.That(press.WasPinned, Is.True, "Setup: pressed.");
            Assert.That(press.LowestY, Is.GreaterThanOrEqualTo(DrawnBlade.BladeRadiusMeters - 1e-4f));
            Assert.That(press.LargestBowTurnDegrees, Is.LessThan(60.0f), "The bend never swaps side in one frame.");
            Assert.That(press.BowReversals, Is.LessThanOrEqualTo(1), "The bend does not swing back and forth.");
        }

        [Test]
        public void AGlancingPressOnTheDummyDoesNotFlip([Values(0.08f, 0.11f)] float offset, [Values(-90.0f, 90.0f)] float roll)
        {
            // A level blade meets the round torso off its axis, at a glance, with the palm side toward the body or away.
            var bodies = new BladeObstacle[] { Torso, BladeObstacle.Floor };
            var contactX = Torso.Start.X - (float)Math.Sqrt((Torso.RadiusMeters * Torso.RadiusMeters) - (offset * offset));
            var start = new Vector3(contactX - BladePose.BladeLengthMeters - 0.03f, 1.2f, offset);
            var end = start + new Vector3(0.20f, 0.0f, 0.0f);
            var path = Go(start, end, 60).Concat(Go(end, start, 60)).Select(guard => (guard, 0.0f)).ToList();
            var press = Run(path, roll, bodies);

            Assert.That(press.WasPinned || press.WasOnSide, Is.True, "Setup: pressed, by the point or (more glancing) the side.");
            Assert.That(press.LargestBowTurnDegrees, Is.LessThan(60.0f), "The bend never changes side.");
            Assert.That(press.ClosestToTorso, Is.GreaterThan(0.0f), "Never in the body.");
        }

        [Test]
        public void AStraightPushIntoTheDummyStillBowsTheWayTheBladeHangs()
        {
            // PER-52: straight in with the thumb up, the blade bows down (the set and sag), the point holds (the jacket
            // grips), and the side does not change through the push and back.
            var bodies = new BladeObstacle[] { Torso, BladeObstacle.Floor };
            var start = new Vector3(Torso.Start.X - Torso.RadiusMeters - BladePose.BladeLengthMeters - 0.03f, 1.2f, 0.0f);
            var drawn = new DrawnBlade();
            Vector3? pin = null;
            foreach (var guard in Go(start, start + new Vector3(0.15f, 0.0f, 0.0f), 40))
            {
                Step(drawn, guard, 0.0f, 0.0f, bodies);
                if (drawn.Contact.IsPinned)
                {
                    pin ??= drawn.Contact.PinnedPoint;
                    Assert.That(drawn.Contact.BowDirection.Y, Is.LessThan(-0.5f), "Bows down.");
                }
            }

            Assert.That(pin.HasValue, Is.True, "Setup: pressed.");
            Assert.That(Vector3.Distance(drawn.Contact.PinnedPoint, pin.Value), Is.LessThan(0.002f), "The point held.");
            Assert.That(drawn.GetCenterlinePoint(0.5f).Y, Is.LessThan(1.2f - 0.08f), "A big bow down.");
        }

        [Test]
        public void TheSideOfTheBladePressedOnTheDummyBendsRoundItAndLetsGoSmoothly()
        {
            // The blade level and pointing ahead past the torso, its middle beside it; the hand sweeps the flat 15 cm
            // into the torso and back out.
            var bodies = new BladeObstacle[] { Torso, BladeObstacle.Floor };
            var outside = new Vector3(0.2f, 1.2f, Torso.RadiusMeters + 0.10f);
            var inside = new Vector3(0.2f, 1.2f, 0.0f);
            var drawn = new DrawnBlade();
            var wasOnSide = false;
            var largestBend = 0.0f;
            Vector3? lastTip = null;
            var largestTipStep = 0.0f;
            var letGo = false;
            foreach (var guard in Go(outside, inside, 60).Concat(Go(inside, outside, 60)))
            {
                var blade = new BladeSegment(guard, guard + (Vector3.UnitX * BladePose.BladeLengthMeters));
                drawn.Step(blade, bodies, Vector3.UnitY, FrameSeconds);
                Assert.That(drawn.Contact.IsPinned, Is.False, "The side is not the point: no pin, no Touch.");
                for (var index = 1; index <= DrawnBlade.SampleCount; index++)
                {
                    Assert.That(SegmentMath.FindDistanceToCapsule(drawn.GetSample(index), Torso), Is.GreaterThanOrEqualTo(DrawnBlade.BladeRadiusMeters - 1e-3f), $"Guard z {guard.Z:0.000}: point {index} in the body.");
                }

                Assert.That(DrawnShape.FindSharpestTurnDegrees(Enumerable.Range(0, DrawnBlade.SampleCount + 1).Select(drawn.GetSample).ToList()), Is.LessThan(20.0f), "One smooth curve.");
                if (drawn.IsOnSide)
                {
                    wasOnSide = true;
                    Assert.That(drawn.IsDecoupled, Is.False, $"Guard z {guard.Z:0.000}: the hilt stays in the hand while the blade bends.");
                    largestBend = Math.Max(largestBend, drawn.GetSample(DrawnBlade.SampleCount / 2).Z - guard.Z);
                }

                letGo |= wasOnSide && !drawn.IsOnSide;
                var tip = drawn.GetSample(DrawnBlade.SampleCount);
                if (lastTip.HasValue)
                {
                    largestTipStep = Math.Max(largestTipStep, Vector3.Distance(tip, lastTip.Value));
                }

                lastTip = tip;
            }

            Assert.That(wasOnSide, Is.True, "The side pressed the torso.");
            Assert.That(largestBend, Is.GreaterThan(0.05f), "The blade bowed round the torso instead of staying straight.");
            Assert.That(letGo, Is.True, "Swept back out, it let go.");
            Assert.That(largestTipStep, Is.LessThan(0.02f), "No jump in or out: the point moves under 2 cm a frame.");
            Assert.That(drawn.IsDecoupled, Is.False);
        }

        [Test]
        public void ASideSlidOffTheEndOfAThinArmSpringsFree()
        {
            // A forearm-thin upright capsule near the point; the blade crosses it at a slant, so sweeping the hand on
            // carries the contact toward the point until the point slips round the arm's end.
            var arm = new Capsule(new Vector3(0.8f, 0.8f, 0.0f), new Vector3(0.8f, 1.5f, 0.0f), 0.04f);
            var bodies = new BladeObstacle[] { arm };
            var direction = Vector3.Normalize(new Vector3(1.0f, 0.0f, 0.3f));
            var drawn = new DrawnBlade();
            var wasOnSide = false;
            foreach (var guard in Go(new Vector3(0.0f, 1.2f, -0.12f), new Vector3(0.0f, 1.2f, -0.42f), 60))
            {
                drawn.Step(new BladeSegment(guard, guard + (direction * BladePose.BladeLengthMeters)), bodies, Vector3.UnitY, FrameSeconds);
                wasOnSide |= drawn.IsOnSide;
                for (var index = 1; index <= DrawnBlade.SampleCount; index++)
                {
                    Assert.That(SegmentMath.FindDistanceToCapsule(drawn.GetSample(index), arm), Is.GreaterThan(0.0f), $"Guard z {guard.Z:0.000}: point {index} in the arm.");
                }
            }

            Assert.That(wasOnSide, Is.True, "Setup: the side pressed the arm.");
            Assert.That(drawn.IsOnSide, Is.False, "It slid off the end.");
            Assert.That(drawn.IsDecoupled, Is.False, "And follows the hand again.");
        }

        private static PressRecord Run(IReadOnlyList<(Vector3 guard, float pitch)> path, float rollDegrees, IReadOnlyList<BladeObstacle> bodies)
        {
            var record = new PressRecord { LowestY = float.MaxValue, ClosestToTorso = float.MaxValue };
            var drawn = new DrawnBlade();
            Vector3? lastBow = null;
            Vector3? lastPiece = null;
            float? lastSideways = null;
            foreach (var (guard, pitch) in path)
            {
                var release = Step(drawn, guard, pitch, rollDegrees, bodies);
                record.Released |= release != PinRelease.None;
                for (var index = 1; index <= DrawnBlade.SampleCount; index++)
                {
                    var sample = drawn.GetSample(index);
                    record.LowestY = Math.Min(record.LowestY, sample.Y);
                    record.ClosestToTorso = Math.Min(record.ClosestToTorso, SegmentMath.FindDistanceToCapsule(sample, Torso));
                }

                record.WasOnSide |= drawn.IsOnSide;
                if (!drawn.Contact.IsPinned)
                {
                    lastBow = null;
                    lastPiece = null;
                    lastSideways = null;
                    continue;
                }

                record.WasPinned = true;
                var bow = drawn.Contact.BowDirection;
                var piece = drawn.GetSample(DrawnBlade.SampleCount) - drawn.GetSample(DrawnBlade.SampleCount - 1);
                if (drawn.Contact.PinnedPoint.Y < 0.1f)
                {
                    record.MostBowIntoSurface = Math.Max(record.MostBowIntoSurface, -bow.Y - 0.5f);
                }

                if (lastBow.HasValue)
                {
                    record.LargestBowTurnDegrees = Math.Max(record.LargestBowTurnDegrees, SegmentMath.FindAngleDegrees(lastBow.Value, bow));
                    if (drawn.Contact.PushMeters > LandingPushMeters)
                    {
                        record.LargestPointTurnDegrees = Math.Max(record.LargestPointTurnDegrees, SegmentMath.FindAngleDegrees(lastPiece.Value, piece));
                    }
                }

                var sideways = bow.Z;
                if (lastSideways.HasValue && Math.Abs(sideways) > 0.2f && Math.Abs(lastSideways.Value) > 0.2f && Math.Sign(sideways) != Math.Sign(lastSideways.Value))
                {
                    record.BowReversals++;
                }

                if (Math.Abs(sideways) > 0.2f)
                {
                    lastSideways = sideways;
                }

                lastBow = bow;
                lastPiece = piece;
            }

            return record;
        }

        /// <summary>One step of the drawn blade for the forte at <paramref name="guard"/>, pitched down and rolled, through the rest shape as in the app.</summary>
        private static PinRelease Step(DrawnBlade drawn, Vector3 guard, float pitchDegrees, float rollDegrees, IReadOnlyList<BladeObstacle> bodies)
        {
            var direction = Forward(pitchDegrees);
            var pitch = pitchDegrees * (float)Math.PI / 180.0f;
            var up = new Vector3((float)Math.Sin(pitch), (float)Math.Cos(pitch), 0.0f);
            var side = Vector3.Cross(direction, up);
            var roll = rollDegrees * (float)Math.PI / 180.0f;
            var gripUp = (up * (float)Math.Cos(roll)) + (side * (float)Math.Sin(roll));
            var droop = BladeDroop.Create(direction, gripUp, -Vector3.UnitY, SetSagittaMeters, SagMeters);
            var forte = new BladeSegment(guard, guard + (direction * BladePose.BladeLengthMeters));
            return drawn.Step(droop.GetChord(forte), bodies, gripUp, FrameSeconds, droop);
        }

        private static Vector3 Forward(float pitchDegrees)
        {
            var pitch = pitchDegrees * (float)Math.PI / 180.0f;
            return new Vector3((float)Math.Cos(pitch), -(float)Math.Sin(pitch), 0.0f);
        }

        /// <summary>A guard whose drooped point is 2 cm above the floor at this pitch.</summary>
        private static Vector3 GuardTouchingTheFloor(float pitchDegrees)
        {
            var direction = Forward(pitchDegrees);
            var pitch = pitchDegrees * (float)Math.PI / 180.0f;
            var up = new Vector3((float)Math.Sin(pitch), (float)Math.Cos(pitch), 0.0f);
            var droop = BladeDroop.Create(direction, up, -Vector3.UnitY, SetSagittaMeters, SagMeters);
            var chord = droop.GetChord(new BladeSegment(Vector3.Zero, direction * BladePose.BladeLengthMeters));
            return new Vector3(0.0f, 0.02f - chord.TipPosition.Y, 0.0f);
        }

        private static IEnumerable<Vector3> Go(Vector3 from, Vector3 to, int frames)
        {
            return Enumerable.Range(1, frames).Select(frame => Vector3.Lerp(from, to, (float)frame / frames));
        }

        /// <summary>The drawn blade's farthest offset from the line between its ends.</summary>
        private static float MeasureBulge(DrawnBlade drawn)
        {
            var guard = drawn.GetSample(0);
            var line = Vector3.Normalize(drawn.GetSample(DrawnBlade.SampleCount) - guard);
            return Enumerable.Range(0, DrawnBlade.SampleCount + 1).Max(index =>
            {
                var offset = drawn.GetSample(index) - guard;
                return (offset - (Vector3.Dot(offset, line) * line)).Length();
            });
        }

        private sealed class PressRecord
        {
            public bool WasPinned;
            public bool WasOnSide;
            public bool Released;
            public float LowestY;
            public float ClosestToTorso;
            public float MostBowIntoSurface = float.MinValue;
            public float LargestBowTurnDegrees;
            public float LargestPointTurnDegrees;
            public int BowReversals;
        }
    }
}
