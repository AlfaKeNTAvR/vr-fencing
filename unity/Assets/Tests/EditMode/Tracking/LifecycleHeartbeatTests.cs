// The once-per-second [Life] heartbeat in the device log (PER-71).
using NUnit.Framework;
using VrFencing.Tracking;

namespace VrFencing.Tests.Tracking
{
    public class LifecycleHeartbeatTests
    {
        [Test]
        public void BeatsOnTheFirstFrameAndThenOncePerInterval()
        {
            var heartbeat = new LifecycleHeartbeat();

            Assert.That(heartbeat.TryBeat(3.0), Is.True);
            Assert.That(heartbeat.TryBeat(3.5), Is.False);
            Assert.That(heartbeat.TryBeat(3.99), Is.False);
            Assert.That(heartbeat.TryBeat(4.0), Is.True);
            Assert.That(heartbeat.TryBeat(4.2), Is.False);
        }

        [Test]
        public void BeatsOnceAfterALongStall()
        {
            // A frozen frame loop shows as a gap, then one beat, not a burst of catch-up beats.
            var heartbeat = new LifecycleHeartbeat();
            heartbeat.TryBeat(1.0);

            Assert.That(heartbeat.TryBeat(30.0), Is.True);
            Assert.That(heartbeat.TryBeat(30.5), Is.False);
        }

        [Test]
        public void FormatsFrameTimeAndRuntimeState()
        {
            var line = LifecycleHeartbeat.Format(1234, 56.789, new RuntimeReadiness(true, false, true, false));

            Assert.That(line, Is.EqualTo("[Life] frame 1234 t 56.79 s vrFocus 1 inputFocus 0 hmd 1 user 0"));
        }
    }
}
