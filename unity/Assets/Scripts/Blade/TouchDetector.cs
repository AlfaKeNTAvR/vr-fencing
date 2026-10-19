// Tip Touch and flat-contact detection, run once per rendered frame with the
// blade pose of the previous and the current frame. Swept, so a 10-15 m/s tip
// (14-21 cm per frame at 72 Hz) cannot pass through a Target unseen.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace VrFencing.Blade
{
    public sealed class TouchDetector
    {
        // The blade body sweeps a surface between frames; it is sampled at this many
        // poses (straight-line interpolation of guard and tip). 8 keeps the sample gap
        // under 3 cm at the point for a 15 m/s tip at 72 Hz.
        private const int FlatContactSubsteps = 8;
        private const float MinDeltaTimeSeconds = 1e-4f;

        private static readonly BladeEvent[] NoEvents = Array.Empty<BladeEvent>();

        private readonly SphereTarget[] _targets;
        // Per Target: was the tip inside / blade body touching at the end of the last step.
        // A new Touch or flat contact needs the tip / body to leave first.
        private readonly bool[] _isTipInside;
        private readonly bool[] _isBodyTouching;

        public TouchDetector(IReadOnlyList<SphereTarget> targets)
        {
            _targets = new SphereTarget[targets.Count];
            for (var index = 0; index < targets.Count; index++)
                _targets[index] = targets[index];
            _isTipInside = new bool[_targets.Length];
            _isBodyTouching = new bool[_targets.Length];
        }

        public IReadOnlyList<SphereTarget> Targets => _targets;

        /// <summary>
        /// Advances one frame. Returns the Touch, glance and flat-contact events that
        /// started during the frame, at most one tip event and one flat event per Target.
        /// </summary>
        public IReadOnlyList<BladeEvent> Step(BladeSegment previous, BladeSegment current, float deltaTimeSeconds,
            TouchRules rules)
        {
            List<BladeEvent> events = null;
            var tipVelocity = (current.TipPosition - previous.TipPosition) / Mathf.Max(deltaTimeSeconds, MinDeltaTimeSeconds);

            for (var index = 0; index < _targets.Length; index++)
            {
                var target = _targets[index];
                var tipSphereRadius = target.Radius + rules.TipRadiusMeters;
                var wasTipInside = _isTipInside[index];
                var isTipInsideNow = (current.TipPosition - target.Center).sqrMagnitude <= tipSphereRadius * tipSphereRadius;

                BladeEvent? tipEvent = null;
                if (!wasTipInside && SegmentMath.TryFindSphereEntry(previous.TipPosition, current.TipPosition,
                        target.Center, tipSphereRadius, out var entryFraction))
                {
                    tipEvent = ClassifyTipEntry(index, target, BladeSegment.Lerp(previous, current, entryFraction),
                        tipVelocity, rules);
                }

                // Pushing the point on into the Target after a Touch must not also read as flat.
                var isTipInvolved = wasTipInside || isTipInsideNow || tipEvent.HasValue;
                var bodyContact = FindBodyContact(target, previous, current, rules);
                var isBodyTouchingNow = IsBodyTouching(target, current, rules);

                if (tipEvent.HasValue)
                    (events ??= new List<BladeEvent>()).Add(tipEvent.Value);
                if (!_isBodyTouching[index] && !isTipInvolved && bodyContact.HasValue)
                {
                    (events ??= new List<BladeEvent>()).Add(new BladeEvent(BladeEventType.FlatContact, index,
                        bodyContact.Value, tipVelocity.magnitude, float.NaN, float.NaN));
                }

                _isTipInside[index] = isTipInsideNow;
                _isBodyTouching[index] = isBodyTouchingNow;
            }

            return events ?? (IReadOnlyList<BladeEvent>)NoEvents;
        }

        private static BladeEvent ClassifyTipEntry(int targetIndex, SphereTarget target, BladeSegment bladeAtEntry,
            Vector3 tipVelocity, TouchRules rules)
        {
            var entryPoint = bladeAtEntry.TipPosition;
            var inwardNormal = (target.Center - entryPoint).normalized;
            var bladeDirection = (bladeAtEntry.TipPosition - bladeAtEntry.GuardPosition).normalized;

            var tipSpeed = tipVelocity.magnitude;
            var normalSpeed = Vector3.Dot(tipVelocity, inwardNormal);
            var approachAngle = Vector3.Angle(tipVelocity, inwardNormal);
            var bladeAxisAngle = Vector3.Angle(bladeDirection, inwardNormal);

            var isPointFirst = normalSpeed >= rules.MinNormalSpeedMetersPerSecond
                               && approachAngle <= rules.MaxApproachAngleDegrees
                               && bladeAxisAngle <= rules.MaxBladeAxisAngleDegrees;
            var type = isPointFirst ? BladeEventType.Touch : BladeEventType.TipGlance;
            var contactPoint = target.Center - inwardNormal * target.Radius;
            return new BladeEvent(type, targetIndex, contactPoint, tipSpeed, approachAngle, bladeAxisAngle);
        }

        /// <summary>First sampled point where the blade body touched the Target during the frame, if any.</summary>
        private static Vector3? FindBodyContact(SphereTarget target, BladeSegment previous, BladeSegment current,
            TouchRules rules)
        {
            for (var substep = 0; substep <= FlatContactSubsteps; substep++)
            {
                var blade = BladeSegment.Lerp(previous, current, (float)substep / FlatContactSubsteps);
                if (IsBodyTouching(target, blade, rules))
                    return target.Center; // Good enough for logging; the exact point is not needed yet.
            }

            return null;
        }

        private static bool IsBodyTouching(SphereTarget target, BladeSegment blade, TouchRules rules)
        {
            var bladeVector = blade.TipPosition - blade.GuardPosition;
            var bladeLength = bladeVector.magnitude;
            if (bladeLength <= rules.TipZoneLengthMeters)
                return false;

            var bodyEnd = blade.GuardPosition + bladeVector * ((bladeLength - rules.TipZoneLengthMeters) / bladeLength);
            var distance = SegmentMath.FindDistanceFromPointToSegment(target.Center, blade.GuardPosition, bodyEnd);
            return distance <= target.Radius + rules.BladeRadiusMeters;
        }
    }
}
