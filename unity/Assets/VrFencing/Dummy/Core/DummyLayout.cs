using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using VrFencing.Settings;

namespace VrFencing.Dummy
{
    /// <summary>
    /// The Dummy as data: its Target Zones and the rest of its silhouette, as capsules in the Dummy frame
    /// (origin on the floor under the front foot, +x the Dummy's right, +y up, +z toward the Fencer),
    /// scaled to <see cref="HeightMeters"/>. Immutable; <see cref="WithArmPose"/> gives the layout for another arm pose.
    ///
    /// The sword arm is a joint chain (<see cref="Arm"/>): the hand, wrist, forearm and upper arm zones are derived
    /// from its joint poses. The body, thigh and foot zones and the silhouette are fixed to the stance.
    ///
    /// Hand: the Dummy holds its blade in the same hand as the Fencer, so a right-handed Fencer faces a
    /// right-handed Dummy and a left-handed Fencer a left-handed one ("left mirrors everything", Settings).
    /// The left-handed layout is the right-handed one reflected across x = 0.
    /// </summary>
    public sealed class DummyLayout
    {
        private readonly ZoneCapsule[] _zones;
        private readonly Capsule[] _silhouetteParts;

        private DummyLayout(float heightMeters, SwordArm arm, Capsule[] silhouetteParts)
        {
            HeightMeters = heightMeters;
            Arm = arm;
            _silhouetteParts = silhouetteParts;
            _zones = Enum.GetValues(typeof(TargetZone))
                .Cast<TargetZone>()
                .Select(zone => new ZoneCapsule(zone, SwordArm.IsArmZone(zone) ? arm.GetZone(zone) : Scale(StanceZoneAtUnitHeight(zone), heightMeters, arm.Hand)))
                .ToArray();
        }

        public float HeightMeters { get; }

        /// <summary>The Dummy's sword hand.</summary>
        public Hand Hand => Arm.Hand;

        /// <summary>The sword arm's joints and its pose.</summary>
        public SwordArm Arm { get; }

        /// <summary>One capsule per <see cref="TargetZone"/>, in enum order.</summary>
        public IReadOnlyList<ZoneCapsule> Zones => _zones;

        /// <summary>Head, back arm, front shin and back leg: drawn, but not Target Zones.</summary>
        public IReadOnlyList<Capsule> SilhouetteParts => _silhouetteParts;

        /// <summary>Lays out the Dummy in en garde with the arm in line, for a standing height and sword hand.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The height is not a positive number.</exception>
        public static DummyLayout Create(float heightMeters, Hand hand)
        {
            return Create(heightMeters, hand, SwordArmPose.ArmInLine);
        }

        /// <summary>Lays out the Dummy in en garde with the sword arm in <paramref name="armPose"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The height is not a positive number.</exception>
        public static DummyLayout Create(float heightMeters, Hand hand, SwordArmPose armPose)
        {
            RequirePositiveHeight(heightMeters);

            var silhouetteParts = SilhouetteAtUnitHeight()
                .Select(part => Scale(part, heightMeters, hand))
                .ToArray();
            return new DummyLayout(heightMeters, new SwordArm(heightMeters, hand, armPose), silhouetteParts);
        }

        /// <summary>The same Dummy with the sword arm moved to <paramref name="armPose"/>.</summary>
        public DummyLayout WithArmPose(SwordArmPose armPose)
        {
            return new DummyLayout(HeightMeters, new SwordArm(HeightMeters, Hand, armPose), _silhouetteParts);
        }

