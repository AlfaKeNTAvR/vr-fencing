// Reading gpu-experiments.json: the stale XR texture cleanup is on unless the file turns it off (PER-93).
using System.IO;
using NUnit.Framework;
using VrFencing.Tracking;

namespace VrFencing.Tests.Tracking
{
    public class GpuExperimentsFileTests
    {
        private string _directory;

        [SetUp]
        public void CreateDirectory()
        {
            _directory = Path.Combine(Path.GetTempPath(), "vr-fencing-gpu-experiments-" + Path.GetRandomFileName());
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void DeleteDirectory()
        {
            Directory.Delete(_directory, true);
        }

        private GpuExperiments LoadWithFile(string json)
        {
            File.WriteAllText(Path.Combine(_directory, GpuExperiments.FileName), json);
            return GpuDiagnosticsReader.LoadExperiments(_directory);
        }

        [Test]
        public void WithoutAFileTheCleanupIsOn()
        {
            Assert.That(GpuDiagnosticsReader.LoadExperiments(_directory).DestroyStaleXrTexturesOnResume, Is.True);
        }

        [Test]
        public void AFileThatLeavesTheKeyOutKeepsTheCleanupOn()
        {
            var experiments = LoadWithFile("{\"urpMsaa\":2}");

            Assert.That(experiments.DestroyStaleXrTexturesOnResume, Is.True);
            Assert.That(experiments.UrpMsaaSampleCount, Is.EqualTo(2));
        }

        [Test]
        public void FalseInTheFileTurnsTheCleanupOff()
        {
            Assert.That(LoadWithFile("{\"destroyStaleXrTexturesOnResume\":false}").DestroyStaleXrTexturesOnResume, Is.False);
        }
    }
}
