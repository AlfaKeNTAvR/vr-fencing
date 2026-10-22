// The blade-contact probe (PER-136): reads the two blades as the app steps them into the log's sample, read-only,
// and measures the bell-guard and drawn-blade gaps. A press past every give shows the saturated stages and the
// slide-off; a blade drawn through a bell shows a negative bell gap; the probe and a row together cost a small part of
// a 72 Hz frame. The Dummy's blade lies along the Strip at y = 1.1, its guard at z = 1.6; the Fencer's lies across it
// along +x at z = 1.0. Meters, seconds, Strip frame.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Blade;

namespace VrFencing.Tests.Blade
{
    public class BladeContactProbeTests
    {
        private const float DummyHeight = 1.1f;
        private const float CrossingZ = 1.0f;

        // Probe and row together, per frame: a small share of the 13.9 ms frame (measured in the editor; the headset logs its own).
        private const double BudgetMilliseconds = 0.5;

        private static readonly BladeSegment DummyForte = new BladeSegment(new Vector3(0.0f, DummyHeight, 1.6f), new Vector3(0.0f, DummyHeight, 0.7f));

        [Test]
        public void ApartTheSampleHasTheBladesAndNoContact()
        {
            var scene = new BladePairScene();
            scene.Frame(Across(DummyHeight + 0.3f), DummyForte);

            var sample = Read(scene);

            Assert.That(sample.IsDummyArmed, Is.True);
            Assert.That(sample.IsInContact, Is.False);
            Assert.That(sample.Fencer.Guard, Is.EqualTo(scene.LastHand.GuardPosition));
            Assert.That(Vector3.Distance(sample.Fencer.DrawnPoint, scene.Fencer.GetSample(DrawnBlade.SampleCount)), Is.LessThan(1e-6f));
            Assert.That(sample.Dummy.Guard, Is.EqualTo(scene.Dummy.HandChord.GuardPosition));
            Assert.That(sample.DrawnGapMeters, Is.EqualTo(0.3f - (2.0f * DrawnBlade.BladeRadiusMeters)).Within(0.03f), "30 cm above, less the sag.");
            Assert.That(sample.Fencer.BellGapMeters, Is.GreaterThan(0.1f));
            Assert.That(sample.Dummy.BellGapMeters, Is.GreaterThan(0.1f));
            Assert.That(sample.SaturatedStages, Is.Zero);
        }

        [Test]
        public void APressPastEveryGiveShowsTheSaturatedStagesAndTheSlideOff()
        {
            var scene = new BladePairScene(hand: VrFencing.Settings.OpponentHand.Strong);
            var log = new BladeContactLog();
            log.WriteHeader(TextWriter.Null, "test", VrFencing.Settings.Hand.Right, DateTimeOffset.UnixEpoch);
            var mostPress = 0.0f;
            var saturated = 0;
            var events = BladeLogEvents.None;
            var sawSliding = false;
            for (var frame = 0; frame < 200; frame++)
            {
                // The Fencer's forte presses down across the Dummy's foible, on past every give, 3 mm a frame.
                scene.Frame(Across(DummyHeight + 0.05f - Math.Min(0.55f, frame * 0.003f)), DummyForte);
                var sample = Read(scene);
                events |= log.WriteRow(sample, TextWriter.Null);
                mostPress = Math.Max(mostPress, sample.PressNewtons);
                saturated |= sample.SaturatedStages;
                sawSliding |= sample.Fencer.SlideOff == SlideOffPhase.Sliding || sample.Dummy.SlideOff == SlideOffPhase.Sliding;
                if (sample.IsInContact)
                {
                    Assert.That(sample.ContactOnFencer, Is.InRange(0.0f, 1.0f));
                    Assert.That(sample.ContactOnDummy, Is.InRange(0.0f, 1.0f));
                    Assert.That(sample.OverlapMeters, Is.GreaterThan(0.0f));
                }
            }

            TestContext.WriteLine($"Most press {mostPress:0.00} N, saturated stages {Convert.ToString(saturated, 2)}, events {events}.");
            Assert.That(mostPress, Is.GreaterThan(0.0f));
            Assert.That(saturated, Is.Not.Zero, "Some give reached its limit.");
            Assert.That(sawSliding, Is.True, "Past every give a blade slides off.");
            Assert.That(events & BladeLogEvents.SlideOffStart, Is.EqualTo(BladeLogEvents.SlideOffStart));
        }

