// Two-point placement and the Strip frame (PER-31, PER-90): the first press sets the Fencer's En Garde Line, the second the Dummy's,
// which give the Strip's direction and the gap between the lines; room points convert to and from the Strip frame.
using System;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Settings;
using VrFencing.Strip;

namespace VrFencing.Tests.Strip
{
    public class StripPlacementTests
    {
        private const float Tolerance = 1e-4f;

        [Test]
        public void StripFacesFromTheFencersEnGardeLineTowardTheDummys()
        {
            var (enGardeLine, placedLength) = StripFrame.FromPlacementPoints(new Vector3(1.0f, 0.9f, 2.0f), new Vector3(4.0f, 1.1f, 6.0f));

            Assert.That(placedLength, Is.EqualTo(5.0f).Within(Tolerance));
            AssertVector(enGardeLine.Position, new Vector3(1.0f, 0.0f, 2.0f));
            AssertVector(Vector3.Transform(Vector3.UnitZ, enGardeLine.Rotation), new Vector3(0.6f, 0.0f, 0.8f));
            AssertVector(Vector3.Transform(Vector3.UnitY, enGardeLine.Rotation), Vector3.UnitY);
        }

        [Test]
        public void DummysEnGardeLineLiesOnThePositiveZAxisOfTheStripFrame()
        {
            var dummyLine = new Vector3(-3.0f, 1.2f, -1.0f);
            var (enGardeLine, placedLength) = StripFrame.FromPlacementPoints(new Vector3(0.5f, 1.0f, 3.0f), dummyLine);

            var dummyLineInStrip = StripFrame.ToStripFrame(enGardeLine, new Vector3(dummyLine.X, 0.0f, dummyLine.Z));

            AssertVector(dummyLineInStrip, new Vector3(0.0f, 0.0f, placedLength));
        }

        [Test]
        public void PositiveXIsTheFencersRightFacingTheDummy()
        {
            // Facing +z in the room, the right is +x (Unity's convention).
            var (enGardeLine, _) = StripFrame.FromPlacementPoints(Vector3.Zero, new Vector3(0.0f, 0.0f, 3.0f));

            AssertVector(StripFrame.ToStripFrame(enGardeLine, new Vector3(0.5f, 1.7f, 1.0f)), new Vector3(0.5f, 1.7f, 1.0f));

            // Facing -x in the room, the right is +z.
            var (turned, _) = StripFrame.FromPlacementPoints(Vector3.Zero, new Vector3(-3.0f, 0.0f, 0.0f));

            Assert.That(StripFrame.ToStripFrame(turned, new Vector3(0.0f, 0.0f, 0.5f)).X, Is.EqualTo(0.5f).Within(Tolerance));
        }

        [Test]
        public void RoomAndStripFrameConversionsRoundTrip()
        {
            var (enGardeLine, _) = StripFrame.FromPlacementPoints(new Vector3(2.0f, 0.0f, -1.0f), new Vector3(-1.0f, 0.0f, 3.0f));
            var pointInRoom = new Vector3(0.3f, 1.6f, 0.7f);

            AssertVector(StripFrame.ToRoom(enGardeLine, StripFrame.ToStripFrame(enGardeLine, pointInRoom)), pointInRoom);
        }

        [Test]
        public void RoomPoseFacingDownTheStripFacesPlusZInTheStripFrame()
        {
            // The Strip runs toward -x in the room; a pose facing -x faces down the Strip.
            var (enGardeLine, _) = StripFrame.FromPlacementPoints(new Vector3(1.0f, 0.0f, 0.0f), new Vector3(-3.0f, 0.0f, 0.0f));
            var facingMinusX = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2.0f);
            var poseInRoom = new TrackedPose(new Vector3(0.0f, 1.2f, 0.0f), facingMinusX);

            var poseInStrip = StripFrame.ToStripFrame(enGardeLine, poseInRoom);

            AssertVector(poseInStrip.Position, new Vector3(0.0f, 1.2f, 1.0f));
            AssertVector(Vector3.Transform(Vector3.UnitZ, poseInStrip.Rotation), Vector3.UnitZ);
        }

