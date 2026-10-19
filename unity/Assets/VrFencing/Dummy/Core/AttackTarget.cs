using System;
using System.Numerics;

namespace VrFencing.Dummy
{
    /// <summary>Where on the Fencer an Armed Dummy thrust is aimed (PER-23).</summary>
    public enum AttackTarget
    {
        Hand,
        Forearm,
        Chest,
        Thigh,
    }

    /// <summary>
    /// The aim point of a thrust at each <see cref="AttackTarget"/>. The Fencer is taken to stand en garde at the En
    /// Garde Line in the Dummy's own stance and size (the Dummy is the Fencer's height and hand), so chest and thigh
    /// are the fronts of the Dummy's body and thigh zones as seen from the Strip frame; hand and forearm follow the
    /// Fencer's live blade, behind its guard. IOBT body tracking replaces the stance model later (PER-98).
    /// </summary>
    public static class AttackAim
    {
        /// <summary>
        /// The aim for <paramref name="target"/> in the Dummy frame (the Dummy's own, not mirrored to right-handed),
        /// with the Dummy <paramref name="distanceMeters"/> along the Strip. Hand and forearm need the Fencer's blade
        /// (<paramref name="fencerGuardOnStrip"/>, the front of its guard, and <paramref name="fencerBladeDirectionOnStrip"/>,
        /// guard to point, both in the Strip frame); without it they fall back to the chest.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="layout"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="target"/> is not a target.</exception>
        public static Vector3 GetAimInDummyFrame(
            AttackTarget target, DummyLayout layout, float distanceMeters, Vector3? fencerGuardOnStrip, Vector3 fencerBladeDirectionOnStrip)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            Vector3 aimOnStrip;
            switch (target)
            {
                case AttackTarget.Hand when fencerGuardOnStrip.HasValue:
                    aimOnStrip = fencerGuardOnStrip.Value - (fencerBladeDirectionOnStrip * ArmedDummyTuning.HandBehindGuardMeters);
                    break;
                case AttackTarget.Forearm when fencerGuardOnStrip.HasValue:
                    aimOnStrip = fencerGuardOnStrip.Value - (fencerBladeDirectionOnStrip * ArmedDummyTuning.ForearmBehindGuardMeters);
                    break;
                case AttackTarget.Hand:
                case AttackTarget.Forearm:
                case AttackTarget.Chest:
                    aimOnStrip = FrontOf(layout.GetZone(TargetZone.Body));
                    break;
                case AttackTarget.Thigh:
                    aimOnStrip = FrontOf(layout.GetZone(TargetZone.Thigh));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(target), target, "Not an attack target.");
            }

            // The Dummy frame is the Strip frame turned half a turn about the vertical and moved along the Strip; the
            // same move takes it back.
            return DummyLayout.DummyToStrip(aimOnStrip, distanceMeters);
        }

        /// <summary>A random target, each of the four equally likely.</summary>
        public static AttackTarget DrawTarget(Random random)
        {
            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            return (AttackTarget)random.Next(4);
        }

        /// <summary>The front of a Fencer zone, toward the Dummy (+z in the Strip frame).</summary>
        private static Vector3 FrontOf(Capsule zone)
        {
            return zone.Center + new Vector3(0.0f, 0.0f, zone.RadiusMeters);
        }
    }
}
