using System;
using System.Numerics;
using VrFencing.Settings;

namespace VrFencing.Dummy
{
    /// <summary>How fast the Armed Dummy's point travels in a thrust (PER-23).</summary>
    public enum AttackSpeed
    {
        Slow,
        Medium,
        Real,
    }

    /// <summary>Where an Armed Dummy attack is in its script.</summary>
    public enum AttackPhase
    {
        /// <summary>En garde with the blade in line, before the random delay runs out.</summary>
        Waiting,

        /// <summary>The short, visible draw-back before the thrust.</summary>
        WindUp,

        /// <summary>The point travels straight to the aim.</summary>
        Thrust,

        /// <summary>The point stays at the aim a moment.</summary>
        Hold,

        /// <summary>Back to en garde, after the hold or at once when parried.</summary>
        Recover,

        /// <summary>En garde again; the attack is over.</summary>
        Done,
    }

    /// <summary>
    /// One scripted straight thrust of the Armed Dummy (PER-96, from the PER-95 prototype), as sword arm poses over
    /// time: en garde with the blade in line (<see cref="SwordArmReach.PoseEnGarde"/>) for a random delay, a short
    /// wind-up, the thrust, a short hold, then back to en garde. A Parry (<see cref="ReportParried"/>) cuts the thrust
    /// or hold short and recovers from where the arm is; landing on the Fencer (<see cref="ReportLanded"/>) stops the
    /// thrust where it is and holds there. Only the arm moves; the feet stay planted.
    ///
    /// The thrust moves the point along a straight line from the wind-up to the aim (<see cref="Path"/>), the arm
    /// posed by <see cref="SwordArmReach.PoseToReach"/> at each instant, so every pose is within the joint limits and
    /// the point's peak speed is the speed level's. All numbers are in <see cref="ArmedDummyTuning"/>.
    /// Poses are right-handed (<see cref="SwordArmPose"/>); positions are in the Dummy's own frame. Times in seconds on
    /// any one clock.
    /// </summary>
    public sealed class ArmedDummyAttack
    {
        private readonly double _startSeconds;
        private readonly float _heightMeters;
        private readonly float _bladeLengthMeters;
        private readonly SwordArmPose _guard;
        private readonly SwordArmPose _windUp;
        private readonly SwordArmPose _thrust;
        private readonly Vector3 _rightHandedStart;
        private readonly Vector3 _rightHandedAim;
        private double _recoverStartSeconds;
        private SwordArmPose _recoverFrom;
        private double _holdStartSeconds;
        private SwordArmPose _holdPose;

        private ArmedDummyAttack(
            double startSeconds, float delaySeconds, float heightMeters, Hand hand, Vector3 rightHandedAim, AttackSpeed speed, float bladeLengthMeters)
        {
            _startSeconds = startSeconds;
            _heightMeters = heightMeters;
            _bladeLengthMeters = bladeLengthMeters;
            DelaySeconds = delaySeconds;
            Speed = speed;

            _guard = SwordArmReach.PoseEnGarde(heightMeters, bladeLengthMeters);
            _windUp = SwordArmReach.PoseWindUp(heightMeters, bladeLengthMeters);
            _rightHandedStart = SwordArmReach.GetPoint(heightMeters, _windUp, bladeLengthMeters);
            _thrust = SwordArmReach.PoseToReach(heightMeters, rightHandedAim, bladeLengthMeters);
            _rightHandedAim = SwordArmReach.GetPoint(heightMeters, _thrust, bladeLengthMeters);

            var lengthMeters = Vector3.Distance(_rightHandedStart, _rightHandedAim);
            ThrustSeconds = Math.Max(
                ArmedDummyTuning.MinimumThrustSeconds, AttackPath.MinimumJerkPeakFactor * lengthMeters / GetPeakPointSpeed(speed));
            Path = new AttackPath(ToHand(_rightHandedStart, hand), ToHand(_rightHandedAim, hand), ThrustStartSeconds, ThrustSeconds);

            _holdStartSeconds = ThrustEndSeconds;
            _holdPose = _thrust;
            _recoverStartSeconds = ThrustEndSeconds + ArmedDummyTuning.HoldSeconds;
            _recoverFrom = _thrust;
        }

