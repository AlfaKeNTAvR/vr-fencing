// The Fencer's blade against the Armed Dummy's (PER-104, redesigned in PER-122): stepped as the app steps them, the
// drawn blades never pass through each other, at real attack speeds and in a nearly parallel bind sliding to the guard;
// the drawn hilt never leaves the hand (PER-121's wedge recipe); the Dummy's blade bends from contact and its hand gives
// way to a press and comes back; the "Opponent's hand" Setting makes it give more or less; all within the frame
// budget. The Dummy's blade lies along the Strip at y = 1.1, its guard at z = 1.6 and its point toward the Fencer; the
// Fencer's blade lies across it along +x at z = 1.0 (30 cm from the Fencer's guard). Meters, seconds, Strip frame.
using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Blade;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Tests.Blade
{
    public class BladeOnBladeTests
    {
        private const float FrameSeconds = BladePairScene.FrameSeconds;
        private const float DummyHeight = 1.1f;
        private const float CrossingZ = 1.0f;
        private const float Tolerance = BladePairScene.ToleranceMeters;
        private const float Contact = BladeContactSolver.ContactMeters;

        private static readonly BladeSegment DummyForte = new BladeSegment(new Vector3(0.0f, DummyHeight, 1.6f), new Vector3(0.0f, DummyHeight, 0.7f));

        [Test]
        public void AFastCrossingAt3MetersPerSecondDoesNotTunnel()
        {
            var scene = new BladePairScene();
            var height = DummyHeight + 0.10f;
            for (var frame = 0; frame < 72; frame++)
            {
                height = Math.Max(DummyHeight - 0.20f, height - (3.0f * FrameSeconds));
                scene.Frame(Across(height), DummyForte);
                if (scene.Contact.Side.Value.Y > 0.0f)
                {
                    Assert.That(scene.FindSeparationAlong(Vector3.UnitY), Is.GreaterThan(Contact - Tolerance), $"Frame {frame}: through.");
                }
                else
                {
                    Assert.That(BladePairScene.FindSeparation(scene.FencerSamples(), scene.DummySamples(), -Vector3.UnitY), Is.GreaterThan(Contact - Tolerance), $"Frame {frame}: through after sliding off.");
                }
            }

            TestContext.WriteLine($"Side at the end {scene.Contact.Side}, releasing {scene.Contact.IsReleasing}, Dummy's point off line {scene.Dummy.JudgedTipOffset.Length() * 100.0f:0} cm.");
            Assert.That(scene.Dummy.JudgedTipOffset.Y, Is.LessThan(-0.02f), "The Dummy's blade was pushed down.");
        }

        [Test]
        public void ABeatAcrossTheEngagedBladesAt3MetersPerSecondDoesNotTunnel([Values(0.0f, 0.1f, 0.2f, 0.45f)] float slope, [Values(0.0f, 0.005f, -0.005f)] float height)
        {
            // En garde, blades engaged near the points: the Fencer's blade along the Strip, turned in by slope (0:
            // parallel to the Dummy's), beats across from the right at 3 m/s, in the Dummy's blade's plane (both blades
            // straight here, so that plane is exact).
            var scene = new BladePairScene(hasDroop: false);
            scene.Frame(Across(DummyHeight + 0.5f), DummyForte);
            var near = scene.Dummy.Drawn.GetCenterlinePoint(0.85f / 0.9f);
            var far = scene.Dummy.Drawn.GetCenterlinePoint(0.35f / 0.9f);
            var rise = (far.Y - near.Y) / (far.Z - near.Z);
            var direction = Vector3.Normalize(new Vector3(-slope, rise, 1.0f));
            var tipHeight = near.Y + ((1.2f - near.Z) * rise) + height;
            var met = false;
            for (var frame = 0; frame < 36; frame++)
            {
                var x = Math.Max(-0.25f, 0.10f - (frame * 3.0f * FrameSeconds));
                var tip = new Vector3(x, tipHeight, 1.2f);
                scene.Frame(new BladeSegment(tip - (direction * BladePose.BladeLengthMeters), tip), DummyForte);
                met |= scene.Contact.IsInContact;
                var side = scene.Contact.Side.Value;
                Assert.That(scene.FindSeparationAlong(side), Is.GreaterThan(Contact - Tolerance), $"Frame {frame}: through.");
            }

            Assert.That(met, Is.True, "Setup: the beat met the Dummy's blade.");
        }

        [Test]
        public void TheDummysBladeMovingAt3MetersPerSecondIntoAStillBladeIsTurnedAside()
        {
            var scene = new BladePairScene();
            var hand = Across(DummyHeight + 0.15f);
            var lowest = 0.0f;
            for (var frame = 0; frame < 36; frame++)
            {
                var rise = Math.Min(0.35f, frame * 3.0f * FrameSeconds);
                var forte = new BladeSegment(DummyForte.GuardPosition + new Vector3(0.0f, rise, 0.0f), DummyForte.TipPosition + new Vector3(0.0f, rise, 0.0f));
                scene.Frame(hand, forte);
                Assert.That(scene.FindSeparationAlong(scene.Contact.Side.Value), Is.GreaterThan(Contact - Tolerance), $"Frame {frame}: through.");
                lowest = Math.Min(lowest, scene.Dummy.JudgedTipOffset.Y);
            }

            Assert.That(lowest, Is.LessThan(-0.05f), "The Dummy's point was turned down out of the way.");
        }

        [Test]
        public void ASustainedPressKeepsContactSettledAndBendsTheDummysBladeToo()
        {
            var scene = new BladePairScene();
            PressDown(scene, 0.04f);
            var drops = new List<float>();
            for (var frame = 0; frame < 144; frame++)
            {
                scene.Frame(Across(DummyHeight - 0.04f), DummyForte);
                Assert.That(scene.FindSeparationAlong(Vector3.UnitY), Is.GreaterThan(Contact - Tolerance), $"Frame {frame}: through.");
                drops.Add(scene.Dummy.JudgedTipOffset.Y);
            }

            Assert.That(scene.Contact.IsInContact, Is.True, "The blades still press after 2 s.");
            Assert.That(scene.FindSeparationAlong(Vector3.UnitY), Is.LessThan(Contact + 0.005f), "And touch.");
            Assert.That(Math.Abs(drops[drops.Count - 1] - drops[drops.Count - 10]), Is.LessThan(0.002f), "Settled, not oscillating.");
            Assert.That(scene.Contact.GiveB.BowMeters, Is.GreaterThan(0.001f), "The Dummy's blade bows from the contact.");
            Assert.That(scene.Contact.GiveA.BowMeters, Is.GreaterThan(0.001f), "So does the Fencer's.");
            Assert.That(scene.Dummy.Deflection.TipOffset.Y, Is.LessThan(-0.005f), "The Dummy's hand gives way to the press.");
        }

        [Test]
        public void PullingBackSlidesOffTheEndWithoutCrossing()
        {
            var scene = new BladePairScene();
            PressDown(scene, 0.04f);
            for (var frame = 0; frame < 30; frame++)
            {
                scene.Frame(Across(DummyHeight - 0.04f), DummyForte);
            }

            var pulled = 0.0f;
            while (pulled < 0.70f)
            {
                pulled += 1.0f * FrameSeconds;
                scene.Frame(Across(DummyHeight - 0.04f, -pulled), DummyForte);
                Assert.That(scene.FindSeparationAlong(Vector3.UnitY), Is.GreaterThan(Contact - Tolerance), $"Pulled {pulled:0.00} m: through.");
            }

            for (var frame = 0; frame < 72; frame++)
            {
                scene.Frame(Across(DummyHeight - 0.04f, -pulled), DummyForte);
            }

            var tip = scene.Fencer.GetSample(DrawnBlade.SampleCount);
            Assert.That(tip.X, Is.LessThan(-0.05f), "Setup: the Fencer's point is short of the Dummy's blade.");
            Assert.That(scene.Contact.IsInContact, Is.False);
            Assert.That(scene.Dummy.FencerGive.TurnDegrees, Is.EqualTo(0.0f), "Off the end, no give: the blade as the hand holds it.");
            Assert.That(Vector3.Distance(tip, scene.Dummy.FencerGive.Chord.TipPosition), Is.LessThan(0.01f), "The drawn point is back with the hand's, below.");
            Assert.That(scene.Contact.Side.Value.Y, Is.LessThan(0.5f), "Free of it, the side follows the hand again.");
        }

        [Test]
        public void TheDummysBladeReturnsToItsLineAfterRelease()
        {
            var scene = new BladePairScene();
            PressDown(scene, 0.06f);
            for (var frame = 0; frame < 36; frame++)
            {
                scene.Frame(Across(DummyHeight - 0.06f), DummyForte);
            }

            Assert.That(scene.Dummy.JudgedTipOffset.Y, Is.LessThan(-0.01f), "Setup: pushed aside.");
            for (var frame = 0; frame < 144; frame++)
            {
                scene.Frame(Across(Math.Min(DummyHeight + 0.2f, DummyHeight - 0.06f + (frame * 0.01f))), DummyForte);
            }

            Assert.That(scene.Contact.IsInContact, Is.False);
            Assert.That(scene.Dummy.JudgedTipOffset.Length(), Is.LessThan(0.005f), "Back in line within 2 s.");
        }

        [Test]
        public void TheWedgeByTheBellNeverHoldsTheHiltOrPassesThrough(
            [Values(0.03f, 0.10f)] float depth,
            [Values(0, 1)] int pull)
        {
            // PER-121's recipe: the Fencer's blade comes down at 3 m/s across the Dummy's 2 cm in front of its bell to
            // depth past its line and holds a second, then the hand pulls 50 cm away at 2 m/s, along its blade (off the
            // Dummy's guard) or toward the Dummy (past the bell). The scene checks every frame that the drawn hilt is in
            // the hand; here, that the blades never pass through each other and let go at the end.
            var scene = new BladePairScene(withBody: true);
            var crossing = DummyForte.GuardPosition + (DummyForte.Direction * 0.02f);
            var away = pull == 0 ? Vector3.UnitX : Vector3.UnitZ;
            var above = 0.10f;
            for (var frame = 0; frame < 72; frame++)
            {
                above = Math.Max(-depth, above - (3.0f * FrameSeconds));
                scene.Frame(AcrossAt(crossing, above, Vector3.Zero), DummyForte);
                AssertNotThrough(scene, $"pressing, frame {frame}");
            }

            for (var frame = 1; frame <= 72; frame++)
            {
                var pulled = Math.Min(0.5f, frame * 2.0f * FrameSeconds);
                scene.Frame(AcrossAt(crossing, -depth, away * pulled), DummyForte);
                AssertNotThrough(scene, $"pulled {pulled:0.00} m");
            }

            Assert.That(scene.Contact.IsInContact, Is.False, "Pulled away, the blades are apart.");
            Assert.That(scene.Fencer.IsDecoupled, Is.False, "And the drawn blade follows the hand.");
        }

        [Test]
        public void AParallelBindSlidesToTheGuardWithoutPassingThrough([Values(-1.0f, 1.0f)] float line)
        {
            // A bind in 4 (the Fencer's blade on the Dummy's left, line -1) or 6 (right, +1): the blades nearly parallel,
            // the Fencer's pressing 1 cm into the Dummy's from the side and a little above, sliding 40 cm toward the
            // Dummy's guard in a second. Contact persists and the drawn blades never cross.
            var scene = new BladePairScene(hasDroop: false);
            var side = Vector3.Normalize(new Vector3(line, 0.3f, 0.0f));
            var press = 0.01f;
            Vector3? startSide = null;
            var frames = 0;
            var inContact = 0;
            for (var slid = 0.0f; slid <= 0.40f; slid += 0.40f / 72.0f)
            {
                // Apart first, then pressed in; the Fencer's blade turned 3 degrees in toward the Dummy's guard.
                var offset = frames < 10 ? Contact + 0.02f : Contact - press;
                var guard = new Vector3(0.0f, DummyHeight, 0.25f + slid) + (side * offset);
                var direction = Vector3.Normalize(new Vector3(-line * 0.02f, 0.0f, 1.0f));
                scene.Frame(new BladeSegment(guard - (direction * 0.0f), guard + (direction * BladePose.BladeLengthMeters)), DummyForte);
                frames++;
                startSide ??= scene.Contact.Side;
                if (frames <= 10)
                {
                    continue;
                }

                inContact += scene.Contact.IsInContact ? 1 : 0;
                Assert.That(Vector3.Dot(scene.Contact.Side.Value, startSide.Value), Is.GreaterThan(0.5f), $"Slid {slid:0.00} m: the side held.");
                Assert.That(BladePairScene.FindDistance(scene.FencerSamples(), scene.DummySamples()), Is.GreaterThan(Contact - Tolerance), $"Slid {slid:0.00} m: into the Dummy's blade.");
                Assert.That(scene.FindSeparationAlong(startSide.Value), Is.GreaterThan(Contact - Tolerance), $"Slid {slid:0.00} m: through.");
            }

            Assert.That(inContact, Is.EqualTo(frames - 10), "The bind kept contact all the way.");
        }

        [Test]
        public void ALightHandGivesMoreThanAStrongOne()
        {
            float Give(OpponentHand hand)
            {
                var scene = new BladePairScene(hand: hand);
                PressDown(scene, 0.04f);
                for (var frame = 0; frame < 72; frame++)
                {
                    scene.Frame(Across(DummyHeight - 0.04f), DummyForte);
                }

                return -scene.Dummy.JudgedTipOffset.Y;
            }

            var light = Give(OpponentHand.Light);
            var medium = Give(OpponentHand.Medium);
            var strong = Give(OpponentHand.Strong);
            TestContext.WriteLine($"The Dummy's point off line under the same press: Light {light * 100.0f:0.0} cm, Medium {medium * 100.0f:0.0} cm, Strong {strong * 100.0f:0.0} cm.");
            Assert.That(light, Is.GreaterThan(medium));
            Assert.That(medium, Is.GreaterThan(strong));
        }

        [Test]
        public void AFarAwayDummyLeavesAWallPressUnchanged()
        {
            var wall = BladeObstacle.FromPlane(new Vector3(0.0f, 0.0f, 1.0f), -Vector3.UnitZ);
            var plain = new DrawnBlade();
            var withDummy = new BladePairScene();
            var farForte = new BladeSegment(new Vector3(3.0f, 1.0f, 3.0f), new Vector3(3.0f, 1.0f, 2.1f));
            var touching = 1.0f - BladeContactBend.PointRadiusMeters - BladePose.BladeLengthMeters;
            var obstacles = new List<BladeObstacle> { wall };
            for (var push = -0.02f; push <= 0.08f + 1e-4f; push += 0.01f)
            {
                var guard = new Vector3(0.02f * push, 1.0f, touching + push);
                var pose = new BladeSegment(guard, guard + (Vector3.UnitZ * BladePose.BladeLengthMeters));
                plain.Step(pose, obstacles, Vector3.UnitY, FrameSeconds);
                withDummy.Dummy.Step(farForte, farForte.GuardPosition + Vector3.UnitY, Vector3.UnitY, -Vector3.UnitY, withDummy.Settings, OpponentHand.Medium, pose, default, FrameSeconds);
                Assert.That(withDummy.Contact.IsInContact, Is.False);
                Assert.That(withDummy.Dummy.FencerGive.Chord.TipPosition, Is.EqualTo(pose.TipPosition), "Apart, the Fencer's blade gets no give.");
            }

            Assert.That(plain.Contact.IsPinned, Is.True);
        }

        [Test]
        public void PressingBladeOnBladeStaysWithinTheFrameBudget()
        {
            // A press that moves every frame (deeper and shallower, along the blade), with the Dummy's body behind its
            // blade: the Dummy's hand, the contact solver and both drawn blades, all in one frame's budget.
            var scene = new BladePairScene(withBody: true);
            PressDown(scene, 0.04f);
            var poses = new List<BladeSegment>();
            for (var frame = 0; frame < 400; frame++)
            {
                var phase = frame * 0.05f;
                poses.Add(Across(DummyHeight - 0.03f - (0.02f * (float)Math.Sin(phase)), 0.05f * (float)Math.Sin(phase * 0.7f)));
            }

            foreach (var pose in poses.GetRange(0, 50))
            {
                scene.Frame(pose, DummyForte);
            }

            var milliseconds = double.MaxValue;
            for (var run = 0; run < 3; run++)
            {
                scene.Watch.Reset();
                foreach (var pose in poses)
                {
                    scene.Frame(pose, DummyForte);
                }

                milliseconds = Math.Min(milliseconds, scene.Watch.Elapsed.TotalMilliseconds / poses.Count);
            }

            TestContext.WriteLine($"Both blades and their contact while pressed: {milliseconds:0.000} ms per frame (editor).");
            Assert.That(scene.Contact.IsInContact, Is.True, "Setup: pressed on the Dummy's blade.");
            Assert.That(milliseconds, Is.LessThan(0.40), "The old budgets together (drawn blade 0.25 ms, Dummy's blade 0.15 ms): well under the 13.9 ms of a 72 Hz frame.");
        }

        private static void AssertNotThrough(BladePairScene scene, string when)
        {
            if (scene.Fencer.IsDecoupled)
            {
                // The Dummy's bell holds the Fencer's drawn blade off the hand (PER-120, until the leash): the contact
                // solves the hand's blade, so the held drawn blade may touch the Dummy's forte by a few millimeters.
                Assert.That(BladePairScene.FindDistance(scene.FencerSamples(), scene.DummySamples()), Is.GreaterThan(Contact - 0.005f), $"{when}: into the Dummy's blade.");
                return;
            }

            Assert.That(scene.FindSeparationAlong(scene.Contact.Side.Value), Is.GreaterThan(Contact - Tolerance), $"{when}: through.");
        }

        /// <summary>Brings the Fencer's blade from 10 cm above down to <paramref name="depth"/> below the Dummy's line at 0.5 m/s.</summary>
        private static void PressDown(BladePairScene scene, float depth)
        {
            for (var height = DummyHeight + 0.10f; height > DummyHeight - depth; height -= 0.5f * FrameSeconds)
            {
                scene.Frame(Across(height), DummyForte);
            }

            scene.Frame(Across(DummyHeight - depth), DummyForte);
        }

        /// <summary>The Fencer's blade along +x at <paramref name="height"/>, crossing the Dummy's at z 1.0, 30 cm from its guard (moved along by <paramref name="along"/>).</summary>
        private static BladeSegment Across(float height, float along = 0.0f)
        {
            var guard = new Vector3(-0.3f + along, height, CrossingZ);
            return new BladeSegment(guard, guard + (Vector3.UnitX * BladePose.BladeLengthMeters));
        }

        /// <summary>The Fencer's blade along +x, 30 cm of it left of <paramref name="crossing"/>, <paramref name="above"/> over it, moved by <paramref name="moved"/>.</summary>
        private static BladeSegment AcrossAt(Vector3 crossing, float above, Vector3 moved)
        {
            var middle = crossing + new Vector3(0.0f, above, 0.0f) + moved;
            return new BladeSegment(middle - (0.3f * Vector3.UnitX), middle + ((BladePose.BladeLengthMeters - 0.3f) * Vector3.UnitX));
        }
    }
}
