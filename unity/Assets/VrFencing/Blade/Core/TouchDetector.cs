using System;
using System.Collections.Generic;
using System.Numerics;
using VrFencing.Dummy;

namespace VrFencing.Blade
{
    /// <summary>
    /// Finds Touches, tip glances and flat contacts between the blade and the Dummy's Target Zones, fed one blade pose
    /// per rendered frame as plain data (ADR 0002), so a partner's blade could feed the same detector later.
    ///
    /// The tip is swept from the last pose to this one against each zone's capsule, so a 10-15 m/s tip (14-21 cm per
    /// frame at 72 Hz) cannot pass through a zone unseen. When the tip enters a zone, the entry is judged against
    /// <see cref="TouchRules"/>: a Touch if it moved into the surface fast enough and both its approach and the blade
    /// point in; otherwise a glance. The approach is the tip's motion over the last
    /// <see cref="ApproachWindowSeconds"/> before the entry, and the blade direction is averaged over the same window,
    /// not taken from one frame: one 72 Hz frame step is only 28 mm at 2 m/s, so a single noisy or curving frame made
    /// fast point-first thrusts read as sideways in the prototype (PER-22 feel test 2).
    ///
    /// Zones overlap where the arm's capsules meet, so the tip produces at most one event per entry: only when it comes
    /// from outside every zone, judged against the zone it reaches first. Flat contact fires when the blade body
    /// (all but the last <see cref="TouchRules.TipZoneLengthMeters"/>) crosses a zone while the point is not in any zone.
    /// A zone needs the tip, or the body, to leave before it can report again.
    /// </summary>
    public sealed class TouchDetector
    {
        /// <summary>The approach is judged over at least this much motion history (about 3 frames at 72 Hz).</summary>
        public const double ApproachWindowSeconds = 0.04;

        /// <summary>If the tip moved less than this over the window (a slow press), the window stretches back further, so hand tremor does not decide the direction.</summary>
        public const float MinApproachChordMeters = 0.015f;

        /// <summary>The window never stretches further back than this.</summary>
        public const double MaxApproachWindowSeconds = 0.1;

        // The blade body sweeps a surface between frames; it is sampled at this many poses. 8 keeps the sample gap
        // under 3 cm at the point for a 15 m/s tip at 72 Hz.
        private const int FlatContactSubsteps = 8;

        private const double MinStepSeconds = 1e-4;
        private const float MinChordLengthSquared = 1e-10f;

        private static readonly BladeEvent[] NoEvents = Array.Empty<BladeEvent>();

        // Newest last. Enough history for MaxApproachWindowSeconds.
        private readonly List<BladeSample> _history = new List<BladeSample>();
        private readonly HashSet<TargetZone> _bodyTouchingZones = new HashSet<TargetZone>();
        private bool _wasTipInAnyZone;

        /// <summary>Forgets the motion history, e.g. after the blade jumped (tracking lost, mount changed, Strip moved).</summary>
        public void Reset()
        {
            _history.Clear();
            _bodyTouchingZones.Clear();
            _wasTipInAnyZone = false;
        }

        /// <summary>
        /// Advances to a new blade pose. Returns the events that started since the last pose: at most one tip event
        /// (Touch or glance) and one flat contact per zone. The first pose after construction or <see cref="Reset"/> only
        /// records where the blade is.
        /// </summary>
        public IReadOnlyList<BladeEvent> Step(BladeSample current, IReadOnlyList<ZoneCapsule> zones, TouchRules rules)
        {
            if (zones == null)
            {
                throw new ArgumentNullException(nameof(zones));
            }

            if (_history.Count == 0)
            {
                Record(current, zones, rules);
                return NoEvents;
            }

            var previous = _history[_history.Count - 1];
            if (current.TimeSeconds - previous.TimeSeconds < MinStepSeconds)
            {
                // A repeated or out-of-order pose has no motion to judge; keep the older one.
                return NoEvents;
            }

            List<BladeEvent> events = null;
            BladeEvent? tipEvent = null;
            if (!_wasTipInAnyZone)
            {
                tipEvent = FindTipEntry(previous, current, zones, rules);
                if (tipEvent.HasValue)
                {
                    events = new List<BladeEvent> { tipEvent.Value };
                }
            }

            // Pushing the point on into a zone after a Touch, or the body following the point in, must not also read as flat.
            var isTipInvolved = _wasTipInAnyZone || tipEvent.HasValue || IsTipInAnyZone(current.Blade.TipPosition, zones, rules);
            foreach (var zone in zones)
            {
                if (isTipInvolved || _bodyTouchingZones.Contains(zone.Zone))
                {
                    continue;
                }

                if (TryFindBodyContact(zone.Shape, previous.Blade, current.Blade, rules, out var contactPoint))
                {
                    (events ??= new List<BladeEvent>()).Add(new BladeEvent(
                        BladeEventType.FlatContact,
                        zone.Zone,
                        contactPoint,
                        FindStepSpeed(previous, current),
                        float.NaN,
                        float.NaN,
                        Vector3.Distance(contactPoint, zone.Shape.Center)));
                }
            }

            Record(current, zones, rules);
            return events ?? (IReadOnlyList<BladeEvent>)NoEvents;
        }

