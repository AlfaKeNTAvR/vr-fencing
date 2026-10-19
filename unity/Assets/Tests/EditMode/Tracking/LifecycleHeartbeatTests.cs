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
        public void FormatsFrameTimeRuntimeStateAndEyeBuffer()
        {
            var eyeBuffer = new EyeBufferState(2064, 2208, 1f, 0.75f, 1f, 4);

            var line = LifecycleHeartbeat.Format(1234, 56.789, new RuntimeReadiness(true, false, true, false), eyeBuffer);

            Assert.That(line, Is.EqualTo(
                "[Life] frame 1234 t 56.79 s vrFocus 1 inputFocus 0 hmd 1 user 0"
                + " eye 2064x2208 scale 1.00 viewport 0.75 urp 1.00 msaa 4"));
        }

        [Test]
        public void ShowsAScaledUpEyeBuffer()
        {
            // PER-93: Dynamic Resolution at its Quest 3 maximum makes the eye textures 1.6x per side.
            var eyeBuffer = new EyeBufferState(3302, 3533, 1.6f, 1f, 1.6f, 4);

            var line = LifecycleHeartbeat.Format(1, 0, new RuntimeReadiness(true, true, true, true), eyeBuffer);

            Assert.That(line, Does.EndWith(" eye 3302x3533 scale 1.60 viewport 1.00 urp 1.60 msaa 4"));
        }

        [Test]
        public void SaysUrpNoneWhenNoUrpAssetIsActive()
        {
            var eyeBuffer = new EyeBufferState(0, 0, 1f, 1f, null, null);

            var line = LifecycleHeartbeat.Format(1, 0, new RuntimeReadiness(false, false, false, false), eyeBuffer);

            Assert.That(line, Does.EndWith(" eye 0x0 scale 1.00 viewport 1.00 urp none"));
        }
    }
}
