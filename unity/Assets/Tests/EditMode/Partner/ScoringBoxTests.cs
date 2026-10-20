// The scoring box in each headset (PER-82, decided in PER-74): your lamp green, the opponent's red, both for a double,
// lit 2 s; being touched is felt once per light; the ping shows under it.
using NUnit.Framework;
using VrFencing.Dummy;
using VrFencing.Partner;

namespace VrFencing.Tests.Partner
{
    public class ScoringBoxTests
    {
        private const double Now = 50.0;

        [TestCase(BoutSide.Host, LightKind.HostTouch, true, false)]
        [TestCase(BoutSide.Host, LightKind.PartnerTouch, false, true)]
        [TestCase(BoutSide.Partner, LightKind.HostTouch, false, true)]
        [TestCase(BoutSide.Partner, LightKind.PartnerTouch, true, false)]
        [TestCase(BoutSide.Host, LightKind.Double, true, true)]
        [TestCase(BoutSide.Partner, LightKind.Double, true, true)]
        public void EachHeadsetLightsItsOwnLampForItsOwnTouch(BoutSide me, LightKind kind, bool isMine, bool isOpponents)
        {
            var box = new ScoringBox(me);

            var isTouched = box.Show(new BoutLight(1, kind, TargetZone.Body, TargetZone.Body), Now);

            Assert.That(box.IsMyLampLit(Now), Is.EqualTo(isMine));
            Assert.That(box.IsOpponentsLampLit(Now), Is.EqualTo(isOpponents));
            Assert.That(isTouched, Is.EqualTo(isOpponents), "Being touched is the opponent's lamp lighting.");
        }

        [Test]
        public void TheLampsStayLitTwoSeconds()
        {
            var box = new ScoringBox(BoutSide.Host);
            box.Show(new BoutLight(1, LightKind.Double, TargetZone.Body, TargetZone.Hand), Now);

            Assert.That(box.IsMyLampLit(Now + 1.99), Is.True);
            Assert.That(box.IsMyLampLit(Now + 2.0), Is.False);
            Assert.That(box.IsOpponentsLampLit(Now + 2.0), Is.False);
        }

        [Test]
        public void ALateDoubleOnASingleYouMadeIsFeltButTheSameDoubleTwiceIsNot()
        {
            var box = new ScoringBox(BoutSide.Host);
            Assert.That(box.Show(new BoutLight(4, LightKind.HostTouch, TargetZone.Body, null), Now), Is.False);

            Assert.That(box.Show(new BoutLight(4, LightKind.Double, TargetZone.Body, TargetZone.Foot), Now + 0.05), Is.True);
            Assert.That(box.Show(new BoutLight(4, LightKind.Double, TargetZone.Body, TargetZone.Foot), Now + 0.06), Is.False);
        }

        [Test]
        public void ClearTurnsTheLampsOff()
        {
            var box = new ScoringBox(BoutSide.Partner);
            box.Show(new BoutLight(1, LightKind.Double, TargetZone.Body, TargetZone.Body), Now);

            box.Clear();

            Assert.That(box.IsMyLampLit(Now), Is.False);
            Assert.That(box.LastLight, Is.Null);
        }

        [Test]
        public void ThePingReadsInWholeMilliseconds()
        {
            Assert.That(ScoringBox.FormatPing(0.0844), Is.EqualTo("Ping 84 ms"));
            Assert.That(ScoringBox.FormatPing(null), Is.EqualTo("Ping: measuring"));
        }

        [Test]
        public void ALightIsDescribedFromThisFencersSide()
        {
            var light = new BoutLight(1, LightKind.PartnerTouch, null, TargetZone.Thigh);

            Assert.That(ScoringBox.Describe(light, BoutSide.Partner), Is.EqualTo("Your Touch (Thigh)"));
            Assert.That(ScoringBox.Describe(light, BoutSide.Host), Is.EqualTo("Opponent's Touch (Thigh)"));
        }
    }
}
