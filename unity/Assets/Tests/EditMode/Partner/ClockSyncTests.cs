// The bout clock (PER-82): the other app's monotonic clock from ping round trips, trusting only the quickest of the
// recent ones, so a slow or lopsided trip never moves it.
using NUnit.Framework;
using VrFencing.Partner;

namespace VrFencing.Tests.Partner
{
    public class ClockSyncTests
    {
        private const double Tolerance = 1e-9;

        // The other clock reads this much more than this one.
        private const double TrueOffset = 1234.5;

        [Test]
        public void ASymmetricRoundTripGivesTheOffsetExactly()
        {
            var clock = new ClockSync();

            // Sent at 10.000, answered 40 ms later by the other clock, back 80 ms after sending.
            clock.AddRoundTrip(10.0, 10.04 + TrueOffset, 10.08);

            Assert.That(clock.OffsetSeconds, Is.EqualTo(TrueOffset).Within(Tolerance));
            Assert.That(clock.ClockErrorSeconds, Is.EqualTo(0.04).Within(Tolerance));
            Assert.That(clock.ToRemoteTime(20.0), Is.EqualTo(20.0 + TrueOffset).Within(Tolerance));
        }

        [Test]
        public void TheQuickestRoundTripSetsTheOffsetNotALopsidedSlowOne()
        {
            var clock = new ClockSync();

            // A slow trip whose way back was stuck 300 ms in a queue: taken alone, it would put the clock 150 ms off.
            clock.AddRoundTrip(1.0, 1.02 + TrueOffset, 1.34);
            // A quick, even one.
            clock.AddRoundTrip(1.5, 1.52 + TrueOffset, 1.54);
            // Another lopsided one, slow on the way out.
            clock.AddRoundTrip(2.0, 2.25 + TrueOffset, 2.27);

            Assert.That(clock.OffsetSeconds, Is.EqualTo(TrueOffset).Within(Tolerance));
            Assert.That(clock.ClockErrorSeconds, Is.EqualTo(0.02).Within(Tolerance));
        }

        [Test]
        public void TheShownRoundTripIsTheWindowsMedian()
        {
            var clock = new ClockSync();
            clock.AddRoundTrip(0.0, TrueOffset, 0.05);
            clock.AddRoundTrip(1.0, TrueOffset, 1.30);
            clock.AddRoundTrip(2.0, TrueOffset, 2.07);

            Assert.That(clock.RoundTripSeconds, Is.EqualTo(0.07).Within(Tolerance));

            clock.AddRoundTrip(3.0, TrueOffset, 3.09);
            Assert.That(clock.RoundTripSeconds, Is.EqualTo(0.08).Within(Tolerance), "Even count: the mean of the middle two.");
        }

        [Test]
        public void NothingIsKnownBeforeTheFirstRoundTrip()
        {
            var clock = new ClockSync();

            Assert.That(clock.IsSynced, Is.False);
            Assert.That(clock.OffsetSeconds, Is.Null);
            Assert.That(clock.RoundTripSeconds, Is.Null);
            Assert.That(clock.ToRemoteTime(5.0), Is.Null);
        }

        [TestCase(-0.01)]
        [TestCase(ClockSync.MaxRoundTripSeconds + 0.01)]
        public void AnImpossibleOrTooSlowRoundTripIsDropped(double roundTripSeconds)
        {
            var clock = new ClockSync();

            Assert.That(clock.AddRoundTrip(10.0, TrueOffset, 10.0 + roundTripSeconds), Is.False);
            Assert.That(clock.IsSynced, Is.False);
        }

        [Test]
        public void OnlyTheLastWindowOfRoundTripsCounts()
        {
            var clock = new ClockSync();

            // A very quick trip first, then a whole window of slower ones after the route changed for good.
            clock.AddRoundTrip(0.0, TrueOffset, 0.001);
            for (var index = 1; index <= ClockSync.WindowSize; index++)
            {
                clock.AddRoundTrip(index, index + 0.03 + TrueOffset + 0.5, index + 0.06);
            }

            Assert.That(clock.Count, Is.EqualTo(ClockSync.WindowSize));
            Assert.That(clock.OffsetSeconds, Is.EqualTo(TrueOffset + 0.5).Within(1e-6), "The old quick trip has left the window.");
        }

        [Test]
        public void ResetForgetsEverything()
        {
            var clock = new ClockSync();
            clock.AddRoundTrip(0.0, TrueOffset, 0.05);

            clock.Reset();

            Assert.That(clock.IsSynced, Is.False);
        }
    }
}
