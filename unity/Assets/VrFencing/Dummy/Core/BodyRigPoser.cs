using System;
using System.Numerics;
using VrFencing.Settings;

namespace VrFencing.Dummy
{
    /// <summary>
    /// What a rigged body is posed on for one frame (PER-142): the <see cref="SkeletonJoint"/>s, named by fencing side for
    /// a Fencer holding the blade in <see cref="Hand"/>, and the extras the joints alone do not give. Positions and
    /// directions in the parts frame; anything unknown is null. Filled again every frame; nothing is allocated.
    /// </summary>
    public sealed class BodyRigTargets
    {
        public static readonly int JointCount = Enum.GetValues(typeof(SkeletonJoint)).Length;

        private readonly Vector3?[] _joints = new Vector3?[JointCount];

        public Hand Hand { get; set; }

        /// <summary>This frame's time, for the length fit's sampling (any clock that goes forward).</summary>
        public double TimeSeconds { get; set; }

        /// <summary>The back hand's end (its middle knuckle); the Dummy carries the forearm's line on.</summary>
        public Vector3? BackHandEnd { get; set; }

        /// <summary>Where the rig's head bone (the base of its skull) goes, unless <see cref="Eyes"/> places it.</summary>
        public Vector3? HeadJoint { get; set; }

        /// <summary>
        /// Where the eyes are: when the rig has eyes, its head is placed so its eyes are here, turned by
        /// <see cref="HeadRotation"/>, whatever its head joint's place in the head.
        /// </summary>
        public Vector3? Eyes { get; set; }

        /// <summary>The head's turn: +z where the eyes look, +y up. Null: upright, looking along +z.</summary>
        public Quaternion? HeadRotation { get; set; }

        /// <summary>
        /// The sword hand's palm, the outward normal (down for a hand resting palm down): it sets the hand's roll. Null: the hand
        /// only points at its end, carried by the forearm.
        /// </summary>
        public Vector3? SwordPalm { get; set; }

        /// <summary>
        /// The sword hand's line, wrist to middle knuckle, when it is known from a turn (the controller's) rather than
        /// from the hand end; used only with <see cref="SwordPalm"/>. Null: toward the hand end.
        /// </summary>
        public Vector3? SwordFingers { get; set; }

        /// <inheritdoc cref="SwordPalm"/>
        public Vector3? BackPalm { get; set; }

        /// <inheritdoc cref="SwordFingers"/>
        public Vector3? BackFingers { get; set; }

        public Vector3? Get(SkeletonJoint joint)
        {
            return _joints[(int)joint];
        }

        public void Set(SkeletonJoint joint, Vector3? position)
        {
            _joints[(int)joint] = position;
        }

        /// <summary>Forgets every target (all null), keeping the hand.</summary>
        public void Clear()
        {
            Array.Clear(_joints, 0, _joints.Length);
            BackHandEnd = null;
            HeadJoint = null;
            Eyes = null;
            HeadRotation = null;
            SwordPalm = null;
            SwordFingers = null;
            BackPalm = null;
            BackFingers = null;
        }
    }

