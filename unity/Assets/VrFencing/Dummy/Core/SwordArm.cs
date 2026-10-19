using System;
using System.Numerics;
using VrFencing.Settings;

namespace VrFencing.Dummy
{
    /// <summary>The joints of the Dummy's sword arm, from the body out. Each one is the parent of the next.</summary>
    public enum ArmJoint
    {
        Shoulder,
        Elbow,
        Wrist,
    }

    /// <summary>
    /// The sword arm's pose as joint rotations. Every arm segment lies along its joint's +z axis, so with all
    /// rotations at identity the arm points straight along the Dummy's +z (at the Fencer).
    /// The MVP Dummy only uses <see cref="ArmInLine"/>; an armed Dummy can later thrust, parry or lunge by passing
    /// other poses, and the arm Target Zones follow without rebuilding the Dummy.
    /// Rotations are for a right-handed Dummy; a left-handed one mirrors them (see <see cref="SwordArm"/>).
    /// </summary>
    public readonly struct SwordArmPose
    {
        public SwordArmPose(Quaternion shoulder, Quaternion elbow, Quaternion wrist)
        {
            Shoulder = shoulder;
            Elbow = elbow;
            Wrist = wrist;
        }

        /// <summary>Upper arm orientation in the Dummy frame.</summary>
        public Quaternion Shoulder { get; }

        /// <summary>Forearm orientation relative to the upper arm.</summary>
        public Quaternion Elbow { get; }

        /// <summary>Hand orientation relative to the forearm.</summary>
        public Quaternion Wrist { get; }

        /// <summary>En garde with the arm in line: straight from the shoulder toward the Fencer.</summary>
        public static SwordArmPose ArmInLine { get; } = new SwordArmPose(
            RotationFromForwardTo(EnGardePose.ArmInLineDirection), Quaternion.Identity, Quaternion.Identity);

        /// <summary>
        /// The same pose with the elbow bent by <paramref name="radians"/> about its own x axis.
        /// Positive bends the forearm up from an in-line arm.
        /// </summary>
        public SwordArmPose WithElbowBend(float radians)
        {
            return new SwordArmPose(Shoulder, Quaternion.CreateFromAxisAngle(-Vector3.UnitX, radians), Wrist);
        }

        private static Quaternion RotationFromForwardTo(Vector3 direction)
        {
            var target = Vector3.Normalize(direction);
            var axis = Vector3.Cross(Vector3.UnitZ, target);
            var angle = (float)Math.Acos(Math.Clamp(Vector3.Dot(Vector3.UnitZ, target), -1.0f, 1.0f));
            return axis.LengthSquared() < 1e-12f ? Quaternion.Identity : Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), angle);
        }
    }

    /// <summary>
    /// The sword arm as a joint chain (shoulder, elbow, wrist) for one height, hand and <see cref="SwordArmPose"/>:
    /// each joint's pose relative to its parent, the same in the Dummy frame, and each arm Target Zone as a capsule
    /// in the frame of the joint it is attached to. The Unity layer builds the same chain as nested transforms.
    ///
    /// Attachments: upper arm on the shoulder, forearm on the elbow, wrist and hand on the wrist.
    /// </summary>
    public sealed class SwordArm
    {
        private readonly TrackedPose[] _localPoses;
        private readonly TrackedPose[] _poses;
        private readonly float _heightMeters;

        /// <exception cref="ArgumentException">A rotation in <paramref name="pose"/> is zero (an unset pose).</exception>
        public SwordArm(float heightMeters, Hand hand, SwordArmPose pose)
        {
            DummyLayout.RequirePositiveHeight(heightMeters);
            RequireRotation(pose.Shoulder, nameof(pose.Shoulder));
            RequireRotation(pose.Elbow, nameof(pose.Elbow));
            RequireRotation(pose.Wrist, nameof(pose.Wrist));

            _heightMeters = heightMeters;
            Hand = hand;
            Pose = pose;

            _localPoses = new[]
            {
                new TrackedPose(EnGardePose.SwordShoulder * heightMeters, Quaternion.Normalize(pose.Shoulder)),
                new TrackedPose(new Vector3(0.0f, 0.0f, EnGardePose.UpperArmLength * heightMeters), Quaternion.Normalize(pose.Elbow)),
                new TrackedPose(new Vector3(0.0f, 0.0f, EnGardePose.ForearmLength * heightMeters), Quaternion.Normalize(pose.Wrist)),
            };
            for (var joint = 0; joint < _localPoses.Length; joint++)
            {
                _localPoses[joint] = HandMirroring.ForHand(_localPoses[joint], hand);
            }

            _poses = new TrackedPose[_localPoses.Length];
            _poses[0] = _localPoses[0];
            for (var joint = 1; joint < _localPoses.Length; joint++)
            {
                _poses[joint] = Compose(_poses[joint - 1], _localPoses[joint]);
            }
        }

        public Hand Hand { get; }

        public SwordArmPose Pose { get; }

        /// <summary>The joint's pose in its parent's frame (the Dummy frame for the shoulder).</summary>
        public TrackedPose GetLocalPose(ArmJoint joint)
        {
            return _localPoses[(int)joint];
        }

        /// <summary>The joint's pose in the Dummy frame.</summary>
        public TrackedPose GetPose(ArmJoint joint)
        {
            return _poses[(int)joint];
        }

        /// <summary>Whether <paramref name="zone"/> is on the sword arm (hand, wrist, forearm or upper arm).</summary>
        public static bool IsArmZone(TargetZone zone)
        {
            return zone == TargetZone.Hand || zone == TargetZone.Wrist || zone == TargetZone.Forearm || zone == TargetZone.UpperArm;
        }

        /// <exception cref="ArgumentOutOfRangeException"><paramref name="zone"/> is not an arm zone.</exception>
        public static ArmJoint GetAttachedJoint(TargetZone zone)
        {
            switch (zone)
            {
                case TargetZone.UpperArm:
                    return ArmJoint.Shoulder;
                case TargetZone.Forearm:
                    return ArmJoint.Elbow;
                case TargetZone.Wrist:
                case TargetZone.Hand:
                    return ArmJoint.Wrist;
                default:
                    throw new ArgumentOutOfRangeException(nameof(zone), zone, "Not an arm Target Zone.");
            }
        }

        /// <summary>
        /// An arm zone's capsule in the frame of <see cref="GetAttachedJoint"/>. It lies on the joint's z axis, so it
        /// is the same for either hand.
        /// </summary>
        public Capsule GetZoneInJointFrame(TargetZone zone)
        {
            switch (zone)
            {
                case TargetZone.UpperArm:
                    return AlongJoint(0.0f, EnGardePose.UpperArmLength, EnGardePose.UpperArmRadius);
                case TargetZone.Forearm:
                    return AlongJoint(0.0f, EnGardePose.ForearmLength - EnGardePose.WristHalfWidth, EnGardePose.ForearmRadius);
                case TargetZone.Wrist:
                    return AlongJoint(-EnGardePose.WristHalfWidth, EnGardePose.WristHalfWidth, EnGardePose.WristRadius);
                case TargetZone.Hand:
                    return AlongJoint(EnGardePose.WristHalfWidth, EnGardePose.HandLength, EnGardePose.HandRadius);
                default:
                    throw new ArgumentOutOfRangeException(nameof(zone), zone, "Not an arm Target Zone.");
            }
        }

        /// <summary>An arm zone's capsule in the Dummy frame, at the current joint poses.</summary>
        public Capsule GetZone(TargetZone zone)
        {
            var joint = GetPose(GetAttachedJoint(zone));
            var local = GetZoneInJointFrame(zone);
            return new Capsule(
                joint.Position + Vector3.Transform(local.Start, joint.Rotation),
                joint.Position + Vector3.Transform(local.End, joint.Rotation),
                local.RadiusMeters);
        }

        private static TrackedPose Compose(TrackedPose parent, TrackedPose child)
        {
            // Concatenate(first, second) applies the child's rotation, then the parent's.
            return new TrackedPose(
                parent.Position + Vector3.Transform(child.Position, parent.Rotation),
                Quaternion.Normalize(Quaternion.Concatenate(child.Rotation, parent.Rotation)));
        }

        /// <summary>
        /// The capsule covering the joint's z axis from <paramref name="from"/> to <paramref name="to"/> (fractions of
        /// height): its rounded ends are pulled in by the radius so the surface spans the section. A section shorter
        /// than its diameter becomes a sphere at its middle.
        /// </summary>
        private Capsule AlongJoint(float from, float to, float radius)
        {
            var start = from + radius;
            var end = to - radius;
            if (end < start)
            {
                start = end = (from + to) * 0.5f;
            }

            return new Capsule(
                new Vector3(0.0f, 0.0f, start * _heightMeters),
                new Vector3(0.0f, 0.0f, end * _heightMeters),
                radius * _heightMeters);
        }

        private static void RequireRotation(Quaternion rotation, string name)
        {
            if (!(rotation.LengthSquared() > 1e-6f))
            {
                throw new ArgumentException($"The {name} rotation is zero; use SwordArmPose.ArmInLine for the default pose.", name);
            }
        }
    }
}
