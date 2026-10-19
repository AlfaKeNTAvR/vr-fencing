using System;
using System.Numerics;
using VrFencing.Settings;

namespace VrFencing.Blade
{
    /// <summary>The straight blade for one moment: front of the guard to the point, in one frame (the Strip frame in the app), meters.</summary>
    public readonly struct BladeSegment
    {
        public BladeSegment(Vector3 guardPosition, Vector3 tipPosition)
        {
            GuardPosition = guardPosition;
            TipPosition = tipPosition;
        }

        public Vector3 GuardPosition { get; }

        public Vector3 TipPosition { get; }

        /// <summary>Unit vector from the guard to the point.</summary>
        public Vector3 Direction => Vector3.Normalize(TipPosition - GuardPosition);

        public static BladeSegment Lerp(BladeSegment from, BladeSegment to, float fraction)
        {
            return new BladeSegment(
                Vector3.Lerp(from.GuardPosition, to.GuardPosition, fraction),
                Vector3.Lerp(from.TipPosition, to.TipPosition, fraction));
        }
    }

    /// <summary>One blade pose with its time stamp, as fed to the <see cref="TouchDetector"/>.</summary>
    public readonly struct BladeSample
    {
        public BladeSample(double timeSeconds, BladeSegment blade)
        {
            TimeSeconds = timeSeconds;
            Blade = blade;
        }

        public double TimeSeconds { get; }

        public BladeSegment Blade { get; }
    }

    /// <summary>
    /// Épée dimensions and where the blade sits on the sword-hand controller. The blade runs along +z of its mount
    /// frame, whose origin is the front of the guard.
    /// </summary>
    public static class BladePose
    {
        /// <summary>FIE m.16.3 maximum blade length.</summary>
        public const float BladeLengthMeters = 0.90f;

        /// <summary>FIE m.17.1: the bell guard is at most 13.5 cm across.</summary>
        public const float GuardDiameterMeters = 0.135f;

        /// <summary>FIE m.17.1: the bell is 3 to 5.5 cm deep.</summary>
        public const float GuardDepthMeters = 0.04f;

        /// <summary>The mount for the chosen grip at its angles in use (<see cref="GripGeometry.GetActiveAngles"/>).</summary>
        public static TrackedPose GetMountInController(BladeSettings blade, Hand hand)
        {
            if (blade == null)
            {
                throw new ArgumentNullException(nameof(blade));
            }

            return GetMountInController(blade.Mount, GripGeometry.GetActiveAngles(blade), hand);
        }

        /// <summary>
        /// The mount's pose in the controller anchor's frame for a sword hand (PER-51): turned by pitch (up positive) and
        /// yaw (right positive for the right hand), with the front of the guard
        /// <see cref="GripGeometry.GetGuardForwardMeters"/> out along that turned blade line, so the guard sits on the
        /// blade's line through the thumb web. The left hand mirrors the yaw, since its controller frame is the mirror
        /// of the right one.
        /// </summary>
        public static TrackedPose GetMountInController(BladeMount mount, GripAngles angles, Hand hand)
        {
            var yawDegrees = hand == Hand.Left ? -angles.YawDegrees : angles.YawDegrees;

            // Same convention as Unity's Quaternion.Euler(x, y, 0) = yaw about y after pitch about x. Positive x
            // rotation tips +z down, so pitch up is a negative x rotation.
            var rotation = Quaternion.CreateFromYawPitchRoll(ToRadians(yawDegrees), ToRadians(-angles.PitchDegrees), 0.0f);
            var guard = Vector3.Transform(new Vector3(0.0f, 0.0f, GripGeometry.GetGuardForwardMeters(mount)), rotation);
            return new TrackedPose(guard, rotation);
        }

        /// <summary>
        /// The right-hand pitch and yaw that point the blade along <paramref name="directionInController"/>, a direction
        /// in the sword-hand controller anchor's frame. The inverse of the mount's rotation.
        /// </summary>
        public static GripAngles GetAnglesForDirection(Vector3 directionInController, Hand hand)
        {
            var direction = Vector3.Normalize(directionInController);
            var x = hand == Hand.Left ? -direction.X : direction.X;
            var pitch = Math.Asin(Math.Max(-1.0f, Math.Min(1.0f, direction.Y)));
            var yaw = Math.Atan2(x, direction.Z);
            return new GripAngles((float)(pitch * 180.0 / Math.PI), (float)(yaw * 180.0 / Math.PI));
        }

        /// <summary>The straight blade for a controller pose, in the controller pose's frame.</summary>
        public static BladeSegment GetBlade(TrackedPose controller, TrackedPose mountInController)
        {
            var guard = controller.Position + Vector3.Transform(mountInController.Position, controller.Rotation);
            var bladeRotation = controller.Rotation * mountInController.Rotation;
            var tip = guard + Vector3.Transform(new Vector3(0.0f, 0.0f, BladeLengthMeters), bladeRotation);
            return new BladeSegment(guard, tip);
        }

        private static float ToRadians(float degrees)
        {
            return degrees * (float)(Math.PI / 180.0);
        }
    }
}
