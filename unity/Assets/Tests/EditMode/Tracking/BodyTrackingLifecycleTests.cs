// When body tracking runs (PER-43, PER-71): stopped at once while paused, unfocused or off the head; restarted from the
// frame loop only after the runtime has stayed ready for the settle time.
using NUnit.Framework;
using VrFencing.Tracking;

namespace VrFencing.Tests.Tracking
{
    public class BodyTrackingLifecycleTests
    {
        private const double Settle = BodyTrackingLifecycle.DefaultSettleSeconds;

        private static readonly RuntimeReadiness Ready = new RuntimeReadiness(true, true, true, true);
        private static readonly RuntimeReadiness NoInputFocus = new RuntimeReadiness(true, false, true, true);

        /// <summary>A lifecycle that has started tracking at t = <see cref="Settle"/>.</summary>
        private static BodyTrackingLifecycle CreateRunningLifecycle()
        {
            var lifecycle = new BodyTrackingLifecycle();
            lifecycle.Tick(0.0, Ready, isRunning: false);
            Assert.That(lifecycle.Tick(Settle, Ready, isRunning: false).Action, Is.EqualTo(BodyTrackingAction.Start));
            return lifecycle;
        }

        [Test]
        public void SettleTimeIsOneAndAHalfSeconds()
        {
            Assert.That(BodyTrackingLifecycle.DefaultSettleSeconds, Is.EqualTo(1.5));
        }

        [Test]
        public void FreshLifecycleWaitsForTheRuntimeBeforeStarting()
        {
            var lifecycle = new BodyTrackingLifecycle();

            Assert.That(lifecycle.ShouldRun, Is.False);
            Assert.That(lifecycle.DescribeHolds(), Is.EqualTo("runtime state not read yet"));
        }

        [Test]
        public void StartsOnlyAfterTheRuntimeHasBeenReadyForTheSettleTime()
        {
            var lifecycle = new BodyTrackingLifecycle();

            Assert.That(lifecycle.Tick(10.0, Ready, isRunning: false).Action, Is.EqualTo(BodyTrackingAction.None));
            Assert.That(lifecycle.DescribeHolds(), Is.EqualTo("settling (1.5 s)"));
            Assert.That(lifecycle.Tick(10.0 + Settle - 0.01, Ready, isRunning: false).Action, Is.EqualTo(BodyTrackingAction.None));

            var decision = lifecycle.Tick(10.0 + Settle, Ready, isRunning: false);

            Assert.That(decision.Action, Is.EqualTo(BodyTrackingAction.Start));
            Assert.That(decision.Reason, Is.EqualTo("runtime ready for 1.5 s since launch"));
            Assert.That(lifecycle.ShouldRun, Is.True);
            Assert.That(lifecycle.DescribeHolds(), Is.EqualTo("none"));
        }

        [Test]
        public void HonoursACustomSettleTime()
        {
            var lifecycle = new BodyTrackingLifecycle(settleSeconds: 0.5);
            lifecycle.Tick(0.0, Ready, isRunning: false);

            Assert.That(lifecycle.Tick(0.5, Ready, isRunning: false).Action, Is.EqualTo(BodyTrackingAction.Start));
        }

        [TestCase(LifecycleEvent.AppPaused, "app paused")]
        [TestCase(LifecycleEvent.FocusLost, "focus lost")]
        [TestCase(LifecycleEvent.HeadsetRemoved, "headset removed")]
        public void EachLossEventStopsRunningTrackingAtOnce(LifecycleEvent lossEvent, string reason)
        {
            var lifecycle = CreateRunningLifecycle();

            var decision = lifecycle.RecordEvent(lossEvent, isRunning: true);

            Assert.That(decision.Action, Is.EqualTo(BodyTrackingAction.Stop));
            Assert.That(decision.Reason, Is.EqualTo(reason));
            Assert.That(lifecycle.ShouldRun, Is.False);
        }

