using System;
using System.Collections.Generic;
using System.Numerics;

namespace VrFencing.Blade
{
    /// <summary>
    /// The Fencer's blade meeting the Armed Dummy's blade (PER-40): where, how far along each blade, and how hard.
    /// Positions in one frame (the Strip frame in the app), meters, seconds.
    /// </summary>
    public readonly struct BladeClash
    {
        public BladeClash(
            double timeSeconds,
            Vector3 contactPoint,
            float fencerBladeFraction,
            float opponentBladeFraction,
            float relativeSpeedMetersPerSecond,
            Vector3 pushDirection)
        {
            TimeSeconds = timeSeconds;
            ContactPoint = contactPoint;
            FencerBladeFraction = fencerBladeFraction;
            OpponentBladeFraction = opponentBladeFraction;
            RelativeSpeedMetersPerSecond = relativeSpeedMetersPerSecond;
            PushDirection = pushDirection;
        }

        public double TimeSeconds { get; }

        /// <summary>Midway between the two blades' closest points.</summary>
        public Vector3 ContactPoint { get; }

        /// <summary>Where on the Fencer's blade: 0 at the guard (forte) to 1 at the point (foible).</summary>
        public float FencerBladeFraction { get; }

        /// <summary>Where on the Dummy's blade: 0 at its guard to 1 at its point.</summary>
        public float OpponentBladeFraction { get; }

        /// <summary>
        /// How fast the Fencer's blade was moving into the Dummy's at the contact: their relative speed square to the
        /// Dummy's blade. How hard the contact was.
        /// </summary>
        public float RelativeSpeedMetersPerSecond { get; }

        /// <summary>Unit vector square to the Dummy's blade: the way the Fencer's blade pushes it. Zero if it did not move.</summary>
        public Vector3 PushDirection { get; }

        /// <summary>The clash buzz's strength, from <see cref="BladeClashDetector.HapticFloor"/> to 1 with the contact's speed.</summary>
        public float HapticStrength => BladeClashDetector.GetHapticStrength(RelativeSpeedMetersPerSecond);
    }

    /// <summary>
    /// Finds the Fencer's blade meeting the Armed Dummy's blade, as two straight segments (guard to point) fed every frame
    /// (prototype PER-95). The test is swept: both blades are moved from last frame's pose to this frame's in steps of at
    /// most <see cref="SweepStepMeters"/>, so a fast parry or beat cannot pass through a 1 cm blade between frames.
    /// One contact raises one <see cref="BladeClash"/>; the blades must come apart by <see cref="ReleaseDistanceMeters"/>
    /// before the next.
    /// </summary>
    public sealed class BladeClashDetector
    {
        /// <summary>
        /// Centerline to centerline at which the blades touch: two half-widths of an épée blade near the forte (about
        /// 5 mm each) plus a little for tracking jitter.
        /// </summary>
        public const float ContactDistanceMeters = 0.012f;

        /// <summary>The blades must part this far before another contact counts, so jitter along a held contact does not buzz.</summary>
        public const float ReleaseDistanceMeters = 0.03f;

        /// <summary>Longest step of the sweep between frames.</summary>
        public const float SweepStepMeters = 0.005f;

        /// <summary>Most steps in one frame: a 15 m/s point moves 21 cm in a 72 Hz frame, 42 steps of 5 mm.</summary>
        public const int MaxSweepSteps = 64;

        /// <summary>The clash buzz at the lightest contact, as a fraction of full: still felt.</summary>
        public const float HapticFloor = 0.35f;

        /// <summary>Relative speed at which the clash buzz is full (a firm parry is about 1 to 2 m/s at the blade).</summary>
        public const float HapticFullSpeedMetersPerSecond = 2.0f;

        /// <summary>The clash buzz: a sharp, high hit that rings down, unlike the Touch's single thump.</summary>
        public static IReadOnlyList<HapticSegment> HapticSegments { get; } = new[]
        {
            new HapticSegment(1.0f, 1.0f, 0.015f),
            new HapticSegment(0.55f, 0.85f, 0.030f),
            new HapticSegment(0.25f, 0.7f, 0.040f),
        };

        private BladeSegment? _lastFencer;
        private BladeSegment? _lastOpponent;
        private double _lastTimeSeconds;

        /// <summary>True from a contact until the blades part by <see cref="ReleaseDistanceMeters"/>.</summary>
        public bool IsInContact { get; private set; }

        /// <summary>From <see cref="HapticFloor"/> at no speed, rising in a straight line to 1 at <see cref="HapticFullSpeedMetersPerSecond"/>.</summary>
        public static float GetHapticStrength(float relativeSpeedMetersPerSecond)
        {
            var fraction = Math.Clamp(relativeSpeedMetersPerSecond / HapticFullSpeedMetersPerSecond, 0.0f, 1.0f);
            return HapticFloor + ((1.0f - HapticFloor) * fraction);
        }

