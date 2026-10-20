using System;
using System.Numerics;
using VrFencing.Settings;

namespace VrFencing.Dummy
{
    /// <summary>What the Dummy's feet are doing.</summary>
    public enum FootworkStep
    {
        /// <summary>Both feet planted in en garde (breathing and shifting its weight, unless the life layer is off).</summary>
        Standing,

        /// <summary>Stepping toward the Fencer: front foot first, then the back foot.</summary>
        Advancing,

        /// <summary>Stepping away from the Fencer: back foot first, then the front foot.</summary>
        Retreating,

        /// <summary>Lunging toward the Fencer (PER-111): the front foot kicks out, the back foot stays, the hips drop.</summary>
        Lunging,

        /// <summary>Coming back from a lunge into en garde where the lunge started.</summary>
        Recovering,

        /// <summary>Running at the Fencer (PER-111): the back foot crosses over in front, then the front foot runs past it.</summary>
        Fleching,

        /// <summary>
        /// Stepping more across the Strip than along it (PER-118): the front foot first, then the back foot. A diagonal
        /// step that goes further along the Strip than across is an advance or a retreat.
        /// </summary>
        SteppingSideways,
    }

    /// <summary>
    /// The Dummy's footwork synthesizer (PER-110 and PER-111, stages 1 and 2 of PER-108): it moves the Dummy along the
    /// Strip in real footwork and gives the legs' joints every frame.
    ///
    /// Moves, and who drives them:
    /// - Advance, retreat and side step (<see cref="Step(float, float, float)"/>): to a target distance, e.g.
    ///   <see cref="DistanceKeeper.TargetFrontFootZ"/>, and a target side, e.g. <see cref="DistanceKeeper.TargetSideMeters"/>
    ///   (PER-118), in steps of at most <see cref="FootworkTuning.MaxStridePerHeight"/> over the floor (at most
    ///   <see cref="FootworkTuning.MaxSideStridePerHeight"/> of it across), each finished before the next starts. A step
    ///   with a side part is a diagonal; one mostly across is a side step (<see cref="FootworkStep.SteppingSideways"/>).
    /// - Lunge and recovery (<see cref="StepLunge"/>): the body follows a lunge offset someone else times, e.g. the Armed
    ///   Dummy's (<see cref="ArmedDummyAttack.GetLungeOffsetMeters"/>), so the attack's timing and judging stay its own.
    /// - Fleche (<see cref="StartFleche"/>, then <see cref="Step"/>): a self-timed run of two crossing strides that
    ///   ends in en garde <see cref="FlecheLengthMeters"/> nearer the Fencer.
    /// - Standing (<see cref="Hold"/> or <see cref="Step"/> with nowhere to go): breathing and weight shift only.
    ///
    /// Outputs: <see cref="DistanceMeters"/>, where the body is (what <see cref="DummyLayout.DummyToStrip"/> places the
    /// Dummy frame at: the spot under the body where its front foot stands in en garde), and <see cref="Legs"/>, the leg
    /// joints in the Dummy frame. The body's place across the Strip, <see cref="SideMeters"/> (PER-118), is carried in
    /// the legs, not the frame: the hips and planted feet stand that far to the side in the Dummy frame and the upper body
    /// rides on the hips, so whatever places the Dummy frame (the Target Zones on the Strip, the Blade) is unchanged.
    ///
    /// Rules:
    /// - The body and every swinging foot move on minimum-jerk curves (Flash and Hogan 1985): no jolt at either end.
    /// - A planted foot is pinned to its spot on the Strip. The one exception: in a lunge longer than the back leg can
    ///   reach, the back foot drags forward the least it must (<see cref="FootworkTuning.LungeBackLegReachFraction"/>)
    ///   and steps back in the recovery.
    /// - Knees come from analytic two-bone IK, bending the way they bend in en garde.
    /// - The hips dip in a step, drop and stay back in a lunge, crouch in a fleche, breathe and shift weight when
    ///   standing. The upper body rides on them and leans with them (PER-115, <see cref="DummyTorso"/>, posed by
    ///   <see cref="DummyLayout.PoseLegs"/>), so the whole body moves as one.
    /// - Each step's duration, lift, foot pitch, hip dip and foot rhythm vary slightly, and the breathing and weight shift
    ///   never repeat exactly, all drawn from one seeded random generator, so no two steps look the same and tests repeat.
    ///
    /// Nothing is allocated per frame.
    /// </summary>
    public sealed class DummyFootwork
    {
        // A lunge offset this close to zero is back in en garde; a drop this big from the peak starts the recovery.
        private const float LungeSettledMeters = 1e-4f;