        [Test]
        public void ABladeDrawnThroughABellHasANegativeBellGap()
        {
            var owner = new DrawnBlade();
            var other = new DrawnBlade();
            var none = new List<BladeObstacle>();
            owner.Step(new BladeSegment(Vector3.Zero, new Vector3(0.0f, 0.0f, 0.9f)), none, Vector3.UnitY, 0.0f);

            // Across the bell 1.5 cm behind its front face (the guard), through its middle; then 30 cm in front of it.
            other.Step(new BladeSegment(new Vector3(-0.45f, 0.0f, -0.015f), new Vector3(0.45f, 0.0f, -0.015f)), none, Vector3.UnitY, 0.0f);
            var through = BladeContactProbe.FindBellGap(owner, other);
            other.Reset();
            other.Step(new BladeSegment(new Vector3(-0.45f, 0.0f, 0.3f), new Vector3(0.45f, 0.0f, 0.3f)), none, Vector3.UnitY, 0.0f);
            var clear = BladeContactProbe.FindBellGap(owner, other);

            TestContext.WriteLine($"Through the bell {through * 100.0f:0.0} cm, in front of it {clear * 100.0f:0.0} cm.");
            Assert.That(through, Is.LessThan(0.0f));
            Assert.That(clear, Is.GreaterThan(0.1f));
        }

        [Test]
        public void ABladeDrawnThroughAGripHasANegativeHandleGap()
        {
            // The grip is solid for the other blade in the contact solver (PER-122 fix): the log shows how far out of it.
            var owner = new DrawnBlade();
            var other = new DrawnBlade();
            var none = new List<BladeObstacle>();
            owner.Step(new BladeSegment(Vector3.Zero, new Vector3(0.0f, 0.0f, 0.9f)), none, Vector3.UnitY, 0.0f);

            // Across the grip 10 cm behind the guard, through its axis; then 10 cm above it.
            other.Step(new BladeSegment(new Vector3(-0.45f, 0.0f, -0.10f), new Vector3(0.45f, 0.0f, -0.10f)), none, Vector3.UnitY, 0.0f);
            var through = BladeContactProbe.FindHandleGap(owner, other);
            other.Reset();
            other.Step(new BladeSegment(new Vector3(-0.45f, 0.10f, -0.10f), new Vector3(0.45f, 0.10f, -0.10f)), none, Vector3.UnitY, 0.0f);
            var clear = BladeContactProbe.FindHandleGap(owner, other);

            Assert.That(through, Is.LessThan(0.0f));
            Assert.That(clear, Is.EqualTo(0.10f - BladeContactSolver.GripRadiusMeters - DrawnBlade.BladeRadiusMeters).Within(0.002f));
        }

        [Test]
        public void ProbeAndRowCostASmallPartOfAFrame()
        {
            var scene = new BladePairScene();
            var log = new BladeContactLog();
            log.WriteHeader(TextWriter.Null, "test", VrFencing.Settings.Hand.Right, DateTimeOffset.UnixEpoch);
            var watch = new Stopwatch();
            const int Frames = 720;
            for (var frame = 0; frame < Frames; frame++)
            {
                // Down onto the Dummy's blade and back up, 10 s at 72 Hz.
                var phase = frame % 144;
                var depth = phase < 72 ? phase * 0.004f : (144 - phase) * 0.004f;
                scene.Frame(Across(DummyHeight + 0.05f - depth), DummyForte);
                watch.Start();
                var sample = Read(scene);
                log.WriteRow(sample, TextWriter.Null);
                watch.Stop();
            }

            var milliseconds = watch.Elapsed.TotalMilliseconds / Frames;
            TestContext.WriteLine($"Probe and row: {milliseconds * 1000.0:0} us a frame; the blades' step itself {scene.Watch.Elapsed.TotalMilliseconds / Frames * 1000.0:0} us.");
            Assert.That(milliseconds, Is.LessThan(BudgetMilliseconds));
        }

        private static BladeContactSample Read(BladePairScene scene)
        {
            var sample = default(BladeContactSample);
            BladeContactProbe.ReadFencer(ref sample, scene.LastHand, scene.Fencer);
            BladeContactProbe.ReadArmed(ref sample, scene.Dummy, scene.Fencer);
            sample.DummyMode = "drill";
            sample.DummyAction = "en_garde";
            return sample;
        }

        private static BladeSegment Across(float height)
        {
            var guard = new Vector3(-0.3f, height, CrossingZ);
            return new BladeSegment(guard, guard + (Vector3.UnitX * BladePose.BladeLengthMeters));
        }
    }
}
