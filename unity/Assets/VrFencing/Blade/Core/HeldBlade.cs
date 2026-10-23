using System;
using System.Numerics;
using VrFencing.Settings;

namespace VrFencing.Blade
{
    /// <summary>
    /// A Fencer's blade as a sword-hand controller pose and grip Settings hold it (PER-145): the chord from the guard to
    /// the real (drooped) point, its rest shape, the grip's up and its flex. The one place the partner's blade, the bout's
    /// Touch sensor and the Host's replay judge build a blade from a controller, exactly as the Blade does for this
    /// Fencer's own. Positions in the controller pose's frame (the Strip frame in the app), meters.
    /// </summary>
    public readonly struct HeldBlade
    {
        public HeldBlade(BladeSegment chord, BladeDroop droop, Vector3 gripUp, BladeFlexSoftness flex)
        {
            Chord = chord;
            Droop = droop;
            GripUp = gripUp;
            Flex = flex;
        }

        /// <summary>The guard to the real (drooped) point.</summary>
        public BladeSegment Chord { get; }

        /// <summary>The rest shape on <see cref="Chord"/>.</summary>
        public BladeDroop Droop { get; }

        /// <summary>The grip's up (the thumb's side).</summary>
        public Vector3 GripUp { get; }

        public BladeFlexSoftness Flex { get; }

        /// <summary>
        /// The blade held by <paramref name="controller"/> with <paramref name="blade"/>'s grip, set and flex in
        /// <paramref name="hand"/>, hanging under gravity (-y).
        /// </summary>
        public static HeldBlade FromController(TrackedPose controller, BladeSettings blade, Hand hand)
        {
            if (blade == null)
            {
                throw new ArgumentNullException(nameof(blade));
            }

            var mountInController = BladePose.GetMountInController(blade, hand);
            var forte = BladePose.GetBlade(controller, mountInController);
            var gripUp = Vector3.Transform(Vector3.UnitY, controller.Rotation * mountInController.Rotation);
            var droop = BladeDroop.Create(forte.Direction, gripUp, -Vector3.UnitY, blade);
            return new HeldBlade(droop.GetChord(forte), droop, gripUp, blade.Flex);
        }

        /// <summary>How this blade gives in the contact solver: a Fencer's grip and its own flex.</summary>
        public BladeHold ToHold()
        {
            return new BladeHold(Chord, Droop, BladeContactSolver.FencerGripStiffness, BladeContactSolver.GetBendStiffness(Flex));
        }
    }
}
