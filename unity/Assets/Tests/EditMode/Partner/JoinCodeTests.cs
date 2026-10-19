// The join code and the Partner's keypad (PER-80): codes are 6 letters or digits, case-insensitive, and the keypad
// only lets a full code be joined.
using NUnit.Framework;
using VrFencing.Partner;

namespace VrFencing.Tests.Partner
{
    public class JoinCodeTests
    {
        [TestCase("FGR78N", "FGR78N")]
        [TestCase("fgr78n", "FGR78N")]
        [TestCase(" fgr-78n ", "FGR78N")]
        [TestCase("FG R7 8N", "FGR78N")]
        [TestCase(null, "")]
        public void NormalizeUpperCasesAndDropsSpacesAndDashes(string typed, string expected)
        {
            Assert.That(JoinCode.Normalize(typed), Is.EqualTo(expected));
        }

        [TestCase("FGR78N", true)]
        [TestCase("7QJ9KW", true)]
        [TestCase("FGR78", false)]
        [TestCase("FGR78NB", false)]
        [TestCase("FGR78!", false)]
        [TestCase("ZZZZZZ", false)]
        [TestCase("ABC123", false)]
        [TestCase("fgr78n", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void IsValidOnlyForSixKeypadCharacters(string code, bool expected)
        {
            Assert.That(JoinCode.IsValid(code), Is.EqualTo(expected));
        }

        [Test]
        public void TryParseReturnsTheNormalizedCode()
        {
            Assert.That(JoinCode.TryParse("fgr-78n", out var problem), Is.EqualTo("FGR78N"));
            Assert.That(problem, Is.Empty);
        }

        [TestCase("", "Type the host's code")]
        [TestCase("BCD", "A code has 6 characters; this has 3")]
        [TestCase("FGR78?", "A code has only the characters 6789BCDFGHJKLMNPQRTW")]
        [TestCase("FGR780", "A code has only the characters 6789BCDFGHJKLMNPQRTW")]
        public void TryParseSaysWhyACodeCannotBeJoined(string typed, string expectedProblem)
        {
            Assert.That(JoinCode.TryParse(typed, out var problem), Is.Null);
            Assert.That(problem, Is.EqualTo(expectedProblem));
        }

        [Test]
        public void ACodeIsReadOutInTwoHalves()
        {
            Assert.That(JoinCode.FormatForReading("FGR78N"), Is.EqualTo("FGR 78N"));
            Assert.That(JoinCode.FormatForReading("FGR"), Is.EqualTo("FGR"));
        }
    }

    public class CodeKeypadTests
    {
        [Test]
        public void KeysAreTypedUpToSixCharacters()
        {
            var keypad = new CodeKeypad();
            foreach (var key in "fgr78nb")
            {
                keypad.Press(key);
            }

            Assert.That(keypad.Typed, Is.EqualTo("FGR78N"));
            Assert.That(keypad.IsComplete, Is.True);
        }

        [Test]
        public void ANonKeyIsIgnored()
        {
            var keypad = new CodeKeypad();
            Assert.That(keypad.Press('?'), Is.False);
            Assert.That(keypad.Press('A'), Is.False, "No code holds a vowel.");
            Assert.That(keypad.Typed, Is.Empty);
        }

        [Test]
        public void BackspaceRemovesTheLastCharacterAndClearRemovesAll()
        {
            var keypad = new CodeKeypad();
            keypad.Press('B');
            keypad.Press('C');
            keypad.Backspace();
            Assert.That(keypad.Typed, Is.EqualTo("B"));

            keypad.Clear();
            keypad.Backspace();
            Assert.That(keypad.Typed, Is.Empty);
            Assert.That(keypad.IsComplete, Is.False);
        }

        [Test]
        public void TheDisplayShowsTheMissingPlaces()
        {
            var keypad = new CodeKeypad();
            keypad.Press('F');
            keypad.Press('7');
            Assert.That(keypad.FormatDisplay(), Is.EqualTo("F 7 _ _ _ _"));
        }

        [Test]
        public void TheRowsHoldEveryKeyOnce()
        {
            var rows = CodeKeypad.GetRows();
            Assert.That(rows, Is.EqualTo(new[] { "6789BCD", "FGHJKLM", "NPQRTW" }));
            Assert.That(string.Concat(rows), Is.EqualTo(JoinCode.Alphabet));
        }
    }
}