        public float DelaySeconds { get; }

        public AttackSpeed Speed { get; }

        public float ThrustSeconds { get; }

        /// <summary>The point's planned straight path in the thrust, in the Dummy frame, for judging.</summary>
        public AttackPath Path { get; }

        /// <summary>When the wind-up starts.</summary>
        public double WindUpStartSeconds => _startSeconds + DelaySeconds;

        /// <summary>When the thrust starts, at the end of the wind-up.</summary>
        public double ThrustStartSeconds => WindUpStartSeconds + ArmedDummyTuning.WindUpSeconds;

        /// <summary>When the point reaches the aim, unless parried before.</summary>
        public double ThrustEndSeconds => ThrustStartSeconds + ThrustSeconds;

        /// <summary>When the recovery is over and the arm is en garde again.</summary>
        public double DoneSeconds => _recoverStartSeconds + ArmedDummyTuning.RecoverSeconds;

        /// <summary>True once a Parry cut the thrust or the hold short.</summary>
        public bool IsParried { get; private set; }

        /// <summary>True once the point landed on the Fencer (<see cref="ReportLanded"/>).</summary>
        public bool HasLanded { get; private set; }

        /// <summary>The point's peak speed for a speed level (<see cref="ArmedDummyTuning"/>).</summary>
        /// <exception cref="ArgumentOutOfRangeException">Not a speed level.</exception>
        public static float GetPeakPointSpeed(AttackSpeed speed)
        {
            switch (speed)
            {
                case AttackSpeed.Slow:
                    return ArmedDummyTuning.SlowPointSpeedMetersPerSecond;
                case AttackSpeed.Medium:
                    return ArmedDummyTuning.MediumPointSpeedMetersPerSecond;
                case AttackSpeed.Real:
                    return ArmedDummyTuning.RealPointSpeedMetersPerSecond;
                default:
                    throw new ArgumentOutOfRangeException(nameof(speed), speed, "Not an attack speed.");
            }
        }

        /// <summary>A random delay from <see cref="ArmedDummyTuning.MinimumDelaySeconds"/> to <see cref="ArmedDummyTuning.MaximumDelaySeconds"/>.</summary>
        public static float DrawDelaySeconds(Random random)
        {
            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            return ArmedDummyTuning.MinimumDelaySeconds
                + ((ArmedDummyTuning.MaximumDelaySeconds - ArmedDummyTuning.MinimumDelaySeconds) * (float)random.NextDouble());
        }

        /// <summary>
        /// Plans a thrust at <paramref name="aim"/> (in the Dummy frame, see <see cref="AttackAim"/>) for a Dummy of
        /// <paramref name="heightMeters"/> and <paramref name="hand"/> holding a blade of <paramref name="bladeLengthMeters"/>:
        /// en garde for <paramref name="delaySeconds"/> from <paramref name="startSeconds"/>, the wind-up, then the
        /// thrust at the speed level's speed. An aim out of reach gets the point as near it as the arm reaches.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The delay is outside <see cref="ArmedDummyTuning.MinimumDelaySeconds"/> to
        /// <see cref="ArmedDummyTuning.MaximumDelaySeconds"/>, the height is not positive, or the speed is not a level.
        /// </exception>
        public static ArmedDummyAttack Plan(
            double startSeconds, float delaySeconds, float heightMeters, Hand hand, Vector3 aim, AttackSpeed speed, float bladeLengthMeters)
        {
            if (!(delaySeconds >= ArmedDummyTuning.MinimumDelaySeconds && delaySeconds <= ArmedDummyTuning.MaximumDelaySeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(delaySeconds), delaySeconds,
                    $"Must be from {ArmedDummyTuning.MinimumDelaySeconds} to {ArmedDummyTuning.MaximumDelaySeconds} s.");
            }

            DummyLayout.RequirePositiveHeight(heightMeters);
            GetPeakPointSpeed(speed);
            return new ArmedDummyAttack(startSeconds, delaySeconds, heightMeters, hand, ToHand(aim, hand), speed, bladeLengthMeters);
        }

