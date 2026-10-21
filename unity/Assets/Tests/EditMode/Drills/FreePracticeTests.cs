// Free Practice (PER-123): outside any Drill the armed Dummy does what the home screen's row picked, with no Reps or
// scoring; the attacking Dummy thrusts now and then from the Armed Dummy test Drill's script.
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VrFencing.Drills;
using VrFencing.Dummy;
using VrFencing.Settings;
using VrFencing.UI;

namespace VrFencing.Tests.Drills
{
    public class FreePracticeTests
    {
        [Test]
        public void KeepingDistanceIsTheDefaultBehavior()
        {
            Assert.That(new TrainerSettings().FreePractice, Is.EqualTo(FreePracticeBehavior.KeepDistance));
        }

        [Test]
        public void TheBehaviorSavesAndAnOlderFileLoadsTheDefault()
        {
            var directory = Path.Combine(Path.GetTempPath(), "vrfencing-free-practice-tests-" + Path.GetRandomFileName());
            var path = Path.Combine(directory, SettingsStorage.FileName);
            try
            {
                SettingsStorage.Save(new TrainerSettings { FreePractice = FreePracticeBehavior.Attacks }, path);
                Assert.That(SettingsStorage.Load(path).FreePractice, Is.EqualTo(FreePracticeBehavior.Attacks));
                Assert.That(File.ReadAllText(path), Does.Contain("\"FreePractice\": \"Attacks\""));

                File.WriteAllText(path, "{ \"Hand\": \"Left\" }");
                Assert.That(SettingsStorage.Load(path).FreePractice, Is.EqualTo(FreePracticeBehavior.KeepDistance));
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        [Test]
        public void TheRowCyclesThroughAllFourBehaviorsAndBack()
        {
            var seen = new[] { FreePracticeBehavior.Guard };
            var behavior = FreePracticeBehavior.Guard;
            for (var press = 0; press < 3; press++)
            {
                behavior = FreePractice.GetNext(behavior);
                seen = seen.Append(behavior).ToArray();
            }

            Assert.That(seen, Is.EqualTo(new[]
            {
                FreePracticeBehavior.Guard, FreePracticeBehavior.KeepDistance, FreePracticeBehavior.Footwork, FreePracticeBehavior.Attacks,
            }));
            Assert.That(FreePractice.GetNext(FreePracticeBehavior.Attacks), Is.EqualTo(FreePracticeBehavior.Guard));
            Assert.Throws<ArgumentOutOfRangeException>(() => FreePractice.GetNext((FreePracticeBehavior)99));
        }

        [Test]
        public void TheRowNamesWhatTheDummyDoes()
        {
            Assert.That(FreePracticeText.FormatButton(FreePracticeBehavior.Guard), Is.EqualTo("Dummy: guard"));
            Assert.That(FreePracticeText.FormatButton(FreePracticeBehavior.KeepDistance), Is.EqualTo("Dummy: keep distance"));
            Assert.That(FreePracticeText.FormatButton(FreePracticeBehavior.Footwork), Is.EqualTo("Dummy: footwork"));
            Assert.That(FreePracticeText.FormatButton(FreePracticeBehavior.Attacks), Is.EqualTo("Dummy: attacks"));
        }

        [Test]
        public void FreePracticeIsOnOnlyOutsideDrillsWithTheDummyShownOnAPlacedStrip()
        {
            Assert.That(FreePractice.IsOn(isDrillOn: false, isDummyShown: true, isStripReady: true), Is.True);
            Assert.That(FreePractice.IsOn(isDrillOn: true, isDummyShown: true, isStripReady: true), Is.False, "A Drill takes the Dummy over.");
            Assert.That(FreePractice.IsOn(isDrillOn: false, isDummyShown: false, isStripReady: true), Is.False, "Hidden in a Partner session.");
            Assert.That(FreePractice.IsOn(isDrillOn: false, isDummyShown: true, isStripReady: false), Is.False, "No Strip, or placing one.");
        }

        [Test]
        public void EachBehaviorStandsTheDummyWhereItsDrillDoes()
        {
            Assert.That(FreePractice.GetDummyDistance(FreePracticeBehavior.Guard), Is.EqualTo(DummyDistance.Lunge));
            Assert.That(FreePractice.GetDummyDistance(FreePracticeBehavior.KeepDistance), Is.EqualTo(DummyDistance.Lunge));
            Assert.That(FreePractice.GetDummyDistance(FreePracticeBehavior.Footwork), Is.EqualTo(DummyDistance.StepLunge));
            Assert.That(FreePractice.GetDummyDistance(FreePracticeBehavior.Attacks), Is.EqualTo(DummyDistance.Extension));
        }

        [Test]
        public void TheFirstAttackComesAfterARest()
        {
            var schedule = new FreeAttackSchedule(new Random(1));

            Assert.That(schedule.Step(10.0, isAttackUnderWay: false), Is.Null);
            Assert.That(schedule.Step(10.0 + FreeAttackSchedule.RestSeconds - 0.01, isAttackUnderWay: false), Is.Null);
            Assert.That(schedule.Step(10.0 + FreeAttackSchedule.RestSeconds, isAttackUnderWay: false), Is.Not.Null);
        }

        [Test]
        public void NoAttackWhileOneIsUnderWayAndTheNextRestsAfterIt()
        {
            var schedule = new FreeAttackSchedule(new Random(1));
            schedule.Step(0.0, isAttackUnderWay: false);
            Assert.That(schedule.Step(FreeAttackSchedule.RestSeconds, isAttackUnderWay: false), Is.Not.Null);

            // The thrust is under way for a long time: nothing new.
            Assert.That(schedule.Step(3.0, isAttackUnderWay: true), Is.Null);
            Assert.That(schedule.Step(30.0, isAttackUnderWay: true), Is.Null);

            // Over at 31 s: the next one a rest later, not at once.
            Assert.That(schedule.Step(31.0, isAttackUnderWay: false), Is.Null);
            Assert.That(schedule.Step(31.0 + FreeAttackSchedule.RestSeconds, isAttackUnderWay: false), Is.Not.Null);
        }

        [Test]
        public void AttacksUseTheTestDrillsScriptSpeedsInTurnTargetsAtRandom()
        {
            var schedule = new FreeAttackSchedule(new Random(7));
            var script = AttackScript.EveryTargetAndSpeed;
            var now = 0.0;
            var attacks = Enumerable.Range(0, 40).Select(_ => NextAttack(schedule, ref now)).ToArray();

            Assert.That(attacks.Select(attack => attack.Speed), Is.EqualTo(Enumerable.Range(0, 40).Select(script.GetSpeed)));
            Assert.That(attacks.Select(attack => attack.Target).Distinct(), Is.EquivalentTo(script.Targets), "Every target comes up.");
        }

        [Test]
        public void ResetStartsTheSpeedsOverAndWaitsARest()
        {
            var schedule = new FreeAttackSchedule(new Random(3));
            var now = 0.0;
            NextAttack(schedule, ref now);
            NextAttack(schedule, ref now);

            schedule.Reset();

            Assert.That(schedule.Step(now, isAttackUnderWay: false), Is.Null);
            Assert.That(NextAttack(schedule, ref now).Speed, Is.EqualTo(AttackScript.EveryTargetAndSpeed.GetSpeed(0)));
        }

        [Test]
        public void AScheduleNeedsARandomSource()
        {
            Assert.Throws<ArgumentNullException>(() => new FreeAttackSchedule(null));
        }

        // Steps a tenth of a second at a time, no attack under way, until the next attack comes.
        private static FreeAttack NextAttack(FreeAttackSchedule schedule, ref double now)
        {
            for (var step = 0; step < 1000; step++)
            {
                var attack = schedule.Step(now, isAttackUnderWay: false);
                now += 0.1;
                if (attack.HasValue)
                {
                    return attack.Value;
                }
            }

            Assert.Fail("No attack within 100 s.");
            return default;
        }
    }
}
