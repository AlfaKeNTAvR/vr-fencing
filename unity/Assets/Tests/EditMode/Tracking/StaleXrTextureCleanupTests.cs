// When the XR eye textures from before a pause are destroyed after the resume (PER-93 workaround).
using NUnit.Framework;
using VrFencing.Tracking;

namespace VrFencing.Tests.Tracking
{
    public class StaleXrTextureCleanupTests
    {
        private const double ResumeSeconds = 100.0;
        private const double Interval = StaleXrTextureCleanup.ScanIntervalSeconds;
        private static readonly int[] OldSet = { 11, 12, 13 };
        private static readonly int[] OldAndNewSets = { 11, 12, 13, 21, 22, 23 };

        private static StaleXrTextureCleanup ArmedAtResume()
        {
            var cleanup = new StaleXrTextureCleanup();
            cleanup.Arm(OldSet, ResumeSeconds);
            return cleanup;
        }

        [Test]
        public void WithoutAnExperimentsFileTheCleanupIsOn()
        {
            Assert.That(StaleXrTextureCleanup.IsOnByDefault, Is.True);
            Assert.That(GpuExperiments.Default.DestroyStaleXrTexturesOnResume, Is.True);
            Assert.That(GpuExperiments.Default.Describe(), Is.EqualTo("destroyStaleXrTexturesOnResume"));
        }

        [Test]
        public void ScansOnlyEveryIntervalWhileArmed()
        {
            var cleanup = new StaleXrTextureCleanup();
            Assert.That(cleanup.IsScanDue(ResumeSeconds + 10), Is.False);

            cleanup.Arm(OldSet, ResumeSeconds);

            Assert.That(cleanup.IsScanDue(ResumeSeconds), Is.False);
            Assert.That(cleanup.IsScanDue(ResumeSeconds + Interval), Is.True);
            cleanup.Evaluate(OldSet, ResumeSeconds + Interval, 10);
            Assert.That(cleanup.IsScanDue(ResumeSeconds + Interval + 0.1), Is.False);
            Assert.That(cleanup.IsScanDue(ResumeSeconds + 2 * Interval), Is.True);
        }

        [Test]
        public void WaitsWhileNoNewSetExists()
        {
            var cleanup = ArmedAtResume();

            var verdict = cleanup.Evaluate(OldSet, ResumeSeconds + Interval, 10);

            Assert.That(verdict.Action, Is.EqualTo(StaleXrTextureAction.Wait));
            Assert.That(verdict.NewCount, Is.Zero);
            Assert.That(cleanup.IsArmed, Is.True);
        }

        [Test]
        public void DestroysTheOldSetOnceTheNewSetIsCompleteAndHasRendered()
        {
            var cleanup = ArmedAtResume();

            var first = cleanup.Evaluate(OldAndNewSets, ResumeSeconds + Interval, 100);
            var second = cleanup.Evaluate(OldAndNewSets, ResumeSeconds + 2 * Interval, 136);

            Assert.That(first.Action, Is.EqualTo(StaleXrTextureAction.Wait));
            Assert.That(second.Action, Is.EqualTo(StaleXrTextureAction.Destroy));
            Assert.That(second.StaleIds, Is.EqualTo(OldSet));
            Assert.That(second.NewCount, Is.EqualTo(3));
            Assert.That(second.CurrentCount, Is.EqualTo(6));
        }

        [Test]
        public void WaitsWhileTheNewSetIsStillGrowing()
        {
            var cleanup = ArmedAtResume();

            cleanup.Evaluate(new[] { 11, 12, 13, 21 }, ResumeSeconds + Interval, 100);
            var growing = cleanup.Evaluate(OldAndNewSets, ResumeSeconds + 2 * Interval, 136);
            var stable = cleanup.Evaluate(OldAndNewSets, ResumeSeconds + 3 * Interval, 172);

            Assert.That(growing.Action, Is.EqualTo(StaleXrTextureAction.Wait));
            Assert.That(stable.Action, Is.EqualTo(StaleXrTextureAction.Destroy));
        }

