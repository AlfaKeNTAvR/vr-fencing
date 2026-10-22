// The opponent's body on Nikita's real recording (PER-142), through the whole partner path as "Mirror me" draws it: the
// opponent faces -z, yet its elbows and knees hinge in the tracked planes, its limbs twist exactly as the same body
// facing +z (the Dummy's way), the hands roll with the controller and the back wrist without jumps, the feet stand right
// side up along the heel-to-toe line (the front one toward this Fencer), the head sits on the tracked eyes with the neck
// reaching it, and the fitted bones match the tracked lengths.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Dummy;
using VrFencing.Partner;
using VrFencing.Settings;
using VrFencing.Tests.Dummy;

namespace VrFencing.Tests.Partner
{
    public class OpponentBodyMappingTests
    {
        private const float DegreesPerRadian = (float)(180.0 / Math.PI);

        private static MirrorRecordingReplay _replay;
        private static List<Posed> _posed;
        private static BodyRigRest _rest;

        [OneTimeSetUp]
        public void PlayTheRecording()
        {
            if (_replay != null)
            {
                return;
            }

            _replay = MirrorRecordingReplay.Run();
            _rest = ArmatureRestData.Create(_replay.HeightMeters);
            var opponent = new BodyRigPoser(_rest, true);
            var facingPlusZ = new BodyRigPoser(_rest, true);
            _posed = new List<Posed>();
            foreach (var frame in _replay.Frames)
            {
                var targets = new BodyRigTargets();
                OpponentRigTargets.Fill(targets, frame.View.Body, frame.View.Info.HeightMeters, frame.View.Info.Hand, frame.View.Controller, frame.TimeSeconds);
                opponent.Pose(targets);
                var turned = new BodyRigTargets();
                OpponentRigTargets.Fill(turned, frame.View.Body, frame.View.Info.HeightMeters, frame.View.Info.Hand, frame.View.Controller, frame.TimeSeconds);
                BodyRigPoserTests.TurnHalfWay(turned);
                facingPlusZ.Pose(turned);
                _posed.Add(new Posed(frame, targets, Snapshot(opponent), Snapshot(facingPlusZ), opponent.IsPosed(RigBone.RightUpperArm)));
            }
        }

        [Test]
        public void TheRecordingPlaysThroughThePartnerPath()
        {
            Assert.That(_replay.Hand, Is.EqualTo(Hand.Right));
            Assert.That(_posed.Count, Is.GreaterThan(600), "About 20 s at 36 frames per second.");
            Assert.That(_posed.Count(posed => posed.Frame.View.Controller.HasValue), Is.GreaterThan(_posed.Count * 9 / 10));
            Assert.That(_posed.Count(posed => posed.Frame.View.Body.BackWristRotation.HasValue), Is.GreaterThan(_posed.Count * 9 / 10), "The back wrist's turn comes through.");
        }

        [Test]
        public void TheElbowsHingeInTheTrackedArmPlanes()
        {
            var sword = _posed.Select(posed => ElbowError(posed, false)).ToList();
            var back = _posed.Select(posed => ElbowError(posed, true)).ToList();
            TestContext.WriteLine($"Elbow hinge error, sword arm: max {sword.Max():0.00} deg; back arm: max {back.Max():0.00} deg.");

            Assert.That(sword.Max(), Is.LessThan(1.0f));
            Assert.That(back.Max(), Is.LessThan(1.0f));
        }

        [Test]
        public void TheKneesHingeInTheLegPlanes()
        {
            var front = _posed.Select(posed => KneeError(posed, true)).ToList();
            var back = _posed.Select(posed => KneeError(posed, false)).ToList();
            TestContext.WriteLine($"Knee hinge error, front: max {front.Max():0.00} deg; back: max {back.Max():0.00} deg.");

            Assert.That(front.Max(), Is.LessThan(1.0f));
            Assert.That(back.Max(), Is.LessThan(1.0f));
        }

        [Test]
        public void TheOpponentFacingMinusZIsPosedAsTheSameBodyFacingPlusZ()
        {
            var worst = 0.0f;
            var worstBone = RigBone.Hips;
            foreach (var posed in _posed)
            {
                foreach (RigBone bone in Enum.GetValues(typeof(RigBone)))
                {
                    var difference = BodyRigPoserTests.AngleDegrees(posed.Opponent[(int)bone].Rotation, MirrorRecordingReplay.HalfTurn * posed.FacingPlusZ[(int)bone].Rotation);
                    if (difference > worst)
                    {
                        worst = difference;
                        worstBone = bone;
                    }
                }
            }

            TestContext.WriteLine($"Largest difference between the two facings: {worst:0.00} deg ({worstBone}).");
            Assert.That(worst, Is.LessThan(0.5f));
        }

