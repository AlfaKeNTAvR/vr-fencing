using System;
using System.Numerics;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Partner
{
    /// <summary>
    /// The receiver's side of the remote Fencer's legs (PER-112, stage 3 of PER-108): it replaces the partner's
    /// generated legs (Meta's Generative Legs, a guess with no front or back foot) with legs built on the sender's feet
    /// (<see cref="FencerBody.Footwork"/>), the same way the Dummy's legs are built (<see cref="LegRig"/>: analytic
    /// two-bone IK with the knees bending as in en garde, the foot pitched about its own axis and never dipping into the
    /// floor). The tracked hips stay where they are; only the knees, ankles and toes are new.
    ///
    /// Feet the sender is unsure of (<see cref="RemoteFootworkTuning.MinConfidence"/>: no head, hips guessed, walking
    /// across the Strip) are not followed: each foot stays planted where it last was sure, and steps to its en garde
    /// place under the hips only once they leave it far behind (<see cref="RemoteFootworkTuning.HeldFootDriftPerHeight"/>).
    /// A body that never came with sure feet stands in en garde under its hips. Whenever a foot's target jumps (sure
    /// feet coming back, a rejoin, a tracking jump), it steps there instead of teleporting.
    ///
    /// In a lunge or recovery a foot the leg cannot reach drags along the floor toward the hips, as the Dummy's back foot
    /// does (<see cref="FootworkTuning.LungeBackLegReachFraction"/>); otherwise an unreachable foot hangs at the leg's
    /// full stretch. Positions in the sender's Strip frame (+x their right, +y up, +z toward their opponent), before the
    /// bout turns them into this Fencer's.
    /// </summary>
    public sealed class RemoteFencerLegs
    {
        private readonly FootTrack _front = new FootTrack();
        private readonly FootTrack _back = new FootTrack();

        private float _heightMeters;
        private Hand _hand;
        private LegRig _frontRig;
        private LegRig _backRig;
        private Vector3 _frontRestFromHips;
        private Vector3 _backRestFromHips;
        private Vector3 _frontHipFromCenter;
        private Vector3 _backHipFromCenter;
        private double? _lastSeconds;
        private bool _hasFeet;
        private bool _wasSure;

        /// <summary>
        /// <paramref name="body"/> with its legs built on its footwork at <paramref name="timeSeconds"/> (this app's
        /// clock, for the steps' timing), for a Fencer of <paramref name="heightMeters"/> holding the blade in
        /// <paramref name="hand"/>. A body with no hips comes back unchanged: there is nothing to hang the legs from.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="body"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The height is not a positive number.</exception>
        public FencerBody Apply(FencerBody body, float heightMeters, Hand hand, double timeSeconds)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            if (!(heightMeters > 0.0f) || float.IsInfinity(heightMeters))
            {
                throw new ArgumentOutOfRangeException(nameof(heightMeters), heightMeters, "Must be a positive number of meters.");
            }
            if (heightMeters != _heightMeters || hand != _hand || _frontRig == null)
            {
                SetProportions(heightMeters, hand);
            }

            var center = body.Get(SkeletonJoint.HipCenter);
            var frontHip = body.Get(SkeletonJoint.FrontHip) ?? center + _frontHipFromCenter;
            var backHip = body.Get(SkeletonJoint.BackHip) ?? center + _backHipFromCenter;
            if (!frontHip.HasValue || !backHip.HasValue)
            {
                return body;
            }

            var hips = center ?? (frontHip.Value + backHip.Value) * 0.5f;
            var deltaSeconds = _lastSeconds.HasValue ? Math.Max(0.0, timeSeconds - _lastSeconds.Value) : 0.0;
            _lastSeconds = timeSeconds;

            var frontPlace = Place(hips + _frontRestFromHips);
            var backPlace = Place(hips + _backRestFromHips);
            var footwork = body.Footwork;
            var isSure = footwork.HasValue && footwork.Value.Confidence >= RemoteFootworkTuning.MinConfidence;
            if (!_hasFeet)
            {
                _front.StandAt(isSure ? footwork.Value.Front : frontPlace);
                _back.StandAt(isSure ? footwork.Value.Back : backPlace);
                _wasSure = isSure;
                _hasFeet = true;
            }

            var isModeChanged = isSure != _wasSure;
            _wasSure = isSure;
            if (isSure)
            {
                _front.RememberSure(footwork.Value.Front);
                _back.RememberSure(footwork.Value.Back);
            }

            var drift = RemoteFootworkTuning.HeldFootDriftPerHeight * heightMeters;
            var frontTarget = isSure ? footwork.Value.Front : _front.GetHeldTarget(frontPlace, drift);
            var backTarget = isSure ? footwork.Value.Back : _back.GetHeldTarget(backPlace, drift);
            var lift = FootworkTuning.FootLiftPerHeight * heightMeters;
            var front = _front.Follow(frontTarget, isModeChanged, deltaSeconds, lift);
            var back = _back.Follow(backTarget, isModeChanged, deltaSeconds, lift);

            var mayDrag = isSure && (footwork.Value.Phase == FootworkPhase.Lunging || footwork.Value.Phase == FootworkPhase.Recovering);
            var joints = new Vector3?[FencerBody.JointCount];
            for (var index = 0; index < FencerBody.JointCount; index++)
            {
                joints[index] = body.Get(index);
            }

            var frontLeg = PoseLeg(_frontRig, frontHip.Value, front, mayDrag);
            var backLeg = PoseLeg(_backRig, backHip.Value, back, mayDrag);
            joints[(int)SkeletonJoint.FrontKnee] = frontLeg.Knee;
            joints[(int)SkeletonJoint.FrontAnkle] = frontLeg.Ankle;
            joints[(int)SkeletonJoint.FrontToe] = frontLeg.Toe;
            joints[(int)SkeletonJoint.BackKnee] = backLeg.Knee;
            joints[(int)SkeletonJoint.BackAnkle] = backLeg.Ankle;
            joints[(int)SkeletonJoint.BackToe] = backLeg.Toe;
            return new FencerBody(joints, body.HeadRotation, body.Fidelity, footwork);
        }

        /// <summary>Forgets the feet (the partner left or rejoined): the next body stands in en garde under its hips.</summary>
        public void Reset()
        {
            _hasFeet = false;
            _wasSure = false;
            _lastSeconds = null;
        }

        private static FootPlacement Place(Vector3 spot)
        {
            return new FootPlacement(spot.X, spot.Z, 0.0f, 0.0f);
        }

        private static float GetFloorDistance(FootPlacement a, FootPlacement b)
        {
            return new Vector2(a.X - b.X, a.Z - b.Z).Length();
        }

        private void SetProportions(float heightMeters, Hand hand)
        {
            _heightMeters = heightMeters;
            _hand = hand;
            var unit = DummyLegs.EnGarde(1.0f, Hand.Right);
            _frontRig = new LegRig(unit.Front, heightMeters);
            _backRig = new LegRig(unit.Back, heightMeters);

            var legs = DummyLegs.EnGarde(heightMeters, hand);
            var center = (legs.Front.Hip + legs.Back.Hip) * 0.5f;
            _frontRestFromHips = OnFloor(legs.Front.Ankle - center);
            _backRestFromHips = OnFloor(legs.Back.Ankle - center);
            _frontHipFromCenter = legs.Front.Hip - center;
            _backHipFromCenter = legs.Back.Hip - center;
            _hasFeet = false;
        }

        private static Vector3 OnFloor(Vector3 point)
        {
            return new Vector3(point.X, 0.0f, point.Z);
        }

        /// <summary>The leg from the tracked hip to the foot, solved right-handed (mirrored in and out for a left-hander).</summary>
        private LegPose PoseLeg(LegRig rig, Vector3 hip, FootPlacement foot, bool mayDrag)
        {
            var isLeft = _hand == Hand.Left;
            var hipRight = isLeft ? HandMirroring.MirrorPosition(hip) : hip;
            var footX = isLeft ? -foot.X : foot.X;
            var footZ = foot.Z;
            var hipOffset = hipRight - rig.Rest.Hip;
            if (mayDrag)
            {
                var reach = rig.GetFloorReachMeters(hipOffset, FootworkTuning.LungeBackLegReachFraction, footX - rig.Rest.Ankle.X);
                footZ = Math.Min(Math.Max(footZ, hipRight.Z - reach), hipRight.Z + reach);
            }

            var ankleOffset = new Vector3(footX - rig.Rest.Ankle.X, foot.LiftMeters, footZ - rig.Rest.Ankle.Z);
            var leg = rig.Pose(hipOffset, ankleOffset, foot.PitchRadians);
            return isLeft ? leg.ScaledForHand(1.0f, Hand.Left) : leg;
        }

        /// <summary>One foot as drawn: where it is now, the last spot it was sure of, and a step under way to a jumped target.</summary>
        private sealed class FootTrack
        {
            private FootPlacement _current;
            private FootPlacement _lastTarget;
            private FootPlacement _held;
            private FootPlacement _replantFrom;
            private double _replantSeconds = double.PositiveInfinity;

            public void StandAt(FootPlacement spot)
            {
                _current = spot;
                _lastTarget = spot;
                _held = new FootPlacement(spot.X, spot.Z, 0.0f, 0.0f);
                _replantSeconds = double.PositiveInfinity;
            }

            /// <summary>The floor spot of a foot the sender is sure of: where it stays if the sender stops being sure.</summary>
            public void RememberSure(FootPlacement foot)
            {
                _held = new FootPlacement(foot.X, foot.Z, 0.0f, 0.0f);
            }

            /// <summary>
            /// Where an unsure foot goes: the floor spot it was last sure of, or its en garde place under the hips once the
            /// hips have left that spot more than <paramref name="driftMeters"/> behind.
            /// </summary>
            public FootPlacement GetHeldTarget(FootPlacement place, float driftMeters)
            {
                if (GetFloorDistance(_held, place) > driftMeters)
                {
                    _held = place;
                }

                return _held;
            }

            /// <summary>
            /// This frame's foot, following <paramref name="target"/>: straight on while it moves as a foot can, or in a
            /// step of <see cref="RemoteFootworkTuning.ReplantSeconds"/> (lifting up to <paramref name="liftMeters"/>)
            /// after it jumps or the foot changes between sure and held.
            /// </summary>
            public FootPlacement Follow(FootPlacement target, bool isModeChanged, double deltaSeconds, float liftMeters)
            {
                var isJump = GetFloorDistance(target, _lastTarget) > RemoteFootworkTuning.ReplantJumpMeters;
                _lastTarget = target;
                if (isModeChanged || isJump)
                {
                    _replantFrom = _current;
                    _replantSeconds = 0.0;
                }

                if (_replantSeconds < RemoteFootworkTuning.ReplantSeconds)
                {
                    _replantSeconds += deltaSeconds;
                    var fraction = (float)(_replantSeconds / RemoteFootworkTuning.ReplantSeconds);
                    var stepped = FootPlacement.Lerp(_replantFrom, target, DummyFootwork.MinimumJerk(fraction));
                    var stride = Math.Min(1.0f, GetFloorDistance(_replantFrom, target) / RemoteFootworkTuning.ReplantJumpMeters);
                    _current = new FootPlacement(
                        stepped.X,
                        stepped.Z,
                        stepped.LiftMeters + (liftMeters * stride * DummyFootwork.SwingBump(fraction)),
                        stepped.PitchRadians);
                    return _current;
                }

                _current = target;
                return _current;
            }
        }
    }
}
