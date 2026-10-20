// The attacker's side of a bout Touch (PER-82): this Fencer's blade against the opponent's body as drawn here, through
// the Dummy's Touch rules, and whether the attacker stood on their own Strip.
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Blade;
using VrFencing.Dummy;
using VrFencing.Partner;
using VrFencing.Session;
using VrFencing.Settings;

namespace VrFencing.Tests.Partner
{
    public class OpponentTouchSensorTests
    {
        private const double FrameSeconds = 1.0 / 72.0;
        private const float SpeedMetersPerSecond = 2.0f;

        private static readonly TouchRules Rules = TouchRules.For(TouchRule.Loose, TouchPressure.Realistic);

        [Test]
        public void APointFirstThrustIntoTheTorsoIsATouchOnTheBody()
        {
            var body = PartnerTestData.Body(BodyTrackingFidelity.High, Vector3.Zero);
            var torso = TorsoCenter(body);
            var sensor = new OpponentTouchSensor();

            // Straight in along -z from 40 cm out, the blade pointing the same way.
            var events = Thrust(sensor, body, torso + new Vector3(0.0f, 0.0f, 0.4f), -Vector3.UnitZ);

            Assert.That(events.Count(e => e.Type == BladeEventType.Touch), Is.EqualTo(1));
            Assert.That(events.First(e => e.Type == BladeEventType.Touch).Zone, Is.EqualTo(TargetZone.Body));
        }

        [Test]
        public void ASidewaysSweepIsAGlanceNeverATouch()
        {
            // Only the torso, so the sweep cannot run into an arm head on.
            var body = TorsoOnly(PartnerTestData.Body(BodyTrackingFidelity.High, Vector3.Zero));
            var torso = TorsoCenter(body);
            var sensor = new OpponentTouchSensor();
            var radius = EnGardePose.TorsoRadius * PartnerTestData.HeightMeters;
            var events = new List<BladeEvent>();

            // The point slides across the front of the torso, grazing it (in at about 80 degrees off its normal), the blade pointing in.
            var start = torso + new Vector3(-0.5f, 0.0f, radius * 0.99f);
            for (var frame = 0; frame < 60; frame++)
            {
                var tip = start + new Vector3(SpeedMetersPerSecond * (float)(frame * FrameSeconds), 0.0f, 0.0f);
                events.AddRange(sensor.Step(frame * FrameSeconds, BladeTo(tip, -Vector3.UnitZ), body, PartnerTestData.HeightMeters, Rules));
            }

            Assert.That(events.Any(e => e.Type == BladeEventType.Touch), Is.False);
            Assert.That(events.Any(e => e.Type == BladeEventType.TipGlance), Is.True);
        }

        [Test]
        public void NoBodyOrNoBladeForgetsTheMotionSoNothingIsJudgedAcrossTheGap()
        {
            var body = PartnerTestData.Body(BodyTrackingFidelity.High, Vector3.Zero);
            var torso = TorsoCenter(body);
            var sensor = new OpponentTouchSensor();

            sensor.Step(0.0, BladeTo(torso + new Vector3(0.0f, 0.0f, 0.4f), -Vector3.UnitZ), body, PartnerTestData.HeightMeters, Rules);
            Assert.That(sensor.Step(FrameSeconds, null, body, PartnerTestData.HeightMeters, Rules), Is.Empty);
            Assert.That(sensor.Step(2 * FrameSeconds, BladeTo(torso, -Vector3.UnitZ), null, PartnerTestData.HeightMeters, Rules), Is.Empty);

            // Back, already inside: the first pose only records where the blade is.
            Assert.That(sensor.Step(3 * FrameSeconds, BladeTo(torso, -Vector3.UnitZ), body, PartnerTestData.HeightMeters, Rules), Is.Empty);
        }

        [Test]
        public void EveryCapsuleOfTheBodyHasAZoneName()
        {
            var capsules = new List<Capsule>();
            var zones = new List<ZoneCapsule>();

            OpponentTargets.GetZones(PartnerTestData.Body(BodyTrackingFidelity.High, Vector3.Zero), PartnerTestData.HeightMeters, capsules, zones);

            Assert.That(OpponentTargets.ZoneOfCapsule.Count, Is.EqualTo(FencerBodyShape.CapsuleCount));
            Assert.That(zones.Count, Is.EqualTo(FencerBodyShape.CapsuleCount));
            Assert.That(zones[1].Zone, Is.EqualTo(TargetZone.Body), "The torso.");
            Assert.That(zones[6].Zone, Is.EqualTo(TargetZone.Hand), "The sword hand.");
        }

