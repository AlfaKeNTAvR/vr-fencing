// The rep log's arm pose (PER-37): joints in the Strip frame at judging time, the tracking fidelity,
// and whether the blade arm was extended when the lunge started.
using System;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Session;
using VrFencing.Settings;
using VrFencing.Strip;
using VrFencing.Tracking;
using static VrFencing.Tests.Tracking.TrackingTestData;

namespace VrFencing.Tests.Tracking
{
    public class RepArmTrackerTests
    {
        private const float Tolerance = 1e-3f;
        private const float HeadHeightMeters = 1.6f;
        private static readonly TrackedPose StripAtRoomOrigin = new TrackedPose(Vector3.Zero, Quaternion.Identity);

        /// <summary>
        /// Feeds the tracker a Rep: the head moves as <paramref name="bursts"/> say (along room +z, which is the
        /// Strip's +z for <see cref="StripAtRoomOrigin"/>), and the arm's elbow angle at each time comes from
        /// <paramref name="elbowAngleAt"/> (null: arm not tracked). Returns the last sample's time.
        /// </summary>
        private static double FeedRep(
            RepArmTracker tracker,
            double durationSeconds,
            Func<double, float?> elbowAngleAt,
            BodyTrackingFidelity? fidelity = BodyTrackingFidelity.High,
            params Burst[] bursts)
        {
            var track = HeadTrack(durationSeconds, 0.0f, bursts);
            foreach (var position in track)
            {
                var head = new Vector3(0.0f, HeadHeightMeters, position.ForwardMeters);
                var angle = elbowAngleAt(position.TimeSeconds);
                if (!angle.HasValue)
                {
                    tracker.AddSample(new BodySample(position.TimeSeconds, head, fidelity, null, null, null));
                    continue;
                }

                var arm = ArmExtensionTests.ArmPointingForward(angle.Value);
                var bodyOffset = new Vector3(0.0f, 0.0f, position.ForwardMeters);
                tracker.AddSample(new BodySample(
                    position.TimeSeconds,
                    head,
                    fidelity,
                    new TrackedPose(arm.Shoulder + bodyOffset, Quaternion.Identity),
                    new TrackedPose(arm.Elbow + bodyOffset, Quaternion.Identity),
                    new TrackedPose(arm.Wrist + bodyOffset, Quaternion.Identity)));
            }

            return track[track.Count - 1].TimeSeconds;
        }

        [Test]
        public void ArmExtendedBeforeTheLungeIsLoggedAsExtended()
        {
            var tracker = new RepArmTracker();
            var judgedAt = FeedRep(tracker, 2.0, time => time < 0.6 ? 100.0f : 175.0f, BodyTrackingFidelity.High, Lunge(1.0));

            var snapshot = tracker.SnapshotAtJudging(StripAtRoomOrigin, judgedAt);

            Assert.That(snapshot.ArmExtendedBeforeLunge, Is.True);
            Assert.That(snapshot.ElbowAngleAtLungeStartDegrees, Is.EqualTo(175.0f).Within(0.1f));
            Assert.That(snapshot.LungeStartSecondsBeforeJudging, Is.EqualTo(1.0).Within(0.1));
            Assert.That(snapshot.Fidelity, Is.EqualTo(BodyTrackingFidelity.High));
        }

        [Test]
        public void ArmExtendingOnlyDuringTheLungeIsNotExtendedBeforeIt()
        {
            var tracker = new RepArmTracker();
            var judgedAt = FeedRep(tracker, 2.0, time => time < 1.2 ? 100.0f : 175.0f, BodyTrackingFidelity.High, Lunge(1.0));

            var snapshot = tracker.SnapshotAtJudging(StripAtRoomOrigin, judgedAt);

            Assert.That(snapshot.ArmExtendedBeforeLunge, Is.False);
            Assert.That(snapshot.ElbowAngleAtLungeStartDegrees, Is.EqualTo(100.0f).Within(0.1f));
        }

        [Test]
        public void NoLungeLeavesTheLungeFieldsEmptyButLogsTheArm()
        {
            var tracker = new RepArmTracker();
            var judgedAt = FeedRep(tracker, 2.0, _ => 175.0f);

            var snapshot = tracker.SnapshotAtJudging(StripAtRoomOrigin, judgedAt);

            Assert.That(snapshot.ArmExtendedBeforeLunge, Is.Null);
            Assert.That(snapshot.ElbowAngleAtLungeStartDegrees, Is.Null);
            Assert.That(snapshot.LungeStartSecondsBeforeJudging, Is.Null);
            Assert.That(snapshot.Shoulder, Is.Not.Null);
            Assert.That(snapshot.Elbow, Is.Not.Null);
            Assert.That(snapshot.Wrist, Is.Not.Null);
        }

        [Test]
        public void ArmNotTrackedAtTheLungeStartLeavesExtensionUnknown()
        {
            var tracker = new RepArmTracker();
            var judgedAt = FeedRep(tracker, 2.0, time => time < 1.3 ? (float?)null : 175.0f, BodyTrackingFidelity.Low, Lunge(1.0));

            var snapshot = tracker.SnapshotAtJudging(StripAtRoomOrigin, judgedAt);

            Assert.That(snapshot.ArmExtendedBeforeLunge, Is.Null);
            Assert.That(snapshot.LungeStartSecondsBeforeJudging, Is.EqualTo(1.0).Within(0.1));
            Assert.That(snapshot.Fidelity, Is.EqualTo(BodyTrackingFidelity.Low));
            Assert.That(snapshot.Wrist, Is.Not.Null);
        }

