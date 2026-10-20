using UnityEngine;

namespace VrFencing.Dummy
{
    /// <summary>A part of the rigged body's mesh, as the model's own materials name it.</summary>
    public enum RigMeshPart
    {
        /// <summary>Torso and head (and anything the model does not name).</summary>
        Body,

        Arms,

        Legs,

        /// <summary>The fencing mask's wire front and cloth back (PER-41).</summary>
        Mask,

        /// <summary>The mask's bib over the throat.</summary>
        Bib,
    }

    /// <summary>
    /// One material slot of one of the rig's skinned meshes and the body part it draws (PER-81), so a part can be colored
    /// on its own with a property block for that slot: the opponent's generated legs fade, the rest shows.
    /// </summary>
    public readonly struct RigMeshSlot
    {
        public RigMeshSlot(Renderer renderer, int materialIndex, RigMeshPart part)
        {
            Renderer = renderer;
            MaterialIndex = materialIndex;
            Part = part;
        }

        public Renderer Renderer { get; }

        public int MaterialIndex { get; }

        public RigMeshPart Part { get; }

        /// <summary>
        /// The part a model material (or mesh) name stands for: "Arm" or "Leg" in it, the mask model's "MaskBib" and
        /// "Mask", else the body.
        /// </summary>
        public static RigMeshPart GetPart(string name)
        {
            var lower = (name ?? string.Empty).ToLowerInvariant();
            if (lower.StartsWith("maskbib"))
            {
                return RigMeshPart.Bib;
            }

            if (lower.StartsWith("mask"))
            {
                return RigMeshPart.Mask;
            }

            if (lower.Contains("arm") && !lower.Contains("armature"))
            {
                return RigMeshPart.Arms;
            }

            return lower.Contains("leg") ? RigMeshPart.Legs : RigMeshPart.Body;
        }
    }
}
