using System;
using System.Collections.Generic;
using VrFencing.Dummy;

namespace VrFencing.Drills
{
    /// <summary>
    /// Runs one Drill as a state machine: Idle, then per Rep Cue, Window, Judge, Rest, and Done after the last Rep.
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

        /// <summary>Raised once per judged Rep. This is the record for the rep log.</summary>
        public event Action<RepResult> RepJudged;

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

        /// <summary>The Target Zone cued for the current Rep.</summary>
        public TargetZone CurrentTargetZone { get; private set; }

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
        /// The blade met the Dummy. Only the first contact inside an open window counts; contacts during the cue,
        /// the rest or a pause are ignored. A contact that arrives after the window ran out is a timeout.
        /// </summary>
        public void ReportContact(BladeContact contact)
        {
            if (!IsRunning || IsPaused)
            {
                return;
            }

            // Settle anything already due, so a late contact is judged against the right window.
            AdvanceDueTransitions();
            if (State != DrillState.Window)
            {
                return;
            }

            var contactAtActiveSeconds = ActiveSeconds();
            var outcome = JudgeContact(contact);
            var result = new RepResult(
                Drill.Id,
                CurrentRepIndex,
                CurrentTargetZone,
                outcome,
                contact.Zone,
                reactionSeconds: contactAtActiveSeconds - _stateStartedAtActiveSeconds,
                contact.TipSpeedMetersPerSecond,
                contact.DistanceFromZoneCenterMeters,
                IsShrunk);

            FinishRep(result, contactAtActiveSeconds);
            AdvanceDueTransitions();
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

        private RepOutcome JudgeContact(BladeContact contact)
        {
            if (contact.Kind == ContactKind.Flat)
            {
                return RepOutcome.FlatContact;
            }

            return contact.Zone == CurrentTargetZone ? RepOutcome.Success : RepOutcome.WrongZone;
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
                        var windowClosesAt = _stateStartedAtActiveSeconds + Drill.WindowSeconds;
                        if (now < windowClosesAt)
                        {
                            return;
                        }

                        FinishRep(TimeoutResult(), windowClosesAt);
                        break;

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

        private RepResult TimeoutResult()
        {
            return new RepResult(
                Drill.Id,
                CurrentRepIndex,
                CurrentTargetZone,
                RepOutcome.Timeout,
                contactZone: null,
                reactionSeconds: null,
                tipSpeedMetersPerSecond: null,
                distanceFromZoneCenterMeters: null,
                IsShrunk);
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
                EnterState(DrillState.Rest, atActiveSeconds);
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
            CurrentTargetZone = Drill.TargetZones[_random.Next(Drill.TargetZones.Count)];
            EnterState(DrillState.Cue, atActiveSeconds);
            RepCued?.Invoke(new RepCue(CurrentRepIndex, CurrentTargetZone, Drill.CueType));
        }

        private void EnterState(DrillState state, double atActiveSeconds)
        {
            State = state;
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
