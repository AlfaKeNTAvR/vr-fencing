// When body tracking runs (PER-43): always while the app is active, stopped while paused, unfocused or off the head.
using NUnit.Framework;
using VrFencing.Tracking;

namespace VrFencing.Tests.Tracking
{
    public class BodyTrackingLifecycleTests
    {
        [Test]
        public void FreshLifecycleWantsTrackingRunning()
        {
            var lifecycle = new BodyTrackingLifecycle();

            Assert.That(lifecycle.ShouldRun, Is.True);
            Assert.That(lifecycle.DescribeHolds(), Is.EqualTo("none"));
        }

        [TestCase(LifecycleEvent.AppPaused, "app paused")]
        [TestCase(LifecycleEvent.FocusLost, "input focus lost")]
        [TestCase(LifecycleEvent.HeadsetRemoved, "headset removed")]
        public void EachStopEventStopsRunningTrackingWithItsReason(LifecycleEvent stopEvent, string reason)
        {
            var lifecycle = new BodyTrackingLifecycle();

            var decision = lifecycle.Apply(stopEvent, isRunning: true);

            Assert.That(decision.Action, Is.EqualTo(BodyTrackingAction.Stop));
            Assert.That(decision.Reason, Is.EqualTo(reason));
        }

        [TestCase(LifecycleEvent.AppPaused, LifecycleEvent.AppResumed, "app resumed")]
        [TestCase(LifecycleEvent.FocusLost, LifecycleEvent.FocusRegained, "input focus regained")]
        [TestCase(LifecycleEvent.HeadsetRemoved, LifecycleEvent.HeadsetPutOn, "headset put on")]
        public void MatchingResumeEventRestartsTracking(LifecycleEvent stopEvent, LifecycleEvent resumeEvent, string reason)
        {
            var lifecycle = new BodyTrackingLifecycle();
            lifecycle.Apply(stopEvent, isRunning: true);

            var decision = lifecycle.Apply(resumeEvent, isRunning: false);

            Assert.That(decision.Action, Is.EqualTo(BodyTrackingAction.Start));
            Assert.That(decision.Reason, Is.EqualTo(reason));
        }

        [Test]
        public void RepeatedStopEventIsIdempotent()
        {
            var lifecycle = new BodyTrackingLifecycle();
            lifecycle.Apply(LifecycleEvent.FocusLost, isRunning: true);

            // OnApplicationFocus(false) and OVRManager.InputFocusLost both report the same loss.
            var decision = lifecycle.Apply(LifecycleEvent.FocusLost, isRunning: false);

            Assert.That(decision.Action, Is.EqualTo(BodyTrackingAction.None));
        }

        [Test]
        public void RepeatedResumeEventDoesNotStartTwice()
        {
            var lifecycle = new BodyTrackingLifecycle();
            lifecycle.Apply(LifecycleEvent.AppPaused, isRunning: true);
            lifecycle.Apply(LifecycleEvent.AppResumed, isRunning: false);

            var decision = lifecycle.Apply(LifecycleEvent.AppResumed, isRunning: true);

            Assert.That(decision.Action, Is.EqualTo(BodyTrackingAction.None));
        }

        [Test]
        public void SleepWithFocusAndHeadsetLostRestartsOnlyAfterAllClear()
        {
            // The order a Quest reports going to sleep and waking up can vary; only the last clearing event restarts.
            var lifecycle = new BodyTrackingLifecycle();
            Assert.That(lifecycle.Apply(LifecycleEvent.HeadsetRemoved, isRunning: true).Action, Is.EqualTo(BodyTrackingAction.Stop));
            Assert.That(lifecycle.Apply(LifecycleEvent.FocusLost, isRunning: false).Action, Is.EqualTo(BodyTrackingAction.None));
            Assert.That(lifecycle.Apply(LifecycleEvent.AppPaused, isRunning: false).Action, Is.EqualTo(BodyTrackingAction.None));
            Assert.That(lifecycle.DescribeHolds(), Is.EqualTo("app paused, input focus lost, headset off"));

            Assert.That(lifecycle.Apply(LifecycleEvent.AppResumed, isRunning: false).Action, Is.EqualTo(BodyTrackingAction.None));
            Assert.That(lifecycle.Apply(LifecycleEvent.HeadsetPutOn, isRunning: false).Action, Is.EqualTo(BodyTrackingAction.None));
            Assert.That(lifecycle.DescribeHolds(), Is.EqualTo("input focus lost"));

            var decision = lifecycle.Apply(LifecycleEvent.FocusRegained, isRunning: false);

            Assert.That(decision.Action, Is.EqualTo(BodyTrackingAction.Start));
            Assert.That(lifecycle.ShouldRun, Is.True);
        }

        [Test]
        public void ResumeWithoutPriorStopIsNoOpWhileRunning()
        {
            // Unity sends OnApplicationFocus(true) at launch, after tracking has already started.
            var lifecycle = new BodyTrackingLifecycle();

            var decision = lifecycle.Apply(LifecycleEvent.FocusRegained, isRunning: true);

            Assert.That(decision.Action, Is.EqualTo(BodyTrackingAction.None));
        }

        [Test]
        public void ResumeRetriesStartThatFailedEarlier()
        {
            // Tracking not running although nothing holds it off (for example the permission was not granted yet).
            var lifecycle = new BodyTrackingLifecycle();

            var decision = lifecycle.Apply(LifecycleEvent.AppResumed, isRunning: false);

            Assert.That(decision.Action, Is.EqualTo(BodyTrackingAction.Start));
        }

        [Test]
        public void ResumeEventWhileAnotherHoldRemainsKeepsTrackingStopped()
        {
            var lifecycle = new BodyTrackingLifecycle();
            lifecycle.Apply(LifecycleEvent.HeadsetRemoved, isRunning: true);

            var decision = lifecycle.Apply(LifecycleEvent.FocusRegained, isRunning: false);

            Assert.That(decision.Action, Is.EqualTo(BodyTrackingAction.None));
            Assert.That(lifecycle.ShouldRun, Is.False);
        }
    }
}
