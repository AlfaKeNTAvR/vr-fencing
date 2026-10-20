// An Armed Dummy thrust that lands on the Fencer (PER-98) stops where its point met the body, holds there, then recovers;
// it lands once, never after a Parry, and a landing during the hold keeps the planned hold.
using System.Numerics;
using NUnit.Framework;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Tests.Dummy
{
    public class ArmedDummyLandingTests
    {
        private const float Height = 1.75f;
        private const float BladeLength = DistancePresets.BladeLengthMeters;
        private const double Start = 10.0;

        private static ArmedDummyAttack Plan()
        {
            var shoulder = SwordArmReach.GetShoulderPosition(Height);
            var reach = SwordArmReach.GetFullReachMeters(Height, BladeLength);
            var aim = shoulder + (Vector3.Normalize(new Vector3(-0.1f, -0.2f, 1.0f)) * (reach - 0.08f));
            return ArmedDummyAttack.Plan(Start, 2.0f, Height, Hand.Right, aim, AttackSpeed.Medium, BladeLength);
        }

        private static Vector3 PointAt(ArmedDummyAttack attack, double nowSeconds)
        {
            return new SwordArm(Height, Hand.Right, attack.GetPose(nowSeconds)).GetBladePoint(BladeLength);
        }

        [Test]
        public void ALandingMidThrustStopsThePointThereAndHoldsBeforeRecovering()
        {
            var attack = Plan();
            var landedAt = attack.ThrustStartSeconds + (attack.ThrustSeconds * 0.6);
            var pointAtLanding = PointAt(attack, landedAt);

            Assert.That(attack.ReportLanded(landedAt), Is.True);

            Assert.That(attack.HasLanded, Is.True);
            Assert.That(attack.IsParried, Is.False);
            Assert.That(attack.GetPhase(landedAt + 0.01), Is.EqualTo(AttackPhase.Hold));
            Assert.That(Vector3.Distance(PointAt(attack, landedAt + 0.01), pointAtLanding), Is.LessThan(1e-4f), "The point runs no further.");
            Assert.That(Vector3.Distance(PointAt(attack, landedAt + ArmedDummyTuning.HoldSeconds - 0.01), pointAtLanding), Is.LessThan(1e-4f));
            Assert.That(attack.GetPhase(landedAt + ArmedDummyTuning.HoldSeconds + 0.01), Is.EqualTo(AttackPhase.Recover));
            Assert.That(attack.DoneSeconds, Is.EqualTo(landedAt + ArmedDummyTuning.HoldSeconds + ArmedDummyTuning.RecoverSeconds).Within(1e-6));
        }

        [Test]
        public void ALandingDuringTheHoldKeepsThePlannedHold()
        {
            var attack = Plan();
            var plannedDone = attack.DoneSeconds;
            var landedAt = attack.ThrustEndSeconds + 0.1;

            Assert.That(attack.ReportLanded(landedAt), Is.True);

            Assert.That(attack.DoneSeconds, Is.EqualTo(plannedDone).Within(1e-9));
            Assert.That(attack.GetPhase(landedAt + 0.01), Is.EqualTo(AttackPhase.Hold));
        }

        [Test]
        public void AThrustLandsOnceAndNeverOutsideTheThrustOrHold()
        {
            var attack = Plan();

            Assert.That(attack.ReportLanded(attack.WindUpStartSeconds + 0.05), Is.False, "Not in the wind-up.");
            var landedAt = attack.ThrustStartSeconds + (attack.ThrustSeconds * 0.5);
            Assert.That(attack.ReportLanded(landedAt), Is.True);
            Assert.That(attack.ReportLanded(landedAt + 0.05), Is.False, "Landed once.");
            Assert.That(attack.ReportLanded(attack.DoneSeconds + 0.1), Is.False, "Not once it is over.");
        }

        [Test]
        public void AParriedThrustDoesNotLand()
        {
            var attack = Plan();
            var parriedAt = attack.ThrustStartSeconds + (attack.ThrustSeconds * 0.5);
            attack.ReportParried(parriedAt);

            Assert.That(attack.ReportLanded(parriedAt + 0.01), Is.False);
            Assert.That(attack.HasLanded, Is.False);
        }
    }
}
