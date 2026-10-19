// The GPU memory diagnostics and experiment toggles for the sleep/wake memory growth (PER-93).
using NUnit.Framework;
using VrFencing.Tracking;

namespace VrFencing.Tests.Tracking
{
    public class GpuDiagnosticsTests
    {
        private const long Mebibyte = 1024 * 1024;

        [Test]
        public void EstimatesASingleSampleTexture()
        {
            // 1024 x 1024 RGBA8 with a 4-byte depth buffer: 4 MiB color plus 4 MiB depth.
            var bytes = RenderTextureMemory.EstimateBytes(1024, 1024, 1, 1, 4, 4, false);

            Assert.That(bytes, Is.EqualTo(8 * Mebibyte));
        }

        [Test]
        public void EstimatesMultisampledStereoEyeBuffers()
        {
            // A 2-slice multiview array at 4x MSAA keeps 4 color samples plus a resolve copy, and 4 depth samples.
            var bytes = RenderTextureMemory.EstimateBytes(1024, 1024, 2, 4, 4, 4, false);

            Assert.That(bytes, Is.EqualTo((2 * 4 * 5 + 2 * 4 * 4) * Mebibyte));
        }

        [Test]
        public void AddsAThirdForMipMapsToTheColorOnly()
        {
            var bytes = RenderTextureMemory.EstimateBytes(1024, 1024, 1, 1, 3, 4, true);

            Assert.That(bytes, Is.EqualTo(4 * Mebibyte + 4 * Mebibyte));
        }

        [Test]
        public void EstimatesNothingForAnEmptyTexture()
        {
            Assert.That(RenderTextureMemory.EstimateBytes(0, 0, 0, 0, 4, 4, false), Is.Zero);
        }

        [Test]
        public void RecognizesTheXrEyeTextures()
        {
            Assert.That(RenderTextureMemory.IsXrTexture("XR Texture [7]"), Is.True);
            Assert.That(RenderTextureMemory.IsXrTexture("_CameraTargetAttachmentA"), Is.False);
            Assert.That(RenderTextureMemory.IsXrTexture(null), Is.False);
        }

        [Test]
        public void SumsCountAndBothMemoryFigures()
        {
            var totals = RenderTextureMemory.Sum(new[]
            {
                new RenderTextureEntry("a", 10, 7),
                new RenderTextureEntry("b", 5, 3),
            });

            Assert.That(totals.Count, Is.EqualTo(2));
            Assert.That(totals.EstimatedBytes, Is.EqualTo(15));
            Assert.That(totals.ProfilerBytes, Is.EqualTo(10));
        }

        [Test]
        public void GroupsInstancesOfTheSameNameLargestFirst()
        {
            // A wake that leaks the eye buffers shows as "XR Texture x6" instead of x3.
            var groups = RenderTextureMemory.SummarizeByName(
                new[]
                {
                    new RenderTextureEntry("XR Texture [1]", 100, 0),
                    new RenderTextureEntry("XR Texture [2]", 100, 0),
                    new RenderTextureEntry("_CameraDepthAttachment", 50, 0),
                    new RenderTextureEntry("", 1, 0),
                    new RenderTextureEntry("Small", 2, 0),
                },
                3);

            Assert.That(groups.Count, Is.EqualTo(3));
            Assert.That(groups[0].Name, Is.EqualTo("XR Texture"));
            Assert.That(groups[0].Count, Is.EqualTo(2));
            Assert.That(groups[0].EstimatedBytes, Is.EqualTo(200));
            Assert.That(groups[1].Name, Is.EqualTo("_CameraDepthAttachment"));
            Assert.That(groups[2].Name, Is.EqualTo("Small"));
        }

        [Test]
        public void NamesUnnamedTextures()
        {
            var groups = RenderTextureMemory.SummarizeByName(new[] { new RenderTextureEntry(null, 1, 0) }, 5);

            Assert.That(groups[0].Name, Is.EqualTo("(unnamed)"));
        }

        [Test]
        public void TakesAScheduledSnapshotOnceWhenDue()
        {
            var schedule = new GpuSnapshotSchedule();
            schedule.Schedule("after-resume", 10.0);

            Assert.That(schedule.TryTakeDue(9.9, out _), Is.False);
            Assert.That(schedule.TryTakeDue(10.0, out var moment), Is.True);
            Assert.That(moment, Is.EqualTo("after-resume"));
            Assert.That(schedule.TryTakeDue(11.0, out _), Is.False);
        }

