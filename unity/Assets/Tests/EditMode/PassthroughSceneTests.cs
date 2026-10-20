// Guards camera rig settings in the passthrough scene that only show up as
// wrong placement in the headset.
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VrFencing.Tests
{
    public class PassthroughSceneTests
    {
        private const string PassthroughScenePath = "Assets/Scenes/Passthrough.unity";

        [Test]
        public void CameraRigTracksFromFloorLevel()
        {
            EditorSceneManager.OpenScene(PassthroughScenePath, OpenSceneMode.Single);

            var manager = Object.FindFirstObjectByType<OVRManager>();

            Assert.That(manager, Is.Not.Null, "No OVRManager in the passthrough scene.");
            Assert.That(manager.trackingOriginType, Is.EqualTo(OVRManager.TrackingOrigin.FloorLevel),
                "Eye-level origin puts scene heights relative to the head, not the floor.");
        }

        [Test]
        public void SceneHasTheDummy()
        {
            EditorSceneManager.OpenScene(PassthroughScenePath, OpenSceneMode.Single);

            var dummy = Object.FindFirstObjectByType<VrFencing.Dummy.DummyBehaviour>();

            Assert.That(dummy, Is.Not.Null, "No Dummy in the passthrough scene.");
            Assert.That(dummy.enabled, Is.True);
        }

        [Test]
        public void DummyStandsUnderTheStripsEnGardeLine()
        {
            EditorSceneManager.OpenScene(PassthroughScenePath, OpenSceneMode.Single);

            var strip = Object.FindFirstObjectByType<VrFencing.Strip.StripBehaviour>();
            var dummy = Object.FindFirstObjectByType<VrFencing.Dummy.DummyBehaviour>();

            Assert.That(strip, Is.Not.Null, "No Strip in the passthrough scene.");
            Assert.That(strip.EnGardeLine, Is.Not.Null, "The Strip has no En Garde Line transform.");
            Assert.That(dummy.transform.parent, Is.SameAs(strip.EnGardeLine),
                "The Dummy places itself along its parent's +z, so it must sit under the En Garde Line.");
        }

        [Test]
        public void ArmTrackingFeedsTheSessionRecorder()
        {
            EditorSceneManager.OpenScene(PassthroughScenePath, OpenSceneMode.Single);

            var armTracking = Object.FindFirstObjectByType<VrFencing.Tracking.ArmTrackingBehaviour>();
            var recorder = Object.FindFirstObjectByType<VrFencing.Session.SessionRecorderBehaviour>();
            var runner = Object.FindFirstObjectByType<VrFencing.Drills.DrillRunnerBehaviour>();

            Assert.That(armTracking, Is.Not.Null, "No arm tracking in the passthrough scene.");
            Assert.That(recorder, Is.Not.Null, "No session recorder in the passthrough scene.");
            Assert.That(runner, Is.Not.Null, "No Drill runner in the passthrough scene.");
            var serializedTracking = new SerializedObject(armTracking);
            Assert.That(serializedTracking.FindProperty("_sessionRecorder").objectReferenceValue, Is.SameAs(recorder));
            Assert.That(serializedTracking.FindProperty("_drillRunner").objectReferenceValue, Is.SameAs(runner));
            Assert.That(new SerializedObject(recorder).FindProperty("_drillRunner").objectReferenceValue, Is.SameAs(runner));
        }

        [Test]
        public void FootworkTrackingReportsToThePracticeRunnerInTheStripFrame()
        {
            EditorSceneManager.OpenScene(PassthroughScenePath, OpenSceneMode.Single);

            var footworkTracking = Object.FindFirstObjectByType<VrFencing.Tracking.FootworkTrackingBehaviour>();
            var runner = Object.FindFirstObjectByType<VrFencing.Drills.DrillRunnerBehaviour>();
            var strip = Object.FindFirstObjectByType<VrFencing.Strip.StripBehaviour>();

            Assert.That(footworkTracking, Is.Not.Null, "No footwork tracking in the passthrough scene.");
            Assert.That(footworkTracking.gameObject, Is.SameAs(runner.gameObject), "It lives on the Practice object.");
            var serialized = new SerializedObject(footworkTracking);
            Assert.That(serialized.FindProperty("_drillRunner").objectReferenceValue, Is.SameAs(runner));
            Assert.That(serialized.FindProperty("_strip").objectReferenceValue, Is.SameAs(strip));
        }

        [Test]
        public void OneBodyTrackingOwnerFeedsTheArmTrackingAndTheFencerRecorder()
        {
            EditorSceneManager.OpenScene(PassthroughScenePath, OpenSceneMode.Single);

            var owners = Object.FindObjectsByType<VrFencing.Tracking.BodyTrackingBehaviour>(FindObjectsSortMode.None);
            var armTracking = Object.FindFirstObjectByType<VrFencing.Tracking.ArmTrackingBehaviour>();
            var fencerRecorder = Object.FindFirstObjectByType<VrFencing.Tracking.FencerRecorderBehaviour>();
            var runner = Object.FindFirstObjectByType<VrFencing.Drills.DrillRunnerBehaviour>();

            Assert.That(owners, Has.Length.EqualTo(1), "Exactly one component may start and stop body tracking.");
            Assert.That(owners[0].gameObject, Is.SameAs(runner.gameObject), "It lives on the Practice object.");
            Assert.That(fencerRecorder, Is.Not.Null, "No Fencer recorder in the passthrough scene.");
            Assert.That(new SerializedObject(armTracking).FindProperty("_bodyTracking").objectReferenceValue, Is.SameAs(owners[0]));
            var serializedRecorder = new SerializedObject(fencerRecorder);
            Assert.That(serializedRecorder.FindProperty("_bodyTracking").objectReferenceValue, Is.SameAs(owners[0]));
            Assert.That(serializedRecorder.FindProperty("_strip").objectReferenceValue, Is.SameAs(Object.FindFirstObjectByType<VrFencing.Strip.StripBehaviour>()));
        }

        [Test]
        public void DrillRunnerMovesTheScenesDummy()
        {
            EditorSceneManager.OpenScene(PassthroughScenePath, OpenSceneMode.Single);

            var runner = Object.FindFirstObjectByType<VrFencing.Drills.DrillRunnerBehaviour>();
            var dummy = Object.FindFirstObjectByType<VrFencing.Dummy.DummyBehaviour>();

            Assert.That(runner, Is.Not.Null, "No Drill runner in the passthrough scene.");
            Assert.That(new SerializedObject(runner).FindProperty("_dummy").objectReferenceValue, Is.SameAs(dummy),
                "The runner stands the Dummy at each Drill's Dummy Distance.");
        }

        [Test]
        public void MenuDrivesThePracticeRunnerTheStripAndTheDummy()
        {
            EditorSceneManager.OpenScene(PassthroughScenePath, OpenSceneMode.Single);

            var menu = Object.FindFirstObjectByType<VrFencing.UI.MenuBehaviour>();
            var runners = Object.FindObjectsByType<VrFencing.Drills.DrillRunnerBehaviour>(FindObjectsSortMode.None);

            Assert.That(menu, Is.Not.Null, "No Menu in the passthrough scene.");
            Assert.That(runners, Has.Length.EqualTo(1), "There must be exactly one Drill runner.");
            var serializedMenu = new SerializedObject(menu);
            Assert.That(serializedMenu.FindProperty("_drillRunner").objectReferenceValue, Is.SameAs(runners[0]));
            Assert.That(serializedMenu.FindProperty("_sessionRecorder").objectReferenceValue,
                Is.SameAs(Object.FindFirstObjectByType<VrFencing.Session.SessionRecorderBehaviour>()));
            Assert.That(serializedMenu.FindProperty("_strip").objectReferenceValue, Is.SameAs(Object.FindFirstObjectByType<VrFencing.Strip.StripBehaviour>()));
            Assert.That(serializedMenu.FindProperty("_dummy").objectReferenceValue, Is.SameAs(Object.FindFirstObjectByType<VrFencing.Dummy.DummyBehaviour>()));
        }

        [Test]
        public void BladeReportsToThePracticeRunnerAndTestsTheDummyOnTheStrip()
        {
            EditorSceneManager.OpenScene(PassthroughScenePath, OpenSceneMode.Single);

            var blades = Object.FindObjectsByType<VrFencing.Blade.BladeBehaviour>(FindObjectsSortMode.None);
            var menu = Object.FindFirstObjectByType<VrFencing.UI.MenuBehaviour>();

            Assert.That(blades, Has.Length.EqualTo(1), "There must be exactly one Blade.");
            var serializedBlade = new SerializedObject(blades[0]);
            Assert.That(serializedBlade.FindProperty("_drillRunner").objectReferenceValue, Is.SameAs(Object.FindFirstObjectByType<VrFencing.Drills.DrillRunnerBehaviour>()));
            Assert.That(serializedBlade.FindProperty("_dummy").objectReferenceValue, Is.SameAs(Object.FindFirstObjectByType<VrFencing.Dummy.DummyBehaviour>()));
            Assert.That(serializedBlade.FindProperty("_strip").objectReferenceValue, Is.SameAs(Object.FindFirstObjectByType<VrFencing.Strip.StripBehaviour>()));
            Assert.That(new SerializedObject(menu).FindProperty("_blade").objectReferenceValue, Is.SameAs(blades[0]),
                "The menu hands changed Blade Settings to the Blade.");
        }

        [Test]
        public void MenuDrivesThePartnerSessionOnItsNetworkManager()
        {
            EditorSceneManager.OpenScene(PassthroughScenePath, OpenSceneMode.Single);

            var partners = Object.FindObjectsByType<VrFencing.Partner.PartnerSessionBehaviour>(FindObjectsSortMode.None);
            var menu = Object.FindFirstObjectByType<VrFencing.UI.MenuBehaviour>();

            Assert.That(partners, Has.Length.EqualTo(1), "Exactly one Partner session in the passthrough scene.");
            var network = partners[0].GetComponent<Unity.Netcode.NetworkManager>();
            Assert.That(network, Is.Not.Null);
            Assert.That(network.NetworkConfig.NetworkTransport, Is.SameAs(partners[0].GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>()),
                "Sessions' Relay network needs the NetworkManager's transport to be its UnityTransport.");
            Assert.That(new SerializedObject(menu).FindProperty("_partner").objectReferenceValue, Is.SameAs(partners[0]));
        }

        [Test]
        public void CameraRigAsksForTheBodyTrackingPermission()
        {
            EditorSceneManager.OpenScene(PassthroughScenePath, OpenSceneMode.Single);

            var manager = Object.FindFirstObjectByType<OVRManager>();

            Assert.That(new SerializedObject(manager).FindProperty("requestBodyTrackingPermissionOnStartup").boolValue, Is.True,
                "Without the permission IOBT never starts and the rep log gets no arm pose.");
        }

        [Test]
        public void SceneHasNoGreyTestCube()
        {
            var scene = EditorSceneManager.OpenScene(PassthroughScenePath, OpenSceneMode.Single);

            foreach (var root in scene.GetRootGameObjects())
            {
                Assert.That(root.name, Is.Not.EqualTo("ReferenceCube"), "The setup ticket's test cube is back in the scene.");
            }
        }
    }
}
