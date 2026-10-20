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
    /// The app's Drills (PER-21), easy to hard: Drills 1 to 3 (PER-38) and 4 to 6 (PER-39). Development builds also
    /// list two test Drills after the real ones: one that never fits, to show a greyed-out row, and the Armed Dummy
    /// feel test (PER-95).
    ///
    /// Strip lengths are end to end, rear run-back included, as <c>StripGeometry.LengthMeters</c> measures them.
    /// The shortest Strip that can be placed is about 2 m end to end, so a 1 m requirement always fits.
    /// </summary>
    public static class DrillCatalog
    {
        public const string PointControlDrillId = "point-control";
        public const string LungeToTargetDrillId = "lunge-to-target";
        public const string ReactionDrillId = "reaction-lit-target";
        public const string DistanceChoiceDrillId = "distance-choice";
        public const string CompoundDrillId = "compound-sequence";
        public const string FootworkCallsDrillId = "footwork-calls";
        public const string FullPisteTestDrillId = "test-full-piste";
        public const string ArmedDummyTestDrillId = "test-armed-dummy";

        /// <summary>
        /// Drill 4 at step-lunge distance: an advance (0.2 of the height) and a lunge (0.6) forward of the En Garde Line
        /// plus the run-back behind it (PER-21: about 3.5 m).
        /// </summary>
        public const float DistanceChoiceStripLengthMeters = 3.5f;

        /// <summary>Drills 5 and 6 (PER-21: about 3 m): a lunge, or footwork kept within two steps of the En Garde Line.</summary>
        public const float CompoundStripLengthMeters = 3.0f;

        /// <inheritdoc cref="CompoundStripLengthMeters"/>
        public const float FootworkCallsStripLengthMeters = 3.0f;

        /// <summary>Drill 5's shortened variant stands the Dummy at extension distance, so it needs no lunge room.</summary>
        public const float CompoundShrunkStripLengthMeters = 1.0f;

        /// <summary>
        /// Time from a compound's cue to its first Touch. PER-21 sets only the second Touch's window; this gives as long
        /// as a named zone, since the Fencer reads two zones before starting.
        /// </summary>
        public const float CompoundFirstTouchWindowSeconds = NamedZoneWindowSeconds;

        /// <summary>The Dummy Distances distance choice picks from, one at random per Rep.</summary>
        public static readonly IReadOnlyList<DummyDistance> DistanceChoiceDistances = new[]
        {
            DummyDistance.Extension, DummyDistance.Lunge, DummyDistance.StepLunge,
        };

        /// <summary>
        /// Distance choice cues the body only: the presets are measured so the point just reaches the body, so the
        /// distance names the action only for the body; nearer zones (the hand) take less.
        /// </summary>
        public static readonly IReadOnlyList<TargetZone> DistanceChoiceZones = new[] { TargetZone.Body };

        /// <summary>
        /// Compound pairs (PER-21: "hand then thigh, foot then arm, and so on"): each moves the point between the high
        /// and the low line or between the arm and the body, so the second Touch needs a real change of line.
        /// </summary>
        public static readonly IReadOnlyList<ZonePair> CompoundZonePairs = new[]
        {
            new ZonePair(TargetZone.Hand, TargetZone.Thigh),
            new ZonePair(TargetZone.Foot, TargetZone.UpperArm),
            new ZonePair(TargetZone.Wrist, TargetZone.Body),
            new ZonePair(TargetZone.Forearm, TargetZone.Foot),
            new ZonePair(TargetZone.Thigh, TargetZone.Hand),
            new ZonePair(TargetZone.UpperArm, TargetZone.Thigh),
            new ZonePair(TargetZone.Hand, TargetZone.Body),
            new ZonePair(TargetZone.Foot, TargetZone.Wrist),
        };

        /// <summary>The calls of Drill 6 (PER-21).</summary>
        public static readonly IReadOnlyList<FootworkAction> FootworkCalls = new[]
        {
            FootworkAction.Advance, FootworkAction.Retreat, FootworkAction.Lunge, FootworkAction.Check,
        };

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

        /// <summary>
        /// Task lines (PER-92) stay this short so each fits on one line under the Drill's name in the picker.
        /// </summary>
        public const int MaxTaskLineLength = 52;

        /// <summary>Drill 1's task line, with the named zone only named.</summary>
        public const string PointControlTaskLine = "Extend to Touch the named zone. No step.";

        /// <summary>Drill 1's task line when the named zone also lights.</summary>
        public const string PointControlLitTaskLine = "Extend to Touch the named, lit zone. No step.";

        /// <summary>Drill 2's task line, with the named zone only named.</summary>
        public const string LungeToTargetTaskLine = "Lunge to Touch the named zone, then recover.";

        /// <summary>Drill 2's task line when the named zone also lights.</summary>
        public const string LungeToTargetLitTaskLine = "Lunge to Touch the named, lit zone, then recover.";

        public const string ReactionTaskLine = "Touch the zone as it lights, before it goes dark.";

        public const string DistanceChoiceTaskLine = "Extend, lunge or step-lunge to Touch the lit body.";

        public const string CompoundTaskLine = "Touch both named zones in order, the second fast.";

        public const string FootworkCallsTaskLine = "Do the called footwork at once. No blade.";

        public const string FullPisteTestTaskLine = "Test only: shows a Drill that needs a long Strip.";

        public const string ArmedDummyTestTaskLine = "Test: parry the thrust, then riposte to the body.";

        /// <summary>
        /// The Armed Dummy feel test's window (PER-95): its random 1 to 3 s delay, the wind-up, a slow thrust (about 1.5 s)
        /// and its hold, then time to riposte.
        /// </summary>
        public const float ArmedDummyTestWindowSeconds = 8.0f;

        /// <summary>Test Drills sort after every real Drill.</summary>
        private const int TestDrillOrder = 1000;

        /// <summary>Cue time of the test Drills: long enough to see the cue before the window opens.</summary>
        private const float TestCueSeconds = 1.0f;

        /// <param name="includeTestDrills">Adds the test Drills; pass true only in development builds.</param>
        public static DrillRegistry CreateDefault(bool includeTestDrills)
        {
            var registry = new DrillRegistry();
            registry.Register(PointControlDrillId, CreatePointControlDrill);
            registry.Register(LungeToTargetDrillId, CreateLungeToTargetDrill);
            registry.Register(ReactionDrillId, CreateReactionDrill);
            registry.Register(DistanceChoiceDrillId, CreateDistanceChoiceDrill);
            registry.Register(CompoundDrillId, CreateCompoundDrill);
            registry.Register(FootworkCallsDrillId, CreateFootworkCallsDrill);
            if (includeTestDrills)
            {
                registry.Register(FullPisteTestDrillId, CreateFullPisteTestDrill);
                registry.Register(ArmedDummyTestDrillId, CreateArmedDummyTestDrill);
            }

            return registry;
        }

        /// <summary>
        /// Drill 1, point control: the Dummy stands at extension distance and the app names a Target Zone (and, with
        /// "Light named zones" on, lights it); the Fencer extends and Touches it from en garde, with no footwork.
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
                DummyDistance.Extension,
                taskLine: settings.LightNamedZones ? PointControlLitTaskLine : PointControlTaskLine,
                lightsNamedZone: settings.LightNamedZones);
        }

        /// <summary>
        /// Drill 2, lunge to target: the Dummy stands at lunge distance and the app names a Target Zone (and, with
        /// "Light named zones" on, lights it); the Fencer extends, lunges, Touches it, and recovers during the rest.
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
                DummyDistance.Lunge,
                taskLine: settings.LightNamedZones ? LungeToTargetLitTaskLine : LungeToTargetTaskLine,
                lightsNamedZone: settings.LightNamedZones);
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
                shrunkDummyDistance: DummyDistance.Extension,
                taskLine: ReactionTaskLine);
        }

        /// <summary>
        /// Drill 4, distance choice: on each cue the Dummy stands at extension, lunge or step-lunge distance (at random)
        /// and its body lights; the Fencer picks the action and Touches the body within the distance-choice window (a
        /// Setting, 2.0 s). The footwork read from the head up to the Touch must suit the distance
        /// (<see cref="FootworkJudging.SuitsDistance"/>), else the Rep fails as wrong footwork.
        /// </summary>
        public static DrillDefinition CreateDistanceChoiceDrill(TrainerSettings settings)
        {
            return new DrillDefinition(
                DistanceChoiceDrillId,
                "Distance choice",
                order: 4,
                requiredStripLengthMeters: DistanceChoiceStripLengthMeters,
                shrunkStripLengthMeters: null,
                repCount: settings.RepsPerDrill,
                cueSeconds: ImmediateWindowCueSeconds,
                windowSeconds: settings.TimeWindows.DistanceChoiceSeconds,
                restSeconds: settings.RestBetweenRepsSeconds,
                CueType.LitTargetZone,
                SuccessRule.TouchWithFootworkForDistance,
                DistanceChoiceZones,
                DummyDistance.Lunge,
                repDummyDistances: DistanceChoiceDistances,
                taskLine: DistanceChoiceTaskLine);
        }

        /// <summary>
        /// Drill 5, compound sequence: the first zone of a pair lights (the label above the Dummy names both, in order);
        /// once it is Touched the second lights, and the second Touch must land within the compound window (a Setting,
        /// 1.0 s) of the first. The Dummy stands at lunge distance; the shortened variant stands it at extension distance.
        /// </summary>
        public static DrillDefinition CreateCompoundDrill(TrainerSettings settings)
        {
            return new DrillDefinition(
                CompoundDrillId,
                "Compound sequence",
                order: 5,
                requiredStripLengthMeters: CompoundStripLengthMeters,
                shrunkStripLengthMeters: CompoundShrunkStripLengthMeters,
                repCount: settings.RepsPerDrill,
                cueSeconds: ImmediateWindowCueSeconds,
                windowSeconds: CompoundFirstTouchWindowSeconds,
                restSeconds: settings.RestBetweenRepsSeconds,
                CueType.LitTargetZone,
                SuccessRule.TwoTouchesInOrder,
                targetZones: null,
                DummyDistance.Lunge,
                shrunkDummyDistance: DummyDistance.Extension,
                zonePairs: CompoundZonePairs,
                secondTouchWindowSeconds: settings.TimeWindows.CompoundSecondTouchSeconds,
                taskLine: CompoundTaskLine);
        }

        /// <summary>
        /// Drill 6, footwork calls: the app calls advance, retreat, lunge or check above the Dummy, and the footwork read
        /// from the head in the footwork-call window (a Setting, 2.0 s) must answer it. No blade. The Dummy stands at
        /// step-lunge distance, out of the way of two advances and a lunge.
        /// </summary>
        public static DrillDefinition CreateFootworkCallsDrill(TrainerSettings settings)
        {
            return new DrillDefinition(
                FootworkCallsDrillId,
                "Footwork calls",
                order: 6,
                requiredStripLengthMeters: FootworkCallsStripLengthMeters,
                shrunkStripLengthMeters: null,
                repCount: settings.RepsPerDrill,
                cueSeconds: ImmediateWindowCueSeconds,
                windowSeconds: settings.TimeWindows.FootworkCallSeconds,
                restSeconds: settings.RestBetweenRepsSeconds,
                CueType.FootworkCall,
                SuccessRule.FootworkCall,
                targetZones: null,
                DummyDistance.StepLunge,
                footworkCalls: FootworkCalls,
                taskLine: FootworkCallsTaskLine);
        }

        /// <summary>Needs a full 14 m piste, so the picker shows a greyed-out Drill with its required length.</summary>
        public static DrillDefinition CreateFullPisteTestDrill(TrainerSettings settings)
        {
            return new DrillDefinition(
                FullPisteTestDrillId,
                "Test: needs a full piste",
                TestDrillOrder,
                requiredStripLengthMeters: 14.0f,
                shrunkStripLengthMeters: null,
                repCount: settings.RepsPerDrill,
                cueSeconds: TestCueSeconds,
                windowSeconds: settings.TimeWindows.ReactionSeconds,
                restSeconds: settings.RestBetweenRepsSeconds,
                CueType.NamedTargetZone,
                SuccessRule.PointFirstTouchOnTargetZone,
                new[] { TargetZone.Body },
                taskLine: FullPisteTestTaskLine);
        }

        /// <summary>
        /// The Armed Dummy feel test (PER-95, development builds only): the Dummy stands at extension distance holding a
        /// blade and, on each Rep's cue, makes one scripted thrust after a random delay. Its speed cycles slow, medium,
        /// real by Rep (the Blade module's Armed Dummy runs it). The Fencer must Parry it (PER-97: deflect its point off
        /// the body) and Riposte with a Touch on the body within 1 s.
        /// </summary>
        public static DrillDefinition CreateArmedDummyTestDrill(TrainerSettings settings)
        {
            return new DrillDefinition(
                ArmedDummyTestDrillId,
                "Test: Armed Dummy parry feel",
                TestDrillOrder + 1,
                requiredStripLengthMeters: PointControlStripLengthMeters,
                shrunkStripLengthMeters: null,
                repCount: settings.RepsPerDrill,
                cueSeconds: ImmediateWindowCueSeconds,
                windowSeconds: ArmedDummyTestWindowSeconds,
                restSeconds: settings.RestBetweenRepsSeconds,
                CueType.NamedTargetZone,
                SuccessRule.Parry,
                new[] { TargetZone.Body },
                DummyDistance.Extension,
                taskLine: ArmedDummyTestTaskLine,
                requiresRiposte: true);
        }
    }
}
