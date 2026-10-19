// Test doubles for the Drill runner (PER-34): a hand-driven clock, a standard test Drill,
// and a recorder that captures every event the runner raises.
using System;
using System.Collections.Generic;
using VrFencing.Drills;
using VrFencing.Dummy;

namespace VrFencing.Tests.Drills
{
    internal sealed class FakeClock : IClock
    {
        public double NowSeconds { get; private set; } = 100.0;

        public void Advance(double seconds)
        {
            NowSeconds += seconds;
        }
    }

    internal static class TestDrills
    {
        public const float RequiredLength = 2.5f;
        public const float ShrunkLength = 1.5f;
        public const float CueSeconds = 0.5f;
        public const float WindowSeconds = 1.5f;
        public const float RestSeconds = 2.0f;

        public static DrillDefinition Create(
            int repCount = 3,
            float cueSeconds = CueSeconds,
            float restSeconds = RestSeconds,
            float? shrunkLength = ShrunkLength,
            TargetZone[] zones = null)
        {
            return new DrillDefinition(
                id: "lunge-to-target",
                name: "Lunge to target",
                order: 2,
                requiredStripLengthMeters: RequiredLength,
                shrunkStripLengthMeters: shrunkLength,
                repCount: repCount,
                cueSeconds: cueSeconds,
                windowSeconds: WindowSeconds,
                restSeconds: restSeconds,
                cueType: CueType.NamedTargetZone,
                successRule: SuccessRule.PointFirstTouchOnTargetZone,
                targetZones: zones ?? new[] { TargetZone.Thigh });
        }

        public static BladeContact PointOn(TargetZone zone)
        {
            return new BladeContact(zone, ContactKind.PointFirst, tipSpeedMetersPerSecond: 3.0f, distanceFromZoneCenterMeters: 0.02f);
        }

        public static BladeContact FlatOn(TargetZone zone)
        {
            return new BladeContact(zone, ContactKind.Flat, tipSpeedMetersPerSecond: 2.0f, distanceFromZoneCenterMeters: 0.05f);
        }
    }

    internal sealed class RunnerRecorder
    {
        public RunnerRecorder(DrillRunner runner)
        {
            runner.StateChanged += state => States.Add(state);
            runner.RepCued += cue => Cues.Add(cue);
            runner.RepJudged += result => Judged.Add(result);
            runner.Paused += () => Events.Add("Paused");
            runner.Resumed += () => Events.Add("Resumed");
            runner.Finished += summary => Summaries.Add(summary);
        }

        public List<DrillState> States { get; } = new List<DrillState>();

        public List<RepCue> Cues { get; } = new List<RepCue>();

        public List<RepResult> Judged { get; } = new List<RepResult>();

        public List<string> Events { get; } = new List<string>();

        public List<DrillRunSummary> Summaries { get; } = new List<DrillRunSummary>();
    }
}
