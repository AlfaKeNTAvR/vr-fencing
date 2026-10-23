// The blade-contact log (PER-136): a header with units, every row as wide as the header, the slide-off edges found
// from frame to frame, a mark held for the next row, the Fencer recording's file stem, a size that stays well under
// 60 MB for a 10-minute recording at 72 Hz, and no allocation per row.
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Blade;
using VrFencing.Settings;

namespace VrFencing.Tests.Blade
{
    public class BladeContactLogTests
    {
        private const int FramesPerMinute = 72 * 60;

        // Columns that are flags or names, with no unit.
        private static readonly string[] UnitlessColumns =
        {
            "fencer_slide_off", "fencer_slide_phase", "dummy_slide_off", "dummy_slide_phase", "dummy_armed", "in_contact",
            "engaged", "side_x", "side_y", "side_z", "saturated", "dummy_mode", "dummy_action", "opponent_hand",
            "dummy_deflection_phase", "partner_view_contact", "partner_view_side_x", "partner_view_side_y", "partner_view_side_z", "events",
        };

        [Test]
        public void TheFileIsNamedAfterTheFencerRecordingsStem()
        {
            var started = new DateTimeOffset(2026, 10, 22, 10, 15, 0, TimeSpan.Zero);

            Assert.That(BladeContactLog.GetFileName(started), Is.EqualTo("blade-20261022-101500.csv"));
            Assert.That(BladeContactLog.GetFileName(started).Substring(6, 15), Is.EqualTo(VrFencing.Tracking.FencerRecordingFile.GetFileName(started).Substring(7, 15)));
        }

        [Test]
        public void EveryColumnNamesItsUnitAndEveryRowIsAsWideAsTheHeader()
        {
            var lines = WriteLines(Pressing(0.5), Apart(1.0));
            var columns = lines.First(line => !line.StartsWith("#", StringComparison.Ordinal)).Split(',');

            Assert.That(lines.Take(3).All(line => line.StartsWith("#", StringComparison.Ordinal)), Is.True, "Three comment lines first.");
            Assert.That(columns, Has.Length.EqualTo(BladeContactLog.ColumnCount));
            foreach (var column in columns.Where(column => !UnitlessColumns.Contains(column)))
            {
                Assert.That(column, Does.EndWith("_s").Or.EndWith("_m").Or.EndWith("_deg").Or.EndWith("_n").Or.EndWith("_mps").Or.EndWith("_frac"), $"{column} names no unit.");
            }

            foreach (var row in lines.Skip(4))
            {
                Assert.That(row.Split(','), Has.Length.EqualTo(columns.Length), row);
            }
        }

        [Test]
        public void ARowWritesTheSampleInItsColumns()
        {
            var lines = WriteLines(Pressing(2.5));
            var cells = ToCells(lines[3], lines[4]);

            Assert.That(cells["time_s"], Is.EqualTo("2.5000"));
            Assert.That(cells["in_contact"], Is.EqualTo("1"));
            Assert.That(cells["press_n"], Is.EqualTo("3.250"));
            Assert.That(cells["contact_on_dummy_frac"], Is.EqualTo("0.800"));
            Assert.That(cells["dummy_grip_turn_deg"], Is.EqualTo("4.50"));
            Assert.That(cells["saturated"], Is.EqualTo("fencer_turn|dummy_bow"));
            Assert.That(cells["dummy_deflection_phase"], Is.EqualTo("open"));
            Assert.That(cells["opponent_hand"], Is.EqualTo("strong"));
            Assert.That(cells["dummy_action"], Is.EqualTo("thrust"));
            Assert.That(cells["sword_hand_speed_mps"], Is.EqualTo("5.000"));
            Assert.That(cells["fencer_bell_gap_m"], Is.EqualTo("-0.0030"));
            Assert.That(cells["fencer_handle_gap_m"], Is.EqualTo("0.0310"));
        }

        [Test]
        public void ApartTheContactCellsAreEmpty()
        {
            var lines = WriteLines(Apart(0.0));
            var cells = ToCells(lines[3], lines[4]);

            Assert.That(cells["in_contact"], Is.EqualTo("0"));
            Assert.That(cells["contact_x_m"], Is.Empty);
            Assert.That(cells["contact_on_fencer_frac"], Is.Empty);
            Assert.That(cells["dummy_deflection_phase"], Is.Empty, "Unarmed: no Dummy hand.");
            Assert.That(cells["events"], Is.Empty);
            Assert.That(cells["partner_view_contact"], Is.Empty, "No partner: no partner view.");
            Assert.That(cells["partner_view_side_x"], Is.Empty);
        }

