using UnityEngine;
using VrFencing.Dummy;
using VrFencing.Settings;
using VrFencing.Strip;
using VrFencing.Tracking;

namespace VrFencing.Drills
{
    /// <summary>
    /// Runs the moving Dummy (PER-105) in a Drill that keeps its distance (<see cref="DrillDefinition.KeepsDistance"/>):
    /// every frame it reads where the Fencer's front foot is from the headset, steps the Core
    /// <see cref="DistanceKeeper"/> (the intent: where the Dummy should stand), steps the Dummy there with the Core
    /// <see cref="DummyFootwork"/> (PER-110: advances and retreats on planted feet) and poses the Dummy where the
    /// footwork has it, body and legs, so its Target Zones move with it. The Fencer's front
    /// foot is taken to be <see cref="StartMark.HeadBehindFrontFootPerHeight"/> ahead of the headset, as at the Start Mark.
    /// Logs each reaction with a <c>[Dummy]</c> tag. All rules are in the Core; this only reads, forwards and places.
    /// </summary>
    internal sealed class DistanceKeeperDriver
    {
        private readonly DummyBehaviour _dummy;
        private readonly StripBehaviour _strip;
        private readonly Transform _head;
        private readonly System.Random _random = new System.Random();
        private DistanceKeeper _keeper;
        private DummyFootwork _footwork;
        private DistanceKeeperState _loggedState;

        public DistanceKeeperDriver(DummyBehaviour dummy, StripBehaviour strip, Transform head)
        {
            _dummy = dummy;
            _strip = strip;
            _head = head;
        }

        public bool IsRunning => _keeper != null;

        /// <summary>
        /// Starts keeping the distance the Dummy stands at now (the Drill's Dummy Distance) from the Fencer, assumed on
        /// the En Garde Line until the headset says otherwise.
        /// </summary>
        public void Begin(DummyDifficulty difficulty)
        {
            if (_dummy == null || _dummy.Layout == null)
            {
                Debug.LogWarning("[Dummy] No configured Dummy; it will not keep its distance.");
                return;
            }

            if (_head == null)
            {
                Debug.LogWarning("[Dummy] No headset camera rig; the Dummy will not keep its distance.");
            }

            var gap = _dummy.DistanceMeters;
            var geometry = _strip != null ? _strip.Geometry : null;
            var limits = geometry != null
                ? DummyStripLimits.ForStrip(geometry.RearEndZ, geometry.FarEndZ, _dummy.Layout.HeightMeters)
                : DummyStripLimits.Unlimited;
            _keeper = new DistanceKeeper(gap, gap, limits, difficulty, _random);
            _footwork = new DummyFootwork(_dummy.Layout.HeightMeters, _dummy.Layout.Hand, _keeper.TargetFrontFootZ, _random);
            _loggedState = _keeper.State;
            PoseDummy();
            Debug.Log($"[Dummy] Keeps {gap:0.00} m from the Fencer, {difficulty}; front foot between {limits.NearestFrontFootZ:0.00} and " +
                $"{limits.FarthestFrontFootZ:0.00} m on the Strip.");
        }

        /// <summary>The next reaction comes at <paramref name="difficulty"/>'s delay (one Rep's difficulty).</summary>
        public void SetDifficulty(DummyDifficulty difficulty, int repIndex)
        {
            if (_keeper == null)
            {
                return;
            }

            _keeper.SetDifficulty(difficulty);
            var timing = _keeper.Timing;
            Debug.Log($"[Dummy] Rep {repIndex + 1}: keeps distance at {difficulty}, reaction {timing.DelaySeconds * 1000.0f:0} " +
                $"+/- {timing.JitterSeconds * 1000.0f:0} ms.");
        }

        /// <summary>One frame of the running Drill (not while paused: the Dummy then stays where it is).</summary>
        public void Step(double nowSeconds, float deltaSeconds)
        {
            if (_keeper == null)
            {
                return;
            }

            var fencerFrontFootZ = ReadFencerFrontFootZ();
            _keeper.Step(nowSeconds, fencerFrontFootZ);
            LogStateChange(fencerFrontFootZ);
            _footwork.Step(_keeper.TargetFrontFootZ, deltaSeconds);
            PoseDummy();
        }

        /// <summary>Stops keeping the distance; a Dummy caught mid-step stands back in en garde.</summary>
        public void End()
        {
            if (_footwork != null && _dummy != null && _dummy.Layout != null)
            {
                _dummy.SetLegs(DummyLegs.EnGarde(_dummy.Layout.HeightMeters, _dummy.Layout.Hand));
            }

            _keeper = null;
            _footwork = null;
        }

        private void PoseDummy()
        {
            _dummy.SetDistanceMeters(_footwork.DistanceMeters);
            _dummy.SetLegs(_footwork.Legs);
        }

        /// <summary>Strip-frame z of the Fencer's front foot, from the headset; null without a headset or Strip.</summary>
        private float? ReadFencerFrontFootZ()
        {
            if (_head == null || _strip == null || _strip.EnGardeLine == null)
            {
                return null;
            }

            var enGardeLine = new Pose(_strip.EnGardeLine.position, _strip.EnGardeLine.rotation).ToTrackedPose();
            var headInStrip = StripFrame.ToStripFrame(enGardeLine, _head.position.ToNumerics());
            return headInStrip.Z - StartMark.GetHeadZ(_dummy.Layout.HeightMeters);
        }

        private void LogStateChange(float? fencerFrontFootZ)
        {
            var state = _keeper.State;
            if (state == _loggedState)
            {
                return;
            }

            _loggedState = state;
            var fencer = fencerFrontFootZ.HasValue ? $"{fencerFrontFootZ.Value:0.00} m" : "unknown";
            switch (state)
            {
                case DistanceKeeperState.Reacting:
                    Debug.Log($"[Dummy] Fencer moved (front foot at {fencer}): reacting in {_keeper.ReactionDelaySeconds * 1000.0f:0} ms.");
                    break;
                case DistanceKeeperState.Following:
                    Debug.Log($"[Dummy] Reacted: stepping toward {_keeper.TargetFrontFootZ:0.00} m (Fencer at {fencer}).");
                    break;
                default:
                    var gap = fencerFrontFootZ.HasValue ? $", gap {_keeper.TargetFrontFootZ - fencerFrontFootZ.Value:0.00} m" : string.Empty;
                    Debug.Log($"[Dummy] Holding at {_keeper.TargetFrontFootZ:0.00} m (Fencer at {fencer}{gap}).");
                    break;
            }
        }
    }
}
