// Behaviour of the swept tip Touch / flat-contact detector. World units are
// metres; the Fencer faces +z, so a lunge moves the blade along +z.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VrFencing.Blade;

namespace VrFencing.Tests
{
    public class TouchDetectorTests
    {
        private const float FrameSeconds = 1f / 72f;
        private const float BladeLengthMeters = 0.9f;

        private static BladeSegment PointingForward(Vector3 tipPosition)
        {
            return new BladeSegment(tipPosition - Vector3.forward * BladeLengthMeters, tipPosition);
        }

        private static List<BladeEvent> RunFrames(TouchDetector detector, IReadOnlyList<BladeSegment> poses, TouchRules rules)
        {
            var events = new List<BladeEvent>();
            for (var frame = 1; frame < poses.Count; frame++)
                events.AddRange(detector.Step(poses[frame - 1], poses[frame], FrameSeconds, rules));
            return events;
        }

        [Test]
        public void FastTipDoesNotTunnelThroughThinTarget()
        {
            // 3 cm Target; a 15 m/s tip moves 20.8 cm per 72 Hz frame, so both frame
            // endpoints are outside the Target and a point-in-sphere check would miss it.
            var detector = new TouchDetector(new[] { new SphereTarget("wrist", new Vector3(0f, 1f, 2f), 0.03f) });
            var stepMeters = 15f * FrameSeconds;
            var before = PointingForward(new Vector3(0f, 1f, 2f - stepMeters / 2f - 0.01f));
            var after = PointingForward(before.TipPosition + Vector3.forward * stepMeters);

            var events = detector.Step(before, after, FrameSeconds, TouchRules.Strict);

            Assert.That(events.Count(e => e.Type == BladeEventType.Touch), Is.EqualTo(1));
            Assert.That(events.Single().TipSpeedMetersPerSecond, Is.EqualTo(15f).Within(0.01f));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void TangentialSwipeIsNotATouch(bool isStrict)
        {
            // Tip slides sideways across the front of the Target, just grazing it.
            var center = new Vector3(0f, 1.3f, 2f);
            var detector = new TouchDetector(new[] { new SphereTarget("torso", center, 0.1f) });
            var rules = isStrict ? TouchRules.Strict : TouchRules.Loose;
            var tipZ = center.z - 0.1f;
            var poses = Enumerable.Range(0, 20)
                .Select(frame => PointingForward(new Vector3(-0.3f + frame * 0.03f, center.y, tipZ)))
                .ToList();

            var events = RunFrames(detector, poses, rules);

            Assert.That(events.Any(e => e.Type == BladeEventType.Touch), Is.False);
            Assert.That(events.Count(e => e.Type == BladeEventType.TipGlance), Is.EqualTo(1));
        }

        [Test]
        public void SlowPressIsOneTouch()
        {
            // 0.1 m/s straight into the Target, then holding the point pressed in.
            var center = new Vector3(0f, 1f, 2f);
            var detector = new TouchDetector(new[] { new SphereTarget("hand", center, 0.05f) });
            var speedMetersPerSecond = 0.1f;
            var poses = Enumerable.Range(0, 60)
                .Select(frame => PointingForward(new Vector3(0f, 1f,
                    center.z - 0.07f + frame * speedMetersPerSecond * FrameSeconds)))
                .ToList();

            var events = RunFrames(detector, poses, TouchRules.Strict);

            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(events[0].Type, Is.EqualTo(BladeEventType.Touch));
            Assert.That(events[0].ApproachAngleDegrees, Is.LessThan(1f));
        }

        [Test]
        public void WithdrawAndRetouchCountsTwice()
        {
            var center = new Vector3(0f, 1f, 2f);
            var detector = new TouchDetector(new[] { new SphereTarget("hand", center, 0.05f) });
            var outside = PointingForward(new Vector3(0f, 1f, center.z - 0.1f));
            var inside = PointingForward(new Vector3(0f, 1f, center.z - 0.04f));

            var events = RunFrames(detector, new[] { outside, inside, outside, inside }, TouchRules.Strict);

            Assert.That(events.Count(e => e.Type == BladeEventType.Touch), Is.EqualTo(2));
        }

        [Test]
        public void PointMovingInWithBladeSidewaysIsNotATouch()
        {
            // Tip moves straight in, but the blade lies across the Target's surface.
            var center = new Vector3(0f, 1f, 2f);
            var detector = new TouchDetector(new[] { new SphereTarget("forearm", center, 0.05f) });
            BladeSegment SidewaysAt(float tipZ) =>
                new(new Vector3(-BladeLengthMeters, 1f, tipZ), new Vector3(0f, 1f, tipZ));

            var events = detector.Step(SidewaysAt(center.z - 0.08f), SidewaysAt(center.z - 0.04f), FrameSeconds,
                TouchRules.Loose);

            Assert.That(events.Any(e => e.Type == BladeEventType.Touch), Is.False);
        }

        [Test]
        public void FlatBladeCrossingTargetIsFlatContact()
        {
            // Blade held across the body (along x), swept forward through the Target in
            // one fast frame; the tip stays 0.3 m to the side of it.
            var center = new Vector3(0f, 1f, 2f);
            var detector = new TouchDetector(new[] { new SphereTarget("torso", center, 0.1f) });
            BladeSegment AcrossAt(float z) => new(new Vector3(-0.6f, 1f, z), new Vector3(0.3f, 1f, z));

            var events = detector.Step(AcrossAt(center.z - 0.2f), AcrossAt(center.z + 0.2f), FrameSeconds,
                TouchRules.Strict);

            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(events[0].Type, Is.EqualTo(BladeEventType.FlatContact));
        }

        [Test]
        public void PressingThroughAfterTouchIsNotFlatContact()
        {
            var center = new Vector3(0f, 1f, 2f);
            var detector = new TouchDetector(new[] { new SphereTarget("thigh", center, 0.08f) });
            var poses = new[]
            {
                PointingForward(new Vector3(0f, 1f, center.z - 0.1f)),
                PointingForward(new Vector3(0f, 1f, center.z - 0.05f)),
                PointingForward(new Vector3(0f, 1f, center.z + 0.1f)),
            };

            var events = RunFrames(detector, poses, TouchRules.Strict);

            Assert.That(events.Select(e => e.Type), Is.EqualTo(new[] { BladeEventType.Touch }));
        }
    }