    /// <summary>
    /// Poses a Humanoid rig (<see cref="BodyRigRest"/>) on a body's joints (PER-142): the Dummy's en garde and the
    /// opponent's tracked body alike. Plain math on the parts frame; the Unity layer copies the result onto the bones.
    /// <list type="bullet">
    /// <item>The hips, chest and head take their whole frame (side and up) from the stance, as before (PER-78).</item>
    /// <item>Each limb bone is aimed through its parent: its rest pose is first carried by its posed parent (torso, upper
    /// limb, lower limb), then turned the least to point at the next joint. So the result is the same whichever way the
    /// body faces: a body facing -z (the opponent) twists its arms and legs as one facing +z (the Dummy).</item>
    /// <item>The upper arm and the thigh then turn about their own length so the elbow or knee hinges in the plane of the
    /// three tracked joints; a limb too straight to have such a plane keeps its carried twist (an arm) or turns the
    /// knee over the toes (a leg).</item>
    /// <item>A hand with a palm direction takes its roll from it, and the forearm takes
    /// <see cref="BodyRigTuning.ForearmTwistShare"/> of that roll; without one it only points at its end.</item>
    /// <item>A foot only turns about the vertical, from the body's rest forward to the heel-to-toe line, so its sole stays
    /// flat; any heading works, straight back included.</item>
    /// <item>Fitting lengths (a tracked body): the upper arms, forearms, thighs, shins and chest are scaled to the
    /// median tracked length of their segment (<see cref="BoneLengthFit"/>), and the neck, from its rest place over the
    /// shoulders, is aimed at the head.</item>
    /// </list>
    /// A part whose joints are missing keeps its last pose. Nothing is allocated per frame.
    /// </summary>
    public sealed class BodyRigPoser
    {
        private const float DegreesToRadians = (float)(Math.PI / 180.0);

        // Without finger bones to read it from, a hand is taken to rest palm down.
        private static readonly Vector3 DefaultRestPalm = -Vector3.UnitY;

        private readonly BodyRigRest _rest;
        private readonly RigBonePose[] _poses = new RigBonePose[BodyRigRest.BoneCount];
        private readonly bool[] _isPosed = new bool[BodyRigRest.BoneCount];
        private readonly BoneLengthFit[] _fits = new BoneLengthFit[BodyRigRest.BoneCount];
        private readonly Vector3 _restForward;
        private readonly Vector3 _restHipsRight;
        private readonly Vector3 _restShouldersRight;
        private readonly Vector3 _restSpineUp;
        private readonly Vector3 _restHipJointCenter;
        private readonly Vector3 _restShoulderCenter;
        private readonly RigBone _chestTop;
        private Vector3 _shoulderCenter;
        private float _leftRoll;
        private float _rightRoll;
        private bool _hasLeftRoll;
        private bool _hasRightRoll;

        /// <param name="rest">The rig's rest pose, with every bone <see cref="BodyRigRest.HasRequiredBones"/> names.</param>
        /// <param name="fitsLengths">Fit bone lengths to the joints and aim the neck (a tracked body); false keeps rest sizes (the Dummy).</param>
        public BodyRigPoser(BodyRigRest rest, bool fitsLengths)
        {
            _rest = rest ?? throw new ArgumentNullException(nameof(rest));
            if (!rest.HasRequiredBones())
            {
                throw new ArgumentException("The rig lacks a bone every pose needs.", nameof(rest));
            }

            FitsLengths = fitsLengths;
            for (var index = 0; index < BodyRigRest.BoneCount; index++)
            {
                var bone = (RigBone)index;
                _poses[index] = rest.Has(bone) ? new RigBonePose(rest.GetPosition(bone), rest.GetRotation(bone), 1.0f) : default;
                _fits[index] = new BoneLengthFit();
            }

            _chestTop = rest.Has(RigBone.UpperChest) ? RigBone.UpperChest : RigBone.Chest;
            _restHipsRight = GetRestDirection(RigBone.LeftUpperLeg, RigBone.RightUpperLeg);
            _restShouldersRight = GetRestDirection(RigBone.LeftUpperArm, RigBone.RightUpperArm);
            _restHipJointCenter = (rest.GetPosition(RigBone.LeftUpperLeg) + rest.GetPosition(RigBone.RightUpperLeg)) * 0.5f;
            _restShoulderCenter = (rest.GetPosition(RigBone.LeftUpperArm) + rest.GetPosition(RigBone.RightUpperArm)) * 0.5f;
            _restSpineUp = _restShoulderCenter - rest.GetPosition(RigBone.Hips);
            var forward = Vector3.Cross(_restHipsRight, _restSpineUp);
            _restForward = Vector3.Normalize(Flatten(forward));
            _shoulderCenter = _restShoulderCenter;
        }

        public bool FitsLengths { get; }