        [Test]
        public void ANewScheduleReplacesThePendingOne()
        {
            var schedule = new GpuSnapshotSchedule();
            schedule.Schedule("launch", 5.0);
            schedule.Schedule("after-resume", 20.0);

            Assert.That(schedule.TryTakeDue(6.0, out _), Is.False);
            Assert.That(schedule.TryTakeDue(20.0, out var moment), Is.True);
            Assert.That(moment, Is.EqualTo("after-resume"));
        }

        [Test]
        public void ACancelledSnapshotIsNeverTaken()
        {
            // A pause before the resume snapshot was due: the frame loop was not running, so it is dropped.
            var schedule = new GpuSnapshotSchedule();
            schedule.Schedule("after-resume", 5.0);
            schedule.Cancel();

            Assert.That(schedule.HasPending, Is.False);
            Assert.That(schedule.TryTakeDue(100.0, out _), Is.False);
        }

        [Test]
        public void FormatsTheGpuSnapshotLine()
        {
            var snapshot = new GpuMemorySnapshot(
                1,
                2,
                1,
                new RenderTextureTotals(14, 812 * Mebibyte, 640 * Mebibyte),
                new RenderTextureTotals(3, 600 * Mebibyte, 500 * Mebibyte),
                700 * Mebibyte,
                900 * Mebibyte,
                1200 * Mebibyte,
                1,
                1,
                1,
                1);

            var line = LifecycleHeartbeat.FormatGpuSnapshot("after-resume", 900, 75.2, snapshot);

            Assert.That(line, Is.EqualTo(
                "[Life] gpu after-resume frame 900 t 75.20 s cams 1 objects 2 enabled 1"
                + " rt 14 est 812.0 MB prof 640.0 MB xrTex 3 est 600.0 MB prof 500.0 MB"
                + " texCur 700.0 MB texTotal 900.0 MB driver 1200.0 MB ptLayers 1 overlays 1 xrDisplays 1 running 1"));
        }

        [Test]
        public void FormatsTheLargestRenderTextureGroups()
        {
            var groups = new[]
            {
                new RenderTextureGroup("XR Texture", 6, 1200 * Mebibyte),
                new RenderTextureGroup("_CameraTargetAttachmentA", 1, 50 * Mebibyte),
            };

            var line = LifecycleHeartbeat.FormatRenderTextureGroups("after-resume", groups);

            Assert.That(line, Is.EqualTo(
                "[Life] gpu after-resume rt-top XR Texture x6 1200.0 MB; _CameraTargetAttachmentA x1 50.0 MB"));
        }

        [Test]
        public void SaysNoneWhenThereAreNoRenderTextures()
        {
            var line = LifecycleHeartbeat.FormatRenderTextureGroups("launch", new RenderTextureGroup[0]);

            Assert.That(line, Is.EqualTo("[Life] gpu launch rt-top none"));
        }

        [Test]
        public void NoExperimentsMeansTodaysBehavior()
        {
            Assert.That(GpuExperiments.None.Describe(), Is.EqualTo("none"));
            Assert.That(GpuExperiments.None.HasResumeSteps, Is.False);
            Assert.That(GpuExperiments.None.UrpMsaaSampleCount, Is.Null);
        }

        [Test]
        public void DescribesTheTogglesThatAreOn()
        {
            var experiments = new GpuExperiments(true, 1, false, true, false);

            Assert.That(experiments.Describe(), Is.EqualTo("passthroughOffAcrossPause urpMsaa 1 recreateUrpPipelineOnResume"));
            Assert.That(experiments.HasResumeSteps, Is.True);
        }

        [Test]
        public void PassthroughAndMsaaAloneHaveNoResumeSteps()
        {
            Assert.That(new GpuExperiments(true, 2, false, false, false).HasResumeSteps, Is.False);
        }

        [TestCase(0, null, true)]
        [TestCase(1, 1, true)]
        [TestCase(2, 2, true)]
        [TestCase(4, 4, true)]
        [TestCase(8, 8, true)]
        [TestCase(3, null, false)]
        [TestCase(-1, null, false)]
        public void ParsesTheUrpMsaaValue(int value, int? expected, bool expectedValid)
        {
            var sampleCount = GpuExperiments.ParseUrpMsaa(value, out var isValid);

            Assert.That(sampleCount, Is.EqualTo(expected));
            Assert.That(isValid, Is.EqualTo(expectedValid));
        }
    }
}