        private const float DegreesToRadians = (float)(Math.PI / 180.0);

        private readonly float _heightMeters;
        private readonly Hand _hand;
        private readonly Random _random;
        private readonly LegRig _front;
        private readonly LegRig _back;
        private readonly FootworkLife _life;

        private FootState _frontFoot;
        private FootState _backFoot;
        private Vector3 _moveHipOffset;

        // Advance, retreat and side step.
        private float _stepFrom;
        private float _stepTo;
        private float _stepFromSide;
        private float _stepToSide;
        private float _stepSeconds;
        private float _elapsedSeconds;
        private float _stepLiftMeters;
        private float _stepPitchScale;
        private float _stepHipDipMeters;
        private float _leadLandsAt;
        private float _trailLiftsAt;

        // Lunge and recovery.
        private float _lungeStart;
        private float _lungeLengthMeters;
        private float _lungePeakMeters;
        private float _frontTravelAtPeak;
        private FootState _frontFootAtPeak;
        private float _backDragAtPeak;
        private float _backDragMeters;

        // Fleche.
        private float _flecheFrom;

        /// <param name="heightMeters">The Dummy's height; strides, lifts and the life layer scale with it.</param>
        /// <param name="hand">The Dummy's sword hand: a left-handed Dummy's legs are mirrored.</param>
        /// <param name="startDistanceMeters">Where the Dummy stands now, in en garde.</param>
        /// <param name="random">Draws every variation; seed it for repeatable tests.</param>
        /// <param name="isAlive">
        /// Breathing and weight shift on (PER-111). Off, a standing Dummy holds en garde exactly, as in PER-110.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">The height is not positive, or the start not a finite number.</exception>
        public DummyFootwork(float heightMeters, Hand hand, float startDistanceMeters, Random random, bool isAlive = true)
        {
            DummyLayout.RequirePositiveHeight(heightMeters);
            RequireFinite(startDistanceMeters, nameof(startDistanceMeters));
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _heightMeters = heightMeters;
            _hand = hand;
            _front = new LegRig(DummyLegs.FrontAtUnitHeight, heightMeters);
            _back = new LegRig(DummyLegs.BackAtUnitHeight, heightMeters);
            _life = isAlive ? new FootworkLife(heightMeters, random) : null;
            PlaceAt(startDistanceMeters);
        }

        /// <summary>Where the body is on the Strip: the Dummy frame's distance (see the class summary).</summary>
        public float DistanceMeters { get; private set; }

        /// <summary>
        /// How far the body stands to the side of the Strip's center line (PER-118): Strip-frame x, + the Fencer's right.
        /// 0 is en garde on the center line, where <see cref="PlaceAt"/> stands it.
        /// </summary>
        public float SideMeters { get; private set; }

        /// <summary>The leg joints in the Dummy frame, mirrored for a left-handed Dummy.</summary>
        public DummyLegs Legs { get; private set; }

        public FootworkStep CurrentStep { get; private set; } = FootworkStep.Standing;

        /// <summary>How many advances, retreats and side steps have started since the Dummy was made.</summary>
        public int StepCount { get; private set; }

        /// <summary>Whether the front foot is on the floor, pinned to its spot.</summary>
        public bool IsFrontFootPlanted { get; private set; } = true;

        /// <summary>Whether the back foot is on the floor, pinned to its spot (false while it swings or drags).</summary>
        public bool IsBackFootPlanted { get; private set; } = true;

