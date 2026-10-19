using System;
using VrFencing.Dummy;

namespace VrFencing.Drills
{
    /// <summary>
    /// Which footwork answers what, for the Drills that judge footwork (PER-39). The footwork itself is read by the
    /// Tracking module's classifier. The Fencer does not drift along the Strip: the runner waits for the Start Mark
    /// between Reps (PER-89), so footwork calls no longer limit advances and retreats by count.
    /// </summary>
    public static class FootworkJudging
    {
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
    }
}
