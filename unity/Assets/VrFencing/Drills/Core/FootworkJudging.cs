using System;
using System.Collections.Generic;
using System.Linq;
using VrFencing.Dummy;

namespace VrFencing.Drills
{
    /// <summary>
    /// Which footwork answers what, for the Drills that judge footwork (PER-39), and how footwork calls are picked so
    /// the Fencer does not drift off the Strip. The footwork itself is read by the Tracking module's classifier.
    /// </summary>
    public static class FootworkJudging
    {
        /// <summary>
        /// Footwork calls stop calling advances once the Fencer's judged advances outnumber the retreats by this many
        /// (about 0.66 m ahead of the En Garde Line for a 1.65 m Fencer), so a lunge from there still lands on a 3 m Strip.
        /// </summary>
        public const int MaxNetAdvances = 2;

        /// <summary>
        /// Footwork calls stop calling retreats once the retreats outnumber the advances by this many: the Strip has only
        /// about the en garde stance plus the rear Warning Zone behind the En Garde Line.
        /// </summary>
        public const int MaxNetRetreats = 1;

        /// <summary>
        /// Distance choice: does the footwork read at the Touch suit the Dummy Distance? Extension distance needs no
        /// step (<see cref="FootworkAction.None"/>) and lunge distance a lunge. At step-lunge distance a step-lunge or a
        /// lunge suits it: the Dummy is too far to reach with less, and a long lunge that still lands is not a wrong choice.
        /// </summary>
        public static bool SuitsDistance(DummyDistance distance, FootworkAction footwork)
        {
            switch (distance)
            {
                case DummyDistance.Extension:
                    return footwork == FootworkAction.None;
                case DummyDistance.Lunge:
                    return footwork == FootworkAction.Lunge;
                case DummyDistance.StepLunge:
                    return footwork == FootworkAction.StepLunge || footwork == FootworkAction.Lunge;
                default:
                    throw new ArgumentOutOfRangeException(nameof(distance), distance, "Not a distance preset.");
            }
        }

        /// <summary>
        /// Footwork calls: does the footwork read in the window answer the call? A lunge call is also answered by what
        /// reads as a step-lunge, since a long lunge can travel as far; every other call needs its own action.
        /// </summary>
        public static bool AnswersCall(FootworkAction call, FootworkAction footwork)
        {
            if (call == FootworkAction.Lunge)
            {
                return footwork == FootworkAction.Lunge || footwork == FootworkAction.StepLunge;
            }

            return footwork == call;
        }

        /// <summary>
        /// The calls the next Rep may make, given the footwork judged so far in the run: all of <paramref name="calls"/>
        /// except an advance once the Fencer is <see cref="MaxNetAdvances"/> steps ahead, and a retreat once
        /// <see cref="MaxNetRetreats"/> behind (judged advances minus retreats; lunges are recovered, so they do not count).
        /// Never empty when <paramref name="calls"/> is not.
        /// </summary>
        public static IReadOnlyList<FootworkAction> FilterCallsByDrift(IReadOnlyList<FootworkAction> calls, IEnumerable<FootworkAction?> judgedFootwork)
        {
            var netAdvances = judgedFootwork.Sum(footwork => footwork == FootworkAction.Advance ? 1 : footwork == FootworkAction.Retreat ? -1 : 0);
            var allowed = calls
                .Where(call => !(call == FootworkAction.Advance && netAdvances >= MaxNetAdvances))
                .Where(call => !(call == FootworkAction.Retreat && netAdvances <= -MaxNetRetreats))
                .ToArray();
            return allowed.Length > 0 ? allowed : calls;
        }
    }
}
