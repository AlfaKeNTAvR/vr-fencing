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
        /// <summary>En garde, before the random delay runs out.</summary>
        Waiting,

        /// <summary>The short, visible draw-back before the thrust.</summary>
        WindUp,

        /// <summary>The point travels to the aim.</summary>
        Thrust,

        /// <summary>The point stays at the aim a moment.</summary>
        Hold,

        /// <summary>Back to en garde, after the hold or at once when parried.</summary>
        Recover,

        /// <summary>En garde again; the attack is over.</summary>
        Done,
    }

    /// <summary>
    /// One scripted straight thrust of the Armed Dummy (PER-23, prototype PER-95), as arm poses over time: en garde
    /// (<see cref="SwordArmReach.PoseEnGarde"/>) for a random delay, a short wind-up, the thrust to the aim at the attack's speed, a short hold, then back to
    /// en garde. A Parry (<see cref="ReportParried"/>) cuts the thrust or hold short and recovers from where the arm is.
    ///
    /// The thrust follows a minimum-jerk profile (smooth start and stop, like a reaching arm); its length in time is set
    /// so the point's peak speed is the attack's speed. Poses are right-handed (<see cref="SwordArmPose"/>).
    /// Times in seconds on any one clock.
    /// </summary>
    public sealed class ArmedDummyAttack
    {
        /// <summary>The random delay before the wind-up starts, at least (PER-23: 1 to 3 s).</summary>
        public const float MinimumDelaySeconds = 1.0f;

        /// <inheritdoc cref="MinimumDelaySeconds"/>
        public const float MaximumDelaySeconds = 3.0f;

        /// <summary>The wind-up: short, but long enough to read as the cue.</summary>
        public const float WindUpSeconds = 0.35f;

        /// <summary>How long the point stays at the aim before recovering.</summary>
        public const float HoldSeconds = 0.3f;

        /// <summary>Back to en garde.</summary>
        public const float RecoverSeconds = 0.6f;

        /// <summary>Peak speed of a minimum-jerk move as a multiple of its average speed.</summary>
        public const float MinimumJerkPeakFactor = 1.875f;

        /// <summary>Points sampled along the thrust's path to measure its length.</summary>
        private const int PathSamples = 32;

        private readonly double _startSeconds;
        private readonly SwordArmPose _guard;
        private readonly SwordArmPose _windUp;
        private readonly SwordArmPose _thrust;
        private double _recoverStartSeconds;
        private SwordArmPose _recoverFrom;

        /// <param name="startSeconds">When the attack begins (the delay runs from here).</param>
        /// <param name="delaySeconds">How long the arm stays en garde before the wind-up.</param>
        /// <param name="guard">The en garde pose before and after.</param>
        /// <param name="windUp">The pose at the end of the wind-up.</param>
        /// <param name="thrust">The pose with the point at the aim.</param>
        /// <param name="thrustSeconds">How long the thrust takes from the wind-up to the aim.</param>
        /// <exception cref="ArgumentOutOfRangeException">The delay is negative or the thrust takes no time.</exception>
        public ArmedDummyAttack(double startSeconds, float delaySeconds, SwordArmPose guard, SwordArmPose windUp, SwordArmPose thrust, float thrustSeconds)
        {
            if (!(delaySeconds >= 0.0f) || float.IsInfinity(delaySeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(delaySeconds), delaySeconds, "Must be a non-negative number of seconds.");
            }

            if (!(thrustSeconds > 0.0f) || float.IsInfinity(thrustSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(thrustSeconds), thrustSeconds, "Must be a positive number of seconds.");
            }

            _startSeconds = startSeconds;
            DelaySeconds = delaySeconds;
            _guard = guard;
            _windUp = windUp;
            _thrust = thrust;
            ThrustSeconds = thrustSeconds;
            _recoverStartSeconds = ThrustEndSeconds + HoldSeconds;
            _recoverFrom = thrust;
        }

        public float DelaySeconds { get; }

        public float ThrustSeconds { get; }

        /// <summary>When the wind-up starts.</summary>
        public double WindUpStartSeconds => _startSeconds + DelaySeconds;

        /// <summary>When the thrust starts, at the end of the wind-up.</summary>
        public double ThrustStartSeconds => WindUpStartSeconds + WindUpSeconds;

        /// <summary>When the point reaches the aim, unless parried before.</summary>
        public double ThrustEndSeconds => ThrustStartSeconds + ThrustSeconds;

        /// <summary>When the recovery is over and the arm is en garde again.</summary>
        public double DoneSeconds => _recoverStartSeconds + RecoverSeconds;

        /// <summary>True once a Parry cut the thrust or the hold short.</summary>
        public bool IsParried { get; private set; }

        /// <summary>The point's peak speed for a speed level (PER-23: slow 0.5, medium 1.5, real 2.5 to 3 m/s; real is taken mid-range).</summary>
        /// <exception cref="ArgumentOutOfRangeException">Not a speed level.</exception>
        public static float GetPeakPointSpeed(AttackSpeed speed)
        {
            switch (speed)
            {
                case AttackSpeed.Slow:
                    return 0.5f;
                case AttackSpeed.Medium:
                    return 1.5f;
                case AttackSpeed.Real:
                    return 2.75f;
                default:
                    throw new ArgumentOutOfRangeException(nameof(speed), speed, "Not an attack speed.");
            }
        }

        /// <summary>A random delay from <see cref="MinimumDelaySeconds"/> to <see cref="MaximumDelaySeconds"/>.</summary>
        public static float DrawDelaySeconds(Random random)
        {
            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            return MinimumDelaySeconds + ((MaximumDelaySeconds - MinimumDelaySeconds) * (float)random.NextDouble());
        }

        /// <summary>
        /// Plans a thrust at <paramref name="aim"/> (right-handed Dummy frame) for a Dummy of
        /// <paramref name="heightMeters"/> holding a blade of <paramref name="bladeLengthMeters"/>: wind-up from en garde, then a thrust whose point peaks at the speed level's speed.
        /// </summary>
        public static ArmedDummyAttack Plan(
            double startSeconds, float delaySeconds, float heightMeters, Vector3 aim, AttackSpeed speed, float bladeLengthMeters)
        {
            var windUp = SwordArmReach.PoseWindUp(heightMeters, bladeLengthMeters);
            var thrust = SwordArmReach.PoseToReach(heightMeters, aim, bladeLengthMeters);
            var pathMeters = MeasurePointPathMeters(heightMeters, windUp, thrust, bladeLengthMeters);
            var thrustSeconds = Math.Max(0.05f, MinimumJerkPeakFactor * pathMeters / GetPeakPointSpeed(speed));
            var guard = SwordArmReach.PoseEnGarde(heightMeters, bladeLengthMeters);
            return new ArmedDummyAttack(startSeconds, delaySeconds, guard, windUp, thrust, thrustSeconds);
        }

        /// <summary>The length of the path the point takes from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public static float MeasurePointPathMeters(float heightMeters, SwordArmPose from, SwordArmPose to, float bladeLengthMeters)
        {
            var length = 0.0f;
            var previous = GetPoint(heightMeters, from, bladeLengthMeters);
            for (var sample = 1; sample <= PathSamples; sample++)
            {
                var point = GetPoint(heightMeters, SwordArmPose.Slerp(from, to, (float)sample / PathSamples), bladeLengthMeters);
                length += Vector3.Distance(previous, point);
                previous = point;
            }

            return length;
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

            return nowSeconds < ThrustEndSeconds ? AttackPhase.Thrust : AttackPhase.Hold;
        }

        /// <summary>The arm pose at <paramref name="nowSeconds"/>.</summary>
        public SwordArmPose GetPose(double nowSeconds)
        {
            switch (GetPhase(nowSeconds))
            {
                case AttackPhase.WindUp:
                    return SwordArmPose.Slerp(_guard, _windUp, SmoothStep(Fraction(nowSeconds, WindUpStartSeconds, WindUpSeconds)));
                case AttackPhase.Thrust:
                    return SwordArmPose.Slerp(_windUp, _thrust, MinimumJerk(Fraction(nowSeconds, ThrustStartSeconds, ThrustSeconds)));
                case AttackPhase.Hold:
                    return _thrust;
                case AttackPhase.Recover:
                    return SwordArmPose.Slerp(_recoverFrom, _guard, MinimumJerk(Fraction(nowSeconds, _recoverStartSeconds, RecoverSeconds)));
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

        private static Vector3 GetPoint(float heightMeters, SwordArmPose pose, float bladeLengthMeters)
        {
            return new SwordArm(heightMeters, Hand.Right, pose).GetBladePoint(bladeLengthMeters);
        }

        private static float Fraction(double nowSeconds, double startSeconds, float durationSeconds)
        {
            return (float)Math.Clamp((nowSeconds - startSeconds) / durationSeconds, 0.0, 1.0);
        }

        private static float SmoothStep(float fraction)
        {
            return fraction * fraction * (3.0f - (2.0f * fraction));
        }

        /// <summary>10 f^3 - 15 f^4 + 6 f^5: zero speed and acceleration at both ends, peak speed 1.875 at the middle.</summary>
        private static float MinimumJerk(float fraction)
        {
            var cube = fraction * fraction * fraction;
            return cube * (10.0f + (fraction * ((6.0f * fraction) - 15.0f)));
        }
    }
}
