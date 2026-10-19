// The Drill picker's rows (PER-36): easy to hard, greyed out with the needed Strip length when a Drill doesn't fit.
using System.Linq;
using NUnit.Framework;
using VrFencing.Drills;
using VrFencing.Dummy;
using VrFencing.UI;

namespace VrFencing.Tests.UI
{
    public class DrillPickerTests
    {
        private static DrillDefinition Drill(string id, int order, float requiredLength, float? shrunkLength = null)
        {
            return new DrillDefinition(id, "Drill " + id, order, requiredLength, shrunkLength, repCount: 5, cueSeconds: 0.5f,
                windowSeconds: 1.5f, restSeconds: 2.0f, CueType.NamedTargetZone, SuccessRule.PointFirstTouchOnTargetZone,
                new[] { TargetZone.Body });
        }

        [Test]
        public void DrillsAreListedEasyToHard()
        {
            var drills = new[] { Drill("c", 3, 2.0f), Drill("a", 1, 2.0f), Drill("b", 2, 2.0f) };

            var entries = DrillPicker.BuildEntries(drills, 5.0f, shrinkDrillsToFit: false);

            Assert.That(entries.Select(entry => entry.Drill.Id), Is.EqualTo(new[] { "a", "b", "c" }));
        }

        [Test]
        public void ADrillThatDoesNotFitIsGreyedOutWithTheLengthItNeeds()
        {
            var entries = DrillPicker.BuildEntries(new[] { Drill("long", 1, 6.0f), Drill("short", 2, 2.0f) }, 4.2f, false);

            Assert.That(entries[0].IsAvailable, Is.False);
            Assert.That(entries[0].Note, Is.EqualTo("Needs a 6.0 m Strip"));
            Assert.That(entries[1].IsAvailable, Is.True);
            Assert.That(entries[1].Note, Is.Empty);
        }

        [Test]
        public void InShrinkModeAShortenedDrillFitsAndSaysSo()
        {
            var entries = DrillPicker.BuildEntries(new[] { Drill("long", 1, 6.0f, shrunkLength: 3.0f) }, 4.2f, shrinkDrillsToFit: true);

            Assert.That(entries[0].IsAvailable, Is.True);
            Assert.That(entries[0].IsShrunk, Is.True);
            Assert.That(entries[0].Note, Is.EqualTo("Shortened to fit"));
        }

        [Test]
        public void InShrinkModeTheShortenedLengthIsTheOneShownWhenEvenThatDoesNotFit()
        {
            var entries = DrillPicker.BuildEntries(new[] { Drill("long", 1, 6.0f, shrunkLength: 3.0f) }, 2.5f, shrinkDrillsToFit: true);

            Assert.That(entries[0].IsAvailable, Is.False);
            Assert.That(entries[0].Note, Is.EqualTo("Needs a 3.0 m Strip"));
        }

        [Test]
        public void WithoutAStripEveryDrillIsGreyedOut()
        {
            var entries = DrillPicker.BuildEntries(new[] { Drill("a", 1, 2.0f) }, stripLengthMeters: null, false);

            Assert.That(entries[0].IsAvailable, Is.False);
            Assert.That(entries[0].Note, Is.EqualTo(DrillPicker.NoStripNote));
        }

        [Test]
        public void NoRegisteredDrillsGiveAnEmptyPicker()
        {
            Assert.That(DrillPicker.BuildEntries(new DrillDefinition[0], 5.0f, false), Is.Empty);
        }

        [Test]
        public void SelectionStaysOnAnAvailableDrillAndOtherwiseMovesToTheFirstAvailable()
        {
            var entries = DrillPicker.BuildEntries(new[] { Drill("long", 1, 6.0f), Drill("a", 2, 2.0f), Drill("b", 3, 2.0f) }, 4.0f, false);

            Assert.That(DrillPicker.ResolveSelection(entries, "b"), Is.EqualTo("b"));
            Assert.That(DrillPicker.ResolveSelection(entries, "long"), Is.EqualTo("a"), "A greyed-out Drill cannot stay selected.");
            Assert.That(DrillPicker.ResolveSelection(entries, null), Is.EqualTo("a"));
            Assert.That(DrillPicker.ResolveSelection(DrillPicker.BuildEntries(new[] { Drill("long", 1, 6.0f) }, 4.0f, false), "long"), Is.Null);
        }
    }
}
