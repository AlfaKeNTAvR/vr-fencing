// The Fencer's drawn blade and the Armed Dummy's blade stepped together each frame as the app steps them (PER-122):
// the Dummy's hand and the two blades' contact first (ArmedDummyBlade), then the Fencer's drawn blade on what the
// contact gives it, against the Dummy's body, its bell and the floor. Measures what the blade tests check: how far
// apart the drawn blades are along a side, and that the drawn hilt is in the hand. Meters, seconds, Strip frame.
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
    internal sealed class BladePairScene
    {
        public const float FrameSeconds = 1.0f / 72.0f;

        /// <summary>The drawn blades may come this much closer than touching (pieces of a curve, one look past the split).</summary>
        public const float ToleranceMeters = 0.0025f;

        private readonly List<BladeObstacle> _body = new List<BladeObstacle>();
        private readonly List<BladeObstacle> _obstacles = new List<BladeObstacle>();
        private readonly Vector3 _down;

        /// <param name="withBody">Adds a Dummy's body of 14 capsules behind its blade, out of the blades' way.</param>
        /// <param name="hasDroop">False: both blades straight (no set, no sag), for clean geometry.</param>
        public BladePairScene(bool withBody = false, bool hasDroop = true, OpponentHand hand = OpponentHand.Medium)
        {
            Hand = hand;
            _down = hasDroop ? -Vector3.UnitY : Vector3.Zero;
            Settings = new BladeSettings();
            if (!hasDroop)
            {
                Settings.SetSagittaMeters = 0.0f;
            }

            for (var part = 0; withBody && part < 14; part++)
            {
                var x = -0.3f + (0.05f * part);
                _body.Add(new Capsule(new Vector3(x, 0.5f, 2.2f), new Vector3(x, 1.4f, 2.2f), 0.06f));
            }
        }

        public BladeSettings Settings { get; }

        public OpponentHand Hand { get; set; }

        public ArmedDummyBlade Dummy { get; } = new ArmedDummyBlade();

        public DrawnBlade Fencer { get; } = new DrawnBlade();

        public BladeSegment LastHand { get; private set; }

        public Stopwatch Watch { get; } = new Stopwatch();

        /// <summary>The most the drawn hilt lagged the hand (held by the bell, a body or the floor), over all frames.</summary>
        public float MostLagMeters { get; private set; }

        public BladeContactSolver Contact => Dummy.Contact;

        /// <summary>One frame: <paramref name="hand"/> the Fencer's blade (straight), <paramref name="dummyForte"/> the Dummy's line.</summary>
        public void Frame(BladeSegment hand, BladeSegment dummyForte, Vector3? shoulder = null)
        {
            LastHand = hand;
            var fencerDroop = BladeDroop.Create(hand.Direction, Vector3.UnitY, _down, Settings);
            var chord = fencerDroop.GetChord(hand);
            Watch.Start();
            Dummy.Step(dummyForte, shoulder ?? dummyForte.GuardPosition + new Vector3(0.0f, 0.3f, 0.6f), Vector3.UnitY, _down, Settings, Hand, chord, fencerDroop, FrameSeconds);
            _obstacles.Clear();
            _obstacles.AddRange(_body);
            _obstacles.Add(BladeObstacle.Floor);
            Dummy.AddObstacles(_obstacles);
            if (Contact.IsNear)
            {
                Fencer.Flex.Reset();
            }

            Fencer.Step(Dummy.FencerGive.Chord, _obstacles, Vector3.UnitY, FrameSeconds, Dummy.FencerGive.Droop);
            Watch.Stop();
            if (!Fencer.IsDecoupled)
            {
                Assert.That(Vector3.Distance(Fencer.GuardPosition, chord.GuardPosition), Is.LessThanOrEqualTo(DrawnShape.LagMeters + 1e-4f), "Every frame the drawn hilt is in the hand.");
            }
            else
            {
                // Only the Dummy's bell, its body or the floor can hold it, and never past the leash.
                MostLagMeters = Math.Max(MostLagMeters, Fencer.LagMeters);
                Assert.That(Fencer.LagMeters, Is.LessThanOrEqualTo(DrawnBlade.LeashMeters + 0.03f), "Never past the leash.");
            }
        }

        public Vector3[] FencerSamples()
        {
            return Samples(Fencer);
        }

        public Vector3[] DummySamples()
        {
            return Samples(Dummy.Drawn);
        }

        /// <summary>
        /// The closest the drawn blades come along <paramref name="side"/> (from the Dummy's to the Fencer's; negative past
        /// it) where they lie over each other seen along it; float.MaxValue where they never do.
        /// </summary>
        public float FindSeparationAlong(Vector3 side)
        {
            return FindSeparation(FencerSamples(), DummySamples(), side);
        }

        public static float FindSeparation(IReadOnlyList<Vector3> a, IReadOnlyList<Vector3> b, Vector3 side)
        {
            var worst = float.MaxValue;
            for (var pieceA = 0; pieceA + 1 < a.Count; pieceA++)
            {
                for (var pieceB = 0; pieceB + 1 < b.Count; pieceB++)
                {
                    var flatA0 = Flatten(a[pieceA], side);
                    var flatA1 = Flatten(a[pieceA + 1], side);
                    var flatB0 = Flatten(b[pieceB], side);
                    var flatB1 = Flatten(b[pieceB + 1], side);
                    FindClosest(flatA0, flatA1, flatB0, flatB1, out var alongA, out var alongB);
                    var flatDistance = Vector3.Distance(Vector3.Lerp(flatA0, flatA1, alongA), Vector3.Lerp(flatB0, flatB1, alongB));
                    if (flatDistance > BladeContactSolver.ContactMeters * 0.5f)
                    {
                        continue;
                    }

                    var onA = Vector3.Lerp(a[pieceA], a[pieceA + 1], alongA);
                    var onB = Vector3.Lerp(b[pieceB], b[pieceB + 1], alongB);
                    worst = Math.Min(worst, Vector3.Dot(onA - onB, side));
                }
            }

            return worst;
        }

        /// <summary>The smallest distance between the two drawn blades, centerline to centerline.</summary>
        public static float FindDistance(IReadOnlyList<Vector3> a, IReadOnlyList<Vector3> b)
        {
            var closest = float.MaxValue;
            for (var pieceA = 0; pieceA + 1 < a.Count; pieceA++)
            {
                for (var pieceB = 0; pieceB + 1 < b.Count; pieceB++)
                {
                    FindClosest(a[pieceA], a[pieceA + 1], b[pieceB], b[pieceB + 1], out var alongA, out var alongB);
                    closest = Math.Min(closest, Vector3.Distance(Vector3.Lerp(a[pieceA], a[pieceA + 1], alongA), Vector3.Lerp(b[pieceB], b[pieceB + 1], alongB)));
                }
            }

            return closest;
        }

        public static Vector3[] Samples(DrawnBlade drawn)
        {
            var samples = new Vector3[DrawnBlade.SampleCount + 1];
            for (var index = 0; index <= DrawnBlade.SampleCount; index++)
            {
                samples[index] = drawn.GetSample(index);
            }

            return samples;
        }

        private static Vector3 Flatten(Vector3 point, Vector3 side)
        {
            return point - (Vector3.Dot(point, side) * side);
        }

        /// <summary>Closest points of two segments (sampled finely: test code, not fast).</summary>
        private static void FindClosest(Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1, out float alongA, out float alongB)
        {
            var directionA = a1 - a0;
            var directionB = b1 - b0;
            var lengthA = directionA.LengthSquared();
            var lengthB = directionB.LengthSquared();
            alongA = 0.0f;
            alongB = 0.0f;
            var best = float.MaxValue;
            const int Steps = 8;
            for (var step = 0; step <= Steps; step++)
            {
                var s = (float)step / Steps;
                var point = a0 + (directionA * s);
                var t = lengthB > 1e-12f ? Math.Clamp(Vector3.Dot(point - b0, directionB) / lengthB, 0.0f, 1.0f) : 0.0f;
                var back = b0 + (directionB * t);
                var refined = lengthA > 1e-12f ? Math.Clamp(Vector3.Dot(back - a0, directionA) / lengthA, 0.0f, 1.0f) : 0.0f;
                var t2 = lengthB > 1e-12f ? Math.Clamp(Vector3.Dot(a0 + (directionA * refined) - b0, directionB) / lengthB, 0.0f, 1.0f) : 0.0f;
                var distance = Vector3.DistanceSquared(a0 + (directionA * refined), b0 + (directionB * t2));
                if (distance < best)
                {
                    best = distance;
                    alongA = refined;
                    alongB = t2;
                }
            }
        }
    }
}