        public AttackPhase GetPhase(double nowSeconds)
        {
            if (nowSeconds < WindUpStartSeconds)
            {
                return AttackPhase.Waiting;
            }

            if (nowSeconds >= DoneSeconds)
            {
                return AttackPhase.Done;
            }

            if (nowSeconds >= _recoverStartSeconds)
            {
                return AttackPhase.Recover;
            }

            if (nowSeconds < ThrustStartSeconds)
            {
                return AttackPhase.WindUp;
            }

            return nowSeconds < _holdStartSeconds ? AttackPhase.Thrust : AttackPhase.Hold;
        }

        /// <summary>The right-handed arm pose at <paramref name="nowSeconds"/> (<see cref="SwordArm"/> mirrors it for a left hand).</summary>
        public SwordArmPose GetPose(double nowSeconds)
        {
            switch (GetPhase(nowSeconds))
            {
                case AttackPhase.WindUp:
                    return SwordArmPose.Slerp(_guard, _windUp, SmoothStep(Fraction(nowSeconds, WindUpStartSeconds, ArmedDummyTuning.WindUpSeconds)));
                case AttackPhase.Thrust:
                    var point = Vector3.Lerp(_rightHandedStart, _rightHandedAim, Path.GetProgress(nowSeconds));
                    return SwordArmReach.PoseToReach(_heightMeters, point, _bladeLengthMeters);
                case AttackPhase.Hold:
                    return _holdPose;
                case AttackPhase.Recover:
                    return SwordArmPose.Slerp(
                        _recoverFrom, _guard, AttackPath.MinimumJerk(Fraction(nowSeconds, _recoverStartSeconds, ArmedDummyTuning.RecoverSeconds)));
                default:
                    return _guard;
            }
        }

        /// <summary>
        /// The Fencer's blade met the attacking blade: during the thrust or the hold the attack is parried and recovers
        /// from the arm's pose now. Contact in any other phase (a Beat on the blade en garde) changes nothing.
        /// </summary>
        /// <returns>True if this parried the attack.</returns>
        public bool ReportParried(double nowSeconds)
        {
            var phase = GetPhase(nowSeconds);
            if (IsParried || (phase != AttackPhase.Thrust && phase != AttackPhase.Hold))
            {
                return false;
            }

            _recoverFrom = GetPose(nowSeconds);
            _recoverStartSeconds = nowSeconds;
            IsParried = true;
            return true;
        }

        /// <summary>
        /// The point landed on the Fencer (PER-98) during the thrust: the arm stops where it is, holds there for
        /// <see cref="ArmedDummyTuning.HoldSeconds"/> and recovers, so the point does not run on through the body. During
        /// the hold it only marks the landing; the hold ends as planned.
        /// </summary>
        /// <returns>True if this was the landing (the thrust or hold, not parried, not landed before).</returns>
        public bool ReportLanded(double nowSeconds)
        {
            var phase = GetPhase(nowSeconds);
            if (IsParried || HasLanded || (phase != AttackPhase.Thrust && phase != AttackPhase.Hold))
            {
                return false;
            }

            HasLanded = true;
            if (phase == AttackPhase.Thrust)
            {
                _holdPose = GetPose(nowSeconds);
                _holdStartSeconds = nowSeconds;
                _recoverFrom = _holdPose;
                _recoverStartSeconds = nowSeconds + ArmedDummyTuning.HoldSeconds;
            }

            return true;
        }

        /// <summary>Between the Dummy frame and the right-handed Dummy frame: a reflection, its own inverse.</summary>
        private static Vector3 ToHand(Vector3 position, Hand hand)
        {
            return hand == Hand.Left ? HandMirroring.MirrorPosition(position) : position;
        }

        private static float Fraction(double nowSeconds, double startSeconds, float durationSeconds)
        {
            return (float)Math.Clamp((nowSeconds - startSeconds) / durationSeconds, 0.0, 1.0);
        }

        private static float SmoothStep(float fraction)
        {
            return fraction * fraction * (3.0f - (2.0f * fraction));
        }
    }
}
