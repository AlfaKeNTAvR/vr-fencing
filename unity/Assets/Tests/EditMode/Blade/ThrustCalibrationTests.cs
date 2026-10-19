// The thrust calibration (PER-52): three held straight-arm thrusts at targets, and a fit of the grip's pitch and yaw
// that puts the real (drooped) point on the line to each target.
using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Blade;
using VrFencing.Settings;

namespace VrFencing.Tests.Blade
{
    public class ThrustCalibrationTests
    {
        private const double FrameSeconds = 1.0 / 72.0;
        private static readonly TrackedPose Head = new TrackedPose(new Vector3(0.2f, 1.6f, -0.3f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.4f));

        [Test]
        public void TheTargetsStandTwoMetersFromTheShoulderInFrontOfTheHead([Values(Hand.Right, Hand.Left)] Hand hand)
        {
            var calibration = new ThrustCalibration(Head, hand);
            var shoulder = ThrustCalibration.EstimateShoulder(Head, hand);
            var forward = Vector3.Transform(Vector3.UnitZ, Head.Rotation);

            Assert.That(calibration.Targets.Count, Is.EqualTo(ThrustCalibration.TargetCount));
            foreach (var target in calibration.Targets)
            {
                Assert.That(Vector3.Distance(target, shoulder), Is.EqualTo(ThrustCalibration.TargetDistanceMeters).Within(1e-4f));
                Assert.That(Vector3.Dot(Vector3.Normalize(target - shoulder), forward), Is.GreaterThan(0.9f));
            }

            var right = Vector3.Transform(Vector3.UnitX, Head.Rotation);
            var swordSide = Vector3.Dot(shoulder - Head.Position, right);
            Assert.That(hand == Hand.Right ? swordSide : -swordSide, Is.EqualTo(ThrustCalibration.ShoulderSideMeters).Within(1e-4f));
        }

        [Test]
        public void AHeldStraightArmThrustIsTakenAfterOneSecond()
        {
            var calibration = new ThrustCalibration(Head, Hand.Right);
            var controller = AimedController(calibration, 0);
            var time = 0.0;
            var taken = false;
            for (; time < 0.9; time += FrameSeconds)
            {
                taken |= calibration.Step(time, Head, controller);
            }

            Assert.That(taken, Is.False, "Not held long enough yet.");
            Assert.That(calibration.IsAimed, Is.True);
            Assert.That(calibration.HoldFraction, Is.GreaterThan(0.8f));

            for (; time < 1.1 && !taken; time += FrameSeconds)
            {
                taken = calibration.Step(time, Head, controller);
            }

            Assert.That(taken, Is.True);
            Assert.That(calibration.CurrentTargetIndex, Is.EqualTo(1));
            Assert.That(Vector3.Distance(calibration.Captures[0].Controller.Position, controller.Position), Is.LessThan(1e-5f));
            Assert.That(calibration.Captures[0].Target, Is.EqualTo(calibration.Targets[0]));
        }

        [Test]
        public void MovingOrPointingElsewhereRestartsTheHold()
        {
            var calibration = new ThrustCalibration(Head, Hand.Right);
            var aimed = AimedController(calibration, 0);
            var time = 0.0;
            for (var frame = 0; frame < 300; frame++, time += FrameSeconds)
            {
                // Every half second the hand jerks 3 cm: never steady for a whole second.
                var jerk = (frame / 36) % 2 == 0 ? Vector3.Zero : new Vector3(0.0f, 0.03f, 0.0f);
                Assert.That(calibration.Step(time, Head, new TrackedPose(aimed.Position + jerk, aimed.Rotation)), Is.False, $"Frame {frame}.");
            }

            var bentArm = new TrackedPose(ThrustCalibration.EstimateShoulder(Head, Hand.Right) + new Vector3(0.0f, 0.0f, 0.2f), aimed.Rotation);
            for (var frame = 0; frame < 150; frame++, time += FrameSeconds)
            {
                Assert.That(calibration.Step(time, Head, bentArm), Is.False, "A bent arm near the shoulder is no thrust.");
            }

            Assert.That(calibration.IsAimed, Is.False);
            Assert.That(calibration.Captures, Is.Empty);
        }

