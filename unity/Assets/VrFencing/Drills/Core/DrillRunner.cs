using System;
using System.Collections.Generic;
using VrFencing.Dummy;

namespace VrFencing.Drills
{
    /// <summary>
    /// Runs one Drill as a state machine: Idle, then per Rep Cue, Window, Judge, Return (Drills where the Fencer moves),
    /// Rest, and Done after the last Rep.
    /// Inputs are plain calls (blade contacts, Fencer off and back on the Strip, cancel) and an injected clock,
    /// so it runs headless in tests. Outputs are C# events carrying plain data.
    ///
    /// Timing: every state lasts exactly its configured time measured on the run's active clock, which stops while
    /// paused. Transitions are scheduled from when the previous state was due, not from when <see cref="Tick"/> noticed,
    /// so frame jitter never stretches a window.
    ///
    /// Pausing: leaving the Strip pauses the run, and so does the Fencer's pause button (<see cref="PauseByFencer"/>).
    /// The run resumes once the Fencer is on the Strip and has not paused it. Resuming during Rest continues the rest
    /// where it stopped. Resuming during Cue or Window restarts the interrupted Rep with a fresh cue, because the
    /// Fencer has moved and the old window would no longer be fair; the interrupted Rep is not judged.
    ///
    /// Rules (<see cref="SuccessRule"/>): a Touch on the cued zone; the same plus footwork that suits the Rep's Dummy
    /// Distance (distance choice); two Touches in order, the second within its own window of the first (compound); or,
    /// with no blade, the footwork read in the window answering the call (footwork calls, judged when the window ends);
    /// or a Parry of the Armed Dummy's attack, then a Riposte within 1 s if the Drill requires one (PER-97).
    /// Footwork comes in through <see cref="ReportFootwork"/>, Parries through <see cref="ReportParry"/> and
    /// <see cref="ReportAttackLanded"/>.
    ///
    /// Start Mark (PER-89): in a Drill where the Fencer moves (<see cref="DrillDefinition.WaitsAtStartMark"/>), the run
    /// waits after each judged Rep, in <see cref="DrillState.Return"/>, until the Fencer is back at the Start Mark and
    /// still (<see cref="ReportAtStartMark"/>), with no time limit; the rest starts from then. After
    /// <see cref="ReturnPromptSeconds"/> of waiting it raises <see cref="ReturnPrompted"/> once. With no report at all
    /// (no head tracking), the Fencer counts as back, so the run never waits.
    /// </summary>
    public sealed class DrillRunner
    {
        private readonly IClock _clock;
        private readonly Random _random;
        private readonly List<RepResult> _results = new List<RepResult>();

        private bool _isFencerOnStrip = true;
        private bool _isPausedByFencer;
        private double _runStartedAtSeconds;
        private double _pausedTotalSeconds;
        private double _pauseStartedAtSeconds;

        // On the active clock: seconds since the run started, paused time excluded.
        private double _stateStartedAtActiveSeconds;

        // Compound Reps: when the first Touch landed (active clock), null until it has.
        private double? _firstTouchAtActiveSeconds;

        // Parry Reps: the Parry that a Riposte now answers, and when it landed (active clock); null until then.
        private ParryReport? _parry;
        private double? _parryAtActiveSeconds;

        // Beat Reps (PER-101): the Beat that the Touch now follows, and when it came (active clock); null until then.
        private BeatReport? _beat;
        private double? _beatAtActiveSeconds;

        // Stop Hit Reps (PER-101): the attack is under way (contacts count), and the Stop Hit's contact and time.
        private bool _isAttackUnderWay;
        private BladeContact? _stopHit;
        private double? _stopHitAtActiveSeconds;

        // The latest footwork reading reported since the current cue, for Drills that judge footwork.
        private FootworkReading? _latestFootwork;

        // The Fencer is back at the Start Mark and still, per the latest report, and since when (active clock; negative
        // infinity when it was already so before the run).
        private bool _isFencerAtStartMark = true;
        private double _atStartMarkSinceActiveSeconds = double.NegativeInfinity;

        /// <summary>How long the run waits in <see cref="DrillState.Return"/> before <see cref="ReturnPrompted"/> (PER-89: 2 s).</summary>
        public const double ReturnPromptSeconds = 2.0;

