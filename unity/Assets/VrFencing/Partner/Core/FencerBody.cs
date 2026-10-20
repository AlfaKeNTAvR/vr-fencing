using System;
using System.Collections.Generic;
using System.Numerics;
using VrFencing.Dummy;
using VrFencing.Session;
using VrFencing.Settings;

namespace VrFencing.Partner
{
    /// <summary>
    /// A Fencer's tracked body reduced to the joints the rigged body is posed on (PER-81): the same
    /// <see cref="SkeletonJoint"/>s the Dummy's rig uses (PER-78), named by fencing side, plus the back hand's end
    /// (the Dummy's back hand has none) and the head's turn. This is what goes over the network at 30 per second
    /// (about 4 KB/s), instead of all 84 tracked joints. Positions in one frame (a Strip frame), meters; a joint the
    /// tracker did not give is null.
    /// </summary>
    public sealed class FencerBody
    {
        /// <summary>Joints held per body: every <see cref="SkeletonJoint"/>, then <see cref="BackHandEndIndex"/>.</summary>
        public static readonly int JointCount = Enum.GetValues(typeof(SkeletonJoint)).Length + 1;

        /// <summary>The slot after the <see cref="SkeletonJoint"/>s: the back hand's end (its middle knuckle).</summary>
        public static readonly int BackHandEndIndex = JointCount - 1;

        private readonly Vector3?[] _joints;

        /// <param name="joints">One entry per <see cref="JointCount"/>; copied.</param>
        /// <param name="footwork">What the feet are doing, as the sender read it (PER-112); null when unknown.</param>
        public FencerBody(IReadOnlyList<Vector3?> joints, Quaternion? headRotation, BodyTrackingFidelity? fidelity, FootworkState? footwork = null)
        {
            if (joints == null)
            {
                throw new ArgumentNullException(nameof(joints));
            }

            if (joints.Count != JointCount)
            {
                throw new ArgumentException($"A body holds {JointCount} joints, not {joints.Count}.", nameof(joints));
            }

            _joints = new Vector3?[JointCount];
            for (var index = 0; index < JointCount; index++)
            {
                _joints[index] = joints[index];
            }

            HeadRotation = headRotation;
            Fidelity = fidelity;
            Footwork = footwork;
        }

        /// <summary>The headset's turn (its +z where the Fencer looks, +y up), or null when not tracked.</summary>
        public Quaternion? HeadRotation { get; }

        /// <summary>The body tracker's fidelity when this body was read; null when it gave no body (only the head then).</summary>
        public BodyTrackingFidelity? Fidelity { get; }

        /// <summary>
        /// The sender's footwork state read with this body (PER-112), which the receiver builds the legs on
        /// (<see cref="RemoteFencerLegs"/>); null when the body came without one.
        /// </summary>
        public FootworkState? Footwork { get; }

        public Vector3? Get(SkeletonJoint joint)
        {
            return _joints[(int)joint];
        }

        /// <summary>The joint in slot <paramref name="index"/> (a <see cref="SkeletonJoint"/>, or <see cref="BackHandEndIndex"/>).</summary>
        public Vector3? Get(int index)
        {
            return _joints[index];
        }

        public Vector3? BackHandEnd => _joints[BackHandEndIndex];

        /// <summary>The same body carrying <paramref name="footwork"/> (the sender's state, to send with it).</summary>
        public FencerBody WithFootwork(FootworkState? footwork)
        {
            return new FencerBody(_joints, HeadRotation, Fidelity, footwork);
        }

        /// <summary>
        /// The same body with every position (the footwork's feet too) moved by <paramref name="movePoint"/> and the head
        /// turned by <paramref name="turn"/> (applied first), e.g. into the other Fencer's Strip frame.
        /// </summary>
        public FencerBody Transform(Func<Vector3, Vector3> movePoint, Quaternion turn)
        {
            if (movePoint == null)
            {
                throw new ArgumentNullException(nameof(movePoint));
            }

            var moved = new Vector3?[JointCount];
            for (var index = 0; index < JointCount; index++)
            {
                moved[index] = _joints[index].HasValue ? movePoint(_joints[index].Value) : (Vector3?)null;
            }

            var head = HeadRotation.HasValue ? Quaternion.Normalize(turn * HeadRotation.Value) : (Quaternion?)null;
            return new FencerBody(moved, head, Fidelity, Footwork?.Transform(movePoint));
        }