        [Test]
        public void ThreeHeldThrustsCompleteTheCalibration()
        {
            var calibration = new ThrustCalibration(Head, Hand.Left);
            var time = 0.0;
            for (var target = 0; target < ThrustCalibration.TargetCount; target++)
            {
                var controller = AimedController(calibration, target, Hand.Left);
                var taken = false;
                for (var frame = 0; frame < 200 && !taken; frame++, time += FrameSeconds)
                {
                    taken = calibration.Step(time, Head, controller);
                }

                Assert.That(taken, Is.True, $"Target {target}.");
            }

            Assert.That(calibration.IsComplete, Is.True);
            Assert.That(calibration.Step(time, Head, AimedController(calibration, 0, Hand.Left)), Is.False, "Nothing more is taken.");
        }

        [Test]
        public void TheFitFindsTheMountWhoseDroopedPointLinesUpWithTheTargets(
            [Values(BladeMount.Center, BladeMount.French)] BladeMount mount, [Values(Hand.Right, Hand.Left)] Hand hand)
        {
            var blade = new BladeSettings { Mount = mount };
            var trueAngles = new GripAngles(6.0f, -9.0f);
            var captures = new List<ThrustCapture>();
            foreach (var (pitchRadians, yawRadians) in new[] { (0.1f, 0.3f), (-0.2f, -0.1f), (0.05f, 0.6f) })
            {
                var rotation = Quaternion.CreateFromYawPitchRoll(yawRadians, pitchRadians, 0.2f);
                var controller = new TrackedPose(new Vector3(0.3f, 1.3f, 0.5f), rotation);
                captures.Add(new ThrustCapture(controller, FindRealPointLineTarget(controller, mount, trueAngles, hand, blade)));
            }

            var fit = ThrustCalibration.Fit(captures, blade, hand);

            Assert.That(fit.Angles.PitchDegrees, Is.EqualTo(trueAngles.PitchDegrees).Within(0.2f));
            Assert.That(fit.Angles.YawDegrees, Is.EqualTo(trueAngles.YawDegrees).Within(0.2f));
            Assert.That(fit.SpreadDegrees, Is.LessThan(0.3f), "Consistent thrusts.");
        }

        [Test]
        public void TheFitTakesTheDroopOffSoTheForteSitsAboveTheLineToTheTarget()
        {
            // The hand level and the line from the guard to the target level too: the forte must point a few degrees up
            // for the hanging point to land on that line.
            var blade = new BladeSettings();
            var controller = new TrackedPose(new Vector3(0.0f, 1.3f, 0.0f), Quaternion.Identity);
            var target = new Vector3(0.0f, 1.3f, 2.0f);

            var fit = ThrustCalibration.Fit(new[] { new ThrustCapture(controller, target) }, blade, Hand.Right);

            var droopDegrees = Math.Atan(((4.0f * blade.SetSagittaMeters) + BladeDroop.NormalSagMeters) / BladePose.BladeLengthMeters) * 180.0 / Math.PI;
            Assert.That(fit.Angles.PitchDegrees, Is.EqualTo((float)droopDegrees).Within(0.3f));
            Assert.That(fit.Angles.YawDegrees, Is.EqualTo(0.0f).Within(0.01f));
        }

        [Test]
        public void TheFitMeasuresTheReachFromTheShoulderToTheRealPoint([Values(BladeMount.Center, BladeMount.FrenchPosted)] BladeMount mount)
        {
            // PER-92: the reach sets the Dummy's Extension distance. The point is computed from the controller, so it holds
            // with the blade hidden too.
            var blade = new BladeSettings { Mount = mount };
            var trueAngles = new GripAngles(-8.0f, -3.0f);
            var shoulder = new Vector3(0.18f, 1.4f, 0.0f);
            var captures = new List<ThrustCapture>();
            var expectedSum = 0.0f;
            foreach (var (pitchRadians, yawRadians) in new[] { (0.1f, 0.0f), (-0.15f, 0.25f), (0.05f, -0.25f) })
            {
                var rotation = Quaternion.CreateFromYawPitchRoll(yawRadians, pitchRadians, 0.0f);
                var controller = new TrackedPose(shoulder + Vector3.Transform(new Vector3(0.0f, 0.0f, 0.62f), rotation), rotation);
                var target = FindRealPointLineTarget(controller, mount, trueAngles, Hand.Right, blade);
                captures.Add(new ThrustCapture(controller, target, shoulder));
                expectedSum += Vector3.Distance(FindRealPoint(controller, mount, trueAngles, Hand.Right, blade), shoulder);
            }

            var fit = ThrustCalibration.Fit(captures, blade, Hand.Right);

            Assert.That(fit.ReachMeters, Is.Not.Null);
            Assert.That(fit.ReachMeters.Value, Is.EqualTo(expectedSum / captures.Count).Within(0.005f));
            Assert.That(fit.ReachMeters.Value, Is.InRange(1.4f, 1.8f), "Arm 0.62 m plus a 90 cm blade, about.");
        }

