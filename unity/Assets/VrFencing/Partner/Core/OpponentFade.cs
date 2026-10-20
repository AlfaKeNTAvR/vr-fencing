using System;
using VrFencing.Session;

namespace VrFencing.Partner
{
    /// <summary>
    /// How opaque the opponent is drawn, as a fraction of the Dummy look's body opacity (PER-81, decided in PER-59). The
    /// whole body shows while it comes; its legs are built on the partner's footwork (PER-112), so they no longer fade
    /// as Meta's generated legs did. A body that stopped coming (asleep, unfocused, link lost) holds its last pose as a
    /// ghost.
    /// </summary>
    public static class OpponentFade
    {
        /// <summary>The whole body while the partner's body is not coming (no fidelity, or stale).</summary>
        public const float LostBodyFactor = 0.25f;

        /// <summary>
        /// The whole body, times this, while the two bodies are within corps-a-corps distance in Paired Practice (PER-72): the
        /// opponent fades so this Fencer sees through a body they are standing in, instead of a halt.
        /// </summary>
        public const float CorpsACorpsFactor = 0.3f;

        /// <summary>How long a fade takes from fully shown to gone, so a lost body that comes back does not blink.</summary>
        public const float FadeSeconds = 0.3f;

        /// <summary>The opacity factor for the partner's current fidelity (null: no body) and whether their body went stale.</summary>
        public static float GetTargetFactor(BodyTrackingFidelity? fidelity, bool isBodyStale)
        {
            return isBodyStale || !fidelity.HasValue ? LostBodyFactor : 1.0f;
        }

        /// <summary>One frame of the fade from <paramref name="current"/> toward <paramref name="target"/>, at most a full fade per <see cref="FadeSeconds"/>.</summary>
        public static float StepToward(float current, float target, float deltaSeconds)
        {
            var step = Math.Max(0.0f, deltaSeconds) / FadeSeconds;
            return current < target ? Math.Min(target, current + step) : Math.Max(target, current - step);
        }
    }
}