        [TestCase(LifecycleEvent.AppResumed)]
        [TestCase(LifecycleEvent.FocusRegained)]
        [TestCase(LifecycleEvent.HeadsetPutOn)]
        public void RegainEventNeverStartsTrackingFromTheCallback(LifecycleEvent regainEvent)
        {
            // PER-71: starting inside the wake callbacks, under the lock screen, left a black frozen view.
            var lifecycle = CreateRunningLifecycle();
            lifecycle.RecordEvent(LifecycleEvent.AppPaused, isRunning: true);

            var decision = lifecycle.RecordEvent(regainEvent, isRunning: false);

            Assert.That(decision.Action, Is.EqualTo(BodyTrackingAction.None));
        }

        [Test]
        public void WakeRestartsFromTheFrameLoopAfterTheSettleTime()
        {
            var lifecycle = CreateRunningLifecycle();
            lifecycle.RecordEvent(LifecycleEvent.AppPaused, isRunning: true);
            lifecycle.RecordEvent(LifecycleEvent.AppResumed, isRunning: false);

            Assert.That(lifecycle.Tick(100.0, Ready, isRunning: false).Action, Is.EqualTo(BodyTrackingAction.None));
            var decision = lifecycle.Tick(100.0 + Settle, Ready, isRunning: false);

            Assert.That(decision.Action, Is.EqualTo(BodyTrackingAction.Start));
            Assert.That(decision.Reason, Is.EqualTo("runtime ready for 1.5 s since app resumed"));
        }

        [Test]
        public void PausedAppDoesNotStartEvenWhenTheRuntimeLooksReady()
        {
            var lifecycle = new BodyTrackingLifecycle();
            lifecycle.RecordEvent(LifecycleEvent.AppPaused, isRunning: false);

            lifecycle.Tick(0.0, Ready, isRunning: false);
            var decision = lifecycle.Tick(10.0, Ready, isRunning: false);

            Assert.That(decision.Action, Is.EqualTo(BodyTrackingAction.None));
            Assert.That(lifecycle.DescribeHolds(), Is.EqualTo("app paused"));
        }

        [Test]
        public void ReadinessDropRestartsTheSettleWindow()
        {
            // The lock screen and its interstitial can flicker focus while waking; only an unbroken settle time counts.
            var lifecycle = new BodyTrackingLifecycle();
            lifecycle.Tick(0.0, Ready, isRunning: false);
            lifecycle.Tick(1.0, NoInputFocus, isRunning: false);
            lifecycle.Tick(1.25, Ready, isRunning: false);

            Assert.That(lifecycle.Tick(1.25 + Settle - 0.01, Ready, isRunning: false).Action, Is.EqualTo(BodyTrackingAction.None));
            Assert.That(lifecycle.Tick(1.25 + Settle, Ready, isRunning: false).Action, Is.EqualTo(BodyTrackingAction.Start));
        }

        [Test]
        public void AnyEventRestartsTheSettleWindow()
        {
            var lifecycle = new BodyTrackingLifecycle();
            lifecycle.Tick(0.0, Ready, isRunning: false);
            lifecycle.RecordEvent(LifecycleEvent.FocusRegained, isRunning: false);

            Assert.That(lifecycle.Tick(Settle, Ready, isRunning: false).Action, Is.EqualTo(BodyTrackingAction.None));
            Assert.That(lifecycle.Tick(2 * Settle, Ready, isRunning: false).Action, Is.EqualTo(BodyTrackingAction.Start));
        }

        [TestCase(false, true, true, true, "no vr focus")]
        [TestCase(true, false, true, true, "no input focus")]
        [TestCase(true, true, false, true, "no hmd")]
        [TestCase(true, true, true, false, "headset off")]
        public void RuntimeNotReadyStopsRunningTrackingWithItsReason(
            bool hasVrFocus, bool hasInputFocus, bool isHmdPresent, bool isUserPresent, string hold)
        {
            // A missed callback cannot leave tracking running: the frame loop sees the runtime state directly.
            var lifecycle = CreateRunningLifecycle();

            var decision = lifecycle.Tick(5.0, new RuntimeReadiness(hasVrFocus, hasInputFocus, isHmdPresent, isUserPresent), isRunning: true);

            Assert.That(decision.Action, Is.EqualTo(BodyTrackingAction.Stop));
            Assert.That(decision.Reason, Is.EqualTo($"not ready: {hold}"));
        }

