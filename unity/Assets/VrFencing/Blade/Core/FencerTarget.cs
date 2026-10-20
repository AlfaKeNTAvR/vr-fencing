using System;
using System.Collections.Generic;
using System.Numerics;
using VrFencing.Dummy;
using VrFencing.Partner;
using VrFencing.Session;
using VrFencing.Settings;

namespace VrFencing.Blade
{
    /// <summary>
    /// The Fencer's body as the Armed Dummy attacks it (PER-23 resolution, PER-98), rebuilt each frame from tracking:
    /// what its point can land on (<see cref="FindHit"/>), what a Parry must turn it off (<see cref="Capsules"/>), and
    /// where its chest and thigh thrusts aim (<see cref="TryGetAim"/>).
    ///
    /// Two sources, logged with every hit (<see cref="FencerHitSource"/>):
    /// <list type="bullet">
    /// <item>IOBT: the tracked body's capsules (<see cref="FencerBodyShape"/>), used only while the tracker reports high
    /// fidelity and gives every joint they need (<see cref="IsTrackedBodyConfident"/>).</item>
    /// <item>Headset capsules, otherwise: the en garde stand-in (<see cref="AttackAim.GetFencerStanceOnStrip"/>) moved
    /// along the floor so its head is under the headset's, plus a sword arm from its shoulder to the live guard. Heights
    /// stay the stance's, so the legs stay on the floor.</item>
    /// </list>
    /// With no headset pose there is no Fencer to judge (<see cref="HasBody"/> false). Reuses its lists, so a frame does
    /// not allocate for them. Positions in the Strip frame (+x the Fencer's right, +y up, +z toward the Dummy), meters.
    /// </summary>
    public sealed class FencerTarget
    {
        /// <summary>
        /// How far behind the eyes the head's center is, per meter of height: the headset reports the eyes, the stance
        /// is placed by the head's center (about 9 cm for 1.75 m).
        /// </summary>
        public const float HeadCenterBehindEyesPerHeight = 0.05f;

        /// <summary>
        /// How deep inside the body's front a chest or thigh thrust aims, so a thrust that reaches its aim lands even
        /// with a little tracking jitter.
        /// </summary>
        public const float AimDepthMeters = 0.03f;

        // Along the torso capsule from the hips (0) to the shoulders (1), the chest aim: its middle, as the stand-in aims.
        private const float ChestAlongTorso = 0.5f;

        private readonly List<BodyCapsule> _parts = new List<BodyCapsule>();
        private readonly List<Capsule> _capsules = new List<Capsule>();
        private readonly List<Capsule> _trackedCapsules = new List<Capsule>(FencerBodyShape.CapsuleCount);

        /// <summary>The source of the body this frame; null when there is none.</summary>
        public FencerHitSource? Source { get; private set; }

        /// <summary>True when there is a body this frame.</summary>
        public bool HasBody => Source.HasValue;

        /// <summary>This frame's body, each capsule with its part; empty without one.</summary>
        public IReadOnlyList<BodyCapsule> Parts => _parts;

        /// <summary>This frame's body as plain capsules, for the Parry judge; empty without one.</summary>
        public IReadOnlyList<Capsule> Capsules => _capsules;

        /// <summary>
        /// True when a tracked body is good enough to judge on: the tracker reported high fidelity (IOBT) and gave every
        /// joint of every capsule.
        /// </summary>
        public static bool IsTrackedBodyConfident(FencerBody trackedBody, float fencerHeightMeters)
        {
            return IsTrackedBodyConfident(trackedBody, fencerHeightMeters, new List<Capsule>(FencerBodyShape.CapsuleCount));
        }

        /// <summary>
        /// The head's center from the headset's pose: behind the eyes along where the head looks, level.
        /// </summary>
        public static Vector3 GetHeadCenter(TrackedPose headset, float fencerHeightMeters)
        {
            var forward = Vector3.Transform(Vector3.UnitZ, headset.Rotation);
            var level = new Vector3(forward.X, 0.0f, forward.Z);
            if (level.LengthSquared() < 1e-6f)
            {
                return headset.Position;
            }

            return headset.Position - (Vector3.Normalize(level) * HeadCenterBehindEyesPerHeight * fencerHeightMeters);
        }

