using System;
using System.Collections.Generic;
using System.Numerics;
using VrFencing.Settings;

namespace VrFencing.Blade
{
    /// <summary>One held straight-arm thrust: the sword-hand controller's pose and the target it aimed at, room frame.</summary>
    public readonly struct ThrustCapture
    {
        public ThrustCapture(TrackedPose controller, Vector3 target)
        {
            Controller = controller;
            Target = target;
        }

        public TrackedPose Controller { get; }

        public Vector3 Target { get; }
    }

    /// <summary>The pitch and yaw fitted from the thrusts, and how far apart the single thrusts' fits were.</summary>
    public readonly struct GripFit
    {
        public GripFit(GripAngles angles, float spreadDegrees)
        {
            Angles = angles;
            SpreadDegrees = spreadDegrees;
        }

        public GripAngles Angles { get; }

        /// <summary>The largest angle between one thrust's blade direction and the fitted one: small means consistent thrusts.</summary>
        public float SpreadDegrees { get; }
    }

    /// <summary>
    /// The thrust calibration (PER-52, from the PER-51 research): the Fencer thrusts with a straight arm at
    /// <see cref="TargetCount"/> targets in turn, so the shoulder, the hand and the point form one line, and holds each for
    /// <see cref="HoldSeconds"/>. The blade's pitch and yaw for the chosen grip are then fitted so the real (drooped) point
    /// lies on the line from the guard to each target, averaged over the targets.
    ///
    /// The shoulder is estimated from the head: <see cref="ShoulderBelowEyesMeters"/> down and
    /// <see cref="ShoulderSideMeters"/> to the sword side. A thrust counts while the hand is at least
    /// <see cref="MinimumReachMeters"/> from the shoulder and within <see cref="AimToleranceDegrees"/> of the shoulder's
    /// line to the target, and is taken once the controller has stayed within <see cref="SteadyMeters"/> and
    /// <see cref="SteadyDegrees"/> for <see cref="HoldSeconds"/>. Positions in the room frame (Unity world), meters.
    /// </summary>
    public sealed class ThrustCalibration
    {
        public const int TargetCount = 3;

        /// <summary>The targets stand this far from the shoulder: far enough that the arm's line, not the hand, aims.</summary>
        public const float TargetDistanceMeters = 2.0f;

        public const float HoldSeconds = 1.0f;

        /// <summary>The hand's line from the shoulder must point this close to the target: a straight arm at it.</summary>
        public const float AimToleranceDegrees = 12.0f;

        /// <summary>The hand at least this far from the shoulder: the arm is extended, not bent.</summary>
        public const float MinimumReachMeters = 0.4f;

        /// <summary>A hold restarts when the controller moves more than this from where it began.</summary>
        public const float SteadyMeters = 0.02f;

        /// <summary>A hold restarts when the controller turns more than this from where it began.</summary>
        public const float SteadyDegrees = 3.0f;

        public const float ShoulderBelowEyesMeters = 0.2f;

        public const float ShoulderSideMeters = 0.18f;

        // Each target's direction from the shoulder: degrees toward the sword side, and degrees up. Spread across both so
        // a pitch error and a yaw error show differently.
        private static readonly (float SwordSideDegrees, float UpDegrees)[] TargetDirections =
        {
            (0.0f, 0.0f),
            (-15.0f, 8.0f),
            (15.0f, -10.0f),
        };

        private readonly Hand _hand;
        private readonly Vector3[] _targets = new Vector3[TargetCount];
        private readonly List<ThrustCapture> _captures = new List<ThrustCapture>();

        private double? _holdStartedAt;
        private TrackedPose _holdStart;
        private Vector3 _positionSum;
        private Vector4 _rotationSum;
        private int _holdSamples;

