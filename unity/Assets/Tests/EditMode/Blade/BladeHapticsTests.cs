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

        [Test]
        public void TheArmedDummysHitIsOnItsOwnUnlikeEveryOtherBuzz()
        {
            var hit = BladeHaptics.ArmedDummyHit;
            var seconds = hit.Sum(segment => segment.DurationSeconds);

            Assert.That(seconds, Is.InRange(0.2f, 0.35f), "Short, but longer than any contact buzz: being hit outlasts a contact.");
            Assert.That(hit[0].Amplitude, Is.EqualTo(1.0f), "It opens with the sharp jab.");
            Assert.That(hit[0].Frequency, Is.GreaterThan(hit[hit.Count - 1].Frequency), "Then rumbles lower.");
            Assert.That(hit.SequenceEqual(BladeHaptics.BeingTouched), Is.False, "Distinct from the bout's being-touched buzz.");
            foreach (HapticPattern pattern in System.Enum.GetValues(typeof(HapticPattern)))
            {
                foreach (var eventType in new[] { BladeEventType.Touch, BladeEventType.TipGlance, BladeEventType.FlatContact })
                {
                    Assert.That(BladeHaptics.GetSegments(eventType, pattern).SequenceEqual(hit), Is.False, $"Distinct from the {eventType}'s {pattern}.");
                }

                Assert.That(ClashHaptics.GetSegments(pattern).SequenceEqual(hit), Is.False, $"Distinct from the clash's {pattern}.");
            }
        }

        [Test]
        public void BeingTouchedIsAShortBuzzUnlikeAnyTouchPattern()
        {
            var segments = BladeHaptics.BeingTouched;
            var seconds = segments.Sum(segment => segment.DurationSeconds);

            Assert.That(seconds, Is.InRange(0.1f, 0.25f), "Short (PER-74).");
            Assert.That(segments.Max(segment => segment.Amplitude), Is.EqualTo(1.0f));
            foreach (HapticPattern pattern in System.Enum.GetValues(typeof(HapticPattern)))
            {
                var touch = BladeHaptics.GetSegments(BladeEventType.Touch, pattern);
                Assert.That(touch.SequenceEqual(segments), Is.False, $"Distinct from the Touch's {pattern}.");
            }
        }
    }
}
