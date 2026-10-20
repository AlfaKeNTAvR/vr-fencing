// Partner stream samples for the PER-81 tests: a hand with a French grip and a full body standing in the Dummy's en garde.
using System;
using System.Numerics;
using VrFencing.Dummy;
using VrFencing.Partner;
using VrFencing.Session;
using VrFencing.Settings;
using HandSide = VrFencing.Settings.Hand;

namespace VrFencing.Tests.Partner
{
    internal static class PartnerTestData
    {
        public const float HeightMeters = 1.7f;

        /// <summary>A right hand at <paramref name="position"/>, turned 20 degrees about the vertical, with a French grip.</summary>
        public static HandSample Hand(double timeSeconds, Vector3 position, Vector3 velocity, Vector3 angularVelocity = default)
        {
            var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 20.0f * (float)(Math.PI / 180.0));
            return new HandSample(
                timeSeconds,
                new TrackedPose(position, rotation),
                velocity,
                angularVelocity,
                HandSide.Right,
                BladeMount.French,
                new GripAngles(-13.0f, -4.0f),
                0.008f,
                BladeFlexSoftness.Soft);
        }

        /// <summary>Every joint of a right-handed Fencer in the Dummy's en garde, facing +z, moved by <paramref name="offset"/>.</summary>
        public static FencerBody Body(BodyTrackingFidelity? fidelity, Vector3 offset)
        {
            var layout = DummyLayout.Create(HeightMeters, HandSide.Right);
            var joints = new Vector3?[FencerBody.JointCount];
            foreach (SkeletonJoint joint in Enum.GetValues(typeof(SkeletonJoint)))
            {
                joints[(int)joint] = DummySkeleton.GetJointPosition(layout, joint) + offset;
            }

            joints[FencerBody.BackHandEndIndex] = joints[(int)SkeletonJoint.BackHand] + new Vector3(0.0f, 0.0f, 0.08f);
            return new FencerBody(joints, Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.1f), fidelity);
        }
    }
}
