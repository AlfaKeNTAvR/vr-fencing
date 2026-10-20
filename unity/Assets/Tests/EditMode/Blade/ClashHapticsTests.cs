// The clash buzz when the Fencer's blade meets the Armed Dummy's (PER-99, decided in PER-23): the chosen pattern,
// stronger the harder the contact, from a floor to full, one buzz per contact and never two within the rate limit.
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Blade;
using VrFencing.Settings;

namespace VrFencing.Tests.Blade
{
    public class ClashHapticsTests
    {
        private const double FrameSeconds = 1.0 / 72.0;

        // The Dummy's blade lies along +z at chest height, its point toward the Fencer.
        private static readonly BladeSegment Opponent = new BladeSegment(new Vector3(0.0f, 1.0f, 1.5f), new Vector3(0.0f, 1.0f, 0.6f));

        private static readonly BladeEventType[] BladeEvents = { BladeEventType.Touch, BladeEventType.TipGlance, BladeEventType.FlatContact };

        private static readonly HapticPattern[] PlayedPatterns = { HapticPattern.Tick, HapticPattern.Crisp, HapticPattern.Heavy, HapticPattern.DoubleTap };

        /// <summary>The Fencer's blade across the Dummy's (along +x), <paramref name="heightMeters"/> above it.</summary>
        private static BladeSegment Above(float heightMeters)
        {
            return new BladeSegment(new Vector3(-0.3f, 1.0f + heightMeters, 0.8f), new Vector3(0.6f, 1.0f + heightMeters, 0.8f));
        }

        private static float SumSeconds(IEnumerable<HapticSegment> segments)
        {
            return segments.Sum(segment => segment.DurationSeconds);
        }

        [Test]
        public void DefaultsAreThePrototypesCrispRingAndThirtyFivePercentFloor()
        {
            var blade = new BladeSettings();

            Assert.That(blade.ClashHaptic, Is.EqualTo(HapticPattern.Crisp));
            Assert.That(blade.ClashHapticFloor, Is.EqualTo(0.35f));
            Assert.That(ClashHaptics.GetSegments(HapticPattern.Crisp).Select(segment => segment.Amplitude), Is.EqualTo(new[] { 1.0f, 0.55f, 0.25f }));
        }

        [Test]
        public void StrengthRisesWithRelativeBladeSpeed()
        {
            var lightBeat = ClashHaptics.GetStrength(0.35f, 0.3f);
            var firmParry = ClashHaptics.GetStrength(0.35f, 1.0f);
            var hardClash = ClashHaptics.GetStrength(0.35f, 1.8f);

            Assert.That(lightBeat, Is.LessThan(firmParry));
            Assert.That(firmParry, Is.LessThan(hardClash));
            Assert.That(firmParry, Is.EqualTo((0.35f + 1.0f) * 0.5f).Within(1e-5f), "Halfway to full speed is halfway from the floor to full.");
        }

        [Test]
        public void StrengthStaysBetweenTheFloorAndFull()
        {
            Assert.That(ClashHaptics.GetStrength(0.35f, 0.0f), Is.EqualTo(0.35f));
            Assert.That(ClashHaptics.GetStrength(0.35f, -1.0f), Is.EqualTo(0.35f), "No speed is the floor.");
            Assert.That(ClashHaptics.GetStrength(0.35f, ClashHaptics.FullStrengthSpeedMetersPerSecond), Is.EqualTo(1.0f));
            Assert.That(ClashHaptics.GetStrength(0.35f, 10.0f), Is.EqualTo(1.0f), "Never above full.");
            Assert.That(ClashHaptics.GetStrength(1.5f, 0.0f), Is.EqualTo(1.0f), "A floor above full is full.");
            Assert.That(ClashHaptics.GetStrength(0.0f, 0.0f), Is.EqualTo(0.0f));
        }

        [Test]
        public void OffPlaysNothingAndEveryOtherPatternIsShortAndStartsHardest()
        {
            Assert.That(ClashHaptics.GetSegments(HapticPattern.Off), Is.Empty);
            foreach (var pattern in PlayedPatterns)
            {
                var segments = ClashHaptics.GetSegments(pattern);
                Assert.That(segments[0].Amplitude, Is.EqualTo(segments.Max(segment => segment.Amplitude)), $"{pattern} starts at its hardest.");
                Assert.That(SumSeconds(segments), Is.LessThan(0.15f), $"{pattern} is short enough not to blur into the riposte's Touch.");
            }
        }

        [Test]
        public void EveryClashPatternDiffersFromEveryBladeEventAndBeingTouched()
        {
            foreach (var pattern in PlayedPatterns)
            {
                var clash = ClashHaptics.GetSegments(pattern);
                Assert.That(clash.SequenceEqual(BladeHaptics.BeingTouched), Is.False, $"Clash {pattern} vs being touched.");
                foreach (var other in PlayedPatterns)
                {
                    foreach (var eventType in BladeEvents)
                    {
                        Assert.That(clash.SequenceEqual(BladeHaptics.GetSegments(eventType, other)), Is.False, $"Clash {pattern} vs {eventType} {other}.");
                    }
                }
            }
        }

        [Test]
        public void OneContactBuzzesOnceWhileTheBladesStayTogether()
        {
            var detector = new BladeClashDetector();
            var gate = new ClashBuzzGate();
            var buzzes = 0;
            var time = 0.0;

            // Come down onto the Dummy's blade over four frames, then hold on it for half a second, jittering 2 mm.
            detector.Step(time, Above(0.05f), Opponent);
            for (var frame = 1; frame <= 40; frame++)
            {
                time += FrameSeconds;
                var height = frame <= 4 ? 0.05f - (frame * 0.0125f) : 0.002f * (frame % 2);
                var clash = detector.Step(time, Above(height), Opponent);
                if (clash.HasValue && gate.TryPass(time))
                {
                    buzzes++;
                }
            }

            Assert.That(buzzes, Is.EqualTo(1));
        }

        [Test]
        public void ASecondContactWithinTheRateLimitDoesNotBuzz()
        {
            var gate = new ClashBuzzGate();

            Assert.That(gate.TryPass(10.0), Is.True);
            Assert.That(gate.TryPass(10.0 + (ClashBuzzGate.MinIntervalSeconds * 0.5)), Is.False);
            Assert.That(gate.TryPass(10.0 + (ClashBuzzGate.MinIntervalSeconds * 0.9)), Is.False, "Timed from the last buzz, not the last refusal.");
            Assert.That(gate.TryPass(10.0 + ClashBuzzGate.MinIntervalSeconds + 0.001), Is.True);
        }

        [Test]
        public void ResetLetsTheNextClashBuzzAtOnce()
        {
            var gate = new ClashBuzzGate();
            gate.TryPass(5.0);

            gate.Reset();

            Assert.That(gate.TryPass(5.01), Is.True);
        }

        [Test]
        public void TheRateLimitOutlastsTheDefaultBuzz()
        {
            Assert.That(ClashBuzzGate.MinIntervalSeconds, Is.GreaterThan(SumSeconds(ClashHaptics.GetSegments(HapticPattern.Crisp))));
        }
    }
}
