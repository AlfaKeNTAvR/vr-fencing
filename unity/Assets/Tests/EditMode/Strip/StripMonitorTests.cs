// Strip warnings (PER-31): entering a Warning Zone, leaving the Strip past an end or a side, coming back,
// with the off-strip tolerance from Settings and hysteresis against flicker.
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Settings;
using VrFencing.Strip;

namespace VrFencing.Tests.Strip
{
    public class StripMonitorTests
    {
        private const float HeadHeightMeters = 1.6f;

        private StripGeometry _geometry;
        private StripMonitor _monitor;
        private List<string> _events;

        [SetUp]
        public void CreateMonitor()
        {
            // En Garde Lines about 3.75 m apart: Warning Zones about 0.54 m, Strip from about -1.03 m to 5 m.
            var zones = new WarningZoneSettings();
            _geometry = StripGeometry.Create(StripGeometry.ConvertFarEndLengthToGap(5.0f, 1.65f, zones), 1.65f, zones);
            _monitor = new StripMonitor(_geometry);
            _events = new List<string>();
            _monitor.EnteredWarningZone += end => _events.Add($"zone {end}");
            _monitor.LeftStrip += () => _events.Add("left");
            _monitor.BackOnStrip += () => _events.Add("back");
        }

        [Test]
        public void WalkingIntoTheFarZoneBuzzesOnce()
        {
            Step(z: 2.0f);
            Step(z: 4.5f);
            Step(z: 4.6f);

            Assert.That(_events, Is.EqualTo(new[] { "zone Far" }));
            Assert.That(_monitor.CurrentWarningZone, Is.EqualTo(StripEnd.Far));
        }

        [Test]
        public void RetreatingIntoTheRearZoneBuzzes()
        {
            Step(z: 0.0f);
            Step(z: _geometry.RearEndZ + 0.1f);

            Assert.That(_events, Is.EqualTo(new[] { "zone Rear" }));
        }

        [Test]
        public void StartingInsideAZoneDoesNotBuzz()
        {
            Step(z: 4.8f);

            Assert.That(_events, Is.Empty);
            Assert.That(_monitor.CurrentWarningZone, Is.EqualTo(StripEnd.Far));
        }

        [Test]
        public void HoveringOnAZoneEdgeDoesNotBuzzAgain()
        {
            var zoneStart = _geometry.GetWarningZoneRange(StripEnd.Far).StartZ;
            Step(z: 2.0f);
            Step(z: zoneStart + 0.01f);
            Step(z: zoneStart - 0.02f);
            Step(z: zoneStart + 0.01f);

            Assert.That(_events, Is.EqualTo(new[] { "zone Far" }));

            Step(z: zoneStart - 0.1f);
            Step(z: zoneStart + 0.01f);

            Assert.That(_events, Is.EqualTo(new[] { "zone Far", "zone Far" }));
        }

        [Test]
        public void PassingTheFarEndLeavesTheStripAndComingBackReturns()
        {
            Step(z: 2.0f);
            Step(z: 4.5f);
            Step(z: 5.05f);

            Assert.That(_monitor.IsOnStrip, Is.False);
            Assert.That(_monitor.CurrentWarningZone, Is.Null);

            Step(z: 4.9f);

            Assert.That(_events, Is.EqualTo(new[] { "zone Far", "left", "back" }));
            Assert.That(_monitor.IsOnStrip, Is.True);
            Assert.That(_monitor.CurrentWarningZone, Is.EqualTo(StripEnd.Far));
        }

        [Test]
        public void PassingASideLeavesTheStrip()
        {
            Step(z: 2.0f);
            Step(x: 0.8f, z: 2.0f);

            Assert.That(_events, Is.EqualTo(new[] { "left" }));
        }

        [Test]
        public void ToleranceLetsTheHeadPassTheLineBeforeLeaving()
        {
            Step(z: 2.0f, toleranceMeters: 0.2f);
            Step(x: 0.9f, z: 2.0f, toleranceMeters: 0.2f);

            Assert.That(_monitor.IsOnStrip, Is.True);

            Step(x: 0.96f, z: 2.0f, toleranceMeters: 0.2f);

            Assert.That(_monitor.IsOnStrip, Is.False);
        }

        [Test]
        public void ComingBackNeedsTheHeadClearlyInside()
        {
            Step(z: 2.0f);
            Step(x: 0.8f, z: 2.0f);
            Step(x: 0.73f, z: 2.0f);

            Assert.That(_monitor.IsOnStrip, Is.False, "Within the hysteresis band: still off.");

            Step(x: 0.69f, z: 2.0f);

            Assert.That(_monitor.IsOnStrip, Is.True);
            Assert.That(_events, Is.EqualTo(new[] { "left", "back" }));
        }

        [Test]
        public void StartingOffTheStripRaisesLeftStrip()
        {
            Step(x: 2.0f, z: 2.0f);

            Assert.That(_events, Is.EqualTo(new[] { "left" }));
        }

        [Test]
        public void SteppingBackOnIntoAZoneDoesNotAlsoBuzz()
        {
            Step(z: 4.0f);
            Step(x: 1.0f, z: 4.8f);
            Step(z: 4.8f);

            Assert.That(_events, Is.EqualTo(new[] { "left", "back" }));
            Assert.That(_monitor.CurrentWarningZone, Is.EqualTo(StripEnd.Far));
        }

        private void Step(float z, float x = 0.0f, float toleranceMeters = 0.0f)
        {
            _monitor.Update(new Vector3(x, HeadHeightMeters, z), toleranceMeters);
        }
    }
}