        /// <summary>
        /// Between <paramref name="from"/> (0) and <paramref name="to"/> (1); a joint (or footwork state) only one of them
        /// has is taken from it.
        /// </summary>
        public static FencerBody Lerp(FencerBody from, FencerBody to, float fraction)
        {
            if (from == null || to == null)
            {
                throw new ArgumentNullException(from == null ? nameof(from) : nameof(to));
            }

            var joints = new Vector3?[JointCount];
            for (var index = 0; index < JointCount; index++)
            {
                var a = from._joints[index];
                var b = to._joints[index];
                joints[index] = a.HasValue && b.HasValue ? Vector3.Lerp(a.Value, b.Value, fraction) : b ?? a;
            }

            Quaternion? head;
            if (from.HeadRotation.HasValue && to.HeadRotation.HasValue)
            {
                head = Quaternion.Slerp(from.HeadRotation.Value, to.HeadRotation.Value, fraction);
            }
            else
            {
                head = to.HeadRotation ?? from.HeadRotation;
            }

            FootworkState? footwork;
            if (from.Footwork.HasValue && to.Footwork.HasValue)
            {
                footwork = FootworkState.Lerp(from.Footwork.Value, to.Footwork.Value, fraction);
            }
            else
            {
                footwork = to.Footwork ?? from.Footwork;
            }

            return new FencerBody(joints, head, fraction < 0.5f ? from.Fidelity ?? to.Fidelity : to.Fidelity ?? from.Fidelity, footwork);
        }
    }

    /// <summary>
    /// Reads a <see cref="FencerBody"/> from OVRPlugin's Full Body skeleton (84 joints, PER-79), the joint set the body
    /// tracker gives and a Fencer Recording keeps. Indices are <c>OVRPlugin.BoneId.FullBody_*</c>; the Core cannot name
    /// that enum, so they are kept here as numbers and checked against it by an Edit-mode test.
    /// </summary>
    public static class FullBodyJoints
    {
        public const int Hips = 1;
        public const int Head = 7;
        public const int LeftArmUpper = 10;
        public const int LeftArmLower = 11;
        public const int RightArmUpper = 15;
        public const int RightArmLower = 16;
        public const int LeftHandWrist = 19;
        public const int LeftHandMiddleProximal = 30;
        public const int RightHandWrist = 45;
        public const int RightHandMiddleProximal = 56;
        public const int LeftUpperLeg = 70;
        public const int LeftLowerLeg = 71;
        public const int LeftFootAnkle = 73;
        public const int LeftFootBall = 76;
        public const int RightUpperLeg = 77;
        public const int RightLowerLeg = 78;
        public const int RightFootAnkle = 80;
        public const int RightFootBall = 83;

        /// <summary>Joints in the Full Body set.</summary>
        public const int Count = 84;

        /// <summary>
        /// How far the head's center sits above the tracker's head joint (the base of the skull), per meter of height: the
        /// inverse of the rig's own offset, so the rig's head joint lands on the tracker's.
        /// </summary>
        public const float HeadCenterAboveHeadJointPerHeight = 0.02f;