        [Test]
        public void WaitsUntilTheNewSetHasRenderedAFewFrames()
        {
            var cleanup = ArmedAtResume();
            var firstFrame = 100;

            cleanup.Evaluate(OldAndNewSets, ResumeSeconds + Interval, firstFrame);
            var tooSoon = cleanup.Evaluate(
                OldAndNewSets, ResumeSeconds + 2 * Interval, firstFrame + StaleXrTextureCleanup.MinFramesRenderedByNewSet - 1);
            var enough = cleanup.Evaluate(
                OldAndNewSets, ResumeSeconds + 3 * Interval, firstFrame + StaleXrTextureCleanup.MinFramesRenderedByNewSet);

            Assert.That(tooSoon.Action, Is.EqualTo(StaleXrTextureAction.Wait));
            Assert.That(enough.Action, Is.EqualTo(StaleXrTextureAction.Destroy));
        }

        [Test]
        public void ReportsWhatIsLeftOnTheScanAfterTheDestroyAndStops()
        {
            var cleanup = ArmedAtResume();
            cleanup.Evaluate(OldAndNewSets, ResumeSeconds + Interval, 100);
            cleanup.Evaluate(OldAndNewSets, ResumeSeconds + 2 * Interval, 136);

            var report = cleanup.Evaluate(new[] { 21, 22, 23 }, ResumeSeconds + 3 * Interval, 172);

            Assert.That(report.Action, Is.EqualTo(StaleXrTextureAction.ReportAfterDestroy));
            Assert.That(report.CurrentCount, Is.EqualTo(3));
            Assert.That(report.StaleIds, Is.Empty);
            Assert.That(cleanup.IsArmed, Is.False);
        }

        [Test]
        public void StopsWithoutDestroyingWhenNothingFromBeforeThePauseIsAlive()
        {
            var cleanup = ArmedAtResume();

            var verdict = cleanup.Evaluate(new[] { 21, 22, 23 }, ResumeSeconds + Interval, 100);

            Assert.That(verdict.Action, Is.EqualTo(StaleXrTextureAction.NothingStale));
            Assert.That(cleanup.IsArmed, Is.False);
        }

        [Test]
        public void StopsWithoutDestroyingWhenNothingWasRecordedAtThePause()
        {
            var cleanup = new StaleXrTextureCleanup();
            cleanup.Arm(new int[0], ResumeSeconds);

            var verdict = cleanup.Evaluate(OldSet, ResumeSeconds + Interval, 100);

            Assert.That(verdict.Action, Is.EqualTo(StaleXrTextureAction.NothingStale));
        }

        [Test]
        public void GivesUpAfterTheWatchWindowWhenNoNewSetAppeared()
        {
            var cleanup = ArmedAtResume();

            var verdict = cleanup.Evaluate(OldSet, ResumeSeconds + StaleXrTextureCleanup.WatchWindowSeconds + Interval, 2000);

            Assert.That(verdict.Action, Is.EqualTo(StaleXrTextureAction.Expired));
            Assert.That(verdict.StaleIds, Is.EqualTo(OldSet));
            Assert.That(cleanup.IsArmed, Is.False);
        }

        [Test]
        public void DisarmingStopsTheScans()
        {
            var cleanup = ArmedAtResume();

            cleanup.Disarm();

            Assert.That(cleanup.IsArmed, Is.False);
            Assert.That(cleanup.IsScanDue(ResumeSeconds + 5), Is.False);
        }

        [Test]
        public void RearmingAtTheNextResumeStartsOver()
        {
            var cleanup = ArmedAtResume();
            cleanup.Evaluate(OldAndNewSets, ResumeSeconds + Interval, 100);
            cleanup.Disarm();

            // Paused again before the cleanup: both sets were recorded, and the next resume adds a third.
            cleanup.Arm(OldAndNewSets, ResumeSeconds + 60);
            var first = cleanup.Evaluate(new[] { 11, 12, 13, 21, 22, 23, 31, 32, 33 }, ResumeSeconds + 61, 3000);
            var second = cleanup.Evaluate(new[] { 11, 12, 13, 21, 22, 23, 31, 32, 33 }, ResumeSeconds + 62, 3072);

            Assert.That(first.Action, Is.EqualTo(StaleXrTextureAction.Wait));
            Assert.That(second.Action, Is.EqualTo(StaleXrTextureAction.Destroy));
            Assert.That(second.StaleIds, Is.EqualTo(OldAndNewSets));
        }

        [Test]
        public void TheCleanupIsNotAFiveSecondResumeStep()
        {
            Assert.That(new GpuExperiments(false, null, false, false, true).HasResumeSteps, Is.False);
            Assert.That(new GpuExperiments(false, null, false, false, true).Describe(), Is.EqualTo("destroyStaleXrTexturesOnResume"));
        }
    }
}
