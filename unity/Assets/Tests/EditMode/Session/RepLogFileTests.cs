// The JSONL rep log (PER-35): one line per Rep, read back field for field, and robust to
// a missing file, broken lines and a line cut short when the app was killed mid-write.
using System;
using System.IO;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Drills;
using VrFencing.Dummy;
using VrFencing.Session;
using VrFencing.Settings;
using static VrFencing.Tests.Session.SessionTestData;

namespace VrFencing.Tests.Session
{
    public class RepLogFileTests
    {
        private ScratchFolder _folder;
        private string _logPath;

        [SetUp]
        public void CreateScratchFolder()
        {
            _folder = new ScratchFolder();
            _logPath = _folder.File(RepLogFile.FileName);
        }

        [TearDown]
        public void DeleteScratchFolder()
        {
            _folder.Dispose();
        }

        private static RepLogEntry FullEntry()
        {
            var armPose = new ArmPoseSnapshot(
                shoulder: new TrackedPose(new Vector3(0.2f, 1.4f, 0.0f), Quaternion.Identity),
                elbow: new TrackedPose(new Vector3(0.25f, 1.3f, 0.3f), new Quaternion(0.0f, 0.70710677f, 0.0f, 0.70710677f)),
                wrist: null);
            return new RepLogEntry(
                new DateTimeOffset(2026, 10, 19, 12, 34, 56, 789, TimeSpan.Zero),
                "session-a",
                "reaction-lit-target",
                repIndex: 3,
                targetZone: TargetZone.Forearm,
                result: RepOutcome.WrongZone,
                contactZone: TargetZone.Wrist,
                reactionSeconds: 0.4321,
                tipSpeedMetersPerSecond: 2.75f,
                distanceFromZoneCenterMeters: 0.031f,
                isShrunk: true,
                flagged: true,
                armPose: armPose);
        }

        [Test]
        public void AppendedRepReadsBackFieldForField()
        {
            var written = FullEntry();

            RepLogFile.Append(_logPath, written);
            var read = RepLogFile.Read(_logPath);

            Assert.That(read.SkippedLineCount, Is.EqualTo(0));
            Assert.That(read.Items, Has.Count.EqualTo(1));
            var entry = read.Items[0];
            Assert.That(entry.Schema, Is.EqualTo(RepLogEntry.CurrentSchema));
            Assert.That(entry.Timestamp, Is.EqualTo(written.Timestamp));
            Assert.That(entry.SessionId, Is.EqualTo("session-a"));
            Assert.That(entry.DrillId, Is.EqualTo("reaction-lit-target"));
            Assert.That(entry.RepIndex, Is.EqualTo(3));
            Assert.That(entry.TargetZone, Is.EqualTo(TargetZone.Forearm));
            Assert.That(entry.Result, Is.EqualTo(RepOutcome.WrongZone));
            Assert.That(entry.ContactZone, Is.EqualTo(TargetZone.Wrist));
            Assert.That(entry.ReactionSeconds, Is.EqualTo(0.4321));
            Assert.That(entry.TipSpeedMetersPerSecond, Is.EqualTo(2.75f));
            Assert.That(entry.DistanceFromZoneCenterMeters, Is.EqualTo(0.031f));
            Assert.That(entry.IsShrunk, Is.True);
            Assert.That(entry.Flagged, Is.True);
            Assert.That(entry.ArmPose.Shoulder.Value.Position, Is.EqualTo(new Vector3(0.2f, 1.4f, 0.0f)));
            Assert.That(entry.ArmPose.Elbow.Value.Rotation, Is.EqualTo(new Quaternion(0.0f, 0.70710677f, 0.0f, 0.70710677f)));
            Assert.That(entry.ArmPose.Wrist, Is.Null);
        }

        [Test]
        public void TimeoutWithoutArmPoseReadsBackWithEmptyFields()
        {
            RepLogFile.Append(_logPath, Miss(TargetZone.Foot, RepOutcome.Timeout));

            var entry = RepLogFile.Read(_logPath).Items[0];

            Assert.That(entry.Result, Is.EqualTo(RepOutcome.Timeout));
            Assert.That(entry.ContactZone, Is.Null);
            Assert.That(entry.ReactionSeconds, Is.Null);
            Assert.That(entry.TipSpeedMetersPerSecond, Is.Null);
            Assert.That(entry.ArmPose, Is.Null);
        }

