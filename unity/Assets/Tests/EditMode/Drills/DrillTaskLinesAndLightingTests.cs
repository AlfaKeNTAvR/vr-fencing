// Drill clarity (PER-92): every Drill says what to do in one short line, and Drills 1 and 2 can also light the zone
// they name ("Light named zones", on by default).
using System;
using System.Linq;
using NUnit.Framework;
using VrFencing.Drills;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Tests.Drills
{
    public class DrillTaskLinesAndLightingTests
    {
        private static DrillDefinition Build(string id, TrainerSettings settings = null)
        {
            return DrillCatalog.CreateDefault(includeTestDrills: true).BuildAll(settings ?? new TrainerSettings()).Single(drill => drill.Id == id);
        }

        [Test]
        public void EveryDrillHasAShortTaskLineWithTheLightOnOrOff([Values(true, false)] bool lightNamedZones)
        {
            var settings = new TrainerSettings { LightNamedZones = lightNamedZones };
            var drills = DrillCatalog.CreateDefault(includeTestDrills: true).BuildAll(settings);

            foreach (var drill in drills)
            {
                Assert.That(drill.TaskLine, Is.Not.Empty, drill.Id);
                Assert.That(drill.TaskLine.Length, Is.LessThanOrEqualTo(DrillCatalog.MaxTaskLineLength), drill.TaskLine);
                Assert.That(drill.TaskLine.All(character => character < 128), Is.True, $"Plain text only (no en or em dashes): {drill.TaskLine}");
            }

            Assert.That(drills.Select(drill => drill.TaskLine), Is.Unique, "Each Drill says something different.");
        }

        [Test]
        public void DrillsOneAndTwoLightTheNamedZoneByDefaultAndSaySo()
        {
            var pointControl = Build(DrillCatalog.PointControlDrillId);
            var lungeToTarget = Build(DrillCatalog.LungeToTargetDrillId);

            Assert.That(new TrainerSettings().LightNamedZones, Is.True);
            Assert.That(pointControl.CueType, Is.EqualTo(CueType.NamedTargetZone), "Still named: the label above the Dummy stays.");
            Assert.That(pointControl.LightsCuedZone, Is.True);
            Assert.That(lungeToTarget.LightsCuedZone, Is.True);
            Assert.That(pointControl.TaskLine, Is.EqualTo(DrillCatalog.PointControlLitTaskLine));
            Assert.That(lungeToTarget.TaskLine, Is.EqualTo(DrillCatalog.LungeToTargetLitTaskLine));
        }

        [Test]
        public void WithTheLightOffDrillsOneAndTwoOnlyNameTheZone()
        {
            var settings = new TrainerSettings { LightNamedZones = false };

            var pointControl = Build(DrillCatalog.PointControlDrillId, settings);
            var lungeToTarget = Build(DrillCatalog.LungeToTargetDrillId, settings);

            Assert.That(pointControl.LightsCuedZone, Is.False);
            Assert.That(lungeToTarget.LightsCuedZone, Is.False);
            Assert.That(pointControl.TaskLine, Is.EqualTo(DrillCatalog.PointControlTaskLine));
            Assert.That(lungeToTarget.TaskLine, Is.EqualTo(DrillCatalog.LungeToTargetTaskLine));
        }

        [Test]
        public void TheSettingLeavesTheOtherDrillsAsTheyWere([Values(true, false)] bool lightNamedZones)
        {
            var settings = new TrainerSettings { LightNamedZones = lightNamedZones };

            Assert.That(Build(DrillCatalog.ReactionDrillId, settings).LightsCuedZone, Is.True);
            Assert.That(Build(DrillCatalog.DistanceChoiceDrillId, settings).LightsCuedZone, Is.True);
            Assert.That(Build(DrillCatalog.CompoundDrillId, settings).LightsCuedZone, Is.True);
            Assert.That(Build(DrillCatalog.FootworkCallsDrillId, settings).LightsCuedZone, Is.False, "A footwork call cues no zone.");
            Assert.That(Build(DrillCatalog.FullPisteTestDrillId, settings).LightsCuedZone, Is.False);
        }

        [Test]
        public void OnlyANamedZoneDrillCanAlsoLightItsZone()
        {
            Assert.Throws<ArgumentException>(() => new DrillDefinition(
                "lit", "Lit", 1, 1.0f, null, 1, 0.0f, 1.0f, 0.0f, CueType.LitTargetZone, SuccessRule.PointFirstTouchOnTargetZone,
                new[] { TargetZone.Body }, lightsNamedZone: true));

            var named = new DrillDefinition(
                "named", "Named", 1, 1.0f, null, 1, 0.0f, 1.0f, 0.0f, CueType.NamedTargetZone, SuccessRule.PointFirstTouchOnTargetZone,
                new[] { TargetZone.Body }, lightsNamedZone: true);
            Assert.That(named.LightsCuedZone, Is.True);
        }

        [Test]
        public void ADrillWithoutATaskLineHasAnEmptyOne()
        {
            var drill = new DrillDefinition(
                "plain", "Plain", 1, 1.0f, null, 1, 0.0f, 1.0f, 0.0f, CueType.NamedTargetZone, SuccessRule.PointFirstTouchOnTargetZone,
                new[] { TargetZone.Body });

            Assert.That(drill.TaskLine, Is.Empty);
            Assert.That(drill.LightsCuedZone, Is.False);
        }
    }
}
