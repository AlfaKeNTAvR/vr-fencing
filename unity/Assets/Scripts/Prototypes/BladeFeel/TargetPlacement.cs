// Places the static Targets in world space, in front of the Fencer's start pose.
// Pure function of the head pose so it can be tested without a headset.
using System.Collections.Generic;
using UnityEngine;
using VrFencing.Blade;

namespace VrFencing.Prototypes.BladeFeel
{
    public static class TargetPlacement
    {
        // Below this horizontal length the head looks almost straight up or down and its
        // forward vector says little about which way the Fencer faces.
        private const float MinHorizontalForward = 0.3f;

        /// <summary>
        /// Horizontal unit vector the Fencer faces, in world space. Uses the head's forward
        /// projected onto the floor plane; when the head is pitched steeply (e.g. looking
        /// down at the left controller), the head's up vector carries the facing instead:
        /// looking down, up tilts forward; looking up, up tilts backward.
        /// </summary>
        public static Vector3 FindFacing(Quaternion headRotationWorld)
        {
            var forward = headRotationWorld * Vector3.forward;
            var horizontal = new Vector3(forward.x, 0f, forward.z);
            if (horizontal.magnitude >= MinHorizontalForward)
                return horizontal.normalized;

            var up = headRotationWorld * Vector3.up;
            var fromUp = forward.y < 0f ? new Vector3(up.x, 0f, up.z) : new Vector3(-up.x, 0f, -up.z);
            return fromUp.sqrMagnitude > 1e-8f ? fromUp.normalized : Vector3.forward;
        }

        /// <summary>
        /// World-space Targets for the given head pose. Heights are above <paramref name="floorHeightWorld"/>
        /// (the world y of the floor, i.e. the floor-level tracking origin).
        /// </summary>
        public static SphereTarget[] PlaceTargets(Vector3 headPositionWorld, Quaternion headRotationWorld,
            float floorHeightWorld, IReadOnlyList<BladeFeelPresets.TargetLayout> layouts)
        {
            var forward = FindFacing(headRotationWorld);
            var right = Vector3.Cross(Vector3.up, forward);
            var feet = new Vector3(headPositionWorld.x, floorHeightWorld, headPositionWorld.z);

            var targets = new SphereTarget[layouts.Count];
            for (var index = 0; index < layouts.Count; index++)
            {
                var layout = layouts[index];
                var center = feet + forward * layout.ForwardMeters + right * layout.RightMeters
                             + Vector3.up * layout.HeightMeters;
                targets[index] = new SphereTarget(layout.Id, center, layout.RadiusMeters);
            }

            return targets;
        }
    }
}
