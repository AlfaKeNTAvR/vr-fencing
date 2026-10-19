// Strip geometry (PER-31, PER-90): Warning Zone scaling, the Strip's extent around the two En Garde Lines, old
// far-end Strips converted to keep their footprint, and which part of the Strip a head position is over.
using System;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Settings;
using VrFencing.Strip;

namespace VrFencing.Tests.Strip
{
    public class StripGeometryTests
    {
        private const float Tolerance = 1e-4f;
        private const float FencerHeightMeters = 1.65f;

        private static readonly WarningZoneSettings DefaultZones = new WarningZoneSettings();

        [TestCase(14.0f, 2.0f)]
        [TestCase(7.0f, 1.0f)]
        [TestCase(5.0f, 5.0f * 2.0f / 14.0f)]
        public void WarningZoneScalesWithLengthLikeThePiste(float stripLengthMeters, float expectedMeters)
        {
            Assert.That(StripGeometry.ScaleWarningZoneLength(stripLengthMeters, DefaultZones), Is.EqualTo(expectedMeters).Within(Tolerance));
        }

        [TestCase(1.0f)]
        [TestCase(2.5f)]
        [TestCase(3.5f)]
        public void WarningZoneNeverGoesBelowTheMinimumOnShortStrips(float stripLengthMeters)
        {
            Assert.That(StripGeometry.ScaleWarningZoneLength(stripLengthMeters, DefaultZones), Is.EqualTo(0.5f).Within(Tolerance));
        }

        [Test]
        public void WarningZoneStopsGrowingAtThePisteLength()
        {
            Assert.That(StripGeometry.ScaleWarningZoneLength(20.0f, DefaultZones), Is.EqualTo(2.0f).Within(Tolerance));
        }

        [Test]
        public void WarningZoneFollowsTheSettings()
        {
            var zones = new WarningZoneSettings { LengthOnPisteMeters = 3.0f, MinimumLengthMeters = 0.75f };

            Assert.That(StripGeometry.ScaleWarningZoneLength(14.0f, zones), Is.EqualTo(3.0f).Within(Tolerance));
            Assert.That(StripGeometry.ScaleWarningZoneLength(2.0f, zones), Is.EqualTo(0.75f).Within(Tolerance));
        }

        [Test]
        public void StripRunsOnBehindBothEnGardeLines()
        {
            var geometry = StripGeometry.Create(4.0f, FencerHeightMeters, DefaultZones);

            var stance = StripGeometry.StanceAllowancePerHeight * FencerHeightMeters;
            var runBack = stance + geometry.WarningZoneLengthMeters;
            Assert.That(geometry.DummyEnGardeLineZ, Is.EqualTo(4.0f).Within(Tolerance));
            Assert.That(geometry.WarningZoneLengthMeters, Is.EqualTo(0.5714f).Within(Tolerance));
            Assert.That(geometry.RearEndZ, Is.EqualTo(-runBack).Within(Tolerance));
            Assert.That(geometry.FarEndZ, Is.EqualTo(4.0f + runBack).Within(Tolerance));
            Assert.That(geometry.LengthMeters, Is.EqualTo(geometry.FarEndZ - geometry.RearEndZ).Within(Tolerance));
            Assert.That(geometry.CenterLineZ, Is.EqualTo(2.0f).Within(Tolerance), "The center line is midway between the En Garde Lines.");
        }

        [Test]
        public void ShortestPlacementGivesAboutTwoMetersOfStrip()
        {
            var geometry = StripGeometry.Create(StripGeometry.MinimumPlacedLengthMeters, FencerHeightMeters, DefaultZones);

            Assert.That(geometry.LengthMeters, Is.EqualTo(0.2f + (2.0f * (0.495f + 0.5f))).Within(Tolerance));
        }

        [Test]
        public void DummyEnGardeOnItsLineIsOnTheStripAndOutsideBothZones()
        {
            var geometry = StripGeometry.Create(2.5f, FencerHeightMeters, DefaultZones);

            // Mirror of the Fencer: the Dummy's head over its stance, behind its front foot on its line.
            var head = new Vector3(0.0f, 1.5f, geometry.DummyEnGardeLineZ + (0.16f * FencerHeightMeters));

            Assert.That(geometry.Contains(head, 0.0f), Is.True);
            Assert.That(geometry.GetWarningZone(head), Is.Null);
        }

        [TestCase(4.45f)]
        [TestCase(3.0f)]
        [TestCase(8.0f)]
        [TestCase(20.0f)]
        public void AStripSavedWithItsFarEndKeepsThatFarEnd(float farEndLengthMeters)
        {
            var gap = StripGeometry.ConvertFarEndLengthToGap(farEndLengthMeters, FencerHeightMeters, DefaultZones);

            var geometry = StripGeometry.Create(gap, FencerHeightMeters, DefaultZones);

            Assert.That(geometry.FarEndZ, Is.EqualTo(farEndLengthMeters).Within(1e-3f));
        }

