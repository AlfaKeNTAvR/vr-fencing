// A deep press near the Armed Dummy's guard (PER-120): the Fencer's drawn blade never reaches the Dummy's blade axis
// and the Dummy's point is not kicked about, and the Dummy's bell guard is solid, so a press or a point at its guard
// meets the bell. The Dummy stands 3 m down the Strip, en garde, with its body solid as in the app. Meters, seconds,
// Strip frame.
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
    public class BladeGuardPressTests
    {
        private const float FrameSeconds = 1.0f / 72.0f;
        private const float DummyDistance = 3.0f;

        // Drawn blades may come this much closer than touching (the Dummy's curve is taken as straight pieces).
        private const float ToleranceMeters = 0.0025f;

        [Test]
        public void ADeepPressNearTheGuardNeitherOverlapsNorJitters(
            [Values(1.5f, 2.1f)] float height,
            [Values(Hand.Right, Hand.Left)] Hand hand,
            [Values(0.10f, 0.20f, 0.30f)] float depth)
        {
            // PER-119's reproduction: the Fencer's blade comes down at 3 m/s across the Dummy's, a tenth of the way from
            // its guard (about 9 cm), to depth past its line and holds there. It used to be taken as through the Dummy's
            // blade after about 0.3 s and kicked its point 7 to 12 cm a frame, until the blades overlapped.
            var scene = new Scene(height, hand);
            var mostPointMove = 0.0f;
            var mostHeldPointMove = 0.0f;
            var mostHeldDrawnMove = 0.0f;
            var worstSeparation = float.MaxValue;
            var above = 0.10f;
            for (var frame = 0; frame < 216; frame++)
            {
                above = Math.Max(-depth, above - (3.0f * FrameSeconds));
                var pointBefore = scene.Dummy.Deflection.TipOffset;
                var drawnBefore = scene.Fencer.HasShape ? scene.Fencer.GetSample(DrawnBlade.SampleCount) : Vector3.Zero;
                scene.Frame(() => scene.Across(0.1f, above));

                var separation = scene.FindSeparationAbove();
                worstSeparation = Math.Min(worstSeparation, separation);
                Assert.That(separation, Is.GreaterThan(BladeBind.ContactMeters - ToleranceMeters), $"Frame {frame}: the Fencer's drawn blade reached the Dummy's.");
                var pointMove = Vector3.Distance(scene.Dummy.Deflection.TipOffset, pointBefore);
                mostPointMove = Math.Max(mostPointMove, pointMove);
                if (frame >= 72)
                {
                    // Held still for a second: nothing should move but a slow settle.
                    mostHeldPointMove = Math.Max(mostHeldPointMove, pointMove);
                    mostHeldDrawnMove = Math.Max(mostHeldDrawnMove, Vector3.Distance(scene.Fencer.GetSample(DrawnBlade.SampleCount), drawnBefore));
                }
            }

            TestContext.WriteLine($"Closest {worstSeparation * 100.0f:0.00} cm; the Dummy's point moved at most {mostPointMove * 100.0f:0.0} cm a frame, held {mostHeldPointMove * 1000.0f:0.0} mm; the drawn point held {mostHeldDrawnMove * 1000.0f:0.0} mm.");
            Assert.That(scene.Dummy.IsPressed, Is.True, "Setup: the hand presses past the Dummy's line.");
            Assert.That(mostPointMove, Is.LessThan(0.02f), "The Dummy's point is never kicked: at most 2 cm a frame.");
            Assert.That(mostHeldPointMove, Is.LessThan(0.002f), "Held, the Dummy's point is still (no jitter).");
            Assert.That(mostHeldDrawnMove, Is.LessThan(0.002f), "Held, the Fencer's drawn point is still (no jitter).");
            Assert.That(scene.Dummy.Deflection.TipOffset.Length(), Is.LessThan(0.15f), "Pressed near its guard, the Dummy's point gives way only a little.");
        }

        [Test]
        public void APressAcrossTheGuardRestsOnTheBell([Values(1.5f, 2.1f)] float height, [Values(Hand.Right, Hand.Left)] Hand hand)
        {
            // The Fencer's blade comes down at 3 m/s across the Dummy's bell (2 cm behind its front face, in front of
            // the hand) to 20 cm below the Dummy's blade line: the bell stops it 6.75 cm above that line.
            var scene = new Scene(height, hand);
            var above = 0.15f;
            for (var frame = 0; frame < 108; frame++)
            {
                above = Math.Max(-0.20f, above - (3.0f * FrameSeconds));
                scene.Frame(() => scene.Across(-0.02f / BladePose.BladeLengthMeters, above));
                Assert.That(scene.FindBellClearance(), Is.GreaterThan(DrawnBlade.BladeRadiusMeters - 0.001f), $"Frame {frame}: the Fencer's drawn blade went into the bell.");
            }

            Assert.That(scene.FindBellClearance(), Is.LessThan(DrawnBlade.BladeRadiusMeters + 0.01f), "The drawn blade lies on the bell.");
            Assert.That(scene.Fencer.IsOnSide || scene.Fencer.IsDecoupled, Is.True, "It bends over the bell, or lags the hand (the ghost hilt).");
        }

        [Test]
        public void APointOnTheBellStopsOnIt([Values(1.5f, 2.1f)] float height, [Values(Hand.Right, Hand.Left)] Hand hand)
        {
            // The Fencer thrusts at the Dummy's guard along its blade, 4 cm above it: the point lands on the bell's face
            // and pins there instead of reaching the hand behind it.
            var scene = new Scene(height, hand);
            for (var frame = 0; frame < 72; frame++)
            {
                var reach = Math.Min(0.25f, frame * 1.0f * FrameSeconds);
                scene.Frame(() => scene.ThrustAtTheGuard(0.20f - reach, 0.04f));
                Assert.That(scene.FindBellClearance(), Is.GreaterThan(DrawnBlade.BladeRadiusMeters - 0.001f), $"Frame {frame}: the Fencer's drawn blade went into the bell.");
            }

            Assert.That(scene.Fencer.Contact.IsPinned, Is.True, "The point is pinned on the bell's face.");
            Assert.That(scene.FindBellClearance(), Is.LessThan(BladeContactBend.PointRadiusMeters + 0.005f), "On the bell, not stopped short of it.");
        }

        [Test]
        public void TheBellIsTheDrawnGuardsSize()
        {
            var bell = BladeObstacle.FromBell(Vector3.Zero, Vector3.UnitZ);

            Assert.That(bell.FindDistance(new Vector3(0.0f, 0.0f, 0.01f)), Is.EqualTo(0.01f).Within(1e-5f), "The front face is on the guard.");
            Assert.That(bell.FindDistance(new Vector3(0.0f, 0.0f, -BladePose.GuardDepthMeters - 0.01f)), Is.EqualTo(0.01f).Within(1e-5f), "The back face is a bell's depth behind.");
            Assert.That(bell.FindDistance(new Vector3(0.0f, 0.1f, -0.02f)), Is.EqualTo(0.1f - (BladePose.GuardDiameterMeters * 0.5f)).Within(1e-5f), "13.5 cm across.");
            Assert.That(bell.FindDistance(new Vector3(0.0f, 0.03f, -0.02f)), Is.LessThan(0.0f), "Solid inside.");
            Assert.That(bell.FindOutwardNormal(new Vector3(0.0f, 0.03f, 0.001f)), Is.EqualTo(Vector3.UnitZ), "Out of the front face toward the blade.");
        }

        [Test]
        public void AFastPointThroughTheBellIsCaught()
        {
            // 9 cm in one frame, through the 4 cm deep bell: no point of the path's ends is near it.
            var bell = BladeObstacle.FromBell(Vector3.Zero, Vector3.UnitZ);

            Assert.That(bell.TryFindEntry(new Vector3(0.0f, 0.03f, 0.03f), new Vector3(0.0f, 0.03f, -0.06f), DrawnBlade.BladeRadiusMeters, out var entry), Is.True);
            Assert.That(entry, Is.EqualTo((0.03f - DrawnBlade.BladeRadiusMeters) / 0.09f).Within(1e-4f), "Met on the front face.");
            Assert.That(bell.TryFindEntry(new Vector3(0.0f, 0.10f, 0.03f), new Vector3(0.0f, 0.10f, -0.06f), DrawnBlade.BladeRadiusMeters, out _), Is.False, "Past the rim: clear.");
        }

        [Test]
        public void ADeepPressNearTheGuardStaysWithinTheFrameBudget()
        {
            // The press PER-120 fixed, moving a little every frame, with the Dummy's body, blade and bell solid as in the
            // app: the same budget as PER-104's press on the blade.
            var scene = new Scene(1.8f, Hand.Right);
            var poses = new List<float>();
            for (var frame = 0; frame < 400; frame++)
            {
                poses.Add(-0.15f - (0.03f * (float)Math.Sin(frame * 0.05f)));
            }

            for (var above = 0.10f; above > -0.15f; above -= 3.0f * FrameSeconds)
            {
                scene.Frame(() => scene.Across(0.1f, above));
            }

            var fencerMilliseconds = double.MaxValue;
            var dummyMilliseconds = double.MaxValue;
            for (var run = 0; run < 3; run++)
            {
                scene.FencerWatch.Reset();
                scene.DummyWatch.Reset();
                foreach (var pose in poses)
                {
                    scene.Frame(() => scene.Across(0.1f, pose));
                }

                fencerMilliseconds = Math.Min(fencerMilliseconds, scene.FencerWatch.Elapsed.TotalMilliseconds / poses.Count);
                dummyMilliseconds = Math.Min(dummyMilliseconds, scene.DummyWatch.Elapsed.TotalMilliseconds / poses.Count);
            }

            TestContext.WriteLine($"Drawn blade step pressed near the Dummy's guard: {fencerMilliseconds:0.000} ms per frame; the Dummy's blade step: {dummyMilliseconds:0.000} ms.");
            Assert.That(scene.Dummy.IsPressed, Is.True, "Setup: pressed on the Dummy's blade.");
            Assert.That(fencerMilliseconds, Is.LessThan(0.25), "Within PER-104's budget.");
            Assert.That(dummyMilliseconds, Is.LessThan(0.15), "The Dummy's blade adds little.");
        }

        /// <summary>The Armed Dummy en garde and the Fencer's blade, stepped as the app steps them each frame.</summary>
        private sealed class Scene
        {
            private readonly DummyLayout _layout;
            private readonly SwordArmPose _enGarde;
            private readonly BladeSettings _settings = new BladeSettings();
            private readonly List<BladeObstacle> _obstacles = new List<BladeObstacle>();
            private readonly Vector3[] _samples = new Vector3[DrawnBlade.SampleCount + 1];
            private BladeSegment _forte;

            public Scene(float height, Hand hand)
            {
                _layout = DummyLayout.Create(height, hand);
                _enGarde = SwordArmReach.PoseEnGarde(height, BladePose.BladeLengthMeters);
                PoseForte();
            }

            public ArmedDummyBlade Dummy { get; } = new ArmedDummyBlade();

            public DrawnBlade Fencer { get; } = new DrawnBlade();

            public Stopwatch DummyWatch { get; } = new Stopwatch();

            public Stopwatch FencerWatch { get; } = new Stopwatch();

            /// <summary>
            /// The Fencer's blade along +x, 30 cm of it left of where it crosses the Dummy's blade line
            /// <paramref name="fraction"/> of the way from its guard (negative: behind the guard), <paramref name="above"/>
            /// over it.
            /// </summary>
            public BladeSegment Across(float fraction, float above)
            {
                var crossing = _forte.GuardPosition + (_forte.Direction * (fraction * BladePose.BladeLengthMeters)) + new Vector3(0.0f, above, 0.0f);
                return new BladeSegment(crossing - (0.3f * Vector3.UnitX), crossing + ((BladePose.BladeLengthMeters - 0.3f) * Vector3.UnitX));
            }

            /// <summary>The Fencer's blade pointing back along the Dummy's at its guard, its point <paramref name="ahead"/> in front of the bell's face and <paramref name="above"/> over the blade line.</summary>
            public BladeSegment ThrustAtTheGuard(float ahead, float above)
            {
                var tip = _forte.GuardPosition + (_forte.Direction * ahead) + new Vector3(0.0f, above, 0.0f);
                return new BladeSegment(tip + (_forte.Direction * BladePose.BladeLengthMeters), tip);
            }

            /// <summary>One frame: the Dummy's blade, then the Fencer's drawn blade against the Dummy's body, blade and bell and the floor.</summary>
            public void Frame(Func<BladeSegment> fencerHandAt)
            {
                PoseForte();
                var fencerHand = fencerHandAt();
                var shoulder = DummyLayout.DummyToStrip(_layout.Arm.GetPose(ArmJoint.Shoulder).Position, DummyDistance);
                DummyWatch.Start();
                Dummy.Step(_forte, shoulder, Vector3.UnitY, -Vector3.UnitY, _settings, fencerHand, Fencer, FrameSeconds);
                DummyWatch.Stop();
                _layout.PoseArm(Dummy.GetKnockedArmPose(_enGarde, _layout.Hand));

                _obstacles.Clear();
                foreach (var part in _layout.GetBodyOnStrip(DummyDistance))
                {
                    _obstacles.Add(part);
                }

                _obstacles.Add(BladeObstacle.Floor);
                Dummy.AddObstacles(_obstacles);
                FencerWatch.Start();
                Fencer.Step(fencerHand, _obstacles, Vector3.UnitY, FrameSeconds);
                FencerWatch.Stop();
                Dummy.UpdateSide(fencerHand, Fencer);
                for (var index = 0; index <= DrawnBlade.SampleCount; index++)
                {
                    _samples[index] = Fencer.GetSample(index);
                }

                Assert.That(DrawnShape.IsClear(_samples, null, _obstacles, DrawnBlade.BladeRadiusMeters - 0.001f, 1, true), Is.True, "Every frame, the Fencer's drawn blade is clear of the Dummy, its blade and bell, and the floor.");
            }

            /// <summary>
            /// The closest the drawn blades come, centerline to centerline, signed by whether the Fencer's is above the
            /// Dummy's there (it pressed down from above: below means through).
            /// </summary>
            public float FindSeparationAbove()
            {
                var closest = float.MaxValue;
                var isAbove = true;
                for (var piece = 0; piece < BladeBind.PieceCount; piece++)
                {
                    for (var index = 0; index < DrawnBlade.SampleCount; index++)
                    {
                        var distance = SegmentMath.FindDistanceBetweenSegments(_samples[index], _samples[index + 1], Dummy.Shape[piece], Dummy.Shape[piece + 1], out var fraction);
                        if (distance < closest)
                        {
                            var onFencer = Vector3.Lerp(_samples[index], _samples[index + 1], fraction);
                            var onDummy = SegmentMath.FindClosestPointOnSegment(onFencer, Dummy.Shape[piece], Dummy.Shape[piece + 1]);
                            closest = distance;
                            isAbove = onFencer.Y >= onDummy.Y;
                        }
                    }
                }

                return isAbove ? closest : -closest;
            }

            /// <summary>How far the Fencer's drawn blade is out of the Dummy's bell at its nearest sample (negative inside).</summary>
            public float FindBellClearance()
            {
                var direction = Dummy.Drawn.HiltDirection;
                var center = Dummy.Drawn.GuardPosition - (direction * (BladePose.GuardDepthMeters * 0.5f));
                var nearest = float.MaxValue;
                foreach (var sample in _samples)
                {
                    var along = Vector3.Dot(sample - center, direction);
                    var radial = (sample - center - (along * direction)).Length();
                    var outOfRim = radial - (BladePose.GuardDiameterMeters * 0.5f);
                    var outOfFace = Math.Abs(along) - (BladePose.GuardDepthMeters * 0.5f);
                    var distance = outOfRim > 0.0f && outOfFace > 0.0f
                        ? (float)Math.Sqrt((outOfRim * outOfRim) + (outOfFace * outOfFace))
                        : Math.Max(outOfRim, outOfFace);
                    nearest = Math.Min(nearest, distance);
                }

                return nearest;
            }

            /// <summary>The Dummy's blade line this frame, where its script holds the arm (en garde), before any knock.</summary>
            private void PoseForte()
            {
                _layout.PoseArm(_enGarde);
                _forte = new BladeSegment(
                    DummyLayout.DummyToStrip(_layout.Arm.GetGuardPosition(), DummyDistance),
                    DummyLayout.DummyToStrip(_layout.Arm.GetBladePoint(BladePose.BladeLengthMeters), DummyDistance));
            }
        }
    }
}
