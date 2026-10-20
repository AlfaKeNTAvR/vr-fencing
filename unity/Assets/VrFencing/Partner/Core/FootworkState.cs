using System;
using System.Numerics;

namespace VrFencing.Partner
{
    /// <summary>What a Fencer's feet are doing, as the sender reads it from its own full-rate tracking (PER-112).</summary>
    public enum FootworkPhase
    {
        /// <summary>Both feet planted in en garde.</summary>
        Standing,

        /// <summary>
        /// Stepping toward the opponent: the front foot first, then the back foot. Also a step mostly across the Strip
        /// (PER-118), which the front foot leads too; the feet's spots say which way it goes.
        /// </summary>
        Advancing,

        /// <summary>Stepping away from the opponent: the back foot first, then the front foot.</summary>
        Retreating,

        /// <summary>Lunging: the front foot kicks out and lands far ahead, the back foot stays.</summary>
        Lunging,

        /// <summary>Coming back from a lunge into en garde (backward, or forward by bringing the back foot up).</summary>
        Recovering,
    }

    /// <summary>
    /// One foot as the sender places it: its spot on the floor (the ankle's x and z in the sender's Strip frame), how
    /// high it is lifted and how far it is pitched. The receiver poses the leg on it (<see cref="RemoteFencerLegs"/>).
    /// </summary>
    public readonly struct FootPlacement
    {
        public FootPlacement(float x, float z, float liftMeters, float pitchRadians)
        {
            X = x;
            Z = z;
            LiftMeters = liftMeters;
            PitchRadians = pitchRadians;
        }

        /// <summary>The ankle's place across the Strip (+x the Fencer's right).</summary>
        public float X { get; }

        /// <summary>The ankle's place along the Strip (+z toward the opponent).</summary>
        public float Z { get; }

        /// <summary>How far the ankle is above its en garde height; 0 is on the floor.</summary>
        public float LiftMeters { get; }

        /// <summary>The foot's turn about its sideways axis: positive lifts the toe, negative the heel.</summary>
        public float PitchRadians { get; }

        /// <summary>True when the foot is on the floor.</summary>
        public bool IsPlanted => LiftMeters <= 0.0f && PitchRadians == 0.0f;

        public static FootPlacement Lerp(FootPlacement from, FootPlacement to, float fraction)
        {
            return new FootPlacement(
                from.X + ((to.X - from.X) * fraction),
                from.Z + ((to.Z - from.Z) * fraction),
                from.LiftMeters + ((to.LiftMeters - from.LiftMeters) * fraction),
                from.PitchRadians + ((to.PitchRadians - from.PitchRadians) * fraction));
        }

        /// <summary>The same foot with its floor spot moved by <paramref name="movePoint"/>; lift and pitch are the foot's own.</summary>
        public FootPlacement Transform(Func<Vector3, Vector3> movePoint)
        {
            var moved = movePoint(new Vector3(X, 0.0f, Z));
            return new FootPlacement(moved.X, moved.Z, LiftMeters, PitchRadians);
        }
    }

    /// <summary>
    /// The footwork state that goes with each body sample (PER-112, stage 3 of PER-108): the sender reads its own feet
    /// from its full-rate hips and head (<see cref="FootworkStateTracker"/>), since the receiver sees the body only 30
    /// times a second and 100 ms late, and would read a lunge late. The receiver keeps the tracked upper body and hips
    /// and builds the legs below them on these feet (<see cref="RemoteFencerLegs"/>), replacing Meta's Generative Legs.
    /// </summary>
    public readonly struct FootworkState
    {
        public FootworkState(FootworkPhase phase, FootPlacement front, FootPlacement back, float confidence)
        {
            Phase = phase;
            Front = front;
            Back = back;
            Confidence = Math.Min(Math.Max(confidence, 0.0f), 1.0f);
        }

        public FootworkPhase Phase { get; }

        /// <summary>The foot on the sword hand's side.</summary>
        public FootPlacement Front { get; }

        public FootPlacement Back { get; }

        /// <summary>
        /// How sure the sender is of these feet, from 0 to 1 (see <see cref="RemoteFootworkTuning"/>'s confidence
        /// levels). Below <see cref="RemoteFootworkTuning.MinConfidence"/> the receiver keeps the feet planted where they
        /// last were sure.
        /// </summary>
        public float Confidence { get; }

        /// <summary>Between two states: the feet and the confidence in a straight line, the phase from the nearer one.</summary>
        public static FootworkState Lerp(FootworkState from, FootworkState to, float fraction)
        {
            return new FootworkState(
                fraction < 0.5f ? from.Phase : to.Phase,
                FootPlacement.Lerp(from.Front, to.Front, fraction),
                FootPlacement.Lerp(from.Back, to.Back, fraction),
                from.Confidence + ((to.Confidence - from.Confidence) * fraction));
        }

        /// <summary>The same state with both feet's floor spots moved by <paramref name="movePoint"/>.</summary>
        public FootworkState Transform(Func<Vector3, Vector3> movePoint)
        {
            return new FootworkState(Phase, Front.Transform(movePoint), Back.Transform(movePoint), Confidence);
        }
    }
}