        private BladeEvent? FindTipEntry(BladeSample previous, BladeSample current, IReadOnlyList<ZoneCapsule> zones, TouchRules rules)
        {
            ZoneCapsule? firstZone = null;
            var firstFraction = float.MaxValue;
            foreach (var zone in zones)
            {
                if (SegmentMath.TryFindCapsuleEntry(previous.Blade.TipPosition, current.Blade.TipPosition, zone.Shape, rules.TipRadiusMeters, out var fraction)
                    && fraction < firstFraction)
                {
                    firstFraction = fraction;
                    firstZone = zone;
                }
            }

            if (!firstZone.HasValue)
            {
                return null;
            }

            var entry = new BladeSample(
                previous.TimeSeconds + ((current.TimeSeconds - previous.TimeSeconds) * firstFraction),
                BladeSegment.Lerp(previous.Blade, current.Blade, firstFraction));
            return ClassifyTipEntry(firstZone.Value, entry, previous, current, rules);
        }

        private BladeEvent ClassifyTipEntry(ZoneCapsule zone, BladeSample entry, BladeSample previous, BladeSample current, TouchRules rules)
        {
            var entryPoint = entry.Blade.TipPosition;
            var inwardNormal = -SegmentMath.FindOutwardNormal(entryPoint, zone.Shape);

            var approachVelocity = FindApproachVelocity(entry, previous, current);
            var bladeDirection = FindAverageBladeDirection(entry);

            var normalSpeed = Vector3.Dot(approachVelocity, inwardNormal);
            var approachAngle = SegmentMath.FindAngleDegrees(approachVelocity, inwardNormal);
            var bladeAxisAngle = SegmentMath.FindAngleDegrees(bladeDirection, inwardNormal);

            var isPointFirst = normalSpeed >= rules.MinNormalSpeedMetersPerSecond
                && approachAngle <= rules.MaxApproachAngleDegrees
                && bladeAxisAngle <= rules.MaxBladeAxisAngleDegrees;
            var contactPoint = entryPoint + (inwardNormal * rules.TipRadiusMeters);
            return new BladeEvent(
                isPointFirst ? BladeEventType.Touch : BladeEventType.TipGlance,
                zone.Zone,
                contactPoint,
                approachVelocity.Length(),
                approachAngle,
                bladeAxisAngle,
                Vector3.Distance(contactPoint, zone.Shape.Center));
        }

        /// <summary>
        /// The tip's average velocity from the newest recorded pose at least <see cref="ApproachWindowSeconds"/> before the
        /// entry (and <see cref="MinApproachChordMeters"/> away from it) to the entry point. With too little history, the
        /// oldest pose is used; with no motion at all, this frame's step.
        /// </summary>
        private Vector3 FindApproachVelocity(BladeSample entry, BladeSample previous, BladeSample current)
        {
            var start = _history[0];
            for (var index = _history.Count - 1; index >= 0; index--)
            {
                var candidate = _history[index];
                var elapsed = entry.TimeSeconds - candidate.TimeSeconds;
                var chordLength = Vector3.Distance(entry.Blade.TipPosition, candidate.Blade.TipPosition);
                if ((elapsed >= ApproachWindowSeconds && chordLength >= MinApproachChordMeters) || elapsed >= MaxApproachWindowSeconds)
                {
                    start = candidate;
                    break;
                }
            }

            var chord = entry.Blade.TipPosition - start.Blade.TipPosition;
            var seconds = entry.TimeSeconds - start.TimeSeconds;
            if (chord.LengthSquared() < MinChordLengthSquared || seconds < MinStepSeconds)
            {
                return (current.Blade.TipPosition - previous.Blade.TipPosition) / (float)(current.TimeSeconds - previous.TimeSeconds);
            }

            return chord / (float)seconds;
        }

