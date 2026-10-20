using UnityEngine;
using VrFencing.Dummy;

namespace VrFencing.Drills
{
    /// <summary>
    /// Runs the footwork feel test (PER-111, <see cref="DrillDefinition.ShowsFootwork"/>): every frame of the running
    /// Drill it steps the Core <see cref="FootworkShowcase"/> on the Dummy's own footwork and poses the Dummy where the
    /// footwork has it, so its Target Zones move with it. Logs each move with a <c>[Dummy]</c> tag. All rules are in
    /// the Core; this only forwards and places.
    /// </summary>
    internal sealed class FootworkShowcaseDriver
    {
        private readonly DummyBehaviour _dummy;
        private FootworkShowcase _showcase;

        public FootworkShowcaseDriver(DummyBehaviour dummy)
        {
            _dummy = dummy;
        }

        /// <summary>Starts the script from where the Dummy stands now (the Drill's Dummy Distance).</summary>
        public void Begin()
        {
            if (_dummy == null || _dummy.Footwork == null)
            {
                Debug.LogWarning("[Dummy] No configured Dummy; it will not show its footwork.");
                return;
            }

            _showcase = new FootworkShowcase(_dummy.Footwork, _dummy.Layout.HeightMeters);
            _showcase.MoveStarted += move => Debug.Log($"[Dummy] Footwork test: {move} (at {_dummy.Footwork.DistanceMeters:0.00} m).");
            Debug.Log($"[Dummy] Footwork test from {_dummy.Footwork.DistanceMeters:0.00} m; fleche {_dummy.Footwork.FlecheLengthMeters:0.00} m.");
        }

        /// <summary>One frame of the running Drill (not while paused: the Dummy then holds its pose).</summary>
        public void Step(float deltaSeconds)
        {
            if (_showcase == null)
            {
                return;
            }

            _showcase.Step(deltaSeconds);
            _dummy.ApplyFootwork();
        }

        /// <summary>Stops the script; the run's end places the Dummy back in en garde.</summary>
        public void End()
        {
            _showcase = null;
        }
    }
}
