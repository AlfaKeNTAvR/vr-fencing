// Numbering the Paired Practice tallies in the Session log (PER-84): an interrupted tally keeps its number when a rejoin
// carries it on, and a tally that ended for good moves on to the next.
using NUnit.Framework;
using VrFencing.Partner;
using VrFencing.Session;

namespace VrFencing.Tests.Partner
{
    public class TallyNumberingTests
    {
        private static readonly PairedPracticeCounts One = new PairedPracticeCounts(1, 0, 0);

        private static EndedTally Ended(PairedPracticeEnd end)
        {
            return new EndedTally(end, One, default);
        }

        [Test]
        public void TalliesThatEndAreNumberedInTurn()
        {
            var numbering = new TallyNumbering();

            Assert.That(numbering.Number(Ended(PairedPracticeEnd.Reset)), Is.EqualTo(1));
            Assert.That(numbering.Number(Ended(PairedPracticeEnd.SwitchedToBout)), Is.EqualTo(2));
            Assert.That(numbering.Number(Ended(PairedPracticeEnd.Disconnected)), Is.EqualTo(3));
            Assert.That(numbering.Current, Is.EqualTo(4));
        }

        [Test]
        public void AnInterruptedTallyCarriedOnByARejoinKeepsItsNumber()
        {
            var numbering = new TallyNumbering();

            Assert.That(numbering.Number(Ended(PairedPracticeEnd.Interrupted)), Is.EqualTo(1));
            numbering.OnReconnected(isTallyEmpty: false);

            Assert.That(numbering.Number(Ended(PairedPracticeEnd.Reset)), Is.EqualTo(1), "The whole tally, saved again under its number.");
            Assert.That(numbering.Current, Is.EqualTo(2));
        }

        [Test]
        public void AnEmptyTallyAfterARejoinIsANewOne()
        {
            var numbering = new TallyNumbering();
            numbering.Number(Ended(PairedPracticeEnd.Interrupted));

            numbering.OnReconnected(isTallyEmpty: true);

            Assert.That(numbering.Number(Ended(PairedPracticeEnd.Reset)), Is.EqualTo(2), "The Host ended the old tally while away.");
        }

        [Test]
        public void AReconnectWithNothingInterruptedChangesNothing()
        {
            var numbering = new TallyNumbering();

            numbering.OnReconnected(isTallyEmpty: true);

            Assert.That(numbering.Current, Is.EqualTo(1));
        }
    }
}
