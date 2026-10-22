// The blade-contact log's row builder (PER-136): numbers fixed-point with invariant formatting, an empty cell for no
// value, flags joined by '|', and no allocation per row once warmed up.
using System;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using VrFencing.Blade;

namespace VrFencing.Tests.Blade
{
    public class CsvRowBuilderTests
    {
        [Test]
        public void NumbersAreRoundedToTheirDecimalsWithAPointWhateverTheCulture()
        {
            var culture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                var row = new CsvRowBuilder();
                row.Append(1.23456, 3);
                row.Append(-0.0004, 3);
                row.Append(-1.5, 1);
                row.Append(0.05, 1);
                row.Append(12.0, 0);
                row.Append(-7);
                row.Append(0.000123, 6);

                Assert.That(row.ToString(), Is.EqualTo("1.235,0.000,-1.5,0.1,12,-7,0.000123"));
            }
            finally
            {
                CultureInfo.CurrentCulture = culture;
            }
        }

        [Test]
        public void NoValueIsAnEmptyCell()
        {
            var row = new CsvRowBuilder();
            row.Append(double.NaN, 3);
            row.Append(float.PositiveInfinity, 3);
            row.AppendEmpty();
            row.Append((string)null);
            row.Append(true);

            Assert.That(row.ToString(), Is.EqualTo(",,,,1"));
        }

        [Test]
        public void FlagsAreJoinedByABarAndNoneIsEmpty()
        {
            var names = new[] { "a", "b", "c" };
            var row = new CsvRowBuilder();
            row.AppendFlags(0b101, names);
            row.AppendFlags(0, names);
            row.AppendFlags(0b010, names);

            Assert.That(row.ToString(), Is.EqualTo("a|c,,b"));
        }

        [Test]
        public void ManyValuesReadBackWithinHalfTheLastDigit()
        {
            var random = new Random(7);
            var row = new CsvRowBuilder();
            for (var index = 0; index < 2000; index++)
            {
                var value = (random.NextDouble() - 0.5) * Math.Pow(10.0, random.Next(-4, 6));
                var decimals = random.Next(0, 7);
                row.Clear();
                row.Append(value, decimals);
                var read = double.Parse(row.ToString(), CultureInfo.InvariantCulture);
                Assert.That(read, Is.EqualTo(value).Within((0.5 * Math.Pow(10.0, -decimals)) + 1e-9), $"{value} with {decimals} decimals read {row}.");
            }
        }

        [Test]
        public void WritingALineEndsItAndTheRowCanBeWrittenAgain()
        {
            var row = new CsvRowBuilder();
            row.Append(1);
            row.Append("x");
            using var text = new StringWriter();
            row.WriteLineTo(text);
            row.WriteLineTo(text);

            Assert.That(text.ToString(), Is.EqualTo("1,x\n1,x\n"));
        }

        [Test]
        public void ARowAllocatesNothingOnceWarmedUp()
        {
            var row = new CsvRowBuilder(16);
            var names = new[] { "beat", "parry" };
            for (var frame = 0; frame < 2; frame++)
            {
                FillRow(row, frame, names);
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var frame = 0; frame < 1000; frame++)
            {
                FillRow(row, frame, names);
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            TestContext.WriteLine($"Allocated {allocated} bytes over 1000 rows.");
            Assert.That(allocated, Is.LessThan(1000), "No allocation per row.");
        }

        private static void FillRow(CsvRowBuilder row, int frame, string[] names)
        {
            row.Clear();
            for (var cell = 0; cell < 60; cell++)
            {
                row.Append((frame * 0.0137) - cell, 4);
            }

            row.Append("drill");
            row.AppendFlags(frame & 3, names);
            row.WriteLineTo(TextWriter.Null);
        }
    }
}