        /// <param name="clock">Source of time; the runner never reads Unity's time itself.</param>
        /// <param name="random">Picks each Rep's Target Zone. Pass a seeded one for reproducible runs.</param>
        public DrillRunner(IClock clock, Random random)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        /// <summary>
        /// A run started: <see cref="Drill"/> and <see cref="IsShrunk"/> are set, and the first Rep is about to be cued.
        /// The Unity layer moves the Dummy to the Drill's Dummy Distance here, before the first cue.
        /// </summary>
        public event Action Started;

        /// <summary>Raised on every state change, including the pass through <see cref="DrillState.Judge"/>.</summary>
        public event Action<DrillState> StateChanged;

        /// <summary>Raised when a Rep is cued, and again when an interrupted Rep restarts after a pause.</summary>
        public event Action<RepCue> RepCued;

        /// <summary>Compound Reps: the first Touch landed, so the second zone is now the one to Touch.</summary>
        public event Action<TargetZone> SecondZoneCued;

        /// <summary>Raised once per judged Rep. This is the record for the rep log.</summary>
        public event Action<RepResult> RepJudged;

        /// <summary>The Fencer has not come back to the Start Mark <see cref="ReturnPromptSeconds"/> after the judgement: ask them to.</summary>
        public event Action ReturnPrompted;

        /// <summary>The Fencer left the Strip; timers are frozen and contacts ignored.</summary>
        public event Action Paused;

        /// <summary>The Fencer is back on the Strip.</summary>
        public event Action Resumed;

        /// <summary>The run reached <see cref="DrillState.Done"/>, after the last Rep or a cancel.</summary>
        public event Action<DrillRunSummary> Finished;

        public DrillState State { get; private set; } = DrillState.Idle;

        public bool IsPaused { get; private set; }

        /// <summary>The Fencer pressed pause; the run stays paused until <see cref="ResumeByFencer"/>, even back on the Strip.</summary>
        public bool IsPausedByFencer => _isPausedByFencer;

        public bool IsRunning => State != DrillState.Idle && State != DrillState.Done;

        /// <summary>The Drill of the current or last run, or null before the first run.</summary>
        public DrillDefinition Drill { get; private set; }

        /// <summary>The current or last run uses the shortened variant.</summary>
        public bool IsShrunk { get; private set; }

        /// <summary>Zero-based index of the Rep being cued or judged.</summary>
        public int CurrentRepIndex { get; private set; }

        /// <summary>The current Rep's cue.</summary>
        public RepCue CurrentCue { get; private set; }

        /// <summary>The Target Zone to Touch now: the cued zone, or a compound's second zone once its first Touch landed; null for a footwork call.</summary>
        public TargetZone? CurrentTargetZone => IsAwaitingSecondTouch ? CurrentCue.SecondTargetZone : CurrentCue.TargetZone;

        /// <summary>A compound Rep's first Touch landed and its second is awaited.</summary>
        public bool IsAwaitingSecondTouch => _firstTouchAtActiveSeconds.HasValue && State == DrillState.Window;

        /// <summary>A Parry Rep's attack was parried and the Riposte is awaited.</summary>
        public bool IsAwaitingRiposte => _parryAtActiveSeconds.HasValue && State == DrillState.Window;

        /// <summary>A Beat Rep's Beat landed and the Touch is awaited.</summary>
        public bool IsAwaitingTouchAfterBeat => _beatAtActiveSeconds.HasValue && State == DrillState.Window;

        /// <summary>The run is waiting for the Fencer at the Start Mark and has asked them to come back (<see cref="ReturnPrompted"/>).</summary>
        public bool IsPromptingReturn { get; private set; }

        /// <summary>Reps judged so far in the current or last run.</summary>
        public IReadOnlyList<RepResult> Results => _results;