        /// <summary>The blade direction at the entry averaged with the recorded poses inside the approach window.</summary>
        private Vector3 FindAverageBladeDirection(BladeSample entry)
        {
            var sum = entry.Blade.Direction;
            for (var index = _history.Count - 1; index >= 0; index--)
            {
                var sample = _history[index];
                if (entry.TimeSeconds - sample.TimeSeconds > ApproachWindowSeconds)
                {
                    break;
                }

                sum += sample.Blade.Direction;
            }

            return sum.LengthSquared() > MinChordLengthSquared ? Vector3.Normalize(sum) : entry.Blade.Direction;
        }

        private void Record(BladeSample sample, IReadOnlyList<ZoneCapsule> zones, TouchRules rules)
        {
            _history.Add(sample);

            // Keep one pose older than the longest window, so the window can always reach back far enough.
            while (_history.Count > 2 && sample.TimeSeconds - _history[1].TimeSeconds > MaxApproachWindowSeconds)
            {
                _history.RemoveAt(0);
            }

            _wasTipInAnyZone = IsTipInAnyZone(sample.Blade.TipPosition, zones, rules);
            _bodyTouchingZones.Clear();
            foreach (var zone in zones)
            {
                if (IsBodyTouching(zone.Shape, sample.Blade, rules, out _))
                {
                    _bodyTouchingZones.Add(zone.Zone);
                }
            }
        }

        private static bool IsTipInAnyZone(Vector3 tip, IReadOnlyList<ZoneCapsule> zones, TouchRules rules)
        {
            foreach (var zone in zones)
            {
                if (SegmentMath.FindDistanceToCapsule(tip, zone.Shape) <= rules.TipRadiusMeters)
                {
                    return true;
                }
            }

            return false;
        }

        private static float FindStepSpeed(BladeSample previous, BladeSample current)
        {
            return Vector3.Distance(previous.Blade.TipPosition, current.Blade.TipPosition) / (float)(current.TimeSeconds - previous.TimeSeconds);
        }

        /// <summary>The first sampled pose during the step where the blade body touched the zone, if any.</summary>
        private static bool TryFindBodyContact(Capsule zone, BladeSegment previous, BladeSegment current, TouchRules rules, out Vector3 contactPoint)
        {
            for (var substep = 0; substep <= FlatContactSubsteps; substep++)
            {
                var blade = BladeSegment.Lerp(previous, current, (float)substep / FlatContactSubsteps);
                if (IsBodyTouching(zone, blade, rules, out contactPoint))
                {
                    return true;
                }
            }

            contactPoint = default;
            return false;
        }

        private static bool IsBodyTouching(Capsule zone, BladeSegment blade, TouchRules rules, out Vector3 contactPoint)
        {
            contactPoint = default;
            var bladeVector = blade.TipPosition - blade.GuardPosition;
            var bladeLength = bladeVector.Length();
            if (bladeLength <= rules.TipZoneLengthMeters)
            {
                return false;
            }

            var bodyEnd = blade.GuardPosition + (bladeVector * ((bladeLength - rules.TipZoneLengthMeters) / bladeLength));
            var distance = SegmentMath.FindDistanceBetweenSegments(blade.GuardPosition, bodyEnd, zone.Start, zone.End, out var fractionOnBody);
            if (distance > zone.RadiusMeters + rules.BladeRadiusMeters)
            {
                return false;
            }

            var bodyPoint = Vector3.Lerp(blade.GuardPosition, bodyEnd, fractionOnBody);
            var axisPoint = SegmentMath.FindClosestPointOnSegment(bodyPoint, zone.Start, zone.End);
            contactPoint = axisPoint + (SegmentMath.FindOutwardNormal(bodyPoint, zone) * zone.RadiusMeters);
            return true;
        }
    }
}
