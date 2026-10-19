// Where the blade sits on the sword-hand controller (PER-22 feel test 1: Center mount, pitch +10, yaw -10), and that
// the left hand mirrors it.
using NUnit.Framework;
using UnityEngine;
using VrFencing.Blade;
using VrFencing.Settings;
using NumericsQuaternion = System.Numerics.Quaternion;
using NumericsVector3 = System.Numerics.Vector3;

namespace VrFencing.Tests.Blade
{
    public class BladePoseTests
    {
        private static readonly TrackedPose ControllerAtOrigin = new TrackedPose(NumericsVector3.Zero, NumericsQuaternion.Identity);

        [Test]
        public void DefaultsAreTheFeelTestAlignment()
        {
            var blade = new BladeSettings();

            Assert.That(blade.Mount, Is.EqualTo(BladeMount.Center));
            Assert.That(blade.PitchDegrees, Is.EqualTo(10.0f));
            Assert.That(blade.YawDegrees, Is.EqualTo(-10.0f));
            Assert.That(blade.TouchRule, Is.EqualTo(TouchRule.Loose));
        }

        [Test]
        public void StraightMountPutsTheTipABladeLengthAheadOfTheGuard()
        {
            var settings = new BladeSettings { Mount = BladeMount.Pistol, PitchDegrees = 0.0f, YawDegrees = 0.0f };

            var blade = BladePose.GetBlade(ControllerAtOrigin, BladePose.GetMountInController(settings, Hand.Right));

            AssertClose(blade.GuardPosition, new NumericsVector3(0.0f, 0.0f, 0.05f));
            AssertClose(blade.TipPosition, new NumericsVector3(0.0f, 0.0f, 0.05f + BladePose.BladeLengthMeters));
        }

        [Test]
        public void PositivePitchTipsTheBladeUpAndNegativeYawTurnsItLeft()
        {
            var blade = BladePose.GetBlade(ControllerAtOrigin, BladePose.GetMountInController(new BladeSettings(), Hand.Right));

            Assert.That(blade.TipPosition.Y, Is.EqualTo(BladePose.BladeLengthMeters * Mathf.Sin(10.0f * Mathf.Deg2Rad)).Within(1e-4f));
            Assert.That(blade.TipPosition.X, Is.LessThan(-0.1f));
            Assert.That(blade.TipPosition.Z, Is.GreaterThan(0.8f));
        }

        [Test]
        public void LeftHandMirrorsTheYawAndKeepsThePitch()
        {
            var mount = BladePose.GetMountInController(new BladeSettings(), Hand.Right);
            var mirroredMount = BladePose.GetMountInController(new BladeSettings(), Hand.Left);

            var right = BladePose.GetBlade(ControllerAtOrigin, mount);
            var left = BladePose.GetBlade(ControllerAtOrigin, mirroredMount);

            AssertClose(left.TipPosition, new NumericsVector3(-right.TipPosition.X, right.TipPosition.Y, right.TipPosition.Z));
        }

        [Test]
        public void TheBladeMatchesUnitysTransformOfTheSameMount()
        {
            // The Unity layer draws the blade on a child transform of the controller anchor with the mount's local pose;
            // detection uses the Core math. Both must agree.
            var controllerObject = new GameObject("Controller");
            var mountObject = new GameObject("Mount");
            try
            {
                controllerObject.transform.SetPositionAndRotation(new Vector3(0.3f, 1.2f, 0.4f), Quaternion.Euler(20.0f, -35.0f, 15.0f));
                var mountInController = BladePose.GetMountInController(new BladeSettings { Mount = BladeMount.FrenchPosted, PitchDegrees = 15.0f, YawDegrees = -20.0f }, Hand.Right);
                mountObject.transform.SetParent(controllerObject.transform, false);
                mountObject.transform.SetLocalPositionAndRotation(mountInController.Position.ToUnity(), mountInController.Rotation.ToUnity());

                var controller = new TrackedPose(controllerObject.transform.position.ToNumerics(), controllerObject.transform.rotation.ToNumerics());
                var blade = BladePose.GetBlade(controller, mountInController);

                AssertClose(blade.GuardPosition, mountObject.transform.position.ToNumerics());
                AssertClose(blade.TipPosition, mountObject.transform.TransformPoint(0.0f, 0.0f, BladePose.BladeLengthMeters).ToNumerics());
                Assert.That(Quaternion.Angle(mountInController.Rotation.ToUnity(), Quaternion.Euler(-15.0f, -20.0f, 0.0f)), Is.LessThan(0.01f));
            }
            finally
            {
                Object.DestroyImmediate(mountObject);
                Object.DestroyImmediate(controllerObject);
            }
        }

        private static void AssertClose(NumericsVector3 actual, NumericsVector3 expected)
        {
            Assert.That(NumericsVector3.Distance(actual, expected), Is.LessThan(1e-4f), $"Expected {expected}, got {actual}.");
        }
    }
}
