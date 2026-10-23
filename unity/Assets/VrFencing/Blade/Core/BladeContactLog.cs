using System;
using System.Globalization;
using System.IO;
using System.Numerics;
using VrFencing.Settings;

namespace VrFencing.Blade
{
    /// <summary>
    /// The blade-contact log (PER-136): a CSV written next to each Fencer recording, one row per rendered frame, so a
    /// headset test of the blades can be read on the PC (<c>scripts/blade-log.py</c>). The file starts with '#' comment
    /// lines (format version, app version, sword hand, start time, the frame and units), then the column names, each
    /// ending in its unit (_m, _deg, _n, _s, _mps, _frac; none for flags and names). Flags are 1/0, names are lower
    /// case, an empty cell is no value. The event column joins the frame's events with '|'.
    /// <para>
    /// The log also finds the slide-off edges (start and end, per blade) from frame to frame, and holds events raised
    /// between frames (a mark, a Touch) for the next row. About 470 bytes a row: 2 MB a minute at 72 Hz.
    /// </para>
    /// </summary>
    public sealed class BladeContactLog
    {
        /// <summary>3: the partner-view columns, and the Dummy's columns hold a partner's blade in a Partner session (PER-145).</summary>
        public const int FormatVersion = 3;

        public const string FilePrefix = "blade-";

        public const string Extension = ".csv";

        /// <summary>Positions and lengths to 0.1 mm.</summary>
        private const int MeterDecimals = 4;

        private const int SecondDecimals = 4;

        private const int DegreeDecimals = 2;

        private const int NewtonDecimals = 3;

        private const int FractionDecimals = 3;

        private const int SpeedDecimals = 3;

        /// <summary>Names of <see cref="BladeLogEvents"/>' bits, in bit order, as the event column writes them.</summary>
        public static readonly string[] EventNames =
        {
            "clash", "beat", "parry", "touch", "glance", "flat", "slide_off_start", "slide_off_end", "hit_on_fencer", "mark",
        };

        /// <summary>Names of the solver's give stages' bits (<see cref="BladeContactSolver.SaturatedStages"/>), in bit order.</summary>
        public static readonly string[] StageNames = { "fencer_turn", "fencer_bow", "dummy_turn", "dummy_bow" };

        private static readonly string[] SlideOffNames = { "none", "sliding", "returning" };

        private static readonly string[] DeflectionPhaseNames = { "spring", "out", "open", "recover" };

        private static readonly string[] OpponentHandNames = { "light", "medium", "strong" };

        private static readonly string ColumnLine = BuildColumnLine();

        private readonly CsvRowBuilder _row = new CsvRowBuilder();
        private BladeLogEvents _pending;
        private SlideOffPhase _lastFencerSlide;
        private SlideOffPhase _lastDummySlide;

        /// <summary>Rows written since the header.</summary>
        public int RowCount { get; private set; }

        /// <summary>Marks written since the header.</summary>
        public int MarkCount { get; private set; }

        /// <summary>The log's file name for a recording started at <paramref name="startedUtc"/>: the Fencer recording's stem.</summary>
        public static string GetFileName(DateTimeOffset startedUtc)
        {
            return FilePrefix + startedUtc.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + Extension;
        }

        /// <summary>The column names, comma-separated, each with its unit.</summary>
        public static string Columns => ColumnLine;

        /// <summary>The number of columns in every row.</summary>
        public static int ColumnCount => ColumnLine.Split(',').Length;

        /// <summary>Writes the comment lines and the column names; starts the edges and the counts over.</summary>
        public void WriteHeader(TextWriter writer, string appVersion, Hand swordHand, DateTimeOffset startedUtc)
        {
            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            writer.Write("# vr-fencing blade-contact log, format " + FormatVersion.ToString(CultureInfo.InvariantCulture) + "\n");
            writer.Write("# app " + (appVersion ?? "unknown") + ", sword hand " + swordHand.ToString().ToLowerInvariant() +
                ", started " + startedUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture) + "\n");
            writer.Write("# Strip frame: +x the Fencer's right, +y up, +z toward the Dummy. Units in the column names. " +
                "side: unit vector from the Dummy's blade to the Fencer's. bell_gap: the other drawn blade's clearance from this " +
                "blade's bell guard (negative: through it), handle_gap the same for its grip behind the bell. drawn_gap: the drawn " +
                "blades' clearance (negative: through each other). With a partner (dummy_mode partner or mirror) the dummy columns are " +
                "the partner's blade from this headset's own solve, and partner_view the partner's own view as they reported it " +
                "(partner_view_side in the side convention).\n");
            writer.Write(ColumnLine);
            writer.Write("\n");
            RowCount = 0;
            MarkCount = 0;
            _pending = BladeLogEvents.None;
            _lastFencerSlide = SlideOffPhase.None;
            _lastDummySlide = SlideOffPhase.None;
        }