        /// <summary>
        /// Starts <paramref name="drill"/> if it fits the Strip, and returns the availability either way.
        /// An unavailable Drill leaves the runner untouched. If the Fencer is off the Strip, the run starts paused.
        /// </summary>
        /// <exception cref="InvalidOperationException">A run is already in progress; cancel it first.</exception>
        public DrillAvailability Start(DrillDefinition drill, float stripLengthMeters, bool shrinkDrillsToFit)
        {
            if (drill == null)
            {
                throw new ArgumentNullException(nameof(drill));
            }

            if (IsRunning)
            {
                throw new InvalidOperationException("A Drill is already running. Cancel it before starting another.");
            }

            var availability = DrillAvailability.Check(drill, stripLengthMeters, shrinkDrillsToFit);
            if (!availability.IsAvailable)
            {
                return availability;
            }

            Drill = drill;
            IsShrunk = availability.IsShrunk;
            _results.Clear();
            _runStartedAtSeconds = _clock.NowSeconds;
            _pausedTotalSeconds = 0.0;
            _atStartMarkSinceActiveSeconds = double.NegativeInfinity;
            IsPaused = false;
            _isPausedByFencer = false;
            Started?.Invoke();

            EnterCue(atActiveSeconds: 0.0);
            if (!_isFencerOnStrip)
            {
                Pause();
            }

            AdvanceDueTransitions();
            return availability;
        }

        /// <summary>Call every frame. Moves the run on when a cue, window or rest has run out.</summary>
        public void Tick()
        {
            if (!IsRunning || IsPaused)
            {
                return;
            }

            AdvanceDueTransitions();
        }

        /// <summary>
        /// The blade met the Dummy. Only the first contact inside an open window counts (in a compound, the first after
        /// the first Touch too); contacts during the cue, the rest or a pause are ignored, and so is every contact in a
        /// footwork-call Drill. A contact that arrives after the window ran out is a timeout.
        /// </summary>
        public void ReportContact(BladeContact contact)
        {
            if (!IsRunning || IsPaused || Drill.SuccessRule == SuccessRule.FootworkCall)
            {
                return;
            }

            // Settle anything already due, so a late contact is judged against the right window.
            AdvanceDueTransitions();
            if (State != DrillState.Window)
            {
                return;
            }

            if (Drill.SuccessRule == SuccessRule.Parry && !_parryAtActiveSeconds.HasValue)
            {
                // Before the Parry, the Fencer's blade may brush the attacking arm: only the Riposte is judged.
                return;
            }

            if (Drill.SuccessRule == SuccessRule.StopHit)
            {
                ReportStopHitContact(contact);
                return;
            }

            var contactAtActiveSeconds = ActiveSeconds();
            if (Drill.SuccessRule == SuccessRule.BeatThenTouch && !_beatAtActiveSeconds.HasValue)
            {
                // The Fencer's blade sweeping across the Dummy while beating is not an attack; a point-first Touch is.
                if (contact.Kind == ContactKind.PointFirst)
                {
                    FinishRep(CreateResult(RepOutcome.NoBeat, contact, contactAtActiveSeconds - _stateStartedAtActiveSeconds, null), contactAtActiveSeconds);
                    AdvanceDueTransitions();
                }

                return;
            }

            var outcome = JudgeContact(contact);
            if (Drill.SuccessRule == SuccessRule.TwoTouchesInOrder && !_firstTouchAtActiveSeconds.HasValue && outcome == RepOutcome.Success)
            {
                // The first Touch of a compound: the Rep goes on, with the second zone to Touch in its own window.
                _firstTouchAtActiveSeconds = contactAtActiveSeconds;
                SecondZoneCued?.Invoke(CurrentCue.SecondTargetZone.Value);
                AdvanceDueTransitions();
                return;
            }

            var result = CreateResult(
                outcome,
                contact,
                reactionSeconds: (_parryAtActiveSeconds ?? _beatAtActiveSeconds ?? _firstTouchAtActiveSeconds ?? contactAtActiveSeconds) - _stateStartedAtActiveSeconds,
                secondTouchSeconds: contactAtActiveSeconds - _firstTouchAtActiveSeconds,
                riposteSeconds: contactAtActiveSeconds - _parryAtActiveSeconds,
                touchAfterBeatSeconds: contactAtActiveSeconds - _beatAtActiveSeconds);

            FinishRep(result, contactAtActiveSeconds);
            AdvanceDueTransitions();
        }

