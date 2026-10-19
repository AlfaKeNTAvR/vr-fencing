// Dummy layout (PER-32): Target Zones as capsules for an en garde épée opponent, scaled to height,
// mirrored for a left-handed Fencer, and turned to face the Fencer on the Strip.
using System;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Tests.Dummy
{
    public class DummyLayoutTests
    {
        private const float Tolerance = 1e-4f;
        private const float DefaultHeight = 1.65f;

        [Test]
        public void HasOneShapePerTargetZone()
        {
            var layout = DummyLayout.Create(DefaultHeight, Hand.Right);

            var allZones = Enum.GetValues(typeof(TargetZone)).Cast<TargetZone>();
            Assert.That(layout.Zones.Select(zone => zone.Zone), Is.EquivalentTo(allZones));
        }

        [Test]
        public void TheBodyOnTheStripIsEveryZoneAndSilhouettePartAtTheDistance()
        {
            var layout = DummyLayout.Create(DefaultHeight, Hand.Right);

            var body = layout.GetBodyOnStrip(2.0f);

            Assert.That(body.Count, Is.EqualTo(layout.Zones.Count + layout.SilhouetteParts.Count));
            var head = layout.SilhouetteParts[0];
            Assert.That(body.Any(part => Vector3.Distance(part.Start, DummyLayout.DummyToStrip(head.Start, 2.0f)) < Tolerance), Is.True, "The head is in the body.");
            var zonesOnStrip = layout.GetZonesOnStrip(2.0f);
            Assert.That(body.Take(zonesOnStrip.Count).Select(part => part.Start), Is.EqualTo(zonesOnStrip.Select(zone => zone.Shape.Start)));
        }

        [Test]
        public void ZonePositionsAndSizesScaleWithHeight()
        {
            var shorter = DummyLayout.Create(1.5f, Hand.Right);
            var taller = DummyLayout.Create(1.8f, Hand.Right);
            const float ratio = 1.8f / 1.5f;

            foreach (var zone in shorter.Zones)
            {
                var tallerShape = taller.GetZone(zone.Zone);
                AssertClose(tallerShape.Start, zone.Shape.Start * ratio, zone.Zone);
                AssertClose(tallerShape.End, zone.Shape.End * ratio, zone.Zone);
                Assert.That(tallerShape.RadiusMeters, Is.EqualTo(zone.Shape.RadiusMeters * ratio).Within(Tolerance), zone.Zone.ToString());
            }
        }

        [Test]
        public void SilhouetteIsAboutAsTallAsTheHeightInACrouch()
        {
            var layout = DummyLayout.Create(DefaultHeight, Hand.Right);

            var top = layout.SilhouetteParts.Concat(layout.Zones.Select(zone => zone.Shape))
                .Max(capsule => Math.Max(capsule.Start.Y, capsule.End.Y) + capsule.RadiusMeters);

            Assert.That(top, Is.InRange(0.9f * DefaultHeight, DefaultHeight));
        }

        [Test]
        public void ArmZonesRunFromShoulderToHandTowardTheFencer()
        {
            var layout = DummyLayout.Create(DefaultHeight, Hand.Right);

            // In the Dummy frame +z points at the Fencer.
            var upperArm = layout.GetZone(TargetZone.UpperArm).Center.Z;
            var forearm = layout.GetZone(TargetZone.Forearm).Center.Z;
            var wrist = layout.GetZone(TargetZone.Wrist).Center.Z;
            var hand = layout.GetZone(TargetZone.Hand).Center.Z;
            Assert.That(new[] { upperArm, forearm, wrist, hand }, Is.Ordered.Ascending);
        }

        [Test]
        public void HandIsTheZoneNearestTheFencerAndBodyTheFarthest()
        {
            var layout = DummyLayout.Create(DefaultHeight, Hand.Right);

            var byNearness = layout.Zones.OrderByDescending(zone => zone.Shape.Center.Z).Select(zone => zone.Zone).ToArray();

            Assert.That(byNearness.First(), Is.EqualTo(TargetZone.Hand));
            Assert.That(byNearness.Last(), Is.EqualTo(TargetZone.Body));
        }

        [Test]
        public void LowZonesAreBelowTheBodyAndTheFootIsOnTheFloor()
        {
            var layout = DummyLayout.Create(DefaultHeight, Hand.Right);

            var foot = layout.GetZone(TargetZone.Foot);
            Assert.That(foot.Center.Y - foot.RadiusMeters, Is.EqualTo(0.0f).Within(0.01f));
            Assert.That(layout.GetZone(TargetZone.Thigh).Center.Y, Is.LessThan(layout.GetZone(TargetZone.Body).Center.Y));
        }

        [Test]
        public void RightHandedDummyHoldsItsArmOnItsRight()
        {
            var layout = DummyLayout.Create(DefaultHeight, Hand.Right);

            Assert.That(layout.GetZone(TargetZone.UpperArm).Center.X, Is.GreaterThan(0.0f));
        }

        [Test]
        public void LeftHandedLayoutIsTheRightHandedOneMirrored()
        {
            var right = DummyLayout.Create(DefaultHeight, Hand.Right);
            var left = DummyLayout.Create(DefaultHeight, Hand.Left);

            foreach (var zone in right.Zones)
            {
                var leftShape = left.GetZone(zone.Zone);
                AssertClose(leftShape.Start, HandMirroring.MirrorPosition(zone.Shape.Start), zone.Zone);
                AssertClose(leftShape.End, HandMirroring.MirrorPosition(zone.Shape.End), zone.Zone);
            }

            Assert.That(left.Hand, Is.EqualTo(Hand.Left));
        }

        [Test]
        public void ZonesOnTheStripStandAtTheDistanceFacingTheFencer()
        {
            var layout = DummyLayout.Create(DefaultHeight, Hand.Right);
            const float distance = 2.0f;

            var onStrip = layout.GetZonesOnStrip(distance).ToDictionary(zone => zone.Zone, zone => zone.Shape);

            // The front foot sits on the distance mark; the hand reaches toward the Fencer, the body stays behind.
            Assert.That(onStrip[TargetZone.Foot].Center.Z, Is.EqualTo(distance).Within(Tolerance));
            Assert.That(onStrip[TargetZone.Hand].Center.Z, Is.LessThan(distance));
            Assert.That(onStrip[TargetZone.Body].Center.Z, Is.GreaterThan(distance));

            // Facing each other, the Dummy's right (its sword side) is on the Fencer's left.
            Assert.That(onStrip[TargetZone.UpperArm].Center.X, Is.LessThan(0.0f));
            Assert.That(onStrip[TargetZone.Hand].RadiusMeters, Is.EqualTo(layout.GetZone(TargetZone.Hand).RadiusMeters));
        }

        [TestCase(0.0f)]
        [TestCase(-1.7f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void RejectsAHeightThatIsNotPositive(float height)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => DummyLayout.Create(height, Hand.Right));
        }

        private static void AssertClose(Vector3 actual, Vector3 expected, TargetZone zone)
        {
            Assert.That(Vector3.Distance(actual, expected), Is.LessThan(Tolerance), $"{zone}: {actual} vs {expected}");
        }
    }
}
