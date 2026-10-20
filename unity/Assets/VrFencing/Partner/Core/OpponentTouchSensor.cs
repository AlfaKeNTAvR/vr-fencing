using System;
using System.Collections.Generic;
using System.Numerics;
using VrFencing.Blade;
using VrFencing.Dummy;
using VrFencing.Settings;
using VrFencing.Strip;

namespace VrFencing.Partner
{
    /// <summary>
    /// The opponent's body as Touch targets (PER-82): the same capsules their drawn blade-stopping body uses
    /// (<see cref="FencerBodyShape"/>), each named by the nearest <see cref="TargetZone"/> for the logs and claims. In
    /// épée the whole body scores, so the names never decide anything. The head and the torso are "Body"; the shins
    /// count with the feet as "Foot" (the zones have no head or shin of their own).
    /// </summary>
    public static class OpponentTargets
    {
        /// <summary>One name per <see cref="FencerBodyShape"/> capsule, in its order.</summary>
        public static IReadOnlyList<TargetZone> ZoneOfCapsule { get; } = new[]
        {
            TargetZone.Body, // head
            TargetZone.Body, // torso
            TargetZone.UpperArm,
            TargetZone.UpperArm,
            TargetZone.Forearm,
            TargetZone.Forearm,
            TargetZone.Hand,
            TargetZone.Hand,
            TargetZone.Thigh,
            TargetZone.Thigh,
            TargetZone.Foot, // shins
            TargetZone.Foot,
            TargetZone.Foot,
            TargetZone.Foot,
        };

        /// <summary>Fills <paramref name="zones"/> (cleared first) with the body's named capsules; missing parts are parked out of reach.</summary>
        public static void GetZones(FencerBody body, float heightMeters, List<Capsule> capsules, List<ZoneCapsule> zones)
        {
            if (zones == null)
            {
                throw new ArgumentNullException(nameof(zones));
            }

            FencerBodyShape.GetCapsules(body, heightMeters, capsules);
            zones.Clear();
            for (var index = 0; index < capsules.Count; index++)
            {
                zones.Add(new ZoneCapsule(ZoneOfCapsule[index], capsules[index]));
            }
        }
    }

    /// <summary>Which way a Fencer left their own placed Strip, if they did.</summary>
    public enum OffStripKind : byte
    {
        None,

        /// <summary>Off a side line.</summary>
        Side,

        /// <summary>Past the rear end, behind them.</summary>
        RearEnd,
    }

    /// <summary>
    /// Where a Fencer stands on their own placed Strip, for the bout's off-Strip rule (PER-56): a Touch made with both
    /// feet off the side or past the rear end of the attacker's own Strip does not light, with the Fencer's off-strip
    /// tolerance, and in a Bout leaving it halts (PER-83). The ankles stand for the feet (the toes when an ankle is
    /// missing); with no foot tracked the head stands for both, as the Strip's own off-strip signal does. The front end
    /// is not checked: past it is the opponent's half of the bout Strip.
    /// </summary>
    public static class OwnStripCheck
    {
        /// <summary>True when both feet (or the head standing in for them) are off the side or past the rear end.</summary>
        public static bool IsOffOwnStrip(FencerBody body, Vector3? head, StripExtent strip, float toleranceMeters)
        {
            return FindOffStrip(body, head, strip, toleranceMeters) != OffStripKind.None;
        }

        /// <summary>
        /// Which way both feet (or the head standing in for them) are off: past the rear end when either foot is past it,
        /// else off a side; <see cref="OffStripKind.None"/> while a foot is still on.
        /// </summary>
        public static OffStripKind FindOffStrip(FencerBody body, Vector3? head, StripExtent strip, float toleranceMeters)
        {
            var tolerance = Math.Max(0.0f, toleranceMeters);
            var front = FindFoot(body, SkeletonJoint.FrontAnkle, SkeletonJoint.FrontToe);
            var back = FindFoot(body, SkeletonJoint.BackAnkle, SkeletonJoint.BackToe);
            if (front.HasValue && back.HasValue)
            {
                var frontOff = Classify(front.Value, strip, tolerance);
                var backOff = Classify(back.Value, strip, tolerance);
                if (frontOff == OffStripKind.None || backOff == OffStripKind.None)
                {
                    return OffStripKind.None;
                }

                return frontOff == OffStripKind.RearEnd || backOff == OffStripKind.RearEnd ? OffStripKind.RearEnd : OffStripKind.Side;
            }

            var only = front ?? back ?? head;
            return only.HasValue ? Classify(only.Value, strip, tolerance) : OffStripKind.None;
        }

