// What pressed blades feel and sound like (PER-122): a low, steady bind buzz that grows with the press, unlike the clash
// and hit buzzes, and a looping steel scrape, louder with the press and higher with the slide.
using System;
using System.Linq;
using NUnit.Framework;
using VrFencing.Blade;

namespace VrFencing.Tests.Blade
{
    public class BladeContactFeelTests
    {
        [Test]
        public void TheBindBuzzGrowsWithThePressFromItsFloorToFull()
        {
            Assert.That(BladeHaptics.GetBindStrength(0.0f), Is.EqualTo(0.0f));
            Assert.That(BladeHaptics.GetBindStrength(0.001f), Is.EqualTo(BladeHaptics.BindFloor).Within(0.02f));
            Assert.That(BladeHaptics.GetBindStrength(0.02f), Is.GreaterThan(BladeHaptics.GetBindStrength(0.01f)));
            Assert.That(BladeHaptics.GetBindStrength(BladeHaptics.BindFullPressMeters), Is.EqualTo(1.0f).Within(1e-6f));
            Assert.That(BladeHaptics.GetBindStrength(0.5f), Is.EqualTo(1.0f), "Never past full.");
        }

        [Test]
        public void TheBindBuzzIsLowAndSteadyUnlikeTheClashAndTheHit()
        {
            var bind = BladeHaptics.BindPulse.Single();
            Assert.That(bind.DurationSeconds, Is.GreaterThan(BladeHaptics.BindPulseIntervalSeconds), "Each pulse runs into the next: a hum, not taps.");
            Assert.That(bind.Frequency, Is.LessThan(0.3f), "Low.");
            Assert.That(BladeHaptics.ArmedDummyHit.Max(segment => segment.Frequency), Is.GreaterThan(bind.Frequency + 0.5f), "The hit jabs high.");
        }

        [Test]
        public void TheScrapeIsSilentApartAndLouderAndHigherAsTheBladesPressAndSlide()
        {
            Assert.That(ScrapeSound.GetVolume(0.0f, 1.0f), Is.EqualTo(0.0f), "Apart: silent, however fast.");
            Assert.That(ScrapeSound.GetVolume(0.001f, 0.0f), Is.EqualTo(ScrapeSound.QuietVolume).Within(0.03f));
            Assert.That(ScrapeSound.GetVolume(0.03f, 0.0f), Is.GreaterThan(ScrapeSound.GetVolume(0.01f, 0.0f)));
            Assert.That(ScrapeSound.GetVolume(0.005f, 0.8f), Is.GreaterThan(ScrapeSound.GetVolume(0.005f, 0.0f)), "A glide is heard.");
            Assert.That(ScrapeSound.GetPitch(0.0f), Is.EqualTo(ScrapeSound.LowPitch));
            Assert.That(ScrapeSound.GetPitch(0.5f), Is.InRange(ScrapeSound.LowPitch, ScrapeSound.HighPitch));
            Assert.That(ScrapeSound.GetPitch(5.0f), Is.EqualTo(ScrapeSound.HighPitch));
            Assert.That(ScrapeSound.FindSlideSpeed(0.2f, 0.3f, 0.5f, 0.5f, 0.9f, 0.1f), Is.EqualTo(0.9f).Within(1e-5f));
        }

        [Test]
        public void TheScrapeLoopIsTheSameEveryTimeBoundedAndLoopsWithoutAClick()
        {
            var loop = ScrapeSound.BuildLoop(48000);
            var again = ScrapeSound.BuildLoop(48000);

            Assert.That(loop.Length, Is.EqualTo(48000));
            Assert.That(loop, Is.EqualTo(again));
            Assert.That(loop.Max(Math.Abs), Is.EqualTo(ScrapeSound.LoopPeak).Within(1e-4f));
            var steps = Enumerable.Range(1, loop.Length - 1).Select(index => Math.Abs(loop[index] - loop[index - 1])).ToList();
            var wrap = Math.Abs(loop[0] - loop[loop.Length - 1]);
            Assert.That(wrap, Is.LessThanOrEqualTo(steps.Max()), "The jump from the end back to the start is no bigger than any step inside.");
        }
    }
}
