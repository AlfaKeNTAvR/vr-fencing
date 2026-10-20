// Smoke test for the test pipeline: proves batchmode Edit-mode runs work and
// guards the one project invariant the APK build relies on.
using System.Linq;
using NUnit.Framework;
using UnityEditor;

namespace VrFencing.Tests
{
    public class BuildSettingsTests
    {
        private const string PassthroughScenePath = "Assets/Scenes/Passthrough.unity";

        [Test]
        public void PassthroughSceneIsTheFirstEnabledBuildScene()
        {
            var enabledScenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).ToArray();

            Assert.That(enabledScenes, Is.Not.Empty, "No enabled scenes in Build Settings.");
            Assert.That(enabledScenes[0].path, Is.EqualTo(PassthroughScenePath));
        }

        [Test]
        public void TheProjectIsLinkedToItsUnityCloudProject()
        {
            // Unity Services (sign-in, Relay, Lobby) run under this project (PER-60, PER-80); QuestProjectSetup keeps it.
            // Read from the Player Settings asset, which the build packs (the player's Application.cloudProjectId):
            // CloudProjectSettings.projectId reads empty in batchmode without a signed-in Editor.
            var playerSettings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            Assert.That(playerSettings.FindProperty("cloudProjectId").stringValue, Is.EqualTo("fc12ae47-8dcf-4350-9d81-f13c0d13dc3c"));
            Assert.That(playerSettings.FindProperty("organizationId").stringValue, Is.EqualTo("2474804760271"));
        }
    }
}
