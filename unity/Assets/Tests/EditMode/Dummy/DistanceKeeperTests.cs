// The moving Dummy's distance keeper (PER-105, stage 1 of PER-44): it holds the Dummy Distance as the Fencer advances
// and retreats, one reaction delay late (Easy 600, Medium 400, Hard 250 ms plus jitter), inside the Strip's ends.
using System;
using NUnit.Framework;
using VrFencing.Dummy;

namespace VrFencing.Tests.Dummy
{
    public class DistanceKeeperTests
    {
        private const float Gap = 1.9f;
        private const float Step = 0.33f;
        private const double FrameSeconds = 1.0 / 72.0;
        private const float Tolerance = 1e-4f;

        /// <summary>A random source whose draws all sit mid-range, so every reaction comes exactly at the mean delay.</summary>
        private sealed class MidRandom : Random
        {
            private readonly double _value;

            public MidRandom(double value = 0.5)
            {
                _value = value;
            }

            public override double NextDouble()
            {
                return _value;
            }
        }

        private static DistanceKeeper Create(
            DummyDifficulty difficulty = DummyDifficulty.Medium, DummyStripLimits? limits = null, Random random = null)
        {
            return new DistanceKeeper(Gap, Gap, limits ?? DummyStripLimits.Unlimited, difficulty, random ?? new MidRandom());
        }

        /// <summary>Feeds the Fencer's front foot at <paramref name="frontFootZ"/> every frame from <paramref name="fromSeconds"/> for <paramref name="seconds"/>.</summary>
        private static double Hold(DistanceKeeper keeper, double fromSeconds, double seconds, float? frontFootZ)
        {
            var now = fromSeconds;
            var end = fromSeconds + seconds;
            while (now < end - 1e-9)
            {
                now += FrameSeconds;
                keeper.Step(now, frontFootZ);
            }

            return now;
        }

        [Test]
        public void TheDifficultiesStartAtTheTicketsDelays()
        {
            Assert.That(DistanceKeeperTuning.GetReaction(DummyDifficulty.Easy).DelaySeconds, Is.EqualTo(0.6f));
            Assert.That(DistanceKeeperTuning.GetReaction(DummyDifficulty.Medium).DelaySeconds, Is.EqualTo(0.4f));
            Assert.That(DistanceKeeperTuning.GetReaction(DummyDifficulty.Hard).DelaySeconds, Is.EqualTo(0.25f));
            foreach (DummyDifficulty difficulty in Enum.GetValues(typeof(DummyDifficulty)))
            {
                var timing = DistanceKeeperTuning.GetReaction(difficulty);
                Assert.That(timing.JitterSeconds, Is.GreaterThan(0.0f).And.LessThan(timing.DelaySeconds), difficulty.ToString());
            }
        }

        [Test]
        public void EachReactionLandsWithinTheJitterOfTheDelay()
        {
            var timing = DistanceKeeperTuning.GetReaction(DummyDifficulty.Easy);
            var random = new Random(3);
            var earliest = float.MaxValue;
            var latest = float.MinValue;
            for (var draw = 0; draw < 2000; draw++)
            {
                var delay = timing.DrawDelaySeconds(random);
                earliest = Math.Min(earliest, delay);
                latest = Math.Max(latest, delay);
            }

            Assert.That(earliest, Is.GreaterThanOrEqualTo(timing.DelaySeconds - timing.JitterSeconds));
            Assert.That(latest, Is.LessThanOrEqualTo(timing.DelaySeconds + timing.JitterSeconds));
            Assert.That(latest - earliest, Is.GreaterThan(timing.JitterSeconds), "The delay really varies, so the Fencer cannot time it.");
        }

        [Test]
        public void ItStartsWhereTheDummyStandsAndHolds()
        {
            var keeper = Create();

            Hold(keeper, 0.0, 1.0, 0.0f);

            Assert.That(keeper.TargetFrontFootZ, Is.EqualTo(Gap));
            Assert.That(keeper.State, Is.EqualTo(DistanceKeeperState.Holding));
        }

        [Test]
        public void SwayAndAnExtensionWithinTheToleranceNeverMoveIt()
        {
            var keeper = Create();
            var now = 0.0;
            for (var frame = 0; frame < 300; frame++)
            {
                now += FrameSeconds;
                var sway = (float)Math.Sin(frame * 0.2) * (DistanceKeeperTuning.ToleranceMeters * 0.9f);
                keeper.Step(now, sway);
            }

            Assert.That(keeper.TargetFrontFootZ, Is.EqualTo(Gap));
            Assert.That(keeper.State, Is.EqualTo(DistanceKeeperState.Holding));
        }

