// The partner's clock, hand and body streams (PER-81, decided in PER-59): interpolation, a view-only forward
// prediction capped at 50 ms and 15 cm, stale streams, and the send rates.
using System;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Dummy;
using VrFencing.Partner;
using VrFencing.Session;
using VrFencing.Settings;

namespace VrFencing.Tests.Partner
{
    public class PartnerStreamsTests
    {
        private const float Tolerance = 1e-4f;

        [Test]
        public void TheClockKeepsTheQuickestTrip()
        {
            var clock = new RemoteClock();

            clock.Observe(100.0, 5.08);
            clock.Observe(100.1, 5.15);
            clock.Observe(100.2, 5.30);

            // Gaps -94.92, -94.95, -94.90: the smallest is kept, so the partner's now is 0.05 s behind the slow arrivals.
            Assert.That(clock.ToSenderTime(5.30).Value, Is.EqualTo(100.25).Within(1e-3));
        }

        [Test]
        public void TheClockFollowsADriftSlowlyAndAJumpAtOnce()
        {
            var clock = new RemoteClock();
            clock.Observe(10.0, 0.0);

            // Arrivals 5 ms slower for good: the gap creeps up by 1 ms per second, not at once.
            clock.Observe(11.0, 1.005);
            Assert.That(clock.ToSenderTime(1.005).Value, Is.EqualTo(11.004).Within(1e-5));

            // The partner restarted: its clock is 50 s behind now.
            clock.Observe(0.0, 2.0);
            Assert.That(clock.ToSenderTime(2.0).Value, Is.EqualTo(0.0).Within(1e-6));
        }

        [Test]
        public void TheClockHasNoTimeBeforeTheFirstMessage()
        {
            Assert.That(new RemoteClock().ToSenderTime(1.0), Is.Null);
        }

        [Test]
        public void TheHandPassesThroughEverySample()
        {
            var stream = new HandStream();
            stream.Add(PartnerTestData.Hand(1.00, new Vector3(0.0f, 1.0f, 0.0f), new Vector3(1.0f, 0.0f, 0.0f)));
            stream.Add(PartnerTestData.Hand(1.02, new Vector3(0.02f, 1.0f, 0.0f), new Vector3(1.0f, 0.0f, 0.0f)));

            Assert.That(stream.TryInterpolate(1.02, out var atSecond), Is.True);
            Assert.That(Vector3.Distance(atSecond.Position, new Vector3(0.02f, 1.0f, 0.0f)), Is.LessThan(Tolerance));
            stream.TryInterpolate(1.01, out var between);
            Assert.That(between.Position.X, Is.EqualTo(0.01f).Within(Tolerance));
        }

        [Test]
        public void TheHandIsDrawnBehindTheBufferThenPredictedAhead()
        {
            var stream = new HandStream();
            var velocity = new Vector3(0.0f, 0.0f, 1.0f);
            for (var step = 0; step <= 10; step++)
            {
                var time = step * 0.01;
                stream.Add(PartnerTestData.Hand(time, velocity * (float)time, velocity));
            }

            Assert.That(stream.TrySample(0.08, out var pose), Is.True);

            // Interpolated at 0.08 - 0.04 = 0.04 s, then 0.05 s ahead along 1 m/s: 9 cm.
            Assert.That(pose.Position.Z, Is.EqualTo(0.09f).Within(Tolerance));
        }

        [Test]
        public void ThePredictionNeverMovesTheHandMoreThan15Centimeters()
        {
            var predicted = HandStream.Predict(new TrackedPose(Vector3.Zero, Quaternion.Identity), new Vector3(10.0f, 0.0f, 0.0f), Vector3.Zero, HandStream.PredictionSeconds);

            Assert.That(predicted.Position.Length(), Is.EqualTo(HandStream.MaxPredictionMeters).Within(Tolerance));
        }

        [Test]
        public void ThePredictionTurnsTheHandAtMostItsCap()
        {
            var spin = new Vector3(0.0f, 100.0f, 0.0f);

            var predicted = HandStream.Predict(new TrackedPose(Vector3.Zero, Quaternion.Identity), Vector3.Zero, spin, HandStream.PredictionSeconds);

            var turnedDegrees = 2.0 * Math.Acos(Math.Min(1.0, Math.Abs(predicted.Rotation.W))) * 180.0 / Math.PI;
            Assert.That(turnedDegrees, Is.EqualTo(HandStream.MaxPredictionDegrees).Within(0.01));
        }

        [Test]
        public void ALateHandHoldsTheNewestSampleWithItsPrediction()
        {
            var stream = new HandStream();
            stream.Add(PartnerTestData.Hand(1.0, new Vector3(0.0f, 1.0f, 0.0f), new Vector3(0.0f, 0.0f, 1.0f)));

            stream.TrySample(1.3, out var pose);

            Assert.That(pose.Position.Z, Is.EqualTo(0.05f).Within(Tolerance));
        }

        [Test]
        public void TheHandIsLostWhenMessagesStop()
        {
            var stream = new HandStream();
            stream.Add(PartnerTestData.Hand(1.0, Vector3.Zero, Vector3.Zero));

            Assert.That(stream.TrySample(1.0 + HandStream.StaleSeconds + 0.01, out _), Is.False);
            Assert.That(new HandStream().TrySample(1.0, out _), Is.False);
        }

        [Test]
        public void AHandArrivingOutOfOrderIsDropped()
        {
            var stream = new HandStream();
            stream.Add(PartnerTestData.Hand(2.0, Vector3.Zero, Vector3.Zero));
            stream.Add(PartnerTestData.Hand(1.0, Vector3.One, Vector3.Zero));

            Assert.That(stream.Count, Is.EqualTo(1));
        }

