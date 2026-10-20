// Haptic choices per Blade event and speed-scaled strength (PER-22: Touch = Heavy, flat = Double tap, glance = a light
// tick weaker than a Touch; faster hit = stronger buzz, with a floor).
using System.Linq;
using NUnit.Framework;
using VrFencing.Blade;
using VrFencing.Settings;

namespace VrFencing.Tests.Blade
{
    public class BladeHapticsTests
    {
        [Test]
        public void DefaultsAreHeavyTouchDoubleTapFlatAndTickGlance()
        {
            var blade = new BladeSettings();

            Assert.That(BladeHaptics.GetChosenPattern(blade, BladeEventType.Touch), Is.EqualTo(HapticPattern.Heavy));
            Assert.That(BladeHaptics.GetChosenPattern(blade, BladeEventType.FlatContact), Is.EqualTo(HapticPattern.DoubleTap));
            Assert.That(BladeHaptics.GetChosenPattern(blade, BladeEventType.TipGlance), Is.EqualTo(HapticPattern.Tick));
        }

        [Test]
        public void TheDefaultGlanceIsWeakerThanTheDefaultTouch()
        {
            var glance = BladeHaptics.GetSegments(BladeEventType.TipGlance, HapticPattern.Tick);
            var touch = BladeHaptics.GetSegments(BladeEventType.Touch, HapticPattern.Heavy);

            Assert.That(glance.Max(segment => segment.Amplitude), Is.LessThan(touch.Max(segment => segment.Amplitude)));
            Assert.That(glance.Sum(segment => segment.DurationSeconds), Is.LessThan(touch.Sum(segment => segment.DurationSeconds)));
        }

        [Test]
        public void DoubleTapHasTwoPulsesWithAGap()
        {
            foreach (var eventType in new[] { BladeEventType.Touch, BladeEventType.FlatContact, BladeEventType.TipGlance })
            {
                var amplitudes = BladeHaptics.GetSegments(eventType, HapticPattern.DoubleTap).Select(segment => segment.Amplitude > 0.0f).ToArray();

                Assert.That(amplitudes, Is.EqualTo(new[] { true, false, true }), eventType.ToString());
            }
        }

        [Test]
        public void TheHitOnTheFencerBuzzIsLongerAndHasMoreTapsThanAnyTouchPattern()
        {
            var hit = BladeHaptics.FencerHitBuzz;
            var hitTaps = CountPulses(hit);
            var hitSeconds = hit.Sum(segment => segment.DurationSeconds);

            foreach (var pattern in new[] { HapticPattern.Tick, HapticPattern.Crisp, HapticPattern.Heavy, HapticPattern.DoubleTap })
            {
                var touch = BladeHaptics.GetSegments(BladeEventType.Touch, pattern);
                Assert.That(hitTaps, Is.GreaterThan(CountPulses(touch)), pattern.ToString());
                Assert.That(hitSeconds, Is.GreaterThan(touch.Sum(segment => segment.DurationSeconds) * 1.5f), pattern.ToString());
            }

            Assert.That(hit.Max(segment => segment.Amplitude), Is.EqualTo(1.0f), "Sharp: full strength.");
        }

        private static int CountPulses(System.Collections.Generic.IReadOnlyList<HapticSegment> segments)
        {
            var pulses = 0;
            var wasOn = false;
            foreach (var segment in segments)
            {
                var isOn = segment.Amplitude > 0.0f;
                if (isOn && !wasOn)
                {
                    pulses++;
                }

                wasOn = isOn;
            }

            return pulses;
        }

        [Test]
        public void OffPlaysNothing()
        {
            Assert.That(BladeHaptics.GetSegments(BladeEventType.Touch, HapticPattern.Off), Is.Empty);
        }

        [Test]
        public void StrengthRisesWithTipSpeedFromTheFloorToFull()
        {
            var blade = new BladeSettings { HapticFloor = 0.4f, HapticFullStrengthSpeedMetersPerSecond = 2.0f };

            Assert.That(BladeHaptics.GetStrength(blade, 0.0f), Is.EqualTo(0.4f).Within(1e-5f));
            Assert.That(BladeHaptics.GetStrength(blade, 1.0f), Is.EqualTo(0.7f).Within(1e-5f));
            Assert.That(BladeHaptics.GetStrength(blade, 2.0f), Is.EqualTo(1.0f).Within(1e-5f));
            Assert.That(BladeHaptics.GetStrength(blade, 9.0f), Is.EqualTo(1.0f).Within(1e-5f));
        }

        [Test]
        public void ClipJsonHoldsEachSegmentsAmplitudeAndEndsSilent()
        {
            var json = BladeHaptics.BuildClipJson(BladeHaptics.GetSegments(BladeEventType.Touch, HapticPattern.DoubleTap));

            Assert.That(json, Does.StartWith("{\"version\":{\"major\":1,"));
            Assert.That(json, Does.Contain("\"amplitude\":[{\"time\":0,\"amplitude\":1}"));
            Assert.That(json, Does.Contain("{\"time\":0.025,\"amplitude\":0}"));
            Assert.That(json, Does.Contain("{\"time\":0.09,\"amplitude\":0}]"));
            Assert.That(json, Does.Contain("\"frequency\":[{\"time\":0,\"frequency\":1}"));
        }
    }
}
