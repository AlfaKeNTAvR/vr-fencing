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
    /// The picker orders them easy to hard by <see cref="DrillDefinition.Order"/>. The specific Drills register in <see cref="DrillCatalog"/>.
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
    /// The app's Drills (PER-21), easy to hard. Drills 1 to 3 (PER-38) are here; Drills 4 to 6 (PER-39) register in
    /// <see cref="CreateDefault"/> the same way. Development builds also list two test Drills after the real ones.
    ///
    /// Strip lengths are end to end, rear run-back included, as <c>StripGeometry.LengthMeters</c> measures them.
    /// The shortest Strip that can be placed is about 2 m end to end, so a 1 m requirement always fits.
    /// </summary>
    public static class DrillCatalog
    {
        public const string PointControlDrillId = "point-control";
        public const string LungeToTargetDrillId = "lunge-to-target";
        public const string ReactionDrillId = "reaction-lit-target";
        public const string TimingTestDrillId = "test-timing";
        public const string FullPisteTestDrillId = "test-full-piste";

        /// <summary>
        /// Time to Touch a named Target Zone, from the cue. There is no Setting for it (PER-21 sets windows only for
        /// reaction, compound, distance choice and footwork); 3 s leaves time to read the name, extend and, in Drill 2,
        /// lunge, without a long wait after a miss.
        /// </summary>
        public const float NamedZoneWindowSeconds = 3.0f;

        /// <summary>Drill 1 stays at extension distance with no footwork, so it fits the shortest Strip.</summary>
        public const float PointControlStripLengthMeters = 1.0f;

        /// <summary>Drills 2 and 3 at lunge distance: room to lunge about 1 m forward and recover.</summary>
        public const float LungeStripLengthMeters = 2.5f;

        /// <summary>Drill 3's shortened variant stands the Dummy at extension distance, so it needs no lunge room.</summary>
        public const float ReactionShrunkStripLengthMeters = 1.0f;

        /// <summary>
        /// The cue and the window open together in Drills 1 to 3, so the logged reaction time runs from the cue (the
        /// zone named or lit) to the Touch, and the IOBT lunge search, which starts at the cue, sees the whole lunge.
        /// </summary>
        public const float ImmediateWindowCueSeconds = 0.0f;

        /// <summary>Every Target Zone on the Dummy (PER-21).</summary>
        public static readonly IReadOnlyList<TargetZone> AllTargetZones = new[]
        {
            TargetZone.Hand, TargetZone.Wrist, TargetZone.Forearm, TargetZone.UpperArm, TargetZone.Thigh, TargetZone.Foot, TargetZone.Body,
        };

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
            registry.Register(PointControlDrillId, CreatePointControlDrill);
            registry.Register(LungeToTargetDrillId, CreateLungeToTargetDrill);
            registry.Register(ReactionDrillId, CreateReactionDrill);

            // PER-39 registers Drills 4 to 6 here, with Order 4 to 6.
            if (includeTestDrills)
            {
                registry.Register(TimingTestDrillId, CreateTimingTestDrill);
                registry.Register(FullPisteTestDrillId, CreateFullPisteTestDrill);
            }

            return registry;
        }

        /// <summary>
        /// Drill 1, point control: the Dummy stands at extension distance and the app names a Target Zone; the Fencer
        /// extends and Touches it from en garde, with no footwork.
        /// </summary>
        public static DrillDefinition CreatePointControlDrill(TrainerSettings settings)
        {
            return new DrillDefinition(
                PointControlDrillId,
                "Point control",
                order: 1,
                requiredStripLengthMeters: PointControlStripLengthMeters,
                shrunkStripLengthMeters: null,
                repCount: settings.RepsPerDrill,
                cueSeconds: ImmediateWindowCueSeconds,
                windowSeconds: NamedZoneWindowSeconds,
                restSeconds: settings.RestBetweenRepsSeconds,
                CueType.NamedTargetZone,
                SuccessRule.PointFirstTouchOnTargetZone,
                AllTargetZones,
                DummyDistance.Extension);
        }

        /// <summary>
        /// Drill 2, lunge to target: the Dummy stands at lunge distance and the app names a Target Zone; the Fencer
        /// extends, lunges, Touches it, and recovers during the rest.
        /// </summary>
        public static DrillDefinition CreateLungeToTargetDrill(TrainerSettings settings)
        {
            return new DrillDefinition(
                LungeToTargetDrillId,
                "Lunge to target",
                order: 2,
                requiredStripLengthMeters: LungeStripLengthMeters,
                shrunkStripLengthMeters: null,
                repCount: settings.RepsPerDrill,
                cueSeconds: ImmediateWindowCueSeconds,
                windowSeconds: NamedZoneWindowSeconds,
                restSeconds: settings.RestBetweenRepsSeconds,
                CueType.NamedTargetZone,
                SuccessRule.PointFirstTouchOnTargetZone,
                AllTargetZones,
                DummyDistance.Lunge);
        }

        /// <summary>
        /// Drill 3, reaction: a random Target Zone lights for the reaction window (a Setting, 1.5 s by default) and the
        /// Fencer Touches it before it goes dark; the reaction time is logged. The Dummy stands at lunge distance. With
        /// "shrink drills to fit" on a short Strip, the shortened variant stands it at extension distance instead.
        /// </summary>
        public static DrillDefinition CreateReactionDrill(TrainerSettings settings)
        {
            return new DrillDefinition(
                ReactionDrillId,
                "Reaction: lit target",
                order: 3,
                requiredStripLengthMeters: LungeStripLengthMeters,
                shrunkStripLengthMeters: ReactionShrunkStripLengthMeters,
                repCount: settings.RepsPerDrill,
                cueSeconds: ImmediateWindowCueSeconds,
                windowSeconds: settings.TimeWindows.ReactionSeconds,
                restSeconds: settings.RestBetweenRepsSeconds,
                CueType.LitTargetZone,
                SuccessRule.PointFirstTouchOnTargetZone,
                AllTargetZones,
                DummyDistance.Lunge,
                shrunkDummyDistance: DummyDistance.Extension);
        }

        /// <summary>
        /// Runs the Rep timing with the Fencer's Reps, rest and reaction window, lighting the body or the hand.
        /// A Touch on the lit zone succeeds; another zone or a flat contact fails the Rep; no contact times out.
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