        /// <summary>How far a fleche carries the Dummy toward the Fencer: the en garde stance plus the crossing stride.</summary>
        public float FlecheLengthMeters =>
            (_front.Rest.Ankle.Z - _back.Rest.Ankle.Z) + (FootworkTuning.FlecheCrossAheadPerHeight * _heightMeters);

        /// <summary>
        /// Stands the Dummy in en garde at <paramref name="distanceMeters"/> on the Strip's center line at once, ending
        /// any move: for a Drill that places the Dummy, not for footwork.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The distance is not a finite number.</exception>
        public void PlaceAt(float distanceMeters)
        {
            RequireFinite(distanceMeters, nameof(distanceMeters));
            DistanceMeters = distanceMeters;
            SideMeters = 0.0f;
            CurrentStep = FootworkStep.Standing;
            Stand();
        }

        /// <summary>
        /// One frame toward <paramref name="targetDistanceMeters"/> along the Strip, keeping the body's side
        /// (<see cref="SideMeters"/>): see <see cref="Step(float, float, float)"/>.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The target is not a finite number.</exception>
        public void Step(float targetDistanceMeters, float deltaSeconds)
        {
            Step(targetDistanceMeters, SideMeters, deltaSeconds);
        }

        /// <summary>
        /// One frame: moves toward <paramref name="targetDistanceMeters"/> along the Strip and
        /// <paramref name="targetSideMeters"/> across it (Strip-frame x, see <see cref="SideMeters"/>) for
        /// <paramref name="deltaSeconds"/> in advances, retreats and side steps (diagonal when both change), or carries on
        /// a fleche, and updates <see cref="DistanceMeters"/>, <see cref="SideMeters"/>, <see cref="Legs"/> and
        /// <see cref="CurrentStep"/>. A lunge is driven by <see cref="StepLunge"/> only: here it holds where it is.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">A target is not a finite number.</exception>
        public void Step(float targetDistanceMeters, float targetSideMeters, float deltaSeconds)
        {
            RequireFinite(targetDistanceMeters, nameof(targetDistanceMeters));
            RequireFinite(targetSideMeters, nameof(targetSideMeters));
            if (!(deltaSeconds > 0.0f))
            {
                return;
            }

            _life?.Advance(deltaSeconds);
            switch (CurrentStep)
            {
                case FootworkStep.Standing:
                    var wanted = targetDistanceMeters - DistanceMeters;
                    var wantedSide = targetSideMeters - SideMeters;
                    if (Math.Abs(wanted) <= FootworkTuning.MinStepMeters && Math.Abs(wantedSide) <= FootworkTuning.MinStepMeters)
                    {
                        break;
                    }

                    StartStep(wanted, wantedSide);
                    ContinueStep(deltaSeconds);
                    break;
                case FootworkStep.Advancing:
                case FootworkStep.Retreating:
                case FootworkStep.SteppingSideways:
                    ContinueStep(deltaSeconds);
                    break;
                case FootworkStep.Fleching:
                    ContinueFleche(deltaSeconds);
                    break;
            }

            ComposeLegs();
        }

