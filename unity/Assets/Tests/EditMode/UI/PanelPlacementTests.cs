// Grabbing, placing and pinning the menu panels (PER-86): follow vs pinned, the pin kept in the Strip frame (so a
// re-placed Strip carries the panels along), saving and restoring it, and the pose math of dragging by the grab bar.
using System;
using System.IO;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Settings;
using VrFencing.UI;

namespace VrFencing.Tests.UI
{
    public class PanelPlacementTests
    {
        private const float Tolerance = 1e-4f;

        private static readonly TrackedPose RoomOrigin = new TrackedPose(Vector3.Zero, Quaternion.Identity);

        // A Strip whose En Garde Line is 2 m to the right and 1 m ahead, running toward -x (turned -90 degrees).
        private static readonly TrackedPose TurnedStrip =
            new TrackedPose(new Vector3(2.0f, 0.0f, 1.0f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, -(float)Math.PI / 2.0f));

        private static readonly TrackedPose PanelAtEyeLevel =
            new TrackedPose(new Vector3(0.5f, 1.4f, 1.2f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.3f));

        private string _directory;
        private string _path;

        [SetUp]
        public void CreateScratchDirectory()
        {
            _directory = Path.Combine(Path.GetTempPath(), "vrfencing-panel-tests-" + Path.GetRandomFileName());
            Directory.CreateDirectory(_directory);
            _path = Path.Combine(_directory, PanelPlacement.FileName);
        }

        [TearDown]
        public void DeleteScratchDirectory()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        [Test]
        public void TheDefaultFollowsTheHead()
        {
            Assert.That(PanelPlacement.Following.IsPinned, Is.False);
            Assert.That(PanelPlacement.Following.GetPoseInRoom(TurnedStrip), Is.Null);
            Assert.That(PanelPlacement.Following.GetPoseInRoom(null), Is.Null);
        }

        [Test]
        public void APinnedPanelStaysExactlyWhereItWasReleased()
        {
            var pinned = PanelPlacement.PinAt(PanelAtEyeLevel, TurnedStrip);

            Assert.That(pinned.IsPinned, Is.True);
            Assert.That(pinned.IsInStripFrame, Is.True);
            AssertSamePose(pinned.GetPoseInRoom(TurnedStrip).Value, PanelAtEyeLevel);
        }

        [Test]
        public void ThePinIsKeptInTheStripFrame()
        {
            var pinned = PanelPlacement.PinAt(PanelAtEyeLevel, TurnedStrip);

            // The Strip runs toward -x in the room, so the panel 1.5 m toward -x is 1.5 m down the Strip, and 0.2 m toward
            // +z in the room is 0.2 m to the Fencer's right.
            Assert.That(pinned.PinnedPose.Position.X, Is.EqualTo(0.2f).Within(Tolerance));
            Assert.That(pinned.PinnedPose.Position.Y, Is.EqualTo(1.4f).Within(Tolerance));
            Assert.That(pinned.PinnedPose.Position.Z, Is.EqualTo(1.5f).Within(Tolerance));
        }

        [Test]
        public void AReplacedStripCarriesThePinnedPanelAlong()
        {
            var pinned = PanelPlacement.PinAt(PanelAtEyeLevel, RoomOrigin);
            var moved = new TrackedPose(new Vector3(3.0f, 0.0f, -2.0f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)Math.PI));

            var pose = pinned.GetPoseInRoom(moved).Value;