        private static Vector3? FindFoot(FencerBody body, SkeletonJoint ankle, SkeletonJoint toe)
        {
            return body?.Get(ankle) ?? body?.Get(toe);
        }

        private static OffStripKind Classify(Vector3 point, StripExtent strip, float tolerance)
        {
            if (point.Z < strip.RearEndZ - tolerance)
            {
                return OffStripKind.RearEnd;
            }

            return Math.Abs(point.X) > (StripGeometry.WidthMeters * 0.5f) + tolerance ? OffStripKind.Side : OffStripKind.None;
        }
    }

    /// <summary>
    /// The attacker's side of the Touch rule in a bout (PER-82): this Fencer's blade against the opponent's body as drawn
    /// in this headset, through the same <see cref="TouchDetector"/> the Dummy uses, so a Touch, a glance and a flat
    /// contact read the same on a partner as on the Dummy. The blade is the line from the guard to the drooped point,
    /// built from the sword-hand controller and the Fencer's grip settings exactly as the Blade does. Glances and flat
    /// contacts never light: only Touches are claimed.
    /// </summary>
    public sealed class OpponentTouchSensor
    {
        private static readonly IReadOnlyList<BladeEvent> NoEvents = Array.Empty<BladeEvent>();

        private readonly TouchDetector _detector = new TouchDetector();
        private readonly List<Capsule> _capsules = new List<Capsule>(FencerBodyShape.CapsuleCount);
        private readonly List<ZoneCapsule> _zones = new List<ZoneCapsule>(FencerBodyShape.CapsuleCount);

        /// <summary>The opponent's named capsules from the last step (parked before the first body).</summary>
        public IReadOnlyList<ZoneCapsule> Zones => _zones;

        /// <summary>
        /// The blade used to judge, in the controller's frame (the Strip frame in the app): from the front of the guard
        /// to the drooped point, as <c>BladeBehaviour</c> builds it from the same controller and settings.
        /// </summary>
        public static BladeSegment GetBlade(TrackedPose controller, BladeSettings blade, Hand hand)
        {
            if (blade == null)
            {
                throw new ArgumentNullException(nameof(blade));
            }

            var mountInController = BladePose.GetMountInController(blade, hand);
            var forte = BladePose.GetBlade(controller, mountInController);
            var gripUp = Vector3.Transform(Vector3.UnitY, controller.Rotation * mountInController.Rotation);
            var droop = BladeDroop.Create(forte.Direction, gripUp, -Vector3.UnitY, blade);
            return droop.GetChord(forte);
        }

        /// <summary>
        /// Advances to this frame. <paramref name="blade"/> null (controller lost) or <paramref name="opponentBody"/>
        /// null (none drawn, or a stale ghost) forgets the motion, so nothing is judged across the gap. Returns the
        /// events that started since the last frame.
        /// </summary>
        public IReadOnlyList<BladeEvent> Step(double timeSeconds, BladeSegment? blade, FencerBody opponentBody, float opponentHeightMeters, TouchRules rules)
        {
            if (!blade.HasValue || opponentBody == null)
            {
                Reset();
                return NoEvents;
            }

            OpponentTargets.GetZones(opponentBody, opponentHeightMeters, _capsules, _zones);
            return _detector.Step(new BladeSample(timeSeconds, blade.Value), _zones, rules);
        }

        public void Reset()
        {
            _detector.Reset();
        }
    }
}