        [Test]
        public void APartnersOwnViewIsWrittenBesideThisOne()
        {
            // PER-145: with a partner, their reported view of the contact sits beside this headset's own.
            var sample = Pressing(1.0);
            sample.DummyMode = "partner";
            sample.PartnerViewInContact = false;
            sample.PartnerViewPressMeters = 0.0f;
            sample.PartnerViewSide = new System.Numerics.Vector3(0.0f, -1.0f, 0.0f);

            var lines = WriteLines(sample);
            var cells = ToCells(lines[3], lines[4]);

            Assert.That(cells["dummy_mode"], Is.EqualTo("partner"));
            Assert.That(cells["partner_view_contact"], Is.EqualTo("0"));
            Assert.That(cells["partner_view_overlap_m"], Is.EqualTo("0.0000"));
            Assert.That(cells["partner_view_side_y"], Is.EqualTo("-1.000"));
            Assert.That(lines[0], Does.Contain("format 3"));
        }

        [Test]
        public void ASlideOffIsMarkedWhereItStartsAndWhereItEnds()
        {
            var phases = new[]
            {
                (SlideOffPhase.None, SlideOffPhase.None),
                (SlideOffPhase.None, SlideOffPhase.Sliding),
                (SlideOffPhase.None, SlideOffPhase.Sliding),
                (SlideOffPhase.None, SlideOffPhase.Returning),
                (SlideOffPhase.None, SlideOffPhase.Returning),
                (SlideOffPhase.None, SlideOffPhase.None),
                (SlideOffPhase.Sliding, SlideOffPhase.None),
                (SlideOffPhase.None, SlideOffPhase.None),
            };
            var log = new BladeContactLog();
            log.WriteHeader(TextWriter.Null, "test", Hand.Right, DateTimeOffset.UnixEpoch);
            var events = phases.Select(phase =>
            {
                var sample = Pressing(0.0);
                sample.Fencer.SlideOff = phase.Item1;
                sample.Dummy.SlideOff = phase.Item2;
                return log.WriteRow(sample, TextWriter.Null);
            }).ToArray();

            Assert.That(events.Select(e => (e & BladeLogEvents.SlideOffStart) != 0), Is.EqualTo(new[] { false, true, false, false, false, false, true, false }));
            Assert.That(events.Select(e => (e & BladeLogEvents.SlideOffEnd) != 0), Is.EqualTo(new[] { false, false, false, false, false, true, false, true }));
        }

        [Test]
        public void AMarkIsWrittenOnTheNextRowOnly()
        {
            var log = new BladeContactLog();
            using var text = new StringWriter();
            log.WriteHeader(text, "test", Hand.Left, DateTimeOffset.UnixEpoch);
            log.WriteRow(Apart(0.0), text);
            log.AddEvents(BladeLogEvents.Mark);
            log.AddEvents(BladeLogEvents.Touch);
            var sample = Apart(0.1);
            sample.Events = BladeLogEvents.Clash;
            log.WriteRow(sample, text);
            log.WriteRow(Apart(0.2), text);

            var rows = text.ToString().Split('\n').Skip(4).Where(line => line.Length > 0).ToArray();
            Assert.That(rows[0], Does.EndWith(","));
            Assert.That(rows[1], Does.EndWith(",clash|touch|mark"));
            Assert.That(rows[2], Does.EndWith(","));
            Assert.That(log.MarkCount, Is.EqualTo(1));
            Assert.That(log.RowCount, Is.EqualTo(3));
        }

        [Test]
        public void TenMinutesAt72HertzStayWellUnder60Megabytes()
        {
            var log = new BladeContactLog();
            using var bytes = new MemoryStream();
            using (var writer = new StreamWriter(bytes, new System.Text.UTF8Encoding(false), 65536, leaveOpen: true))
            {
                log.WriteHeader(writer, "test", Hand.Right, DateTimeOffset.UnixEpoch);
                for (var frame = 0; frame < FramesPerMinute; frame++)
                {
                    var sample = frame % 3 == 0 ? Apart(frame / 72.0) : Pressing(frame / 72.0);
                    sample.Fencer.Guard += new Vector3(frame * 1e-4f, -frame * 1e-4f, frame * 3e-4f);
                    log.WriteRow(sample, writer);
                }
            }

            var megabytesPerMinute = bytes.Length / (1024.0 * 1024.0);
            TestContext.WriteLine($"{bytes.Length / (double)FramesPerMinute:0} bytes a row, {megabytesPerMinute:0.00} MB a minute, {megabytesPerMinute * 10.0:0.0} MB for 10 minutes.");
            Assert.That(megabytesPerMinute * 10.0, Is.LessThan(30.0));
        }