        /// <summary>
        /// One frame of a lunge whose timing someone else owns: the body stands <paramref name="offsetMeters"/> toward the
        /// Fencer from where the lunge started (the distance when the offset first rose above zero). While the offset
        /// grows the Dummy lunges: the front foot kicks out ahead of the body and lands as the body arrives, the back foot
        /// stays planted (dragging only if the lunge is longer than the leg reaches), the hips drop and stay back. Once
        /// it falls the Dummy recovers, both feet coming back with the body, and at zero it stands in en garde where it
        /// started. A step or fleche under way is finished first, the offset ignored meanwhile.
        /// </summary>
        /// <param name="offsetMeters">The body's offset toward the Fencer; negative counts as zero.</param>
        /// <param name="lungeLengthMeters">The full lunge's offset (e.g. <see cref="ArmedDummyAttack.LungeMeters"/>): sets how deep the hips go.</param>
        /// <param name="deltaSeconds">The frame's duration; zero or less changes nothing.</param>
        /// <exception cref="ArgumentOutOfRangeException">The offset is not finite, or the lunge length not a positive number.</exception>
        public void StepLunge(float offsetMeters, float lungeLengthMeters, float deltaSeconds)
        {
            RequireFinite(offsetMeters, nameof(offsetMeters));
            if (!(lungeLengthMeters > 0.0f) || float.IsInfinity(lungeLengthMeters))
            {
                throw new ArgumentOutOfRangeException(nameof(lungeLengthMeters), lungeLengthMeters, "Must be a positive number of meters.");
            }

            if (!(deltaSeconds > 0.0f))
            {
                return;
            }

            _life?.Advance(deltaSeconds);
            var offset = Math.Max(0.0f, offsetMeters);
            switch (CurrentStep)
            {
                case FootworkStep.Advancing:
                case FootworkStep.Retreating:
                case FootworkStep.SteppingSideways:
                    ContinueStep(deltaSeconds);
                    break;
                case FootworkStep.Fleching:
                    ContinueFleche(deltaSeconds);
                    break;
                case FootworkStep.Standing:
                    if (offset > LungeSettledMeters)
                    {
                        StartLunge(lungeLengthMeters);
                        FollowLunge(offset);
                    }

                    break;
                default:
                    FollowLunge(offset);
                    break;
            }

            ComposeLegs();
        }

        /// <summary>
        /// Starts a fleche from en garde; <see cref="Step"/> (or <see cref="StepLunge"/>) then runs it for
        /// <see cref="FootworkTuning.FlecheSeconds"/>, ignoring their targets, until the Dummy stands in en garde
        /// <see cref="FlecheLengthMeters"/> nearer the Fencer.
        /// </summary>
        /// <returns>False, and nothing starts, unless the Dummy is standing.</returns>
        public bool StartFleche()
        {
            if (CurrentStep != FootworkStep.Standing)
            {
                return false;
            }

            _flecheFrom = DistanceMeters;
            _elapsedSeconds = 0.0f;
            CurrentStep = FootworkStep.Fleching;
            return true;
        }

        /// <summary>
        /// One frame in which nobody moves the Dummy (a paused Drill, or a Drill whose Dummy stands still): a standing
        /// Dummy breathes and shifts its weight; one caught mid-move holds that pose.
        /// </summary>
        public void Hold(float deltaSeconds)
        {
            if (!(deltaSeconds > 0.0f) || _life == null)
            {
                return;
            }

            _life.Advance(deltaSeconds);
            ComposeLegs();
        }

        /// <summary>
        /// How far the hips have moved from en garde (right-handed Dummy frame, before the life layer) with the body
        /// <paramref name="offsetMeters"/> into a lunge of <paramref name="lungeLengthMeters"/>: down and back, deeper as
        /// the lunge goes on. The Armed Dummy plans its thrust from the shoulder this puts the torso at (PER-115).
        /// </summary>
        public static Vector3 GetLungeHipOffset(float heightMeters, float offsetMeters, float lungeLengthMeters)
        {
            var depth = lungeLengthMeters > 0.0f ? SmoothStep(offsetMeters / lungeLengthMeters) : 0.0f;
            return new Vector3(
                0.0f, -FootworkTuning.LungeHipDropPerHeight * heightMeters * depth, -FootworkTuning.LungeHipBackPerHeight * heightMeters * depth);
        }

        /// <summary>The minimum-jerk curve from 0 to 1: zero speed and acceleration at both ends.</summary>
        public static float MinimumJerk(float fraction)
        {
            var t = Clamp01(fraction);
            return t * t * t * ((t * ((t * 6.0f) - 15.0f)) + 10.0f);
        }

        /// <summary>A bump from 0 up to 1 at the middle and back to 0, with zero speed at both ends.</summary>
        public static float SwingBump(float fraction)
        {
            var t = Clamp01(fraction);
            return 16.0f * t * t * (1.0f - t) * (1.0f - t);
        }

        private static float SmoothStep(float fraction)
        {
            var t = Clamp01(fraction);
            return t * t * (3.0f - (2.0f * t));
        }

