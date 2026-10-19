// The results panel's words and numbers (PER-27): summary, hit map and the last-10-sessions trend bars.
using System;
using System.Linq;
using NUnit.Framework;
using VrFencing.Drills;
using VrFencing.Dummy;
using VrFencing.Session;
using VrFencing.Tests.Session;
using VrFencing.UI;

namespace VrFencing.Tests.UI
{
    public class ResultsTextTests
    {
        [Test]
        public void SummaryShowsSuccessReactionAndTipSpeed()
        {
            var results = DrillResults.Aggregate(new[]
            {
                SessionTestData.Touch(TargetZone.Wrist, 0.40, 3.0f),
                SessionTestData.Touch(TargetZone.Wrist, 0.50, 4.0f, repIndex: 1),
                SessionTestData.Miss(TargetZone.Body, RepOutcome.Timeout, repIndex: 2),
                SessionTestData.Miss(TargetZone.Body, RepOutcome.WrongZone, 0.3, 2.0f, repIndex: 3),
            });

            var lines = ResultsText.FormatSummary(results, wasCancelled: false);

            Assert.That(lines, Is.EqualTo(new[]
            {
                "Success: 2 of 4 Reps (50%)",
                "Average reaction time: 0.45 s",
                "Average tip speed: 3.5 m/s",
            }));
        }

        [Test]
        public void SummarySaysNoTouchesWhenNothingLanded()
        {
            var results = DrillResults.Aggregate(new[] { SessionTestData.Miss(TargetZone.Body, RepOutcome.Timeout) });

            var lines = ResultsText.FormatSummary(results, wasCancelled: false);

            Assert.That(lines[1], Does.EndWith(ResultsText.NoTouchesText));
            Assert.That(lines[2], Does.EndWith(ResultsText.NoTouchesText));
        }

        [Test]
        public void SummaryMarksCancelledAndShortenedRuns()
        {
            var results = DrillResults.Aggregate(new[] { SessionTestData.Touch(TargetZone.Body, 0.4, 3.0f, isShrunk: true) });

            var lines = ResultsText.FormatSummary(results, wasCancelled: true);

            Assert.That(lines, Has.Some.EqualTo("Cancelled after 1 Rep"));
            Assert.That(lines, Has.Some.StartsWith("Shortened Drill"));
        }

        [Test]
        public void HitMapListsEachCuedZoneWithItsTouches()
        {
            var results = DrillResults.Aggregate(new[]
            {
                SessionTestData.Touch(TargetZone.UpperArm, 0.4, 3.0f),
                SessionTestData.Miss(TargetZone.UpperArm, RepOutcome.FlatContact, 0.4, 3.0f, repIndex: 1),
                SessionTestData.Touch(TargetZone.Body, 0.4, 3.0f, repIndex: 2),
            });

            var hitMap = ResultsText.FormatHitMap(results);

            Assert.That(hitMap.Select(line => line.ZoneName), Is.EqualTo(new[] { "Upper arm", "Body" }));
            Assert.That(hitMap[0].CountText, Is.EqualTo("Touched 1 of 2"));
            Assert.That(hitMap[0].SuccessFraction, Is.EqualTo(0.5f));
        }

        [Test]
        public void TrendBarsAreDatedInLocalTimeOldestFirst()
        {
            var lateEvening = new DateTimeOffset(2026, 10, 19, 23, 30, 0, TimeSpan.Zero);
            var history = new SessionHistory(new[]
            {
                SessionTestData.Run("s1", lateEvening, repCount: 10, successCount: 7),
                SessionTestData.Run("s2", lateEvening.AddDays(1), repCount: 4, successCount: 1),
            });

            var bars = ResultsText.FormatTrend(history.GetTrend(SessionTestData.DrillId, isShrunk: false), TimeSpan.FromHours(2));

            Assert.That(bars.Select(bar => bar.DateLabel), Is.EqualTo(new[] { "10/20", "10/21" }), "23:30 UTC is the next day at UTC+2.");
            Assert.That(bars.Select(bar => bar.ValueLabel), Is.EqualTo(new[] { "70%", "25%" }));
            Assert.That(bars[0].SuccessFraction, Is.EqualTo(0.7f).Within(1e-5f));
        }
    }
}
