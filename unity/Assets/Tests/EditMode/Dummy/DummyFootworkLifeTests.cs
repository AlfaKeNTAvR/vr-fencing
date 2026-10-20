// The life layer and the variety of the Dummy's footwork (PER-111): a standing Dummy breathes and shifts its weight
// with its feet planted and its moves continuous; the motion repeats for a seed but never with itself; no two steps
// look the same; and a Dummy nobody moves holds a move's pose but keeps breathing when it stands.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Tests.Dummy
{
    public class DummyFootworkLifeTests
    {
        private const float FrameSeconds = 1.0f / 72.0f;
        private const float Height = 1.8f;
        private const float Start = 2.0f;

        [Test]
        public void AStandingDummyBreathesAndShiftsItsWeightOnPlantedFeet()
        {
            var footwork = new DummyFootwork(Height, Hand.Right, Start, new Random(4));
            var rest = DummyLegs.EnGarde(Height, Hand.Right);
            var hipsY = new List<float>();
            var hipsZ = new List<float>();
            var previousHip = footwork.Legs.Front.Hip;

            for (var frame = 0; frame < 72 * 20; frame++)
            {
                footwork.Step(Start, FrameSeconds);
                var legs = footwork.Legs;
                Assert.That(footwork.CurrentStep, Is.EqualTo(FootworkStep.Standing));
                Assert.That(Vector3.Distance(legs.Front.Heel, rest.Front.Heel), Is.LessThan(1e-3f), $"Frame {frame}: the front foot moved.");
                Assert.That(Vector3.Distance(legs.Back.Toe, rest.Back.Toe), Is.LessThan(1e-3f), $"Frame {frame}: the back foot moved.");
                Assert.That(Vector3.Distance(legs.Front.Hip, previousHip), Is.LessThan(0.001f), $"Frame {frame}: the hips jumped.");
                previousHip = legs.Front.Hip;
                hipsY.Add(legs.Front.Hip.Y - rest.Front.Hip.Y);
                hipsZ.Add(legs.Front.Hip.Z - rest.Front.Hip.Z);
            }

            var breath = FootworkTuning.BreathHipRisePerHeight * Height;
            var shift = FootworkTuning.WeightShiftPerHeight * Height;
            Assert.That(hipsY.Max() - hipsY.Min(), Is.EqualTo(breath).Within(0.2f * breath), "It breathes.");
            Assert.That(hipsZ.Max() - hipsZ.Min(), Is.GreaterThan(shift), "Its weight shifts fore and aft.");
            Assert.That(hipsZ.Select(Math.Abs).Max(), Is.LessThanOrEqualTo(shift + 1e-4f));
            Assert.That(footwork.DistanceMeters, Is.EqualTo(Start), "Breathing does not move the Dummy.");
        }

        [Test]
        public void TheLifeMotionRepeatsForASeedButNotForAnother()
        {
            var first = HipTrack(new Random(8));
            var again = HipTrack(new Random(8));
            var other = HipTrack(new Random(9));

            Assert.That(again, Is.EqualTo(first));
            Assert.That(other, Is.Not.EqualTo(first));
        }

        [Test]
        public void TheLifeMotionDoesNotRepeatItself()
        {
            var footwork = new DummyFootwork(Height, Hand.Right, Start, new Random(1));
            var track = new List<Vector3>();
            for (var frame = 0; frame < 72 * 30; frame++)
            {
                footwork.Hold(FrameSeconds);
                track.Add(footwork.Legs.Front.Hip);
            }

            // Compare 3 s stretches 7.3 s apart (the slow wave's period) and 3.5 s apart (a nominal breath): never the same.
            foreach (var lag in new[] { (int)(7.3f * 72), (int)(3.5f * 72) })
            {
                var largest = 0.0f;
                for (var frame = 0; frame < 72 * 3; frame++)
                {
                    largest = Math.Max(largest, Vector3.Distance(track[frame + 72], track[frame + 72 + lag]));
                }

                Assert.That(largest, Is.GreaterThan(0.002f), $"It repeats after {lag} frames.");
            }
        }

        [Test]
        public void NoTwoStepsLookTheSame()
        {
            var footwork = new DummyFootwork(Height, Hand.Right, Start, new Random(21), isAlive: false);
            var stride = FootworkTuning.MaxStridePerHeight * Height;
            var floor = DummyLegs.EnGarde(Height, Hand.Right).Front.Heel.Y;
            var steps = new List<(int Frames, float Lift, float Dip)>();
            var target = Start - (6.0f * stride);
            var frames = 0;
            var lift = 0.0f;
            var dip = 0.0f;
            var restHip = footwork.Legs.Front.Hip.Y;
            var countBefore = 0;

            for (var frame = 0; frame < 72 * 10 && steps.Count < 6; frame++)
            {
                footwork.Step(target, FrameSeconds);
                if (footwork.StepCount != countBefore && countBefore > 0)
                {
                    steps.Add((frames, lift, dip));
                    frames = 0;
                    lift = 0.0f;
                    dip = 0.0f;
                }

                countBefore = footwork.StepCount;
                frames++;
                lift = Math.Max(lift, footwork.Legs.Front.Heel.Y - floor);
                dip = Math.Max(dip, restHip - footwork.Legs.Front.Hip.Y);
            }

            Assert.That(steps, Has.Count.EqualTo(5));
            for (var a = 0; a < steps.Count; a++)
            {
                for (var b = a + 1; b < steps.Count; b++)
                {
                    var isSame = steps[a].Frames == steps[b].Frames
                        && Math.Abs(steps[a].Lift - steps[b].Lift) < 0.002f
                        && Math.Abs(steps[a].Dip - steps[b].Dip) < 0.001f;
                    Assert.That(isSame, Is.False, $"Steps {a} and {b} look the same.");
                }
            }

            Assert.That(steps.Select(step => step.Dip).Max(), Is.LessThan(0.02f * Height), "The hips dip only a little.");
        }

        [Test]
        public void HoldFreezesAMoveButBreathesWhileStanding()
        {
            var footwork = new DummyFootwork(Height, Hand.Right, Start, new Random(2));
            footwork.Step(Start - 0.3f, FrameSeconds);
            footwork.Step(Start - 0.3f, FrameSeconds);
            var distance = footwork.DistanceMeters;
            var toe = footwork.Legs.Front.Toe;

            for (var frame = 0; frame < 72; frame++)
            {
                footwork.Hold(FrameSeconds);
            }

            Assert.That(footwork.CurrentStep, Is.EqualTo(FootworkStep.Advancing), "Held mid-step.");
            Assert.That(footwork.DistanceMeters, Is.EqualTo(distance));
            Assert.That(Vector3.Distance(footwork.Legs.Front.Toe, toe), Is.LessThan(0.02f), "Only the hips breathe.");

            var standing = new DummyFootwork(Height, Hand.Right, Start, new Random(2));
            var hip = standing.Legs.Front.Hip;
            for (var frame = 0; frame < 72; frame++)
            {
                standing.Hold(FrameSeconds);
            }

            Assert.That(Vector3.Distance(standing.Legs.Front.Hip, hip), Is.GreaterThan(1e-4f), "A standing Dummy breathes.");
        }

        [Test]
        public void PlacingTheDummyStandsItInEnGardeThereAndEndsAnyMove()
        {
            var footwork = new DummyFootwork(Height, Hand.Right, Start, new Random(2), isAlive: false);
            footwork.Step(Start - 0.3f, FrameSeconds);

            footwork.PlaceAt(4.0f);

            Assert.That(footwork.CurrentStep, Is.EqualTo(FootworkStep.Standing));
            Assert.That(footwork.DistanceMeters, Is.EqualTo(4.0f));
            Assert.That(Vector3.Distance(footwork.Legs.Front.Toe, DummyLegs.EnGarde(Height, Hand.Right).Front.Toe), Is.LessThan(1e-4f));
            Assert.Throws<ArgumentOutOfRangeException>(() => footwork.PlaceAt(float.PositiveInfinity));
        }

        private static List<Vector3> HipTrack(Random random)
        {
            var footwork = new DummyFootwork(Height, Hand.Right, Start, random);
            var track = new List<Vector3>();
            for (var frame = 0; frame < 72 * 5; frame++)
            {
                footwork.Step(Start, FrameSeconds);
                track.Add(footwork.Legs.Back.Hip);
            }

            return track;
        }
    }
}