        /// <summary>
        /// Beat Drills (PER-101): a contact knocked the Armed Dummy's blade out of line (the Blade's Beat judge). The first
        /// one inside an open window opens <see cref="DrillDefinition.BeatTouchWindowSeconds"/> for a Touch. Ignored in
        /// other Drills, after the first, during the cue, the rest or a pause.
        /// </summary>
        public void ReportBeat(BeatReport beat)
        {
            if (!IsRunning || IsPaused || Drill.SuccessRule != SuccessRule.BeatThenTouch)
            {
                return;
            }

            AdvanceDueTransitions();
            if (State != DrillState.Window || _beatAtActiveSeconds.HasValue)
            {
                return;
            }

            _beat = beat;
            _beatAtActiveSeconds = ActiveSeconds();
            AdvanceDueTransitions();
        }

        /// <summary>
        /// Stop Hit Drills (PER-101): the Armed Dummy's attack has started (its wind-up), so a Touch on its arm is now a
        /// Stop Hit. Contacts before it are ignored, as a Touch while the Dummy only stands en garde is no Stop Hit.
        /// </summary>
        public void ReportAttackUnderWay()
        {
            if (!IsRunning || IsPaused || Drill.SuccessRule != SuccessRule.StopHit || State != DrillState.Window)
            {
                return;
            }

            _isAttackUnderWay = true;
        }

        /// <summary>
        /// Parry Drills: a contact deflected the Armed Dummy's point off the Fencer's body (the Blade's Parry judge).
        /// Only the first one inside an open window counts. If it fails the Parry rule the Rep fails; otherwise the Rep
        /// succeeds now, or, when the Drill requires a Riposte, waits <see cref="DrillDefinition.RiposteWindowSeconds"/>
        /// for a Touch on the cued zone. Ignored in other Drills, during the cue, the rest or a pause.
        /// </summary>
        public void ReportParry(ParryReport parry)
        {
            if (!IsRunning || IsPaused || Drill.SuccessRule != SuccessRule.Parry)
            {
                return;
            }

            AdvanceDueTransitions();
            if (State != DrillState.Window || _parryAtActiveSeconds.HasValue)
            {
                return;
            }

            var parryAtActiveSeconds = ActiveSeconds();
            _parry = parry;
            var reactionSeconds = parryAtActiveSeconds - _stateStartedAtActiveSeconds;
            if (!parry.CountsAsParry || !Drill.RequiresRiposte)
            {
                var outcome = parry.CountsAsParry ? RepOutcome.Success : RepOutcome.NotForteOnFoible;
                FinishRep(CreateResult(outcome, contact: null, reactionSeconds, secondTouchSeconds: null), parryAtActiveSeconds);
                AdvanceDueTransitions();
                return;
            }

            _parryAtActiveSeconds = parryAtActiveSeconds;
            AdvanceDueTransitions();
        }

        /// <summary>
        /// Parry Drills: the Armed Dummy's attack got through with no Parry (the Blade's Parry judge saw no deflecting
        /// contact): its point landed on the Fencer (<paramref name="hit"/>, PER-98), or, with no hit, it ended its hold
        /// without reaching them. Fails the open Rep as <see cref="RepOutcome.NotParried"/>, with the hit; ignored once it
        /// was parried, and outside an open window.
        /// </summary>
        public void ReportAttackLanded(FencerHit? hit = null)
        {
            if (!IsRunning || IsPaused || (Drill.SuccessRule != SuccessRule.Parry && Drill.SuccessRule != SuccessRule.StopHit))
            {
                return;
            }

            AdvanceDueTransitions();
            if (Drill.SuccessRule == SuccessRule.StopHit)
            {
                JudgeStopHitLanding(hit);
                return;
            }

            if (State != DrillState.Window || _parryAtActiveSeconds.HasValue)
            {
                return;
            }

            var landedAtActiveSeconds = ActiveSeconds();
            FinishRep(
                CreateResult(RepOutcome.NotParried, contact: null, reactionSeconds: null, secondTouchSeconds: null, fencerHit: hit),
                landedAtActiveSeconds);
            AdvanceDueTransitions();
        }

