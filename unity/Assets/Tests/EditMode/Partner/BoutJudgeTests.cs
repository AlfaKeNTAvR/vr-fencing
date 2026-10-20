// The bout judge (PER-82, rules from PER-56): first claim wins, a double within the window, a wait before lighting,
// stale and off-Strip claims refused, nothing counting while the lamps are lit.
using NUnit.Framework;
using VrFencing.Dummy;
using VrFencing.Partner;

namespace VrFencing.Tests.Partner
{
    public class BoutJudgeTests
    {
        private const double Contact = 100.0;

        [Test]
        public void TheDefaultsAreTheDecidedOnes()
        {
            var judge = new BoutJudge();

            Assert.That(judge.DoubleWindowSeconds, Is.EqualTo(0.05));
            Assert.That(judge.LightDelaySeconds, Is.EqualTo(0.12).Within(1e-9), "About 120 ms before lighting (PER-56).");
            Assert.That(BoutJudge.StaleClaimSeconds, Is.EqualTo(0.2));
            Assert.That(BoutJudge.LampSeconds, Is.EqualTo(2.0));
        }

        [Test]
        public void ASingleTouchLightsAfterTheDelayNotBefore()
        {
            var judge = new BoutJudge();

            Assert.That(judge.Submit(Claim(BoutSide.Partner, Contact, TargetZone.Forearm), Contact + 0.03), Is.EqualTo(ClaimVerdict.Accepted));
            Assert.That(judge.Step(Contact + 0.11), Is.Null, "Still waiting for a possible double.");

            var light = judge.Step(Contact + 0.12);

            Assert.That(light.HasValue, Is.True);
            Assert.That(light.Value.Kind, Is.EqualTo(LightKind.PartnerTouch));
            Assert.That(light.Value.PartnerZone, Is.EqualTo(TargetZone.Forearm));
            Assert.That(light.Value.HostZone, Is.Null);
            Assert.That(judge.Step(Contact + 0.2), Is.Null, "A light is given once.");
        }

        [TestCase(0.0)]
        [TestCase(0.05)]
        [TestCase(-0.05)]
        public void TheOtherTouchWithinTheWindowEitherWayIsADouble(double partnerAfterHostSeconds)
        {
            var judge = new BoutJudge();
            judge.Submit(Claim(BoutSide.Host, Contact, TargetZone.Body), Contact + 0.01);
            judge.Submit(Claim(BoutSide.Partner, Contact + partnerAfterHostSeconds, TargetZone.Hand), Contact + 0.1);

            var light = judge.Step(Contact + 0.3);

            Assert.That(light.Value.Kind, Is.EqualTo(LightKind.Double));
            Assert.That(light.Value.HostZone, Is.EqualTo(TargetZone.Body));
            Assert.That(light.Value.PartnerZone, Is.EqualTo(TargetZone.Hand));
            Assert.That(light.Value.Lights(BoutSide.Host) && light.Value.Lights(BoutSide.Partner), Is.True);
        }

        [Test]
        public void OutsideTheWindowTheFirstTouchWinsEvenIfItsClaimCameLast()
        {
            var judge = new BoutJudge();

            // The Host's Touch is 60 ms later but its claim is in first; the Partner's claim arrives 50 ms after its Touch.
            judge.Submit(Claim(BoutSide.Host, Contact + 0.06, TargetZone.Thigh), Contact + 0.06);
            judge.Submit(Claim(BoutSide.Partner, Contact, TargetZone.Body), Contact + 0.05);

            var light = judge.Step(Contact + 0.12);

            Assert.That(light.Value.Kind, Is.EqualTo(LightKind.PartnerTouch));
        }

        [Test]
        public void ALateEarlierClaimMovesTheLightUpWhenItsDelayHasPassed()
        {
            var judge = new BoutJudge();
            judge.Submit(Claim(BoutSide.Host, Contact + 0.1, TargetZone.Body), Contact + 0.1);
            Assert.That(judge.Step(Contact + 0.19), Is.Null);

            // The Partner touched first, 100 ms before; its claim is 190 ms old (not stale), and its delay is past.
            judge.Submit(Claim(BoutSide.Partner, Contact, TargetZone.Body), Contact + 0.19);

            Assert.That(judge.Step(Contact + 0.19).Value.Kind, Is.EqualTo(LightKind.PartnerTouch));
        }

        [Test]
        public void ALongerWindowMakesALongerDelayAndALaterDouble()
        {
            var judge = new BoutJudge { DoubleWindowSeconds = 0.15 };
            judge.Submit(Claim(BoutSide.Host, Contact, TargetZone.Body), Contact);
            judge.Submit(Claim(BoutSide.Partner, Contact + 0.14, TargetZone.Body), Contact + 0.18);

            Assert.That(judge.LightDelaySeconds, Is.EqualTo(0.22).Within(1e-9));
            Assert.That(judge.Step(Contact + 0.21), Is.Null);
            Assert.That(judge.Step(Contact + 0.22).Value.Kind, Is.EqualTo(LightKind.Double));
        }

        [TestCase(0.01, 0.05)]
        [TestCase(0.5, 0.15)]
        [TestCase(double.NaN, 0.05)]
        public void TheWindowStaysWithinTheHostSettingsRange(double asked, double expected)
        {
            Assert.That(new BoutJudge { DoubleWindowSeconds = asked }.DoubleWindowSeconds, Is.EqualTo(expected));
        }

