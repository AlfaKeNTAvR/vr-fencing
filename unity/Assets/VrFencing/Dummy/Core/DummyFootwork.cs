using System;
using System.Numerics;
using VrFencing.Settings;

namespace VrFencing.Dummy
{
    /// <summary>What the Dummy's feet are doing.</summary>
    public enum FootworkStep
    {
        /// <summary>Both feet planted in en garde.</summary>
        Standing,

        /// <summary>Stepping toward the Fencer: front foot first, then the back foot.</summary>
        Advancing,

        /// <summary>Stepping away from the Fencer: back foot first, then the front foot.</summary>
        Retreating,
    }

    /// <summary>
    /// The Dummy's footwork synthesizer (PER-110, stage 1 of PER-108): it carries the Dummy to a target distance on the
    /// Strip in real steps, advancing and retreating, and gives the legs' joints every frame.
    ///
    /// Input: the intent, where the Dummy should stand (its distance, front foot to the Fencer's En Garde Line, e.g.
    /// <see cref="DistanceKeeper.TargetFrontFootZ"/>). Output: <see cref="DistanceMeters"/>, where the body is (what
    /// <see cref="DummyLayout.DummyToStrip"/> places the Dummy frame at: the spot under the body where its front foot
    /// stands in en garde), and <see cref="Legs"/>, the leg joints in the Dummy frame.
    ///
    /// Rules:
    /// - A step covers at most <see cref="FootworkTuning.MaxStridePerHeight"/>; a longer move is several steps, each
    ///   planned from where the body is when it starts. A step is finished before the next one starts, even when the
    ///   target turns back meanwhile, as a fencer finishes a step.
    /// - The body moves on a minimum-jerk curve (Flash and Hogan 1985): it starts and stops with no jolt.
    /// - Advance: the front foot swings first, the back foot follows; retreat: the reverse. Each foot swings on its own
    ///   minimum-jerk curve with a low lift, the front foot toe up, the back foot heel up.
    /// - A planted foot is pinned to its spot on the Strip: it never slides while the body moves over it.
    /// - Knees come from analytic two-bone IK (<see cref="TwoBoneIk"/>), bending the way they bend in en garde; hips
    ///   and the upper body ride rigidly with the body, which keeps the head level as fencing footwork should.
    /// - Each step's duration and lift vary slightly, drawn from a seeded random generator.
    ///
    /// Nothing is allocated per frame.
    /// </summary>
    public sealed class DummyFootwork
    {
        private readonly float _heightMeters;
        private readonly Hand _hand;
        private readonly Random _random;
        private readonly LegRig _front;
        private readonly LegRig _back;

        // Each foot's spot on the Strip, as the body distance at which that spot is its en garde place.
        private float _frontFootAnchor;
        private float _backFootAnchor;

        private float _stepFrom;
        private float _stepTo;
        private float _stepSeconds;
        private float _stepElapsedSeconds;
        private float _stepLiftMeters;

        /// <param name="heightMeters">The Dummy's height; strides and lifts scale with it.</param>
        /// <param name="hand">The Dummy's sword hand: a left-handed Dummy's legs are mirrored.</param>
        /// <param name="startDistanceMeters">Where the Dummy stands now, in en garde.</param>
        /// <param name="random">Draws each step's small variations; seed it for repeatable tests.</param>
        /// <exception cref="ArgumentOutOfRangeException">The height is not positive, or the start not a finite number.</exception>
        public DummyFootwork(float heightMeters, Hand hand, float startDistanceMeters, Random random)
        {
            DummyLayout.RequirePositiveHeight(heightMeters);
            RequireFinite(startDistanceMeters, nameof(startDistanceMeters));
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _heightMeters = heightMeters;
            _hand = hand;
            _front = new LegRig(DummyLegs.FrontAtUnitHeight, heightMeters, FootworkTuning.FrontFootToeUpDegrees);
            _back = new LegRig(DummyLegs.BackAtUnitHeight, heightMeters, -FootworkTuning.BackFootHeelUpDegrees);
            DistanceMeters = startDistanceMeters;
            _frontFootAnchor = startDistanceMeters;
            _backFootAnchor = startDistanceMeters;
            Legs = DummyLegs.EnGarde(heightMeters, hand);
        }

        /// <summary>Where the body is on the Strip: the Dummy frame's distance (see the class summary).</summary>
        public float DistanceMeters { get; private set; }

        /// <summary>The leg joints in the Dummy frame, mirrored for a left-handed Dummy.</summary>
        public DummyLegs Legs { get; private set; }

        public FootworkStep CurrentStep { get; private set; } = FootworkStep.Standing;

        /// <summary>How many steps have started since the Dummy was made.</summary>
        public int StepCount { get; private set; }

