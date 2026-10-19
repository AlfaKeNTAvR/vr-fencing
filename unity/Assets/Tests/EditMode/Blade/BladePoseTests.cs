// Where the blade sits on the sword-hand controller: each grip's computed mount (PER-51, PER-52), the Fencer's saved
// angles over it, the guard on the blade's line, and that the left hand mirrors it.
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
        public void DefaultsAreTheCenterGripAtItsComputedAngles()
        {
            var blade = new BladeSettings();

            Assert.That(blade.Mount, Is.EqualTo(BladeMount.Center));
            Assert.That(GripGeometry.GetActiveAngles(blade).PitchDegrees, Is.EqualTo(-10.0f));
            Assert.That(GripGeometry.GetActiveAngles(blade).YawDegrees, Is.EqualTo(0.0f));
            Assert.That(blade.TouchRule, Is.EqualTo(TouchRule.Loose));
        }

        [Test]
        public void AStraightMountPutsTheTipABladeLengthAheadOfTheGuard()
        {
            var settings = new BladeSettings { Mount = BladeMount.Pistol };
            settings.SavedAngles.Pistol.PitchDegrees = 0.0f;
            settings.SavedAngles.Pistol.YawDegrees = 0.0f;

            var blade = BladePose.GetBlade(ControllerAtOrigin, BladePose.GetMountInController(settings, Hand.Right));

            AssertClose(blade.GuardPosition, new NumericsVector3(0.0f, 0.0f, 0.055f));
            AssertClose(blade.TipPosition, new NumericsVector3(0.0f, 0.0f, 0.055f + BladePose.BladeLengthMeters));
        }

        [Test]
        public void TheGuardSitsOnTheBladesLineThroughTheControllersOrigin([Values] BladeMount mount)
        {
            var blade = BladePose.GetBlade(ControllerAtOrigin, BladePose.GetMountInController(new BladeSettings { Mount = mount }, Hand.Right));

            AssertClose(blade.GuardPosition, blade.Direction * GripGeometry.GetGuardForwardMeters(mount));
        }

        [Test]
        public void TheComputedGripsPointTheBladeDownAlongTheForearmAndCantTheFrenchGripInward()
        {
            var center = BladePose.GetBlade(ControllerAtOrigin, BladePose.GetMountInController(new BladeSettings(), Hand.Right));
            Assert.That(center.TipPosition.Y, Is.EqualTo(-BladePose.BladeLengthMeters * Mathf.Sin(10.0f * Mathf.Deg2Rad)).Within(1e-4f), "10 degrees down.");
            Assert.That(center.TipPosition.X, Is.EqualTo(0.0f).Within(1e-5f));

            var french = BladePose.GetBlade(ControllerAtOrigin, BladePose.GetMountInController(new BladeSettings { Mount = BladeMount.French }, Hand.Right));
            Assert.That(french.TipPosition.X, Is.LessThan(-0.05f), "Inward (left for the right hand).");
            Assert.That(french.Direction.Y, Is.EqualTo(-Mathf.Sin(13.0f * Mathf.Deg2Rad)).Within(1e-4f));
        }

        [Test]
        public void SavedAnglesOverrideTheComputedOnesForTheirGripOnly()
        {
            var settings = new BladeSettings { Mount = BladeMount.French };
            settings.SavedAngles.French.PitchDegrees = 10.0f;
            settings.SavedAngles.Pistol.YawDegrees = 20.0f;

            var angles = GripGeometry.GetActiveAngles(settings);

            Assert.That(angles.PitchDegrees, Is.EqualTo(10.0f));
            Assert.That(angles.YawDegrees, Is.EqualTo(-4.0f), "The yaw is still the French grip's computed one.");
        }

        [Test]
        public void LeftHandMirrorsTheYawAndKeepsThePitch([Values] BladeMount mount)
        {
            var settings = new BladeSettings { Mount = mount };
            var right = BladePose.GetBlade(ControllerAtOrigin, BladePose.GetMountInController(settings, Hand.Right));
            var left = BladePose.GetBlade(ControllerAtOrigin, BladePose.GetMountInController(settings, Hand.Left));

            AssertClose(left.TipPosition, new NumericsVector3(-right.TipPosition.X, right.TipPosition.Y, right.TipPosition.Z));
            AssertClose(left.GuardPosition, new NumericsVector3(-right.GuardPosition.X, right.GuardPosition.Y, right.GuardPosition.Z));
        }

        [Test]
        public void AnglesForADirectionUndoTheMount([Values(Hand.Right, Hand.Left)] Hand hand)
        {
            var angles = new GripAngles(-12.5f, 7.0f);
            var mount = BladePose.GetMountInController(BladeMount.Pistol, angles, hand);

            var found = BladePose.GetAnglesForDirection(NumericsVector3.Transform(NumericsVector3.UnitZ, mount.Rotation), hand);

            Assert.That(found.PitchDegrees, Is.EqualTo(angles.PitchDegrees).Within(1e-3f));
            Assert.That(found.YawDegrees, Is.EqualTo(angles.YawDegrees).Within(1e-3f));
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
                var mountInController = BladePose.GetMountInController(BladeMount.FrenchPosted, new GripAngles(15.0f, -20.0f), Hand.Right);
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
