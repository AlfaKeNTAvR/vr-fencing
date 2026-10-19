// Guards camera rig settings in the passthrough scene that only show up as
// wrong placement in the headset.
using NUnit.Framework;
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
    }
}
