using System;
using System.IO;

namespace VrFencing.Blade
{
    /// <summary>
    /// Builds one CSV row in a reused character buffer, with invariant number formatting and no allocation per row
    /// (PER-136: a row every rendered frame on the headset). Numbers are written fixed-point with a given count of
    /// decimals; NaN or infinity is an empty cell. Cells are separated by commas; text cells must not hold commas.
    /// </summary>
    public sealed class CsvRowBuilder
    {
        // Past this a fixed-point number would overflow a long: written as an empty cell, as it is no measurement.
        private const double MaxMagnitude = 1e15;

        private const int MaxDecimals = 9;

        private static readonly long[] PowersOfTen =
        {
            1L, 10L, 100L, 1_000L, 10_000L, 100_000L, 1_000_000L, 10_000_000L, 100_000_000L, 1_000_000_000L,
        };

        private readonly char[] _digits = new char[20];
        private char[] _buffer;
        private int _length;
        private bool _isRowStarted;

        public CsvRowBuilder(int capacity = 1024)
        {
            _buffer = new char[Math.Max(16, capacity)];
        }

        /// <summary>Characters in the row so far.</summary>
        public int Length => _length;

        /// <summary>Starts a new, empty row.</summary>
        public void Clear()
        {
            _length = 0;
            _isRowStarted = false;
        }

        /// <summary>A number with <paramref name="decimals"/> digits after the point (0 to 9), rounded half away from zero.</summary>
        public void Append(double value, int decimals)
        {
            StartCell();
            AppendNumber(value, decimals);
        }

        /// <summary>A whole number.</summary>
        public void Append(int value)
        {
            StartCell();
            AppendNumber(value, 0);
        }

        /// <summary>1 for true, 0 for false.</summary>
        public void Append(bool value)
        {
            StartCell();
            AppendChar(value ? '1' : '0');
        }

        /// <summary>Text as it is (null is an empty cell); it must hold no comma or line break.</summary>
        public void Append(string text)
        {
            StartCell();
            AppendText(text);
        }

        /// <summary>An empty cell.</summary>
        public void AppendEmpty()
        {
            StartCell();
        }

        /// <summary>
        /// Several names in one cell joined by '|', one for each bit set in <paramref name="flags"/> (bit i named
        /// <paramref name="names"/>[i]); an empty cell when none is set.
        /// </summary>
        public void AppendFlags(int flags, string[] names)
        {
            if (names == null)
            {
                throw new ArgumentNullException(nameof(names));
            }

            StartCell();
            var isFirst = true;
            for (var bit = 0; bit < names.Length; bit++)
            {
                if ((flags & (1 << bit)) == 0)
                {
                    continue;
                }

                if (!isFirst)
                {
                    AppendChar('|');
                }

                AppendText(names[bit]);
                isFirst = false;
            }
        }

        /// <summary>Writes the row and a line break to <paramref name="writer"/> (buffered by the writer, no allocation).</summary>
        public void WriteLineTo(TextWriter writer)
        {
            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            AppendChar('\n');
            writer.Write(_buffer, 0, _length);
            _length--;
        }

        public override string ToString()
        {
            return new string(_buffer, 0, _length);
        }

        private void StartCell()
        {
            if (_isRowStarted)
            {
                AppendChar(',');
            }

            _isRowStarted = true;
        }

        private void AppendNumber(double value, int decimals)
        {
            if (decimals < 0 || decimals > MaxDecimals)
            {
                throw new ArgumentOutOfRangeException(nameof(decimals), decimals, "From 0 to 9 decimals.");
            }

            if (double.IsNaN(value) || double.IsInfinity(value) || Math.Abs(value) >= MaxMagnitude)
            {
                return;
            }

            var scale = PowersOfTen[decimals];
            var scaled = (long)Math.Round(Math.Abs(value) * scale, MidpointRounding.AwayFromZero);
            if (value < 0.0 && scaled != 0)
            {
                AppendChar('-');
            }

            AppendWhole(scaled / scale);
            if (decimals == 0)
            {
                return;
            }

            AppendChar('.');
            var fraction = scaled % scale;
            for (var place = decimals - 1; place >= 0; place--)
            {
                AppendChar((char)('0' + (fraction / PowersOfTen[place] % 10)));
            }
        }

        private void AppendWhole(long whole)
        {
            var count = 0;
            do
            {
                _digits[count++] = (char)('0' + (whole % 10));
                whole /= 10;
            }
            while (whole > 0);

            while (count > 0)
            {
                AppendChar(_digits[--count]);
            }
        }

        private void AppendText(string text)
        {
            if (text == null)
            {
                return;
            }

            for (var index = 0; index < text.Length; index++)
            {
                AppendChar(text[index]);
            }
        }

        private void AppendChar(char character)
        {
            if (_length == _buffer.Length)
            {
                // Only while the first rows find the row's size: then the buffer stays.
                Array.Resize(ref _buffer, _buffer.Length * 2);
            }

            _buffer[_length++] = character;
        }
    }
}
