using UnityEngine;

namespace VrFencing.Drills
{
    /// <summary>
    /// Scene host for the Core <see cref="DrillRunner"/>: ticks it every frame with Unity's game time.
    /// Other components subscribe to <see cref="Runner"/>'s events and forward their inputs to it
    /// (the Blade's contacts, the Strip's off-strip and back-on signals, the UI's start and cancel).
    /// All rules live in the Core; this only supplies time.
    /// </summary>
    public sealed class DrillRunnerBehaviour : MonoBehaviour
    {
        private sealed class UnityTimeClock : IClock
        {
            public double NowSeconds => Time.timeAsDouble;
        }

        public DrillRunner Runner { get; private set; }

        private void Awake()
        {
            Runner = new DrillRunner(new UnityTimeClock(), new System.Random());
        }

        private void Update()
        {
            Runner.Tick();
        }

        private void OnDisable()
        {
            Runner?.Cancel();
        }
    }
}
