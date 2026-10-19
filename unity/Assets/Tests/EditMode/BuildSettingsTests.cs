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
    }
}