        private static float Clamp01(float fraction)
        {
            return Math.Min(Math.Max(fraction, 0.0f), 1.0f);
        }

        /// <summary>Both feet planted under the body in en garde, hips at rest (the life layer still applies).</summary>
        private void Stand()
        {
            _frontFoot = FootState.PlantedAt(DistanceMeters, SideMeters);
            _backFoot = FootState.PlantedAt(DistanceMeters, SideMeters);
            _moveHipOffset = Vector3.Zero;
            IsFrontFootPlanted = true;
            IsBackFootPlanted = true;
            ComposeLegs();
        }

        private void ComposeLegs()
        {
            Legs = new DummyLegs(PoseFoot(_front, _frontFoot, GetHipOffset()), PoseFoot(_back, _backFoot, GetHipOffset()));
        }

        /// <summary>Where the hips are from en garde on the center line (right-handed): the move, the life layer and the side.</summary>
        private Vector3 GetHipOffset()
        {
            return _moveHipOffset + (_life?.HipOffset ?? Vector3.Zero) + new Vector3(ToRightHandedX(SideMeters), 0.0f, 0.0f);
        }

        private LegPose PoseFoot(LegRig leg, FootState foot, Vector3 hipOffset)
        {
            var ankleOffset = new Vector3(foot.SwingOutMeters + ToRightHandedX(foot.SideAnchor), foot.LiftMeters, DistanceMeters - foot.Anchor);
            return leg.Pose(hipOffset, ankleOffset, foot.PitchRadians).ScaledForHand(1.0f, _hand);
        }

        /// <summary>
        /// The right-handed Dummy-frame x of Strip-frame x <paramref name="stripX"/>, before the legs are mirrored for the
        /// hand: the Dummy faces the Fencer, so its x runs the other way (<see cref="DummyLayout.DummyToStrip"/>), and a
        /// left-handed Dummy's mirroring turns it back.
        /// </summary>
        private float ToRightHandedX(float stripX)
        {
            return _hand == Hand.Left ? stripX : -stripX;
        }

        /// <summary>Evenly between -1 and 1.</summary>
        private float DrawVariation()
        {
            return ((float)_random.NextDouble() * 2.0f) - 1.0f;
        }

        // ---- Advance, retreat and side step ----

        private void StartStep(float wantedMeters, float wantedSideMeters)
        {
            // Across at most a side stride, and over the floor at most a stride: a diagonal is no longer or faster than a
            // straight step.
            var maxStride = FootworkTuning.MaxStridePerHeight * _heightMeters;
            var maxSideStride = FootworkTuning.MaxSideStridePerHeight * _heightMeters;
            var stride = Math.Min(Math.Max(wantedMeters, -maxStride), maxStride);
            var sideStride = Math.Min(Math.Max(wantedSideMeters, -maxSideStride), maxSideStride);
            var length = (float)Math.Sqrt((stride * stride) + (sideStride * sideStride));
            if (length > maxStride)
            {
                stride *= maxStride / length;
                sideStride *= maxStride / length;
                length = maxStride;
            }

            var strideFraction = length / maxStride;
            var nominalSeconds = FootworkTuning.ShortStepSeconds + ((FootworkTuning.FullStepSeconds - FootworkTuning.ShortStepSeconds) * strideFraction);

            _stepFrom = DistanceMeters;
            _stepTo = DistanceMeters + stride;
            _stepFromSide = SideMeters;
            _stepToSide = SideMeters + sideStride;
            _stepSeconds = nominalSeconds * (1.0f + (DrawVariation() * FootworkTuning.DurationVariationFraction));
            _stepLiftMeters = FootworkTuning.FootLiftPerHeight * _heightMeters * (1.0f + (DrawVariation() * FootworkTuning.LiftVariationFraction));
            _stepPitchScale = 1.0f + (DrawVariation() * FootworkTuning.PitchVariationFraction);
            _stepHipDipMeters = FootworkTuning.StepHipDipPerHeight * _heightMeters * (1.0f + (DrawVariation() * FootworkTuning.HipDipVariationFraction));
            var rhythmShift = DrawVariation() * FootworkTuning.LeadFootLandsVariation;
            _leadLandsAt = FootworkTuning.LeadFootLandsAtFraction + rhythmShift;
            _trailLiftsAt = FootworkTuning.TrailFootLiftsAtFraction + rhythmShift;
            _elapsedSeconds = 0.0f;

            // The Dummy faces the Fencer: a smaller distance is toward them, an advance.
            CurrentStep = Math.Abs(sideStride) > Math.Abs(stride)
                ? FootworkStep.SteppingSideways
                : stride < 0.0f ? FootworkStep.Advancing : FootworkStep.Retreating;
            StepCount++;
        }

