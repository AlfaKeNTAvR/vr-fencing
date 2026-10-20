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
    /// The app's Drills (PER-21), easy to hard: Drills 1 to 3 (PER-38), 4 to 6 (PER-39), then the Armed Dummy's Parry
    /// Drills 7 to 9 (PER-100) and its Beat, Stop Hit and lunge Drills 10 to 12 (PER-101). Development builds also
    /// list three test Drills after the real ones: one that never fits, to show a greyed-out row, the Armed Dummy
    /// feel test (PER-95), and the moving Dummy's distance keeper (PER-105).
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
        public const string DistanceKeeperTestDrillId = "test-distance-keeper";
        public const string FootworkTestDrillId = "test-dummy-footwork";
        public const string SlowParryDrillId = "parry-six-four-slow";
        public const string FastParryDrillId = "parry-six-four-fast";
        public const string LowParryDrillId = "parry-eight-seven";
        public const string BeatDrillId = "beat-then-thrust";
        public const string StopHitDrillId = "stop-hit-arm";
        public const string LungeParryDrillId = "dummy-lunges";

        /// <summary>A Stop Hit lands on the Armed Dummy's arm (PER-23: "Touch the hand or arm").</summary>
        public static readonly IReadOnlyList<TargetZone> StopHitZones = new[]
        {
            TargetZone.Hand, TargetZone.Wrist, TargetZone.Forearm, TargetZone.UpperArm,
        };

        /// <summary>
        /// Drill 11 attacks the body and thigh, never the arm, so the Dummy stands at extension distance and its arm is in
        /// reach for the Stop Hit.
        /// </summary>
        public static readonly IReadOnlyList<AttackTarget> StopHitAttackTargets = new[] { AttackTarget.Chest, AttackTarget.Thigh };

        /// <summary>Drill 11: slow and medium in turn, so the arm is there to hit; a real-speed thrust leaves almost no time.</summary>
        public static readonly IReadOnlyList<AttackSpeed> StopHitSpeeds = new[] { AttackSpeed.Slow, AttackSpeed.Medium };

        /// <summary>Drill 12: the lunge goes at the chest or the thigh, so it is parried in 6/4 or 8/7.</summary>
        public static readonly IReadOnlyList<AttackTarget> LungeAttackTargets = new[] { AttackTarget.Chest, AttackTarget.Thigh };

        /// <summary>
        /// Drill 12: medium and real in turn. The speed is the point's over the floor, lunge included; a slow lunge over
        /// about 1.5 m would take longer than the Rep's window.
        /// </summary>
        public static readonly IReadOnlyList<AttackSpeed> LungeSpeeds = new[] { AttackSpeed.Medium, AttackSpeed.Real };

        /// <summary>
        /// Drill 10: time from the cue to the Beat. The Fencer comes on guard against the blade in line, then beats; long
        /// enough to set up, short enough not to wait after a miss.
        /// </summary>
        public const float BeatWindowSeconds = 5.0f;

        /// <summary>
        /// Parry 6 and 4 close the high line (PER-23): the Armed Dummy thrusts at the hand, forearm or chest. Hand and
        /// forearm thrusts come from further back (<see cref="AttackAim.GetStandBackMeters"/>).
        /// </summary>
        public static readonly IReadOnlyList<AttackTarget> HighLineAttackTargets = new[]
        {
            AttackTarget.Hand, AttackTarget.Forearm, AttackTarget.Chest,
        };

        /// <summary>Parry 8 and 7 close the low line: the Armed Dummy thrusts at the thigh.</summary>
        public static readonly IReadOnlyList<AttackTarget> LowLineAttackTargets = new[] { AttackTarget.Thigh };

        /// <summary>Drill 7 (PER-23): slow thrusts only, to learn the parries.</summary>
        public static readonly IReadOnlyList<AttackSpeed> SlowParrySpeeds = new[] { AttackSpeed.Slow };

        /// <summary>Drill 8 (PER-23: "the same at medium and real speed"): medium and real in turn.</summary>
        public static readonly IReadOnlyList<AttackSpeed> FastParrySpeeds = new[] { AttackSpeed.Medium, AttackSpeed.Real };

        /// <summary>
        /// Drill 9: PER-23 sets no speed for the low parries, which are new to a beginner; slow and medium in turn, so each
        /// is learnt slow and then tried at speed.
        /// </summary>
        public static readonly IReadOnlyList<AttackSpeed> LowParrySpeeds = new[] { AttackSpeed.Slow, AttackSpeed.Medium };

        /// <summary>
        /// The window of a Rep with the Armed Dummy (PER-95, PER-100): its random delay (up to 3 s), the wind-up, the
        /// slowest thrust (the slow speed over the longest path, hand and forearm thrusts included) and its hold all fit
        /// inside, so an attack always ends (parried, landed or short) before the window does. The Riposte then gets its
        /// own 1 s from the Parry.
        /// </summary>
        public const float ArmedDummyWindowSeconds = 8.0f;

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

        public const string DistanceKeeperTestTaskLine = "Test: Dummy keeps distance. Easy, Medium, Hard.";

        /// <summary>
        /// The distance keeper's feel test (PER-105): its difficulties in turn by Rep, so one run compares all three.
        /// </summary>
        public static readonly IReadOnlyList<DummyDifficulty> DistanceKeeperTestDifficulties = new[]
        {
            DummyDifficulty.Easy, DummyDifficulty.Medium, DummyDifficulty.Hard,
        };

        /// <summary>
        /// The distance keeper's feel test: time from the cue to the Touch. Longer than a named zone's 3 s, as the Dummy
        /// keeps its distance and the Fencer has to work in to lunge distance and beat its reaction.
        /// </summary>
        public const float DistanceKeeperTestWindowSeconds = 6.0f;

        /// <summary>
        /// The distance keeper's feel test: Drill 2's lunge room plus about one advance and one retreat each way, so
        /// the Dummy has room to back away before it reaches the far end (as Drills 5 and 6).
        /// </summary>
        public const float DistanceKeeperTestStripLengthMeters = 3.0f;

        public const string SlowParryTaskLine = "Parry 6 or 4, then riposte within 1 s.";

        public const string SlowParryOnlyTaskLine = "Parry 6 or 4 slow thrusts. No riposte.";

        public const string FastParryTaskLine = "Parry 6 or 4 at speed, then riposte within 1 s.";

        public const string FastParryOnlyTaskLine = "Parry 6 or 4 fast thrusts. No riposte.";

        public const string LowParryTaskLine = "Parry 8 or 7 the low thrust, then riposte in 1 s.";

        public const string LowParryOnlyTaskLine = "Parry 8 or 7 the low thrust. No riposte.";

        public const string BeatTaskLine = "Beat the blade aside, then Touch within 1 s.";

        public const string StopHitTaskLine = "Touch the arm before the thrust lands.";

        public const string FootworkTestTaskLine = "Watch the Dummy's footwork. Touch the lit zone.";

        public const string LungeParryTaskLine = "Parry the Dummy's lunge, then riposte in 1 s.";

        public const string LungeParryOnlyTaskLine = "Parry the Dummy's lunge. No riposte.";

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
            registry.Register(SlowParryDrillId, CreateSlowParryDrill);
            registry.Register(FastParryDrillId, CreateFastParryDrill);
            registry.Register(LowParryDrillId, CreateLowParryDrill);
            registry.Register(BeatDrillId, CreateBeatDrill);
            registry.Register(StopHitDrillId, CreateStopHitDrill);
            registry.Register(LungeParryDrillId, CreateLungeParryDrill);
            if (includeTestDrills)
            {
                registry.Register(FullPisteTestDrillId, CreateFullPisteTestDrill);
                registry.Register(ArmedDummyTestDrillId, CreateArmedDummyTestDrill);
                registry.Register(DistanceKeeperTestDrillId, CreateDistanceKeeperTestDrill);
                registry.Register(FootworkTestDrillId, CreateFootworkTestDrill);
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

        /// <summary>
        /// Drill 7, Parry 6/4 + Riposte, slow (PER-100): on each Rep the Armed Dummy thrusts at the hand, forearm or chest
        /// at the slow speed after a random 1 to 3 s delay; the Fencer parries it (6 or 4) and, with the Drill's Riposte
        /// switch on (a Setting), Touches the Dummy anywhere within 1 s. A hit on the Fencer, no Parry or no Riposte in
        /// time fails the Rep.
        /// </summary>
        public static DrillDefinition CreateSlowParryDrill(TrainerSettings settings)
        {
            var riposte = settings.ArmedDummy.RiposteInSlowParries;
            return CreateParryDrill(
                settings, SlowParryDrillId, "Parry 6/4: slow", order: 7, new AttackScript(HighLineAttackTargets, SlowParrySpeeds), riposte,
                riposte ? SlowParryTaskLine : SlowParryOnlyTaskLine);
        }

        /// <summary>Drill 8, Parry 6/4 + Riposte at medium and real speed, in turn (PER-100); otherwise as Drill 7.</summary>
        public static DrillDefinition CreateFastParryDrill(TrainerSettings settings)
        {
            var riposte = settings.ArmedDummy.RiposteInFastParries;
            return CreateParryDrill(
                settings, FastParryDrillId, "Parry 6/4: medium, real", order: 8, new AttackScript(HighLineAttackTargets, FastParrySpeeds),
                riposte, riposte ? FastParryTaskLine : FastParryOnlyTaskLine);
        }

        /// <summary>Drill 9, Parry 8/7 + Riposte (PER-100): thigh thrusts, slow and medium in turn; otherwise as Drill 7.</summary>
        public static DrillDefinition CreateLowParryDrill(TrainerSettings settings)
        {
            var riposte = settings.ArmedDummy.RiposteInLowParries;
            return CreateParryDrill(
                settings, LowParryDrillId, "Parry 8/7", order: 9, new AttackScript(LowLineAttackTargets, LowParrySpeeds), riposte,
                riposte ? LowParryTaskLine : LowParryOnlyTaskLine);
        }

        /// <summary>
        /// Drill 10, Beat then thrust (PER-101): the Armed Dummy stands at lunge distance holding its blade in line. The
        /// Fencer beats it out of line, then lunges and Touches the Dummy anywhere within 1 s of the Beat. A Touch before the
        /// Beat, no Beat in time, or no Touch within 1 s fails the Rep.
        /// </summary>
        public static DrillDefinition CreateBeatDrill(TrainerSettings settings)
        {
            return new DrillDefinition(
                BeatDrillId,
                "Beat, then thrust",
                order: 10,
                requiredStripLengthMeters: LungeStripLengthMeters,
                shrunkStripLengthMeters: null,
                repCount: settings.RepsPerDrill,
                cueSeconds: ImmediateWindowCueSeconds,
                windowSeconds: BeatWindowSeconds,
                restSeconds: settings.RestBetweenRepsSeconds,
                CueType.BladeInLine,
                SuccessRule.BeatThenTouch,
                AllTargetZones,
                DummyDistance.Lunge,
                taskLine: BeatTaskLine);
        }

        /// <summary>
        /// Drill 11, Stop Hit to the arm (PER-101): the Armed Dummy, at extension distance, thrusts at the chest or thigh;
        /// the Fencer Touches its hand or arm while the attack is on the way. Its point landing first, or within the 50 ms
        /// double window after the Stop Hit, fails the Rep.
        /// </summary>
        public static DrillDefinition CreateStopHitDrill(TrainerSettings settings)
        {
            return new DrillDefinition(
                StopHitDrillId,
                "Stop hit to the arm",
                order: 11,
                requiredStripLengthMeters: PointControlStripLengthMeters,
                shrunkStripLengthMeters: null,
                repCount: settings.RepsPerDrill,
                cueSeconds: ImmediateWindowCueSeconds,
                windowSeconds: ArmedDummyWindowSeconds,
                restSeconds: settings.RestBetweenRepsSeconds,
                CueType.ArmedDummyAttack,
                SuccessRule.StopHit,
                StopHitZones,
                DummyDistance.Extension,
                taskLine: StopHitTaskLine,
                attacks: new AttackScript(StopHitAttackTargets, StopHitSpeeds));
        }

        /// <summary>
        /// Drill 12, the Dummy lunges (PER-101): from lunge distance the Armed Dummy lunges (its feet move) with a thrust at
        /// the chest or thigh; the Fencer parries and, with the Drill's Riposte switch on, ripostes within 1 s.
        /// </summary>
        public static DrillDefinition CreateLungeParryDrill(TrainerSettings settings)
        {
            var riposte = settings.ArmedDummy.RiposteInLungeParries;
            return new DrillDefinition(
                LungeParryDrillId,
                "Dummy lunges",
                order: 12,
                requiredStripLengthMeters: PointControlStripLengthMeters,
                shrunkStripLengthMeters: null,
                repCount: settings.RepsPerDrill,
                cueSeconds: ImmediateWindowCueSeconds,
                windowSeconds: ArmedDummyWindowSeconds,
                restSeconds: settings.RestBetweenRepsSeconds,
                CueType.ArmedDummyAttack,
                SuccessRule.Parry,
                AllTargetZones,
                DummyDistance.Lunge,
                taskLine: riposte ? LungeParryTaskLine : LungeParryOnlyTaskLine,
                requiresRiposte: riposte,
                attacks: new AttackScript(LungeAttackTargets, LungeSpeeds, lunges: true));
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
        /// blade and, on each Rep's cue, makes one scripted thrust after a random delay, at any of the four targets. Its
        /// speed cycles slow, medium, real by Rep (the Blade module's Armed Dummy runs it). The Fencer must Parry it
        /// (PER-97: deflect its point off the body) and Riposte with a Touch on the body within 1 s.
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
                windowSeconds: ArmedDummyWindowSeconds,
                restSeconds: settings.RestBetweenRepsSeconds,
                CueType.NamedTargetZone,
                SuccessRule.Parry,
                new[] { TargetZone.Body },
                DummyDistance.Extension,
                taskLine: ArmedDummyTestTaskLine,
                requiresRiposte: true,
                attacks: AttackScript.EveryTargetAndSpeed);
        }

        /// <summary>
        /// The moving Dummy's feel test (PER-105, development builds only): Drill 2 with a Dummy that keeps lunge
        /// distance. It slides along the Strip as the Fencer advances and retreats, one reaction delay late (Easy, Medium
        /// and Hard in turn by Rep), and stops at the Strip's far end. The named zone is also lit, to find it on a moving
        /// Dummy; a point-first Touch on it within the window is the success.
        /// </summary>
        public static DrillDefinition CreateDistanceKeeperTestDrill(TrainerSettings settings)
        {
            return new DrillDefinition(
                DistanceKeeperTestDrillId,
                "Test: Dummy keeps distance",
                TestDrillOrder + 2,
                requiredStripLengthMeters: DistanceKeeperTestStripLengthMeters,
                shrunkStripLengthMeters: null,
                repCount: settings.RepsPerDrill,
                cueSeconds: ImmediateWindowCueSeconds,
                windowSeconds: DistanceKeeperTestWindowSeconds,
                restSeconds: settings.RestBetweenRepsSeconds,
                CueType.NamedTargetZone,
                SuccessRule.PointFirstTouchOnTargetZone,
                AllTargetZones,
                DummyDistance.Lunge,
                taskLine: DistanceKeeperTestTaskLine,
                lightsNamedZone: true,
                dummyDifficulties: DistanceKeeperTestDifficulties);
        }

        /// <summary>
        /// The Dummy's footwork feel test (PER-111, development builds only): the Dummy stands at step-lunge distance and
        /// runs through every footwork move it has, over and over (<see cref="FootworkShowcase"/>): advances, retreats, a
        /// lunge and recovery, a fleche, a side step and a diagonal (PER-118), and breathing, weight shift and side sway
        /// in between. Each Rep names and lights a zone, as
        /// the distance keeper's test, so a Touch on the moving Dummy can be tried too.
        /// </summary>
        public static DrillDefinition CreateFootworkTestDrill(TrainerSettings settings)
        {
            return new DrillDefinition(
                FootworkTestDrillId,
                "Test: Dummy footwork",
                TestDrillOrder + 3,
                requiredStripLengthMeters: DistanceKeeperTestStripLengthMeters,
                shrunkStripLengthMeters: null,
                repCount: settings.RepsPerDrill,
                cueSeconds: ImmediateWindowCueSeconds,
                windowSeconds: DistanceKeeperTestWindowSeconds,
                restSeconds: settings.RestBetweenRepsSeconds,
                CueType.NamedTargetZone,
                SuccessRule.PointFirstTouchOnTargetZone,
                AllTargetZones,
                DummyDistance.StepLunge,
                taskLine: FootworkTestTaskLine,
                lightsNamedZone: true,
                showsFootwork: true);
        }

        /// <summary>
        /// A Parry Drill of the Armed Dummy (PER-100): the Dummy stands at extension distance (further back for a thrust at
        /// the arm), so the Fencer parries and ripostes from en garde without a step, on the shortest Strip. The attack is
        /// the cue; the Riposte may land on any Target Zone.
        /// </summary>
        private static DrillDefinition CreateParryDrill(
            TrainerSettings settings, string id, string name, int order, AttackScript attacks, bool requiresRiposte, string taskLine)
        {
            return new DrillDefinition(
                id,
                name,
                order,
                requiredStripLengthMeters: PointControlStripLengthMeters,
                shrunkStripLengthMeters: null,
                repCount: settings.RepsPerDrill,
                cueSeconds: ImmediateWindowCueSeconds,
                windowSeconds: ArmedDummyWindowSeconds,
                restSeconds: settings.RestBetweenRepsSeconds,
                CueType.ArmedDummyAttack,
                SuccessRule.Parry,
                AllTargetZones,
                DummyDistance.Extension,
                taskLine: taskLine,
                requiresRiposte: requiresRiposte,
                attacks: attacks);
        }
    }
}
