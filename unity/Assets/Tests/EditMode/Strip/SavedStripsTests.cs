// Strips saved per room (PER-31): which saved Strip is restored, how re-placing replaces it, the JSON file,
// and the off-strip tolerance steps the Fencer cycles through on the headset.
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VrFencing.Strip;

namespace VrFencing.Tests.Strip
{
    public class SavedStripsTests
    {
        private static readonly DateTime SavedAt = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

        private string _directory;
        private string _path;

        [SetUp]
        public void CreateScratchDirectory()
        {
            _directory = Path.Combine(Path.GetTempPath(), "vrfencing-strip-tests-" + Path.GetRandomFileName());
            Directory.CreateDirectory(_directory);
            _path = Path.Combine(_directory, SavedStrips.FileName);
        }

        [TearDown]
        public void DeleteScratchDirectory()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        [Test]
        public void RestoresTheNewestStripWhoseAnchorIsFoundInThisRoom()
        {
            var bedroom = CreateSavedStrip(2.5f);
            var livingRoomOld = CreateSavedStrip(4.0f);
            var livingRoomNew = CreateSavedStrip(5.0f);
            var saved = new SavedStrips(new[] { livingRoomNew, bedroom, livingRoomOld });

            Assert.That(saved.ChooseRestored(new[] { livingRoomOld.AnchorId, livingRoomNew.AnchorId }), Is.SameAs(livingRoomNew));
            Assert.That(saved.ChooseRestored(new[] { bedroom.AnchorId }), Is.SameAs(bedroom));
        }

        [Test]
        public void NothingIsRestoredWhenNoAnchorIsFound()
        {
            var saved = new SavedStrips(new[] { CreateSavedStrip(3.0f) });

            Assert.That(saved.ChooseRestored(Array.Empty<Guid>()), Is.Null);
            Assert.That(saved.ChooseRestored(new[] { Guid.NewGuid() }), Is.Null);
            Assert.That(SavedStrips.Empty.ChooseRestored(new[] { Guid.NewGuid() }), Is.Null);
        }

        [Test]
        public void ReplacingThisRoomsStripKeepsTheOtherRooms()
        {
            var bedroom = CreateSavedStrip(2.5f);
            var livingRoom = CreateSavedStrip(4.0f);
            var replacement = CreateSavedStrip(4.5f);

            var saved = new SavedStrips(new[] { livingRoom, bedroom }).WithSaved(replacement, livingRoom.AnchorId);

            Assert.That(saved.Strips, Is.EqualTo(new[] { replacement, bedroom }));
        }

        [Test]
        public void AStripPlacedInANewRoomIsAddedAsTheNewest()
        {
            var bedroom = CreateSavedStrip(2.5f);
            var lab = CreateSavedStrip(6.0f);

            var saved = new SavedStrips(new[] { bedroom }).WithSaved(lab, replacedAnchorId: null);

            Assert.That(saved.Strips, Is.EqualTo(new[] { lab, bedroom }));
        }

        [Test]
        public void OnlyTheNewestRoomsAreRemembered()
        {
            var saved = SavedStrips.Empty;
            for (var index = 0; index < SavedStrips.MaxSavedStrips + 3; index++)
            {
                saved = saved.WithSaved(CreateSavedStrip(3.0f + index), replacedAnchorId: null);
            }

            Assert.That(saved.Strips, Has.Count.EqualTo(SavedStrips.MaxSavedStrips));
            Assert.That(saved.Strips[0].PlacedLengthMeters, Is.EqualTo(3.0f + SavedStrips.MaxSavedStrips + 2));
        }

        [Test]
        public void SavesAndLoadsTheFile()
        {
            var original = new SavedStrips(new[] { CreateSavedStrip(3.25f), CreateSavedStrip(2.5f) });

            original.Save(_path);
            var loaded = SavedStrips.Load(_path);

            Assert.That(loaded.Strips.Select(strip => strip.AnchorId), Is.EqualTo(original.Strips.Select(strip => strip.AnchorId)));
            Assert.That(loaded.Strips[0].PlacedLengthMeters, Is.EqualTo(3.25f));
            Assert.That(loaded.Strips[0].SavedAtUtc, Is.EqualTo(SavedAt));
            Assert.That(loaded.Strips[0].SavedAtUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
        }

        [Test]
        public void MissingFileMeansNoSavedStrips()
        {
            Assert.That(SavedStrips.Load(_path).Strips, Is.Empty);
        }

        [Test]
        public void BrokenFileIsReported()
        {
            File.WriteAllText(_path, "{ not json");

            Assert.Throws<SavedStripsFormatException>(() => SavedStrips.Load(_path));
        }

        [TestCase(0.0f, 0.2f)]
        [TestCase(0.2f, 0.4f)]
        [TestCase(0.4f, 0.0f)]
        [TestCase(0.1f, 0.2f)]
        [TestCase(0.75f, 0.0f)]
        public void ToleranceButtonCyclesZeroPointTwoPointFour(float currentMeters, float expectedMeters)
        {
            Assert.That(OffStripToleranceSteps.GetNext(currentMeters), Is.EqualTo(expectedMeters));
        }

        private static SavedStrip CreateSavedStrip(float placedLengthMeters)
        {
            return new SavedStrip(Guid.NewGuid(), placedLengthMeters, SavedAt);
        }
    }
}
