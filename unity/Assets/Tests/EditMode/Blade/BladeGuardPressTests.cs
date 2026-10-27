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
            // blade after about 0.3 s and kicked its point 7 to 12 cm a frame, until the blades overlapped. Since PER-122
            // a press this deep near the guard is past every give, so a blade may slide off: the side may change once, the
            // blades clear of each other, never through. Since PER-147 a push past every give, the slide and the margin
            // is through instead: the side changes with the hands, and only then, and while the blades swing back to their
            // hands after any let-go, may the drawn blades cross.
            var scene = new Scene(height, hand);
            var mostPointMove = 0.0f;
            var mostHeldPointMove = 0.0f;
            var mostHeldDrawnMove = 0.0f;
            var worstSeparation = float.MaxValue;
            var above = 0.10f;
            for (var frame = 0; frame < 216; frame++)
            {
                above = Math.Max(-depth, above - (3.0f * FrameSeconds));
                var pointBefore = scene.Dummy.JudgedTipOffset;
                var drawnBefore = scene.Fencer.HasShape ? scene.Fencer.GetSample(DrawnBlade.SampleCount) : Vector3.Zero;
                var sideBefore = scene.Dummy.Contact.Side;
                scene.Frame(() => scene.Across(0.1f, above));

                var contact = scene.Dummy.Contact;
                if (sideBefore.HasValue && Vector3.Dot(sideBefore.Value, contact.Side.Value) < 0.0f)
                {
                    // The blade slid off and the side changed over this frame: the blades apart, not through (pushed
                    // through, PER-147, they may be).
                    if (!contact.IsThrough)
                    {
                        Assert.That(BladePairScene.FindDistance(scene.FencerSamples(), BladePairScene.Samples(scene.Dummy.Drawn)), Is.GreaterThan(BladeContactSolver.ContactMeters - ToleranceMeters), $"Frame {frame}: changed sides through.");
                    }

                    continue;
                }

                if (!contact.IsInContact && contact.ReturningBlade >= 0)
                {
                    // Let go and swinging back to the hands, which are past each other now: they may cross (PER-147).
                    continue;
                }

                var separation = scene.FindSeparationAlongSide();
                worstSeparation = Math.Min(worstSeparation, separation);
                Assert.That(separation, Is.GreaterThan(BladeContactSolver.ContactMeters - ToleranceMeters), $"Frame {frame}: the Fencer's drawn blade reached the Dummy's.");
                var pointMove = Vector3.Distance(scene.Dummy.JudgedTipOffset, pointBefore);
                mostPointMove = Math.Max(mostPointMove, pointMove);
                if (frame >= 72)
                {
                    // Held still for a second: nothing should move but a slow settle.
                    mostHeldPointMove = Math.Max(mostHeldPointMove, pointMove);
                    mostHeldDrawnMove = Math.Max(mostHeldDrawnMove, Vector3.Distance(scene.Fencer.GetSample(DrawnBlade.SampleCount), drawnBefore));
                }
            }

            TestContext.WriteLine($"Closest {worstSeparation * 100.0f:0.00} cm; the Dummy's point moved at most {mostPointMove * 100.0f:0.0} cm a frame, held {mostHeldPointMove * 1000.0f:0.0} mm; the drawn point held {mostHeldDrawnMove * 1000.0f:0.0} mm.");
            Assert.That(mostHeldPointMove, Is.LessThan(0.002f), "Held, the Dummy's point is still (no jitter).");
            Assert.That(mostHeldDrawnMove, Is.LessThan(0.002f), "Held, the Fencer's drawn point is still (no jitter).");
        }

        [Test]
        public void APressAcrossTheGuardRestsOnTheBellOrSlidesRoundIt(
            [Values(1.5f, 2.1f)] float height,
            [Values(Hand.Right, Hand.Left)] Hand hand,
            [Values(0.05f, -0.20f)] float depth)
        {
            // The Fencer's blade comes down at 3 m/s across the Dummy's bell (2 cm behind its front face, in front of
            // the hand) to 5 cm above or 20 cm below the Dummy's blade line. The bell (6.75 cm round that line) is solid
            // in the contact solver (PER-122): a light press rests on its rim, the blade turning and bowing; a deep one
            // slides off its front face. Never into it while the blade is the solver's. Pressed 20 cm down, the hand's
            // blade also runs through the Dummy's hand and forearm behind the bell: that body holds the drawn blade off
            // the hand (PR 70's lag and leash, bodies only), and that lag is not the solver's to keep out of the bell.
            var scene = new Scene(height, hand);
            var above = 0.15f;
            var touched = 0;
            for (var frame = 0; frame < 108; frame++)
            {
                above = Math.Max(depth, above - (3.0f * FrameSeconds));
                scene.Frame(() => scene.Across(-0.02f / BladePose.BladeLengthMeters, above));
                touched += scene.Dummy.Contact.IsAOnHiltB ? 1 : 0;
                if (scene.Fencer.IsDecoupled)
                {
                    Assert.That(depth, Is.LessThan(0.0f), $"Frame {frame}: only the deep press reaches the Dummy's hand.");
                    continue;
                }

                Assert.That(scene.FindBellClearance(), Is.GreaterThan(DrawnBlade.BladeRadiusMeters - ToleranceMeters), $"Frame {frame}: the Fencer's drawn blade went into the bell.");
            }

            Assert.That(touched, Is.GreaterThan(0), "The blade met the bell.");
            if (depth > 0.0f)
            {
                Assert.That(scene.FindBellClearance(), Is.LessThan(DrawnBlade.BladeRadiusMeters + 0.01f), "A light press lies on the bell.");
            }
        }

        [Test]
        public void APointOnTheBellSkidsOffItsFace([Values(1.5f, 2.1f)] float height, [Values(Hand.Right, Hand.Left)] Hand hand)
        {
            // The Fencer thrusts at the Dummy's guard along its blade, 4 cm above it, and on 5 cm past the bell's face:
            // the point lands on the face and, end-on, skids along it toward the rim as the hand goes on (PER-122),
            // never into the bell or the hand behind it, the drawn hilt in the Fencer's hand.
            var scene = new Scene(height, hand);
            var touched = 0;
            for (var frame = 0; frame < 72; frame++)
            {
                var reach = Math.Min(0.25f, frame * 1.0f * FrameSeconds);
                scene.Frame(() => scene.ThrustAtTheGuard(0.20f - reach, 0.04f));
                Assert.That(scene.FindBellClearance(), Is.GreaterThan(DrawnBlade.BladeRadiusMeters - ToleranceMeters), $"Frame {frame}: the Fencer's drawn blade went into the bell.");
                Assert.That(scene.Fencer.IsDecoupled, Is.False, $"Frame {frame}: the bell never holds the drawn hilt off the hand.");
                touched += scene.Dummy.Contact.IsAOnHiltB ? 1 : 0;
            }

            Assert.That(touched, Is.GreaterThan(0), "The point met the bell.");
            Assert.That(scene.FindBellClearance(), Is.LessThan(DrawnBlade.BladeRadiusMeters + 0.01f), "Still on the bell, not pushed off it.");
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
            Assert.That(scene.Dummy.Contact.IsNear, Is.True, "Setup: at the Dummy's blade and bell (pressed, or sliding off past every limit).");
            // 0.40 ms until the hilts became solid in the contact solver (PER-122 fix); pressed by the guard both bells are
            // looked at too. Still about 3% of a 72 Hz frame.
            Assert.That(fencerMilliseconds + dummyMilliseconds, Is.LessThan(0.45), "Both blades, their contact and the hilts together, near the Dummy's guard.");
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
                Dummy.Step(_forte, shoulder, Vector3.UnitY, -Vector3.UnitY, _settings, OpponentHand.Medium, fencerHand, default, FrameSeconds);
                DummyWatch.Stop();
                _layout.PoseArm(Dummy.GetKnockedArmPose(_enGarde, _layout.Hand));

                _obstacles.Clear();
                foreach (var part in _layout.GetBodyOnStrip(DummyDistance))
                {
                    _obstacles.Add(part);
                }

                _obstacles.Add(BladeObstacle.Floor);
                FencerWatch.Start();
                if (Dummy.Contact.IsNear)
                {
                    Fencer.Flex.Reset();
                }

                Fencer.Step(Dummy.FencerGive.Chord, _obstacles, Vector3.UnitY, FrameSeconds, Dummy.FencerGive.Droop);
                FencerWatch.Stop();
                for (var index = 0; index <= DrawnBlade.SampleCount; index++)
                {
                    _samples[index] = Fencer.GetSample(index);
                }

                Assert.That(
                    Fencer.IsDecoupled || Vector3.Distance(Fencer.GuardPosition, fencerHand.GuardPosition) <= DrawnShape.LagMeters + 1e-4f,
                    Is.True,
                    "Every frame the drawn hilt is in the hand, unless the bell or a body holds it (never the Dummy's blade).");
                Assert.That(
                    Fencer.IsDecoupled || DrawnShape.IsClear(_samples, null, _obstacles, DrawnBlade.BladeRadiusMeters - 0.001f, 1, true),
                    Is.True,
                    "Every frame, the Fencer's drawn blade is clear of the Dummy, its bell and the floor, or holds off them.");
            }

            /// <summary>
            /// The closest the drawn blades come along the side the contact holds (from the Dummy's to the Fencer's;
            /// negative means through), where they lie over each other seen along it.
            /// </summary>
            public Vector3[] FencerSamples()
            {
                return (Vector3[])_samples.Clone();
            }

            public float FindSeparationAlongSide()
            {
                return BladePairScene.FindSeparation(_samples, BladePairScene.Samples(Dummy.Drawn), Dummy.Contact.Side.Value);
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
