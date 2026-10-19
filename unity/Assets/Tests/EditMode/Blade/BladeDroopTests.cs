// The blade's rest shape (PER-52): the set plus gravity sag drop the point 3 to 8 cm below the forte's line, detection
// uses the line to the real point, the drawn blade leaves the guard along the forte, and a straight push bends the
// blade the way it already hangs.
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Blade;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Tests.Blade
{
    public class BladeDroopTests
    {
        private const float FrameSeconds = 1.0f / 72.0f;
        private const float Set = 0.007f;
        private static readonly Vector3 Down = -Vector3.UnitY;
        private static readonly IReadOnlyList<BladeObstacle> Wall =
            new BladeObstacle[] { new Capsule(new Vector3(-2.0f, 1.0f, 1.2f), new Vector3(2.0f, 1.0f, 1.2f), 0.2f) };

        [Test]
        public void AHorizontalBladeDropsItsPointBySetAndSagWithinTheResearchedRange()
        {
            foreach (var flex in new[] { BladeFlexSoftness.Stiff, BladeFlexSoftness.Normal, BladeFlexSoftness.Soft })
            {
                var blade = new BladeSettings { Flex = flex };
                var droop = BladeDroop.Create(Vector3.UnitZ, Vector3.UnitY, Down, blade);
                var drop = -droop.TipDeflection.Y;

                Assert.That(drop, Is.EqualTo((4.0f * Set) + BladeDroop.GetSagMeters(flex)).Within(1e-5f), flex.ToString());
                Assert.That(drop, Is.InRange(0.03f, 0.08f), $"{flex}: 3 to 8 cm below the forte's line (PER-51).");
            }

            Assert.That(BladeDroop.GetSagMeters(BladeFlexSoftness.Stiff), Is.LessThan(BladeDroop.GetSagMeters(BladeFlexSoftness.Soft)), "A stiffer blade sags less.");
        }

        [Test]
        public void GravitySagFollowsTheElevationWhileTheSetFollowsTheGrip()
        {
            var pointUp = BladeDroop.Create(Vector3.UnitY, -Vector3.UnitZ, Down, Set, 0.03f);
            Assert.That(pointUp.SagDeflection.Length(), Is.LessThan(1e-6f), "Point straight up: no sag.");
            Assert.That(pointUp.SetDeflection.Length(), Is.EqualTo(4.0f * Set).Within(1e-6f), "The set is still there.");

            var elevated = BladeDroop.Create(Vector3.Normalize(new Vector3(0.0f, 0.5f, 0.866f)), Vector3.UnitY, Down, Set, 0.03f);
            Assert.That(elevated.SagDeflection.Length(), Is.EqualTo(0.03f * 0.866f).Within(1e-4f), "30 degrees point up: cosine of the elevation.");

            // Wrist rolled a quarter turn (thumb to the right): the set points left, gravity still down.
            var rolled = BladeDroop.Create(Vector3.UnitZ, Vector3.UnitX, Down, Set, 0.03f);
            Assert.That(rolled.SetDeflection.X, Is.EqualTo(-4.0f * Set).Within(1e-6f));
            Assert.That(rolled.SagDeflection.Y, Is.EqualTo(-0.03f).Within(1e-6f));
        }

        [Test]
        public void TheChordReachesTheRealPointAndKeepsTheBladesLength()
        {
            var forte = new BladeSegment(new Vector3(0.1f, 1.2f, 0.3f), new Vector3(0.1f, 1.2f, 0.3f + BladePose.BladeLengthMeters));
            var droop = BladeDroop.Create(forte.Direction, Vector3.UnitY, Down, new BladeSettings());

            var chord = droop.GetChord(forte);

            Assert.That(Vector3.Distance(chord.GuardPosition, forte.GuardPosition), Is.LessThan(1e-6f));
            Assert.That(Vector3.Distance(chord.GuardPosition, chord.TipPosition), Is.EqualTo(BladePose.BladeLengthMeters).Within(1e-5f));
            Assert.That(chord.TipPosition.Y - forte.TipPosition.Y, Is.EqualTo(droop.TipDeflection.Y).Within(0.002f), "The point hangs by the droop.");
            Assert.That(Vector3.Distance(default(BladeDroop).GetChord(forte).TipPosition, forte.TipPosition), Is.LessThan(1e-6f), "No droop: straight.");
        }

        [Test]
        public void TheRestCurveStartsAndEndsOnTheChordAndLeavesTheGuardAlongTheForte()
        {
            var forte = new BladeSegment(Vector3.Zero, Vector3.UnitZ * BladePose.BladeLengthMeters);
            var droop = BladeDroop.Create(Vector3.UnitZ, Vector3.UnitY, Down, new BladeSettings());
            var chord = droop.GetChord(forte);

            Assert.That(droop.GetChordOffset(0.0f).Length(), Is.LessThan(1e-6f));
            Assert.That(droop.GetChordOffset(1.0f).Length(), Is.LessThan(1e-6f));
            var middle = droop.GetChordOffset(0.5f);
            Assert.That(middle.Y, Is.GreaterThan(0.005f), "The curve sits on the thumb's side of the chord.");
            Assert.That(middle.Y, Is.LessThan(-droop.TipDeflection.Y * 0.3f), "At most about a quarter of the drop.");

            // The first piece of the drawn rest shape runs along the forte, and the hilt direction maps the chord onto it.
            var firstPiece = Vector3.Lerp(chord.GuardPosition, chord.TipPosition, 0.01f) + droop.GetChordOffset(0.01f) - chord.GuardPosition;
            Assert.That(SegmentMath.FindAngleDegrees(firstPiece, Vector3.UnitZ), Is.LessThan(0.3f));
            Assert.That(SegmentMath.FindAngleDegrees(droop.GetHiltDirection(chord.Direction), Vector3.UnitZ), Is.LessThan(0.01f));
        }

        [Test]
        public void TheFreeDrawnBladeHangsInItsRestShapeWithItsPointOnTheChord()
        {
            var forte = new BladeSegment(new Vector3(0.0f, 1.0f, -1.0f), new Vector3(0.0f, 1.0f, -1.0f + BladePose.BladeLengthMeters));
            var droop = BladeDroop.Create(forte.Direction, Vector3.UnitY, Down, new BladeSettings());
            var chord = droop.GetChord(forte);
            var drawn = new DrawnBlade();

            drawn.Step(chord, new BladeObstacle[] { BladeObstacle.Floor }, Vector3.UnitY, FrameSeconds, droop);

            Assert.That(Vector3.Distance(drawn.GetSample(DrawnBlade.SampleCount), chord.TipPosition), Is.LessThan(1e-5f), "The drawn point is the detected point.");
            Assert.That(drawn.GetSample(DrawnBlade.SampleCount).Y, Is.LessThan(1.0f - 0.05f), "It hangs below the forte's line.");
            Assert.That(SegmentMath.FindAngleDegrees(drawn.HiltDirection, forte.Direction), Is.LessThan(0.01f), "The hilt is on the hand's forte.");
        }

        [Test]
        public void AStraightPushBendsTheWayTheBladeHangs()
        {
            // The wrist rolled a quarter turn (thumb to the right), the blade level: the set hangs it left and gravity
            // down, so a straight push bulges down and to the left, not just to the palm side.
            var gripUp = Vector3.UnitX;
            var drawn = new DrawnBlade();
            var touching = 1.0f - BladeContactBend.PointRadiusMeters - BladePose.BladeLengthMeters;
            for (var push = -0.02f; push <= 0.08f + 1e-4f; push += 0.01f)
            {
                var forte = new BladeSegment(new Vector3(0.0f, 1.0f, touching + push), new Vector3(0.0f, 1.0f, touching + push + BladePose.BladeLengthMeters));
                var droop = BladeDroop.Create(forte.Direction, gripUp, Down, new BladeSettings());
                // Lined up so the real point lands straight ahead: the push is straight in along the chord.
                var chord = new BladeSegment(forte.GuardPosition, forte.TipPosition);
                drawn.Step(chord, Wall, gripUp, FrameSeconds, droop);
            }

            Assert.That(drawn.Contact.IsPinned, Is.True, "Setup: pinned on the wall.");
            var bow = drawn.Contact.BowDirection;
            Assert.That(bow.X, Is.LessThan(-0.3f), "Toward the set (the palm side).");
            Assert.That(bow.Y, Is.LessThan(-0.3f), "And toward gravity.");
        }
    }
}