            // Turned half way round about the new En Garde Line: x and z flip, height stays.
            Assert.That(pose.Position.X, Is.EqualTo(3.0f - 0.5f).Within(Tolerance));
            Assert.That(pose.Position.Y, Is.EqualTo(1.4f).Within(Tolerance));
            Assert.That(pose.Position.Z, Is.EqualTo(-2.0f - 1.2f).Within(Tolerance));
            var expectedRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)Math.PI) * PanelAtEyeLevel.Rotation;
            Assert.That(Math.Abs(Quaternion.Dot(pose.Rotation, expectedRotation)), Is.EqualTo(1.0f).Within(Tolerance));
        }

        [Test]
        public void APinFollowsTheHeadUntilItsStripIsBack()
        {
            var pinned = PanelPlacement.PinAt(PanelAtEyeLevel, TurnedStrip);

            Assert.That(pinned.GetPoseInRoom(null), Is.Null, "At launch, before the Strip anchor is found, the panels follow.");
            Assert.That(pinned.GetPoseInRoom(TurnedStrip), Is.Not.Null);
        }

        [Test]
        public void APinWithoutAStripStaysInTheRoomThenMovesIntoTheStripFrameInPlace()
        {
            var pinnedInRoom = PanelPlacement.PinAt(PanelAtEyeLevel, null);

            Assert.That(pinnedInRoom.IsInStripFrame, Is.False);
            AssertSamePose(pinnedInRoom.GetPoseInRoom(null).Value, PanelAtEyeLevel);

            var attached = pinnedInRoom.AttachToStrip(TurnedStrip);

            Assert.That(attached.IsInStripFrame, Is.True);
            AssertSamePose(attached.GetPoseInRoom(TurnedStrip).Value, PanelAtEyeLevel);
        }

        [Test]
        public void AttachingLeavesFollowingAndStripPinsAlone()
        {
            var pinned = PanelPlacement.PinAt(PanelAtEyeLevel, TurnedStrip);

            Assert.That(PanelPlacement.Following.AttachToStrip(TurnedStrip), Is.SameAs(PanelPlacement.Following));
            Assert.That(pinned.AttachToStrip(RoomOrigin), Is.SameAs(pinned));
        }

        [Test]
        public void AStripPinSurvivesARestart()
        {
            PanelPlacement.PinAt(PanelAtEyeLevel, TurnedStrip).Save(_path);

            var loaded = PanelPlacement.Load(_path);

            Assert.That(loaded.IsPinned, Is.True);
            Assert.That(loaded.IsInStripFrame, Is.True);
            AssertSamePose(loaded.GetPoseInRoom(TurnedStrip).Value, PanelAtEyeLevel);
        }

        [Test]
        public void FollowingAndRoomPinsAreSavedAsFollowing()
        {
            PanelPlacement.Following.Save(_path);
            Assert.That(PanelPlacement.Load(_path).IsPinned, Is.False);

            PanelPlacement.PinAt(PanelAtEyeLevel, null).Save(_path);
            Assert.That(PanelPlacement.Load(_path).IsPinned, Is.False, "Room coordinates reset every launch.");
        }

        [Test]
        public void NoFileMeansFollowing()
        {
            Assert.That(PanelPlacement.Load(_path), Is.SameAs(PanelPlacement.Following));
        }

        [TestCase("not json")]
        [TestCase("{ \"isPinned\": true, \"positionInStrip\": [1, 2], \"rotationInStrip\": [0, 0, 0, 1] }")]
        [TestCase("{ \"isPinned\": true, \"positionInStrip\": [1, 2, 3], \"rotationInStrip\": [0, 0, 0, 0] }")]
        [TestCase("{ \"isPinned\": true, \"positionInStrip\": [1, 2, 3] }")]
        public void ABrokenFileIsReported(string contents)
        {
            File.WriteAllText(_path, contents);

            Assert.Throws<PanelPlacementFormatException>(() => PanelPlacement.Load(_path));
        }

        [Test]
        public void ADraggedPanelHangsOffTheLaserAsItWasGrabbed()
        {
            // The panel faces a head at the origin from 1 m ahead; the bar is grabbed 0.33 m under its center.
            var head = new Vector3(0.0f, 1.5f, 0.0f);
            var panel = new TrackedPose(new Vector3(0.0f, 1.5f, 1.0f), Quaternion.Identity);
            var controller = new Vector3(-0.2f, 1.0f, 0.3f);
            var grabPoint = new Vector3(0.0f, 1.17f, 1.0f);
            var drag = new PanelDrag(controller, grabPoint, panel);

            // Sweep the laser to point 90 degrees to the right, a long frame so the smoothing settles.
            var pose = drag.Move(controller, new Vector3(1.0f, 0.0f, 0.0f), head, deltaSeconds: 1.0f);

            var grabDistance = Vector3.Distance(controller, grabPoint);
            var expectedGrab = controller + new Vector3(grabDistance, 0.0f, 0.0f);
            AssertClose(drag.GrabPoint, expectedGrab);
            // Still 0.33 m above the grabbed point, the panel's up stays the room's up, and it faces away from the head.
            var grabToPanel = pose.Position - drag.GrabPoint;
            AssertClose(Vector3.Transform(grabToPanel, Quaternion.Inverse(pose.Rotation)), new Vector3(0.0f, 0.33f, 0.0f));
            Assert.That(Vector3.Transform(Vector3.UnitX, pose.Rotation).Y, Is.EqualTo(0.0f).Within(Tolerance), "No roll.");
            var forward = Vector3.Transform(Vector3.UnitZ, pose.Rotation);
            Assert.That(Vector3.Dot(forward, Vector3.Normalize(drag.GrabPoint - head)), Is.EqualTo(1.0f).Within(Tolerance));
        }

        [Test]
        public void DraggingIsSmoothedButCatchesUpWithinAFewFrames()
        {
            var head = new Vector3(0.0f, 1.5f, 0.0f);
            var panel = new TrackedPose(new Vector3(0.0f, 1.5f, 1.0f), Quaternion.Identity);
            var controller = new Vector3(0.0f, 1.2f, 0.4f);
            var drag = new PanelDrag(controller, new Vector3(0.0f, 1.2f, 1.0f), panel);
            var direction = new Vector3(0.3f, 0.0f, 1.0f);

            var onTheLaser = 0.6f * 0.3f / (float)Math.Sqrt(1.09);

            drag.Move(controller, direction, head, deltaSeconds: 1.0f / 72.0f);
            Assert.That(drag.GrabPoint.X, Is.GreaterThan(0.0f).And.LessThan(onTheLaser * 0.6f), "The first frame moves part of the way.");

            for (var frame = 0; frame < 10; frame++)
            {
                drag.Move(controller, direction, head, deltaSeconds: 1.0f / 72.0f);
            }

            Assert.That(drag.GrabPoint.X, Is.EqualTo(onTheLaser).Within(0.01f), "After 11 frames (150 ms) it is nearly there.");
        }

        [Test]
        public void ANotMovedDragKeepsThePanelWhereItWas()
        {
            var head = new Vector3(0.0f, 1.5f, 0.0f);
            var controller = new Vector3(0.0f, 1.17f, 0.4f);
            var grabPoint = new Vector3(0.0f, 1.17f, 1.0f);
            var facing = PanelDrag.FaceHead(grabPoint, head).Value;
            var panel = new TrackedPose(grabPoint + Vector3.Transform(new Vector3(0.0f, 0.33f, 0.0f), facing), facing);
            var drag = new PanelDrag(controller, grabPoint, panel);

            var pose = drag.Move(controller, grabPoint - controller, head, deltaSeconds: 1.0f);

            AssertSamePose(pose, panel);
            Assert.That(drag.Move(controller, Vector3.Zero, head, 1.0f).Position, Is.EqualTo(pose.Position), "A zero laser direction changes nothing.");
        }

        [Test]
        public void FacingTheHeadMatchesUnitysLookRotation()
        {
            var head = new Vector3(0.0f, 1.6f, 0.0f);

            var level = PanelDrag.FaceHead(new Vector3(1.0f, 1.6f, 0.0f), head).Value;
            AssertClose(Vector3.Transform(Vector3.UnitZ, level), Vector3.UnitX);

            var below = PanelDrag.FaceHead(new Vector3(0.0f, 0.6f, 1.0f), head).Value;
            AssertClose(Vector3.Transform(Vector3.UnitZ, below), Vector3.Normalize(new Vector3(0.0f, -1.0f, 1.0f)));
            Assert.That(Vector3.Transform(Vector3.UnitY, below).Y, Is.GreaterThan(0.0f), "Tilted, not upside down.");

            Assert.That(PanelDrag.FaceHead(head, head), Is.Null);
        }

        private static void AssertSamePose(TrackedPose actual, TrackedPose expected)
        {
            AssertClose(actual.Position, expected.Position);
            Assert.That(Math.Abs(Quaternion.Dot(actual.Rotation, expected.Rotation)), Is.EqualTo(1.0f).Within(Tolerance));
        }

        private static void AssertClose(Vector3 actual, Vector3 expected)
        {
            Assert.That(Vector3.Distance(actual, expected), Is.LessThan(1e-3f), $"Expected {expected}, got {actual}.");
        }
    }
}