        private void ContinueStep(float deltaSeconds)
        {
            _elapsedSeconds += deltaSeconds;
            var progress = Math.Min(1.0f, _elapsedSeconds / _stepSeconds);
            DistanceMeters = _stepFrom + ((_stepTo - _stepFrom) * MinimumJerk(progress));
            SideMeters = _stepFromSide + ((_stepToSide - _stepFromSide) * MinimumJerk(progress));
            _moveHipOffset = new Vector3(0.0f, -_stepHipDipMeters * SwingBump(progress), 0.0f);

            // The front foot leads an advance and a side step (it opens the way and the back foot closes the stance), the
            // back foot a retreat.
            var isAdvance = CurrentStep != FootworkStep.Retreating;
            var frontPitch = FootworkTuning.FrontFootToeUpDegrees * DegreesToRadians * _stepPitchScale;
            var backPitch = -FootworkTuning.BackFootHeelUpDegrees * DegreesToRadians * _stepPitchScale;
            _frontFoot = GetStepSwing(progress, isLeadFoot: isAdvance, frontPitch);
            _backFoot = GetStepSwing(progress, isLeadFoot: !isAdvance, backPitch);
            IsFrontFootPlanted = !IsInStepSwing(progress, isLeadFoot: isAdvance);
            IsBackFootPlanted = !IsInStepSwing(progress, isLeadFoot: !isAdvance);

            if (progress >= 1.0f)
            {
                DistanceMeters = _stepTo;
                SideMeters = _stepToSide;
                CurrentStep = FootworkStep.Standing;
                Stand();
            }
        }

        private FootState GetStepSwing(float stepProgress, bool isLeadFoot, float pitchRadians)
        {
            var (start, end) = GetStepSwingWindow(isLeadFoot);
            var swing = (stepProgress - start) / (end - start);
            var shape = SwingBump(swing);
            var travelled = MinimumJerk(swing);
            var anchor = _stepFrom + ((_stepTo - _stepFrom) * travelled);
            var sideAnchor = _stepFromSide + ((_stepToSide - _stepFromSide) * travelled);
            return new FootState(anchor, sideAnchor, _stepLiftMeters * shape, 0.0f, pitchRadians * shape);
        }

        private bool IsInStepSwing(float stepProgress, bool isLeadFoot)
        {
            var (start, end) = GetStepSwingWindow(isLeadFoot);
            return stepProgress > start && stepProgress < end;
        }

        private (float Start, float End) GetStepSwingWindow(bool isLeadFoot)
        {
            return isLeadFoot ? (0.0f, _leadLandsAt) : (_trailLiftsAt, 1.0f);
        }

        // ---- Lunge and recovery ----

        private void StartLunge(float lungeLengthMeters)
        {
            _lungeStart = DistanceMeters;
            _lungeLengthMeters = lungeLengthMeters;
            _lungePeakMeters = 0.0f;
            _backDragMeters = 0.0f;
            CurrentStep = FootworkStep.Lunging;
        }