        /// <summary>Whether the front foot is on the floor, pinned to its spot.</summary>
        public bool IsFrontFootPlanted => !IsSwinging(isFrontFoot: true);

        /// <summary>Whether the back foot is on the floor, pinned to its spot.</summary>
        public bool IsBackFootPlanted => !IsSwinging(isFrontFoot: false);

        /// <summary>
        /// One frame: moves toward <paramref name="targetDistanceMeters"/> for <paramref name="deltaSeconds"/> and
        /// updates <see cref="DistanceMeters"/>, <see cref="Legs"/> and <see cref="CurrentStep"/>.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The target is not a finite number.</exception>
        public void Step(float targetDistanceMeters, float deltaSeconds)
        {
            RequireFinite(targetDistanceMeters, nameof(targetDistanceMeters));
            if (!(deltaSeconds > 0.0f))
            {
                return;
            }

            if (CurrentStep == FootworkStep.Standing)
            {
                var wanted = targetDistanceMeters - DistanceMeters;
                if (Math.Abs(wanted) <= FootworkTuning.MinStepMeters)
                {
                    return;
                }

                StartStep(wanted);
            }

            _stepElapsedSeconds += deltaSeconds;
            var progress = Math.Min(1.0f, _stepElapsedSeconds / _stepSeconds);
            DistanceMeters = _stepFrom + ((_stepTo - _stepFrom) * MinimumJerk(progress));

            var isAdvance = CurrentStep == FootworkStep.Advancing;
            var front = GetFootSwing(progress, isLeadFoot: isAdvance);
            var back = GetFootSwing(progress, isLeadFoot: !isAdvance);
            _frontFootAnchor = front.Anchor;
            _backFootAnchor = back.Anchor;
            Legs = new DummyLegs(
                _front.Pose(DistanceMeters - _frontFootAnchor, front.Lift, front.Shape).ScaledForHand(1.0f, _hand),
                _back.Pose(DistanceMeters - _backFootAnchor, back.Lift, back.Shape).ScaledForHand(1.0f, _hand));

            if (progress >= 1.0f)
            {
                DistanceMeters = _stepTo;
                _frontFootAnchor = _stepTo;
                _backFootAnchor = _stepTo;
                Legs = DummyLegs.EnGarde(_heightMeters, _hand);
                CurrentStep = FootworkStep.Standing;
            }
        }

        /// <summary>The minimum-jerk curve from 0 to 1: zero speed and acceleration at both ends.</summary>
        internal static float MinimumJerk(float fraction)
        {
            var t = Math.Min(Math.Max(fraction, 0.0f), 1.0f);
            return t * t * t * ((t * ((t * 6.0f) - 15.0f)) + 10.0f);
        }

        /// <summary>A bump from 0 up to 1 at the middle and back to 0, with zero speed at both ends.</summary>
        private static float SwingBump(float fraction)
        {
            var t = Math.Min(Math.Max(fraction, 0.0f), 1.0f);
            return 16.0f * t * t * (1.0f - t) * (1.0f - t);
        }

        private void StartStep(float wantedMeters)
        {
            var maxStride = FootworkTuning.MaxStridePerHeight * _heightMeters;
            var stride = Math.Min(Math.Max(wantedMeters, -maxStride), maxStride);
            var strideFraction = Math.Abs(stride) / maxStride;
            var nominalSeconds = FootworkTuning.ShortStepSeconds + ((FootworkTuning.FullStepSeconds - FootworkTuning.ShortStepSeconds) * strideFraction);

            _stepFrom = DistanceMeters;
            _stepTo = DistanceMeters + stride;
            _stepSeconds = nominalSeconds * (1.0f + (DrawVariation() * FootworkTuning.DurationVariationFraction));
            _stepLiftMeters = FootworkTuning.FootLiftPerHeight * _heightMeters * (1.0f + (DrawVariation() * FootworkTuning.LiftVariationFraction));
            _stepElapsedSeconds = 0.0f;

            // The Dummy faces the Fencer: a smaller distance is toward them, an advance.
            CurrentStep = stride < 0.0f ? FootworkStep.Advancing : FootworkStep.Retreating;
            StepCount++;
        }

        /// <summary>Evenly between -1 and 1.</summary>
        private float DrawVariation()
        {
            return ((float)_random.NextDouble() * 2.0f) - 1.0f;
        }

        private bool IsSwinging(bool isFrontFoot)
        {
            if (CurrentStep == FootworkStep.Standing)
            {
                return false;
            }

            var progress = Math.Min(1.0f, _stepElapsedSeconds / _stepSeconds);
            var isLeadFoot = isFrontFoot == (CurrentStep == FootworkStep.Advancing);
            var window = GetSwingWindow(isLeadFoot);
            return progress > window.Start && progress < window.End;
        }

