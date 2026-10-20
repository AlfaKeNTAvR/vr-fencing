// The two blades are solid against each other (PER-104): the Fencer's drawn blade never passes through the Armed
// Dummy's, at real attack speeds too; the Dummy's blade gives way to a press and springs back; pulling back slides off
// the end. The Dummy's blade lies along the Strip at y = 1.1, its guard at z = 1.6 and its point toward the Fencer;
// the Fencer's blade lies across it along +x at z = 1.0 (30 cm from the Fencer's guard). Meters, seconds, Strip frame.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Blade;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Tests.Blade
{
    public class BladeOnBladeTests
    {
        private const float FrameSeconds = 1.0f / 72.0f;
        private const float DummyHeight = 1.1f;
        private const float CrossingZ = 1.0f;

        // Drawn blades may come this much closer than touching (the Dummy's curve is taken as straight pieces).
        private const float ToleranceMeters = 0.0025f;

        private static readonly BladeSegment DummyForte = new BladeSegment(new Vector3(0.0f, DummyHeight, 1.6f), new Vector3(0.0f, DummyHeight, 0.7f));

        // The Dummy's sword shoulder from its guard: its arm, 0.6 m back along the Strip and 0.3 m up.
        private static readonly Vector3 DummyArm = new Vector3(0.0f, 0.3f, 0.6f);

        [Test]
        public void AFastCrossingAt3MetersPerSecondDoesNotTunnel()
        {
            var pair = new Pair();
            var worst = float.MaxValue;
            var height = DummyHeight + 0.10f;
            for (var frame = 0; frame < 72; frame++)
            {
                height = Math.Max(DummyHeight - 0.20f, height - (3.0f * FrameSeconds));
                pair.Frame(Across(height));
                worst = Math.Min(worst, pair.FindSeparationAlong(Vector3.UnitY));
            }

            Assert.That(worst, Is.GreaterThan(BladeBind.ContactMeters - ToleranceMeters), "The Fencer's drawn blade never got to or past the Dummy's.");
            Assert.That(pair.Dummy.Bind.Side.Value.Y, Is.GreaterThan(0.9f), "The Fencer's blade is still above.");
            Assert.That(pair.Dummy.IsPressed, Is.True, "20 cm past the Dummy's line it presses.");
            Assert.That(pair.Fencer.IsOnSide || pair.Fencer.IsDecoupled, Is.True, "The drawn blade bends round the Dummy's, or lags the hand (the ghost hilt).");
            Assert.That(pair.FindDummyContactDrop(), Is.GreaterThan(0.02f), "The Dummy's blade was pushed down.");
        }

        [Test]
        public void ABeatAcrossTheEngagedBladesAt3MetersPerSecondDoesNotTunnel([Values(0.0f, 0.1f, 0.2f, 0.45f)] float slope, [Values(0.0f, 0.005f, -0.005f)] float height)
        {
            // En garde, blades engaged near the points: the Fencer's blade points along the Strip, turned in by
            // slope (0: parallel to the Dummy's), its point past the Dummy's, and beats across from the right at 3 m/s.
            // It lies in the Dummy's drooping blade's plane (or a little above or below), so it cannot pass over or
            // under: it has to meet it.
            var pair = new Pair();
            pair.Frame(Across(DummyHeight + 0.5f));
            var near = pair.Dummy.Drawn.GetCenterlinePoint(0.85f / 0.9f);
            var far = pair.Dummy.Drawn.GetCenterlinePoint(0.35f / 0.9f);
            var rise = (far.Y - near.Y) / (far.Z - near.Z);
            var direction = Vector3.Normalize(new Vector3(-slope, rise, 1.0f));
            var tipHeight = near.Y + ((1.2f - near.Z) * rise) + height;
            var worst = float.MaxValue;
            Vector3? startSide = null;
            for (var frame = 0; frame < 36; frame++)
            {
                // The point (the blade's leftmost part) starts 10 cm right of the Dummy's blade, 1.2 m down the Strip.
                var x = Math.Max(-0.25f, 0.10f - (frame * 3.0f * FrameSeconds));
                var tip = new Vector3(x, tipHeight, 1.2f);
                pair.Frame(new BladeSegment(tip - (direction * BladePose.BladeLengthMeters), tip));
                startSide ??= pair.Dummy.Bind.Side;
                worst = Math.Min(worst, pair.FindSeparationAlong(startSide.Value, isAlongsideOnly: true));
            }

            Assert.That(startSide.Value.X, Is.GreaterThan(0.7f), "Setup: the Fencer's blade starts on the Dummy's right.");
            // The side turns a little as the blades slide on each other, so the start's side is a few millimeters off the
            // kept one: never more than that past the Dummy's axis.
            Assert.That(worst, Is.GreaterThan(-0.005f), "The Fencer's drawn blade never got past the Dummy's.");
            var isMet = pair.Fencer.IsOnSide || pair.Fencer.IsDecoupled || pair.Fencer.Contact.IsPinned || pair.Dummy.Deflection.TipOffset.X < -0.02f;
            Assert.That(isMet, Is.True, "Setup: the beat met the Dummy's blade (bent or held the Fencer's, or knocked the Dummy's aside).");
        }

        [Test]
        public void TheDummysBladeMovingAt3MetersPerSecondIntoAStillBladeIsDeflected()
        {
            var pair = new Pair();
            var worst = float.MaxValue;
            var hand = Across(DummyHeight + 0.15f);

            // The Dummy's blade rises at 3 m/s from 10 cm below the Fencer's line... through where the Fencer's blade is.
            for (var frame = 0; frame < 36; frame++)
            {
                var rise = Math.Min(0.35f, frame * 3.0f * FrameSeconds);
                var forte = new BladeSegment(DummyForte.GuardPosition + new Vector3(0.0f, rise, 0.0f), DummyForte.TipPosition + new Vector3(0.0f, rise, 0.0f));
                pair.Frame(hand, forte);
                worst = Math.Min(worst, pair.FindSeparationAlong(Vector3.UnitY));
            }

            Assert.That(worst, Is.GreaterThan(BladeBind.ContactMeters - ToleranceMeters), "The Dummy's blade never got to or past the Fencer's.");
            Assert.That(pair.Dummy.Deflection.TipOffset.Y, Is.LessThan(-0.05f), "The Dummy's point was pushed down out of the way.");
        }

        [Test]
        public void ASustainedPressKeepsContact()
        {
            var pair = new Pair();
            PressDown(pair, 0.04f);
            var drops = new List<float>();
            for (var frame = 0; frame < 144; frame++)
            {
                pair.Frame(Across(DummyHeight - 0.04f));
                Assert.That(pair.FindSeparationAlong(Vector3.UnitY), Is.GreaterThan(BladeBind.ContactMeters - ToleranceMeters), $"Frame {frame}: through.");
                drops.Add(pair.FindDummyContactDrop());
            }

            Assert.That(pair.FindSeparationAlong(Vector3.UnitY), Is.LessThan(BladeBind.ContactMeters + 0.005f), "The blades still touch after 2 s of pressing.");
            Assert.That(pair.Dummy.IsPressed, Is.True);
            Assert.That(drops[drops.Count - 1], Is.InRange(0.003f, 0.04f), "The Dummy's blade gives way part of the press, the Fencer's bends the rest.");
            Assert.That(Math.Abs(drops[drops.Count - 1] - drops[drops.Count - 10]), Is.LessThan(0.002f), "Settled, not oscillating.");
            Assert.That(pair.Fencer.IsOnSide || pair.Fencer.Contact.IsPinned, Is.True, "The drawn blade lies against the Dummy's.");
        }

        [Test]
        public void PullingBackSlidesOffTheEndWithoutCrossing()
        {
            var pair = new Pair();
            PressDown(pair, 0.04f);
            for (var frame = 0; frame < 30; frame++)
            {
                pair.Frame(Across(DummyHeight - 0.04f));
            }

            // Back along the Fencer's own blade at 1 m/s until its point is 10 cm short of the Dummy's blade.
            var pulled = 0.0f;
            while (pulled < 0.70f)
            {
                pulled += 1.0f * FrameSeconds;
                pair.Frame(Across(DummyHeight - 0.04f, -pulled));
                Assert.That(pair.FindSeparationAlong(Vector3.UnitY), Is.GreaterThan(BladeBind.ContactMeters - ToleranceMeters), $"Pulled {pulled:0.00} m: through.");
            }

            for (var frame = 0; frame < 72; frame++)
            {
                pair.Frame(Across(DummyHeight - 0.04f, -pulled));
            }

            var tip = pair.Fencer.GetSample(DrawnBlade.SampleCount);
            Assert.That(tip.X, Is.LessThan(-0.05f), "Setup: the Fencer's point is short of the Dummy's blade.");
            Assert.That(tip.Y, Is.EqualTo(DummyHeight - 0.04f).Within(0.01f), "Off the end, the drawn blade is back with the hand, below.");
            Assert.That(pair.Fencer.IsDecoupled || pair.Fencer.IsOnSide, Is.False);
            Assert.That(pair.Dummy.Bind.Side.Value.Y, Is.LessThan(0.5f), "Free of it, the side follows the hand again: no longer held above.");
        }

        [Test]
        public void TheDummysBladeReturnsToItsLineAfterRelease()
        {
            var pair = new Pair();
            PressDown(pair, 0.06f);
            for (var frame = 0; frame < 36; frame++)
            {
                pair.Frame(Across(DummyHeight - 0.06f));
            }

            Assert.That(pair.FindDummyContactDrop(), Is.GreaterThan(0.01f), "Setup: pushed aside.");

            // Lifted straight back up off it.
            for (var frame = 0; frame < 108; frame++)
            {
                pair.Frame(Across(Math.Min(DummyHeight + 0.2f, DummyHeight - 0.06f + (frame * 0.01f))));
            }

            Assert.That(pair.Dummy.IsPressed, Is.False);
            Assert.That(pair.Dummy.Deflection.TipOffset.Length(), Is.LessThan(0.005f), "Back in line within 1.5 s.");
        }

        [Test]
        public void AFarAwayBladeLeavesAWallPressUnchanged()
        {
            var wall = BladeObstacle.FromPlane(new Vector3(0.0f, 0.0f, 1.0f), -Vector3.UnitZ);
            var withBlade = new List<BladeObstacle> { wall };
            var bind = new BladeBind();
            bind.AddObstacles(new[] { new Vector3(2.0f, 2.0f, 2.0f), new Vector3(2.0f, 2.0f, 1.8f), new Vector3(2.0f, 2.0f, 1.6f), new Vector3(2.0f, 2.0f, 1.4f), new Vector3(2.0f, 2.0f, 1.2f) }, withBlade);
            var plain = new DrawnBlade();
            var near = new DrawnBlade();
            var touching = 1.0f - BladeContactBend.PointRadiusMeters - BladePose.BladeLengthMeters;
            for (var push = -0.02f; push <= 0.08f + 1e-4f; push += 0.01f)
            {
                var guard = new Vector3(0.02f * push, 1.0f, touching + push);
                var pose = new BladeSegment(guard, guard + (Vector3.UnitZ * BladePose.BladeLengthMeters));
                plain.Step(pose, new[] { wall }, Vector3.UnitY, FrameSeconds);
                near.Step(pose, withBlade, Vector3.UnitY, FrameSeconds);
            }

            Assert.That(near.Contact.IsPinned, Is.True);
            for (var index = 0; index <= DrawnBlade.SampleCount; index++)
            {
                Assert.That(Vector3.Distance(near.GetSample(index), plain.GetSample(index)), Is.LessThan(1e-6f));
            }
        }

        [Test]
        public void AFastCrossingBetweenTwoFramesIsStillCaught()
        {
            // The obstacle test alone: a drawn blade wholly on the other side of a thin blade, 3 cm past it (a 3 m/s
            // hand moves 4 cm in a frame), is not clear, though no point of it is near the thin blade.
            var bind = new BladeBind();
            var dummyShape = new[] { DummyForte.GuardPosition, Lerp(0.25f), Lerp(0.5f), Lerp(0.75f), DummyForte.TipPosition };
            bind.UpdateSide(Across(DummyHeight + 0.05f), null, dummyShape);
            var obstacles = new List<BladeObstacle>();
            bind.AddObstacles(dummyShape, obstacles);
            var through = new Vector3[DrawnBlade.SampleCount + 1];
            var above = new Vector3[DrawnBlade.SampleCount + 1];
            for (var index = 0; index <= DrawnBlade.SampleCount; index++)
            {
                var fraction = (float)index / DrawnBlade.SampleCount;
                through[index] = Vector3.Lerp(Across(DummyHeight - 0.03f).GuardPosition, Across(DummyHeight - 0.03f).TipPosition, fraction);
                above[index] = Vector3.Lerp(Across(DummyHeight + 0.03f).GuardPosition, Across(DummyHeight + 0.03f).TipPosition, fraction);
            }

            Assert.That(DrawnShape.IsClear(above, null, obstacles, DrawnBlade.BladeRadiusMeters, 1, sweepsTip: true), Is.True);
            Assert.That(DrawnShape.IsClear(through, null, obstacles, DrawnBlade.BladeRadiusMeters, 1, sweepsTip: true), Is.False);
        }

        [Test]
        public void PressingBladeOnBladeStaysWithinTheFrameBudget()
        {
            // A press that moves every frame (deeper and shallower, along the blade), with the Dummy's body behind its
            // blade as in the app. The Fencer's drawn-blade step is the solver budget the other blade tests use; the
            // Dummy's blade step (its own drawn blade, the press and keeping it out of the Fencer's) is on top.
            var pair = new Pair(withBody: true);
            PressDown(pair, 0.04f);
            var poses = new List<BladeSegment>();
            for (var frame = 0; frame < 400; frame++)
            {
                var phase = frame * 0.05f;
                poses.Add(Across(DummyHeight - 0.03f - (0.02f * (float)Math.Sin(phase)), 0.05f * (float)Math.Sin(phase * 0.7f)));
            }

            foreach (var pose in poses.GetRange(0, 50))
            {
                pair.Frame(pose);
            }

            // The best of three runs: the least disturbed by whatever else the machine is doing.
            var fencerMilliseconds = double.MaxValue;
            var dummyMilliseconds = double.MaxValue;
            for (var run = 0; run < 3; run++)
            {
                pair.FencerWatch.Reset();
                pair.DummyWatch.Reset();
                foreach (var pose in poses)
                {
                    pair.Frame(pose);
                }

                fencerMilliseconds = Math.Min(fencerMilliseconds, pair.FencerWatch.Elapsed.TotalMilliseconds / poses.Count);
                dummyMilliseconds = Math.Min(dummyMilliseconds, pair.DummyWatch.Elapsed.TotalMilliseconds / poses.Count);
            }

            TestContext.WriteLine($"Drawn blade step while pressed on the Dummy's blade: {fencerMilliseconds:0.000} ms per frame; the Dummy's blade step: {dummyMilliseconds:0.000} ms (editor).");
            Assert.That(pair.Fencer.IsOnSide || pair.Fencer.Contact.IsPinned, Is.True, "Setup: pressed on the Dummy's blade.");
            Assert.That(fencerMilliseconds, Is.LessThan(0.25), "Well under the 13.9 ms of a 72 Hz frame, with room for the Quest's slower CPU.");
            Assert.That(dummyMilliseconds, Is.LessThan(0.15), "The Dummy's blade adds little.");
        }

        /// <summary>Brings the Fencer's blade from 10 cm above down to <paramref name="depth"/> below the Dummy's line at 0.5 m/s.</summary>
        private static void PressDown(Pair pair, float depth)
        {
            for (var height = DummyHeight + 0.10f; height > DummyHeight - depth; height -= 0.5f * FrameSeconds)
            {
                pair.Frame(Across(height));
            }

            pair.Frame(Across(DummyHeight - depth));
        }

        /// <summary>The Fencer's blade along +x at <paramref name="height"/>, crossing the Dummy's at z 1.0, 30 cm from its guard (moved along by <paramref name="along"/>).</summary>
        private static BladeSegment Across(float height, float along = 0.0f)
        {
            var guard = new Vector3(-0.3f + along, height, CrossingZ);
            return new BladeSegment(guard, guard + (Vector3.UnitX * BladePose.BladeLengthMeters));
        }

        private static Vector3 Lerp(float fraction)
        {
            return Vector3.Lerp(DummyForte.GuardPosition, DummyForte.TipPosition, fraction);
        }

        /// <summary>The two blades as the app steps them each frame.</summary>
        private sealed class Pair
        {
            private readonly BladeSettings _settings = new BladeSettings();
            private readonly List<BladeObstacle> _obstacles = new List<BladeObstacle>();
            private readonly List<BladeObstacle> _body = new List<BladeObstacle>();
            private BladeSegment _forte = DummyForte;

            /// <param name="withBody">Adds a Dummy's body of 14 capsules behind its blade, out of the blades' way.</param>
            public Pair(bool withBody = false)
            {
                for (var part = 0; withBody && part < 14; part++)
                {
                    var x = -0.3f + (0.05f * part);
                    _body.Add(new Capsule(new Vector3(x, 0.5f, 2.2f), new Vector3(x, 1.4f, 2.2f), 0.06f));
                }
            }

            public ArmedDummyBlade Dummy { get; } = new ArmedDummyBlade();

            public DrawnBlade Fencer { get; } = new DrawnBlade();

            public Stopwatch DummyWatch { get; } = new Stopwatch();

            public Stopwatch FencerWatch { get; } = new Stopwatch();

            public void Frame(BladeSegment hand, BladeSegment? dummyForte = null)
            {
                _forte = dummyForte ?? DummyForte;
                DummyWatch.Start();
                Dummy.Step(_forte, _forte.GuardPosition + DummyArm, Vector3.UnitY, -Vector3.UnitY, _settings, hand, Fencer, FrameSeconds);
                DummyWatch.Stop();
                _obstacles.Clear();
                _obstacles.AddRange(_body);
                _obstacles.Add(BladeObstacle.Floor);
                Dummy.AddObstacles(_obstacles);
                FencerWatch.Start();
                Fencer.Step(hand, _obstacles, Vector3.UnitY, FrameSeconds);
                FencerWatch.Stop();
                var samples = new Vector3[DrawnBlade.SampleCount + 1];
                for (var index = 0; index <= DrawnBlade.SampleCount; index++)
                {
                    samples[index] = Fencer.GetSample(index);
                }

                Assert.That(DrawnShape.IsClear(samples, null, _obstacles, DrawnBlade.BladeRadiusMeters - 0.001f, 1, true), Is.True, "Every frame, the Fencer's drawn blade is clear of the Dummy's and of the floor.");
                Dummy.UpdateSide(hand, Fencer);
            }

            /// <summary>
            /// The closest the drawn blades come, centerline to centerline: where they lie across each other, measured
            /// along <paramref name="side"/> (the Fencer's side; negative past it), elsewhere straight apart; and the
            /// Fencer's blade found behind the Dummy's (alongside it, past its axis) counts as past it. With
            /// <paramref name="isAlongsideOnly"/>, blades lying across each other are only measured straight apart, for
            /// near-parallel blades whose line-to-line side means little.
            /// </summary>
            public float FindSeparationAlong(Vector3 side, bool isAlongsideOnly = false)
            {
                var worst = float.MaxValue;
                for (var piece = 0; piece < BladeBind.PieceCount; piece++)
                {
                    var start = Dummy.Shape[piece];
                    var end = Dummy.Shape[piece + 1];
                    var dummyJoints = (piece > 0 ? 1 : 0) | (piece < BladeBind.PieceCount - 1 ? 2 : 0);
                    for (var index = 0; index < DrawnBlade.SampleCount; index++)
                    {
                        var joints = (index > 0 ? 1 : 0) | (index < DrawnBlade.SampleCount - 1 ? 2 : 0);
                        var isAcross = BladeCrossing.FindClosest(Fencer.GetSample(index), Fencer.GetSample(index + 1), joints, start, end, dummyJoints, out var onFencer, out var onDummy, out _, out _);
                        var apart = onFencer - onDummy;
                        worst = Math.Min(worst, isAcross && !isAlongsideOnly ? Vector3.Dot(apart, side) : apart.Length());
                        var overlap = BladeBind.FindOverlap(Fencer.GetSample(index), Fencer.GetSample(index + 1), joints, start, end, dummyJoints, side, out _, out _);
                        worst = Math.Min(worst, BladeBind.ContactMeters - overlap);
                    }
                }

                return worst;
            }

            /// <summary>How far the Dummy's drawn blade is below its line where the Fencer's crosses it (z = 1.0).</summary>
            public float FindDummyContactDrop()
            {
                var fraction = (_forte.GuardPosition.Z - CrossingZ) / (_forte.GuardPosition.Z - _forte.TipPosition.Z);
                var line = new ArmedDummyBlade();
                line.Step(_forte, _forte.GuardPosition + DummyArm, Vector3.UnitY, -Vector3.UnitY, _settings, new BladeSegment(new Vector3(5, 5, 5), new Vector3(5, 5, 6)), null, FrameSeconds);
                return line.Drawn.GetCenterlinePoint(fraction).Y - Dummy.Drawn.GetCenterlinePoint(fraction).Y;
            }
        }
    }
}
