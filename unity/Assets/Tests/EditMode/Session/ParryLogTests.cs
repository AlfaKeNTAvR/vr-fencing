// What Parry Drills (PER-97) add to the rep log: the Parry's line and the time from the Parry to the Riposte, and
// (PER-98) where an unparried point landed on the Fencer and which body judged it.
// Lines written before these existed still read, with the new fields empty.
using System.IO;
using System.Linq;
using NUnit.Framework;
using VrFencing.Drills;
using VrFencing.Dummy;
using VrFencing.Session;
using static VrFencing.Tests.Session.SessionTestData;

namespace VrFencing.Tests.Session
{
    public class ParryLogTests
    {
        private ScratchFolder _folder;

        [SetUp]
        public void CreateScratchFolder()
        {
            _folder = new ScratchFolder();
        }

        [TearDown]
        public void DeleteScratchFolder()
        {
            _folder.Dispose();
        }

        [Test]
        public void AParryRepReadsBackWithItsLineAndRiposteTime()
        {
            var path = _folder.File(RepLogFile.FileName);
            var result = new RepResult(
                DrillCatalog.ArmedDummyTestDrillId, 2, TargetZone.Body, RepOutcome.Success, TargetZone.Body, 1.9, 2.1f, 0.04f, isShrunk: false,
                dummyDistance: DummyDistance.Extension, parryLine: ParryLine.Six, riposteSeconds: 0.62);

            RepLogFile.Append(path, RepLogEntry.FromRepResult(result, Noon, SessionId, flagged: false, armPose: null).WithFlagged(true));
            var entry = RepLogFile.Read(path).Items.Single();

            Assert.That(entry.ParryLine, Is.EqualTo(ParryLine.Six));
            Assert.That(entry.RiposteSeconds, Is.EqualTo(0.62));
            Assert.That(entry.Flagged, Is.True, "Flagging keeps the Parry fields.");
            Assert.That(File.ReadAllText(path), Does.Contain("\"parryLine\":\"Six\"").And.Contain("\"riposteSeconds\":0.62"));
        }

        [Test]
        public void AHitOnTheFencerLogsItsPartAndTheBodyThatJudgedIt()
        {
            var path = _folder.File(RepLogFile.FileName);
            var hit = new FencerHit(FencerHitSource.Iobt, BodyPart.Torso, new System.Numerics.Vector3(0.0f, 1.1f, -0.1f));
            var result = new RepResult(
                DrillCatalog.ArmedDummyTestDrillId, 1, TargetZone.Body, RepOutcome.NotParried, null, null, null, null, isShrunk: false, fencerHit: hit);

            RepLogFile.Append(path, RepLogEntry.FromRepResult(result, Noon, SessionId, flagged: false, armPose: null).WithFlagged(true));
            var entry = RepLogFile.Read(path).Items.Single();

            Assert.That(entry.HitSource, Is.EqualTo(FencerHitSource.Iobt));
            Assert.That(entry.HitPart, Is.EqualTo(BodyPart.Torso));
            Assert.That(File.ReadAllText(path), Does.Contain("\"hitSource\":\"Iobt\"").And.Contain("\"hitPart\":\"Torso\""));
        }

        [Test]
        public void AThrustThatEndsShortAndAnOlderLineHaveNoHit()
        {
            var path = _folder.File(RepLogFile.FileName);
            var result = new RepResult(
                DrillCatalog.ArmedDummyTestDrillId, 0, TargetZone.Body, RepOutcome.NotParried, null, null, null, null, isShrunk: false);
            RepLogFile.Append(path, RepLogEntry.FromRepResult(result, Noon, SessionId, flagged: false, armPose: null));
            File.AppendAllText(path, "{\"schema\":1,\"timestamp\":\"2026-10-19T12:00:00+00:00\",\"sessionId\":\"s\",\"drillId\":\"d\",\"repIndex\":0,"
                                     + "\"targetZone\":\"Hand\",\"result\":\"Success\",\"isShrunk\":false,\"flagged\":false,\"armPose\":null}\n");

            var entries = RepLogFile.Read(path).Items.ToList();

            Assert.That(entries.All(entry => entry.HitSource == null && entry.HitPart == null), Is.True);
            Assert.That(File.ReadAllText(path), Does.Contain("\"hitSource\":null"));
        }

        [Test]
        public void AnUnparriedRepAndAnOlderLineHaveNoParry()
        {
            var path = _folder.File(RepLogFile.FileName);
            var result = new RepResult(
                DrillCatalog.ArmedDummyTestDrillId, 0, TargetZone.Body, RepOutcome.NotParried, null, null, null, null, isShrunk: false);
            RepLogFile.Append(path, RepLogEntry.FromRepResult(result, Noon, SessionId, flagged: false, armPose: null));
            File.AppendAllText(path, "{\"schema\":1,\"timestamp\":\"2026-10-19T12:00:00+00:00\",\"sessionId\":\"s\",\"drillId\":\"d\",\"repIndex\":0,"
                                     + "\"targetZone\":\"Hand\",\"result\":\"Success\",\"isShrunk\":false,\"flagged\":false,\"armPose\":null}\n");

            var entries = RepLogFile.Read(path).Items.ToList();

            Assert.That(entries[0].Result, Is.EqualTo(RepOutcome.NotParried));
            Assert.That(entries.All(entry => entry.ParryLine == null && entry.RiposteSeconds == null), Is.True);
            Assert.That(File.ReadAllText(path), Does.Contain("\"parryLine\":null"));
        }
    }
}