        [Test]
        public void TwoFocusCallbacksCannotDisagree()
        {
            // PER-71: Unity's OnApplicationFocus and OVRManager's input focus events used to write one shared flag, so a
            // late "regained" from one could clear a loss the other still reported. Now the runtime state decides.
            var lifecycle = CreateRunningLifecycle();
            lifecycle.RecordEvent(LifecycleEvent.FocusLost, isRunning: true);
            lifecycle.RecordEvent(LifecycleEvent.FocusLost, isRunning: false);
            lifecycle.RecordEvent(LifecycleEvent.FocusRegained, isRunning: false);

            lifecycle.Tick(20.0, NoInputFocus, isRunning: false);
            var decision = lifecycle.Tick(30.0, NoInputFocus, isRunning: false);

            Assert.That(decision.Action, Is.EqualTo(BodyTrackingAction.None));
            Assert.That(lifecycle.DescribeHolds(), Is.EqualTo("no input focus"));
        }

        [Test]
        public void RepeatedLossEventIsIdempotent()
        {
            var lifecycle = CreateRunningLifecycle();
            lifecycle.RecordEvent(LifecycleEvent.FocusLost, isRunning: true);

            var decision = lifecycle.RecordEvent(LifecycleEvent.FocusLost, isRunning: false);

            Assert.That(decision.Action, Is.EqualTo(BodyTrackingAction.None));
        }

        [Test]
        public void RunningTrackingIsNotStartedAgain()
        {
            var lifecycle = CreateRunningLifecycle();

            var decision = lifecycle.Tick(Settle + 10.0, Ready, isRunning: true);

            Assert.That(decision.Action, Is.EqualTo(BodyTrackingAction.None));
        }

        [Test]
        public void FailedStartIsNotRetriedEveryFrame()
        {
            // Start returned once; the runtime refused (for example no permission yet), so tracking is still not running.
            var lifecycle = CreateRunningLifecycle();

            var decision = lifecycle.Tick(Settle + 0.1, Ready, isRunning: false);

            Assert.That(decision.Action, Is.EqualTo(BodyTrackingAction.None));
            Assert.That(lifecycle.ShouldRun, Is.True);
        }

        [Test]
        public void FailedStartIsRetriedInTheNextSettleWindow()
        {
            var lifecycle = CreateRunningLifecycle();
            lifecycle.RecordEvent(LifecycleEvent.FocusRegained, isRunning: false);

            lifecycle.Tick(50.0, Ready, isRunning: false);
            var decision = lifecycle.Tick(50.0 + Settle, Ready, isRunning: false);

            Assert.That(decision.Action, Is.EqualTo(BodyTrackingAction.Start));
        }

        [Test]
        public void SleepWithEveryHoldRestartsOnlyAfterAllClearAndSettled()
        {
            var lifecycle = CreateRunningLifecycle();
            Assert.That(lifecycle.RecordEvent(LifecycleEvent.HeadsetRemoved, isRunning: true).Action, Is.EqualTo(BodyTrackingAction.Stop));
            lifecycle.RecordEvent(LifecycleEvent.FocusLost, isRunning: false);
            lifecycle.RecordEvent(LifecycleEvent.AppPaused, isRunning: false);
            lifecycle.Tick(10.0, new RuntimeReadiness(false, false, true, false), isRunning: false);
            Assert.That(lifecycle.DescribeHolds(), Is.EqualTo("app paused, no vr focus, no input focus, headset off"));

            lifecycle.RecordEvent(LifecycleEvent.AppResumed, isRunning: false);
            Assert.That(lifecycle.Tick(20.0, NoInputFocus, isRunning: false).Action, Is.EqualTo(BodyTrackingAction.None));
            Assert.That(lifecycle.Tick(21.0, Ready, isRunning: false).Action, Is.EqualTo(BodyTrackingAction.None));

            var decision = lifecycle.Tick(21.0 + Settle, Ready, isRunning: false);

            Assert.That(decision.Action, Is.EqualTo(BodyTrackingAction.Start));
            Assert.That(lifecycle.ShouldRun, Is.True);
        }
    }
}
