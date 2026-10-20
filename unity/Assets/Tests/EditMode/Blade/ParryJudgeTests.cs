// Parry judging (PER-97, PER-23 resolution): a blade-on-blade contact must deflect the Armed Dummy's point off the
// Fencer's body; strict also needs the Fencer's forte on the Dummy's foible; one check names lines 6, 4, 8 and 7.
// The Fencer is the stand-in body the attacks aim at (the Dummy's stance and size at the En Garde Line).
using System;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Blade;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Tests.Blade
{
    public class ParryJudgeTests
    {
        private const float HeightMeters = 1.65f;
        private const double FrameSeconds = 1.0 / 72.0;

        private static readonly DummyLayout Layout = DummyLayout.Create(HeightMeters, Hand.Right);

        // The Fencer's guard en garde, out in front and to the sword-hand side of the chest.
        private static readonly Vector3 FencerGuard = new Vector3(0.2f, 1.05f, 0.35f);

        private static readonly Vector3 Inside = -Vector3.UnitX;
        private static readonly Vector3 Outside = Vector3.UnitX;

        private static Vector3 ChestAim => AttackAimOnStrip(AttackTarget.Chest);

        private static Vector3 AttackAimOnStrip(AttackTarget target)
        {
            var aimInDummyFrame = AttackAim.GetAimInDummyFrame(target, Layout, 2.0f, FencerGuard, Vector3.UnitZ);
            return DummyLayout.DummyToStrip(aimInDummyFrame, 2.0f);
        }

        /// <summary>A thrust from the Dummy at chest height, its point <paramref name="metersToGo"/> short of the aim.</summary>
        private static AttackLine ThrustAt(Vector3 aim, float metersToGo = 0.4f)
        {
            var start = new Vector3(aim.X, aim.Y + 0.1f, 1.4f);
            var direction = Vector3.Normalize(aim - start);
            return new AttackLine(aim - (direction * metersToGo), aim, direction);
        }

        private static System.Collections.Generic.IReadOnlyList<Capsule> FencerBody => AttackAim.GetFencerBodyOnStrip(Layout, FencerGuard);

        /// <summary>The Fencer's blade from the guard, pitched up (positive) or down by <paramref name="pitchDegrees"/>.</summary>
        private static BladeSegment FencerBlade(float pitchDegrees = 10.0f)
        {
            var radians = pitchDegrees * Math.PI / 180.0;
            var direction = new Vector3(0.0f, (float)Math.Sin(radians), (float)Math.Cos(radians));
            return new BladeSegment(FencerGuard, FencerGuard + (direction * 0.9f));
        }

        private static BladeClash Clash(Vector3 push, float speedMetersPerSecond, float fencerFraction = 0.3f, float dummyFraction = 0.8f, double timeSeconds = 0.0)
        {
            return new BladeClash(timeSeconds, FencerGuard + new Vector3(0.0f, 0.03f, 0.27f), fencerFraction, dummyFraction, speedMetersPerSecond, push);
        }

        private static ParryReport? JudgeOneClash(BladeClash clash, ParryRule rule = ParryRule.Loose, float pitchDegrees = 10.0f, Hand hand = Hand.Right)
        {
            var judge = new ParryJudge(rule, hand);
            judge.ReportClash(clash, FencerBlade(pitchDegrees));
            return judge.Step(clash.TimeSeconds, Vector3.Zero, isInContact: true, ThrustAt(ChestAim), FencerBody);
        }

        // ---- Deflecting vs non-deflecting contact ----

        [Test]
        public void AnUndeflectedThrustAtTheChestStaysOnTheBody()
        {
            Assert.That(ParryJudge.IsOffBody(ThrustAt(ChestAim), Vector3.Zero, FencerBody), Is.False);
            Assert.That(ParryJudge.IsOffBody(ThrustAt(AttackAimOnStrip(AttackTarget.Thigh)), Vector3.Zero, FencerBody), Is.False);
            Assert.That(ParryJudge.IsOffBody(ThrustAt(AttackAimOnStrip(AttackTarget.Hand)), Vector3.Zero, FencerBody), Is.False);
        }

        [Test]
        public void ASidewaysDeflectionPastTheBodyClearsItButOneAlongTheBodyDoesNot()
        {
            Assert.That(ParryJudge.IsOffBody(ThrustAt(ChestAim), Inside * 0.2f, FencerBody), Is.True, "4: past the chest, inside.");
            Assert.That(ParryJudge.IsOffBody(ThrustAt(ChestAim), Outside * 0.3f, FencerBody), Is.True, "6: outside the sword arm.");
            Assert.That(ParryJudge.IsOffBody(ThrustAt(ChestAim), Inside * 0.08f, FencerBody), Is.False, "Still on the chest.");
            Assert.That(ParryJudge.IsOffBody(ThrustAt(ChestAim), Vector3.UnitY * 0.12f, FencerBody), Is.False, "Up the chest: still on the body.");
        }

        [Test]
        public void AHandAttackNeedsLessDeflectionThanAChestAttack()
        {
            var hand = ThrustAt(AttackAimOnStrip(AttackTarget.Hand));

            Assert.That(ParryJudge.IsOffBody(hand, Inside * 0.1f, FencerBody), Is.True);
            Assert.That(ParryJudge.IsOffBody(ThrustAt(ChestAim), Inside * 0.1f, FencerBody), Is.False);
        }

        [Test]
        public void AFirmParryIsJudgedAtTheClashBeforeTheSwingGetsThere()
        {
            var report = JudgeOneClash(Clash(Inside, speedMetersPerSecond: 2.0f));

            Assert.That(report.HasValue, Is.True);
            Assert.That(report.Value.Line, Is.EqualTo(ParryLine.Four));
            Assert.That(report.Value.CountsAsParry, Is.True);
            Assert.That(report.Value.DeflectionMeters, Is.EqualTo(BladeSwing.GetPeakMeters(2.0f, 0.8f)).Within(1e-4f));
        }

        [Test]
        public void ALightTouchDoesNotDeflectAndTheWatchEndsAfterItsTime()
        {
            var judge = new ParryJudge(ParryRule.Loose, Hand.Right);
            judge.ReportClash(Clash(Inside, speedMetersPerSecond: 0.2f), FencerBlade());

            Assert.That(judge.Step(0.0, Vector3.Zero, isInContact: false, ThrustAt(ChestAim), FencerBody), Is.Null);
            Assert.That(judge.IsWatching, Is.True);

            Assert.That(judge.Step(ParryJudge.WatchSeconds + FrameSeconds, Inside * 0.03f, isInContact: false, ThrustAt(ChestAim), FencerBody), Is.Null);
            Assert.That(judge.IsWatching, Is.False, "The contact did not deflect: the thrust goes on.");
            Assert.That(judge.Step(1.0, Inside * 0.3f, isInContact: false, ThrustAt(ChestAim), FencerBody), Is.Null, "Nothing watched any more.");
        }

        [Test]
        public void AHeldPressThatPushesThePointOffIsAParryEvenWithoutAKick()
        {
            var judge = new ParryJudge(ParryRule.Loose, Hand.Right);
            judge.ReportHeldContact(Clash(Inside, speedMetersPerSecond: 3.0f, timeSeconds: 0.0), 0.5, FencerBlade());

            Assert.That(judge.Step(0.5, Vector3.Zero, isInContact: true, ThrustAt(ChestAim), FencerBody), Is.Null, "No kick from the old clash.");
            Assert.That(judge.Step(1.5, Outside * 0.05f, isInContact: true, ThrustAt(ChestAim), FencerBody), Is.Null, "Still watching while pressed.");

            var report = judge.Step(1.6, Outside * 0.32f, isInContact: true, ThrustAt(ChestAim), FencerBody);

            Assert.That(report.HasValue, Is.True);
            Assert.That(report.Value.Line, Is.EqualTo(ParryLine.Six));
            Assert.That(judge.IsWatching, Is.False, "Judged once.");
        }

        [Test]
        public void OnlyTheDeflectionSquareToTheThrustCounts()
        {
            var line = ThrustAt(ChestAim);

            Assert.That(ParryJudge.IsOffBody(line, line.Direction * 0.3f, FencerBody), Is.False, "Along the thrust is no deflection.");
            var judge = new ParryJudge(ParryRule.Loose, Hand.Right);
            judge.ReportClash(Clash(Vector3.Zero, 0.0f), FencerBlade());
            Assert.That(judge.Step(0.0, line.Direction * 0.3f, isInContact: true, line, FencerBody), Is.Null);
        }

        // ---- Strict: forte on foible ----

        [Test]
        public void StrictNeedsTheFencersForteOnTheDummysFoible()
        {
            var forteOnFoible = Clash(Inside, 2.0f, fencerFraction: 0.3f, dummyFraction: 0.8f);
            var foibleOnFoible = Clash(Inside, 2.0f, fencerFraction: 0.8f, dummyFraction: 0.8f);
            var forteOnForte = Clash(Inside, 2.0f, fencerFraction: 0.3f, dummyFraction: 0.3f);

            Assert.That(JudgeOneClash(forteOnFoible, ParryRule.Strict).Value.CountsAsParry, Is.True);
            Assert.That(JudgeOneClash(foibleOnFoible, ParryRule.Strict).Value.CountsAsParry, Is.False);
            Assert.That(JudgeOneClash(foibleOnFoible, ParryRule.Strict).Value.IsForteOnFoible, Is.False);
            Assert.That(JudgeOneClash(foibleOnFoible, ParryRule.Loose).Value.CountsAsParry, Is.True, "Loose takes any deflecting contact.");
            Assert.That(JudgeOneClash(forteOnForte, ParryRule.Strict).Value.CountsAsParry, Is.False);
        }

        [Test]
        public void ForteAndFoibleAreTheBladesHalves()
        {
            Assert.That(ParryJudge.IsForteOnFoible(0.5f, 0.5f), Is.True);
            Assert.That(ParryJudge.IsForteOnFoible(0.51f, 0.9f), Is.False);
            Assert.That(ParryJudge.IsForteOnFoible(0.1f, 0.49f), Is.False);
        }

        // ---- Line naming ----

        [TestCase(10.0f, 1.0f, ParryLine.Six)]
        [TestCase(10.0f, -1.0f, ParryLine.Four)]
        [TestCase(-35.0f, 1.0f, ParryLine.Eight)]
        [TestCase(-35.0f, -1.0f, ParryLine.Seven)]
        [TestCase(-10.0f, 1.0f, ParryLine.Six)]
        public void TheLineComesFromTheFencersPointAndTheWayTheAttackWasTurned(float pitchDegrees, float sideways, ParryLine expected)
        {
            Assert.That(ParryJudge.NameLine(new Vector3(sideways * 0.2f, 0.0f, 0.0f), FencerBlade(pitchDegrees), Vector3.Zero, Hand.Right), Is.EqualTo(expected));
        }

        [TestCase(10.0f, -1.0f, ParryLine.Six)]
        [TestCase(10.0f, 1.0f, ParryLine.Four)]
        [TestCase(-35.0f, -1.0f, ParryLine.Eight)]
        [TestCase(-35.0f, 1.0f, ParryLine.Seven)]
        public void ALeftHandedFencersOutsideIsToTheLeft(float pitchDegrees, float sideways, ParryLine expected)
        {
            Assert.That(ParryJudge.NameLine(new Vector3(sideways * 0.2f, 0.0f, 0.0f), FencerBlade(pitchDegrees), Vector3.Zero, Hand.Left), Is.EqualTo(expected));
        }

        [Test]
        public void APointTurnedStraightUpOrDownTakesItsSideFromTheContact()
        {
            var blade = FencerBlade();

            Assert.That(ParryJudge.NameLine(Vector3.UnitY * 0.2f, blade, FencerGuard + new Vector3(0.05f, 0.0f, 0.2f), Hand.Right), Is.EqualTo(ParryLine.Six));
            Assert.That(ParryJudge.NameLine(Vector3.UnitY * 0.2f, blade, FencerGuard + new Vector3(-0.05f, 0.0f, 0.2f), Hand.Right), Is.EqualTo(ParryLine.Four));
        }

        [Test]
        public void ALowParryOfAThighAttackIsJudgedAndNamedEightOrSeven()
        {
            var thigh = ThrustAt(AttackAimOnStrip(AttackTarget.Thigh));

            foreach (var (push, expected) in new[] { (Outside, ParryLine.Eight), (Inside, ParryLine.Seven) })
            {
                var judge = new ParryJudge(ParryRule.Loose, Hand.Right);
                judge.ReportClash(Clash(push, 2.5f), FencerBlade(-35.0f));
                var report = judge.Step(0.0, Vector3.Zero, isInContact: true, thigh, FencerBody);

                Assert.That(report.HasValue, Is.True, expected.ToString());
                Assert.That(report.Value.Line, Is.EqualTo(expected));
            }
        }

        // ---- The thrust and the Fencer's body on the Strip ----

        [Test]
        public void TheAttackLineIsThePlannedPathOnTheStrip()
        {
            var path = new AttackPath(new Vector3(0.1f, 1.2f, 0.3f), new Vector3(0.0f, 1.1f, 1.5f), startSeconds: 0.0, durationSeconds: 0.5f);

            var before = AttackLine.OnStrip(path, 2.0f, -1.0);
            var after = AttackLine.OnStrip(path, 2.0f, 5.0);

            Assert.That(Vector3.Distance(before.PointNow, new Vector3(-0.1f, 1.2f, 1.7f)), Is.LessThan(1e-5f));
            Assert.That(Vector3.Distance(after.PointNow, new Vector3(0.0f, 1.1f, 0.5f)), Is.LessThan(1e-5f));
            Assert.That(Vector3.Distance(after.AimPoint, after.PointNow), Is.LessThan(1e-5f));
            Assert.That(before.Direction.Z, Is.LessThan(-0.9f), "Toward the Fencer.");
        }

        [Test]
        public void TheFencersBodyLeavesOutTheDummysPosedArmAndAddsTheFencersOwn()
        {
            var withArm = AttackAim.GetFencerBodyOnStrip(Layout, FencerGuard);
            var withoutArm = AttackAim.GetFencerBodyOnStrip(Layout, null);
            var dummyHand = Layout.GetZone(TargetZone.Hand);

            Assert.That(withArm.Count, Is.EqualTo(withoutArm.Count + 1));
            Assert.That(withoutArm.Any(capsule => capsule.Start == dummyHand.Start && capsule.End == dummyHand.End), Is.False);
            Assert.That(withArm.Last().Start, Is.EqualTo(FencerGuard));
            Assert.That(withArm.Any(capsule => capsule.Start == Layout.GetZone(TargetZone.Body).Start), Is.True);
        }

        [Test]
        public void TheThrustDirectionMustBeAUnitVector()
        {
            Assert.Throws<ArgumentException>(() => new AttackLine(Vector3.Zero, Vector3.UnitZ, new Vector3(0.0f, 0.0f, 2.0f)));
        }
    }
}