        [Test]
        public void LowFidelityIsLoggedQuietlyWithThePoses()
        {
            var tracker = new RepArmTracker();
            var judgedAt = FeedRep(tracker, 1.0, _ => 120.0f, BodyTrackingFidelity.Low);

            var snapshot = tracker.SnapshotAtJudging(StripAtRoomOrigin, judgedAt);

            Assert.That(snapshot.Fidelity, Is.EqualTo(BodyTrackingFidelity.Low));
            Assert.That(snapshot.Elbow, Is.Not.Null);
        }

        [Test]
        public void BodyTrackingNotRunningLogsNoArmPose()
        {
            var tracker = new RepArmTracker();
            var judgedAt = FeedRep(tracker, 2.0, _ => null, fidelity: null, Lunge(1.0));

            Assert.That(tracker.SnapshotAtJudging(StripAtRoomOrigin, judgedAt), Is.Null);
        }

        [Test]
        public void NoSamplesLogsNoArmPose()
        {
            Assert.That(new RepArmTracker().SnapshotAtJudging(StripAtRoomOrigin, 5.0), Is.Null);
        }

        [Test]
        public void StaleSamplesLogNoArmPose()
        {
            var tracker = new RepArmTracker();
            var lastSampleAt = FeedRep(tracker, 1.0, _ => 175.0f);

            var snapshot = tracker.SnapshotAtJudging(StripAtRoomOrigin, lastSampleAt + RepArmTracker.MaxSampleAgeSeconds + 0.05);

            Assert.That(snapshot, Is.Null);
        }

        [Test]
        public void JointsAreLoggedInTheStripFrame()
        {
            // The Strip runs along room -x from (1, 0, 0): room (0, 1.4, 0) is 1 m down the Strip, on the centerline.
            var (enGardeLine, _) = StripFrame.FromPlacementPoints(new Vector3(1.0f, 0.0f, 0.0f), new Vector3(-3.0f, 0.0f, 0.0f));
            var facingDownTheStrip = enGardeLine.Rotation;
            var tracker = new RepArmTracker();
            var wristInRoom = new TrackedPose(new Vector3(0.0f, 1.4f, 0.0f), facingDownTheStrip);
            tracker.AddSample(new BodySample(1.0, new Vector3(1.0f, HeadHeightMeters, 0.0f), BodyTrackingFidelity.High, null, null, wristInRoom));

            var snapshot = tracker.SnapshotAtJudging(enGardeLine, 1.0);

            var wrist = snapshot.Wrist.Value;
            Assert.That(wrist.Position.X, Is.EqualTo(0.0f).Within(Tolerance));
            Assert.That(wrist.Position.Y, Is.EqualTo(1.4f).Within(Tolerance));
            Assert.That(wrist.Position.Z, Is.EqualTo(1.0f).Within(Tolerance));
            Assert.That(Vector3.Transform(Vector3.UnitZ, wrist.Rotation).Z, Is.EqualTo(1.0f).Within(Tolerance));
            Assert.That(snapshot.Shoulder, Is.Null);
            Assert.That(snapshot.Elbow, Is.Null);
        }

        [Test]
        public void LungeIsMeasuredAlongTheStripNotTheRoom()
        {
            // The Strip runs along room +x: a burst along room +z is sideways on the Strip, so no lunge.
            var (stripAlongRoomX, _) = StripFrame.FromPlacementPoints(Vector3.Zero, new Vector3(4.0f, 0.0f, 0.0f));
            var tracker = new RepArmTracker();
            var judgedAt = FeedRep(tracker, 2.0, _ => 175.0f, BodyTrackingFidelity.High, Lunge(1.0));

            var snapshot = tracker.SnapshotAtJudging(stripAlongRoomX, judgedAt);

            Assert.That(snapshot.LungeStartSecondsBeforeJudging, Is.Null);
        }

        [Test]
        public void NewRepForgetsThePreviousRepsLunge()
        {
            var tracker = new RepArmTracker();
            FeedRep(tracker, 2.0, _ => 175.0f, BodyTrackingFidelity.High, Lunge(1.0));

            tracker.BeginRep();
            var head = new Vector3(0.0f, HeadHeightMeters, 0.5f);
            tracker.AddSample(new BodySample(3.0, head, BodyTrackingFidelity.High, null, null, null));
            tracker.AddSample(new BodySample(3.1, head, BodyTrackingFidelity.High, null, null, null));
            var snapshot = tracker.SnapshotAtJudging(StripAtRoomOrigin, 3.1);

            Assert.That(tracker.SampleCount, Is.EqualTo(2));
            Assert.That(snapshot.LungeStartSecondsBeforeJudging, Is.Null);
        }

        [Test]
        public void SamplesOlderThanTheBufferAreDropped()
        {
            var tracker = new RepArmTracker();
            var head = new Vector3(0.0f, HeadHeightMeters, 0.0f);

            tracker.AddSample(new BodySample(0.0, head, null, null, null, null));
            tracker.AddSample(new BodySample(1.0, head, null, null, null, null));
            tracker.AddSample(new BodySample(RepArmTracker.MaxBufferedSeconds + 0.5, head, null, null, null, null));

            Assert.That(tracker.SampleCount, Is.EqualTo(2));
        }

        [Test]
        public void OutOfOrderSamplesAreIgnored()
        {
            var tracker = new RepArmTracker();
            var head = new Vector3(0.0f, HeadHeightMeters, 0.0f);

            tracker.AddSample(new BodySample(1.0, head, null, null, null, null));
            tracker.AddSample(new BodySample(0.5, head, null, null, null, null));
            tracker.AddSample(new BodySample(1.0, head, null, null, null, null));

            Assert.That(tracker.SampleCount, Is.EqualTo(1));
        }
    }
}
