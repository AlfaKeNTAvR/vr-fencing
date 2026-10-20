using VrFencing.Session;

namespace VrFencing.Partner
{
    /// <summary>
    /// Numbers the Paired Practice tallies this app saves (PER-84), so the Session log tells which records are one tally:
    /// a tally interrupted by a drop or a sleep is saved so far, and if a rejoin carries it on, its later records keep the
    /// number (the last one holds its counts). A tally that ended for good (reset, switched to a Bout, disconnected)
    /// moves on to the next number.
    ///
    /// A rejoin carries the old tally on exactly when the first tally heard after it is not empty: nothing can be counted
    /// while the Partner is away, so a non-empty tally is the one the Host kept, and an empty one means it ended while
    /// away (or was empty, when the number does not matter). Lives as long as the app, across connections.
    /// </summary>
    public sealed class TallyNumbering
    {
        private bool _mayContinue;

        /// <summary>The number the tally in progress is saved under, from 1.</summary>
        public int Current { get; private set; } = 1;

        /// <summary>The number to save <paramref name="ended"/> under; afterwards the next tally's.</summary>
        public int Number(EndedTally ended)
        {
            var number = Current;
            if (ended.End == PairedPracticeEnd.Interrupted)
            {
                _mayContinue = true;
            }
            else
            {
                Current++;
                _mayContinue = false;
            }

            return number;
        }

        /// <summary>
        /// The first tally after reconnecting (the Host's kept one, or the one the Partner first hears): an empty one
        /// starts a new number when the last was interrupted.
        /// </summary>
        public void OnReconnected(bool isTallyEmpty)
        {
            if (_mayContinue && isTallyEmpty)
            {
                Current++;
            }

            _mayContinue = false;
        }
    }
}
