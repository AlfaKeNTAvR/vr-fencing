// Side hits (Nikita's headset test after PER-48): the drawn blade folded into a zig-zag and ran inside the Dummy's arm.
// Now the drawn blade is always one smooth curve outside every body; past the most it can bend, front or side, the
// drawn hilt lags the hand (ghost hilt) instead of folding, and it rings out when it lets go. Meters and seconds; the
// Fencer faces +z.
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Blade;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Tests.Blade
{
    public class BladeSideHitTests
    {
        private const float FrameSeconds = 1.0f / 72.0f;

        // The sharpest the drawn centerline may turn between neighbouring pieces: the curvature cap over one 3.75 cm piece
        // is about 15.5 degrees; a fold is near 180.
        private const float MaxTurnDegrees = 20.0f;

        private const float WallAxisZ = 1.2f;
        private static readonly Capsule WallCapsule = new Capsule(new Vector3(-2.0f, 1.0f, WallAxisZ), new Vector3(2.0f, 1.0f, WallAxisZ), 0.2f);
        private static readonly IReadOnlyList<BladeObstacle> Wall = new BladeObstacle[] { WallCapsule };
        private const float PinnedZ = 1.0f - BladeContactBend.PointRadiusMeters;

        [Test]
        public void TheCurvatureLimitIsTheBowOfTheDeepestStraightPush()
        {
            var shape = default(ElasticaShape);
            Assert.That(BladeArc.TrySolve(BladePose.BladeLengthMeters - BladeContactBend.MaxPushMeters, 0.0f, ref shape), Is.True);
            Assert.That(BladeArc.FindMaxCurvature(shape), Is.EqualTo(BladeArc.MaxCurvaturePerMeter).Within(BladeArc.MaxCurvaturePerMeter * 0.01f));
            Assert.That(1.0f / BladeArc.MaxCurvaturePerMeter, Is.InRange(0.15f, 0.30f), "The tightest bend has a 15-30 cm radius.");
        }

        [Test]
        public void ASideHitOnTheChestThenAHandSweptAcrossNeverFoldsOrEntersTheBody([Values(-1.0f, 1.0f)] float sweepSide)
        {
            // The blade comes in from the Fencer's right, 35 degrees off the Strip, and pins on the chest; then the hand
            // sweeps 60 cm sideways (either way) keeping the blade's direction.
            var layout = DummyLayout.Create(1.65f, Hand.Right);
            var capsules = layout.GetBodyOnStrip(2.0f);
            var bodies = capsules.Select(part => (BladeObstacle)part).Append(BladeObstacle.Floor).ToList();
            var chest = layout.GetZonesOnStrip(2.0f).First(zone => zone.Zone == TargetZone.Body).Shape;
            var aim = chest.Center;
            var direction = Vector3.Normalize(new Vector3(-0.574f, 0.0f, 0.819f));
            var start = aim - (direction * (chest.RadiusMeters + 0.10f));

            var drawn = new DrawnBlade();
            var pinned = false;
            var tipAtPin = start;
            for (var step = 0; step <= 20 && !pinned; step++)
            {
                tipAtPin = start + (direction * (step * 0.01f));
                drawn.Step(BladeWithTipAt(tipAtPin, direction), bodies, Vector3.UnitY, FrameSeconds);
                pinned = drawn.Contact.IsPinned;
            }

            Assert.That(pinned, Is.True, "Setup: the point pinned on the chest.");

            // Pressed 5 cm on, then swept.
            var pinnedTip = tipAtPin + (direction * 0.05f);
            var wasDecoupled = false;
            for (var step = 1; step <= 60; step++)
            {
                var tip = pinnedTip + new Vector3(sweepSide * step * 0.01f, 0.0f, 0.0f);
                drawn.Step(BladeWithTipAt(tip, direction), bodies, Vector3.UnitY, FrameSeconds);
                var when = $"{step} cm across";
                AssertSmoothAndOutside(drawn, capsules, when);
                wasDecoupled |= drawn.IsDecoupled;
            }

            TestContext.WriteLine($"Pinned at the end: {drawn.Contact.IsPinned}, lagged the hand at some point: {wasDecoupled}");
        }

        [Test]
        public void AFlatSweptIntoTheArmAndPushedOnStaysSmoothOutsideAndLagsTheHand()
        {
            // The blade pointing forward beside the Dummy's sword arm, its middle level with the forearm's, swept
            // sideways into the forearm and on 30 cm through it, then back.
            var layout = DummyLayout.Create(1.65f, Hand.Right);
            var capsules = layout.GetBodyOnStrip(2.0f);
            var bodies = capsules.Select(part => (BladeObstacle)part).ToList();
            var forearm = layout.GetZonesOnStrip(2.0f).First(zone => zone.Zone == TargetZone.Forearm).Shape;
            var middle = forearm.Center;
            var offsets = Enumerable.Range(-15, 46).Select(step => step * 0.01f).ToList();
            offsets.AddRange(Enumerable.Range(1, 45).Select(step => 0.30f - (step * 0.01f)));

            var drawn = new DrawnBlade();
            var wasDecoupled = false;
            foreach (var offset in offsets)
            {
                var guard = new Vector3(middle.X - forearm.RadiusMeters + offset, middle.Y, middle.Z - (BladePose.BladeLengthMeters * 0.5f));
                drawn.Step(new BladeSegment(guard, guard + (Vector3.UnitZ * BladePose.BladeLengthMeters)), bodies, Vector3.UnitY, FrameSeconds);
                AssertSmoothAndOutside(drawn, capsules, $"offset {offset:0.00}");
                wasDecoupled |= drawn.IsDecoupled;
                if (offset > 0.05f && !drawn.Contact.IsPinned)
                {
                    Assert.That(drawn.GetCenterlinePoint(0.5f).X, Is.LessThan(forearm.Center.X), $"offset {offset:0.00}: the blade is held on the near side");
                }
            }

            Assert.That(wasDecoupled, Is.True, "Pushed through the arm, the drawn blade lagged the hand.");
            Assert.That(drawn.IsDecoupled, Is.False, "Back out, the drawn blade follows the hand again.");
        }

        [Test]
        public void ATurnedBladeBendsInOneSmoothCurveAndPastTheMostBendLagsInsteadOfFolding()
        {
            // Pinned on the wall with the hand turned 19 degrees off the line to the point (inside the friction angle,
            // so the point holds).
            var drawn = new DrawnBlade();
            drawn.Step(BladeAt(0.0f, 0.0f), Wall, Vector3.UnitY, FrameSeconds);
            drawn.Step(BladeAt(0.0f, 0.12f), Wall, Vector3.UnitY, FrameSeconds);
            Assert.That(drawn.Contact.IsPinned, Is.True);
            var pin = drawn.Contact.PinnedPoint;

            // 10 cm of push: within reach, one smooth curve from the hand.
            var light = Turned(pin, 0.10f, 19.0f);
            drawn.Step(light, Wall, Vector3.UnitY, FrameSeconds);
            Assert.That(drawn.IsDecoupled, Is.False);
            Assert.That(drawn.GuardPosition, Is.EqualTo(light.GuardPosition));
            AssertOneSmoothCurveOnThePin(drawn, pin, "10 cm");

            // 25 cm: more than the blade can bend, so the drawn hilt lags the hand, the same rule as a straight push.
            var deep = Turned(pin, 0.25f, 19.0f);
            drawn.Step(deep, Wall, Vector3.UnitY, FrameSeconds);
            Assert.That(drawn.Contact.IsPinned, Is.True);
            Assert.That(drawn.Contact.PinnedPoint, Is.EqualTo(pin), "The point held.");
            Assert.That(drawn.IsDecoupled, Is.True, "Past the most bend the drawn hilt lags the hand.");
            AssertOneSmoothCurveOnThePin(drawn, pin, "25 cm");

            // Back to a light push: the drawn blade is back in the hand.
            drawn.Step(light, Wall, Vector3.UnitY, FrameSeconds);
            Assert.That(drawn.IsDecoupled, Is.False);
            Assert.That(drawn.GuardPosition, Is.EqualTo(light.GuardPosition));
        }

        [Test]
        public void WhenThePointLetsGoTheBladeRingsAFewDecayingCycles()
        {
            var drawn = new DrawnBlade();
            drawn.Step(BladeAt(0.0f, 0.05f), Wall, Vector3.UnitY, FrameSeconds);
            drawn.Step(BladeAt(0.0f, 0.15f), Wall, Vector3.UnitY, FrameSeconds);
            drawn.Step(BladeAt(0.0f, 0.2f), Wall, Vector3.UnitY, FrameSeconds);
            var held = BladeAt(0.0f, 0.05f);
            Assert.That(drawn.Step(held, Wall, Vector3.UnitY, FrameSeconds), Is.EqualTo(PinRelease.CameOut));
            var startBend = drawn.Flex.Bend;
            Assert.That(drawn.Flex.IsRinging, Is.True);

            var crossings = 0;
            var lastSign = 1.0f;
            var peaks = new List<float>();
            var peak = 0.0f;
            for (var frame = 0; frame < 72; frame++)
            {
                drawn.Step(held, Wall, Vector3.UnitY, FrameSeconds);
                var along = Vector3.Dot(drawn.Flex.Bend, Vector3.Normalize(startBend));
                if (System.Math.Sign(along) != 0 && System.Math.Sign(along) != System.Math.Sign(lastSign))
                {
                    crossings++;
                    peaks.Add(peak);
                    peak = 0.0f;
                    lastSign = along;
                }

                peak = System.Math.Max(peak, System.Math.Abs(along));
            }

            Assert.That(crossings, Is.GreaterThanOrEqualTo(4), "A few cycles, not a single overshoot.");
            Assert.That(peaks.Skip(1), Is.Ordered.Descending, "Each swing smaller than the last.");
            Assert.That(drawn.Flex.Bend.Length(), Is.LessThan(0.002f), "Still within a second.");
            Assert.That(drawn.Flex.IsRinging, Is.False);
        }

        [Test]
        public void AHeldBladeStaysStillAfterRingingOut()
        {
            // Tracking jitter of a few millimeters on a still hand: no ringing is started by it.
            var drawn = new DrawnBlade();
            var random = new System.Random(7);
            for (var frame = 0; frame < 144; frame++)
            {
                var jitter = new Vector3((float)(random.NextDouble() - 0.5), (float)(random.NextDouble() - 0.5), (float)(random.NextDouble() - 0.5)) * 0.007f;
                drawn.Step(new BladeSegment(new Vector3(0.0f, 1.0f, 0.0f) + jitter, new Vector3(0.0f, 1.0f, 0.9f) + jitter), Wall, Vector3.UnitY, FrameSeconds);
                Assert.That(drawn.Flex.IsRinging, Is.False);
            }

            Assert.That(drawn.Flex.Bend.Length(), Is.LessThan(0.001f));
        }

        private static void AssertOneSmoothCurveOnThePin(DrawnBlade drawn, Vector3 pin, string when)
        {
            Assert.That(DrawnShape.FindSharpestTurnDegrees(Samples(drawn)), Is.LessThan(MaxTurnDegrees), $"{when}: the drawn blade folds");
            Assert.That(MeasureLength(drawn), Is.EqualTo(BladePose.BladeLengthMeters).Within(0.01f), $"{when}: full length");
            Assert.That(Vector3.Distance(drawn.GetCenterlinePoint(1.0f), pin), Is.LessThan(1e-4f), $"{when}: the point stays on the pin");
            for (var index = 1; index <= DrawnBlade.SampleCount; index++)
            {
                Assert.That(drawn.GetSample(index).Z, Is.LessThan(1.0f), $"{when}: point {index} is in the wall");
            }
        }

        private static void AssertSmoothAndOutside(DrawnBlade drawn, IReadOnlyList<Capsule> capsules, string when)
        {
            Assert.That(DrawnShape.FindSharpestTurnDegrees(Samples(drawn)), Is.LessThan(MaxTurnDegrees), $"{when}: the drawn blade folds");
            for (var index = 1; index <= DrawnBlade.SampleCount; index++)
            {
                foreach (var capsule in capsules)
                {
                    Assert.That(SegmentMath.FindDistanceToCapsule(drawn.GetSample(index), capsule), Is.GreaterThan(0.0f), $"{when}: point {index} is inside the body");
                }
            }
        }

        private static List<Vector3> Samples(DrawnBlade drawn)
        {
            return Enumerable.Range(0, DrawnBlade.SampleCount + 1).Select(drawn.GetSample).ToList();
        }

        private static float MeasureLength(DrawnBlade drawn)
        {
            var samples = Samples(drawn);
            return Enumerable.Range(1, samples.Count - 1).Sum(index => Vector3.Distance(samples[index - 1], samples[index]));
        }

        /// <summary>The hand's blade with its guard <paramref name="pushMeters"/> closer than a blade's length to the pin, turned about the vertical.</summary>
        private static BladeSegment Turned(Vector3 pin, float pushMeters, float degrees)
        {
            var guard = pin - (Vector3.UnitZ * (BladePose.BladeLengthMeters - pushMeters));
            var radians = degrees * (float)System.Math.PI / 180.0f;
            var direction = new Vector3((float)System.Math.Sin(radians), 0.0f, (float)System.Math.Cos(radians));
            return new BladeSegment(guard, guard + (direction * BladePose.BladeLengthMeters));
        }

        private static BladeSegment BladeAt(float x, float guardZ)
        {
            return new BladeSegment(new Vector3(x, 1.0f, guardZ), new Vector3(x, 1.0f, guardZ + BladePose.BladeLengthMeters));
        }

        private static BladeSegment BladeWithTipAt(Vector3 tip, Vector3 direction)
        {
            return new BladeSegment(tip - (direction * BladePose.BladeLengthMeters), tip);
        }
    }
}