        [Test]
        public void EachRepIsOneLineWithTheDocumentedFieldNames()
        {
            RepLogFile.Append(_logPath, FullEntry());
            RepLogFile.Append(_logPath, Touch(TargetZone.Thigh, 0.5, 3.0f, repIndex: 4));

            var lines = File.ReadAllLines(_logPath);

            Assert.That(lines, Has.Length.EqualTo(2));
            foreach (var field in new[]
                     {
                         "\"schema\":1", "\"timestamp\":", "\"sessionId\":", "\"drillId\":", "\"repIndex\":", "\"targetZone\":\"Forearm\"",
                         "\"result\":\"WrongZone\"", "\"contactZone\":", "\"reactionSeconds\":", "\"tipSpeedMetersPerSecond\":",
                         "\"distanceFromZoneCenterMeters\":", "\"isShrunk\":true", "\"flagged\":true", "\"armPose\":",
                         "\"position\":[", "\"rotation\":[",
                     })
            {
                Assert.That(lines[0], Does.Contain(field));
            }

            Assert.That(lines[1], Does.Contain("\"armPose\":null"));
        }

        [Test]
        public void ArmPoseTrackingFieldsReadBack()
        {
            var armPose = new ArmPoseSnapshot(
                shoulder: null,
                elbow: null,
                wrist: new TrackedPose(new Vector3(0.3f, 1.3f, 0.6f), Quaternion.Identity),
                fidelity: BodyTrackingFidelity.Low,
                armExtendedBeforeLunge: true,
                elbowAngleAtLungeStartDegrees: 168.5f,
                lungeStartSecondsBeforeJudging: 0.42);
            var written = RepLogEntry.FromRepResult(
                new RepResult("lunge-to-target", 0, TargetZone.Body, RepOutcome.Success, TargetZone.Body, 0.9, 2.5f, 0.02f, isShrunk: false),
                Noon,
                "session-c",
                flagged: false,
                armPose);

            RepLogFile.Append(_logPath, written);
            var line = File.ReadAllLines(_logPath)[0];
            var read = RepLogFile.Read(_logPath).Items[0].ArmPose;

            Assert.That(line, Does.Contain("\"fidelity\":\"Low\""));
            Assert.That(line, Does.Contain("\"armExtendedBeforeLunge\":true"));
            Assert.That(read.Fidelity, Is.EqualTo(BodyTrackingFidelity.Low));
            Assert.That(read.ArmExtendedBeforeLunge, Is.True);
            Assert.That(read.ElbowAngleAtLungeStartDegrees, Is.EqualTo(168.5f));
            Assert.That(read.LungeStartSecondsBeforeJudging, Is.EqualTo(0.42));
            Assert.That(read.Wrist.Value.Position, Is.EqualTo(new Vector3(0.3f, 1.3f, 0.6f)));
        }

        [Test]
        public void ArmPoseWrittenBeforeTheTrackingFieldsReadsThemAsEmpty()
        {
            File.AppendAllText(_logPath, "{\"schema\":1,\"timestamp\":\"2026-10-19T12:00:00+00:00\",\"sessionId\":\"s\",\"drillId\":\"d\",\"repIndex\":0,"
                                         + "\"targetZone\":\"Hand\",\"result\":\"Success\",\"isShrunk\":false,\"flagged\":false,"
                                         + "\"armPose\":{\"shoulder\":null,\"elbow\":{\"position\":[0.1,1.2,0.3],\"rotation\":[0,0,0,1]},\"wrist\":null}}\n");

            var read = RepLogFile.Read(_logPath);

            Assert.That(read.SkippedLineCount, Is.EqualTo(0));
            var armPose = read.Items[0].ArmPose;
            Assert.That(armPose.Elbow.Value.Position, Is.EqualTo(new Vector3(0.1f, 1.2f, 0.3f)));
            Assert.That(armPose.Fidelity, Is.Null);
            Assert.That(armPose.ArmExtendedBeforeLunge, Is.Null);
            Assert.That(armPose.ElbowAngleAtLungeStartDegrees, Is.Null);
            Assert.That(armPose.LungeStartSecondsBeforeJudging, Is.Null);
        }

