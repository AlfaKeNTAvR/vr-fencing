// Each blade's hilt (bell guard and grip) against the other blade (PER-122 fix, Nikita's headset test after PR 72: the
// Dummy's blade passed straight through his bell guard and handle, so a wedge did not work at all). The hilt is solid
// for the other blade inside the contact solver, by the same rules as a blade: that blade turns in its grip and bows,
// and past its limits slides off round the rim or off the face, never through and never with a hilt leaving its hand.
// The Fencer's blade lies along +z from its guard at z = 0.6; the Dummy's comes the other way. Meters, seconds.
using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Blade;
using VrFencing.Settings;

namespace VrFencing.Tests.Blade
{
    public class BladeHiltContactTests
    {
        private const float FrameSeconds = BladePairScene.FrameSeconds;
        private const float Tolerance = BladePairScene.ToleranceMeters;
        private static readonly Vector3 FencerGuard = new Vector3(0.0f, 1.1f, 0.6f);
        private static readonly BladeSegment DummyForte = new BladeSegment(new Vector3(0.0f, 1.1f, 1.6f), new Vector3(0.0f, 1.1f, 0.7f));

        [Test]
        public void TheDummysBladePressedTowardTheFencersGuardStopsOnTheBellAndNeverCrossesTheHandle(
            [Values(0.0f, 20.0f, 45.0f)] float degrees,
            [Values(0.0f, 0.03f)] float height)
        {
            // The Dummy's hand advances at 1 m/s so its blade, lying on the Fencer's (or 3 cm over it), slides along it
            // to the Fencer's guard and 40 cm on, past the bell and over the grip. It must stop on the bell's face or rim
            // and go round it, never into the bell or the grip, and neither hilt ever leaves its hand.
            var scene = new BladePairScene(hasDroop: false);
            var fencer = new BladeSegment(FencerGuard, FencerGuard + (Vector3.UnitZ * BladePose.BladeLengthMeters));
            var direction = Turned(-Vector3.UnitZ, degrees);
            var worst = float.MaxValue;
            var touched = 0;
            for (var crossingZ = 1.3f; crossingZ > 0.2f; crossingZ -= 1.0f * FrameSeconds)
            {
                var crossing = new Vector3(0.0f, FencerGuard.Y + BladeContactSolver.ContactMeters - 0.01f + height, crossingZ);
                var guard = crossing - (direction * 0.6f);
                scene.Frame(fencer, new BladeSegment(guard, guard + (direction * BladePose.BladeLengthMeters)));
                var clearance = FindHiltClearance(scene.DummySamples(), scene.Fencer.GuardPosition, scene.Fencer.HiltDirection);
                worst = Math.Min(worst, clearance);
                touched += scene.Contact.IsBOnHiltA ? 1 : 0;
                Assert.That(clearance, Is.GreaterThan(DrawnBlade.BladeRadiusMeters - Tolerance), $"Crossing at z {crossingZ:0.000}: the Dummy's drawn blade is in the Fencer's hilt.");
                Assert.That(scene.Dummy.Drawn.IsDecoupled, Is.False, $"Crossing at z {crossingZ:0.000}: the Dummy's drawn hilt left its hand.");
            }

            TestContext.WriteLine($"Closest to the Fencer's hilt: {worst * 100.0f:0.00} cm (centerline to surface).");
            Assert.That(touched, Is.GreaterThan(0), "The Dummy's blade met the Fencer's hilt.");
        }

        [Test]
        public void TheFencersBladePressedTowardTheDummysGuardStopsOnTheBellAndNeverCrossesTheHandle(
            [Values(0.0f, 20.0f, 45.0f)] float degrees,
            [Values(0.0f, 0.03f)] float height)
        {
            // The reverse: the Fencer's hand advances at 1 m/s, its blade on the Dummy's (or 3 cm over it), sliding to the
            // Dummy's guard and 30 cm on. The same rules keep it out of the Dummy's bell and grip.
            var scene = new BladePairScene(hasDroop: false);
            var direction = Turned(Vector3.UnitZ, degrees);
            var worst = float.MaxValue;
            var touched = 0;
            for (var crossingZ = 0.9f; crossingZ < 1.9f; crossingZ += 1.0f * FrameSeconds)
            {
                var crossing = new Vector3(0.0f, DummyForte.GuardPosition.Y + BladeContactSolver.ContactMeters - 0.01f + height, crossingZ);
                var guard = crossing - (direction * 0.6f);
                scene.Frame(new BladeSegment(guard, guard + (direction * BladePose.BladeLengthMeters)), DummyForte);
                var clearance = FindHiltClearance(scene.FencerSamples(), scene.Dummy.Drawn.GuardPosition, scene.Dummy.Drawn.HiltDirection);
                worst = Math.Min(worst, clearance);
                touched += scene.Contact.IsAOnHiltB ? 1 : 0;
                Assert.That(clearance, Is.GreaterThan(DrawnBlade.BladeRadiusMeters - Tolerance), $"Crossing at z {crossingZ:0.000}: the Fencer's drawn blade is in the Dummy's hilt.");
            }

            TestContext.WriteLine($"Closest to the Dummy's hilt: {worst * 100.0f:0.00} cm (centerline to surface).");
            Assert.That(touched, Is.GreaterThan(0), "The Fencer's blade met the Dummy's hilt.");
        }

