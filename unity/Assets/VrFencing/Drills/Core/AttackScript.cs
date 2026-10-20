using System;
using System.Collections.Generic;
using System.Linq;
using VrFencing.Dummy;

namespace VrFencing.Drills
{
    /// <summary>
    /// What the Armed Dummy attacks with in a Parry Drill (PER-100): the targets it thrusts at, one at random per Rep,
    /// and the speed levels it uses, in turn by Rep. The runner picks both into each Rep's cue
    /// (<see cref="RepCue.AttackTarget"/>, <see cref="RepCue.AttackSpeed"/>); the Blade module's Armed Dummy plans the
    /// thrust from them.
    /// </summary>
    public sealed class AttackScript
    {
        /// <exception cref="ArgumentException">No targets or no speeds.</exception>
        public AttackScript(IReadOnlyList<AttackTarget> targets, IReadOnlyList<AttackSpeed> speeds)
        {
            if (targets == null || targets.Count == 0)
            {
                throw new ArgumentException("An attack script needs at least one target.", nameof(targets));
            }

            if (speeds == null || speeds.Count == 0)
            {
                throw new ArgumentException("An attack script needs at least one speed.", nameof(speeds));
            }

            Targets = targets.ToArray();
            Speeds = speeds.ToArray();
        }

        /// <summary>
        /// Every target, at slow, medium and real speed in turn (the PER-95 feel test's script): what a Parry Drill gets
        /// when it names none.
        /// </summary>
        public static AttackScript EveryTargetAndSpeed { get; } = new AttackScript(
            new[] { AttackTarget.Hand, AttackTarget.Forearm, AttackTarget.Chest, AttackTarget.Thigh },
            new[] { AttackSpeed.Slow, AttackSpeed.Medium, AttackSpeed.Real });

        /// <summary>The targets a thrust may aim at; each Rep picks one at random.</summary>
        public IReadOnlyList<AttackTarget> Targets { get; }

        /// <summary>The speed levels, used in this order Rep by Rep and then again from the first.</summary>
        public IReadOnlyList<AttackSpeed> Speeds { get; }

        /// <summary>The speed of the Rep at <paramref name="repIndex"/> (zero-based).</summary>
        public AttackSpeed GetSpeed(int repIndex)
        {
            if (repIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(repIndex), repIndex, "Must not be negative.");
            }

            return Speeds[repIndex % Speeds.Count];
        }

        /// <summary>A target drawn at random, each of <see cref="Targets"/> equally likely.</summary>
        public AttackTarget DrawTarget(Random random)
        {
            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            return Targets[random.Next(Targets.Count)];
        }
    }
}