        /// <summary>True when the last <see cref="Pose"/> set this bone; false when its part kept its pose (or the rig has no such bone).</summary>
        public bool IsPosed(RigBone bone)
        {
            return _isPosed[(int)bone];
        }

        /// <summary>The bone's pose as last set (its rest pose before the first).</summary>
        public RigBonePose GetPose(RigBone bone)
        {
            return _poses[(int)bone];
        }

        /// <summary>Forgets the fitted lengths and hand rolls (a new body).</summary>
        public void Reset()
        {
            foreach (var fit in _fits)
            {
                fit.Reset();
            }

            _hasLeftRoll = false;
            _hasRightRoll = false;
        }

        /// <summary>Poses every bone whose joints <paramref name="targets"/> has.</summary>
        public void Pose(BodyRigTargets targets)
        {
            if (targets == null)
            {
                throw new ArgumentNullException(nameof(targets));
            }

            Array.Clear(_isPosed, 0, _isPosed.Length);
            var isSwordRight = targets.Hand != Hand.Left;
            var swordHip = targets.Get(SkeletonJoint.FrontHip);
            var backHip = targets.Get(SkeletonJoint.BackHip);
            var swordShoulder = targets.Get(SkeletonJoint.SwordShoulder);
            var backShoulder = targets.Get(SkeletonJoint.BackShoulder);
            var hipCenter = targets.Get(SkeletonJoint.HipCenter);
            var shoulderCenter = targets.Get(SkeletonJoint.ShoulderCenter);
            if (swordHip.HasValue && backHip.HasValue && swordShoulder.HasValue && backShoulder.HasValue && hipCenter.HasValue && shoulderCenter.HasValue)
            {
                // Anatomical right minus left: the sword side is the right for a right-handed body.
                var hipsRight = isSwordRight ? swordHip.Value - backHip.Value : backHip.Value - swordHip.Value;
                var shouldersRight = isSwordRight ? swordShoulder.Value - backShoulder.Value : backShoulder.Value - swordShoulder.Value;
                PoseTorso(targets.TimeSeconds, hipCenter.Value, shoulderCenter.Value, hipsRight, shouldersRight);
            }

            var headTurn = targets.HeadRotation ?? Quaternion.Identity;
            if (targets.Eyes.HasValue && _rest.Eyes.HasValue)
            {
                var headFromEyes = Vector3.Transform(_rest.GetPosition(RigBone.Head) - _rest.Eyes.Value, GetHeadTurn(headTurn));
                PoseHead(targets.Eyes.Value + headFromEyes, headTurn);
            }
            else if (targets.HeadJoint.HasValue)
            {
                PoseHead(targets.HeadJoint.Value, headTurn);
            }

            var time = targets.TimeSeconds;
            var swordElbow = targets.Get(SkeletonJoint.SwordElbow);
            var swordWrist = targets.Get(SkeletonJoint.SwordWrist);
            var swordHandEnd = targets.Get(SkeletonJoint.SwordHandEnd);
            var backElbow = targets.Get(SkeletonJoint.BackElbow);
            var backWrist = targets.Get(SkeletonJoint.BackHand);
            if (isSwordRight)
            {
                PoseArm(false, time, swordShoulder, swordElbow, swordWrist, swordHandEnd, targets.SwordPalm, targets.SwordFingers);
                PoseArm(true, time, backShoulder, backElbow, backWrist, targets.BackHandEnd, targets.BackPalm, targets.BackFingers);
            }
            else
            {
                PoseArm(true, time, swordShoulder, swordElbow, swordWrist, swordHandEnd, targets.SwordPalm, targets.SwordFingers);
                PoseArm(false, time, backShoulder, backElbow, backWrist, targets.BackHandEnd, targets.BackPalm, targets.BackFingers);
            }

            PoseLeg(!isSwordRight, time, swordHip, targets.Get(SkeletonJoint.FrontKnee), targets.Get(SkeletonJoint.FrontAnkle), targets.Get(SkeletonJoint.FrontToe));
            PoseLeg(isSwordRight, time, backHip, targets.Get(SkeletonJoint.BackKnee), targets.Get(SkeletonJoint.BackAnkle), targets.Get(SkeletonJoint.BackToe));
        }

