using System;

namespace VrFencing.Dummy
{
    /// <summary>
    /// Carries the whole Dummy along the Strip to a target position as one rigid body, with no leg motion (PER-105):
    /// it speeds up and slows down at <see cref="DistanceKeeperTuning.GlideAccelerationMetersPerSecondSquared"/>, never
    /// faster than <see cref="DistanceKeeperTuning.GlideTopSpeedMetersPerSecond"/>, and stops on the target without
    /// overshooting it. The target is an intent such as <see cref="DistanceKeeper.TargetFrontFootZ"/>; stepping legs
    /// (PER-110) will later carry the body to the same target instead.
    /// Positions are the Dummy's front foot along the Strip (Strip-frame z).
    /// </summary>
    public sealed class DummyGlide
    {
        // Closer and slower than this, the glide is on the target.
        private const float ArrivedMeters = 0.001f;

        /// <exception cref="ArgumentOutOfRangeException">The start is not a finite number.</exception>
        public DummyGlide(float startFrontFootZ)
        {
            if (float.IsNaN(startFrontFootZ) || float.IsInfinity(startFrontFootZ))
            {
                throw new ArgumentOutOfRangeException(nameof(startFrontFootZ), startFrontFootZ, "Must be a finite number of meters.");
            }

            FrontFootZ = startFrontFootZ;
        }

        /// <summary>Where the Dummy's front foot is now.</summary>
        public float FrontFootZ { get; private set; }

        /// <summary>Along the Strip: positive away from the Fencer (a retreat), negative toward them (an advance).</summary>
        public float VelocityMetersPerSecond { get; private set; }

        /// <summary>Moves toward <paramref name="targetFrontFootZ"/> for <paramref name="deltaSeconds"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The target is not a finite number.</exception>
        public void Step(float targetFrontFootZ, float deltaSeconds)
        {
            if (float.IsNaN(targetFrontFootZ) || float.IsInfinity(targetFrontFootZ))
            {
                throw new ArgumentOutOfRangeException(nameof(targetFrontFootZ), targetFrontFootZ, "Must be a finite number of meters.");
            }

            if (!(deltaSeconds > 0.0f))
            {
                return;
            }

            const float acceleration = DistanceKeeperTuning.GlideAccelerationMetersPerSecondSquared;
            var error = targetFrontFootZ - FrontFootZ;
            var maximumSpeedChange = acceleration * deltaSeconds;
            if (Math.Abs(error) <= ArrivedMeters && Math.Abs(VelocityMetersPerSecond) <= maximumSpeedChange)
            {
                FrontFootZ = targetFrontFootZ;
                VelocityMetersPerSecond = 0.0f;
                return;
            }

            // The fastest speed from which it can still stop on the target, frame by frame, capped at the top speed:
            // braking from m speed steps (each acceleration * deltaSeconds) covers m (m + 1) / 2 steps of distance.
            var distanceStep = acceleration * deltaSeconds * deltaSeconds;
            var brakingSteps = (Math.Sqrt(1.0 + (8.0 * Math.Abs(error) / distanceStep)) - 1.0) * 0.5;
            var stoppingSpeed = (float)(brakingSteps * maximumSpeedChange);
            var wantedVelocity = Math.Sign(error) * Math.Min(DistanceKeeperTuning.GlideTopSpeedMetersPerSecond, stoppingSpeed);
            var speedChange = Math.Min(Math.Max(wantedVelocity - VelocityMetersPerSecond, -maximumSpeedChange), maximumSpeedChange);
            VelocityMetersPerSecond += speedChange;

            var next = FrontFootZ + (VelocityMetersPerSecond * deltaSeconds);
            if ((targetFrontFootZ - next) * error <= 0.0f)
            {
                // It would pass the target this frame: stop on it.
                FrontFootZ = targetFrontFootZ;
                VelocityMetersPerSecond = 0.0f;
                return;
            }

            FrontFootZ = next;
        }
    }
}