        /// <summary>
        /// The Fencer's footwork since the current cue, read from the head (Tracking module). Kept while a Rep of a
        /// Drill that judges footwork is cued or open; the latest one counts when the Rep is judged. Ignored otherwise.
        /// </summary>
        public void ReportFootwork(FootworkReading reading)
        {
            if (!IsRunning || IsPaused || !Drill.UsesFootwork || (State != DrillState.Cue && State != DrillState.Window))
            {
                return;
            }

            _latestFootwork = reading;
        }

        /// <summary>
        /// Whether the Fencer is back at the Start Mark and still (Tracking module), reported every frame while a Drill
        /// that waits for it runs. In <see cref="DrillState.Return"/>, the rest starts as soon as this turns true.
        /// </summary>
        public void ReportAtStartMark(bool isBackAndStill)
        {
            if (isBackAndStill == _isFencerAtStartMark)
            {
                return;
            }

            _isFencerAtStartMark = isBackAndStill;
            _atStartMarkSinceActiveSeconds = IsRunning ? ActiveSeconds() : double.NegativeInfinity;
            if (IsRunning && !IsPaused)
            {
                AdvanceDueTransitions();
            }
        }

        /// <summary>The Fencer's head passed a Strip end or side. Pauses a running Drill.</summary>
        public void ReportLeftStrip()
        {
            _isFencerOnStrip = false;
            if (IsRunning && !IsPaused)
            {
                Pause();
            }
        }

        /// <summary>The Fencer is back on the Strip. Resumes a paused Drill unless the Fencer paused it (see the class summary for what resumes how).</summary>
        public void ReportBackOnStrip()
        {
            _isFencerOnStrip = true;
            ResumeIfClear();
        }

        /// <summary>The Fencer's pause button. Pauses a running Drill until <see cref="ResumeByFencer"/>.</summary>
        public void PauseByFencer()
        {
            if (!IsRunning || _isPausedByFencer)
            {
                return;
            }

            _isPausedByFencer = true;
            if (!IsPaused)
            {
                Pause();
            }
        }

        /// <summary>The Fencer's resume button. The run resumes now if the Fencer is on the Strip, otherwise on return.</summary>
        public void ResumeByFencer()
        {
            if (!_isPausedByFencer)
            {
                return;
            }

            _isPausedByFencer = false;
            ResumeIfClear();
        }

        /// <summary>Stops the run. Reps judged so far are kept in the summary; the Rep in progress is dropped.</summary>
        public void Cancel()
        {
            if (!IsRunning)
            {
                return;
            }

            IsPaused = false;
            _isPausedByFencer = false;
            FinishRun(wasCancelled: true, atActiveSeconds: ActiveSeconds());
        }

        /// <summary>Resumes a paused run once nothing holds it: the Fencer is on the Strip and has not paused it.</summary>
        private void ResumeIfClear()
        {
            if (!IsRunning || !IsPaused || !_isFencerOnStrip || _isPausedByFencer)
            {
                return;
            }

            _pausedTotalSeconds += _clock.NowSeconds - _pauseStartedAtSeconds;
            IsPaused = false;
            Resumed?.Invoke();

            if (State == DrillState.Cue || State == DrillState.Window)
            {
                EnterCue(ActiveSeconds());
            }

            AdvanceDueTransitions();
        }

        /// <summary>
        /// Stop Hit Drills: a contact once the attack is under way. A point-first Touch on the arm (the Drill's zones) is
        /// the Stop Hit, judged a success once the double window passes with no landing; another zone or a flat contact
        /// fails at once. Contacts before the attack and after the Stop Hit are ignored.
        /// </summary>
        private void ReportStopHitContact(BladeContact contact)
        {
            if (!_isAttackUnderWay || _stopHitAtActiveSeconds.HasValue)
            {
                return;
            }

            var contactAtActiveSeconds = ActiveSeconds();
            var outcome = JudgeContact(contact);
            if (outcome == RepOutcome.Success)
            {
                _stopHit = contact;
                _stopHitAtActiveSeconds = contactAtActiveSeconds;
                AdvanceDueTransitions();
                return;
            }

            FinishRep(CreateResult(outcome, contact, contactAtActiveSeconds - _stateStartedAtActiveSeconds, null), contactAtActiveSeconds);
            AdvanceDueTransitions();
        }