        private void PoseTorso(double timeSeconds, Vector3 hipCenter, Vector3 shoulderCenter, Vector3 hipsRight, Vector3 shouldersRight)
        {
            var spineUp = shoulderCenter - hipCenter;
            _shoulderCenter = shoulderCenter;

            // The hips bone sits a little above the line between the hip joints; it keeps that offset, turned with the hips.
            var hipsTurn = RigMath.FrameFromRightUp(hipsRight, spineUp) * Quaternion.Inverse(RigMath.FrameFromRightUp(_restHipsRight, _restSpineUp));
            var hips = hipCenter + Vector3.Transform(_rest.GetPosition(RigBone.Hips) - _restHipJointCenter, hipsTurn);
            Set(RigBone.Hips, hips, hipsTurn * _rest.GetRotation(RigBone.Hips), 1.0f);

            var torsoScale = Fit(RigBone.Chest, timeSeconds, spineUp.Length(), Vector3.Distance(_restShoulderCenter, _restHipJointCenter));

            // The chest bones keep their rest place along the spine, between the hips bone and the shoulders.
            var chestTurn = RigMath.FrameFromRightUp(shouldersRight, spineUp) * Quaternion.Inverse(RigMath.FrameFromRightUp(_restShouldersRight, _restSpineUp));
            PoseChestBone(RigBone.Chest, hips, shoulderCenter, chestTurn, torsoScale);
            if (_rest.Has(RigBone.UpperChest))
            {
                PoseChestBone(RigBone.UpperChest, hips, shoulderCenter, chestTurn, torsoScale);
            }
        }

        private void PoseChestBone(RigBone bone, Vector3 hips, Vector3 shoulderCenter, Quaternion chestTurn, float scale)
        {
            var restHipsHeight = _rest.GetPosition(RigBone.Hips).Y;
            var fraction = (_rest.GetPosition(bone).Y - restHipsHeight) / (_restShoulderCenter.Y - restHipsHeight);
            Set(bone, Vector3.Lerp(hips, shoulderCenter, fraction), chestTurn * _rest.GetRotation(bone), scale);
        }

        private void PoseHead(Vector3 headJoint, Quaternion turn)
        {
            if (FitsLengths && _rest.Has(RigBone.Neck))
            {
                // The neck's base keeps its rest place over the tracked shoulders (the rig's head over its shoulders is
                // within 1 cm of a tracked Fencer's), turned with the chest, and the neck reaches for the head.
                var parentTurn = GetTurn(_chestTop);
                var neck = _shoulderCenter + Vector3.Transform(_rest.GetPosition(RigBone.Neck) - _restShoulderCenter, parentTurn);
                // Not scaled: the head (placed by the eyes) sits within a few cm of its rest place over the shoulders, and
                // a scaled neck would also grow thicker; the skin between neck and head takes the difference.
                var restNeck = GetRestDirection(RigBone.Neck, RigBone.Head);
                Set(RigBone.Neck, neck, Aim(RigBone.Neck, parentTurn, restNeck, headJoint - neck), 1.0f);
            }

            // The head looks where the headset looks; the Dummy's looks at the Fencer: upright, facing +z.
            Set(RigBone.Head, headJoint, GetHeadTurn(turn) * _rest.GetRotation(RigBone.Head), 1.0f);
        }

        /// <summary>The head bone's turn from rest for a head turned by <paramref name="turn"/> (+z where it looks).</summary>
        private Quaternion GetHeadTurn(Quaternion turn)
        {
            return RigMath.FrameFromRightUp(Vector3.Transform(Vector3.UnitX, turn), Vector3.Transform(Vector3.UnitY, turn))
                   * Quaternion.Inverse(RigMath.FrameFromRightUp(_restShouldersRight, _restSpineUp));
        }

