using System;
using VrFencing.Drills;
using VrFencing.Dummy;

namespace VrFencing.UI
{
    /// <summary>
    /// Shows a Drill's lit-zone cue on the Dummy: the cued Target Zone lights when a Rep is cued and goes dark when
    /// the Rep is judged, the run pauses or ends; a successful Touch flashes the zone. Named-zone cues are spoken by the
    /// Running panel's text instead. The Drills (PER-38, PER-39) may move or extend this.
    /// </summary>
    internal sealed class DummyCueLights : IDisposable
    {
        private readonly DrillRunner _runner;
        private readonly DummyBehaviour _dummy;

        public DummyCueLights(DrillRunner runner, DummyBehaviour dummy)
        {
            _runner = runner;
            _dummy = dummy;
            _runner.RepCued += OnRepCued;
            _runner.RepJudged += OnRepJudged;
            _runner.StateChanged += OnStateChanged;
            _runner.Paused += _dummy.DimAllZones;
        }

        public void Dispose()
        {
            _runner.RepCued -= OnRepCued;
            _runner.RepJudged -= OnRepJudged;
            _runner.StateChanged -= OnStateChanged;
            _runner.Paused -= _dummy.DimAllZones;
        }

        private void OnRepCued(RepCue cue)
        {
            _dummy.DimAllZones();
            if (cue.CueType == CueType.LitTargetZone)
            {
                _dummy.LightZone(cue.TargetZone);
            }
        }

        private void OnRepJudged(RepResult result)
        {
            if (result.IsSuccess)
            {
                _dummy.FlashZone(result.RequestedZone);
            }
        }

        private void OnStateChanged(DrillState state)
        {
            if (state == DrillState.Judge || state == DrillState.Done)
            {
                _dummy.DimAllZones();
            }
        }
    }
}