        [Test]
        public void TheHiltContactIsSymmetricAndDeterministic()
        {
            // A blade pressed across the other's bell, solved with the blades as A and B and then swapped: each blade is
            // given the same either way, and the same again on a second run.
            var dummyGuard = new Vector3(0.0f, 1.1f, 1.6f);
            var bell = Hold(new BladeSegment(dummyGuard, dummyGuard - (Vector3.UnitZ * BladePose.BladeLengthMeters)));
            var first = new BladeContactSolver();
            var swapped = new BladeContactSolver();
            var again = new BladeContactSolver();
            for (var above = 0.12f; above > 0.03f; above -= 0.5f * FrameSeconds)
            {
                var guard = new Vector3(-0.3f, 1.1f + above, 1.62f);
                var across = Hold(new BladeSegment(guard, guard + (Vector3.UnitX * BladePose.BladeLengthMeters)));
                first.Solve(across, bell, FrameSeconds);
                swapped.Solve(bell, across, FrameSeconds);
                again.Solve(across, bell, FrameSeconds);
                Assert.That(Vector3.Distance(first.GiveA.Chord.TipPosition, swapped.GiveB.Chord.TipPosition), Is.LessThan(1e-5f), "The same either way round.");
                Assert.That(Vector3.Distance(first.GiveB.Chord.TipPosition, swapped.GiveA.Chord.TipPosition), Is.LessThan(1e-5f));
                Assert.That(first.GiveA.Chord.TipPosition, Is.EqualTo(again.GiveA.Chord.TipPosition), "The same on a second run.");
            }

            Assert.That(first.IsAOnHiltB, Is.True, "Setup: pressed on the bell.");
            Assert.That(first.GiveA.TurnDegrees + (first.GiveA.BowMeters * 100.0f), Is.GreaterThan(0.1f), "The blade on the bell gave.");
            Assert.That(first.GiveB.TurnDegrees, Is.EqualTo(0.0f), "The hilt's own blade did not: its hand holds it.");
        }

        /// <summary>The least distance of the drawn centerline from the hilt's surface (bell and grip); negative inside.</summary>
        internal static float FindHiltClearance(IReadOnlyList<Vector3> samples, Vector3 guard, Vector3 axis)
        {
            // The bell, and the grip as a round cylinder from the bell's back face to past the pommel.
            var center = BellGuard.FindCenter(guard, axis);
            const float GripHalf = BladeContactSolver.GripLengthMeters * 0.5f;
            var gripCenter = guard - (axis * (BladePose.GuardDepthMeters + GripHalf));
            var least = float.MaxValue;
            for (var piece = 0; piece + 1 < samples.Count; piece++)
            {
                for (var step = 0; step <= 10; step++)
                {
                    var point = Vector3.Lerp(samples[piece], samples[piece + 1], step / 10.0f);
                    least = Math.Min(least, BellGuard.FindDistance(point, center, axis, out _));
                    least = Math.Min(least, BellGuard.FindCylinderDistance(point, gripCenter, axis, BladeContactSolver.GripRadiusMeters, GripHalf, out _));
                }
            }

            return least;
        }

        private static Vector3 Turned(Vector3 direction, float degrees)
        {
            return Vector3.Transform(direction, Quaternion.CreateFromAxisAngle(Vector3.UnitY, degrees * (float)Math.PI / 180.0f));
        }

        private static BladeHold Hold(BladeSegment chord)
        {
            return new BladeHold(chord, default, BladeContactSolver.FencerGripStiffness, BladeContactSolver.NormalBendStiffness);
        }
    }
}
