// Visual non-penetration (PER-48): the drawn blade never passes through a body. The point pins on the near-side
// surface it meets and the blade bows as the hand pushes on; past the most bow the drawn blade lags the hand; the
// point lets go only when the hand comes back out or goes around.
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Blade;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Tests.Blade
{
    public class BladeContactBendTests
    {
        private const float FrameSeconds = 1.0f / 72.0f;

        // A wide body whose front surface is the plane z = 1 near x = 0: the axis runs along x at z = 1.2.
        private const float WallAxisZ = 1.2f;
        private const float WallRadius = 0.2f;
        private static readonly Capsule WallCapsule = new Capsule(new Vector3(-2.0f, 1.0f, WallAxisZ), new Vector3(2.0f, 1.0f, WallAxisZ), WallRadius);
        private static readonly IReadOnlyList<BladeObstacle> Wall = new BladeObstacle[] { WallCapsule };

        // The point rests on the surface: the surface minus the drawn point's radius.
        private const float PinnedZ = 1.0f - BladeContactBend.PointRadiusMeters;

        // A leash too long to act (PER-121): the tests of the hold through a body run without the pop to the hand.
        private const float LongLeashMeters = 10.0f;
        private const float LongLeashSeconds = 100.0f;

        [Test]
        public void ThePointStopsWhereItFirstMeetsTheBody()
        {
            var bend = new BladeContactBend();
            bend.Step(BladeAt(0.0f, 0.05f), Wall, Vector3.UnitY);
            Assert.That(bend.IsPinned, Is.False, "Not there yet.");

            // One fast frame carries the straight point 5 cm into the body.
            bend.Step(BladeAt(0.0f, 0.15f), Wall, Vector3.UnitY);

            Assert.That(bend.IsPinned, Is.True);
            Assert.That(bend.PinnedPoint.Z, Is.EqualTo(PinnedZ).Within(1e-4f));
            Assert.That(Vector3.Distance(bend.GetCenterlinePoint(BladeAt(0.0f, 0.15f), 1.0f), bend.PinnedPoint), Is.LessThan(1e-5f));
            Assert.That(bend.PushMeters, Is.EqualTo(0.15f + 0.9f - PinnedZ).Within(1e-4f));
        }

        [Test]
        public void TheBladeBowsMoreThePushGoesOnAndKeepsItsLength()
        {
            var bend = PinAt(0.12f);
            var bows = new List<float>();
            foreach (var guardZ in new[] { 0.12f, 0.14f, 0.17f, 0.20f, 0.24f })
            {
                var blade = BladeAt(0.0f, guardZ);
                Assert.That(bend.Step(blade, Wall, Vector3.UnitY), Is.EqualTo(PinRelease.None));
                Assert.That(bend.IsPinned, Is.True);
                Assert.That(bend.IsDecoupled, Is.False);
                Assert.That(bend.GuardPosition, Is.EqualTo(blade.GuardPosition), "Within the most bow the drawn blade stays in the hand.");
                bows.Add(bend.BowMeters);
                Assert.That(MeasureDrawnLength(bend, blade), Is.EqualTo(BladePose.BladeLengthMeters).Within(0.003f), $"Guard at {guardZ}");
                Assert.That(bend.GetCenterlinePoint(blade, 0.5f).Y, Is.LessThan(1.0f), "Pushed straight in, it bows to its set: away from the grip's up (PER-50).");
            }

            Assert.That(bows, Is.Ordered.Ascending);
            Assert.That(bows.Last(), Is.InRange(0.10f, 0.30f), "About 15 cm of push buckles a 90 cm blade by a hand's width or more.");
        }

        [Test]
        public void TheBladeStillLeavesTheGuardAlongTheHand()
        {
            var bend = PinAt(0.12f);
            var blade = BladeAt(0.0f, 0.18f);
            bend.Step(blade, Wall, Vector3.UnitY);

            var nearGuard = bend.GetCenterlinePoint(blade, 0.02f) - blade.GuardPosition;

            // The first drawn piece is a 3.75 cm chord, and the elastica curves most near the guard (PER-50): its chord
            // turns a few degrees even though the blade leaves the guard exactly along the hand.
            Assert.That(SegmentMath.FindAngleDegrees(nearGuard, blade.Direction), Is.LessThan(5.0f));
            Assert.That(bend.GetCenterlinePoint(blade, 0.0f), Is.EqualTo(blade.GuardPosition));
        }

        [Test]
        public void ADeepPushNeverPutsTheDrawnBladeThroughTheBody()
        {
            // The hand pushes on until its guard is past the far side of the body, then comes back. The hold is tested,
            // not the leash, which pops the drawn blade to the hand once the hand's blade is clear past the body.
            var drawn = new DrawnBlade();
            drawn.SetFeel(LongLeashMeters, LongLeashSeconds, DrawnBlade.GhostLagMeters);
            drawn.Step(BladeAt(0.0f, 0.0f), Wall, Vector3.UnitY, FrameSeconds);
            var guardPath = Enumerable.Range(1, 160).Select(step => step * 0.01f).ToList();
            guardPath.AddRange(Enumerable.Range(0, 160).Select(step => 1.6f - (step * 0.01f)));
            var wasDecoupled = false;
            foreach (var guardZ in guardPath)
            {
                var release = drawn.Step(BladeAt(0.0f, guardZ), Wall, Vector3.UnitY, FrameSeconds);
                AssertOutsideOnTheNearSide(drawn, $"guard at {guardZ:0.00}");
                if (guardZ > 0.3f)
                {
                    Assert.That(release, Is.EqualTo(PinRelease.None), $"Still pressed at guard {guardZ:0.00}.");
                    Assert.That(drawn.Contact.IsPinned, Is.True, $"Still pinned at guard {guardZ:0.00}.");
                }

                wasDecoupled |= drawn.IsDecoupled;
                if (drawn.IsDecoupled)
                {
                    Assert.That(drawn.Contact.PushMeters, Is.GreaterThan(BladeContactBend.MaxPushMeters));
                    Assert.That(drawn.GuardPosition.Z, Is.EqualTo(PinnedZ - BladePose.BladeLengthMeters + BladeContactBend.MaxPushMeters).Within(1e-3f),
                        "The drawn guard holds where the most bow puts it.");
                }
            }

            Assert.That(wasDecoupled, Is.True);
            Assert.That(drawn.IsDecoupled, Is.False, "Back at the start the drawn blade follows the hand again.");
            Assert.That(drawn.Contact.IsPinned, Is.False, "Pulled out, the point let go.");
        }

        [Test]
        public void PullingBackLetsThePointGo()
        {
            var bend = PinAt(0.12f);

            // The straight point is back at the pinned point with the guard at PinnedZ - 0.9 = 0.0955.
            Assert.That(bend.Step(BladeAt(0.0f, 0.10f), Wall, Vector3.UnitY), Is.EqualTo(PinRelease.None), "Still pushed in.");
            Assert.That(bend.Step(BladeAt(0.0f, 0.09f), Wall, Vector3.UnitY), Is.EqualTo(PinRelease.CameOut));
            Assert.That(bend.IsPinned, Is.False);
        }

        [Test]
        public void MovingTheHandSidewaysSlidesThePointOverTheSurfaceAndOff()
        {
            // A ball: moving the hand sideways, the point slides around its near side and comes off beside it.
            var center = new Vector3(0.0f, 1.0f, 1.2f);
            var ball = new BladeObstacle[] { new Capsule(center, center, 0.2f) };
            var drawn = new DrawnBlade();
            drawn.Step(BladeAt(0.0f, 0.0f), ball, Vector3.UnitY, FrameSeconds);
            drawn.Step(BladeAt(0.0f, 0.2f), ball, Vector3.UnitY, FrameSeconds);
            Assert.That(drawn.Contact.IsPinned, Is.True);

            var release = PinRelease.None;
            var slid = false;
            var x = 0.0f;
            while (release == PinRelease.None && x < 1.0f)
            {
                x += 0.01f;
                release = drawn.Step(BladeAt(x, 0.2f), ball, Vector3.UnitY, FrameSeconds);
                slid |= drawn.Contact.IsPinned && drawn.Contact.PinnedPoint.X > 0.01f;
                for (var index = 1; index <= DrawnBlade.SampleCount; index++)
                {
                    Assert.That(Vector3.Distance(drawn.GetSample(index), center), Is.GreaterThanOrEqualTo(0.2f + DrawnBlade.BladeRadiusMeters - 1e-3f), $"x {x:0.00}, point {index}");
                    Assert.That(drawn.GetSample(index).Z, Is.LessThan(1.2f), $"x {x:0.00}, point {index} on the far side");
                }
            }

            Assert.That(slid, Is.True, "The point slid along the surface before it came off.");
            Assert.That(release, Is.EqualTo(PinRelease.CameOut));
        }

        [Test]
        public void AThrustThatWouldTunnelInOneFrameIsStillCaught()
        {
            // From 10 cm short to 30 cm past the body's far side in one 72 Hz frame (58 m/s).
            var drawn = new DrawnBlade();
            drawn.Step(BladeAt(0.0f, 0.0f), Wall, Vector3.UnitY, FrameSeconds);

            drawn.Step(BladeAt(0.0f, 0.8f), Wall, Vector3.UnitY, FrameSeconds);

            Assert.That(drawn.Contact.IsPinned, Is.True);
            Assert.That(drawn.Contact.PinnedPoint.Z, Is.EqualTo(PinnedZ).Within(1e-4f));
            AssertOutsideOnTheNearSide(drawn, "after the thrust");
        }

        [Test]
        public void TheFlatSweptAcrossAThinArmInOneFrameStaysOnTheNearSide()
        {
            // A forearm-thin upright capsule halfway along the blade; the blade sweeps 40 cm sideways across it in one frame.
            var armCapsule = new Capsule(new Vector3(0.0f, 0.5f, 0.45f), new Vector3(0.0f, 1.5f, 0.45f), 0.03f);
            var arm = new BladeObstacle[] { armCapsule };
            var drawn = new DrawnBlade();
            drawn.SetFeel(LongLeashMeters, LongLeashSeconds, DrawnBlade.GhostLagMeters);
            drawn.Step(BladeAt(-0.2f, 0.0f), arm, Vector3.UnitY, FrameSeconds);

            drawn.Step(BladeAt(0.2f, 0.0f), arm, Vector3.UnitY, FrameSeconds);

            var middle = drawn.GetCenterlinePoint(0.5f);
            Assert.That(middle.X, Is.LessThan(-0.03f), "The middle of the blade is held on the side it came from.");
            for (var index = 1; index <= DrawnBlade.SampleCount; index++)
            {
                Assert.That(SegmentMath.FindDistanceToCapsule(drawn.GetSample(index), armCapsule), Is.GreaterThanOrEqualTo(DrawnBlade.BladeRadiusMeters - 1e-4f));
            }
        }

        [Test]
        public void ThePointLetsGoWhenTheBodyMovesAway()
        {
            var bend = PinAt(0.12f);
            var movedWall = new BladeObstacle[] { new Capsule(new Vector3(-2.0f, 1.0f, 1.5f), new Vector3(2.0f, 1.0f, 1.5f), 0.2f) };

            Assert.That(bend.Step(BladeAt(0.0f, 0.12f), movedWall, Vector3.UnitY), Is.EqualTo(PinRelease.BodyMoved));
        }

        [Test]
        public void ThePointStopsOnTheDummysHeadToo()
        {
            // The head is not a Target Zone, but the point cannot pass it either.
            var layout = DummyLayout.Create(1.65f, Hand.Right);
            var body = layout.GetBodyOnStrip(2.0f).Select(part => (BladeObstacle)part).ToList();
            var head = DummyLayout.DummyToStrip(layout.SilhouetteParts[0].Start, 2.0f);
            var bend = new BladeContactBend();
            var approach = new Vector3(0.0f, 0.0f, 1.2f);
            bend.Step(new BladeSegment(head - approach - new Vector3(0.0f, 0.0f, 0.9f), head - approach), body, Vector3.UnitY);

            bend.Step(new BladeSegment(head - new Vector3(0.0f, 0.0f, 0.9f), head), body, Vector3.UnitY);

            Assert.That(bend.IsPinned, Is.True);
            var headCapsule = new Capsule(head, head, layout.SilhouetteParts[0].RadiusMeters);
            Assert.That(SegmentMath.FindDistanceToCapsule(bend.PinnedPoint, headCapsule), Is.EqualTo(BladeContactBend.PointRadiusMeters).Within(1e-3f));
        }

        [Test]
        public void TheDrawnBladeSpringsBackAfterThePointLetsGo()
        {
            var drawn = new DrawnBlade();
            drawn.Step(BladeAt(0.0f, 0.05f), Wall, Vector3.UnitY, FrameSeconds);
            drawn.Step(BladeAt(0.0f, 0.15f), Wall, Vector3.UnitY, FrameSeconds);
            drawn.Step(BladeAt(0.0f, 0.2f), Wall, Vector3.UnitY, FrameSeconds);
            Assert.That(drawn.Contact.IsPinned, Is.True);
            Assert.That(Vector3.Distance(drawn.GetCenterlinePoint(1.0f), drawn.Contact.PinnedPoint), Is.LessThan(1e-5f));

            // A quick pull back out lets go of a well-bowed blade.
            var held = BladeAt(0.0f, 0.05f);
            Assert.That(drawn.Step(held, Wall, Vector3.UnitY, FrameSeconds), Is.EqualTo(PinRelease.CameOut));
            Assert.That(drawn.Flex.Bend.Length(), Is.InRange(0.01f, BladeFlex.MaxBendMeters + 1e-4f), "It starts bent.");

            for (var frame = 0; frame < 72; frame++)
            {
                drawn.Step(held, Wall, Vector3.UnitY, FrameSeconds);
            }

            Assert.That(drawn.Flex.Bend.Length(), Is.LessThan(0.002f));
            Assert.That(Vector3.Distance(drawn.GetCenterlinePoint(1.0f), held.TipPosition), Is.LessThan(0.002f));
        }

        [Test]
        public void ThePressBuzzGrowsWithThePushAndIsFullOncePast()
        {
            Assert.That(BladeHaptics.GetPressStrength(0.0f, false), Is.EqualTo(BladeHaptics.PressFloor));
            Assert.That(BladeHaptics.GetPressStrength(BladeContactBend.MaxPushMeters * 0.5f, false), Is.EqualTo((1.0f + BladeHaptics.PressFloor) * 0.5f).Within(1e-5f));
            Assert.That(BladeHaptics.GetPressStrength(BladeContactBend.MaxPushMeters, false), Is.EqualTo(1.0f).Within(1e-5f));
            Assert.That(BladeHaptics.GetPressStrength(0.0f, true), Is.EqualTo(1.0f));
        }

        private static void AssertOutsideOnTheNearSide(DrawnBlade drawn, string when)
        {
            for (var index = 1; index <= DrawnBlade.SampleCount; index++)
            {
                var sample = drawn.GetSample(index);
                Assert.That(SegmentMath.FindDistanceToCapsule(sample, WallCapsule), Is.GreaterThanOrEqualTo(DrawnBlade.BladeRadiusMeters - 1e-4f), $"{when}: point {index} is in the body");
                Assert.That(sample.Z, Is.LessThan(WallAxisZ), $"{when}: point {index} is on the far side");
            }
        }

        private static BladeContactBend PinAt(float guardZ)
        {
            var bend = new BladeContactBend();
            bend.Step(BladeAt(0.0f, 0.0f), Wall, Vector3.UnitY);
            bend.Step(BladeAt(0.0f, guardZ), Wall, Vector3.UnitY);
            Assert.That(bend.IsPinned, Is.True, "Setup: the point pinned.");
            return bend;
        }

        /// <summary>A blade along +z at height 1 m with its guard at (x, 1, guardZ).</summary>
        private static BladeSegment BladeAt(float x, float guardZ)
        {
            return new BladeSegment(new Vector3(x, 1.0f, guardZ), new Vector3(x, 1.0f, guardZ + BladePose.BladeLengthMeters));
        }

        private static float MeasureDrawnLength(BladeContactBend bend, BladeSegment blade)
        {
            var length = 0.0f;
            for (var piece = 0; piece < 100; piece++)
            {
                length += Vector3.Distance(bend.GetCenterlinePoint(blade, piece / 100.0f), bend.GetCenterlinePoint(blade, (piece + 1) / 100.0f));
            }

            return length;
        }
    }
}