        [Test]
        public void TheSwordHandRollsWithTheControllerWithoutJumps()
        {
            var worstHand = 0.0f;
            var worstForearm = 0.0f;
            for (var index = 1; index < _posed.Count; index++)
            {
                var before = _posed[index - 1];
                var now = _posed[index];
                if (!before.Frame.View.Controller.HasValue || !now.Frame.View.Controller.HasValue)
                {
                    continue;
                }

                var controllerTurn = BodyRigPoserTests.AngleDegrees(before.Frame.View.Controller.Value.Rotation, now.Frame.View.Controller.Value.Rotation);
                var handTurn = BodyRigPoserTests.AngleDegrees(before.Opponent[(int)RigBone.RightHand].Rotation, now.Opponent[(int)RigBone.RightHand].Rotation);
                var forearmTurn = BodyRigPoserTests.AngleDegrees(before.Opponent[(int)RigBone.RightLowerArm].Rotation, now.Opponent[(int)RigBone.RightLowerArm].Rotation);
                // The forearm also swings with the arm: it may turn as much as its line does, or as the controller rolls.
                var forearmSwing = RigMath.GetAngle(
                    before.Targets.Get(SkeletonJoint.SwordWrist).Value - before.Targets.Get(SkeletonJoint.SwordElbow).Value,
                    now.Targets.Get(SkeletonJoint.SwordWrist).Value - now.Targets.Get(SkeletonJoint.SwordElbow).Value) * DegreesPerRadian;
                worstHand = Math.Max(worstHand, handTurn - controllerTurn);
                worstForearm = Math.Max(worstForearm, forearmTurn - forearmSwing - controllerTurn);
            }

            TestContext.WriteLine($"Sword hand: at most {worstHand:0.0} deg more turn than the controller in a frame; forearm {worstForearm:0.0} deg.");
            Assert.That(worstHand, Is.LessThan(0.5f), "The hand turns with the controller, never a jump on its own.");
            Assert.That(worstForearm, Is.LessThan(5.0f));
        }

        [Test]
        public void TheBackHandRollsWithTheBackWristWithoutJumps()
        {
            var worst = 0.0f;
            for (var index = 1; index < _posed.Count; index++)
            {
                var before = _posed[index - 1].Frame.View.Body.BackWristRotation;
                var now = _posed[index].Frame.View.Body.BackWristRotation;
                if (!before.HasValue || !now.HasValue)
                {
                    continue;
                }

                var wristTurn = BodyRigPoserTests.AngleDegrees(before.Value, now.Value);
                var handTurn = BodyRigPoserTests.AngleDegrees(_posed[index - 1].Opponent[(int)RigBone.LeftHand].Rotation, _posed[index].Opponent[(int)RigBone.LeftHand].Rotation);
                worst = Math.Max(worst, handTurn - wristTurn);
            }

            TestContext.WriteLine($"Back hand: at most {worst:0.0} deg more turn than the tracked wrist in a frame.");
            Assert.That(worst, Is.LessThan(10.0f));
        }