        /// <summary>
        /// Stop Hit Drills: the attack landed on the Fencer (<paramref name="hit"/>) or ended without reaching them. Before
        /// any Stop Hit a landing fails as <see cref="RepOutcome.HitFirst"/>, an attack that ended short as
        /// <see cref="RepOutcome.NoStopHit"/>; a landing within the double window after the Stop Hit is a
        /// <see cref="RepOutcome.Double"/>.
        /// </summary>
        private void JudgeStopHitLanding(FencerHit? hit)
        {
            if (State != DrillState.Window)
            {
                return;
            }

            var landedAtActiveSeconds = ActiveSeconds();
            if (_stopHitAtActiveSeconds.HasValue)
            {
                if (hit.HasValue)
                {
                    FinishRep(StopHitResult(RepOutcome.Double, hit), landedAtActiveSeconds);
                    AdvanceDueTransitions();
                }

                return;
            }

            var outcome = hit.HasValue ? RepOutcome.HitFirst : RepOutcome.NoStopHit;
            FinishRep(CreateResult(outcome, contact: null, reactionSeconds: null, secondTouchSeconds: null, fencerHit: hit), landedAtActiveSeconds);
            AdvanceDueTransitions();
        }

        private RepResult StopHitResult(RepOutcome outcome, FencerHit? hit)
        {
            return CreateResult(outcome, _stopHit, _stopHitAtActiveSeconds - _stateStartedAtActiveSeconds, secondTouchSeconds: null, fencerHit: hit);
        }

        private RepOutcome JudgeContact(BladeContact contact)
        {
            if (contact.Kind == ContactKind.Flat)
            {
                return RepOutcome.FlatContact;
            }

            var isOnTarget = Drill.TouchesAnyDrillZone
                ? ContainsZone(Drill.TargetZones, contact.Zone)
                : contact.Zone == CurrentTargetZone;
            if (!isOnTarget)
            {
                return RepOutcome.WrongZone;
            }

            // With no footwork reported (no head tracking), the Touch alone decides.
            var isWrongFootwork = Drill.SuccessRule == SuccessRule.TouchWithFootworkForDistance
                && _latestFootwork.HasValue
                && !FootworkJudging.SuitsDistance(CurrentCue.DummyDistance, _latestFootwork.Value.Action);
            return isWrongFootwork ? RepOutcome.WrongFootwork : RepOutcome.Success;
        }

