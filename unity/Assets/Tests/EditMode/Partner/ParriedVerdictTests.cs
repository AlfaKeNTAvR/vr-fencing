// The Parried verdict (PER-145): the judge waits for the Host's replay judge, a parried claim leaves the waiting Touches
// as a Parried verdict that lights no lamp, the other Fencer's Touch goes on alone, the light never waits past the
// ruling limit, a slow link lengthens the stale-claim limit, and the verdict travels to the Partner's box as a note.
using System.Collections.Generic;
using NUnit.Framework;
using VrFencing.Dummy;
using VrFencing.Partner;

namespace VrFencing.Tests.Partner
{
    public class ParriedVerdictTests
    {
        private const double Contact = 100.0;

        [Test]
        public void AParriedClaimIsAVerdictWithNoLampAndTheOtherTouchLightsAlone()
        {
            var judge = new BoutJudge();
            var referee = new PretendReferee();
            referee.Rulings[(BoutSide.Partner, 1)] = ParryRuling.Parried;
            referee.Rulings[(BoutSide.Host, 1)] = ParryRuling.Touch;
            judge.Submit(Claim(BoutSide.Partner, Contact, TargetZone.Body), Contact + 0.05);
            judge.Submit(Claim(BoutSide.Host, Contact + 0.03, TargetZone.Hand), Contact + 0.05);

            var parried = judge.Step(Contact + 0.06, referee);

            Assert.That(parried.Value.Kind, Is.EqualTo(LightKind.Parried));
            Assert.That(parried.Value.ParriedAttacker, Is.EqualTo(BoutSide.Partner));
            Assert.That(parried.Value.PartnerZone, Is.EqualTo(TargetZone.Body));
            Assert.That(parried.Value.Lights(BoutSide.Host) || parried.Value.Lights(BoutSide.Partner), Is.False, "No lamp.");

            Assert.That(judge.Step(Contact + 0.1, referee), Is.Null, "The Host's Touch waits for the light delay from its own press.");
            var light = judge.Step(Contact + 0.03 + judge.LightDelaySeconds, referee);
            Assert.That(light.Value.Kind, Is.EqualTo(LightKind.HostTouch), "Within the double window, but the parried Touch makes no double.");
            Assert.That(judge.TakeRulings().Count, Is.EqualTo(2));
        }

        [Test]
        public void TheLightWaitsForTheRulingButNotPastItsLimit()
        {
            var judge = new BoutJudge();
            var referee = new PretendReferee();
            judge.Submit(Claim(BoutSide.Partner, Contact, TargetZone.Body), Contact + 0.05);

            Assert.That(judge.Step(Contact + judge.LightDelaySeconds, referee), Is.Null, "The replay has not reached the press yet.");
            Assert.That(judge.Step(Contact + judge.MaxRulingWaitSeconds - 0.01, referee), Is.Null);

            var light = judge.Step(Contact + judge.MaxRulingWaitSeconds + 0.01, referee);

            Assert.That(light.Value.Kind, Is.EqualTo(LightKind.PartnerTouch), "Not ruled in time: the Touch stands.");
            var rulings = judge.TakeRulings();
            Assert.That(rulings.Count, Is.EqualTo(1));
            Assert.That(rulings[0].IsTimedOut, Is.True);
        }

        [Test]
        public void WithoutARefereeEveryTouchStandsAsBefore()
        {
            var judge = new BoutJudge();
            judge.Submit(Claim(BoutSide.Partner, Contact, TargetZone.Body), Contact + 0.05);

            Assert.That(judge.Step(Contact + judge.LightDelaySeconds).Value.Kind, Is.EqualTo(LightKind.PartnerTouch));
        }

        [Test]
        public void ALateClaimThatWasParriedNeverMakesADouble()
        {
            var judge = new BoutJudge();
            var referee = new PretendReferee();
            referee.Rulings[(BoutSide.Host, 1)] = ParryRuling.Touch;
            referee.Rulings[(BoutSide.Partner, 1)] = ParryRuling.Parried;
            judge.Submit(Claim(BoutSide.Host, Contact, TargetZone.Body), Contact);
            var single = judge.Step(Contact + judge.LightDelaySeconds, referee);
            Assert.That(single.Value.Kind, Is.EqualTo(LightKind.HostTouch));

            // The Partner's claim within the double window arrives after the single lit; the replay rules it Parried.
            Assert.That(judge.Submit(Claim(BoutSide.Partner, Contact + 0.02, TargetZone.Forearm), Contact + 0.15), Is.EqualTo(ClaimVerdict.MadeDouble));
            var verdict = judge.Step(Contact + 0.16, referee);

            Assert.That(verdict.Value.Kind, Is.EqualTo(LightKind.Parried));
            Assert.That(judge.Step(Contact + 0.2, referee), Is.Null, "No double after all.");
        }