        private void FollowLunge(float offsetMeters)
        {
            if (CurrentStep == FootworkStep.Lunging && offsetMeters < _lungePeakMeters - LungeSettledMeters)
            {
                // The offset turned back: the recovery starts from the feet as they were at the lunge's peak.
                CurrentStep = FootworkStep.Recovering;
            }

            if (CurrentStep == FootworkStep.Recovering && offsetMeters <= LungeSettledMeters)
            {
                DistanceMeters = _lungeStart;
                CurrentStep = FootworkStep.Standing;
                Stand();
                return;
            }

            DistanceMeters = _lungeStart - offsetMeters;
            _moveHipOffset = GetLungeHipOffset(_heightMeters, offsetMeters, _lungeLengthMeters);

            if (CurrentStep == FootworkStep.Lunging)
            {
                FollowLungeOut(offsetMeters);
            }
            else
            {
                FollowRecovery(offsetMeters);
            }
        }

        /// <summary>Out: the front foot leads the body and lands as it arrives; the back foot stays unless it must drag.</summary>
        private void FollowLungeOut(float offsetMeters)
        {
            var fraction = Clamp01(offsetMeters / _lungeLengthMeters);
            var shape = SwingBump(fraction);
            var frontTravel = offsetMeters + (FootworkTuning.LungeFrontFootLeadPerHeight * _heightMeters * shape);
            _frontFoot = new FootState(
                _lungeStart - frontTravel,
                SideMeters,
                FootworkTuning.LungeFootLiftPerHeight * _heightMeters * shape,
                0.0f,
                FootworkTuning.LungeFrontFootToeUpDegrees * DegreesToRadians * shape);

            // The back ankle may be no further behind the hip than the leg reaches along the floor.
            var hip = GetHipOffset();
            var reach = _back.GetFloorReachMeters(hip, FootworkTuning.LungeBackLegReachFraction, ToRightHandedX(SideMeters));
            var lowestAnkleOffset = (_back.Rest.Hip.Z + hip.Z - reach) - _back.Rest.Ankle.Z;
            var neededDrag = Math.Max(0.0f, offsetMeters + lowestAnkleOffset);
            var isDragging = neededDrag > _backDragMeters;
            _backDragMeters = Math.Max(_backDragMeters, neededDrag);
            _backFoot = FootState.PlantedAt(_lungeStart - _backDragMeters, SideMeters);

            IsFrontFootPlanted = shape <= 0.0f;
            IsBackFootPlanted = !isDragging;
            _lungePeakMeters = Math.Max(_lungePeakMeters, offsetMeters);
            _frontTravelAtPeak = frontTravel;
            _frontFootAtPeak = _frontFoot;
            _backDragAtPeak = _backDragMeters;
        }

        /// <summary>
        /// Back: both feet come back in proportion to the body; the front foot lifts, and a dragged back foot steps. A
        /// lunge cut short (parried) recovers from the front foot still in the air, lowering it as the body comes back.
        /// </summary>
        private void FollowRecovery(float offsetMeters)
        {
            var remaining = Clamp01(offsetMeters / _lungePeakMeters);
            var shape = SwingBump(1.0f - remaining);
            var reachedFraction = Math.Min(1.0f, _frontTravelAtPeak / _lungeLengthMeters);

            // The feet come back a little ahead of the body early on and settle as it slows: their top speed stays below
            // the body's, which in the Armed Dummy's recovery is already near the Blade's sweep.
            var back = 1.0f - remaining;
            var footBack = back + (FootworkTuning.RecoveryFootPacing * (float)Math.Sin(back * 2.0 * Math.PI) / (float)(2.0 * Math.PI));
            var footRemaining = 1.0f - footBack;
            _frontFoot = new FootState(
                _lungeStart - (_frontTravelAtPeak * footRemaining),
                SideMeters,
                (_frontFootAtPeak.LiftMeters * remaining) + (FootworkTuning.RecoveryFootLiftPerHeight * _heightMeters * shape * reachedFraction),
                0.0f,
                (_frontFootAtPeak.PitchRadians * remaining) + (FootworkTuning.RecoveryFrontFootToeUpDegrees * DegreesToRadians * shape));
            var backLift = _backDragAtPeak > 0.0f ? FootworkTuning.RecoveryBackFootLiftPerHeight * _heightMeters * shape : 0.0f;
            _backFoot = new FootState(_lungeStart - (_backDragAtPeak * footRemaining), SideMeters, backLift, 0.0f, 0.0f);

            IsFrontFootPlanted = false;
            IsBackFootPlanted = _backDragAtPeak <= 0.0f;
        }