        [Test]
        public void AnAdvanceIsAnsweredWithARetreatOneReactionDelayLate()
        {
            var keeper = Create(DummyDifficulty.Medium);
            var now = Hold(keeper, 0.0, 0.5, 0.0f);
            var advancedAt = now;

            now = Hold(keeper, now, 0.39, Step);
            Assert.That(keeper.State, Is.EqualTo(DistanceKeeperState.Reacting));
            Assert.That(keeper.TargetFrontFootZ, Is.EqualTo(Gap), "Nothing moves before the 400 ms reaction.");

            now = Hold(keeper, now, 0.05, Step);
            Assert.That(now - advancedAt, Is.GreaterThan(0.4));
            Assert.That(keeper.TargetFrontFootZ, Is.EqualTo(Gap + Step).Within(Tolerance), "It retreats as far as the Fencer advanced.");

            Hold(keeper, now, 0.5, Step);
            Assert.That(keeper.State, Is.EqualTo(DistanceKeeperState.Holding));
            Assert.That(keeper.TargetFrontFootZ - Step, Is.EqualTo(Gap).Within(Tolerance), "The Dummy Distance is held again.");
        }

        [Test]
        public void ARetreatIsAnsweredWithAnAdvance()
        {
            var keeper = Create(DummyDifficulty.Hard);
            var now = Hold(keeper, 0.0, 0.5, 0.0f);

            Hold(keeper, now, 1.0, -Step);

            Assert.That(keeper.TargetFrontFootZ, Is.EqualTo(Gap - Step).Within(Tolerance));
            Assert.That(keeper.State, Is.EqualTo(DistanceKeeperState.Holding));
        }

        [TestCase(DummyDifficulty.Easy, 0.6)]
        [TestCase(DummyDifficulty.Medium, 0.4)]
        [TestCase(DummyDifficulty.Hard, 0.25)]
        public void ItFollowsAMovingFencerAsSeenOneDelayAgo(DummyDifficulty difficulty, double delaySeconds)
        {
            const float speed = 0.5f;
            var keeper = Create(difficulty);
            var now = 0.0;
            for (var frame = 0; frame < 144; frame++)
            {
                now += FrameSeconds;
                keeper.Step(now, (float)(now * speed));
            }

            Assert.That(keeper.State, Is.EqualTo(DistanceKeeperState.Following));
            var fencerOneDelayAgo = (float)((now - delaySeconds) * speed);
            Assert.That(keeper.TargetFrontFootZ, Is.EqualTo(fencerOneDelayAgo + Gap).Within(0.002f));
        }

        [Test]
        public void TheHarderDummyReactsSooner()
        {
            double FirstMoveAfter(DummyDifficulty difficulty)
            {
                var keeper = Create(difficulty);
                var now = Hold(keeper, 0.0, 0.2, 0.0f);
                var advancedAt = now;
                for (var frame = 0; frame < 144 && keeper.TargetFrontFootZ == Gap; frame++)
                {
                    now += FrameSeconds;
                    keeper.Step(now, Step);
                }

                return now - advancedAt;
            }

            var easy = FirstMoveAfter(DummyDifficulty.Easy);
            var medium = FirstMoveAfter(DummyDifficulty.Medium);
            var hard = FirstMoveAfter(DummyDifficulty.Hard);

            Assert.That(new[] { hard, medium, easy }, Is.Ordered.Ascending);
            // The reaction starts on the first frame that sees the advance and ends on a frame boundary.
            Assert.That(easy, Is.InRange(0.6, 0.6 + (2.0 * FrameSeconds) + 1e-6));
            Assert.That(hard, Is.InRange(0.25, 0.25 + (2.0 * FrameSeconds) + 1e-6));
        }

        [Test]
        public void ACheckIsCopiedLateAndTheDummyComesBack()
        {
            var keeper = Create(DummyDifficulty.Hard);
            var now = Hold(keeper, 0.0, 0.5, 0.0f);

            now = Hold(keeper, now, 0.15, 0.1f);
            now = Hold(keeper, now, 0.1, 0.0f);
            var peak = Gap;
            for (var frame = 0; frame < 72; frame++)
            {
                now += FrameSeconds;
                keeper.Step(now, 0.0f);
                peak = Math.Max(peak, keeper.TargetFrontFootZ);
            }

            Assert.That(peak, Is.GreaterThan(Gap + 0.05f), "The check drew a reaction.");
            Assert.That(keeper.TargetFrontFootZ, Is.EqualTo(Gap).Within(Tolerance));
            Assert.That(keeper.State, Is.EqualTo(DistanceKeeperState.Holding));
        }

        [Test]
        public void AtTheFarEndItStaysOnTheStripAndTheFencerClosesTheDistance()
        {
            var limits = new DummyStripLimits(0.0f, Gap + 0.2f);
            var keeper = Create(DummyDifficulty.Hard, limits);
            var now = Hold(keeper, 0.0, 0.5, 0.0f);

            Hold(keeper, now, 2.0, 0.66f);

            Assert.That(keeper.TargetFrontFootZ, Is.EqualTo(Gap + 0.2f));
            Assert.That(keeper.TargetFrontFootZ - 0.66f, Is.LessThan(Gap), "Cornered, the Dummy no longer holds its distance.");
            Assert.That(keeper.State, Is.EqualTo(DistanceKeeperState.Holding));
        }

