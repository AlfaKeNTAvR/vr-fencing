// The shared bout Strip (PER-81, decided in PER-57): the shorter of the two Strips, anchored at each Fencer's own rear
// end, a 4 m En Garde gap shrunk to keep 1 m of retreat, and the partner's poses turned 180 degrees about the centre.
using System;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Partner;
using VrFencing.Settings;
using VrFencing.Strip;

namespace VrFencing.Tests.Partner
{
    public class BoutStripTests
    {
        private const float Tolerance = 1e-4f;

        [Test]
        public void TheBoutIsTheShorterStripAnchoredAtMyRearEnd()
        {
            var bout = BoutStrip.Create(new StripExtent(9.0f, -1.2f), new StripExtent(7.0f, -1.0f));

            Assert.That(bout.LengthMeters, Is.EqualTo(7.0f));
            Assert.That(bout.RearEndZ, Is.EqualTo(-1.2f));
            Assert.That(bout.FarEndZ, Is.EqualTo(5.8f).Within(Tolerance));
            Assert.That(bout.CenterZ, Is.EqualTo(2.3f).Within(Tolerance));
        }

        [Test]
        public void ALongStripKeepsTheFullFourMeterGap()
        {
            var bout = BoutStrip.Create(new StripExtent(10.0f, -1.0f), new StripExtent(12.0f, -1.0f));

            Assert.That(bout.EnGardeGapMeters, Is.EqualTo(BoutStrip.FullEnGardeGapMeters));
            Assert.That(bout.OpponentEnGardeLineZ - bout.MyEnGardeLineZ, Is.EqualTo(4.0f).Within(Tolerance));
            Assert.That(bout.RetreatMeters, Is.EqualTo(3.0f).Within(Tolerance));
        }

        [Test]
        public void AShortStripShrinksTheGapToKeepOneMeterOfRetreatEachSide()
        {
            var bout = BoutStrip.Create(new StripExtent(5.0f, -1.0f), new StripExtent(5.0f, -1.0f));

            Assert.That(bout.EnGardeGapMeters, Is.EqualTo(3.0f).Within(Tolerance));
            Assert.That(bout.RetreatMeters, Is.EqualTo(BoutStrip.MinimumRetreatMeters).Within(Tolerance));
            Assert.That(bout.FarEndZ - bout.OpponentEnGardeLineZ, Is.EqualTo(BoutStrip.MinimumRetreatMeters).Within(Tolerance));
        }

        [Test]
        public void AStripTooShortForRetreatPutsBothLinesAtTheCentre()
        {
            var bout = BoutStrip.Create(new StripExtent(1.5f, -0.5f), new StripExtent(4.0f, -1.0f));

            Assert.That(bout.EnGardeGapMeters, Is.EqualTo(0.0f));
            Assert.That(bout.MyEnGardeLineZ, Is.EqualTo(bout.CenterZ).Within(Tolerance));
        }

        [Test]
        public void ThePartnersRearEndLandsOnMyFarEnd()
        {
            var partner = new StripExtent(8.0f, -1.5f);
            var bout = BoutStrip.Create(new StripExtent(6.0f, -1.0f), partner);

            var partnerRearEnd = bout.ToMyStripFrame(new Vector3(0.0f, 0.0f, partner.RearEndZ));

            Assert.That(partnerRearEnd.Z, Is.EqualTo(bout.FarEndZ).Within(Tolerance));
        }

        [Test]
        public void ThePartnersEnGardeLineLandsOnTheOpponentsLine()
        {
            var mine = new StripExtent(7.0f, -1.0f);
            var partner = new StripExtent(9.0f, -1.3f);
            var myBout = BoutStrip.Create(mine, partner);
            var theirBout = BoutStrip.Create(partner, mine);

            var theirLineSeenHere = myBout.ToMyStripFrame(new Vector3(0.0f, 0.0f, theirBout.MyEnGardeLineZ));

            Assert.That(theirLineSeenHere.Z, Is.EqualTo(myBout.OpponentEnGardeLineZ).Within(Tolerance));
        }

        [Test]
        public void BothFencersSeeTheSameDistanceBetweenThem()
        {
            var mine = new StripExtent(7.0f, -1.0f);
            var partner = new StripExtent(9.0f, -1.3f);
            var myBout = BoutStrip.Create(mine, partner);
            var theirBout = BoutStrip.Create(partner, mine);
            var me = new Vector3(0.1f, 1.6f, 0.4f);
            var them = new Vector3(-0.2f, 1.5f, 1.1f);

            var gapHere = Vector3.Distance(me, myBout.ToMyStripFrame(them));
            var gapThere = Vector3.Distance(them, theirBout.ToMyStripFrame(me));

            Assert.That(gapHere, Is.EqualTo(gapThere).Within(Tolerance));
        }

        [Test]
        public void ThePartnersForwardAndRightTurnToMyBackwardAndLeft()
        {
            var bout = BoutStrip.Create(new StripExtent(6.0f, -1.0f), new StripExtent(6.0f, -1.0f));

            var forward = bout.DirectionToMyStripFrame(Vector3.UnitZ);
            var right = bout.DirectionToMyStripFrame(Vector3.UnitX);
            var pose = bout.ToMyStripFrame(new TrackedPose(Vector3.Zero, Quaternion.Identity));

            Assert.That(forward.Z, Is.EqualTo(-1.0f).Within(Tolerance));
            Assert.That(right.X, Is.EqualTo(-1.0f).Within(Tolerance));
            Assert.That(Vector3.Transform(Vector3.UnitZ, pose.Rotation).Z, Is.EqualTo(-1.0f).Within(Tolerance));
        }

        [Test]
        public void ARightHanderStaysARightHander()
        {
            // A turn, not a mirror: the turned right, up and forward still make a right-handed frame the same way.
            var bout = BoutStrip.Create(new StripExtent(6.0f, -1.0f), new StripExtent(6.0f, -1.0f));
            var right = bout.DirectionToMyStripFrame(Vector3.UnitX);
            var up = bout.DirectionToMyStripFrame(Vector3.UnitY);
            var forward = bout.DirectionToMyStripFrame(Vector3.UnitZ);

            Assert.That(Vector3.Dot(Vector3.Cross(right, up), forward), Is.EqualTo(Vector3.Dot(Vector3.Cross(Vector3.UnitX, Vector3.UnitY), Vector3.UnitZ)).Within(Tolerance));
        }

        [Test]
        public void TheRecordedLengthGivesTheSameRearEndAsTheStripItCameFrom()
        {
            var warningZone = new WarningZoneSettings();
            var placed = StripGeometry.Create(3.0f, 1.7f, warningZone);

            var extent = StripExtent.FromRecordedLength(placed.LengthMeters, 1.7f, warningZone);

            Assert.That(extent.LengthMeters, Is.EqualTo(placed.LengthMeters));
            Assert.That(extent.RearEndZ, Is.EqualTo(placed.RearEndZ).Within(1e-3f));
        }

        [Test]
        public void ARecordingWithoutAStripUsesTheUnplacedStrip()
        {
            var extent = StripExtent.FromRecordedLength(null, 1.7f, new WarningZoneSettings());

            Assert.That(extent.LengthMeters, Is.EqualTo(StripExtent.UnplacedLengthMeters));
            Assert.That(extent.RearEndZ, Is.EqualTo(-StripExtent.UnplacedRunBackMeters));
        }

        [Test]
        public void AStripNeedsAPositiveLength()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new StripExtent(0.0f, -1.0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new StripExtent(float.NaN, -1.0f));
        }
    }
}
