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