        [Test]
        public void TheJudgedBladeRunsFromTheGuardToTheDroopedPoint()
        {
            var controller = new TrackedPose(new Vector3(0.0f, 1.2f, 0.0f), Quaternion.Identity);

            var blade = OpponentTouchSensor.GetBlade(controller, new BladeSettings(), Hand.Right);

            var length = Vector3.Distance(blade.GuardPosition, blade.TipPosition);
            Assert.That(length, Is.InRange(0.85f, BladePose.BladeLengthMeters + 0.001f));
            Assert.That(blade.TipPosition.Z - blade.GuardPosition.Z, Is.GreaterThan(0.8f), "Forward along the controller.");
            Assert.That(blade.TipPosition.Y, Is.LessThan(blade.GuardPosition.Y), "The Center grip's pitch and the droop hang the point.");
        }

        [Test]
        public void BothFeetOffTheSideOrPastTheRearEndIsOffTheStrip()
        {
            var strip = new StripExtent(6.0f, -1.0f);

            Assert.That(OwnStripCheck.IsOffOwnStrip(Feet(new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.1f, 0.0f, -0.5f)), null, strip, 0.0f), Is.False);
            Assert.That(OwnStripCheck.IsOffOwnStrip(Feet(new Vector3(0.9f, 0.0f, 0.0f), new Vector3(0.1f, 0.0f, -0.5f)), null, strip, 0.0f), Is.False, "One foot still on.");
            Assert.That(OwnStripCheck.IsOffOwnStrip(Feet(new Vector3(0.9f, 0.0f, 0.0f), new Vector3(0.8f, 0.0f, -0.5f)), null, strip, 0.0f), Is.True, "Both off the side.");
            Assert.That(OwnStripCheck.IsOffOwnStrip(Feet(new Vector3(0.0f, 0.0f, -1.1f), new Vector3(0.0f, 0.0f, -1.4f)), null, strip, 0.0f), Is.True, "Both past the rear end.");
            Assert.That(OwnStripCheck.IsOffOwnStrip(Feet(new Vector3(0.9f, 0.0f, 0.0f), new Vector3(0.8f, 0.0f, -0.5f)), null, strip, 0.2f), Is.False, "Within the tolerance.");
            Assert.That(OwnStripCheck.IsOffOwnStrip(Feet(new Vector3(0.0f, 0.0f, 5.5f), new Vector3(0.0f, 0.0f, 5.2f)), null, strip, 0.0f), Is.False, "Past the front end is the opponent's half, not off.");
        }

        [Test]
        public void WithNoFeetTheHeadStandsInForThem()
        {
            var strip = new StripExtent(6.0f, -1.0f);
            var noBody = new FencerBody(new Vector3?[FencerBody.JointCount], null, null);

            Assert.That(OwnStripCheck.IsOffOwnStrip(noBody, new Vector3(0.0f, 1.6f, -1.3f), strip, 0.0f), Is.True);
            Assert.That(OwnStripCheck.IsOffOwnStrip(noBody, new Vector3(0.0f, 1.6f, 0.0f), strip, 0.0f), Is.False);
            Assert.That(OwnStripCheck.IsOffOwnStrip(null, null, strip, 0.0f), Is.False, "Nothing tracked: never called off.");
        }

        private static List<BladeEvent> Thrust(OpponentTouchSensor sensor, FencerBody body, Vector3 start, Vector3 direction)
        {
            var events = new List<BladeEvent>();
            for (var frame = 0; frame < 30; frame++)
            {
                var tip = start + (direction * SpeedMetersPerSecond * (float)(frame * FrameSeconds));
                events.AddRange(sensor.Step(frame * FrameSeconds, BladeTo(tip, direction), body, PartnerTestData.HeightMeters, Rules));
            }

            return events;
        }

        private static BladeSegment BladeTo(Vector3 tip, Vector3 direction)
        {
            return new BladeSegment(tip - (direction * BladePose.BladeLengthMeters), tip);
        }

        private static Vector3 TorsoCenter(FencerBody body)
        {
            return (body.Get(SkeletonJoint.HipCenter).Value + body.Get(SkeletonJoint.ShoulderCenter).Value) * 0.5f;
        }

        private static FencerBody TorsoOnly(FencerBody body)
        {
            var joints = new Vector3?[FencerBody.JointCount];
            joints[(int)SkeletonJoint.HipCenter] = body.Get(SkeletonJoint.HipCenter);
            joints[(int)SkeletonJoint.ShoulderCenter] = body.Get(SkeletonJoint.ShoulderCenter);
            return new FencerBody(joints, null, BodyTrackingFidelity.High);
        }

        private static FencerBody Feet(Vector3 front, Vector3 back)
        {
            var joints = new Vector3?[FencerBody.JointCount];
            joints[(int)SkeletonJoint.FrontAnkle] = front;
            joints[(int)SkeletonJoint.BackAnkle] = back;
            return new FencerBody(joints, null, BodyTrackingFidelity.High);
        }
    }
}