        [Test]
        public void AShortOldStripGetsTheShortestGap()
        {
            // The old minimum: 1 m from the En Garde Line to the far end, shorter than one run-back.
            var gap = StripGeometry.ConvertFarEndLengthToGap(1.0f, FencerHeightMeters, DefaultZones);

            Assert.That(gap, Is.EqualTo(StripGeometry.MinimumPlacedLengthMeters));
        }

        [TestCase(0.19f)]
        [TestCase(0.0f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void RejectsPlacementsShorterThanTheMinimum(float placedLengthMeters)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => StripGeometry.Create(placedLengthMeters, FencerHeightMeters, DefaultZones));
        }

        [Test]
        public void RejectsANonPositiveHeight()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => StripGeometry.Create(3.0f, 0.0f, DefaultZones));
        }

        [Test]
        public void FencerEnGardeOnTheLineIsOnTheStripAndOutsideBothZones()
        {
            var geometry = StripGeometry.Create(2.5f, FencerHeightMeters, DefaultZones);

            // Head over the middle of the stance, as in the Dummy's en garde pose (0.16 of height behind the front foot).
            var head = new Vector3(0.0f, 1.5f, -0.16f * FencerHeightMeters);

            Assert.That(geometry.Contains(head, 0.0f), Is.True);
            Assert.That(geometry.GetWarningZone(head), Is.Null);
        }

        [Test]
        public void ZoneMembershipFollowsThePositionAlongTheStrip()
        {
            var geometry = StripGeometry.Create(5.0f, FencerHeightMeters, DefaultZones);
            var (rearStart, rearEnd) = geometry.GetWarningZoneRange(StripEnd.Rear);
            var (farStart, farEnd) = geometry.GetWarningZoneRange(StripEnd.Far);

            Assert.That(rearStart, Is.EqualTo(geometry.RearEndZ).Within(Tolerance));
            Assert.That(farEnd, Is.EqualTo(geometry.FarEndZ).Within(Tolerance));
            Assert.That(geometry.GetWarningZone(new Vector3(0.0f, 1.5f, rearEnd - 0.01f)), Is.EqualTo(StripEnd.Rear));
            Assert.That(geometry.GetWarningZone(new Vector3(0.0f, 1.5f, rearEnd + 0.01f)), Is.Null);
            Assert.That(geometry.GetWarningZone(new Vector3(0.0f, 1.5f, farStart - 0.01f)), Is.Null);
            Assert.That(geometry.GetWarningZone(new Vector3(0.0f, 1.5f, farStart + 0.01f)), Is.EqualTo(StripEnd.Far));
        }

        [Test]
        public void PastAnEndStillCountsAsThatEndsZone()
        {
            var geometry = StripGeometry.Create(5.0f, FencerHeightMeters, DefaultZones);

            Assert.That(geometry.GetWarningZone(new Vector3(0.0f, 1.5f, geometry.FarEndZ + 0.3f)), Is.EqualTo(StripEnd.Far));
            Assert.That(geometry.GetWarningZone(new Vector3(0.0f, 1.5f, geometry.RearEndZ - 0.3f)), Is.EqualTo(StripEnd.Rear));
        }

        [TestCase(0.74f, 1.0f, 0.0f, true)]
        [TestCase(0.76f, 1.0f, 0.0f, false)]
        [TestCase(-0.76f, 1.0f, 0.0f, false)]
        [TestCase(0.9f, 1.0f, 0.2f, true)]
        [TestCase(0.0f, 5.01f, 0.0f, false)]
        [TestCase(0.0f, 5.19f, 0.2f, true)]
        [TestCase(0.0f, 5.41f, 0.4f, false)]
        public void HeadPastASideOrAnEndByMoreThanTheToleranceIsOffTheStrip(float x, float z, float toleranceMeters, bool expectedOnStrip)
        {
            // The far end at 5 m.
            var geometry = StripGeometry.Create(StripGeometry.ConvertFarEndLengthToGap(5.0f, FencerHeightMeters, DefaultZones), FencerHeightMeters, DefaultZones);

            Assert.That(geometry.Contains(new Vector3(x, 1.7f, z), toleranceMeters), Is.EqualTo(expectedOnStrip));
        }

        [Test]
        public void HeadBehindTheRearEndIsOffTheStrip()
        {
            var geometry = StripGeometry.Create(5.0f, FencerHeightMeters, DefaultZones);

            Assert.That(geometry.Contains(new Vector3(0.0f, 1.7f, geometry.RearEndZ - 0.01f), 0.0f), Is.False);
            Assert.That(geometry.Contains(new Vector3(0.0f, 1.7f, geometry.RearEndZ + 0.01f), 0.0f), Is.True);
        }
    }
}