        /// <summary>
        /// Rebuilds the body for this frame: the tracked body when it is confident, else the stance under the headset with
        /// the sword arm to the guard, else none.
        /// </summary>
        /// <param name="trackedBody">The tracked body in the Strip frame; null while body tracking gives none.</param>
        /// <param name="headset">The headset's pose in the Strip frame; null when not tracked.</param>
        /// <param name="fencerGuardOnStrip">The front of the Fencer's guard; null while the blade is not tracked.</param>
        /// <param name="stance">The Fencer's stand-in (the Dummy's layout: the Fencer's height and hand).</param>
        /// <exception cref="ArgumentNullException"><paramref name="stance"/> is null.</exception>
        public void Update(FencerBody trackedBody, TrackedPose? headset, Vector3? fencerGuardOnStrip, DummyLayout stance)
        {
            if (stance == null)
            {
                throw new ArgumentNullException(nameof(stance));
            }

            _parts.Clear();
            _capsules.Clear();
            Source = null;
            var height = stance.HeightMeters;
            if (trackedBody != null && IsTrackedBodyConfident(trackedBody, height, _trackedCapsules))
            {
                Source = FencerHitSource.Iobt;
                for (var index = 0; index < _trackedCapsules.Count; index++)
                {
                    Add(FencerBodyShape.CapsuleParts[index], _trackedCapsules[index]);
                }

                return;
            }

            if (!headset.HasValue)
            {
                return;
            }

            Source = FencerHitSource.HeadsetCapsules;
            var headCenter = GetHeadCenter(headset.Value, height);
            var stanceHead = GetStanceHeadCenter(stance);
            var offset = new Vector3(headCenter.X - stanceHead.X, 0.0f, headCenter.Z - stanceHead.Z);
            foreach (var part in AttackAim.GetFencerStanceOnStrip(stance))
            {
                Add(part.Part, Move(part.Shape, offset));
            }

            if (fencerGuardOnStrip.HasValue)
            {
                Add(BodyPart.SwordArm, AttackAim.GetFencerSwordArm(stance, offset, fencerGuardOnStrip.Value));
            }
        }

        /// <summary>
        /// The Armed Dummy's point moved from <paramref name="pointBefore"/> to <paramref name="pointNow"/> this frame:
        /// the first place on the body it met, or null when it met none. A point already inside the body (the Fencer
        /// moved onto it) meets it at <paramref name="pointBefore"/>. The whole path is tested, so a fast point cannot
        /// pass through a thin part between frames.
        /// </summary>
        public FencerHit? FindHit(Vector3 pointBefore, Vector3 pointNow)
        {
            if (!Source.HasValue)
            {
                return null;
            }

            var bestFraction = float.PositiveInfinity;
            var bestPart = default(BodyPart);
            foreach (var part in _parts)
            {
                float fraction;
                if (SegmentMath.FindDistanceToCapsule(pointBefore, part.Shape) <= 0.0f)
                {
                    fraction = 0.0f;
                }
                else if (!SegmentMath.TryFindCapsuleEntry(pointBefore, pointNow, part.Shape, 0.0f, out fraction))
                {
                    continue;
                }

                if (fraction < bestFraction)
                {
                    bestFraction = fraction;
                    bestPart = part.Part;
                }
            }

            if (float.IsPositiveInfinity(bestFraction))
            {
                return null;
            }

            return new FencerHit(Source.Value, bestPart, Vector3.Lerp(pointBefore, pointNow, bestFraction));
        }

        /// <summary>
        /// Where a chest or thigh thrust aims on this frame's body: <see cref="AimDepthMeters"/> inside the front (toward
        /// the Dummy) of the torso's middle or the front thigh's middle. False for the hand and forearm (they follow the
        /// Fencer's blade) and without a body.
        /// </summary>
        public bool TryGetAim(AttackTarget target, out Vector3 aimOnStrip)
        {
            aimOnStrip = default;
            float along;
            BodyPart part;
            switch (target)
            {
                case AttackTarget.Chest:
                    part = BodyPart.Torso;
                    along = ChestAlongTorso;
                    break;
                case AttackTarget.Thigh:
                    part = BodyPart.FrontThigh;
                    along = 0.5f;
                    break;
                default:
                    return false;
            }

            foreach (var candidate in _parts)
            {
                if (candidate.Part != part)
                {
                    continue;
                }

                var shape = candidate.Shape;
                var depth = Math.Max(0.0f, shape.RadiusMeters - AimDepthMeters);
                aimOnStrip = Vector3.Lerp(shape.Start, shape.End, along) + new Vector3(0.0f, 0.0f, depth);
                return true;
            }

            return false;
        }

        private static bool IsTrackedBodyConfident(FencerBody trackedBody, float fencerHeightMeters, List<Capsule> capsules)
        {
            if (trackedBody == null || trackedBody.Fidelity != BodyTrackingFidelity.High)
            {
                return false;
            }

            FencerBodyShape.GetCapsules(trackedBody, fencerHeightMeters, capsules);
            foreach (var capsule in capsules)
            {
                if (FencerBodyShape.IsParked(capsule))
                {
                    return false;
                }
            }

            return true;
        }

        private static Vector3 GetStanceHeadCenter(DummyLayout stance)
        {
            var head = EnGardePose.HeadCenter * stance.HeightMeters;
            return stance.Hand == Hand.Left ? HandMirroring.MirrorPosition(head) : head;
        }

        private static Capsule Move(Capsule capsule, Vector3 offset)
        {
            return new Capsule(capsule.Start + offset, capsule.End + offset, capsule.RadiusMeters);
        }

        private void Add(BodyPart part, Capsule shape)
        {
            _parts.Add(new BodyCapsule(part, shape));
            _capsules.Add(shape);
        }
    }
}
