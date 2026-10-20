using System;
using System.Collections.Generic;
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
    /// Fencer's live blade, behind its guard. On the headset, chest and thigh aim at the
    /// Fencer's tracked body instead when it is known (the Blade's <c>FencerTarget</c>, PER-98).
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

        /// <summary>
        /// The Fencer's body by the same stand-in as the aims, in the Strip frame: the Dummy's stance and size at the En
        /// Garde Line, without its posed sword arm, each capsule named by its <see cref="BodyPart"/>. The headset
        /// fallback of the hit judging (PER-98) moves it under the headset.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="layout"/> is null.</exception>
        public static IReadOnlyList<BodyCapsule> GetFencerStanceOnStrip(DummyLayout layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            var stance = new List<BodyCapsule>();
            foreach (var zone in layout.Zones)
            {
                if (!SwordArm.IsArmZone(zone.Zone))
                {
                    stance.Add(new BodyCapsule(GetStancePart(zone.Zone), zone.Shape));
                }
            }

            for (var index = 0; index < layout.SilhouetteParts.Count; index++)
            {
                stance.Add(new BodyCapsule(DummyLayout.SilhouetteBodyParts[index], layout.SilhouetteParts[index]));
            }

            return stance;
        }

        /// <summary>
        /// The Fencer's sword arm in the stand-in: a capsule from the stance's sword shoulder, moved by
        /// <paramref name="stanceOffset"/>, to the front of the live guard (<paramref name="fencerGuardOnStrip"/>), as
        /// thick as the upper arm.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="layout"/> is null.</exception>
        public static Capsule GetFencerSwordArm(DummyLayout layout, Vector3 stanceOffset, Vector3 fencerGuardOnStrip)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            var shoulder = layout.Arm.GetPose(ArmJoint.Shoulder).Position + stanceOffset;
            return new Capsule(fencerGuardOnStrip, shoulder, EnGardePose.UpperArmRadius * layout.HeightMeters);
        }

        /// <summary>
        /// The Fencer's body that a thrust must be turned off for a Parry (PER-97), in the Strip frame, by the stand-in
        /// alone (<see cref="GetFencerStanceOnStrip"/>), plus the sword arm (<see cref="GetFencerSwordArm"/>), left out
        /// while the blade is unknown. On the headset the Blade's <c>FencerTarget</c> judges against the tracked body
        /// instead, or this stand-in moved under the headset (PER-98).
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="layout"/> is null.</exception>
        public static IReadOnlyList<Capsule> GetFencerBodyOnStrip(DummyLayout layout, Vector3? fencerGuardOnStrip)
        {
            var body = new List<Capsule>();
            foreach (var part in GetFencerStanceOnStrip(layout))
            {
                body.Add(part.Shape);
            }

            if (fencerGuardOnStrip.HasValue)
            {
                body.Add(GetFencerSwordArm(layout, Vector3.Zero, fencerGuardOnStrip.Value));
            }

            return body;
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

        private static BodyPart GetStancePart(TargetZone zone)
        {
            switch (zone)
            {
                case TargetZone.Body:
                    return BodyPart.Torso;
                case TargetZone.Thigh:
                    return BodyPart.FrontThigh;
                case TargetZone.Foot:
                    return BodyPart.FrontLowerLeg;
                default:
                    throw new ArgumentOutOfRangeException(nameof(zone), zone, "Not a stance Target Zone.");
            }
        }

        /// <summary>The front of a Fencer zone, toward the Dummy (+z in the Strip frame).</summary>
        private static Vector3 FrontOf(Capsule zone)
        {
            return zone.Center + new Vector3(0.0f, 0.0f, zone.RadiusMeters);
        }
    }
}
