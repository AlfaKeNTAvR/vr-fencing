// The floor as an obstacle (PER-49): the drawn blade never goes below the floor (Strip frame y = 0). The point pins
// on the floor and the blade bows, lags the hand past the most bow, slides along the floor sideways and lets go when
// lifted, under the same rule as the Dummy's body (PER-48). Meters and seconds; the Fencer faces +z.
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Blade;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Tests.Blade
{
    public class BladeFloorTests
    {
        private const float FrameSeconds = 1.0f / 72.0f;

        // Every drawn point keeps the blade's half-width above the floor; a little slack for float error.
        private const float LowestDrawnY = DrawnBlade.BladeRadiusMeters - 1e-4f;

        private static readonly IReadOnlyList<BladeObstacle> FloorOnly = new[] { BladeObstacle.Floor };

        // A thrust down at the floor: 40 degrees below the horizontal, toward +z.
        private static readonly Vector3 DownForward = Vector3.Normalize(new Vector3(0.0f, -0.643f, 0.766f));

        [Test]
        public void TheFloorIsAPlaneAtYZero()
        {
            Assert.That(BladeObstacle.Floor.FindDistance(new Vector3(0.3f, 0.25f, -1.0f)), Is.EqualTo(0.25f).Within(1e-6f));
            Assert.That(BladeObstacle.Floor.FindDistance(new Vector3(0.0f, -0.1f, 2.0f)), Is.EqualTo(-0.1f).Within(1e-6f));
            Assert.That(BladeObstacle.Floor.IsPlane, Is.True);
        }

        [Test]
        public void ThePointPinsOnTheFloorAndTheBladeBowsAsTheHandPushes()
        {
            var bend = new BladeContactBend();
            bend.Step(BladeWithTipAt(new Vector3(0.0f, 0.05f, 1.0f)), FloorOnly, Vector3.UnitY);
            Assert.That(bend.IsPinned, Is.False, "Not on the floor yet.");

            var bows = new List<float>();
            foreach (var depth in new[] { 0.02f, 0.05f, 0.08f, 0.12f })
            {
                var blade = BladeWithTipAt(new Vector3(0.0f, 0.05f, 1.0f) + (DownForward * (0.05f / 0.643f + depth)));
                Assert.That(bend.Step(blade, FloorOnly, Vector3.UnitY), Is.EqualTo(PinRelease.None));
                Assert.That(bend.IsPinned, Is.True);
                Assert.That(bend.PinnedPoint.Y, Is.EqualTo(BladeContactBend.PointRadiusMeters).Within(1e-4f), "The point rests on the floor.");
                Assert.That(bend.IsDecoupled, Is.False);
                bows.Add(bend.BowMeters);
            }

            Assert.That(bows, Is.Ordered.Ascending);
            Assert.That(bows.Last(), Is.GreaterThan(0.05f));
            Assert.That(BladeHaptics.GetPressStrength(bend.PushMeters, bend.IsDecoupled), Is.GreaterThan(BladeHaptics.PressFloor),
                "The press buzz grows with the push.");
        }

        [Test]
        public void ADeepPushIntoTheFloorNeverPutsTheDrawnBladeBelowIt([Values(1.0f, -1.0f)] float bowUp, [Values(40.0f, 80.0f)] float degreesDown)
        {
            // The hand drives on until its guard is 60 cm past where the point met the floor, then comes back up;
            // with the grip turned over (bow down) the bow itself would dig into the floor. At 40 degrees the point skids
            // along the floor (PER-69); nearly straight down it holds, and past the most bow the drawn blade lags.
            var radians = degreesDown * (float)System.Math.PI / 180.0f;
            var thrust = new Vector3(0.0f, -(float)System.Math.Sin(radians), (float)System.Math.Cos(radians));
            var drawn = new DrawnBlade();
            var touchdown = new Vector3(0.0f, 0.0f, 1.0f);
            var bowDirection = Vector3.UnitY * bowUp;
            var depths = Enumerable.Range(-5, 66).Select(step => step * 0.01f).ToList();
            depths.AddRange(Enumerable.Range(0, 66).Select(step => 0.60f - (step * 0.01f)));
            var wasDecoupled = false;
            foreach (var depth in depths)
            {
                var tip = touchdown + (thrust * depth);
                drawn.Step(new BladeSegment(tip - (thrust * BladePose.BladeLengthMeters), tip), FloorOnly, bowDirection, FrameSeconds);
                AssertAboveTheFloor(drawn, $"depth {depth:0.00}");
                wasDecoupled |= drawn.IsDecoupled;
            }

            if (degreesDown > 60.0f)
            {
                Assert.That(wasDecoupled, Is.True, "Past 20 cm of push the drawn blade lags the hand.");
            }

            Assert.That(drawn.IsDecoupled, Is.False);
            Assert.That(drawn.Contact.IsPinned, Is.False, "Lifted, the point let go.");
        }

        [Test]
        public void AFastOneFrameThrustIntoTheFloorIsCaught()
        {
            // From 10 cm above the floor to 50 cm below it in one 72 Hz frame.
            var drawn = new DrawnBlade();
            var above = new Vector3(0.0f, 0.10f, 1.0f);
            drawn.Step(BladeWithTipAt(above), FloorOnly, Vector3.UnitY, FrameSeconds);

            var below = above + (DownForward * (0.60f / 0.643f));
            drawn.Step(BladeWithTipAt(below), FloorOnly, Vector3.UnitY, FrameSeconds);

            Assert.That(drawn.Contact.IsPinned, Is.True);
            Assert.That(drawn.Contact.PinnedPoint.Y, Is.EqualTo(BladeContactBend.PointRadiusMeters).Within(1e-4f));
            var expectedZ = above.Z + ((0.10f - BladeContactBend.PointRadiusMeters) / 0.643f * 0.766f);
            Assert.That(drawn.Contact.PinnedPoint.Z, Is.EqualTo(expectedZ).Within(1e-3f), "Pinned where the path first met the floor.");
            AssertAboveTheFloor(drawn, "after the thrust");
        }

        [Test]
        public void MovingTheHandSidewaysSlidesThePointAlongTheFloorAndLiftingLetsGo()
        {
            var drawn = new DrawnBlade();
            var touchdown = new Vector3(0.0f, 0.0f, 1.0f);
            drawn.Step(BladeWithTipAt(touchdown - (DownForward * 0.05f)), FloorOnly, Vector3.UnitY, FrameSeconds);
            drawn.Step(BladeWithTipAt(touchdown + (DownForward * 0.08f)), FloorOnly, Vector3.UnitY, FrameSeconds);
            Assert.That(drawn.Contact.IsPinned, Is.True);

            for (var x = 0.01f; x <= 0.80f; x += 0.01f)
            {
                var release = drawn.Step(BladeWithTipAt(touchdown + (DownForward * 0.08f) + new Vector3(x, 0.0f, 0.0f)), FloorOnly, Vector3.UnitY, FrameSeconds);
                Assert.That(release, Is.EqualTo(PinRelease.None), $"Still pressed into the floor at x {x:0.00}.");
                Assert.That(drawn.Contact.PinnedPoint.Y, Is.EqualTo(BladeContactBend.PointRadiusMeters).Within(1e-4f), $"On the floor at x {x:0.00}.");
                AssertAboveTheFloor(drawn, $"x {x:0.00}");
            }

            Assert.That(drawn.Contact.PinnedPoint.X, Is.GreaterThan(0.4f), "The point slid along the floor with the hand.");

            var lifted = drawn.Step(BladeWithTipAt(touchdown + new Vector3(0.80f, 0.10f, 0.0f)), FloorOnly, Vector3.UnitY, FrameSeconds);
            Assert.That(lifted, Is.EqualTo(PinRelease.CameOut));
            Assert.That(drawn.Contact.IsPinned, Is.False);
        }

        [Test]
        public void ThePointStillPinsOnTheDummyWithTheFloorThere()
        {
            // A body above the floor: the floor joins the obstacles without changing where the point stops on the body.
            var wall = new Capsule(new Vector3(-2.0f, 1.0f, 1.2f), new Vector3(2.0f, 1.0f, 1.2f), 0.2f);
            var obstacles = new[] { (BladeObstacle)wall, BladeObstacle.Floor };
            var bend = new BladeContactBend();
            bend.Step(new BladeSegment(new Vector3(0.0f, 1.0f, 0.0f), new Vector3(0.0f, 1.0f, 0.9f)), obstacles, Vector3.UnitY);

            bend.Step(new BladeSegment(new Vector3(0.0f, 1.0f, 0.2f), new Vector3(0.0f, 1.0f, 1.1f)), obstacles, Vector3.UnitY);

            Assert.That(bend.IsPinned, Is.True);
            Assert.That(bend.PinnedPoint.Z, Is.EqualTo(1.0f - BladeContactBend.PointRadiusMeters).Within(1e-4f));
        }

        [Test]
        public void ThrustingIntoTheCornerOfTheFloorAndTheDummysFootKeepsTheBladeAboveTheFloorAndOutOfTheFoot()
        {
            // The front foot's capsule dips into the floor; the point is driven down into the corner in front of its
            // toe, then swept sideways across under it and lifted.
            var layout = DummyLayout.Create(1.65f, Hand.Right);
            var body = layout.GetBodyOnStrip(2.0f);
            var foot = layout.GetZonesOnStrip(2.0f).First(zone => zone.Zone == TargetZone.Foot).Shape;
            var obstacles = body.Select(part => (BladeObstacle)part).Append(BladeObstacle.Floor).ToList();
            var toe = foot.Start.Z < foot.End.Z ? foot.Start : foot.End;
            var corner = new Vector3(toe.X, 0.0f, toe.Z - foot.RadiusMeters);

            var drawn = new DrawnBlade();
            var tips = Enumerable.Range(-10, 51).Select(step => corner + (DownForward * (step * 0.01f))).ToList();
            var deepest = tips.Last();
            tips.AddRange(Enumerable.Range(1, 40).Select(step => deepest + new Vector3(step * 0.01f, 0.0f, 0.0f)));
            tips.AddRange(Enumerable.Range(1, 60).Select(step => tips.Last() - (DownForward * (step * 0.01f))));
            foreach (var tip in tips)
            {
                drawn.Step(BladeWithTipAt(tip), obstacles, Vector3.UnitY, FrameSeconds);
                var when = $"tip {tip.X:0.00}, {tip.Y:0.00}, {tip.Z:0.00}";
                AssertAboveTheFloor(drawn, when);
                for (var index = 1; index <= DrawnBlade.SampleCount; index++)
                {
                    Assert.That(SegmentMath.FindDistanceToCapsule(drawn.GetSample(index), foot), Is.GreaterThan(0.0f), $"{when}: point {index} is in the foot");
                }
            }

            Assert.That(drawn.Contact.IsPinned, Is.False, "Lifted out of the corner, the point let go.");
        }

        private static void AssertAboveTheFloor(DrawnBlade drawn, string when)
        {
            for (var index = 0; index <= DrawnBlade.SampleCount; index++)
            {
                Assert.That(drawn.GetSample(index).Y, Is.GreaterThanOrEqualTo(index == 0 ? 0.0f : LowestDrawnY), $"{when}: point {index} is below the floor");
            }
        }

        /// <summary>A blade thrust down at the floor (<see cref="DownForward"/>) with its point at <paramref name="tip"/>.</summary>
        private static BladeSegment BladeWithTipAt(Vector3 tip)
        {
            return new BladeSegment(tip - (DownForward * BladePose.BladeLengthMeters), tip);
        }
    }
}