        /// <summary>
        /// The body the rig is posed on, from one frame of the Full Body skeleton. The sword side is the anatomical right
        /// for a right-handed Fencer. <paramref name="headRotation"/> is the headset's.
        /// </summary>
        public static FencerBody Read(
            IReadOnlyList<TrackedPose?> fullBody,
            Quaternion? headRotation,
            Hand hand,
            float fencerHeightMeters,
            BodyTrackingFidelity? fidelity)
        {
            if (fullBody == null)
            {
                throw new ArgumentNullException(nameof(fullBody));
            }

            var joints = new Vector3?[FencerBody.JointCount];
            if (fullBody.Count < Count || !fidelity.HasValue)
            {
                return new FencerBody(joints, headRotation, fidelity);
            }

            Vector3? At(int index) => fullBody[index]?.Position;
            var isRight = hand != Hand.Left;

            joints[(int)SkeletonJoint.HipCenter] = Midpoint(At(LeftUpperLeg), At(RightUpperLeg)) ?? At(Hips);
            joints[(int)SkeletonJoint.ShoulderCenter] = Midpoint(At(LeftArmUpper), At(RightArmUpper));
            var head = At(Head);
            joints[(int)SkeletonJoint.HeadCenter] = head.HasValue
                ? head.Value + new Vector3(0.0f, HeadCenterAboveHeadJointPerHeight * fencerHeightMeters, 0.0f)
                : (Vector3?)null;

            joints[(int)SkeletonJoint.SwordShoulder] = At(isRight ? RightArmUpper : LeftArmUpper);
            joints[(int)SkeletonJoint.SwordElbow] = At(isRight ? RightArmLower : LeftArmLower);
            joints[(int)SkeletonJoint.SwordWrist] = At(isRight ? RightHandWrist : LeftHandWrist);
            joints[(int)SkeletonJoint.SwordHandEnd] = At(isRight ? RightHandMiddleProximal : LeftHandMiddleProximal);
            joints[(int)SkeletonJoint.BackShoulder] = At(isRight ? LeftArmUpper : RightArmUpper);
            joints[(int)SkeletonJoint.BackElbow] = At(isRight ? LeftArmLower : RightArmLower);
            joints[(int)SkeletonJoint.BackHand] = At(isRight ? LeftHandWrist : RightHandWrist);
            joints[FencerBody.BackHandEndIndex] = At(isRight ? LeftHandMiddleProximal : RightHandMiddleProximal);

            joints[(int)SkeletonJoint.FrontHip] = At(isRight ? RightUpperLeg : LeftUpperLeg);
            joints[(int)SkeletonJoint.FrontKnee] = At(isRight ? RightLowerLeg : LeftLowerLeg);
            joints[(int)SkeletonJoint.FrontAnkle] = At(isRight ? RightFootAnkle : LeftFootAnkle);
            joints[(int)SkeletonJoint.FrontToe] = At(isRight ? RightFootBall : LeftFootBall);
            joints[(int)SkeletonJoint.BackHip] = At(isRight ? LeftUpperLeg : RightUpperLeg);
            joints[(int)SkeletonJoint.BackKnee] = At(isRight ? LeftLowerLeg : RightLowerLeg);
            joints[(int)SkeletonJoint.BackAnkle] = At(isRight ? LeftFootAnkle : RightFootAnkle);
            joints[(int)SkeletonJoint.BackToe] = At(isRight ? LeftFootBall : RightFootBall);
            return new FencerBody(joints, headRotation, fidelity);
        }

        private static Vector3? Midpoint(Vector3? a, Vector3? b)
        {
            return a.HasValue && b.HasValue ? (a.Value + b.Value) * 0.5f : (Vector3?)null;
        }
    }

    /// <summary>
    /// A tracked body as capsules (PER-81), sized like the Dummy's (<see cref="EnGardePose"/>) for the Fencer's height:
    /// what a drawn blade bends against. Always <see cref="CapsuleCount"/> capsules in the same order, since the drawn
    /// blade remembers the body it is pinned on by its place in the list; a part whose joints are missing is a point
    /// far under the floor, where no blade reaches.
    /// </summary>
    public static class FencerBodyShape
    {
        /// <summary>Head, torso, two upper arms, two forearms, two hands, two thighs, two shins, two feet.</summary>
        public const int CapsuleCount = 14;

        /// <summary>The body part of each capsule <see cref="GetCapsules"/> gives, in the same order.</summary>
        public static IReadOnlyList<BodyPart> CapsuleParts { get; } = new[]
        {
            BodyPart.Head,
            BodyPart.Torso,
            BodyPart.SwordArm,
            BodyPart.BackArm,
            BodyPart.SwordArm,
            BodyPart.BackArm,
            BodyPart.SwordHand,
            BodyPart.BackArm,
            BodyPart.FrontThigh,
            BodyPart.BackLeg,
            BodyPart.FrontLowerLeg,
            BodyPart.BackLeg,
            BodyPart.FrontLowerLeg,
            BodyPart.BackLeg,
        };

