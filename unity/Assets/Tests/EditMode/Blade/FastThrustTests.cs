// The fast-thrust fix (PER-22 feel test 2: on strict, 0 of 15 thrusts over 2 m/s counted). Point-first thrusts at
// real épée speeds must be Touches on both rules, even when a single tracked frame near the entry is off, while
// genuinely sideways moves must stay glances. Synthetic 72 Hz poses; meters and seconds; the Fencer faces +z.
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Blade;
using VrFencing.Dummy;

namespace VrFencing.Tests.Blade
{
    public class FastThrustTests
    {
        private const float FrameSeconds = 1.0f / 72.0f;
        private static readonly Vector3 HandCenter = new Vector3(0.0f, 1.0f, 2.0f);

        private static readonly ZoneCapsule[] Zones =
        {
            new ZoneCapsule(TargetZone.Hand, new Capsule(HandCenter, HandCenter, 0.05f)),
        };

        [Test]
        public void StraightThrustsAtEpeeSpeedsAreTouchesOnBothRules(
            [Values(0.5f, 2.0f, 3.0f, 5.0f, 10.0f, 15.0f)] float speedMetersPerSecond,
            [Values(0.0f, 0.02f)] float offCenterMeters,
            [Values(true, false)] bool isStrict)
        {
            var poses = Enumerable.Range(0, 400)
                .Select(frame => TouchDetectorTests.PointingForward(new Vector3(offCenterMeters, 1.0f, 1.5f + (frame * speedMetersPerSecond * FrameSeconds))))
                .TakeWhile(pose => pose.TipPosition.Z < 2.2f)
                .ToArray();

            var events = TouchDetectorTests.Run(poses, Zones, isStrict ? TouchRules.Strict : TouchRules.Loose);

            Assert.That(events.Select(e => e.Type), Is.EqualTo(new[] { BladeEventType.Touch }));
            Assert.That(events[0].TipSpeedMetersPerSecond, Is.EqualTo(speedMetersPerSecond).Within(speedMetersPerSecond * 0.01f));
        }

        [Test]
        public void FastThrustWithOneSidewaysTrackedFrameAtTheEntryIsATouchOnStrict()
        {
            // 2 m/s straight in (28 mm per frame), but the frame that crosses into the zone is tracked 25 mm to the
            // side. Judged on that one frame step the tip came in at 42 degrees, over strict's 40; over the last 40 ms
            // it came in nearly straight.
            var poses = Enumerable.Range(0, 20)
                .Select(frame => new Vector3(0.0f, 1.0f, 1.8f + (frame * 2.0f * FrameSeconds)))
                .ToArray();
            var entryFrame = poses.TakeWhile(tip => Vector3.Distance(tip, HandCenter) > 0.054f).Count();
            poses[entryFrame] += new Vector3(0.025f, 0.0f, 0.0f);

            var events = TouchDetectorTests.Run(poses.Take(entryFrame + 1).Select(TouchDetectorTests.PointingForward).ToArray(), Zones, TouchRules.Strict);

            Assert.That(events.Select(e => e.Type), Is.EqualTo(new[] { BladeEventType.Touch }));
            Assert.That(events[0].ApproachAngleDegrees, Is.LessThan(25.0f));
        }

        [Test]
        public void FastThrustWithTheBladeTwistedForOneFrameAtTheEntryIsATouchOnStrict()
        {
            // 3 m/s straight in with the blade pointing in, except that the entry frame reads the blade turned 60 degrees
            // (a wrist flick or a tracking blip). Averaged over the window, the blade still points in.
            var tips = Enumerable.Range(0, 20).Select(frame => new Vector3(0.0f, 1.0f, 1.7f + (frame * 3.0f * FrameSeconds))).ToArray();
            var entryFrame = tips.TakeWhile(tip => Vector3.Distance(tip, HandCenter) > 0.054f).Count();
            var poses = tips.Take(entryFrame + 1).Select(TouchDetectorTests.PointingForward).ToArray();
            var twisted = Vector3.Transform(Vector3.UnitZ, Quaternion.CreateFromAxisAngle(Vector3.UnitY, 60.0f * (float)System.Math.PI / 180.0f));
            poses[entryFrame] = new BladeSegment(tips[entryFrame] - (twisted * 0.9f), tips[entryFrame]);

            var events = TouchDetectorTests.Run(poses, Zones, TouchRules.Strict);

            Assert.That(events.Select(e => e.Type), Is.EqualTo(new[] { BladeEventType.Touch }));
            Assert.That(events[0].BladeAxisAngleDegrees, Is.LessThan(45.0f));
        }

        [Test]
        public void FastSidewaysCutAcrossTheZoneIsStillAGlance(
            [Values(true, false)] bool isStrict)
        {
            // The blade points at the Dummy but the tip sweeps across the zone's front at 3 m/s.
            var poses = Enumerable.Range(0, 20)
                .Select(frame => TouchDetectorTests.PointingForward(new Vector3(-0.3f + (frame * 3.0f * FrameSeconds), 1.0f, HandCenter.Z - 0.053f)))
                .ToArray();

            var events = TouchDetectorTests.Run(poses, Zones, isStrict ? TouchRules.Strict : TouchRules.Loose);

            Assert.That(events.Select(e => e.Type), Is.EqualTo(new[] { BladeEventType.TipGlance }));
        }

        [Test]
        public void SlowPressWithHandTremorIsATouchOnStrict()
        {
            // 0.15 m/s straight in with 3 mm of side-to-side tremor every frame: frame steps are 2 mm, so one frame's
            // direction is mostly tremor. The window stretches back until the tip has really moved.
            var poses = Enumerable.Range(0, 80)
                .Select(frame => TouchDetectorTests.PointingForward(new Vector3(frame % 2 == 0 ? 0.003f : -0.003f, 1.0f, 1.92f + (frame * 0.15f * FrameSeconds))))
                .ToArray();

            var events = TouchDetectorTests.Run(poses, Zones, TouchRules.Strict);

            Assert.That(events.Select(e => e.Type), Is.EqualTo(new[] { BladeEventType.Touch }));
        }
    }
}