        [Test]
        public void PointsAtTheSameSpotHaveNoDirection()
        {
            Assert.Throws<ArgumentException>(() => StripFrame.FromPlacementPoints(new Vector3(1.0f, 0.5f, 1.0f), new Vector3(1.0f, 1.5f, 1.0f)));
        }

        [Test]
        public void FirstPressSetsTheEnGardeLineAndSecondPlacesTheStrip()
        {
            var placement = new StripPlacement();

            Assert.That(placement.Press(new Vector3(0.0f, 1.0f, 0.0f)), Is.EqualTo(PlacementPressOutcome.EnGardeLineSet));
            Assert.That(placement.Step, Is.EqualTo(PlacementStep.DummyEnGardeLine));
            AssertVector(placement.EnGardePointOnFloor.Value, Vector3.Zero);

            Assert.That(placement.Press(new Vector3(0.0f, 1.0f, 3.5f)), Is.EqualTo(PlacementPressOutcome.Placed));
            Assert.That(placement.Step, Is.EqualTo(PlacementStep.Done));
            Assert.That(placement.Result.Value.PlacedLengthMeters, Is.EqualTo(3.5f).Within(Tolerance));
        }

        [Test]
        public void DummysLineTooCloseForADirectionIsRejectedAndPlacementKeepsWaiting()
        {
            var placement = new StripPlacement();
            placement.Press(Vector3.Zero);

            Assert.That(placement.Press(new Vector3(0.0f, 0.0f, 0.15f)), Is.EqualTo(PlacementPressOutcome.TooClose));
            Assert.That(placement.Step, Is.EqualTo(PlacementStep.DummyEnGardeLine));
            Assert.That(placement.Result, Is.Null);

            Assert.That(placement.Press(new Vector3(0.0f, 0.0f, 1.2f)), Is.EqualTo(PlacementPressOutcome.Placed));
        }

        [Test]
        public void LinesInsideLungeRangeAreAccepted()
        {
            // No minimum gap between the two En Garde Lines (PER-90): a bout may start in lunge range.
            var placement = new StripPlacement();
            placement.Press(Vector3.Zero);

            Assert.That(placement.Press(new Vector3(0.0f, 0.0f, 0.5f)), Is.EqualTo(PlacementPressOutcome.Placed));
            Assert.That(placement.Result.Value.PlacedLengthMeters, Is.EqualTo(0.5f).Within(Tolerance));
        }

        [Test]
        public void ControllerHeightDoesNotCountTowardTheLength()
        {
            var placement = new StripPlacement();
            placement.Press(new Vector3(0.0f, 0.2f, 0.0f));

            // 0.1 m along the floor, 1.5 m up: still too close.
            Assert.That(placement.Press(new Vector3(0.0f, 1.7f, 0.1f)), Is.EqualTo(PlacementPressOutcome.TooClose));
        }

        [Test]
        public void PreviewShowsTheStripOnlyWhileAimingAtAValidDummyLine()
        {
            var placement = new StripPlacement();
            Assert.That(placement.Preview(new Vector3(0.0f, 0.0f, 3.0f)), Is.Null);

            placement.Press(Vector3.Zero);

            Assert.That(placement.Preview(new Vector3(0.0f, 0.0f, 0.1f)), Is.Null);
            Assert.That(placement.Preview(new Vector3(0.0f, 0.0f, 3.0f)).Value.PlacedLengthMeters, Is.EqualTo(3.0f).Within(Tolerance));
        }

        [Test]
        public void PressesAfterPlacementAreIgnored()
        {
            var placement = new StripPlacement();
            placement.Press(Vector3.Zero);
            placement.Press(new Vector3(0.0f, 0.0f, 2.0f));

            Assert.That(placement.Press(new Vector3(0.0f, 0.0f, 5.0f)), Is.EqualTo(PlacementPressOutcome.Ignored));
            Assert.That(placement.Result.Value.PlacedLengthMeters, Is.EqualTo(2.0f).Within(Tolerance));
        }

        private static void AssertVector(Vector3 actual, Vector3 expected)
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(Tolerance), "x");
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(Tolerance), "y");
            Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(Tolerance), "z");
        }
    }
}