        // ---- Fleche ----

        private void ContinueFleche(float deltaSeconds)
        {
            _elapsedSeconds += deltaSeconds;
            var progress = Math.Min(1.0f, _elapsedSeconds / FootworkTuning.FlecheSeconds);
            var length = FlecheLengthMeters;
            DistanceMeters = _flecheFrom - (length * MinimumJerk(progress));
            _moveHipOffset = new Vector3(0.0f, -FootworkTuning.FlecheHipDropPerHeight * _heightMeters * SwingBump(progress), 0.0f);

            // The back foot crosses over on its own side (-x for a right-handed Dummy) to land at its new en garde spot;
            // the front foot then runs past it on its side (+x).
            var backSwing = (progress - FootworkTuning.FlecheBackFootSwingStart) / (FootworkTuning.FlecheBackFootSwingEnd - FootworkTuning.FlecheBackFootSwingStart);
            var frontSwing = (progress - FootworkTuning.FlecheFrontFootSwingStart) / (FootworkTuning.FlecheFrontFootSwingEnd - FootworkTuning.FlecheFrontFootSwingStart);
            _backFoot = GetFlecheSwing(backSwing, length, -1.0f, -FootworkTuning.BackFootHeelUpDegrees * DegreesToRadians);
            _frontFoot = GetFlecheSwing(frontSwing, length, 1.0f, FootworkTuning.FrontFootToeUpDegrees * DegreesToRadians);
            IsBackFootPlanted = backSwing <= 0.0f || backSwing >= 1.0f;
            IsFrontFootPlanted = frontSwing <= 0.0f || frontSwing >= 1.0f;

            if (progress >= 1.0f)
            {
                DistanceMeters = _flecheFrom - length;
                CurrentStep = FootworkStep.Standing;
                Stand();
            }
        }

        private FootState GetFlecheSwing(float swing, float lengthMeters, float sideSign, float pitchRadians)
        {
            var shape = SwingBump(swing);
            return new FootState(
                _flecheFrom - (lengthMeters * MinimumJerk(swing)),
                SideMeters,
                FootworkTuning.FlecheFootLiftPerHeight * _heightMeters * shape,
                sideSign * FootworkTuning.FlecheFootSidestepPerHeight * _heightMeters * shape,
                pitchRadians * shape);
        }

        private static void RequireFinite(float meters, string name)
        {
            if (float.IsNaN(meters) || float.IsInfinity(meters))
            {
                throw new ArgumentOutOfRangeException(name, meters, "Must be a finite number of meters.");
            }
        }

        /// <summary>
        /// Where one foot is: its spot along and across the Strip, how high it is and how far it swings out sideways
        /// (right-handed), its pitch.
        /// </summary>
        private readonly struct FootState
        {
            public FootState(float anchor, float sideAnchor, float liftMeters, float swingOutMeters, float pitchRadians)
            {
                Anchor = anchor;
                SideAnchor = sideAnchor;
                LiftMeters = liftMeters;
                SwingOutMeters = swingOutMeters;
                PitchRadians = pitchRadians;
            }

            /// <summary>The foot's spot along the Strip, as the body distance at which that spot is the foot's en garde place.</summary>
            public float Anchor { get; }

            /// <summary>The foot's spot across the Strip, as the body side (<see cref="DummyFootwork.SideMeters"/>) at which that spot is the foot's en garde place.</summary>
            public float SideAnchor { get; }

            /// <summary>How high the foot is off the floor.</summary>
            public float LiftMeters { get; }

            /// <summary>How far the foot swings out to the Dummy's right of its spot (right-handed): a fleche's foot passing the other.</summary>
            public float SwingOutMeters { get; }

            /// <summary>The foot's pitch: positive lifts the toe.</summary>
            public float PitchRadians { get; }

            public static FootState PlantedAt(float anchor, float sideAnchor)
            {
                return new FootState(anchor, sideAnchor, 0.0f, 0.0f, 0.0f);
            }
        }
    }
}