        private void PoseArm(bool isLeft, double timeSeconds, Vector3? shoulderJoint, Vector3? elbowJoint, Vector3? wristJoint, Vector3? handEndJoint, Vector3? palm, Vector3? fingers)
        {
            if (!shoulderJoint.HasValue || !elbowJoint.HasValue || !wristJoint.HasValue || !handEndJoint.HasValue)
            {
                return;
            }

            var shoulder = shoulderJoint.Value;
            var elbow = elbowJoint.Value;
            var wrist = wristJoint.Value;
            var handEnd = handEndJoint.Value;
            var upperArm = isLeft ? RigBone.LeftUpperArm : RigBone.RightUpperArm;
            var lowerArm = isLeft ? RigBone.LeftLowerArm : RigBone.RightLowerArm;
            var hand = isLeft ? RigBone.LeftHand : RigBone.RightHand;
            var finger = isLeft ? _rest.LeftMiddleFinger : _rest.RightMiddleFinger;

            var upperRest = GetRestDirection(upperArm, lowerArm);
            var hinge = GetRestHinge(upperArm);
            var upperRotation = AimHinged(upperArm, GetTurn(_chestTop), upperRest, elbow - shoulder, wrist - elbow, hinge, null);
            var upperScale = Fit(upperArm, timeSeconds, Vector3.Distance(shoulder, elbow), upperRest.Length());
            Set(upperArm, shoulder, upperRotation, upperScale);

            var lowerRest = GetRestDirection(lowerArm, hand);
            var lowerRotation = Aim(lowerArm, GetTurn(upperArm), lowerRest, wrist - elbow);
            var lowerScale = Fit(lowerArm, timeSeconds, Vector3.Distance(elbow, wrist), lowerRest.Length());

            // Without finger bones the hand carries on the rest forearm's line.
            var handRest = finger.HasValue ? finger.Value - _rest.GetPosition(hand) : lowerRest;
            var lowerTurn = lowerRotation * Quaternion.Inverse(_rest.GetRotation(lowerArm));
            var handLine = palm.HasValue && fingers.HasValue ? fingers.Value : handEnd - wrist;
            var handRotation = Aim(hand, lowerTurn, handRest, handLine);
            if (palm.HasValue)
            {
                // The palm sets the roll; the forearm takes its share of it, kept continuous over full turns.
                var rolled = RigMath.FrameFromXY(handLine, palm.Value)
                             * Quaternion.Inverse(RigMath.FrameFromXY(handRest, (isLeft ? _rest.LeftPalm : _rest.RightPalm) ?? DefaultRestPalm))
                             * _rest.GetRotation(hand);
                var forearm = Vector3.Normalize(wrist - elbow);
                var roll = RigMath.GetTwist(rolled * Quaternion.Inverse(handRotation), forearm);
                roll = UpdateRoll(isLeft, roll);
                lowerRotation = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(forearm, roll * BodyRigTuning.ForearmTwistShare) * lowerRotation);
                handRotation = rolled;
            }
            else
            {
                ForgetRoll(isLeft);
            }

            Set(lowerArm, elbow, lowerRotation, lowerScale);
            Set(hand, wrist, handRotation, 1.0f);
        }