        [Test]
        public void ThrustsWithoutAShoulderMeasureNoReach()
        {
            var controller = new TrackedPose(new Vector3(0.0f, 1.3f, 0.0f), Quaternion.Identity);

            var fit = ThrustCalibration.Fit(new[] { new ThrustCapture(controller, new Vector3(0.0f, 1.3f, 2.0f)) }, new BladeSettings(), Hand.Right);

            Assert.That(fit.ReachMeters, Is.Null);
        }

        [Test]
        public void HeldThrustsCarryTheShoulderForTheReach()
        {
            var calibration = new ThrustCalibration(Head, Hand.Right);
            var controller = AimedController(calibration, 0);
            for (var time = 0.0; time < 1.2 && calibration.Captures.Count == 0; time += FrameSeconds)
            {
                calibration.Step(time, Head, controller);
            }

            Assert.That(calibration.Captures[0].Shoulder, Is.Not.Null);
            Assert.That(
                Vector3.Distance(calibration.Captures[0].Shoulder.Value, ThrustCalibration.EstimateShoulder(Head, Hand.Right)), Is.LessThan(1e-5f));
        }

        [Test]
        public void TheFitNeedsAtLeastOneThrust()
        {
            Assert.Throws<ArgumentException>(() => ThrustCalibration.Fit(Array.Empty<ThrustCapture>(), new BladeSettings(), Hand.Right));
        }

        /// <summary>A target 1.5 m out along the real point's line from the guard, for a mount at <paramref name="angles"/>.</summary>
        private static Vector3 FindRealPointLineTarget(TrackedPose controller, BladeMount mount, GripAngles angles, Hand hand, BladeSettings blade)
        {
            var mountPose = BladePose.GetMountInController(mount, angles, hand);
            var forte = BladePose.GetBlade(controller, mountPose);
            var gripUp = Vector3.Transform(Vector3.UnitY, controller.Rotation * mountPose.Rotation);
            var chord = BladeDroop.Create(forte.Direction, gripUp, -Vector3.UnitY, blade).GetChord(forte);
            return chord.GuardPosition + (chord.Direction * 1.5f);
        }

        /// <summary>The real (drooped) point, room frame, for a mount at <paramref name="angles"/>.</summary>
        private static Vector3 FindRealPoint(TrackedPose controller, BladeMount mount, GripAngles angles, Hand hand, BladeSettings blade)
        {
            var mountPose = BladePose.GetMountInController(mount, angles, hand);
            var forte = BladePose.GetBlade(controller, mountPose);
            var gripUp = Vector3.Transform(Vector3.UnitY, controller.Rotation * mountPose.Rotation);
            return BladeDroop.Create(forte.Direction, gripUp, -Vector3.UnitY, blade).GetChord(forte).TipPosition;
        }

        /// <summary>The controller held on the shoulder's line to the target, 0.6 m out: a straight arm at it.</summary>
        private static TrackedPose AimedController(ThrustCalibration calibration, int target, Hand hand = Hand.Right)
        {
            var shoulder = ThrustCalibration.EstimateShoulder(Head, hand);
            var toTarget = Vector3.Normalize(calibration.Targets[target] - shoulder);
            return new TrackedPose(shoulder + (toTarget * 0.6f), Head.Rotation);
        }
    }
}