        [Test]
        public void ASlowLinkLengthensTheStaleClaimLimitWithinItsBounds()
        {
            var judge = new BoutJudge();
            Assert.That(judge.StaleClaimLimitSeconds, Is.EqualTo(BoutJudge.StaleClaimSeconds));

            judge.SetRoundTrip(0.1);
            Assert.That(judge.StaleClaimLimitSeconds, Is.EqualTo(0.2).Within(1e-9), "A quick link keeps the decided 200 ms.");

            judge.SetRoundTrip(0.4);
            Assert.That(judge.StaleClaimLimitSeconds, Is.EqualTo(0.3).Within(1e-9), "200 ms one way: 300 ms.");
            Assert.That(judge.Submit(Claim(BoutSide.Partner, Contact, TargetZone.Body), Contact + 0.25), Is.EqualTo(ClaimVerdict.Accepted));
            Assert.That(judge.MaxRulingWaitSeconds, Is.EqualTo(0.4).Within(1e-9));

            judge.SetRoundTrip(5.0);
            Assert.That(judge.StaleClaimLimitSeconds, Is.EqualTo(BoutJudge.MaxStaleClaimSeconds));
            judge.SetRoundTrip(null);
            Assert.That(judge.StaleClaimLimitSeconds, Is.EqualTo(BoutJudge.StaleClaimSeconds));
        }

        [Test]
        public void TheVerdictTravelsAndShowsAsANoteUnderTheBoxWithTheLampsAsTheyWere()
        {
            var verdict = BoutLight.Parried(4, new TouchClaim(2, BoutSide.Partner, TargetZone.Thigh, Contact, false));

            var decoded = BoutMessages.DecodeLight(BoutMessages.EncodeLight(verdict));

            Assert.That(decoded.Kind, Is.EqualTo(LightKind.Parried));
            Assert.That(decoded.ParriedAttacker, Is.EqualTo(BoutSide.Partner));
            Assert.That(decoded.PartnerZone, Is.EqualTo(TargetZone.Thigh));
            Assert.That(BoutMessages.FormatVersion, Is.EqualTo(2));

            var partnerBox = new ScoringBox(BoutSide.Partner);
            var hostBox = new ScoringBox(BoutSide.Host);
            partnerBox.Show(new BoutLight(3, LightKind.HostTouch, TargetZone.Body, null), Contact);
            Assert.That(partnerBox.Show(decoded, Contact + 0.5), Is.False, "Nobody is touched by a parry.");
            hostBox.Show(decoded, Contact + 0.5);

            Assert.That(partnerBox.GetParriedNote(Contact + 1.0), Is.EqualTo("Parried: your Touch"));
            Assert.That(hostBox.GetParriedNote(Contact + 1.0), Is.EqualTo("You parried"));
            Assert.That(partnerBox.IsOpponentsLampLit(Contact + 1.0), Is.True, "The lamp already lit stays lit.");
            Assert.That(partnerBox.GetParriedNote(Contact + 0.5 + ScoringBox.ParriedNoteSeconds), Is.Empty);
            Assert.That(ScoringBox.Describe(decoded, BoutSide.Partner), Does.StartWith("Parried: your Touch"));
        }

        private static TouchClaim Claim(BoutSide attacker, double contactSeconds, TargetZone zone)
        {
            return new TouchClaim(1, attacker, zone, contactSeconds, isOffStrip: false);
        }

        /// <summary>Rules as told; any other claim is not reached yet.</summary>
        private sealed class PretendReferee : IParryReferee
        {
            public Dictionary<(BoutSide, int), ParryRuling> Rulings { get; } = new Dictionary<(BoutSide, int), ParryRuling>();

            public ParryRuling Rule(TouchClaim claim)
            {
                return Rulings.TryGetValue((claim.Attacker, claim.Sequence), out var ruling) ? ruling : ParryRuling.NotYet;
            }
        }
    }
}
