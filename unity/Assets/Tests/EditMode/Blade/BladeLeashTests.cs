// The drawn blade never gets stuck (PER-121). Nikita's headset video: the drawn hilt stayed wedged between the Armed
// Dummy's blade and bell guard for 5 s with the hand 50 cm away. Now the drawn hilt is on a leash (it pops back to the
// hand past 8 cm of lag or 0.25 s of lagging, if the hand's blade is clear and not through the Dummy's blade or bell),
// it slides off the Dummy's steel instead of wedging, the ghost hilt shows only past 3 cm, and a lag buzz grows with the
// lag. And the "Blade feel" Settings that tune it reach the Core. The Dummy stands 3 m down the Strip, en garde, with
// its body, blade and bell solid as in the app. Meters, seconds, Strip frame.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Blade;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Tests.Blade
{
    public class BladeLeashTests
    {
        private const float FrameSeconds = 1.0f / 72.0f;
        private const float DummyDistance = 3.0f;

        // The hand pulls away at this speed in the wedge: it moves this far a frame, so the drawn hilt can trail it by up
        // to the leash plus one frame's move before it pops.
        private const float PullSpeed = 2.0f;
        private const float PullStepMeters = PullSpeed * FrameSeconds;

        [Test]
        public void TheWedgeBetweenTheDummysBladeAndBellLetsGoWithinTheLeash(
            [Values(1.5f, 2.1f)] float height,
            [Values(Hand.Right, Hand.Left)] Hand hand,
            [Values(0, 1, 2)] int wedge)
        {
            // The Fencer's blade comes down across the Dummy's 2 cm in front of its bell and presses past its line, then
            // the hand pulls 50 cm away at 2 m/s: along its blade (off the Dummy's guard), or toward the Dummy (past the
            // bell, low). Before PER-121 the drawn hilt held 35 to 50 cm behind the hand to the end. While the hand's blade
            // is still through the Dummy's blade it may hold (the steel is solid); once the hand's blade is clear, the
            // drawn hilt trails it by at most the leash (and one frame's move) and for at most the leash's time.
            var (depth, pull) = wedge switch
            {
                0 => (0.03f, Vector3.UnitX),
                1 => (0.10f, Vector3.UnitX),
                _ => (0.10f, Vector3.UnitZ),
            };
            var scene = new Scene(height, hand);
            scene.PressThenPull(0.02f, depth, pull, out var mostLag, out var longestLagSeconds, out var pops);

            TestContext.WriteLine($"With the hand's blade clear: most lag {mostLag * 100.0f:0.0} cm, longest lagging {longestLagSeconds:0.000} s; {pops} pops.");
            Assert.That(mostLag, Is.LessThanOrEqualTo(DrawnBlade.LeashMeters + PullStepMeters + 0.005f), "The drawn hilt never trails the clear hand by more than the leash and one frame's move.");
            Assert.That(longestLagSeconds, Is.LessThanOrEqualTo(DrawnBlade.LeashSeconds + FrameSeconds + 1e-4f), "Nor lags it for longer than the leash's time.");
            Assert.That(scene.Fencer.LagMeters, Is.EqualTo(0.0f), "Pulled away, the drawn blade is back in the hand.");
            Assert.That(scene.Fencer.IsGhostShown, Is.False);
        }

        [Test]
        public void APopNeitherKicksTheDummysBladeNorPutsTheDrawnBladePastIt([Values(1.5f, 2.1f)] float height, [Values(Hand.Right, Hand.Left)] Hand hand)
        {
            // The wedge again, the hand pulled past the bell toward the Dummy: on the frame the drawn blade pops, the Dummy's blade (its
            // point's deflection) does not jump. A pop is visual: Touches, clashes, Parries and Beats are all judged on
            // the hand's blade, and the Dummy's blade is only pushed by a drawn blade that overlaps it.
            var scene = new Scene(height, hand);
            var mostPopKick = 0.0f;
            var popCount = 0;
            scene.PressThenPull(0.02f, 0.10f, Vector3.UnitZ, out _, out _, out _, onFrame: (pointMove, isPop) =>
            {
                if (isPop)
                {
                    popCount++;
                    mostPopKick = Math.Max(mostPopKick, pointMove);
                }
            });

            Assert.That(popCount, Is.GreaterThan(0), "Setup: the drawn blade popped back to the hand.");
            Assert.That(mostPopKick, Is.LessThan(0.01f), "The pop does not knock the Dummy's point.");
        }

        [Test]
        public void ThePopNeverGoesThroughTheDummysBlade([Values(1.5f, 2.1f)] float height, [Values(Hand.Right, Hand.Left)] Hand hand)
        {
            // PER-119's deep press near the guard, held 2 s with the hand 30 cm past the Dummy's line: the hand's blade is
            // clear of everything there, but across the Dummy's blade on its far side. The drawn blade stays on top.
            var scene = new Scene(height, hand);
            var above = 0.10f;
            var pops = 0;
            for (var frame = 0; frame < 216; frame++)
            {
                above = Math.Max(-0.30f, above - (3.0f * FrameSeconds));
                scene.Frame(scene.Across(0.1f, above));
                pops += scene.Fencer.HasPopped ? 1 : 0;
            }

            Assert.That(pops, Is.EqualTo(0));
            Assert.That(scene.Fencer.IsOnSide || scene.Fencer.IsDecoupled, Is.True, "The drawn blade bends over the Dummy's, or lags the hand on top of it.");
        }

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
            // The flat swept 40 cm across a forearm-thin upright capsule in one frame: held on the near side, it pops to
            // the hand once the hand has stayed clear on the far side for the leash's time. Only drawn: no Touch.
            var arm = new BladeObstacle[] { new Capsule(new Vector3(0.0f, 0.5f, 0.45f), new Vector3(0.0f, 1.5f, 0.45f), 0.03f) };
            var drawn = new DrawnBlade();
            drawn.Step(BladeAt(-0.2f, 0.0f), arm, Vector3.UnitY, FrameSeconds);

            drawn.Step(BladeAt(0.2f, 0.0f), arm, Vector3.UnitY, FrameSeconds);

            Assert.That(drawn.HasPopped, Is.True, "40 cm is past the leash at once.");
            Assert.That(drawn.GuardPosition.X, Is.EqualTo(0.2f).Within(1e-4f), "The hilt is in the hand; the blade springs from where it was.");
            Assert.That(drawn.LagMeters, Is.EqualTo(0.0f));
        }

        [Test]
        public void APointPushedSquareOntoTheDummysBladeSlidesRoundItInsteadOfWedging()
        {
            // A thrust square onto the middle of a blade lying across it, pushed on 50 cm. A body of the same size held
            // the point; the steel lets it slip off and round, so the drawn blade stays within a few centimeters of the
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
                Assert.That(DrawnShape.IsClear(Samples(drawn), null, steel, DrawnBlade.BladeRadiusMeters - 0.001f, 1, true), Is.True, $"Frame {frame}: clear of the steel.");
                mostLag = Math.Max(mostLag, drawn.LagMeters);
            }

            Assert.That(mostLag, Is.LessThan(DrawnBlade.LeashMeters), "Never further off than the leash.");
            Assert.That(drawn.LagMeters, Is.LessThan(DrawnBlade.GhostLagMeters), "Off the steel, close beside the hand: no ghost.");
            Assert.That(drawn.Contact.IsPinned, Is.False, "The point slipped off.");
        }

        [Test]
        public void TheGhostShowsOnlyPastItsThreshold()
        {
            // Pushed 1 cm past the most the blade bends into the wall: the drawn hilt lags 1 cm (the logic's lag), with no
            // ghost; 5 cm past, the ghost shows. A ghost threshold of 0 shows it at any lag, as before PER-121.
            var wall = WallObstacles();
            var mostBend = WallPinnedZ - BladePose.BladeLengthMeters + BladeContactBend.MaxPushMeters;

            var drawn = PushIntoWall(wall, mostBend + 0.01f, DrawnBlade.GhostLagMeters);
            Assert.That(drawn.IsDecoupled, Is.True, "Setup: the drawn hilt lags.");
            Assert.That(drawn.LagMeters, Is.EqualTo(0.01f).Within(0.002f));
            Assert.That(drawn.IsGhostShown, Is.False);

            Assert.That(PushIntoWall(wall, mostBend + 0.05f, DrawnBlade.GhostLagMeters).IsGhostShown, Is.True);
            Assert.That(PushIntoWall(wall, mostBend + 0.01f, 0.0f).IsGhostShown, Is.True, "Threshold 0: any lag.");
        }

        [Test]
        public void AShorterLeashPopsSooner()
        {
            var lags = new List<float>();
            foreach (var leash in new[] { 0.04f, 0.12f })
            {
                var feel = new BladeFeelSettings { LeashMeters = leash, LeashSeconds = 1.0f };
                var scene = new Scene(1.8f, Hand.Right, feel);
                scene.PressThenPull(0.02f, 0.10f, Vector3.UnitZ, out var mostLag, out _, out _);
                lags.Add(mostLag);
            }

            Assert.That(lags[0], Is.LessThanOrEqualTo(0.04f + PullStepMeters + 0.005f));
            Assert.That(lags[1], Is.GreaterThan(lags[0] + 0.04f), "A longer leash lets the hilt trail further.");
        }

        [Test]
        public void HigherFrictionHoldsThePointLonger()
        {
            // The point pinned on a ball, the hand moving sideways 1 cm a frame: how far the hand gets before the point
            // starts to slide (Settings "Point friction on jacket", PER-121).
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

            TestContext.WriteLine($"The point slid once the hand was {slips[0] * 100.0f:0} cm aside at friction 0.2, {slips[1] * 100.0f:0} cm at 1.2.");
            Assert.That(slips[1], Is.GreaterThan(slips[0]));
        }

        [Test]
        public void TheBladeFrictionSettingIsOnTheDummysBladeAndBell()
        {
            var scene = new Scene(1.8f, Hand.Right, new BladeFeelSettings { BladeFriction = 0.9f });
            scene.Frame(scene.Across(0.5f, 0.2f));

            var steel = scene.Obstacles.Where(obstacle => obstacle.IsSteel).ToList();
            Assert.That(steel, Has.Count.EqualTo(BladeBind.PieceCount + 1), "The Dummy's blade pieces and its bell.");
            Assert.That(steel.Select(obstacle => obstacle.FrictionCoefficient), Is.All.EqualTo(0.9f));
        }

        [Test]
        public void TheDummysBladeGivesWayByTheYieldSetting()
        {
            // The Fencer presses 5 cm past the Dummy's line at its middle for a second: rigid, its point stays; scaled up,
            // it gives way further.
            var given = new List<float>();
            foreach (var scale in new[] { 0.0f, 1.0f, 1.25f })
            {
                var scene = new Scene(1.8f, Hand.Right, new BladeFeelSettings { DummyBladeYieldScale = scale });
                var above = 0.10f;
                for (var frame = 0; frame < 72; frame++)
                {
                    above = Math.Max(-0.05f, above - (1.0f * FrameSeconds));
                    scene.Frame(scene.Across(0.5f, above));
                }

                given.Add(scene.Dummy.Deflection.TipOffset.Length());
            }

            TestContext.WriteLine($"The Dummy's point gave way {given[0] * 100.0f:0.0}, {given[1] * 100.0f:0.0} and {given[2] * 100.0f:0.0} cm.");
            Assert.That(given[0], Is.LessThan(0.005f), "Rigid: it does not give way (only touching pushes it, a few millimeters).");
            Assert.That(given[1], Is.GreaterThan(given[0] + 0.01f));
            Assert.That(given[2], Is.GreaterThan(given[1]));
        }

        [Test]
        public void TheLagBuzzGrowsWithTheLagAndIsUnlikeTheOtherBuzzes()
        {
            Assert.That(BladeHaptics.GetLagStrength(0.0f, 0.08f, 1.0f), Is.EqualTo(0.0f), "Following the hand: no buzz.");
            Assert.That(BladeHaptics.GetLagStrength(0.001f, 0.08f, 1.0f), Is.EqualTo(BladeHaptics.LagFloor).Within(0.02f), "Felt from the smallest lag.");
            Assert.That(BladeHaptics.GetLagStrength(0.04f, 0.08f, 1.0f), Is.EqualTo((1.0f + BladeHaptics.LagFloor) * 0.5f).Within(1e-5f));
            Assert.That(BladeHaptics.GetLagStrength(0.2f, 0.08f, 1.0f), Is.EqualTo(1.0f), "Full at the leash and past it.");
            Assert.That(BladeHaptics.GetLagStrength(0.04f, 0.08f, 0.0f), Is.EqualTo(0.0f), "Strength 0: off.");
            Assert.That(BladeHaptics.GetLagStrength(0.04f, 0.08f, 0.5f), Is.LessThan(BladeHaptics.GetLagStrength(0.04f, 0.08f, 1.0f)));
            Assert.That(BladeHaptics.GetLagStrength(0.04f, 0.08f, 2.0f), Is.EqualTo(1.0f), "Stronger, never past full.");

            // Continuous: each pulse runs past the next one's start. Low, where every clash ring is high.
            var pulse = BladeHaptics.LagPulse.Single();
            Assert.That(pulse.DurationSeconds, Is.GreaterThan(BladeHaptics.LagPulseIntervalSeconds));
            foreach (var pattern in new[] { HapticPattern.Tick, HapticPattern.Crisp, HapticPattern.Heavy, HapticPattern.DoubleTap })
            {
                Assert.That(ClashHaptics.GetSegments(pattern).Where(segment => segment.Amplitude > 0.0f).Select(segment => segment.Frequency), Is.All.GreaterThan(pulse.Frequency * 2.0f), pattern.ToString());
            }

            Assert.That(BladeHaptics.LeashClick.Sum(segment => segment.DurationSeconds), Is.LessThan(0.02f), "The pop's click is one short tick.");
        }

        [Test]
        public void TheBladeFeelDefaultsAreTheBladeCoresConstants()
        {
            var feel = new BladeFeelSettings();

            Assert.That(feel.LeashMeters, Is.EqualTo(DrawnBlade.LeashMeters));
            Assert.That(feel.LeashSeconds, Is.EqualTo(DrawnBlade.LeashSeconds));
            Assert.That(feel.GhostMeters, Is.EqualTo(DrawnBlade.GhostLagMeters));
            Assert.That(feel.BladeFriction, Is.EqualTo(BladeObstacle.BladeFriction));
            Assert.That(feel.JacketFriction, Is.EqualTo(BladeObstacle.JacketFriction));
            Assert.That(feel.LagBuzzStrength, Is.EqualTo(1.0f));
            Assert.That(feel.DummyBladeYieldScale, Is.EqualTo(1.0f));
        }

        [Test]
        public void TheFeelsLowestValuesAreAccepted()
        {
            // The settings screen's lowest values (SettingsPage): the Core takes them.
            var drawn = new DrawnBlade();
            Assert.DoesNotThrow(() => drawn.SetFeel(0.03f, 0.1f, 0.0f));
            Assert.DoesNotThrow(() => BladeObstacle.FromCapsule(new Capsule(Vector3.Zero, Vector3.UnitY, 0.1f), 0.0f));
            Assert.DoesNotThrow(() => new BladeBind().FindYieldTipOffset(BladeAt(0.0f, 0.0f), new Vector3[BladeBind.PieceCount + 1], 0.0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => drawn.SetFeel(0.0f, 0.1f, 0.0f), "No leash at all is not a value.");
        }

        private const float WallPinnedZ = 1.0f - BladeContactBend.PointRadiusMeters;

        private static IReadOnlyList<BladeObstacle> WallObstacles()
        {
            return new BladeObstacle[] { new Capsule(new Vector3(-2.0f, 1.0f, 1.2f), new Vector3(2.0f, 1.0f, 1.2f), 0.2f) };
        }

        /// <summary>The hand's guard pushed in at 1 cm a frame to <paramref name="guardZ"/>, the point pinned on the wall.</summary>
        private static DrawnBlade PushIntoWall(IReadOnlyList<BladeObstacle> wall, float guardZ, float ghostLagMeters)
        {
            var drawn = new DrawnBlade();
            drawn.SetFeel(DrawnBlade.LeashMeters, DrawnBlade.LeashSeconds, ghostLagMeters);
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

        private static Vector3[] Samples(DrawnBlade drawn)
        {
            return Enumerable.Range(0, DrawnBlade.SampleCount + 1).Select(drawn.GetSample).ToArray();
        }

        /// <summary>The Armed Dummy en garde and the Fencer's blade, stepped as the app steps them each frame, with the feel Settings passed in.</summary>
        private sealed class Scene
        {
            private readonly DummyLayout _layout;
            private readonly SwordArmPose _enGarde;
            private readonly BladeSettings _settings = new BladeSettings();
            private BladeSegment _forte;

            public Scene(float height, Hand hand, BladeFeelSettings feel = null)
            {
                _layout = DummyLayout.Create(height, hand);
                _enGarde = SwordArmReach.PoseEnGarde(height, BladePose.BladeLengthMeters);
                _settings.Feel = feel ?? new BladeFeelSettings();
                Fencer.SetFeel(_settings.Feel.LeashMeters, _settings.Feel.LeashSeconds, _settings.Feel.GhostMeters);
                PoseForte();
            }

            public ArmedDummyBlade Dummy { get; } = new ArmedDummyBlade();

            public DrawnBlade Fencer { get; } = new DrawnBlade();

            public List<BladeObstacle> Obstacles { get; } = new List<BladeObstacle>();

            /// <summary>
            /// The Fencer's blade along +x, 30 cm of it left of where it crosses the Dummy's blade line
            /// <paramref name="fraction"/> of the way from its guard, <paramref name="above"/> over it.
            /// </summary>
            public BladeSegment Across(float fraction, float above)
            {
                var crossing = _forte.GuardPosition + (_forte.Direction * (fraction * BladePose.BladeLengthMeters)) + new Vector3(0.0f, above, 0.0f);
                return new BladeSegment(crossing - (0.3f * Vector3.UnitX), crossing + ((BladePose.BladeLengthMeters - 0.3f) * Vector3.UnitX));
            }

            /// <summary>
            /// The wedge: the blade comes down at 3 m/s across the Dummy's <paramref name="fraction"/> of the way from its
            /// guard to <paramref name="depth"/> past its line and holds a second, then the hand pulls 50 cm along
            /// <paramref name="pull"/> at <see cref="PullSpeed"/> and holds a second more. Measures the drawn hilt's lag
            /// while pulling and after, on the frames the hand's blade is clear (<see cref="IsHandClear"/>): the most, and
            /// the longest it lagged in a row.
            /// </summary>
            /// <param name="onFrame">Each pulling frame: how far the Dummy's point moved, and whether the drawn blade popped.</param>
            public void PressThenPull(
                float fraction, float depth, Vector3 pull, out float mostLag, out float longestLagSeconds, out int pops, Action<float, bool> onFrame = null)
            {
                var above = 0.10f;
                for (var frame = 0; frame < 72; frame++)
                {
                    above = Math.Max(-depth, above - (3.0f * FrameSeconds));
                    Frame(Across(fraction, above));
                }

                mostLag = 0.0f;
                longestLagSeconds = 0.0f;
                pops = 0;
                var lagSeconds = 0.0f;
                for (var frame = 1; frame <= 72 + 36; frame++)
                {
                    var pulled = Math.Min(0.5f, frame * PullStepMeters);
                    var hand = Across(fraction, -depth);
                    var pointBefore = Dummy.Deflection.TipOffset;
                    var moved = new BladeSegment(hand.GuardPosition + (pull * pulled), hand.TipPosition + (pull * pulled));
                    var isHandClear = IsHandClear(moved);
                    Frame(moved);
                    if (isHandClear)
                    {
                        mostLag = Math.Max(mostLag, Fencer.LagMeters);
                        lagSeconds = Fencer.IsDecoupled ? lagSeconds + FrameSeconds : 0.0f;
                        longestLagSeconds = Math.Max(longestLagSeconds, lagSeconds);
                    }
                    else
                    {
                        lagSeconds = 0.0f;
                    }

                    pops += Fencer.HasPopped ? 1 : 0;
                    onFrame?.Invoke(Vector3.Distance(Dummy.Deflection.TipOffset, pointBefore), Fencer.HasPopped);
                }
            }

            /// <summary>
            /// True when the hand's blade (straight) is clear of the Dummy, its blade and bell and the floor as they were
            /// last frame, and not across the Dummy's blade or bell on the far side from the drawn blade: where the drawn
            /// blade may be put.
            /// </summary>
            private bool IsHandClear(BladeSegment hand)
            {
                var shape = Enumerable.Range(0, DrawnBlade.SampleCount + 1)
                    .Select(index => Vector3.Lerp(hand.GuardPosition, hand.TipPosition, (float)index / DrawnBlade.SampleCount))
                    .ToArray();
                if (!DrawnShape.IsClear(shape, null, Obstacles, DrawnBlade.BladeRadiusMeters, 1, false))
                {
                    return false;
                }

                var drawn = Samples(Fencer);
                foreach (var steel in Obstacles.Where(obstacle => obstacle.IsSteel))
                {
                    var drawnNear = drawn.OrderBy(steel.FindDistance).First();
                    if (steel.IsAcrossOnFarSide(hand.GuardPosition, hand.TipPosition, drawnNear))
                    {
                        return false;
                    }
                }

                return true;
            }

            /// <summary>One frame: the Dummy's blade, then the Fencer's drawn blade against the Dummy's body, blade and bell and the floor.</summary>
            public void Frame(BladeSegment fencerHand)
            {
                PoseForte();
                var shoulder = DummyLayout.DummyToStrip(_layout.Arm.GetPose(ArmJoint.Shoulder).Position, DummyDistance);
                Dummy.Step(_forte, shoulder, Vector3.UnitY, -Vector3.UnitY, _settings, fencerHand, Fencer, FrameSeconds);
                _layout.PoseArm(Dummy.GetKnockedArmPose(_enGarde, _layout.Hand));

                Obstacles.Clear();
                foreach (var part in _layout.GetBodyOnStrip(DummyDistance))
                {
                    Obstacles.Add(BladeObstacle.FromCapsule(part, _settings.Feel.JacketFriction));
                }

                Obstacles.Add(BladeObstacle.Floor);
                Dummy.AddObstacles(Obstacles, _settings.Feel.BladeFriction);
                Fencer.Step(fencerHand, Obstacles, Vector3.UnitY, FrameSeconds);
                Dummy.UpdateSide(fencerHand, Fencer);
                Assert.That(DrawnShape.IsClear(Samples(Fencer), null, Obstacles, DrawnBlade.BladeRadiusMeters - 0.001f, 1, true), Is.True, "Every frame, the Fencer's drawn blade is clear of the Dummy, its blade and bell, and the floor.");
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
