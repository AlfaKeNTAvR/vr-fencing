// The swept tip Touch, glance and flat-contact detector (PER-33), ported from the PER-22 prototype's tests and
// extended for capsule zones, overlapping zones and the fast-thrust fix. Units are meters and seconds; the Fencer
// faces +z, so a thrust moves the blade along +z.
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Blade;
using VrFencing.Dummy;

namespace VrFencing.Tests.Blade
{
    public class TouchDetectorTests
    {
        private const double FrameSeconds = 1.0 / 72.0;
        private const float BladeLengthMeters = 0.9f;

        [Test]
        public void FastTipDoesNotTunnelThroughAThinZone()
        {
            // 3 cm zone; a 15 m/s tip moves 20.8 cm per 72 Hz frame, so both frame ends are outside the zone and a
            // point-in-zone check would miss it.
            var zones = Sphere(TargetZone.Wrist, new Vector3(0.0f, 1.0f, 2.0f), 0.03f);
            var stepMeters = 15.0f * (float)FrameSeconds;
            var startZ = 2.0f - (stepMeters / 2.0f) - 0.01f;
            var poses = new[] { PointingForward(new Vector3(0.0f, 1.0f, startZ - stepMeters)), PointingForward(new Vector3(0.0f, 1.0f, startZ)), PointingForward(new Vector3(0.0f, 1.0f, startZ + stepMeters)) };

            var events = Run(poses, zones, TouchRules.Strict);

            Assert.That(events.Select(e => e.Type), Is.EqualTo(new[] { BladeEventType.Touch }));
            Assert.That(events[0].TipSpeedMetersPerSecond, Is.EqualTo(15.0f).Within(0.05f));
            Assert.That(events[0].Zone, Is.EqualTo(TargetZone.Wrist));
        }