        /// <summary>Holds <paramref name="events"/> for the next row (a mark pressed, a Touch raised between frames).</summary>
        public void AddEvents(BladeLogEvents events)
        {
            _pending |= events;
        }

        /// <summary>
        /// Writes <paramref name="sample"/> as one row, with the pending events and this frame's slide-off edges added to
        /// its own. Returns the events written.
        /// </summary>
        public BladeLogEvents WriteRow(in BladeContactSample sample, TextWriter writer)
        {
            var events = sample.Events | _pending | FindSlideEdges(sample.Fencer.SlideOff, sample.Dummy.SlideOff);
            _pending = BladeLogEvents.None;
            _lastFencerSlide = sample.Fencer.SlideOff;
            _lastDummySlide = sample.Dummy.SlideOff;

            _row.Clear();
            _row.Append(sample.TimeSeconds, SecondDecimals);
            _row.Append(sample.DeltaSeconds, SecondDecimals);
            AppendBlade(sample.Fencer);
            AppendBlade(sample.Dummy);

            _row.Append(sample.IsDummyArmed);
            _row.Append(sample.IsInContact);
            _row.Append(sample.IsEngaged);
            var isInContact = sample.IsInContact;
            AppendVector(sample.ContactPoint, MeterDecimals, isInContact);
            AppendNumber(sample.ContactOnFencer, FractionDecimals, isInContact);
            AppendNumber(sample.ContactOnDummy, FractionDecimals, isInContact);
            _row.Append(sample.OverlapMeters, MeterDecimals);
            _row.Append(sample.PressNewtons, NewtonDecimals);
            AppendVector(sample.Side, FractionDecimals, sample.Side != Vector3.Zero);
            _row.AppendFlags(sample.SaturatedStages, StageNames);
            AppendNumber(sample.ClosestMeters, MeterDecimals, sample.ClosestMeters < float.MaxValue);
            _row.Append(sample.DrawnGapMeters, MeterDecimals);

            _row.Append(sample.DummyMode);
            _row.Append(sample.DummyAction);
            _row.Append(GetName(OpponentHandNames, (int)sample.OpponentHand));
            AppendVector(sample.DummyHandGive, MeterDecimals, sample.IsDummyArmed);
            _row.Append(sample.IsDummyArmed ? GetName(DeflectionPhaseNames, (int)sample.DummyDeflectionPhase) : null);
            AppendVector(sample.DummyTipOffset, MeterDecimals, sample.IsDummyArmed);

            var hasPartnerView = sample.PartnerViewInContact.HasValue;
            if (hasPartnerView)
            {
                _row.Append(sample.PartnerViewInContact.Value);
            }
            else
            {
                _row.AppendEmpty();
            }

            AppendNumber(sample.PartnerViewPressMeters, MeterDecimals, hasPartnerView);
            AppendVector(sample.PartnerViewSide, FractionDecimals, hasPartnerView && sample.PartnerViewSide != Vector3.Zero);

            AppendVector(sample.SwordHandVelocity, SpeedDecimals, true);
            _row.Append(sample.SwordHandVelocity.Length(), SpeedDecimals);
            _row.AppendFlags((int)events, EventNames);
            _row.WriteLineTo(writer);

            RowCount++;
            if ((events & BladeLogEvents.Mark) != 0)
            {
                MarkCount++;
            }

            return events;
        }

