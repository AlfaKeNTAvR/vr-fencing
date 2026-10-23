using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace VrFencing.Partner
{
    /// <summary>How this headset's view of the blades' contact differs from the partner's report (PER-145).</summary>
    public enum ContactDisagreement
    {
        None,

        /// <summary>The blades press here, not in the partner's view.</summary>
        PressingOnlyHere,

        /// <summary>The blades press in the partner's view, not here.</summary>
        PressingOnlyThere,

        /// <summary>Both views have the blades pressing, but with this Fencer's blade on opposite sides.</summary>
        OppositeSides,
    }

    /// <summary>
    /// Watches where this headset's view of the blades' contact and the partner's own (sent with their hand,
    /// <see cref="BladeContactReport"/>) disagree, and logs each disagreement that lasts at least
    /// <see cref="MinLoggedSeconds"/>, once it ends, with how long it lasted (PER-145, decided there: the drawn blades are
    /// never corrected, only the disagreement is logged). The partner's report is about one lag old, so short
    /// disagreements at the start and end of every contact are expected; slow binds and presses should agree. Log tag
    /// <c>[Blade]</c>. Plain C#: the Unity side feeds it each frame and prints <see cref="TakeLog"/>.
    /// </summary>
    public sealed class ContactViewAgreement
    {
        /// <summary>Shorter disagreements are counted but not logged: one frame's flicker, or the edge of a contact.</summary>
        public const double MinLoggedSeconds = 0.05;

        /// <summary>Two sides this far from opposite (the cosine between them) still count as the same side.</summary>
        public const float SameSideCosine = 0.0f;

        private const string LogTag = "[Blade]";

        private readonly List<string> _log = new List<string>();
        private ContactDisagreement _current = ContactDisagreement.None;
        private double _startedSeconds;
        private double _lastSeconds;

        /// <summary>Disagreements of at least <see cref="MinLoggedSeconds"/> since the start.</summary>
        public int LoggedCount { get; private set; }

        /// <summary>Seconds of disagreement of any length since the start.</summary>
        public double TotalSeconds { get; private set; }

        /// <summary>What disagrees this frame.</summary>
        public ContactDisagreement Current => _current;

        /// <summary>
        /// What disagrees between this view (<paramref name="isPressingHere"/>, <paramref name="sideHere"/>: from the
        /// partner's blade toward this Fencer's, zero when unknown) and the partner's <paramref name="report"/> (its side
        /// already turned into this Fencer's Strip frame: from this Fencer's blade toward the partner's).
        /// </summary>
        public static ContactDisagreement Compare(bool isPressingHere, Vector3 sideHere, BladeContactReport report)
        {
            if (isPressingHere && !report.IsInContact)
            {
                return ContactDisagreement.PressingOnlyHere;
            }

            if (!isPressingHere && report.IsInContact)
            {
                return ContactDisagreement.PressingOnlyThere;
            }

            if (isPressingHere && sideHere != Vector3.Zero && report.Side != Vector3.Zero
                && Vector3.Dot(sideHere, -report.Side) < SameSideCosine)
            {
                return ContactDisagreement.OppositeSides;
            }

            return ContactDisagreement.None;
        }

        /// <summary>
        /// One frame at <paramref name="nowSeconds"/>. With no <paramref name="report"/> (none came, or the partner's hand
        /// is lost) nothing is compared and an open disagreement ends.
        /// </summary>
        public void Step(double nowSeconds, bool isPressingHere, Vector3 sideHere, BladeContactReport? report)
        {
            var disagreement = report.HasValue ? Compare(isPressingHere, sideHere, report.Value) : ContactDisagreement.None;
            if (_current != ContactDisagreement.None)
            {
                TotalSeconds += Math.Max(0.0, nowSeconds - _lastSeconds);
            }

            if (disagreement != _current)
            {
                End(nowSeconds);
                _current = disagreement;
                _startedSeconds = nowSeconds;
            }

            _lastSeconds = nowSeconds;
        }

        /// <summary>Ends any open disagreement (the partner left) and forgets the counts.</summary>
        public void Reset()
        {
            _current = ContactDisagreement.None;
            LoggedCount = 0;
            TotalSeconds = 0.0;
        }

        /// <summary>The log lines since the last call, in order.</summary>
        public IReadOnlyList<string> TakeLog()
        {
            var taken = _log.ToArray();
            _log.Clear();
            return taken;
        }

        private void End(double nowSeconds)
        {
            var seconds = nowSeconds - _startedSeconds;
            if (_current == ContactDisagreement.None || seconds < MinLoggedSeconds)
            {
                return;
            }

            LoggedCount++;
            var milliseconds = Math.Round(seconds * 1000.0).ToString("0", CultureInfo.InvariantCulture);
            _log.Add($"{LogTag} Views disagreed for {milliseconds} ms: {Describe(_current)} (disagreement {LoggedCount}).");
        }

        private static string Describe(ContactDisagreement disagreement)
        {
            switch (disagreement)
            {
                case ContactDisagreement.PressingOnlyHere:
                    return "the blades pressed here, not in the partner's view";
                case ContactDisagreement.PressingOnlyThere:
                    return "the blades pressed in the partner's view, not here";
                default:
                    return "both pressed, with this blade on opposite sides";
            }
        }
    }
}