        [Test]
        public void TheHandStreamKeepsOnlyItsCapacity()
        {
            var stream = new HandStream();
            for (var step = 0; step < HandStream.Capacity + 10; step++)
            {
                stream.Add(PartnerTestData.Hand(step * 0.014, Vector3.Zero, Vector3.Zero));
            }

            Assert.That(stream.Count, Is.EqualTo(HandStream.Capacity));
        }

        [Test]
        public void TheBodyIsInterpolatedBehindItsBuffer()
        {
            var stream = new BodyStream();
            stream.Add(new BodySample(1.0, PartnerTestData.Body(BodyTrackingFidelity.High, Vector3.Zero)));
            stream.Add(new BodySample(1.1, PartnerTestData.Body(BodyTrackingFidelity.High, new Vector3(0.0f, 0.0f, 0.2f))));

            var body = stream.Sample(1.15);
            var start = PartnerTestData.Body(BodyTrackingFidelity.High, Vector3.Zero);

            // Drawn at 1.15 - 0.1 = 1.05: halfway.
            Assert.That(body.Get(SkeletonJoint.HipCenter).Value.Z - start.Get(SkeletonJoint.HipCenter).Value.Z, Is.EqualTo(0.1f).Within(Tolerance));
            Assert.That(stream.IsStale(1.15), Is.False);
            Assert.That(stream.IsStale(1.1 + BodyStream.StaleSeconds + 0.01), Is.True);
        }

        [Test]
        public void NoBodyBeforeTheFirstSample()
        {
            Assert.That(new BodyStream().Sample(1.0), Is.Null);
            Assert.That(new BodyStream().IsStale(1.0), Is.True);
        }

        [TestCase(72.0, 72.0, 72)]
        [TestCase(72.0, 30.0, 30)]
        [TestCase(200.0, 72.0, 72)]
        [TestCase(90.0, 30.0, 30)]
        public void TheScheduleHoldsTheRateAtAnyFrameRate(double framesPerSecond, double ratePerSecond, int expectedPerSecond)
        {
            var schedule = new SendSchedule(ratePerSecond);
            var sent = 0;
            const int seconds = 10;
            for (var frame = 0; frame < framesPerSecond * seconds; frame++)
            {
                if (schedule.TakeIfDue(frame / framesPerSecond))
                {
                    sent++;
                }
            }

            Assert.That(sent / (double)seconds, Is.EqualTo(expectedPerSecond).Within(expectedPerSecond * 0.1));
        }

        [Test]
        public void TheScheduleStartsAgainAfterAPause()
        {
            var schedule = new SendSchedule(30.0);
            Assert.That(schedule.TakeIfDue(0.0), Is.True);
            Assert.That(schedule.TakeIfDue(100.0), Is.True);
            Assert.That(schedule.TakeIfDue(100.001), Is.False);
        }

        [Test]
        public void HandMotionReadsVelocityOverItsWindow()
        {
            var motion = new HandMotion();
            for (var frame = 0; frame <= 10; frame++)
            {
                var time = frame / 72.0;
                var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)(2.0 * time));
                motion.Add(time, new TrackedPose(new Vector3((float)(1.5 * time), 1.0f, 0.0f), turn));
            }

            Assert.That(motion.Velocity.X, Is.EqualTo(1.5f).Within(1e-3f));
            Assert.That(motion.AngularVelocity.Y, Is.EqualTo(2.0f).Within(1e-3f));
        }

        [Test]
        public void HandMotionStartsAtRestAfterAGap()
        {
            var motion = new HandMotion();
            motion.Add(0.0, new TrackedPose(Vector3.Zero, Quaternion.Identity));
            motion.Add(0.01, new TrackedPose(new Vector3(0.01f, 0.0f, 0.0f), Quaternion.Identity));

            motion.Add(1.0, new TrackedPose(new Vector3(5.0f, 0.0f, 0.0f), Quaternion.Identity));

            Assert.That(motion.Velocity, Is.EqualTo(Vector3.Zero));
        }

        [Test]
        public void TheLegsAlwaysFadeAndTheTorsoAndArmsShow()
        {
            Assert.That(OpponentFade.GetTargetFactor(OpponentBodyPart.Torso, BodyTrackingFidelity.Low, false), Is.EqualTo(1.0f));
            Assert.That(OpponentFade.GetTargetFactor(OpponentBodyPart.Arms, BodyTrackingFidelity.Low, false), Is.EqualTo(1.0f));
            Assert.That(OpponentFade.GetTargetFactor(OpponentBodyPart.Legs, BodyTrackingFidelity.High, false), Is.EqualTo(OpponentFade.GeneratedLegsFactor));
            Assert.That(
                OpponentFade.GetTargetFactor(OpponentBodyPart.Legs, BodyTrackingFidelity.Low, false),
                Is.LessThan(OpponentFade.GetTargetFactor(OpponentBodyPart.Legs, BodyTrackingFidelity.High, false)));
        }

        [Test]
        public void ALostBodyIsAGhost()
        {
            Assert.That(OpponentFade.GetTargetFactor(OpponentBodyPart.Torso, null, false), Is.EqualTo(OpponentFade.LostBodyFactor));
            Assert.That(OpponentFade.GetTargetFactor(OpponentBodyPart.Arms, BodyTrackingFidelity.High, true), Is.EqualTo(OpponentFade.LostBodyFactor));
        }

        [Test]
        public void AFadeTakesItsTime()
        {
            var halfway = OpponentFade.StepToward(1.0f, 0.0f, OpponentFade.FadeSeconds * 0.5f);

            Assert.That(halfway, Is.EqualTo(0.5f).Within(Tolerance));
            Assert.That(OpponentFade.StepToward(halfway, 1.0f, 10.0f), Is.EqualTo(1.0f));
        }
    }
}
