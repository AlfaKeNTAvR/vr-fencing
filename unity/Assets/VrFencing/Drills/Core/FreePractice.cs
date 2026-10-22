using System;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Drills
{
    /// <summary>
    /// Free Practice (PER-123): outside any Drill the Dummy holds its blade, whatever the "Dummy blade in every Drill"
    /// Setting says, and does what the Fencer picked on the home screen (<see cref="FreePracticeBehavior"/>),
    /// continuously, with no Reps or scoring. Every behavior reuses a Drill's motion: the still en garde, the distance
    /// keeper (PER-105), the footwork test's loop (PER-111) and the Armed Dummy test Drill's thrusts (PER-95). Starting a
    /// Drill takes the Dummy over; when the run ends, Free Practice comes back with the same behavior.
    /// </summary>
    public static class FreePractice
    {
        /// <summary>How quickly the Dummy reacts to the Fencer's footwork while keeping its distance.</summary>
        public const DummyDifficulty KeepDistanceDifficulty = DummyDifficulty.Medium;

        /// <summary>Where the attacking Dummy stands, as in the Armed Dummy test Drill (further back for a thrust at the arm).</summary>
        public const DummyDistance AttackDistance = DummyDistance.Extension;

        /// <summary>
        /// Where the Dummy stands when a behavior starts, as in the Drill whose motion it reuses: lunge distance in guard
        /// and while keeping distance (the distance keeper's test Drill), step-lunge distance for the footwork loop (its
        /// fleche needs the room), extension distance to attack (<see cref="AttackDistance"/>).
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="behavior"/> is not a behavior.</exception>
        public static DummyDistance GetDummyDistance(FreePracticeBehavior behavior)
        {
            switch (behavior)
            {
                case FreePracticeBehavior.Guard:
                case FreePracticeBehavior.HandFixed:
                case FreePracticeBehavior.KeepDistance:
                    return DummyDistance.Lunge;
                case FreePracticeBehavior.Footwork:
                    return DummyDistance.StepLunge;
                case FreePracticeBehavior.Attacks:
                    return AttackDistance;
                default:
                    throw new ArgumentOutOfRangeException(nameof(behavior), behavior, "Not a Free Practice behavior.");
            }
        }

        private static readonly FreePracticeBehavior[] Order =
        {
            FreePracticeBehavior.Guard,
            FreePracticeBehavior.HandFixed,
            FreePracticeBehavior.KeepDistance,
            FreePracticeBehavior.Footwork,
            FreePracticeBehavior.Attacks,
        };

        /// <summary>
        /// True for <see cref="FreePracticeBehavior.HandFixed"/>: the Dummy stands en garde and its hand never moves or
        /// gives (no give to a press, no knock from a beat, no footwork), so only the blades themselves react.
        /// </summary>
        public static bool IsHandFixed(FreePracticeBehavior? behavior)
        {
            return behavior == FreePracticeBehavior.HandFixed;
        }

        /// <summary>
        /// Whether Free Practice drives the Dummy now: no Drill is on (neither its task line nor its run, paused or not),
        /// the Dummy is shown (not hidden for a Partner session) and a Strip is placed and not being placed.
        /// </summary>
        public static bool IsOn(bool isDrillOn, bool isDummyShown, bool isStripReady)
        {
            return !isDrillOn && isDummyShown && isStripReady;
        }

        /// <summary>The behavior after <paramref name="behavior"/> on the home screen's row, back to the first after the last.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="behavior"/> is not a behavior.</exception>
        public static FreePracticeBehavior GetNext(FreePracticeBehavior behavior)
        {
            var index = Array.IndexOf(Order, behavior);
            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(behavior), behavior, "Not a Free Practice behavior.");
            }

            return Order[(index + 1) % Order.Length];
        }
    }
}
