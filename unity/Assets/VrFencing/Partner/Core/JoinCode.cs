using System;
using System.Text;

namespace VrFencing.Partner
{
    /// <summary>
    /// The 6-character code a Host's session is joined by (PER-58): shown on the Host's Partner page, read out over
    /// voice and typed on the Partner's keypad. Unity Sessions codes use only the 20 characters of
    /// <see cref="Alphabet"/> (no vowels, no 0/O or 1/I to confuse; the Lobby service refuses any other character, seen
    /// in the online test). They are case-insensitive, so anything typed is upper-cased, and spaces or dashes a person
    /// adds are dropped.
    /// </summary>
    public static class JoinCode
    {
        public const int Length = 6;

        /// <summary>Every character a code can hold, so every key the keypad offers: four digits, then sixteen letters.</summary>
        public const string Alphabet = "6789BCDFGHJKLMNPQRTW";

        /// <summary>Upper-cases and drops spaces and dashes; other characters are kept so <see cref="IsValid"/> catches them.</summary>
        public static string Normalize(string typed)
        {
            if (string.IsNullOrEmpty(typed))
            {
                return string.Empty;
            }

            var normalized = new StringBuilder(typed.Length);
            foreach (var character in typed)
            {
                if (char.IsWhiteSpace(character) || character == '-')
                {
                    continue;
                }

                normalized.Append(char.ToUpperInvariant(character));
            }

            return normalized.ToString();
        }

        /// <summary>A normalized code of exactly <see cref="Length"/> keypad characters.</summary>
        public static bool IsValid(string code)
        {
            return code != null && code.Length == Length && IsAllKeys(code);
        }

        /// <summary>Normalizes <paramref name="typed"/>; returns null and why when it is not a code that can be joined.</summary>
        public static string TryParse(string typed, out string problem)
        {
            var code = Normalize(typed);
            if (code.Length == 0)
            {
                problem = "Type the host's code";
            }
            else if (!IsAllKeys(code))
            {
                problem = $"A code has only the characters {Alphabet}";
            }
            else if (code.Length != Length)
            {
                problem = $"A code has {Length} characters; this has {code.Length}";
            }
            else
            {
                problem = string.Empty;
                return code;
            }

            return null;
        }

        /// <summary>A character on the keypad (upper case).</summary>
        public static bool IsKey(char character)
        {
            return Alphabet.IndexOf(character) >= 0;
        }

        /// <summary>The code split in two halves for reading out ("FGR 78N"), or the text as it is if not a full code.</summary>
        public static string FormatForReading(string code)
        {
            if (code == null || code.Length != Length)
            {
                return code ?? string.Empty;
            }

            const int half = Length / 2;
            return code.Substring(0, half) + " " + code.Substring(half);
        }

        private static bool IsAllKeys(string code)
        {
            foreach (var character in code)
            {
                if (!IsKey(character))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// The Partner's on-screen keypad (PER-58): one key per <see cref="JoinCode.Alphabet"/> character, backspace and
    /// clear. It takes at most <see cref="JoinCode.Length"/> characters, and Join is on only once the code is complete.
    /// </summary>
    public sealed class CodeKeypad
    {
        private readonly StringBuilder _typed = new StringBuilder(JoinCode.Length);

        /// <summary>What is typed so far, upper case.</summary>
        public string Typed => _typed.ToString();

        public bool IsComplete => _typed.Length == JoinCode.Length;

        /// <summary>Adds one key; ignored when the code is already complete or the character is not a key.</summary>
        /// <returns>Whether it was added.</returns>
        public bool Press(char key)
        {
            var upper = char.ToUpperInvariant(key);
            if (IsComplete || !JoinCode.IsKey(upper))
            {
                return false;
            }

            _typed.Append(upper);
            return true;
        }

        /// <summary>Removes the last character, if any.</summary>
        public void Backspace()
        {
            if (_typed.Length > 0)
            {
                _typed.Length -= 1;
            }
        }

        public void Clear()
        {
            _typed.Clear();
        }

        /// <summary>
        /// The typed code with the missing places as underscores, spaced for reading in the panel's big display:
        /// "A B C _ _ _".
        /// </summary>
        public string FormatDisplay()
        {
            var display = new StringBuilder((JoinCode.Length * 2) - 1);
            for (var index = 0; index < JoinCode.Length; index++)
            {
                if (index > 0)
                {
                    display.Append(' ');
                }

                display.Append(index < _typed.Length ? _typed[index] : '_');
            }

            return display.ToString();
        }

        /// <summary>The keypad's rows, in alphabet order, seven keys to a row (3 rows of large keys).</summary>
        public static string[] GetRows()
        {
            const int keysPerRow = 7;
            var rowCount = (JoinCode.Alphabet.Length + keysPerRow - 1) / keysPerRow;
            var rows = new string[rowCount];
            for (var row = 0; row < rowCount; row++)
            {
                var start = row * keysPerRow;
                rows[row] = JoinCode.Alphabet.Substring(start, Math.Min(keysPerRow, JoinCode.Alphabet.Length - start));
            }

            return rows;
        }
    }
}