        /// <param name="head">The head (center eye) pose as the calibration starts: the targets are laid out in front of it.</param>
        public ThrustCalibration(TrackedPose head, Hand hand)
        {
            _hand = hand;
            var shoulder = EstimateShoulder(head, hand);
            var (forward, swordSide) = FindHeadingAxes(head, hand);
            for (var index = 0; index < TargetCount; index++)
            {
                var side = ToRadians(TargetDirections[index].SwordSideDegrees);
                var up = ToRadians(TargetDirections[index].UpDegrees);
                var level = (forward * (float)Math.Cos(side)) + (swordSide * (float)Math.Sin(side));
                var direction = (level * (float)Math.Cos(up)) + (Vector3.UnitY * (float)Math.Sin(up));
                _targets[index] = shoulder + (direction * TargetDistanceMeters);
            }
        }

        /// <summary>Every target, room frame, in the order they are thrust at.</summary>
        public IReadOnlyList<Vector3> Targets => _targets;

        /// <summary>The target to thrust at now; equal to <see cref="TargetCount"/> once complete.</summary>
        public int CurrentTargetIndex => _captures.Count;

        public bool IsComplete => _captures.Count >= TargetCount;

        /// <summary>The thrusts taken so far.</summary>
        public IReadOnlyList<ThrustCapture> Captures => _captures;

        /// <summary>True while the arm points at the current target, extended.</summary>
        public bool IsAimed { get; private set; }

        /// <summary>How much of the hold is done, 0 to 1.</summary>
        public float HoldFraction { get; private set; }

        /// <summary>The shoulder estimated from the head: below the eyes and to the sword side, turned with the head's heading.</summary>
        public static Vector3 EstimateShoulder(TrackedPose head, Hand hand)
        {
            var (_, swordSide) = FindHeadingAxes(head, hand);
            return head.Position - (Vector3.UnitY * ShoulderBelowEyesMeters) + (swordSide * ShoulderSideMeters);
        }

        /// <summary>Takes one frame. Returns true when it completed a hold and took a thrust.</summary>
        public bool Step(double timeSeconds, TrackedPose head, TrackedPose controller)
        {
            if (IsComplete)
            {
                return false;
            }

            var shoulder = EstimateShoulder(head, _hand);
            var toHand = controller.Position - shoulder;
            var toTarget = _targets[CurrentTargetIndex] - shoulder;
            IsAimed = toHand.Length() >= MinimumReachMeters && SegmentMath.FindAngleDegrees(toHand, toTarget) <= AimToleranceDegrees;
            if (!IsAimed)
            {
                _holdStartedAt = null;
                HoldFraction = 0.0f;
                return false;
            }

            if (!_holdStartedAt.HasValue || !IsSteady(controller))
            {
                StartHold(timeSeconds, controller);
                return false;
            }

            _positionSum += controller.Position;
            var rotation = controller.Rotation;
            var sign = Quaternion.Dot(rotation, _holdStart.Rotation) < 0.0f ? -1.0f : 1.0f;
            _rotationSum += new Vector4(rotation.X, rotation.Y, rotation.Z, rotation.W) * sign;
            _holdSamples++;

            var heldSeconds = timeSeconds - _holdStartedAt.Value;
            HoldFraction = (float)Math.Min(1.0, heldSeconds / HoldSeconds);
            if (heldSeconds < HoldSeconds)
            {
                return false;
            }

            var mean = Vector4.Normalize(_rotationSum);
            var held = new TrackedPose(_positionSum / _holdSamples, new Quaternion(mean.X, mean.Y, mean.Z, mean.W));
            _captures.Add(new ThrustCapture(held, _targets[CurrentTargetIndex]));
            _holdStartedAt = null;
            HoldFraction = 0.0f;
            IsAimed = false;
            return true;
        }