        [Test]
        public void MissingFileReadsAsEmpty()
        {
            var read = RepLogFile.Read(_folder.File("does-not-exist.jsonl"));

            Assert.That(read.Items, Is.Empty);
            Assert.That(read.SkippedLineCount, Is.EqualTo(0));
        }

        [Test]
        public void BrokenLinesAreSkippedAndCounted()
        {
            RepLogFile.Append(_logPath, Touch(TargetZone.Thigh, 0.5, 3.0f, repIndex: 0));
            File.AppendAllText(_logPath, "not json at all\n");
            File.AppendAllText(_logPath, "{\"schema\":1,\"sessionId\":\"s\",\"drillId\":\"d\",\"repIndex\":0,\"targetZone\":\"Thigh\"}\n");
            File.AppendAllText(_logPath, "{\"schema\":1,\"timestamp\":\"2026-10-19T12:00:00+00:00\",\"sessionId\":\"s\",\"drillId\":\"d\",\"repIndex\":0,"
                                         + "\"targetZone\":\"Elbow\",\"result\":\"Success\"}\n");
            File.AppendAllText(_logPath, "\n");
            RepLogFile.Append(_logPath, Touch(TargetZone.Hand, 0.5, 3.0f, repIndex: 1));

            var read = RepLogFile.Read(_logPath);

            Assert.That(read.Items, Has.Count.EqualTo(2));
            Assert.That(read.Items[1].TargetZone, Is.EqualTo(TargetZone.Hand));
            Assert.That(read.SkippedLineCount, Is.EqualTo(3));
        }

        [Test]
        public void AppendAfterALineCutShortStartsOnANewLine()
        {
            RepLogFile.Append(_logPath, Touch(TargetZone.Thigh, 0.5, 3.0f, repIndex: 0));
            File.AppendAllText(_logPath, "{\"schema\":1,\"timest");
            RepLogFile.Append(_logPath, Touch(TargetZone.Hand, 0.5, 3.0f, repIndex: 1));

            var read = RepLogFile.Read(_logPath);

            Assert.That(read.Items, Has.Count.EqualTo(2));
            Assert.That(read.Items[1].RepIndex, Is.EqualTo(1));
            Assert.That(read.SkippedLineCount, Is.EqualTo(1));
        }

        [Test]
        public void AppendCreatesTheFolder()
        {
            var nestedPath = Path.Combine(_folder.Path, "nested", RepLogFile.FileName);

            RepLogFile.Append(nestedPath, Touch(TargetZone.Thigh, 0.5, 3.0f));

            Assert.That(RepLogFile.Read(nestedPath).Items, Has.Count.EqualTo(1));
        }

        [Test]
        public void EntryFromRepResultCopiesTheResultAndAddsTheTimestamp()
        {
            var result = new RepResult("point-control", 2, TargetZone.Hand, RepOutcome.FlatContact, TargetZone.Hand, 0.7, 1.5f, 0.04f, isShrunk: true);

            var entry = RepLogEntry.FromRepResult(result, Noon, "session-b", flagged: false, armPose: null);

            Assert.That(entry.Timestamp, Is.EqualTo(Noon));
            Assert.That(entry.SessionId, Is.EqualTo("session-b"));
            Assert.That(entry.DrillId, Is.EqualTo("point-control"));
            Assert.That(entry.RepIndex, Is.EqualTo(2));
            Assert.That(entry.TargetZone, Is.EqualTo(TargetZone.Hand));
            Assert.That(entry.Result, Is.EqualTo(RepOutcome.FlatContact));
            Assert.That(entry.ContactZone, Is.EqualTo(TargetZone.Hand));
            Assert.That(entry.ReactionSeconds, Is.EqualTo(0.7));
            Assert.That(entry.TipSpeedMetersPerSecond, Is.EqualTo(1.5f));
            Assert.That(entry.DistanceFromZoneCenterMeters, Is.EqualTo(0.04f));
            Assert.That(entry.IsShrunk, Is.True);
            Assert.That(entry.Flagged, Is.False);
        }
    }
}
