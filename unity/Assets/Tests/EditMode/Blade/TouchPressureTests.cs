// Touch pressure (PER-50): a real épée scores only with 750 g on the point. With the realistic setting a point-first
// arrival scores once the point is pushed in (or arrives fast enough to press it on impact); a light brush does not.
// "On contact" scores at the first contact, as before. Meters and seconds; the Fencer faces +z.
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Blade;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Tests.Blade
{
    public class TouchPressureTests
    {
        private const double FrameSeconds = 1.0 / 72.0;

        private static readonly Vector3 Center = new Vector3(0.0f, 1.0f, 2.0f);
        private static readonly ZoneCapsule[] Hand = TouchDetectorTests.Sphere(TargetZone.Hand, Center, 0.05f);
        private static readonly TouchRules Realistic = TouchRules.For(TouchRule.Loose, TouchPressure.Realistic);
        private static readonly TouchRules OnContact = TouchRules.For(TouchRule.Loose, TouchPressure.OnContact);

        // The point's sphere first touches the zone's surface with the tip at this z.
        private static readonly float TouchingZ = Center.Z - 0.05f - Realistic.TipRadiusMeters;

        [Test]
        public void TheRealisticPressureIsAFewMillimetersOrAnImpact()
        {
            Assert.That(Realistic.NeedsPressure, Is.True);
            Assert.That(Realistic.PressDepthMeters, Is.InRange(0.002f, 0.02f));
            Assert.That(Realistic.ImpactMetersPerSecond, Is.InRange(0.2f, 1.0f));
            Assert.That(OnContact.NeedsPressure, Is.False);
        }

        [Test]
        public void ALightBrushThatNeverPressesInDoesNotScore()
        {
            // At 0.1 m/s the point reaches 2 mm past the surface and comes straight back.
            var depths = new[] { -0.004f, -0.002f, 0.0f, 0.001f, 0.002f, 0.001f, -0.001f, -0.004f };

            var events = Run(depths, Realistic);

            Assert.That(events.Select(e => e.Type), Is.EqualTo(new[] { BladeEventType.TipGlance }), "Only the light glance tick.");
        }

        [Test]
        public void ASteadyPressPastTheThresholdIsOneTouchAtTheMomentItIsPressed()
        {
            // 0.1 m/s straight in and held: about 1.4 mm per frame.
            var depths = Enumerable.Range(0, 40).Select(frame => -0.004f + (frame * 0.1f * (float)FrameSeconds)).ToArray();

            var detector = new TouchDetector();
            var touches = new System.Collections.Generic.List<(int Frame, BladeEvent Event)>();
            for (var frame = 0; frame < depths.Length; frame++)
            {
                foreach (var bladeEvent in detector.Step(Sample(frame, depths[frame]), Hand, Realistic))
                {
                    touches.Add((frame, bladeEvent));
                }

                if (frame > 0 && touches.Count == 0 && depths[frame] > 0.0f)
                {
                    Assert.That(detector.IsTouchPending, Is.True, $"Frame {frame}: in, waiting to be pressed.");
                }
            }

            Assert.That(touches.Select(touch => touch.Event.Type), Is.EqualTo(new[] { BladeEventType.Touch }));
            var pressedAt = touches[0].Frame;
            Assert.That(depths[pressedAt], Is.GreaterThanOrEqualTo(Realistic.PressDepthMeters), "Scored once pressed in far enough.");
            Assert.That(depths[pressedAt - 1], Is.LessThan(Realistic.PressDepthMeters), "And not before.");
            Assert.That(touches[0].Event.TimeSeconds, Is.EqualTo(pressedAt * FrameSeconds).Within(1e-9), "Its time is the moment it was pressed.");
            Assert.That(touches[0].Event.FirstContactSeconds, Is.LessThan(touches[0].Event.TimeSeconds - 0.02), "The first contact was earlier.");
        }

        [Test]
        public void AFastThrustScoresOnImpact()
        {
            // 2 m/s: the frame it arrives it is already a Touch, timed then.
            var depths = Enumerable.Range(0, 4).Select(frame => -0.06f + (frame * 2.0f * (float)FrameSeconds)).ToArray();

            var events = Run(depths, Realistic);

            Assert.That(events.Select(e => e.Type), Is.EqualTo(new[] { BladeEventType.Touch }));
        }

        [Test]
        public void AQuickTapAtImpactSpeedScoresEvenIfItBouncesStraightBack()
        {
            // 0.7 m/s in, touching 1 mm deep for one frame, then straight back out.
            var step = 0.7f * (float)FrameSeconds;
            var depths = new[] { -0.03f, -0.03f + step, -0.03f + (2 * step), 0.001f, -0.02f, -0.04f };

            var events = Run(depths, Realistic);

            Assert.That(events.Select(e => e.Type), Is.EqualTo(new[] { BladeEventType.Touch }));
        }

        [Test]
        public void OnContactScoresAtTheFirstContact()
        {
            var depths = new[] { -0.004f, -0.002f, 0.0f, 0.001f, 0.002f, 0.001f, -0.001f, -0.004f };

            var events = Run(depths, OnContact);

            Assert.That(events.Select(e => e.Type), Is.EqualTo(new[] { BladeEventType.Touch }));
            Assert.That(events[0].TimeSeconds, Is.EqualTo(events[0].FirstContactSeconds).Within(FrameSeconds), "Reported the frame it touched.");
        }

        [Test]
        public void TheDefaultSettingIsRealistic()
        {
            Assert.That(new BladeSettings().TouchPressure, Is.EqualTo(TouchPressure.Realistic));
        }

        private static System.Collections.Generic.List<BladeEvent> Run(float[] depths, TouchRules rules)
        {
            var detector = new TouchDetector();
            var events = new System.Collections.Generic.List<BladeEvent>();
            for (var frame = 0; frame < depths.Length; frame++)
            {
                events.AddRange(detector.Step(Sample(frame, depths[frame]), Hand, rules));
            }

            return events;
        }

        /// <summary>The blade pointing straight at the zone, its point <paramref name="depthMeters"/> past touching it.</summary>
        private static BladeSample Sample(int frame, float depthMeters)
        {
            return new BladeSample(frame * FrameSeconds, TouchDetectorTests.PointingForward(new Vector3(Center.X, Center.Y, TouchingZ + depthMeters)));
        }
    }
}