        [Test]
        public void TheFeetStandRightSideUpAlongTheirHeelToToeLines()
        {
            var worstUp = 1.0f;
            var worstHeading = 0.0f;
            var worstFrontTowardMe = 1.0f;
            foreach (var posed in _posed)
            {
                foreach (var isFront in new[] { true, false })
                {
                    var foot = isFront ? RigBone.RightFoot : RigBone.LeftFoot;
                    var turn = posed.Opponent[(int)foot].Rotation * Quaternion.Inverse(_rest.GetRotation(foot));
                    worstUp = Math.Min(worstUp, Vector3.Transform(Vector3.UnitY, turn).Y);
                    var heading = Flatten(Vector3.Transform(Vector3.UnitZ, turn));
                    var ankle = posed.Targets.Get(isFront ? SkeletonJoint.FrontAnkle : SkeletonJoint.BackAnkle).Value;
                    var toe = posed.Targets.Get(isFront ? SkeletonJoint.FrontToe : SkeletonJoint.BackToe).Value;
                    worstHeading = Math.Max(worstHeading, RigMath.GetAngle(heading, Flatten(toe - ankle)) * DegreesPerRadian);
                    if (isFront && posed.Frame.View.Body.Footwork?.Phase == FootworkPhase.Standing)
                    {
                        // The opponent faces -z: its front foot points at this Fencer.
                        worstFrontTowardMe = Math.Min(worstFrontTowardMe, Vector3.Dot(Vector3.Normalize(heading), -Vector3.UnitZ));
                    }
                }
            }

            TestContext.WriteLine($"Feet: lowest up {worstUp:0.0000}, worst heading {worstHeading:0.00} deg, front foot toward me at least {worstFrontTowardMe:0.000}.");
            Assert.That(worstUp, Is.GreaterThan(0.999f), "Never upside down.");
            Assert.That(worstHeading, Is.LessThan(1.0f));
            Assert.That(worstFrontTowardMe, Is.GreaterThan(0.95f));
        }

        [Test]
        public void TheHeadSitsOnTheTrackedEyesAndTheNeckReachesIt()
        {
            // The neck as seen: from its base to the eyes. The tracker's neck joint is its base (FullBody_Neck, index 6).
            var necks = new List<float>();
            var trackedNecks = new List<float>();
            var eyeErrors = new List<float>();
            var neckScales = new List<float>();
            foreach (var posed in _posed)
            {
                var head = posed.Opponent[(int)RigBone.Head];
                var neck = posed.Opponent[(int)RigBone.Neck];

                // The rig's eyes, carried by its head, land on the tracked eyes (the headset), turned into this frame.
                var headTurn = head.Rotation * Quaternion.Inverse(_rest.GetRotation(RigBone.Head));
                var rigEyes = head.Position + Vector3.Transform(_rest.Eyes.Value - _rest.GetPosition(RigBone.Head), headTurn);
                eyeErrors.Add(Vector3.Distance(rigEyes, posed.Targets.Eyes.Value));
                necks.Add(Vector3.Distance(neck.Position, rigEyes));
                neckScales.Add(neck.Scale);
                var recorded = posed.Frame.Recorded;
                trackedNecks.Add(Vector3.Distance(recorded.Joints[6].Value.Position, recorded.Head.Value.Position));
            }

            var neckMedian = Median(necks);
            var trackedMedian = Median(trackedNecks);
            TestContext.WriteLine($"Neck base to eyes: posed median {neckMedian:0.000} m, tracked {trackedMedian:0.000} m; neck bone scale median {Median(neckScales):0.00}; eyes off by at most {eyeErrors.Max() * 1000.0f:0.0} mm.");
            Assert.That(eyeErrors.Max(), Is.LessThan(0.001f));
            Assert.That(Math.Abs(neckMedian - trackedMedian), Is.LessThan(0.03f), "Within a few cm of the tracked neck.");
        }

        [Test]
        public void TheFittedBonesMatchTheTrackedLengths()
        {
            var last = _posed[_posed.Count - 1];
            var sent = _replay.Frames.Select(frame => frame.Sent).ToList();
            var view = _posed.Select(posed => posed.Frame.View.Body).ToList();
            var checks = new (RigBone Bone, RigBone Child, float Tracked)[]
            {
                (RigBone.RightUpperArm, RigBone.RightLowerArm, Median(sent, SkeletonJoint.SwordShoulder, SkeletonJoint.SwordElbow)),
                (RigBone.RightLowerArm, RigBone.RightHand, Median(sent, SkeletonJoint.SwordElbow, SkeletonJoint.SwordWrist)),
                (RigBone.LeftUpperArm, RigBone.LeftLowerArm, Median(sent, SkeletonJoint.BackShoulder, SkeletonJoint.BackElbow)),
                (RigBone.LeftLowerArm, RigBone.LeftHand, Median(sent, SkeletonJoint.BackElbow, SkeletonJoint.BackHand)),
                (RigBone.RightUpperLeg, RigBone.RightLowerLeg, Median(view, SkeletonJoint.FrontHip, SkeletonJoint.FrontKnee)),
                (RigBone.LeftUpperLeg, RigBone.LeftLowerLeg, Median(view, SkeletonJoint.BackHip, SkeletonJoint.BackKnee)),
            };

            var lines = new List<string>();
            foreach (var (bone, child, tracked) in checks)
            {
                var fitted = Vector3.Distance(_rest.GetPosition(bone), _rest.GetPosition(child)) * last.Opponent[(int)bone].Scale;
                lines.Add($"{bone} {fitted:0.000} (tracked {tracked:0.000})");
                Assert.That(fitted, Is.EqualTo(tracked).Within(0.01f), bone.ToString());
            }

            var restTorso = Vector3.Distance(
                (_rest.GetPosition(RigBone.LeftUpperArm) + _rest.GetPosition(RigBone.RightUpperArm)) * 0.5f,
                (_rest.GetPosition(RigBone.LeftUpperLeg) + _rest.GetPosition(RigBone.RightUpperLeg)) * 0.5f);
            var torso = restTorso * last.Opponent[(int)RigBone.Chest].Scale;
            var trackedTorso = Median(sent, SkeletonJoint.HipCenter, SkeletonJoint.ShoulderCenter);
            lines.Add($"torso {torso:0.000} (tracked {trackedTorso:0.000})");
            TestContext.WriteLine("Fitted lengths: " + string.Join(", ", lines));
            Assert.That(torso, Is.EqualTo(trackedTorso).Within(0.01f));
        }

