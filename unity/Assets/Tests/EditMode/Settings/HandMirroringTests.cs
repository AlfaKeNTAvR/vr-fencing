// Left-hand mirroring (PER-30): a left-handed Fencer gets every pose mirrored
// across the Fencer's sagittal plane (x is the Fencer's right, y up, z forward).
using System.Numerics;
using NUnit.Framework;
using VrFencing.Settings;

namespace VrFencing.Tests.Settings
{
    public class HandMirroringTests
    {
        private const float Tolerance = 1e-5f;

        // An arbitrary, non-symmetric pose: tip out to the right, rotated about all three axes.
        private static readonly TrackedPose SamplePose = new TrackedPose(
            new Vector3(0.3f, 1.2f, 0.6f),
            Quaternion.CreateFromYawPitchRoll(0.4f, -0.3f, 0.7f));

        [Test]
        public void MirrorPositionFlipsOnlyTheSideToSideAxis()
        {
            var mirrored = HandMirroring.MirrorPosition(new Vector3(0.3f, 1.2f, 0.6f));

            Assert.That(mirrored, Is.EqualTo(new Vector3(-0.3f, 1.2f, 0.6f)));
        }

        [Test]
        public void MirroredRotationTurnsMirroredDirectionsLikeTheOriginal()
        {
            var direction = new Vector3(0.2f, -0.5f, 1.0f);
            var mirroredRotation = HandMirroring.MirrorRotation(SamplePose.Rotation);

            var expected = HandMirroring.MirrorPosition(Vector3.Transform(direction, SamplePose.Rotation));
            var actual = Vector3.Transform(HandMirroring.MirrorPosition(direction), mirroredRotation);

            AssertVectorsClose(actual, expected);
        }

        [Test]
        public void MirroredRotationStaysAProperUnitRotation()
        {
            var mirrored = HandMirroring.MirrorRotation(SamplePose.Rotation);

            Assert.That(mirrored.Length(), Is.EqualTo(1.0f).Within(Tolerance));
        }

        [Test]
        public void MirroringTwiceReturnsTheOriginalPose()
        {
            var twice = HandMirroring.Mirror(HandMirroring.Mirror(SamplePose));

            AssertVectorsClose(twice.Position, SamplePose.Position);
            Assert.That(twice.Rotation, Is.EqualTo(SamplePose.Rotation));
        }

        [Test]
        public void ForRightHandLeavesThePoseUnchanged()
        {
            var result = HandMirroring.ForHand(SamplePose, Hand.Right);

            Assert.That(result.Position, Is.EqualTo(SamplePose.Position));
            Assert.That(result.Rotation, Is.EqualTo(SamplePose.Rotation));
        }

        [Test]
        public void ForLeftHandMirrorsThePose()
        {
            var result = HandMirroring.ForHand(SamplePose, Hand.Left);
            var mirrored = HandMirroring.Mirror(SamplePose);

            Assert.That(result.Position, Is.EqualTo(mirrored.Position));
            Assert.That(result.Rotation, Is.EqualTo(mirrored.Rotation));
            Assert.That(result.Position.X, Is.EqualTo(-0.3f));
        }

        private static void AssertVectorsClose(Vector3 actual, Vector3 expected)
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(Tolerance));
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(Tolerance));
            Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(Tolerance));
        }
    }
}