        [Test]
        public void AtTheNearEndItStaysOnTheStrip()
        {
            var limits = new DummyStripLimits(Gap - 0.1f, 10.0f);
            var keeper = Create(DummyDifficulty.Hard, limits);
            var now = Hold(keeper, 0.0, 0.5, 0.0f);

            Hold(keeper, now, 2.0, -1.0f);

            Assert.That(keeper.TargetFrontFootZ, Is.EqualTo(Gap - 0.1f));
        }

        [Test]
        public void AStartBeyondTheLimitsStartsAtTheLimit()
        {
            var keeper = new DistanceKeeper(Gap, Gap, new DummyStripLimits(0.0f, 1.0f), DummyDifficulty.Easy, new MidRandom());

            Assert.That(keeper.TargetFrontFootZ, Is.EqualTo(1.0f));
        }

        [Test]
        public void WithoutAHeadsetPoseNothingMoves()
        {
            var keeper = Create(DummyDifficulty.Hard);
            var now = Hold(keeper, 0.0, 1.0, null);
            Assert.That(keeper.TargetFrontFootZ, Is.EqualTo(Gap));

            now = Hold(keeper, now, 0.5, 0.0f);
            Hold(keeper, now, 1.0, null);
            Assert.That(keeper.TargetFrontFootZ, Is.EqualTo(Gap));
            Assert.That(keeper.State, Is.EqualTo(DistanceKeeperState.Holding));
        }

        [Test]
        public void ADifficultyChangeAppliesFromTheNextReaction()
        {
            var keeper = Create(DummyDifficulty.Easy);
            var now = Hold(keeper, 0.0, 0.2, 0.0f);
            now = Hold(keeper, now, 0.1, Step);
            Assert.That(keeper.State, Is.EqualTo(DistanceKeeperState.Reacting));

            keeper.SetDifficulty(DummyDifficulty.Hard);
            Hold(keeper, now, 0.3, Step);

            Assert.That(keeper.ReactionDelaySeconds, Is.EqualTo(0.6f), "The reaction under way keeps its delay.");
            Assert.That(keeper.TargetFrontFootZ, Is.EqualTo(Gap), "Still not reacted after 0.4 s at Easy.");
            Assert.That(keeper.Difficulty, Is.EqualTo(DummyDifficulty.Hard));
        }

        [Test]
        public void JitterChangesWhenTheReactionComes()
        {
            var early = Create(DummyDifficulty.Medium, random: new MidRandom(0.0));
            var late = Create(DummyDifficulty.Medium, random: new MidRandom(0.999));
            foreach (var keeper in new[] { early, late })
            {
                var now = Hold(keeper, 0.0, 0.2, 0.0f);
                Hold(keeper, now, 0.05, Step);
            }

            Assert.That(early.ReactionDelaySeconds, Is.EqualTo(0.4f - DistanceKeeperTuning.MediumReactionJitterSeconds).Within(Tolerance));
            Assert.That(late.ReactionDelaySeconds, Is.EqualTo(0.4f + DistanceKeeperTuning.MediumReactionJitterSeconds).Within(0.001f));
        }

        [Test]
        public void TimeGoingBackwardsIsRejected()
        {
            var keeper = Create();
            keeper.Step(1.0, 0.0f);

            Assert.Throws<ArgumentException>(() => keeper.Step(0.5, 0.0f));
        }

        [Test]
        public void ANegativeGapIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new DistanceKeeper(-0.1f, 0.0f, DummyStripLimits.Unlimited, DummyDifficulty.Easy, new Random(1)));
        }

        [Test]
        public void OnAStripTheBackHeelStaysOnTheFarEndAndTheToeOnTheRearEnd()
        {
            const float height = 1.65f;
            var limits = DummyStripLimits.ForStrip(rearEndZ: -0.8f, farEndZ: 3.2f, height);

            Assert.That(limits.FarthestFrontFootZ, Is.EqualTo(3.2f + (EnGardePose.BackHeel.Z * height)).Within(Tolerance));
            Assert.That(limits.FarthestFrontFootZ, Is.LessThan(3.2f));
            Assert.That(limits.NearestFrontFootZ, Is.EqualTo(0.0f), "Never behind the Fencer's En Garde Line.");
        }

        [Test]
        public void AStripTooShortForTheStancePinsTheDummy()
        {
            var limits = DummyStripLimits.ForStrip(rearEndZ: -0.1f, farEndZ: 0.2f, heightMeters: 1.65f);

            Assert.That(limits.NearestFrontFootZ, Is.EqualTo(limits.FarthestFrontFootZ));
            Assert.That(limits.Clamp(5.0f), Is.GreaterThanOrEqualTo(0.0f));
        }
    }
}