        private void PoseLeg(bool isLeft, double timeSeconds, Vector3? hipJoint, Vector3? kneeJoint, Vector3? ankleJoint, Vector3? toeJoint)
        {
            if (!hipJoint.HasValue || !kneeJoint.HasValue || !ankleJoint.HasValue || !toeJoint.HasValue)
            {
                return;
            }

            var hip = hipJoint.Value;
            var knee = kneeJoint.Value;
            var ankle = ankleJoint.Value;
            var toe = toeJoint.Value;
            var upperLeg = isLeft ? RigBone.LeftUpperLeg : RigBone.RightUpperLeg;
            var lowerLeg = isLeft ? RigBone.LeftLowerLeg : RigBone.RightLowerLeg;
            var foot = isLeft ? RigBone.LeftFoot : RigBone.RightFoot;

            // The rig's ankle stands at its own height over the sole, so the foot rests on the floor rather than in it.
            var footJoint = new Vector3(ankle.X, Math.Max(ankle.Y, _rest.GetPosition(foot).Y), ankle.Z);
            var heelToToe = Flatten(toe - ankle);
            var hasHeading = heelToToe.LengthSquared() > 1e-8f;

            // A straight leg turns its knee over the toes.
            var thighRest = GetRestDirection(upperLeg, lowerLeg);
            var hinge = GetRestHinge(upperLeg);
            var overToes = hasHeading ? Vector3.Cross(heelToToe, knee - hip) : (Vector3?)null;
            var thighRotation = AimHinged(upperLeg, GetTurn(RigBone.Hips), thighRest, knee - hip, footJoint - knee, hinge, overToes);
            Set(upperLeg, hip, thighRotation, Fit(upperLeg, timeSeconds, Vector3.Distance(hip, knee), thighRest.Length()));

            var shinRest = GetRestDirection(lowerLeg, foot);
            var shinRotation = Aim(lowerLeg, GetTurn(upperLeg), shinRest, footJoint - knee);
            Set(lowerLeg, knee, shinRotation, Fit(lowerLeg, timeSeconds, Vector3.Distance(knee, footJoint), shinRest.Length()));

            // Turn the foot about the vertical only, from the body's rest forward to the heel-to-toe line.
            var heading = hasHeading ? RigMath.GetSignedAngle(Flatten(_restForward), heelToToe, Vector3.UnitY) : 0.0f;
            Set(foot, footJoint, Quaternion.CreateFromAxisAngle(Vector3.UnitY, heading) * _rest.GetRotation(foot), 1.0f);
        }

        /// <summary>
        /// The bone's rest rotation carried by its parent's turn, then turned the least so its rest direction points along
        /// <paramref name="direction"/>.
        /// </summary>
        private Quaternion Aim(RigBone bone, Quaternion parentTurn, Vector3 restDirection, Vector3 direction)
        {
            var carried = parentTurn * _rest.GetRotation(bone);
            return Quaternion.Normalize(RigMath.FromTo(Vector3.Transform(restDirection, parentTurn), direction) * carried);
        }

        /// <summary>
        /// <see cref="Aim"/>, then turned about the bone's own length so its rest hinge (<paramref name="restHinge"/>) is
        /// square to the plane of the bone and <paramref name="lowerDirection"/>, the next bone (the hinge lies along the
        /// plane's normal, bone cross next). Too straight a limb blends toward <paramref name="straightNormal"/>, or keeps
        /// the carried twist when null.
        /// </summary>
        private Quaternion AimHinged(RigBone bone, Quaternion parentTurn, Vector3 restDirection, Vector3 direction, Vector3 lowerDirection, Vector3 restHinge, Vector3? straightNormal)
        {
            var aimed = Aim(bone, parentTurn, restDirection, direction);
            var axis = Vector3.Normalize(direction);
            var hinge = Vector3.Transform(restHinge, aimed * Quaternion.Inverse(_rest.GetRotation(bone)));
            var fallbackTwist = straightNormal.HasValue && straightNormal.Value.LengthSquared() > 1e-10f
                ? RigMath.GetSignedAngle(hinge, straightNormal.Value, axis)
                : 0.0f;

            var bend = RigMath.GetAngle(direction, lowerDirection);
            var weight = SmoothStep(BodyRigTuning.MinBendDegrees * DegreesToRadians, BodyRigTuning.FullBendDegrees * DegreesToRadians, bend);
            var twist = fallbackTwist;
            var normal = Vector3.Cross(direction, lowerDirection);
            if (weight > 0.0f && normal.LengthSquared() > 1e-10f)
            {
                var bentTwist = RigMath.GetSignedAngle(hinge, normal, axis);
                twist = fallbackTwist + (weight * RigMath.WrapAngle(bentTwist - fallbackTwist));
            }

            return Quaternion.Normalize(Quaternion.CreateFromAxisAngle(axis, twist) * aimed);
        }