        [Test]
        public void FastTipDoesNotTunnelThroughALongCapsuleCrossedSideways()
        {
            // A thin forearm-like capsule lying across the thrust line, hit by a 12 m/s tip.
            var zones = new[] { new ZoneCapsule(TargetZone.Forearm, new Capsule(new Vector3(-0.15f, 1.0f, 2.0f), new Vector3(0.15f, 1.0f, 2.0f), 0.03f)) };
            var stepMeters = 12.0f * (float)FrameSeconds;
            var poses = Enumerable.Range(0, 3).Select(frame => PointingForward(new Vector3(0.0f, 1.0f, 2.0f - 0.04f - stepMeters + (frame * stepMeters)))).ToArray();

            var events = Run(poses, zones, TouchRules.Strict);

            Assert.That(events.Select(e => e.Type), Is.EqualTo(new[] { BladeEventType.Touch }));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void TangentialSwipeIsAGlanceNotATouch(bool isStrict)
        {
            // The tip slides sideways across the front of the zone, just grazing it.
            var center = new Vector3(0.0f, 1.3f, 2.0f);
            var zones = Sphere(TargetZone.Body, center, 0.1f);
            var tipZ = center.Z - 0.1f;
            var poses = Enumerable.Range(0, 20).Select(frame => PointingForward(new Vector3(-0.3f + (frame * 0.03f), center.Y, tipZ))).ToArray();

            var events = Run(poses, zones, isStrict ? TouchRules.Strict : TouchRules.Loose);

            Assert.That(events.Select(e => e.Type), Is.EqualTo(new[] { BladeEventType.TipGlance }));
        }

        [Test]
        public void SlowPressIsOneTouch()
        {
            // 0.1 m/s straight in, then holding the point pressed in.
            var center = new Vector3(0.0f, 1.0f, 2.0f);
            var zones = Sphere(TargetZone.Hand, center, 0.05f);
            var poses = Enumerable.Range(0, 60).Select(frame => PointingForward(new Vector3(0.0f, 1.0f, center.Z - 0.07f + (frame * 0.1f * (float)FrameSeconds)))).ToArray();

            var events = Run(poses, zones, TouchRules.Strict);

            Assert.That(events.Select(e => e.Type), Is.EqualTo(new[] { BladeEventType.Touch }));
            Assert.That(events[0].ApproachAngleDegrees, Is.LessThan(1.0f));
            Assert.That(events[0].BladeAxisAngleDegrees, Is.LessThan(1.0f));
        }

        [Test]
        public void WithdrawAndRetouchCountsTwice()
        {
            var center = new Vector3(0.0f, 1.0f, 2.0f);
            var zones = Sphere(TargetZone.Hand, center, 0.05f);
            var outside = PointingForward(new Vector3(0.0f, 1.0f, center.Z - 0.1f));
            var inside = PointingForward(new Vector3(0.0f, 1.0f, center.Z - 0.04f));

            var events = Run(new[] { outside, inside, outside, inside }, zones, TouchRules.Strict);

            Assert.That(events.Count(e => e.Type == BladeEventType.Touch), Is.EqualTo(2));
        }

        [Test]
        public void PointMovingInWithTheBladeSidewaysIsNotATouch()
        {
            // The tip moves straight in, but the blade lies across the zone's surface.
            var center = new Vector3(0.0f, 1.0f, 2.0f);
            var zones = Sphere(TargetZone.Forearm, center, 0.05f);

            var events = Run(new[] { SidewaysAt(center.Z - 0.08f), SidewaysAt(center.Z - 0.04f) }, zones, TouchRules.Loose);

            Assert.That(events.Select(e => e.Type), Is.EqualTo(new[] { BladeEventType.TipGlance }));
        }

        [Test]
        public void FlatBladeCrossingAZoneIsFlatContact()
        {
            // Blade held across the body (along x), swept forward through the zone in one fast frame; the tip stays
            // 0.3 m to the side of it.
            var center = new Vector3(0.0f, 1.0f, 2.0f);
            var zones = Sphere(TargetZone.Body, center, 0.1f);

            var events = Run(new[] { AcrossAt(center.Z - 0.2f), AcrossAt(center.Z + 0.2f) }, zones, TouchRules.Strict);

            Assert.That(events.Select(e => e.Type), Is.EqualTo(new[] { BladeEventType.FlatContact }));
            Assert.That(float.IsNaN(events[0].ApproachAngleDegrees), Is.True);
            Assert.That(events[0].DistanceFromZoneCenterMeters, Is.EqualTo(0.1f).Within(0.01f));
        }

        [Test]
        public void RestingTheFlatOnAZoneCountsOnce()
        {
            var center = new Vector3(0.0f, 1.0f, 2.0f);
            var zones = Sphere(TargetZone.Body, center, 0.1f);
            var poses = new[] { AcrossAt(center.Z - 0.2f), AcrossAt(center.Z - 0.05f), AcrossAt(center.Z - 0.04f), AcrossAt(center.Z - 0.05f) };

            var events = Run(poses, zones, TouchRules.Strict);

            Assert.That(events.Select(e => e.Type), Is.EqualTo(new[] { BladeEventType.FlatContact }));
        }

        [Test]
        public void PressingThroughAfterATouchIsNotFlatContact()
        {
            var center = new Vector3(0.0f, 1.0f, 2.0f);
            var zones = Sphere(TargetZone.Thigh, center, 0.08f);
            var poses = new[]
            {
                PointingForward(new Vector3(0.0f, 1.0f, center.Z - 0.1f)),
                PointingForward(new Vector3(0.0f, 1.0f, center.Z - 0.05f)),
                PointingForward(new Vector3(0.0f, 1.0f, center.Z + 0.1f)),
            };

            var events = Run(poses, zones, TouchRules.Strict);

            Assert.That(events.Select(e => e.Type), Is.EqualTo(new[] { BladeEventType.Touch }));
        }

        [Test]
        public void TouchOnOverlappingZonesReportsOnlyTheZoneReachedFirst()
        {
            // Hand and wrist capsules overlap like the Dummy's; a thrust along the arm reaches the hand first and then
            // pushes on into the wrist without a second event.
            var zones = new[]
            {
                new ZoneCapsule(TargetZone.Hand, new Capsule(new Vector3(0.0f, 1.0f, 2.0f), new Vector3(0.0f, 1.0f, 2.08f), 0.04f)),
                new ZoneCapsule(TargetZone.Wrist, new Capsule(new Vector3(0.0f, 1.0f, 2.08f), new Vector3(0.0f, 1.0f, 2.14f), 0.035f)),
            };
            var poses = Enumerable.Range(0, 12).Select(frame => PointingForward(new Vector3(0.0f, 1.0f, 1.9f + (frame * 0.02f)))).ToArray();

            var events = Run(poses, zones, TouchRules.Strict);

            Assert.That(events.Select(e => (e.Type, e.Zone)), Is.EqualTo(new[] { (BladeEventType.Touch, TargetZone.Hand) }));
        }

        [Test]
        public void EventsCarryTheContactPointAndItsDistanceFromTheZoneCenter()
        {
            // Point-first onto the side of a body capsule, 10 cm above its middle.
            var zones = new[] { new ZoneCapsule(TargetZone.Body, new Capsule(new Vector3(0.0f, 1.0f, 2.0f), new Vector3(0.0f, 1.4f, 2.0f), 0.12f)) };
            var poses = Enumerable.Range(0, 6).Select(frame => PointingForward(new Vector3(0.0f, 1.3f, 1.8f + (frame * 0.02f)))).ToArray();

            var events = Run(poses, zones, TouchRules.Strict);

            Assert.That(events, Has.Count.EqualTo(1));
            Assert.That(events[0].ContactPoint.Z, Is.EqualTo(1.88f).Within(0.002f));
            Assert.That(events[0].DistanceFromZoneCenterMeters, Is.EqualTo(new Vector2(0.12f, 0.1f).Length()).Within(0.002f));
            Assert.That(events[0].TipSpeedMetersPerSecond, Is.EqualTo(0.02f * 72.0f).Within(0.01f));
        }

        [Test]
        public void ResetForgetsTheLastPoseSoAJumpIsNotSwept()
        {
            var center = new Vector3(0.0f, 1.0f, 2.0f);
            var zones = Sphere(TargetZone.Body, center, 0.1f);
            var detector = new TouchDetector();
            detector.Step(new BladeSample(0.0, PointingForward(new Vector3(0.0f, 1.0f, 1.5f))), zones, TouchRules.Strict);

            detector.Reset();
            var events = detector.Step(new BladeSample(FrameSeconds, PointingForward(new Vector3(0.0f, 1.0f, 2.5f))), zones, TouchRules.Strict);

            Assert.That(events, Is.Empty);
        }

        [Test]
        public void ZoneTouchesNeedTheDummyLayoutInTheStripFrame()
        {
            // End to end with the real Dummy: a straight thrust at the hand zone of a Dummy at lunge distance is a Touch
            // on the hand.
            var layout = DummyLayout.Create(1.75f, VrFencing.Settings.Hand.Right);
            var zones = layout.GetZonesOnStrip(2.0f);
            var hand = zones.Single(zone => zone.Zone == TargetZone.Hand).Shape;
            var start = hand.Center - new Vector3(0.0f, 0.0f, 0.3f);
            var poses = Enumerable.Range(0, 20).Select(frame => PointingForward(start + new Vector3(0.0f, 0.0f, frame * 0.025f))).ToArray();

            var events = Run(poses, zones, TouchRules.Strict);

            Assert.That(events.First().Type, Is.EqualTo(BladeEventType.Touch));
            Assert.That(events.First().Zone, Is.EqualTo(TargetZone.Hand).Or.EqualTo(TargetZone.Wrist));
        }

        internal static List<BladeEvent> Run(IReadOnlyList<BladeSegment> poses, IReadOnlyList<ZoneCapsule> zones, TouchRules rules)
        {
            var detector = new TouchDetector();
            var events = new List<BladeEvent>();
            for (var frame = 0; frame < poses.Count; frame++)
            {
                events.AddRange(detector.Step(new BladeSample(frame * FrameSeconds, poses[frame]), zones, rules));
            }

            return events;
        }

        internal static BladeSegment PointingForward(Vector3 tipPosition)
        {
            return new BladeSegment(tipPosition - (Vector3.UnitZ * BladeLengthMeters), tipPosition);
        }

        internal static ZoneCapsule[] Sphere(TargetZone zone, Vector3 center, float radiusMeters)
        {
            return new[] { new ZoneCapsule(zone, new Capsule(center, center, radiusMeters)) };
        }

        private static BladeSegment SidewaysAt(float tipZ)
        {
            return new BladeSegment(new Vector3(-BladeLengthMeters, 1.0f, tipZ), new Vector3(0.0f, 1.0f, tipZ));
        }

        private static BladeSegment AcrossAt(float z)
        {
            return new BladeSegment(new Vector3(-0.6f, 1.0f, z), new Vector3(0.3f, 1.0f, z));
        }
    }
}
