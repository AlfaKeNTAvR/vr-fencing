using System;
using System.Collections.Generic;
using VrFencing.Session;

namespace VrFencing.Partner
{
    /// <summary>The four French calls (PER-74), each a voice clip and a large floating word.</summary>
    public enum BoutCallClip
    {
        EnGarde,
        Prets,
        Allez,
        Halte,
    }

    /// <summary>
    /// The referee's calls as each headset plays them (PER-83, decided in PER-58, PER-74 and PER-75): the words and their
    /// timing, the halt reasons and prompts as this Fencer reads them, and the credits for the voice.
    /// </summary>
    public static class BoutCall
    {
        /// <summary>"Prets?" this long after "En garde!" starts.</summary>
        public const double PretsAtSeconds = 1.0;

        /// <summary>"Allez!" (and the phrase) this long after "En garde!" starts.</summary>
        public const double AllezAtSeconds = 2.0;

        /// <summary>"Allez!" stays written this long.</summary>
        public const double AllezShownSeconds = 1.0;

        /// <summary>A call that comes more than this late (a message delayed or a rejoin) is shown but not spoken.</summary>
        public const double LateCallSeconds = 0.5;

        /// <summary>
        /// The voice's attribution (PER-75), shown in the app on the Partner page and kept in <c>CREDITS.md</c>. The edited
        /// clips stay CC BY-SA 4.0.
        /// </summary>
        public const string Credits =
            "Voice \"en garde\", \"prêts\", \"allez\", \"halte\" by LoquaxFR, Lingua Libre / Wikimedia Commons: \"prêts\" CC BY 4.0, "
            + "the others CC BY-SA 4.0 (creativecommons.org/licenses/by-sa/4.0). Trimmed and normalized for this app; the edited clips stay CC BY-SA 4.0.";

        /// <summary>The written call, in plain letters (the headset's font has no accents).</summary>
        public static string Word(BoutCallClip clip)
        {
            switch (clip)
            {
                case BoutCallClip.EnGarde:
                    return "En garde!";
                case BoutCallClip.Prets:
                    return "Prets?";
                case BoutCallClip.Allez:
                    return "Allez!";
                default:
                    return "Halte!";
            }
        }