        public Capsule GetZone(TargetZone zone)
        {
            foreach (var zoneCapsule in _zones)
            {
                if (zoneCapsule.Zone == zone)
                {
                    return zoneCapsule.Shape;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(zone), zone, "Not a Target Zone.");
        }

        /// <summary>
        /// The Target Zones in the Strip frame (origin on the floor at the Fencer's En Garde Line on the Strip's
        /// centerline, +x the Fencer's right, +y up, +z along the Strip toward the Dummy), with the Dummy's front
        /// foot <paramref name="distanceMeters"/> ahead of the En Garde Line and the Dummy facing the Fencer.
        /// This is what the Blade's Touch detector tests the blade point against.
        /// </summary>
        public IReadOnlyList<ZoneCapsule> GetZonesOnStrip(float distanceMeters)
        {
            return _zones
                .Select(zone => new ZoneCapsule(zone.Zone, DummyToStrip(zone.Shape, distanceMeters)))
                .ToArray();
        }

        /// <summary>
        /// Moves a point from the Dummy frame to the Strip frame: the Dummy stands <paramref name="distanceMeters"/>
        /// along the Strip, turned half a turn about the vertical to face the Fencer.
        /// </summary>
        public static Vector3 DummyToStrip(Vector3 pointInDummyFrame, float distanceMeters)
        {
            return new Vector3(-pointInDummyFrame.X, pointInDummyFrame.Y, distanceMeters - pointInDummyFrame.Z);
        }

        internal static void RequirePositiveHeight(float heightMeters)
        {
            if (!(heightMeters > 0.0f) || float.IsInfinity(heightMeters))
            {
                throw new ArgumentOutOfRangeException(nameof(heightMeters), heightMeters, "Height must be a positive number of meters.");
            }
        }

        private static Capsule DummyToStrip(Capsule capsule, float distanceMeters)
        {
            return new Capsule(DummyToStrip(capsule.Start, distanceMeters), DummyToStrip(capsule.End, distanceMeters), capsule.RadiusMeters);
        }

        private static Capsule Scale(Capsule unitCapsule, float heightMeters, Hand hand)
        {
            var start = unitCapsule.Start * heightMeters;
            var end = unitCapsule.End * heightMeters;
            if (hand == Hand.Left)
            {
                start = HandMirroring.MirrorPosition(start);
                end = HandMirroring.MirrorPosition(end);
            }

            return new Capsule(start, end, unitCapsule.RadiusMeters * heightMeters);
        }

        /// <summary>The Target Zones that stay with the stance (not on the sword arm), at a height of 1.</summary>
        private static Capsule StanceZoneAtUnitHeight(TargetZone zone)
        {
            switch (zone)
            {
                case TargetZone.Thigh:
                    return new Capsule(EnGardePose.FrontKnee, EnGardePose.FrontHip, EnGardePose.ThighRadius);
                case TargetZone.Foot:
                    return new Capsule(EnGardePose.FrontHeel, EnGardePose.FrontToe, EnGardePose.FootRadius);
                case TargetZone.Body:
                    return new Capsule(EnGardePose.TorsoBottom, EnGardePose.TorsoTop, EnGardePose.TorsoRadius);
                default:
                    throw new ArgumentOutOfRangeException(nameof(zone), zone, "Not a stance Target Zone.");
            }
        }

        private static IEnumerable<Capsule> SilhouetteAtUnitHeight()
        {
            yield return new Capsule(EnGardePose.HeadCenter, EnGardePose.HeadCenter, EnGardePose.HeadRadius);
            yield return new Capsule(EnGardePose.BackShoulder, EnGardePose.BackElbow, EnGardePose.BackArmRadius);
            yield return new Capsule(EnGardePose.BackElbow, EnGardePose.BackHand, EnGardePose.BackArmRadius);
            yield return new Capsule(EnGardePose.FrontAnkle, EnGardePose.FrontKnee, EnGardePose.ShinRadius);
            yield return new Capsule(EnGardePose.BackKnee, EnGardePose.BackHip, EnGardePose.ThighRadius);
            yield return new Capsule(EnGardePose.BackAnkle, EnGardePose.BackKnee, EnGardePose.ShinRadius);
            yield return new Capsule(EnGardePose.BackHeel, EnGardePose.BackToe, EnGardePose.FootRadius);
        }
    }
}