        /// <summary>Forget the last poses, e.g. after a jump or when the Dummy's blade appears.</summary>
        public void Reset()
        {
            _lastFencer = null;
            _lastOpponent = null;
            IsInContact = false;
        }

        /// <summary>Takes this frame's blades and returns the clash, if they met since the last frame.</summary>
        public BladeClash? Step(double nowSeconds, BladeSegment fencer, BladeSegment opponent)
        {
            var fencerBefore = _lastFencer ?? fencer;
            var opponentBefore = _lastOpponent ?? opponent;
            var deltaSeconds = (float)(nowSeconds - _lastTimeSeconds);
            var hasMotion = _lastFencer.HasValue && deltaSeconds > 1e-5f;
            _lastFencer = fencer;
            _lastOpponent = opponent;
            _lastTimeSeconds = nowSeconds;

            if (IsInContact)
            {
                IsInContact = FindDistance(fencer, opponent, out _, out _) < ReleaseDistanceMeters;
                return null;
            }

            var steps = CountSweepSteps(fencerBefore, fencer, opponentBefore, opponent);
            for (var step = 1; step <= steps; step++)
            {
                var fraction = (float)step / steps;
                var fencerNow = BladeSegment.Lerp(fencerBefore, fencer, fraction);
                var opponentNow = BladeSegment.Lerp(opponentBefore, opponent, fraction);
                var distance = FindDistance(fencerNow, opponentNow, out var onFencer, out var onOpponent);
                if (distance > ContactDistanceMeters)
                {
                    continue;
                }

                IsInContact = true;
                return CreateClash(nowSeconds, fencerBefore, fencer, opponentBefore, opponent, fencerNow, opponentNow, onFencer, onOpponent,
                    hasMotion ? deltaSeconds : 0.0f);
            }

            return null;
        }

        private static BladeClash CreateClash(
            double nowSeconds,
            BladeSegment fencerBefore,
            BladeSegment fencer,
            BladeSegment opponentBefore,
            BladeSegment opponent,
            BladeSegment fencerAtContact,
            BladeSegment opponentAtContact,
            float onFencer,
            float onOpponent,
            float deltaSeconds)
        {
            var fencerPoint = PointAt(fencerAtContact, onFencer);
            var opponentPoint = PointAt(opponentAtContact, onOpponent);
            var relativeVelocity = Vector3.Zero;
            if (deltaSeconds > 0.0f)
            {
                var fencerVelocity = (PointAt(fencer, onFencer) - PointAt(fencerBefore, onFencer)) / deltaSeconds;
                var opponentVelocity = (PointAt(opponent, onOpponent) - PointAt(opponentBefore, onOpponent)) / deltaSeconds;
                relativeVelocity = fencerVelocity - opponentVelocity;
            }

            // Only motion square to the Dummy's blade pushes it; sliding along it does not.
            var axis = opponentAtContact.Direction;
            var square = relativeVelocity - (Vector3.Dot(relativeVelocity, axis) * axis);
            var speed = square.Length();
            var push = speed > 1e-4f ? square / speed : Vector3.Zero;
            return new BladeClash(nowSeconds, (fencerPoint + opponentPoint) * 0.5f, onFencer, onOpponent, speed, push);
        }

        private static int CountSweepSteps(BladeSegment fencerBefore, BladeSegment fencer, BladeSegment opponentBefore, BladeSegment opponent)
        {
            var largestMove = Math.Max(
                Math.Max(Vector3.Distance(fencerBefore.GuardPosition, fencer.GuardPosition), Vector3.Distance(fencerBefore.TipPosition, fencer.TipPosition)),
                Math.Max(Vector3.Distance(opponentBefore.GuardPosition, opponent.GuardPosition), Vector3.Distance(opponentBefore.TipPosition, opponent.TipPosition)));
            return Math.Clamp((int)Math.Ceiling(largestMove / SweepStepMeters), 1, MaxSweepSteps);
        }

        private static float FindDistance(BladeSegment fencer, BladeSegment opponent, out float onFencer, out float onOpponent)
        {
            SegmentMath.FindDistanceBetweenSegments(fencer.GuardPosition, fencer.TipPosition, opponent.GuardPosition, opponent.TipPosition, out onFencer);
            var fencerPoint = PointAt(fencer, onFencer);
            onOpponent = FindFraction(fencerPoint, opponent);
            return Vector3.Distance(fencerPoint, PointAt(opponent, onOpponent));
        }

        private static float FindFraction(Vector3 point, BladeSegment blade)
        {
            var along = blade.TipPosition - blade.GuardPosition;
            var lengthSquared = along.LengthSquared();
            return lengthSquared < 1e-12f ? 0.0f : Math.Clamp(Vector3.Dot(point - blade.GuardPosition, along) / lengthSquared, 0.0f, 1.0f);
        }

        private static Vector3 PointAt(BladeSegment blade, float fraction)
        {
            return Vector3.Lerp(blade.GuardPosition, blade.TipPosition, fraction);
        }
    }
}