        /// <summary>The halt's reason under "Halte!", as <paramref name="me"/> reads it.</summary>
        public static string FormatHaltReason(BoutFlowState state, BoutSide me)
        {
            switch (state.Halt)
            {
                case HaltReason.Light:
                    if (state.HaltLight == LightKind.Double)
                    {
                        return "Double";
                    }

                    var isMine = state.HaltLight == (me == BoutSide.Host ? LightKind.HostTouch : LightKind.PartnerTouch);
                    return isMine ? "Your Touch" : "Opponent's Touch";
                case HaltReason.OffStrip:
                    var where = state.HaltOffStrip == OffStripKind.RearEnd ? "rear end" : "side";
                    return state.HaltedBy == me ? $"You left the Strip ({where})" : $"Opponent left the Strip ({where})";
                case HaltReason.CorpsACorps:
                    return "Corps-a-corps";
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// The small line under the scoring box for <paramref name="me"/>: in a Bout between phrases, what the call waits
        /// for; in Paired Practice, both tallies, and a warning while this Fencer is off their Strip.
        /// </summary>
        public static string FormatInfo(BoutFlowState state, BoutSide me, PairedPracticeTally tally, bool isMeOffStrip)
        {
            if (state.Mode == PartnerMode.PairedPractice)
            {
                var opponent = ScoringBox.Other(me);
                var lines = $"You: {FormatCounts(tally.For(me))}\nOpponent: {FormatCounts(tally.For(opponent))}";
                return isMeOffStrip ? "You are off the Strip\n" + lines : lines;
            }

            if (state.Phase != BoutPhase.WaitingForEnGarde)
            {
                return string.Empty;
            }

            if (!state.IsReady(me))
            {
                return "Go back behind your En Garde Line";
            }

            return state.IsReady(ScoringBox.Other(me)) ? "En garde..." : "Waiting for your opponent to go back";
        }

        public static string FormatCounts(PairedPracticeCounts counts)
        {
            return $"{counts.Touches} {Plural(counts.Touches, "Touch", "Touches")}, {counts.Doubles} {Plural(counts.Doubles, "double", "doubles")}, "
                   + $"{counts.Glances} {Plural(counts.Glances, "glance", "glances")}";
        }

        private static string Plural(int count, string one, string many)
        {
            return count == 1 ? one : many;
        }
    }

    /// <summary>
    /// Plays the bout flow on one headset (PER-83): from the flow state (the Host's own, or as the Partner received it)
    /// it says which call to speak now, once each, and what to write: the large word and the reason line. A call's times
    /// come from when "En garde!" started on this headset's clock, so "Allez!" sounds on both headsets together (to the
    /// clock sync's error) even though the Partner hears of the call a one-way trip late.
    /// </summary>
    public sealed class BoutAnnouncer
    {
        private static readonly IReadOnlyList<BoutCallClip> NoCalls = Array.Empty<BoutCallClip>();

        private readonly List<BoutCallClip> _due = new List<BoutCallClip>();
        private readonly HashSet<BoutCallClip> _spokenInCall = new HashSet<BoutCallClip>();
        private double? _callStartHostSeconds;
        private int? _haltSequence;

        public BoutAnnouncer(BoutSide me)
        {
            Me = me;
        }

        public BoutSide Me { get; }

        /// <summary>The large word to show now ("En garde!", "Halte!"...), or empty.</summary>
        public string Word { get; private set; } = string.Empty;

        /// <summary>The reason under the word (a halt's), or empty.</summary>
        public string Reason { get; private set; } = string.Empty;

        /// <summary>
        /// Advances to <paramref name="nowSeconds"/> on this headset's clock, with the call's "En garde!" started at
        /// <paramref name="callStartSeconds"/> on the same clock (<see cref="BoutFlowState.CallStartHostSeconds"/>
        /// turned into it). Returns the calls to speak now.
        /// </summary>
        public IReadOnlyList<BoutCallClip> Step(BoutFlowState state, double callStartSeconds, double nowSeconds)
        {
            _due.Clear();
            Word = string.Empty;
            Reason = string.Empty;
            switch (state.Phase)
            {
                case BoutPhase.Calling:
                case BoutPhase.Fencing:
                    StepCall(state.CallStartHostSeconds, callStartSeconds, nowSeconds);
                    break;
                case BoutPhase.Halted:
                    if (_haltSequence != state.Sequence)
                    {
                        _haltSequence = state.Sequence;
                        _due.Add(BoutCallClip.Halte);
                    }

                    Word = BoutCall.Word(BoutCallClip.Halte);
                    Reason = BoutCall.FormatHaltReason(state, Me);
                    break;
            }

            return _due.Count > 0 ? _due.ToArray() : NoCalls;
        }

        /// <summary>Forgets what was spoken (the partner left).</summary>
        public void Reset()
        {
            _callStartHostSeconds = null;
            _haltSequence = null;
            _spokenInCall.Clear();
            Word = string.Empty;
            Reason = string.Empty;
        }

        private void StepCall(double callStartHostSeconds, double callStartSeconds, double nowSeconds)
        {
            // The Host's start time names the call: the Partner's local start moves a little with every clock update.
            if (_callStartHostSeconds != callStartHostSeconds)
            {
                _callStartHostSeconds = callStartHostSeconds;
                _spokenInCall.Clear();
            }

            var allezAt = callStartSeconds + BoutCall.AllezAtSeconds;
            var current = (BoutCallClip?)null;
            foreach (var (clip, at) in new[]
                     {
                         (BoutCallClip.EnGarde, callStartSeconds),
                         (BoutCallClip.Prets, callStartSeconds + BoutCall.PretsAtSeconds),
                         (BoutCallClip.Allez, allezAt),
                     })
            {
                if (nowSeconds < at)
                {
                    continue;
                }

                current = clip;

                // Spoken once per call, and not at all when more than LateCallSeconds late (a rejoin mid-phrase).
                if (_spokenInCall.Add(clip) && nowSeconds - at <= BoutCall.LateCallSeconds)
                {
                    _due.Add(clip);
                }
            }

            if (current.HasValue && (current.Value != BoutCallClip.Allez || nowSeconds < allezAt + BoutCall.AllezShownSeconds))
            {
                Word = BoutCall.Word(current.Value);
            }
        }
    }
}
