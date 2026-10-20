using System;
using VrFencing.Session;

namespace VrFencing.Partner
{
    /// <summary>
    /// A part of the opponent's rigged body that fades on its own. The rig's mesh comes in three parts (Movement SDK's
    /// <c>Armature.fbx</c>: body, arms, legs), so both arms fade together.
    /// </summary>
    public enum OpponentBodyPart
    {
        /// <summary>Torso and head.</summary>
        Torso,

        /// <summary>Both arms and hands.</summary>
        Arms,

        Legs,
    }

    /// <summary>
    /// How opaque each part of the opponent is drawn, as a fraction of the Dummy look's body opacity (PER-81, decided in
    /// PER-59): parts the partner's tracker is unsure about fade more transparent; the torso and the sword arm always
    /// show. The legs are always generated (Generative Legs, not seen by any camera), so they always fade, and more at
    /// low fidelity (the tracker's quiet fallback from head and controllers only). The arms share one mesh with the sword
    /// arm, which must always show, so they stay. A body that stopped coming (asleep, unfocused, link lost) holds its
    /// last pose as a ghost.
    /// </summary>
    public static class OpponentFade
    {
        /// <summary>The legs at high fidelity: generated, never tracked.</summary>
        public const float GeneratedLegsFactor = 0.5f;

        /// <summary>The legs at low fidelity: generated from a body that is itself a guess.</summary>
        public const float LowFidelityLegsFactor = 0.3f;

        /// <summary>Every part while the partner's body is not coming (no fidelity, or stale).</summary>
        public const float LostBodyFactor = 0.25f;

        /// <summary>
        /// Every part, times this, while the two bodies are within corps-a-corps distance in Paired Practice (PER-72): the
        /// opponent fades so this Fencer sees through a body they are standing in, instead of a halt.
        /// </summary>
        public const float CorpsACorpsFactor = 0.3f;

        /// <summary>How long a fade takes from fully shown to gone, so a fidelity flicker does not blink the legs.</summary>
        public const float FadeSeconds = 0.3f;

        /// <summary>The part's opacity factor for the partner's current fidelity (null: no body) and whether their body went stale.</summary>
        public static float GetTargetFactor(OpponentBodyPart part, BodyTrackingFidelity? fidelity, bool isBodyStale)
        {
            if (isBodyStale || !fidelity.HasValue)
            {
                return LostBodyFactor;
            }

            if (part != OpponentBodyPart.Legs)
            {
                return 1.0f;
            }

            return fidelity.Value == BodyTrackingFidelity.High ? GeneratedLegsFactor : LowFidelityLegsFactor;
        }

        /// <summary>One frame of the fade from <paramref name="current"/> toward <paramref name="target"/>, at most a full fade per <see cref="FadeSeconds"/>.</summary>
        public static float StepToward(float current, float target, float deltaSeconds)
        {
            var step = Math.Max(0.0f, deltaSeconds) / FadeSeconds;
            return current < target ? Math.Min(target, current + step) : Math.Max(target, current - step);
        }
    }
}