        /// <summary>Where a missing part is parked: far below the floor.</summary>
        public static readonly Vector3 ParkedPosition = new Vector3(0.0f, -100.0f, 0.0f);

        /// <summary>A capsule nothing can touch, standing in for a missing part.</summary>
        public static Capsule Parked => new Capsule(ParkedPosition, ParkedPosition, 0.0f);

        /// <summary>True for <see cref="Parked"/>: a part whose joints were missing.</summary>
        public static bool IsParked(Capsule capsule)
        {
            return capsule.Start == ParkedPosition && capsule.End == ParkedPosition && capsule.RadiusMeters == 0.0f;
        }

        /// <summary>Fills <paramref name="capsules"/> (cleared first) with the body's <see cref="CapsuleCount"/> capsules.</summary>
        public static void GetCapsules(FencerBody body, float fencerHeightMeters, List<Capsule> capsules)
        {
            if (capsules == null)
            {
                throw new ArgumentNullException(nameof(capsules));
            }

            capsules.Clear();
            if (body == null)
            {
                for (var index = 0; index < CapsuleCount; index++)
                {
                    capsules.Add(Parked);
                }

                return;
            }

            var height = fencerHeightMeters;
            capsules.Add(Part(body.Get(SkeletonJoint.HeadCenter), body.Get(SkeletonJoint.HeadCenter), EnGardePose.HeadRadius * height));
            capsules.Add(Part(body.Get(SkeletonJoint.HipCenter), body.Get(SkeletonJoint.ShoulderCenter), EnGardePose.TorsoRadius * height));
            capsules.Add(Part(body.Get(SkeletonJoint.SwordShoulder), body.Get(SkeletonJoint.SwordElbow), EnGardePose.UpperArmRadius * height));
            capsules.Add(Part(body.Get(SkeletonJoint.BackShoulder), body.Get(SkeletonJoint.BackElbow), EnGardePose.UpperArmRadius * height));
            capsules.Add(Part(body.Get(SkeletonJoint.SwordElbow), body.Get(SkeletonJoint.SwordWrist), EnGardePose.ForearmRadius * height));
            capsules.Add(Part(body.Get(SkeletonJoint.BackElbow), body.Get(SkeletonJoint.BackHand), EnGardePose.ForearmRadius * height));
            capsules.Add(Part(body.Get(SkeletonJoint.SwordWrist), body.Get(SkeletonJoint.SwordHandEnd), EnGardePose.HandRadius * height));
            capsules.Add(Part(body.Get(SkeletonJoint.BackHand), body.BackHandEnd, EnGardePose.HandRadius * height));
            capsules.Add(Part(body.Get(SkeletonJoint.FrontHip), body.Get(SkeletonJoint.FrontKnee), EnGardePose.ThighRadius * height));
            capsules.Add(Part(body.Get(SkeletonJoint.BackHip), body.Get(SkeletonJoint.BackKnee), EnGardePose.ThighRadius * height));
            capsules.Add(Part(body.Get(SkeletonJoint.FrontKnee), body.Get(SkeletonJoint.FrontAnkle), EnGardePose.ShinRadius * height));
            capsules.Add(Part(body.Get(SkeletonJoint.BackKnee), body.Get(SkeletonJoint.BackAnkle), EnGardePose.ShinRadius * height));
            capsules.Add(Part(body.Get(SkeletonJoint.FrontAnkle), body.Get(SkeletonJoint.FrontToe), EnGardePose.FootRadius * height));
            capsules.Add(Part(body.Get(SkeletonJoint.BackAnkle), body.Get(SkeletonJoint.BackToe), EnGardePose.FootRadius * height));
        }

        private static Capsule Part(Vector3? start, Vector3? end, float radiusMeters)
        {
            return start.HasValue && end.HasValue ? new Capsule(start.Value, end.Value, radiusMeters) : Parked;
        }
    }
}