        private static bool ContainsZone(IReadOnlyList<TargetZone> zones, TargetZone zone)
        {
            foreach (var candidate in zones)
            {
                if (candidate == zone)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>A footwork call when its window ends: the latest reading must answer the call; no movement is a timeout.</summary>
        private RepResult JudgeFootworkCall()
        {
            var footwork = _latestFootwork;
            RepOutcome outcome;
            if (!footwork.HasValue || footwork.Value.Action == FootworkAction.None)
            {
                outcome = RepOutcome.Timeout;
            }
            else
            {
                outcome = FootworkJudging.AnswersCall(CurrentCue.FootworkCall.Value, footwork.Value.Action) ? RepOutcome.Success : RepOutcome.WrongFootwork;
            }

            return CreateResult(outcome, contact: null, reactionSeconds: footwork?.OnsetSecondsAfterCue, secondTouchSeconds: null);
        }

        private RepResult CreateResult(
            RepOutcome outcome,
            BladeContact? contact,
            double? reactionSeconds,
            double? secondTouchSeconds,
            double? riposteSeconds = null,
            FencerHit? fencerHit = null,
            double? touchAfterBeatSeconds = null)
        {
            return new RepResult(
                Drill.Id,
                CurrentRepIndex,
                CurrentCue.TargetZone,
                outcome,
                contact?.Zone,
                reactionSeconds,
                contact?.TipSpeedMetersPerSecond,
                contact?.DistanceFromZoneCenterMeters,
                IsShrunk,
                CurrentCue.SecondTargetZone,
                secondTouchSeconds,
                CurrentCue.FootworkCall,
                Drill.UsesFootwork ? _latestFootwork : null,
                CurrentCue.DummyDistance,
                _parry?.Line,
                riposteSeconds,
                fencerHit,
                CurrentCue.AttackTarget,
                CurrentCue.AttackSpeed,
                touchAfterBeatSeconds,
                _beat?.OutOfLineMeters,
                CurrentCue.DummyDifficulty);
        }

        private void AdvanceDueTransitions()
        {
            // Loops so that one long frame (or zero-length cue and rest) passes through several states in order.
            while (IsRunning && !IsPaused)
            {
                var now = ActiveSeconds();
                switch (State)
                {
                    case DrillState.Cue:
                        var windowOpensAt = _stateStartedAtActiveSeconds + Drill.CueSeconds;
                        if (now < windowOpensAt)
                        {
                            return;
                        }

                        EnterState(DrillState.Window, windowOpensAt);
                        break;

                    case DrillState.Window:
                        var windowClosesAt = GetWindowClosesAt();
                        if (now < windowClosesAt)
                        {
                            return;
                        }

                        FinishRep(JudgeWindowEnd(), windowClosesAt);
                        break;

                    case DrillState.Return:
                        if (_isFencerAtStartMark)
                        {
                            EnterState(DrillState.Rest, Math.Max(_stateStartedAtActiveSeconds, _atStartMarkSinceActiveSeconds));
                            break;
                        }

                        if (!IsPromptingReturn && now >= _stateStartedAtActiveSeconds + ReturnPromptSeconds)
                        {
                            IsPromptingReturn = true;
                            ReturnPrompted?.Invoke();
                        }

                        return;

                    case DrillState.Rest:
                        var restEndsAt = _stateStartedAtActiveSeconds + Drill.RestSeconds;
                        if (now < restEndsAt)
                        {
                            return;
                        }

                        EnterCue(restEndsAt);
                        break;

                    default:
                        return;
                }
            }
        }

        /// <summary>
        /// When the open window ends (active clock): the Riposte's from the Parry, the Touch's from the Beat, the double
        /// window from the Stop Hit, the compound's second Touch from the first, else the Rep's own.
        /// </summary>
        private double GetWindowClosesAt()
        {
            if (_parryAtActiveSeconds.HasValue)
            {
                return _parryAtActiveSeconds.Value + DrillDefinition.RiposteWindowSeconds;
            }

            if (_beatAtActiveSeconds.HasValue)
            {
                return _beatAtActiveSeconds.Value + DrillDefinition.BeatTouchWindowSeconds;
            }

            if (_stopHitAtActiveSeconds.HasValue)
            {
                return _stopHitAtActiveSeconds.Value + DrillDefinition.StopHitDoubleWindowSeconds;
            }

            if (_firstTouchAtActiveSeconds.HasValue)
            {
                return _firstTouchAtActiveSeconds.Value + Drill.SecondTouchWindowSeconds;
            }

            return _stateStartedAtActiveSeconds + Drill.WindowSeconds;
        }

        /// <summary>The Rep when its window ends with nothing more: a footwork call is judged, a Stop Hit that saw no double succeeds, else a timeout.</summary>
        private RepResult JudgeWindowEnd()
        {
            if (Drill.SuccessRule == SuccessRule.FootworkCall)
            {
                return JudgeFootworkCall();
            }

            return _stopHitAtActiveSeconds.HasValue ? StopHitResult(RepOutcome.Success, hit: null) : TimeoutResult();
        }

        /// <summary>
        /// No (further) contact in time. A compound whose first Touch landed keeps that Touch's reaction time; a Parry
        /// with no Riposte after it is <see cref="RepOutcome.NoRiposte"/> and keeps the Parry's.
        /// </summary>
        private RepResult TimeoutResult()
        {
            var outcome = _parryAtActiveSeconds.HasValue
                ? RepOutcome.NoRiposte
                : _beatAtActiveSeconds.HasValue ? RepOutcome.NoTouchAfterBeat : RepOutcome.Timeout;
            return CreateResult(
                outcome,
                contact: null,
                reactionSeconds: (_parryAtActiveSeconds ?? _beatAtActiveSeconds ?? _firstTouchAtActiveSeconds) - _stateStartedAtActiveSeconds,
                secondTouchSeconds: null);
        }

        private void FinishRep(RepResult result, double atActiveSeconds)
        {
            EnterState(DrillState.Judge, atActiveSeconds);
            _results.Add(result);
            RepJudged?.Invoke(result);

            if (State != DrillState.Judge)
            {
                // A listener cancelled the run.
                return;
            }

            if (_results.Count >= Drill.RepCount)
            {
                FinishRun(wasCancelled: false, atActiveSeconds);
            }
            else
            {
                // Already back (or nothing reports the Start Mark): straight to the rest, as in a Drill with no wait.
                var waitsForFencer = Drill.WaitsAtStartMark(IsShrunk) && !_isFencerAtStartMark;
                EnterState(waitsForFencer ? DrillState.Return : DrillState.Rest, atActiveSeconds);
            }
        }

        private void FinishRun(bool wasCancelled, double atActiveSeconds)
        {
            EnterState(DrillState.Done, atActiveSeconds);
            Finished?.Invoke(new DrillRunSummary(Drill, _results.ToArray(), wasCancelled, IsShrunk));
        }

        private void EnterCue(double atActiveSeconds)
        {
            CurrentRepIndex = _results.Count;
            _firstTouchAtActiveSeconds = null;
            _parry = null;
            _parryAtActiveSeconds = null;
            _beat = null;
            _beatAtActiveSeconds = null;
            _isAttackUnderWay = false;
            _stopHit = null;
            _stopHitAtActiveSeconds = null;
            _latestFootwork = null;
            CurrentCue = PickCue();
            EnterState(DrillState.Cue, atActiveSeconds);
            RepCued?.Invoke(CurrentCue);
        }

        /// <summary>The next Rep's cue, picked at random from what the Drill offers (see <see cref="DrillDefinition"/>).</summary>
        private RepCue PickCue()
        {
            var distance = Drill.RepDummyDistances != null
                ? Drill.RepDummyDistances[_random.Next(Drill.RepDummyDistances.Count)]
                : Drill.GetDummyDistance(IsShrunk);
            var difficulty = Drill.GetDummyDifficulty(CurrentRepIndex);

            switch (Drill.SuccessRule)
            {
                case SuccessRule.FootworkCall:
                    var calls = Drill.FootworkCalls;
                    return new RepCue(
                        CurrentRepIndex, targetZone: null, Drill.CueType, distance, footworkCall: calls[_random.Next(calls.Count)], dummyDifficulty: difficulty);

                case SuccessRule.TwoTouchesInOrder:
                    var pair = Drill.ZonePairs[_random.Next(Drill.ZonePairs.Count)];
                    return new RepCue(CurrentRepIndex, pair.First, Drill.CueType, distance, secondTargetZone: pair.Second, dummyDifficulty: difficulty);

                case SuccessRule.Parry:
                case SuccessRule.StopHit:
                    var attacks = Drill.Attacks;
                    var riposteZone = Drill.TouchesAnyDrillZone
                        ? (TargetZone?)null
                        : Drill.TargetZones[_random.Next(Drill.TargetZones.Count)];
                    return new RepCue(
                        CurrentRepIndex,
                        riposteZone,
                        Drill.CueType,
                        distance,
                        attackTarget: attacks.DrawTarget(_random),
                        attackSpeed: attacks.GetSpeed(CurrentRepIndex));

                case SuccessRule.BeatThenTouch:
                    return new RepCue(CurrentRepIndex, targetZone: null, Drill.CueType, distance);

                default:
                    return new RepCue(
                        CurrentRepIndex, Drill.TargetZones[_random.Next(Drill.TargetZones.Count)], Drill.CueType, distance, dummyDifficulty: difficulty);
            }
        }

        private void EnterState(DrillState state, double atActiveSeconds)
        {
            State = state;
            IsPromptingReturn = false;
            _stateStartedAtActiveSeconds = atActiveSeconds;
            StateChanged?.Invoke(state);
        }

        private void Pause()
        {
            IsPaused = true;
            _pauseStartedAtSeconds = _clock.NowSeconds;
            Paused?.Invoke();
        }

        private double ActiveSeconds()
        {
            var now = IsPaused ? _pauseStartedAtSeconds : _clock.NowSeconds;
            return now - _runStartedAtSeconds - _pausedTotalSeconds;
        }
    }
}