    public class SegmentMathTests
    {
        [Test]
        public void TryFindSphereEntryReportsEntryFraction()
        {
            var found = SegmentMath.TryFindSphereEntry(Vector3.zero, new Vector3(0f, 0f, 2f), new Vector3(0f, 0f, 1f),
                0.5f, out var fraction);

            Assert.That(found, Is.True);
            Assert.That(fraction, Is.EqualTo(0.25f).Within(1e-5f));
        }

        [Test]
        public void TryFindSphereEntryIgnoresStartInsideAndMisses()
        {
            var center = new Vector3(0f, 0f, 1f);

            Assert.That(SegmentMath.TryFindSphereEntry(center, center + Vector3.right, center, 0.5f, out _), Is.False);
            Assert.That(SegmentMath.TryFindSphereEntry(Vector3.zero, new Vector3(0f, 0f, 0.4f), center, 0.5f, out _),
                Is.False);
            Assert.That(SegmentMath.TryFindSphereEntry(Vector3.up, new Vector3(0f, 1f, 2f), center, 0.5f, out _),
                Is.False);
        }

        [Test]
        public void FindDistanceFromPointToSegmentClampsToEnds()
        {
            var distance = SegmentMath.FindDistanceFromPointToSegment(new Vector3(0f, 0f, 3f), Vector3.zero,
                new Vector3(0f, 0f, 1f));

            Assert.That(distance, Is.EqualTo(2f).Within(1e-5f));
        }
    }
}