        [Test]
        public void ARowAllocatesNothingOnceWarmedUp()
        {
            var log = new BladeContactLog();
            log.WriteHeader(TextWriter.Null, "test", Hand.Right, DateTimeOffset.UnixEpoch);
            var pressing = Pressing(1.0);
            var apart = Apart(1.0);
            log.WriteRow(pressing, TextWriter.Null);
            log.WriteRow(apart, TextWriter.Null);

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var frame = 0; frame < 1000; frame++)
            {
                log.AddEvents(BladeLogEvents.Mark);
                log.WriteRow(frame % 2 == 0 ? pressing : apart, TextWriter.Null);
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            TestContext.WriteLine($"Allocated {allocated} bytes over 1000 rows.");
            Assert.That(allocated, Is.LessThan(1000));
        }

        private static string[] WriteLines(params BladeContactSample[] samples)
        {
            var log = new BladeContactLog();
            using var text = new StringWriter();
            log.WriteHeader(text, "0.1.0", Hand.Right, new DateTimeOffset(2026, 10, 22, 10, 15, 0, TimeSpan.Zero));
            foreach (var sample in samples)
            {
                log.WriteRow(sample, text);
            }

            return text.ToString().Split('\n').Where(line => line.Length > 0).ToArray();
        }

        private static System.Collections.Generic.Dictionary<string, string> ToCells(string header, string row)
        {
            var names = header.Split(',');
            var cells = row.Split(',');
            return names.Select((name, index) => (name, index)).ToDictionary(pair => pair.name, pair => cells[pair.index]);
        }

        /// <summary>A frame with the blades pressed on each other, every field set.</summary>
        private static BladeContactSample Pressing(double time)
        {
            return new BladeContactSample
            {
                TimeSeconds = time,
                DeltaSeconds = 1.0f / 72.0f,
                Fencer = new BladeTrace
                {
                    Guard = new Vector3(0.2f, 1.1f, 0.4f),
                    RealPoint = new Vector3(0.1f, 1.05f, 1.3f),
                    DrawnPoint = new Vector3(0.12f, 1.04f, 1.29f),
                    GripTurnDegrees = 5.0f,
                    BowMeters = 0.012f,
                    BellGapMeters = -0.003f,
                    HandleGapMeters = 0.031f,
                },
                Dummy = new BladeTrace
                {
                    Guard = new Vector3(0.0f, 1.1f, 2.6f),
                    RealPoint = new Vector3(0.0f, 1.05f, 1.7f),
                    DrawnPoint = new Vector3(0.02f, 1.06f, 1.7f),
                    GripTurnDegrees = 4.5f,
                    BowMeters = 0.08f,
                    BellGapMeters = 0.25f,
                },
                IsDummyArmed = true,
                IsInContact = true,
                IsEngaged = true,
                ContactPoint = new Vector3(0.05f, 1.07f, 1.0f),
                ContactOnFencer = 0.4f,
                ContactOnDummy = 0.8f,
                OverlapMeters = 0.034f,
                PressNewtons = 3.25f,
                Side = Vector3.UnitY,
                SaturatedStages = 0b1001,
                ClosestMeters = 0.004f,
                DrawnGapMeters = 0.0001f,
                DummyMode = "drill",
                DummyAction = "thrust",
                OpponentHand = OpponentHand.Strong,
                DummyHandGive = new Vector3(0.0f, -0.04f, 0.0f),
                DummyDeflectionPhase = DeflectionPhase.Open,
                DummyTipOffset = new Vector3(0.01f, -0.05f, 0.0f),
                SwordHandVelocity = new Vector3(0.0f, 3.0f, 4.0f),
            };
        }

        /// <summary>A frame with the Dummy unarmed: only the Fencer's blade.</summary>
        private static BladeContactSample Apart(double time)
        {
            var sample = Pressing(time);
            BladeContactProbe.ClearArmed(ref sample);
            sample.DummyMode = "unarmed";
            sample.DummyAction = "en_garde";
            return sample;
        }
    }
}