        [Test]
        public void AClaimOlderThanTheCapIsRejected()
        {
            var judge = new BoutJudge();

            Assert.That(judge.Submit(Claim(BoutSide.Partner, Contact, TargetZone.Body), Contact + 0.21), Is.EqualTo(ClaimVerdict.Stale));
            Assert.That(judge.Step(Contact + 1.0), Is.Null);
        }

        [Test]
        public void AClaimFarAheadOfTheHostsClockIsRejected()
        {
            var judge = new BoutJudge();

            Assert.That(judge.Submit(Claim(BoutSide.Partner, Contact + 0.5, TargetZone.Body), Contact), Is.EqualTo(ClaimVerdict.FromTheFuture));
        }

        [Test]
        public void ATouchFromOffTheAttackersStripNeverLights()
        {
            var judge = new BoutJudge();

            var verdict = judge.Submit(new TouchClaim(1, BoutSide.Host, TargetZone.Body, Contact, isOffStrip: true), Contact);

            Assert.That(verdict, Is.EqualTo(ClaimVerdict.OffStrip));
            Assert.That(judge.Step(Contact + 1.0), Is.Null);
        }

        [Test]
        public void TheSameAttackersSecondTouchInAPhraseIsIgnored()
        {
            var judge = new BoutJudge();
            judge.Submit(Claim(BoutSide.Host, Contact, TargetZone.Hand), Contact);

            Assert.That(judge.Submit(Claim(BoutSide.Host, Contact + 0.03, TargetZone.Body), Contact + 0.03), Is.EqualTo(ClaimVerdict.AlreadyClaimed));
            Assert.That(judge.Step(Contact + 0.2).Value.HostZone, Is.EqualTo(TargetZone.Hand));
        }

        [Test]
        public void NothingCountsWhileTheLampsAreLitThenTheNextPhraseStarts()
        {
            var judge = new BoutJudge();
            judge.Submit(Claim(BoutSide.Host, Contact, TargetZone.Body), Contact);
            var lit = Contact + 0.12;
            judge.Step(lit);

            Assert.That(judge.IsLockedOut(lit + 1.0), Is.True);
            Assert.That(judge.Submit(Claim(BoutSide.Host, lit + 1.0, TargetZone.Body), lit + 1.0), Is.EqualTo(ClaimVerdict.LockedOut));
            Assert.That(judge.Submit(Claim(BoutSide.Partner, lit + 1.9, TargetZone.Body), lit + 1.9), Is.EqualTo(ClaimVerdict.LockedOut));

            var next = lit + BoutJudge.LampSeconds + 0.01;
            Assert.That(judge.IsLockedOut(next), Is.False);
            Assert.That(judge.Submit(Claim(BoutSide.Partner, next, TargetZone.Foot), next), Is.EqualTo(ClaimVerdict.Accepted));
            Assert.That(judge.Step(next + 0.12).Value.Kind, Is.EqualTo(LightKind.PartnerTouch));
        }

        [Test]
        public void ADoublesClaimThatArrivesAfterTheSingleLitStillMakesItADouble()
        {
            var judge = new BoutJudge();
            judge.Submit(Claim(BoutSide.Host, Contact, TargetZone.Body), Contact);
            var single = judge.Step(Contact + 0.12).Value;

            // The Partner touched 40 ms after the Host, but its claim took 150 ms (under the 200 ms cap).
            var verdict = judge.Submit(Claim(BoutSide.Partner, Contact + 0.04, TargetZone.Thigh), Contact + 0.19);
            var upgraded = judge.Step(Contact + 0.19);

            Assert.That(verdict, Is.EqualTo(ClaimVerdict.MadeDouble));
            Assert.That(upgraded.Value.Kind, Is.EqualTo(LightKind.Double));
            Assert.That(upgraded.Value.Sequence, Is.EqualTo(single.Sequence), "The same light, now a double.");
            Assert.That(upgraded.Value.HostZone, Is.EqualTo(TargetZone.Body));
            Assert.That(upgraded.Value.PartnerZone, Is.EqualTo(TargetZone.Thigh));
        }

        [Test]
        public void ALateClaimOutsideTheWindowLostTheRace()
        {
            var judge = new BoutJudge();
            judge.Submit(Claim(BoutSide.Host, Contact, TargetZone.Body), Contact);
            judge.Step(Contact + 0.12);

            Assert.That(judge.Submit(Claim(BoutSide.Partner, Contact + 0.08, TargetZone.Body), Contact + 0.15), Is.EqualTo(ClaimVerdict.LockedOut));
            Assert.That(judge.Step(Contact + 0.15), Is.Null);
        }

        [Test]
        public void ResetForgetsAWaitingTouchAndTheLockout()
        {
            var judge = new BoutJudge();
            judge.Submit(Claim(BoutSide.Host, Contact, TargetZone.Body), Contact);
            judge.Step(Contact + 0.12);

            judge.Reset();

            Assert.That(judge.IsLockedOut(Contact + 0.5), Is.False);
            Assert.That(judge.HasPendingTouch, Is.False);
        }

        private static TouchClaim Claim(BoutSide attacker, double contactSeconds, TargetZone zone)
        {
            return new TouchClaim(1, attacker, zone, contactSeconds, isOffStrip: false);
        }
    }
}
