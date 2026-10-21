using System;
using VrFencing.Dummy;

namespace VrFencing.Drills
{
    /// <summary>One thrust the Armed Dummy makes in Free Practice: where it aims and how fast.</summary>
    public readonly struct FreeAttack
    {
        public FreeAttack(AttackTarget target, AttackSpeed speed)
        {
            Target = target;
            Speed = speed;
        }

        public AttackTarget Target { get; }

        public AttackSpeed Speed { get; }
    }

    /// <summary>
    /// When the Armed Dummy attacks in Free Practice (PER-123, <see cref="VrFencing.Settings.FreePracticeBehavior.Attacks"/>): one
    /// thrust at a time, the next <see cref="RestSeconds"/> after the last one is over, from the Armed Dummy test Drill's
    /// script (<see cref="AttackScript.EveryTargetAndSpeed"/>: any of the four targets at random, slow, medium and real
    /// speed in turn). The thrust's own random delay (<see cref="ArmedDummyAttack.DrawDelaySeconds"/>) comes on top, so
    /// the Fencer cannot time it. No Reps: it goes on until Free Practice stops.
    /// </summary>
    public sealed class FreeAttackSchedule
    {
        /// <summary>From one thrust's end (recovered, parried or landed) to the next one's plan, as a Drill's default rest.</summary>
        public const float RestSeconds = 2.0f;

        private readonly AttackScript _script = AttackScript.EveryTargetAndSpeed;
        private readonly Random _random;
        private double? _nextAttackAtSeconds;
        private int _attackCount;

        /// <exception cref="ArgumentNullException"><paramref name="random"/> is null.</exception>
        public FreeAttackSchedule(Random random)
        {
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        /// <summary>
        /// One frame: null while a thrust is under way or the rest after it runs; the next thrust once the rest is over.
        /// The first thrust after a <see cref="Reset"/> also waits a rest.
        /// </summary>
        /// <param name="isAttackUnderWay">A thrust is planned and not yet over (its recovery done).</param>
        public FreeAttack? Step(double nowSeconds, bool isAttackUnderWay)
        {
            if (isAttackUnderWay)
            {
                _nextAttackAtSeconds = null;
                return null;
            }

            _nextAttackAtSeconds ??= nowSeconds + RestSeconds;
            if (nowSeconds < _nextAttackAtSeconds.Value)
            {
                return null;
            }

            _nextAttackAtSeconds = null;
            var attack = new FreeAttack(_script.DrawTarget(_random), _script.GetSpeed(_attackCount));
            _attackCount++;
            return attack;
        }

        /// <summary>Starts over: the next thrust comes a rest from the next <see cref="Step"/>, at the first speed.</summary>
        public void Reset()
        {
            _nextAttackAtSeconds = null;
            _attackCount = 0;
        }
    }
}
