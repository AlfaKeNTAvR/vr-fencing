using System;
using System.Collections.Generic;
using System.IO;
using VrFencing.Drills;

namespace VrFencing.Session
{
    /// <summary>Wall-clock time for log timestamps. The Unity layer reads the system clock; tests set it by hand.</summary>
    public interface IWallClock
    {
        DateTimeOffset UtcNow { get; }
    }

    /// <summary>
    /// Records one session (one app launch): listens to a <see cref="DrillRunner"/>, writes each judged Rep to the
    /// rep log and saves each finished Drill run, with its <see cref="DrillResults"/>, to the session history.
    /// <para>
    /// A judged Rep is held back until the next Rep is cued (or <see cref="Flush"/>), so the flag button pressed
    /// during the rest still marks it. Pressed while no judged Rep is waiting, the flag marks the Rep in progress.
    /// </para>
    /// <para>
    /// File errors never reach the runner: they are raised as <see cref="WriteFailed"/>, and the Drill carries on.
    /// With no <see cref="WriteFailed"/> listener they are thrown.
    /// </para>
    /// </summary>
    public sealed class SessionRecorder
    {
        private readonly IWallClock _clock;
        private readonly Func<ArmPoseSnapshot> _armPoseSource;
        private readonly List<RepLogEntry> _runReps = new List<RepLogEntry>();

        private RepLogEntry _pendingRep;
        private bool _flagNextRep;

        /// <param name="dataFolder">Where the rep log and history live (persistentDataPath on the headset).</param>
        /// <param name="sessionId">Unique per session; joins rep log lines with history records.</param>
        /// <param name="armPoseSource">Samples the IOBT arm pose when a Rep is judged; null (or returning null) logs no pose.</param>
        public SessionRecorder(string dataFolder, string sessionId, IWallClock clock, Func<ArmPoseSnapshot> armPoseSource = null)
        {
            if (string.IsNullOrWhiteSpace(dataFolder))
            {
                throw new ArgumentException("The recorder needs a data folder.", nameof(dataFolder));
            }

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new ArgumentException("The recorder needs a session id.", nameof(sessionId));
            }

            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _armPoseSource = armPoseSource;
            SessionId = sessionId;
            SessionStartedAt = clock.UtcNow;
            RepLogPath = Path.Combine(dataFolder, RepLogFile.FileName);
            HistoryPath = Path.Combine(dataFolder, SessionHistoryFile.FileName);
            PairedPracticePath = Path.Combine(dataFolder, PairedPracticeLogFile.FileName);
        }

        /// <summary>A Drill run finished and was saved to the history (raised even if saving failed).</summary>
        public event Action<DrillRunRecord> DrillRunRecorded;

        /// <summary>Writing the rep log, the history or the Paired Practice log failed (IOException or UnauthorizedAccessException).</summary>
        public event Action<Exception> WriteFailed;

        public string SessionId { get; }

        public DateTimeOffset SessionStartedAt { get; }

        public string RepLogPath { get; }

        public string HistoryPath { get; }

        /// <summary>The Paired Practice log (PER-83), beside the history.</summary>
        public string PairedPracticePath { get; }

        public void Attach(DrillRunner runner)
        {
            runner.RepCued += OnRepCued;
            runner.RepJudged += OnRepJudged;
            runner.Finished += OnFinished;
        }

        public void Detach(DrillRunner runner)
        {
            runner.RepCued -= OnRepCued;
            runner.RepJudged -= OnRepJudged;
            runner.Finished -= OnFinished;
        }

        /// <summary>The flag button: marks the Rep just judged if it is still waiting, otherwise the Rep in progress.</summary>
        public void FlagRep()
        {
            if (_pendingRep != null)
            {
                _pendingRep = _pendingRep.WithFlagged(true);
            }
            else
            {
                _flagNextRep = true;
            }
        }

        /// <summary>Writes the Rep held back for the flag button. Call when the app pauses or quits.</summary>
        public void Flush()
        {
            if (_pendingRep == null)
            {
                return;
            }

            var rep = _pendingRep;
            _pendingRep = null;
            Write(() => RepLogFile.Append(RepLogPath, rep));
        }

        /// <summary>
        /// Saves a Paired Practice tally that ended (reset, switched to a Bout, or the session ended) or was interrupted
        /// (a drop or sleep), from this Fencer's side, as tally <paramref name="tallyNumber"/> of this session. An empty
        /// tally is not saved. Returns the record, or null when nothing was saved.
        /// </summary>
        public PairedPracticeRecord RecordPairedPractice(string role, int tallyNumber, PairedPracticeEnd end, PairedPracticeCounts you, PairedPracticeCounts opponent)
        {
            if (you.IsEmpty && opponent.IsEmpty)
            {
                return null;
            }

            var record = new PairedPracticeRecord(SessionId, _clock.UtcNow, role, end, you, opponent, tallyNumber);
            Write(() => PairedPracticeLogFile.Append(PairedPracticePath, record));
            return record;
        }

        private void OnRepCued(RepCue cue)
        {
            Flush();
        }

        private void OnRepJudged(RepResult result)
        {
            Flush();
            var entry = RepLogEntry.FromRepResult(result, _clock.UtcNow, SessionId, _flagNextRep, _armPoseSource?.Invoke());
            _flagNextRep = false;
            _pendingRep = entry;
            _runReps.Add(entry);
        }

        private void OnFinished(DrillRunSummary summary)
        {
            _flagNextRep = false;
            if (_runReps.Count == 0)
            {
                return;
            }

            var record = new DrillRunRecord(SessionId, SessionStartedAt, _clock.UtcNow, summary.WasCancelled, DrillResults.Aggregate(_runReps.ToArray()));
            _runReps.Clear();
            Write(() => SessionHistoryFile.Append(HistoryPath, record));
            DrillRunRecorded?.Invoke(record);
        }

        private void Write(Action write)
        {
            try
            {
                write();
            }
            catch (Exception error) when ((error is IOException || error is UnauthorizedAccessException) && WriteFailed != null)
            {
                WriteFailed.Invoke(error);
            }
        }
    }
}