        /// <summary>A slide-off starts when a blade begins to slide; it ends when neither blade slides or swings back.</summary>
        private BladeLogEvents FindSlideEdges(SlideOffPhase fencer, SlideOffPhase dummy)
        {
            var wasReleasing = _lastFencerSlide != SlideOffPhase.None || _lastDummySlide != SlideOffPhase.None;
            var isReleasing = fencer != SlideOffPhase.None || dummy != SlideOffPhase.None;
            var events = BladeLogEvents.None;
            if ((fencer == SlideOffPhase.Sliding && _lastFencerSlide != SlideOffPhase.Sliding)
                || (dummy == SlideOffPhase.Sliding && _lastDummySlide != SlideOffPhase.Sliding))
            {
                events |= BladeLogEvents.SlideOffStart;
            }

            if (wasReleasing && !isReleasing)
            {
                events |= BladeLogEvents.SlideOffEnd;
            }

            return events;
        }

        private void AppendBlade(in BladeTrace blade)
        {
            AppendVector(blade.Guard, MeterDecimals, true);
            AppendVector(blade.RealPoint, MeterDecimals, true);
            AppendVector(blade.DrawnPoint, MeterDecimals, true);
            _row.Append(blade.GripTurnDegrees, DegreeDecimals);
            _row.Append(blade.BowMeters, MeterDecimals);
            _row.Append(blade.SlideOff != SlideOffPhase.None);
            _row.Append(GetName(SlideOffNames, (int)blade.SlideOff));
            _row.Append(blade.BellGapMeters, MeterDecimals);
            _row.Append(blade.HandleGapMeters, MeterDecimals);
        }

        private void AppendVector(Vector3 vector, int decimals, bool hasValue)
        {
            AppendNumber(vector.X, decimals, hasValue);
            AppendNumber(vector.Y, decimals, hasValue);
            AppendNumber(vector.Z, decimals, hasValue);
        }

        private void AppendNumber(float value, int decimals, bool hasValue)
        {
            if (hasValue)
            {
                _row.Append(value, decimals);
            }
            else
            {
                _row.AppendEmpty();
            }
        }

        private static string GetName(string[] names, int index)
        {
            return index >= 0 && index < names.Length ? names[index] : null;
        }

        private static string BuildColumnLine()
        {
            var columns = new System.Collections.Generic.List<string> { "time_s", "dt_s" };
            foreach (var blade in new[] { "fencer", "dummy" })
            {
                AddVectorColumns(columns, blade + "_guard", "_m");
                AddVectorColumns(columns, blade + "_real_point", "_m");
                AddVectorColumns(columns, blade + "_drawn_point", "_m");
                columns.Add(blade + "_grip_turn_deg");
                columns.Add(blade + "_bow_m");
                columns.Add(blade + "_slide_off");
                columns.Add(blade + "_slide_phase");
                columns.Add(blade + "_bell_gap_m");
                columns.Add(blade + "_handle_gap_m");
            }

            columns.Add("dummy_armed");
            columns.Add("in_contact");
            columns.Add("engaged");
            AddVectorColumns(columns, "contact", "_m");
            columns.Add("contact_on_fencer_frac");
            columns.Add("contact_on_dummy_frac");
            columns.Add("overlap_m");
            columns.Add("press_n");
            AddVectorColumns(columns, "side", string.Empty);
            columns.Add("saturated");
            columns.Add("closest_m");
            columns.Add("drawn_gap_m");

            columns.Add("dummy_mode");
            columns.Add("dummy_action");
            columns.Add("opponent_hand");
            AddVectorColumns(columns, "dummy_hand_give", "_m");
            columns.Add("dummy_deflection_phase");
            AddVectorColumns(columns, "dummy_tip_offset", "_m");

            columns.Add("partner_view_contact");
            columns.Add("partner_view_overlap_m");
            AddVectorColumns(columns, "partner_view_side", string.Empty);

            AddVectorColumns(columns, "sword_hand_velocity", "_mps");
            columns.Add("sword_hand_speed_mps");
            columns.Add("events");
            return string.Join(",", columns);
        }

        private static void AddVectorColumns(System.Collections.Generic.List<string> columns, string name, string unit)
        {
            columns.Add(name + "_x" + unit);
            columns.Add(name + "_y" + unit);
            columns.Add(name + "_z" + unit);
        }
    }
}