        /// <summary>
        /// The bone's scale for a tracked segment of <paramref name="trackedMeters"/> against its rest length (1 when not
        /// fitting lengths); the chest's fit stands for both chest bones.
        /// </summary>
        private float Fit(RigBone bone, double timeSeconds, float trackedMeters, float restMeters)
        {
            if (!FitsLengths || !(restMeters > 1e-6f))
            {
                return 1.0f;
            }

            var fit = _fits[(int)bone];
            fit.Add(timeSeconds, trackedMeters);
            if (!fit.Meters.HasValue)
            {
                return 1.0f;
            }

            return Math.Min(Math.Max(fit.Meters.Value / restMeters, BodyRigTuning.MinLengthScale), BodyRigTuning.MaxLengthScale);
        }

        private float UpdateRoll(bool isLeft, float roll)
        {
            if (isLeft)
            {
                _leftRoll = _hasLeftRoll ? RigMath.Unwrap(roll, _leftRoll) : roll;
                _hasLeftRoll = true;
                return _leftRoll;
            }

            _rightRoll = _hasRightRoll ? RigMath.Unwrap(roll, _rightRoll) : roll;
            _hasRightRoll = true;
            return _rightRoll;
        }

        private void ForgetRoll(bool isLeft)
        {
            if (isLeft)
            {
                _hasLeftRoll = false;
            }
            else
            {
                _hasRightRoll = false;
            }
        }

        /// <summary>
        /// The rest hinge of the elbow or knee below <paramref name="upperBone"/> (an upper arm or thigh), as the normal of
        /// the bone and the next one (upper cross lower): the rest pose's own bend when it is bent (the Armature's arms rest
        /// bent, hands forward), else the forearm bending forward or the shin back from the rest line.
        /// </summary>
        public Vector3 GetRestHinge(RigBone upperBone)
        {
            var isArm = upperBone == RigBone.LeftUpperArm || upperBone == RigBone.RightUpperArm;
            var isLeg = upperBone == RigBone.LeftUpperLeg || upperBone == RigBone.RightUpperLeg;
            if (!isArm && !isLeg)
            {
                throw new ArgumentOutOfRangeException(nameof(upperBone), upperBone, "Not an upper arm or thigh.");
            }

            var lowerBone = (RigBone)((int)upperBone + 1);
            var endBone = (RigBone)((int)upperBone + 2);
            var upper = GetRestDirection(upperBone, lowerBone);
            var lower = GetRestDirection(lowerBone, endBone);
            if (RigMath.GetAngle(upper, lower) >= BodyRigTuning.MinBendDegrees * DegreesToRadians)
            {
                return Vector3.Normalize(Vector3.Cross(upper, lower));
            }

            return Vector3.Normalize(Vector3.Cross(upper, isArm ? _restForward : -_restForward));
        }

        /// <summary>The turn from the bone's rest rotation to its current one.</summary>
        private Quaternion GetTurn(RigBone bone)
        {
            return _poses[(int)bone].Rotation * Quaternion.Inverse(_rest.GetRotation(bone));
        }

        private void Set(RigBone bone, Vector3 position, Quaternion rotation, float scale)
        {
            _poses[(int)bone] = new RigBonePose(position, Quaternion.Normalize(rotation), scale);
            _isPosed[(int)bone] = true;
        }

        private Vector3 GetRestDirection(RigBone from, RigBone to)
        {
            return _rest.GetPosition(to) - _rest.GetPosition(from);
        }

        private static Vector3 Flatten(Vector3 direction)
        {
            return new Vector3(direction.X, 0.0f, direction.Z);
        }

        private static float SmoothStep(float edge0, float edge1, float value)
        {
            var t = Math.Min(Math.Max((value - edge0) / (edge1 - edge0), 0.0f), 1.0f);
            return t * t * (3.0f - (2.0f * t));
        }
    }
}
