using System;
using System.Collections.Generic;
using System.Linq;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Drills
{
    /// <summary>
    /// The Drills the picker offers. Each Drill is registered as a builder, because its Rep count, rest and window come
    /// from the current <see cref="TrainerSettings"/>; <see cref="BuildAll"/> builds them for the settings in force.
    /// The picker orders them easy to hard by <see cref="DrillDefinition.Order"/>. The specific Drills (PER-38, PER-39) register in <see cref="DrillCatalog"/>.
    /// </summary>
    public sealed class DrillRegistry
    {
        private readonly List<KeyValuePair<string, Func<TrainerSettings, DrillDefinition>>> _builders =
            new List<KeyValuePair<string, Func<TrainerSettings, DrillDefinition>>>();

        public int Count => _builders.Count;

        /// <summary>Adds a Drill. <paramref name="id"/> must match the built Drill's id.</summary>
        /// <exception cref="ArgumentException">The id is empty or already registered.</exception>
        public void Register(string id, Func<TrainerSettings, DrillDefinition> build)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A Drill needs an id.", nameof(id));
            }

            if (build == null)
            {
                throw new ArgumentNullException(nameof(build));
            }

            if (_builders.Any(entry => entry.Key == id))
            {
                throw new ArgumentException($"A Drill with id '{id}' is already registered.", nameof(id));
            }

            _builders.Add(new KeyValuePair<string, Func<TrainerSettings, DrillDefinition>>(id, build));
        }

        /// <summary>Every Drill built for <paramref name="settings"/>, in registration order.</summary>
        /// <exception cref="InvalidOperationException">A builder returned nothing, or a Drill whose id differs from its registration.</exception>
        public IReadOnlyList<DrillDefinition> BuildAll(TrainerSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            var drills = new List<DrillDefinition>();
            foreach (var entry in _builders)
            {
                var drill = entry.Value(settings);
                if (drill == null || drill.Id != entry.Key)
                {
                    throw new InvalidOperationException($"The builder registered as '{entry.Key}' built '{drill?.Id ?? "nothing"}'.");
                }

                drills.Add(drill);
            }

            return drills;
        }
    }

    /// <summary>
    /// The app's Drills. Drills 1 to 6 (PER-38, PER-39) are added in <see cref="CreateDefault"/>. Until they exist,
    /// development builds also list two test Drills so the menus, pause and results can be tried on the headset.
    /// </summary>
    public static class DrillCatalog
    {
        public const string TimingTestDrillId = "test-timing";
        public const string FullPisteTestDrillId = "test-full-piste";

        /// <summary>Test Drills sort after every real Drill.</summary>
        private const int TestDrillOrder = 1000;

        /// <summary>Cue time of the test Drills: long enough to see the cue before the window opens.</summary>
        private const float TestCueSeconds = 1.0f;

        /// <summary>Fits a Strip placed about 1 m long: the rear run-back adds about 1 m end to end.</summary>
        private const float TimingTestStripLengthMeters = 2.0f;

        /// <param name="includeTestDrills">Adds the test Drills; pass true only in development builds.</param>
        public static DrillRegistry CreateDefault(bool includeTestDrills)
        {
            var registry = new DrillRegistry();

            // PER-38 and PER-39 register Drills 1 to 6 here, each with its Order (1 = easiest).
            if (includeTestDrills)
            {
                registry.Register(TimingTestDrillId, CreateTimingTestDrill);
                registry.Register(FullPisteTestDrillId, CreateFullPisteTestDrill);
            }

            return registry;
        }

        /// <summary>
        /// Runs the Rep timing with the Fencer's Reps, rest and reaction window, lighting the body or the hand.
        /// Until the Blade exists nothing can Touch, so every Rep times out.
        /// </summary>
        public static DrillDefinition CreateTimingTestDrill(TrainerSettings settings)
        {
            return new DrillDefinition(
                TimingTestDrillId,
                "Test: Rep timing",
                TestDrillOrder,
                requiredStripLengthMeters: TimingTestStripLengthMeters,
                shrunkStripLengthMeters: null,
                repCount: settings.RepsPerDrill,
                cueSeconds: TestCueSeconds,
                windowSeconds: settings.TimeWindows.ReactionSeconds,
                restSeconds: settings.RestBetweenRepsSeconds,
                CueType.LitTargetZone,
                SuccessRule.PointFirstTouchOnTargetZone,
                new[] { TargetZone.Body, TargetZone.Hand });
        }

        /// <summary>Needs a full 14 m piste, so the picker shows a greyed-out Drill with its required length.</summary>
        public static DrillDefinition CreateFullPisteTestDrill(TrainerSettings settings)
        {
            return new DrillDefinition(
                FullPisteTestDrillId,
                "Test: needs a full piste",
                TestDrillOrder + 1,
                requiredStripLengthMeters: 14.0f,
                shrunkStripLengthMeters: null,
                repCount: settings.RepsPerDrill,
                cueSeconds: TestCueSeconds,
                windowSeconds: settings.TimeWindows.ReactionSeconds,
                restSeconds: settings.RestBetweenRepsSeconds,
                CueType.NamedTargetZone,
                SuccessRule.PointFirstTouchOnTargetZone,
                new[] { TargetZone.Body });
        }
    }
}
