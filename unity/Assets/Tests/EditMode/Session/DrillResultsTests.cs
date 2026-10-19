// Per-Drill results (PER-35, PER-27): success %, average reaction time and tip speed, and the per-zone hit map.
using System;
using System.Linq;
using NUnit.Framework;
using VrFencing.Drills;
using VrFencing.Dummy;
using VrFencing.Session;
using static VrFencing.Tests.Session.SessionTestData;

namespace VrFencing.Tests.Session
{
    public class DrillResultsTests
    {
        private const double Tolerance = 1e-6;

        [Test]
        public void SuccessPercentIsTouchesOverReps()
        {
            var results = DrillResults.Aggregate(new[]
            {
                Touch(TargetZone.Thigh, 0.4, 3.0f),
                Touch(TargetZone.Thigh, 0.5, 3.0f),
                Touch(TargetZone.Hand, 0.6, 3.0f),
                Miss(TargetZone.Hand, RepOutcome.Timeout),
            });

            Assert.That(results.RepCount, Is.EqualTo(4));
            Assert.That(results.SuccessCount, Is.EqualTo(3));
            Assert.That(results.SuccessPercent, Is.EqualTo(75.0).Within(Tolerance));
        }

        [Test]
        public void AveragesCountOnlySuccessfulReps()
        {
            var results = DrillResults.Aggregate(new[]
            {
                Touch(TargetZone.Thigh, 0.4, 2.0f),
                Touch(TargetZone.Thigh, 0.6, 4.0f),
                Miss(TargetZone.Thigh, RepOutcome.WrongZone, reactionSeconds: 0.1, tipSpeed: 9.0f),
                Miss(TargetZone.Thigh, RepOutcome.FlatContact, reactionSeconds: 0.2, tipSpeed: 8.0f),
            });

            Assert.That(results.AverageReactionSeconds, Is.EqualTo(0.5).Within(Tolerance));
            Assert.That(results.AverageTipSpeedMetersPerSecond, Is.EqualTo(3.0).Within(Tolerance));
        }

        [Test]
        public void AveragesAreEmptyWhenNoRepSucceeded()
        {
            var results = DrillResults.Aggregate(new[]
            {
                Miss(TargetZone.Thigh, RepOutcome.Timeout),
                Miss(TargetZone.Thigh, RepOutcome.WrongZone, reactionSeconds: 0.3, tipSpeed: 2.0f),
            });

            Assert.That(results.SuccessPercent, Is.EqualTo(0.0));
            Assert.That(results.AverageReactionSeconds, Is.Null);
            Assert.That(results.AverageTipSpeedMetersPerSecond, Is.Null);
        }

        [Test]
        public void HitMapCountsAttemptsAndTouchesPerCuedZone()
        {
            var results = DrillResults.Aggregate(new[]
            {
                Touch(TargetZone.Thigh, 0.4, 3.0f),
                Miss(TargetZone.Thigh, RepOutcome.WrongZone, 0.4, 3.0f),
                Touch(TargetZone.Hand, 0.4, 3.0f),
                Touch(TargetZone.Hand, 0.4, 3.0f),
                Miss(TargetZone.Foot, RepOutcome.Timeout),
            });

            Assert.That(results.HitsFor(TargetZone.Thigh), Is.EqualTo(new ZoneHitCount(TargetZone.Thigh, attempts: 2, touches: 1)));
            Assert.That(results.HitsFor(TargetZone.Hand), Is.EqualTo(new ZoneHitCount(TargetZone.Hand, attempts: 2, touches: 2)));
            Assert.That(results.HitsFor(TargetZone.Foot), Is.EqualTo(new ZoneHitCount(TargetZone.Foot, attempts: 1, touches: 0)));
            Assert.That(results.HitsFor(TargetZone.Body), Is.EqualTo(new ZoneHitCount(TargetZone.Body, attempts: 0, touches: 0)));
        }

        [Test]
        public void HitMapListsOnlyCuedZonesInTargetZoneOrder()
        {
            var results = DrillResults.Aggregate(new[]
            {
                Touch(TargetZone.Foot, 0.4, 3.0f),
                Touch(TargetZone.Hand, 0.4, 3.0f),
                Touch(TargetZone.Thigh, 0.4, 3.0f),
            });

            Assert.That(
                results.ZoneHits.Select(hits => hits.Zone).ToArray(),
                Is.EqualTo(new[] { TargetZone.Hand, TargetZone.Thigh, TargetZone.Foot }));
        }

        [Test]
        public void ShrunkRepsMakeShrunkResults()
        {
            var results = DrillResults.Aggregate(new[] { Touch(TargetZone.Thigh, 0.4, 3.0f, isShrunk: true) });

            Assert.That(results.DrillId, Is.EqualTo(DrillId));
            Assert.That(results.IsShrunk, Is.True);
        }

        [Test]
        public void AggregateRejectsNoRepsAndMixedDrills()
        {
            Assert.Throws<ArgumentException>(() => DrillResults.Aggregate(Array.Empty<RepLogEntry>()));
            Assert.Throws<ArgumentException>(() => DrillResults.Aggregate(new[]
            {
                Touch(TargetZone.Thigh, 0.4, 3.0f),
                Touch(TargetZone.Thigh, 0.4, 3.0f, drillId: "point-control"),
            }));
        }

        [Test]
        public void CombineWeightsAveragesBySuccessfulReps()
        {
            // 1 Touch at 0.2 s and 3 Touches at 0.6 s: the combined average is 0.5 s, not the 0.4 s mean of means.
            var first = DrillResults.Aggregate(new[]
            {
                Touch(TargetZone.Thigh, 0.2, 2.0f),
                Miss(TargetZone.Thigh, RepOutcome.Timeout),
            });
            var second = DrillResults.Aggregate(new[]
            {
                Touch(TargetZone.Thigh, 0.6, 4.0f),
                Touch(TargetZone.Thigh, 0.6, 4.0f),
                Touch(TargetZone.Thigh, 0.6, 4.0f),
                Miss(TargetZone.Hand, RepOutcome.Timeout),
            });

            var combined = DrillResults.Combine(new[] { first, second });

            Assert.That(combined.RepCount, Is.EqualTo(6));
            Assert.That(combined.SuccessCount, Is.EqualTo(4));
            Assert.That(combined.AverageReactionSeconds, Is.EqualTo(0.5).Within(Tolerance));
            Assert.That(combined.AverageTipSpeedMetersPerSecond, Is.EqualTo(3.5).Within(Tolerance));
            Assert.That(combined.HitsFor(TargetZone.Thigh), Is.EqualTo(new ZoneHitCount(TargetZone.Thigh, attempts: 5, touches: 4)));
            Assert.That(combined.HitsFor(TargetZone.Hand), Is.EqualTo(new ZoneHitCount(TargetZone.Hand, attempts: 1, touches: 0)));
        }

        [Test]
        public void CombineKeepsAveragesEmptyWhenNoRunSucceeded()
        {
            var miss = DrillResults.Aggregate(new[] { Miss(TargetZone.Thigh, RepOutcome.Timeout) });

            var combined = DrillResults.Combine(new[] { miss, miss });

            Assert.That(combined.RepCount, Is.EqualTo(2));
            Assert.That(combined.AverageReactionSeconds, Is.Null);
        }
    }
}