        [Test]
        public void PosingTheOpponentCostsLittle()
        {
            var poser = new BodyRigPoser(_rest, true);
            var targets = new BodyRigTargets();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            const int passes = 10;
            for (var pass = 0; pass < passes; pass++)
            {
                foreach (var frame in _replay.Frames)
                {
                    OpponentRigTargets.Fill(targets, frame.View.Body, frame.View.Info.HeightMeters, frame.View.Info.Hand, frame.View.Controller, frame.TimeSeconds + (pass * 100.0));
                    poser.Pose(targets);
                }
            }

            watch.Stop();
            var microseconds = watch.Elapsed.TotalMilliseconds * 1000.0 / (passes * _replay.Frames.Count);
            TestContext.WriteLine($"Opponent rig targets plus posing: {microseconds:0.0} us per frame.");
            Assert.That(microseconds, Is.LessThan(200.0));
        }

        private static float ElbowError(Posed posed, bool isBack)
        {
            var poser = Replayed(posed);
            return BodyRigPoserTests.GetElbowHingeErrorDegrees(poser, _rest, isBack, posed.Targets);
        }

        private static float KneeError(Posed posed, bool isFront)
        {
            var poser = Replayed(posed);
            return BodyRigPoserTests.GetKneeHingeErrorDegrees(poser, _rest, isFront, posed.Targets);
        }

        /// <summary>A poser holding the frame's pose (the hinge checks read it back from one).</summary>
        private static BodyRigPoser Replayed(Posed posed)
        {
            var poser = new BodyRigPoser(_rest, true);
            poser.Pose(posed.Targets);
            return poser;
        }

        private static RigBonePose[] Snapshot(BodyRigPoser poser)
        {
            var poses = new RigBonePose[BodyRigRest.BoneCount];
            for (var index = 0; index < poses.Length; index++)
            {
                poses[index] = poser.GetPose((RigBone)index);
            }

            return poses;
        }

        private static float Median(IReadOnlyList<FencerBody> bodies, SkeletonJoint from, SkeletonJoint to)
        {
            return Median(bodies.Where(body => body.Get(from).HasValue && body.Get(to).HasValue).Select(body => Vector3.Distance(body.Get(from).Value, body.Get(to).Value)).ToList());
        }

        private static float Median(List<float> values)
        {
            var sorted = values.OrderBy(value => value).ToList();
            return sorted[sorted.Count / 2];
        }

        private static Vector3 Flatten(Vector3 direction)
        {
            return new Vector3(direction.X, 0.0f, direction.Z);
        }

        private sealed class Posed
        {
            public Posed(MirrorRecordingReplay.Frame frame, BodyRigTargets targets, RigBonePose[] opponent, RigBonePose[] facingPlusZ, bool hasArms)
            {
                Frame = frame;
                Targets = targets;
                Opponent = opponent;
                FacingPlusZ = facingPlusZ;
                HasArms = hasArms;
            }

            public MirrorRecordingReplay.Frame Frame { get; }

            public BodyRigTargets Targets { get; }

            public RigBonePose[] Opponent { get; }

            public RigBonePose[] FacingPlusZ { get; }

            public bool HasArms { get; }
        }
    }
}
