// Strips saved per room (PER-31, PER-91): which saved Strip is this room's, how re-placing replaces it and placing
// in a new room adds one, the JSON file (old files included), and the off-strip tolerance steps.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Settings;
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
        public void AStripFoundInTheFencersRoomComesBackWithoutAsking()
        {
            var upstairs = CreateSavedStrip(2.5f);
            var downstairs = CreateSavedStrip(4.0f);
            var saved = new SavedStrips(new[] { upstairs, downstairs });

            // Both floors share one map, so both anchors are found; the Fencer stands downstairs.
            var found = new Dictionary<Guid, Vector3>
            {
                [upstairs.AnchorId] = new Vector3(1.0f, 2.8f, 0.5f),
                [downstairs.AnchorId] = new Vector3(2.0f, 0.0f, -1.0f),
            };
            var choice = saved.ChooseForRoom(found, new Vector3(1.5f, 0.0f, 0.0f));

            Assert.That(choice.Match, Is.EqualTo(StripRoomMatch.ThisRoom));
            Assert.That(choice.Strip, Is.SameAs(downstairs));
        }

        [Test]
        public void OfSeveralStripsInTheRoomTheNearestWins()
        {
            var nextDoor = CreateSavedStrip(3.0f);
            var here = CreateSavedStrip(3.5f);
            var saved = new SavedStrips(new[] { nextDoor, here });

            var found = new Dictionary<Guid, Vector3>
            {
                [nextDoor.AnchorId] = new Vector3(6.0f, 0.0f, 0.0f),
                [here.AnchorId] = new Vector3(0.5f, 0.0f, 0.5f),
            };

            Assert.That(saved.ChooseForRoom(found, Vector3.Zero).Strip, Is.SameAs(here));
        }

        [Test]
        public void StripsFoundOnlyInOtherRoomsOfferTheNewestAsTheLastStrip()
        {
            var older = CreateSavedStrip(2.5f);
            var newest = CreateSavedStrip(4.0f);
            var saved = new SavedStrips(new[] { newest, older });

            var found = new Dictionary<Guid, Vector3>
            {
                [older.AnchorId] = new Vector3(0.0f, -2.7f, 0.0f),
                [newest.AnchorId] = new Vector3(12.0f, 0.0f, 0.0f),
            };
            var choice = saved.ChooseForRoom(found, Vector3.Zero);

            Assert.That(choice.Match, Is.EqualTo(StripRoomMatch.OtherRoom));
            Assert.That(choice.Strip, Is.SameAs(newest));
        }

        [Test]
        public void NothingIsChosenWhenNoAnchorIsFound()
        {
            var saved = new SavedStrips(new[] { CreateSavedStrip(3.0f) });

            Assert.That(saved.ChooseForRoom(new Dictionary<Guid, Vector3>(), Vector3.Zero).Match, Is.EqualTo(StripRoomMatch.NotFound));
            Assert.That(saved.ChooseForRoom(new Dictionary<Guid, Vector3> { [Guid.NewGuid()] = Vector3.Zero }, Vector3.Zero).Strip, Is.Null);
            Assert.That(SavedStrips.Empty.ChooseForRoom(new Dictionary<Guid, Vector3>(), Vector3.Zero).Match, Is.EqualTo(StripRoomMatch.NotFound));
        }

        [TestCase(9.9f, 0.0f, true)]
        [TestCase(10.1f, 0.0f, false)]
        [TestCase(0.0f, 1.9f, true)]
        [TestCase(0.0f, 2.1f, false)]
        [TestCase(0.0f, -2.1f, false)]
        public void AnAnchorFarAlongTheFloorOrOnAnotherFloorIsAnotherRoom(float alongFloorMeters, float heightMeters, bool expectedInRoom)
        {
            var fencerOnFloor = new Vector3(1.0f, 0.0f, 1.0f);
            var anchor = new Vector3(1.0f + (alongFloorMeters * 0.6f), heightMeters, 1.0f + (alongFloorMeters * 0.8f));

            Assert.That(StripRoomCheck.IsInFencersRoom(anchor, fencerOnFloor), Is.EqualTo(expectedInRoom));
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
            Assert.That(loaded.Strips[0].Layout, Is.EqualTo(SavedStripLayout.EnGardeLines));
        }

        [Test]
        public void AFileSavedBeforeTheTwoLinePlacementStillLoads()
        {
            // As the headset wrote it before PER-90: the second point was the far end, and there is no layout field.
            File.WriteAllText(_path, @"{
  ""Strips"": [
    {
      ""AnchorId"": ""5b1c3a2e-7d4f-4e8a-9b6c-0123456789ab"",
      ""PlacedLengthMeters"": 4.45,
      ""SavedAtUtc"": ""2026-10-19T18:23:00Z""
    }
  ]
}");

            var loaded = SavedStrips.Load(_path).Strips.Single();
            var zones = new WarningZoneSettings();
            var geometry = StripGeometry.Create(loaded.GetEnGardeGapMeters(1.65f, zones), 1.65f, zones);

            Assert.That(loaded.AnchorId, Is.EqualTo(new Guid("5b1c3a2e-7d4f-4e8a-9b6c-0123456789ab")));
            Assert.That(loaded.Layout, Is.EqualTo(SavedStripLayout.FarEnd));
            Assert.That(loaded.SavedAtUtc, Is.EqualTo(new DateTime(2026, 10, 19, 18, 23, 0, DateTimeKind.Utc)));
            Assert.That(geometry.FarEndZ, Is.EqualTo(4.45f).Within(1e-3f), "The old Strip keeps its far end in the room.");
        }

        [Test]
        public void ANewStripsGapIsUsedAsSaved()
        {
            var strip = CreateSavedStrip(3.2f);

            Assert.That(strip.GetEnGardeGapMeters(1.65f, new WarningZoneSettings()), Is.EqualTo(3.2f));
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
            return new SavedStrip(Guid.NewGuid(), placedLengthMeters, SavedAt, SavedStripLayout.EnGardeLines);
        }
    }
}
