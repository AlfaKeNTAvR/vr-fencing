// Target Zone lighting (PER-32): zones are lit, dimmed, and flashed on a Touch.
using NUnit.Framework;
using VrFencing.Dummy;

namespace VrFencing.Tests.Dummy
{
    public class TargetZoneLightsTests
    {
        private const double Now = 10.0;

        [Test]
        public void EveryZoneStartsDim()
        {
            var lights = new TargetZoneLights();

            Assert.That(lights.GetLight(TargetZone.Hand, Now), Is.EqualTo(ZoneLight.Dim));
            Assert.That(lights.GetLight(TargetZone.Body, Now), Is.EqualTo(ZoneLight.Dim));
        }

        [Test]
        public void LightingAZoneLeavesTheOthersDim()
        {
            var lights = new TargetZoneLights();

            lights.Light(TargetZone.Thigh);

            Assert.That(lights.GetLight(TargetZone.Thigh, Now), Is.EqualTo(ZoneLight.Lit));
            Assert.That(lights.GetLight(TargetZone.Foot, Now), Is.EqualTo(ZoneLight.Dim));
        }

        [Test]
        public void DimTurnsOneZoneOffAndDimAllTurnsEveryZoneOff()
        {
            var lights = new TargetZoneLights();
            lights.Light(TargetZone.Hand);
            lights.Light(TargetZone.Wrist);

            lights.Dim(TargetZone.Hand);
            Assert.That(lights.GetLight(TargetZone.Hand, Now), Is.EqualTo(ZoneLight.Dim));
            Assert.That(lights.GetLight(TargetZone.Wrist, Now), Is.EqualTo(ZoneLight.Lit));

            lights.DimAll();
            Assert.That(lights.GetLight(TargetZone.Wrist, Now), Is.EqualTo(ZoneLight.Dim));
        }

        [Test]
        public void FlashShowsForItsDurationThenTheZoneReturnsToLit()
        {
            var lights = new TargetZoneLights();
            lights.Light(TargetZone.Forearm);

            lights.Flash(TargetZone.Forearm, Now);

            Assert.That(lights.GetLight(TargetZone.Forearm, Now), Is.EqualTo(ZoneLight.Flash));
            Assert.That(lights.GetLight(TargetZone.Forearm, Now + (TargetZoneLights.FlashSeconds * 0.9)), Is.EqualTo(ZoneLight.Flash));
            Assert.That(lights.GetLight(TargetZone.Forearm, Now + TargetZoneLights.FlashSeconds), Is.EqualTo(ZoneLight.Lit));
        }

        [Test]
        public void DimmingRightAfterATouchStillShowsTheFlash()
        {
            var lights = new TargetZoneLights();
            lights.Light(TargetZone.UpperArm);

            lights.Flash(TargetZone.UpperArm, Now);
            lights.Dim(TargetZone.UpperArm);

            Assert.That(lights.GetLight(TargetZone.UpperArm, Now), Is.EqualTo(ZoneLight.Flash));
            Assert.That(lights.GetLight(TargetZone.UpperArm, Now + TargetZoneLights.FlashSeconds), Is.EqualTo(ZoneLight.Dim));
        }

        [Test]
        public void FlashingAgainRestartsTheFlash()
        {
            var lights = new TargetZoneLights();
            lights.Flash(TargetZone.Hand, Now);

            var later = Now + (TargetZoneLights.FlashSeconds * 0.8);
            lights.Flash(TargetZone.Hand, later);

            Assert.That(lights.GetLight(TargetZone.Hand, Now + TargetZoneLights.FlashSeconds), Is.EqualTo(ZoneLight.Flash));
        }
    }
}