        /// <summary>
        /// The pitch and yaw that put the real point of the chosen grip's blade on the line from the guard to each target,
        /// averaged over the thrusts. The droop is taken off (the blade hangs a few degrees below its forte), so the fit
        /// is the forte's angle, as the mount is set.
        /// </summary>
        /// <exception cref="ArgumentException">No thrusts.</exception>
        public static GripFit Fit(IReadOnlyList<ThrustCapture> captures, BladeSettings blade, Hand hand)
        {
            if (captures == null || captures.Count == 0)
            {
                throw new ArgumentException("Needs at least one thrust.", nameof(captures));
            }

            if (blade == null)
            {
                throw new ArgumentNullException(nameof(blade));
            }

            // The guard and the droop depend on the angles a little, so refine from the grip's computed angles.
            const int Refinements = 4;
            var angles = GripGeometry.GetComputedAngles(blade.Mount);
            var directions = new Vector3[captures.Count];
            for (var refinement = 0; refinement < Refinements; refinement++)
            {
                var mount = BladePose.GetMountInController(blade.Mount, angles, hand);
                var sum = Vector3.Zero;
                for (var index = 0; index < captures.Count; index++)
                {
                    directions[index] = FindForteInController(captures[index], mount, blade);
                    sum += directions[index];
                }

                angles = BladePose.GetAnglesForDirection(sum, hand);
            }

            var fitted = BladePose.GetMountInController(blade.Mount, angles, hand);
            var fittedDirection = Vector3.Transform(Vector3.UnitZ, fitted.Rotation);
            var spread = 0.0f;
            foreach (var direction in directions)
            {
                spread = Math.Max(spread, SegmentMath.FindAngleDegrees(direction, fittedDirection));
            }

            return new GripFit(angles, spread);
        }

        /// <summary>
        /// The forte direction, in the controller's frame, that would droop the point onto the line from the guard to the
        /// target, for this thrust and the current estimate of the mount.
        /// </summary>
        private static Vector3 FindForteInController(ThrustCapture capture, TrackedPose mount, BladeSettings blade)
        {
            var controller = capture.Controller;
            var forte = BladePose.GetBlade(controller, mount);
            var gripUp = Vector3.Transform(Vector3.UnitY, controller.Rotation * mount.Rotation);
            var droop = BladeDroop.Create(forte.Direction, gripUp, -Vector3.UnitY, blade);
            var chord = droop.GetChord(forte).Direction;
            var wanted = Vector3.Normalize(capture.Target - forte.GuardPosition);
            var wantedForte = Vector3.Transform(forte.Direction, SegmentMath.FindRotationBetween(chord, wanted));
            return Vector3.Normalize(Vector3.Transform(wantedForte, Quaternion.Inverse(controller.Rotation)));
        }

        private void StartHold(double timeSeconds, TrackedPose controller)
        {
            _holdStartedAt = timeSeconds;
            _holdStart = controller;
            _positionSum = controller.Position;
            var rotation = controller.Rotation;
            _rotationSum = new Vector4(rotation.X, rotation.Y, rotation.Z, rotation.W);
            _holdSamples = 1;
            HoldFraction = 0.0f;
        }

        private bool IsSteady(TrackedPose controller)
        {
            if (Vector3.Distance(controller.Position, _holdStart.Position) > SteadyMeters)
            {
                return false;
            }

            var dot = Math.Min(1.0f, Math.Abs(Quaternion.Dot(Quaternion.Normalize(controller.Rotation), Quaternion.Normalize(_holdStart.Rotation))));
            var turnedDegrees = 2.0 * Math.Acos(dot) * 180.0 / Math.PI;
            return turnedDegrees <= SteadyDegrees;
        }

        /// <summary>The head's level forward and the level direction to the sword side.</summary>
        private static (Vector3 Forward, Vector3 SwordSide) FindHeadingAxes(TrackedPose head, Hand hand)
        {
            var forward = Vector3.Transform(Vector3.UnitZ, head.Rotation);
            forward = new Vector3(forward.X, 0.0f, forward.Z);
            if (forward.LengthSquared() < 1e-6f)
            {
                // Looking straight up or down: the head's up gives the heading instead.
                var up = Vector3.Transform(Vector3.UnitY, head.Rotation);
                forward = new Vector3(up.X, 0.0f, up.Z);
            }

            forward = forward.LengthSquared() < 1e-6f ? Vector3.UnitZ : Vector3.Normalize(forward);

            // Unity's frame: +x is right of +z with +y up.
            var right = new Vector3(forward.Z, 0.0f, -forward.X);
            return (forward, hand == Hand.Left ? -right : right);
        }

        private static float ToRadians(float degrees)
        {
            return degrees * (float)(Math.PI / 180.0);
        }
    }
}