        private FootSwing GetFootSwing(float stepProgress, bool isLeadFoot)
        {
            var window = GetSwingWindow(isLeadFoot);
            var swing = (stepProgress - window.Start) / (window.End - window.Start);
            var anchor = _stepFrom + ((_stepTo - _stepFrom) * MinimumJerk(swing));
            var shape = SwingBump(swing);
            return new FootSwing(anchor, _stepLiftMeters * shape, shape);
        }

        private static (float Start, float End) GetSwingWindow(bool isLeadFoot)
        {
            return isLeadFoot ? (0.0f, FootworkTuning.LeadFootLandsAtFraction) : (FootworkTuning.TrailFootLiftsAtFraction, 1.0f);
        }

        private static void RequireFinite(float meters, string name)
        {
            if (float.IsNaN(meters) || float.IsInfinity(meters))
            {
                throw new ArgumentOutOfRangeException(name, meters, "Must be a finite number of meters.");
            }
        }

        private readonly struct FootSwing
        {
            public FootSwing(float anchor, float lift, float shape)
            {
                Anchor = anchor;
                Lift = lift;
                Shape = shape;
            }

            /// <summary>The foot's spot on the Strip, as a body distance (see the anchors above).</summary>
            public float Anchor { get; }

            /// <summary>How high the foot is off the floor, in meters.</summary>
            public float Lift { get; }

            /// <summary>0 planted, 1 at mid-swing: scales the foot's pitch.</summary>
            public float Shape { get; }
        }

        /// <summary>
        /// One leg of a right-handed Dummy in meters: its en garde joints, bone lengths and knee direction, and how it
        /// is posed for a foot that is moved, lifted and pitched.
        /// </summary>
        private sealed class LegRig
        {
            private readonly LegPose _rest;
            private readonly float _thighMeters;
            private readonly float _shinMeters;
            private readonly Vector3 _kneeDirection;
            private readonly Vector3 _pitchAxis;
            private readonly float _swingPitchRadians;

            public LegRig(LegPose restAtUnitHeight, float heightMeters, float swingPitchDegrees)
            {
                _rest = restAtUnitHeight.ScaledForHand(heightMeters, Hand.Right);
                _thighMeters = Vector3.Distance(_rest.Hip, _rest.Knee);
                _shinMeters = Vector3.Distance(_rest.Knee, _rest.Ankle);

                // The knee bends where it bends in en garde, so the rest pose solves back to itself.
                var hipToAnkle = Vector3.Normalize(_rest.Ankle - _rest.Hip);
                var hipToKnee = _rest.Knee - _rest.Hip;
                _kneeDirection = Vector3.Normalize(hipToKnee - (hipToAnkle * Vector3.Dot(hipToKnee, hipToAnkle)));

                // Toe up is a turn about the foot's sideways axis (heel-to-toe crossed with up).
                var heelToToe = _rest.Toe - _rest.Heel;
                _pitchAxis = Vector3.Normalize(Vector3.Cross(new Vector3(heelToToe.X, 0.0f, heelToToe.Z), Vector3.UnitY));
                _swingPitchRadians = swingPitchDegrees * (float)(Math.PI / 180.0);
            }

            /// <param name="forwardMeters">How far the foot is in front of its en garde place, along the Dummy's +z.</param>
            /// <param name="liftMeters">How far its lowest point is off the floor.</param>
            /// <param name="pitchShape">0 to 1: how much of the swing pitch the foot has.</param>
            public LegPose Pose(float forwardMeters, float liftMeters, float pitchShape)
            {
                var pitch = Quaternion.CreateFromAxisAngle(_pitchAxis, _swingPitchRadians * pitchShape);
                var heelFromAnkle = Vector3.Transform(_rest.Heel - _rest.Ankle, pitch);
                var toeFromAnkle = Vector3.Transform(_rest.Toe - _rest.Ankle, pitch);

                // A pitched foot is raised so its lowest end is no lower than a flat one's: it never dips into the floor.
                var restLowest = Math.Min(_rest.Heel.Y, _rest.Toe.Y) - _rest.Ankle.Y;
                var pitchedLowest = Math.Min(heelFromAnkle.Y, toeFromAnkle.Y);
                var raise = Math.Max(0.0f, restLowest - pitchedLowest);

                var ankleTarget = _rest.Ankle + new Vector3(0.0f, liftMeters + raise, forwardMeters);
                var solution = TwoBoneIk.Solve(_rest.Hip, ankleTarget, _thighMeters, _shinMeters, _kneeDirection);
                return new LegPose(_rest.Hip, solution.Middle, solution.End, solution.End + heelFromAnkle, solution.End + toeFromAnkle);
            }
        }
    }
}
