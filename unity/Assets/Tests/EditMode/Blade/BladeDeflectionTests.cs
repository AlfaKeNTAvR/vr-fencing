// The Armed Dummy's point knocked off its line (PER-116): a firm beat opens the line long enough to attack, a light
// touch comes straight back, a press yields by where each blade is pressed, the recovery grows with the deflection and
// never passes the 60 cm cap, and a big knock carries the whole arm (its Target Zones within the Blade's 5 cm a frame)
// with the drawn blade staying in the Dummy's hand, also when a hard press near its guard pushes the point far aside at
// once (PER-119). Meters, seconds, Strip frame.
using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Blade;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Tests.Blade
{
    public class BladeDeflectionTests
    {
        private const float FrameSeconds = 1.0f / 72.0f;

        // The Dummy's blade along the Strip at y = 1.1, its guard at z = 1.6, its point at z = 0.7.
        private static readonly BladeSegment DummyForte = new BladeSegment(new Vector3(0.0f, 1.1f, 1.6f), new Vector3(0.0f, 1.1f, 0.7f));

        private static BladeClash Clash(float speedMetersPerSecond, float dummyBladeFraction, Vector3 push)
        {
            return new BladeClash(0.0, Vector3.Zero, 0.3f, dummyBladeFraction, speedMetersPerSecond, push);
        }

        /// <summary>Steps <paramref name="deflection"/> at 72 Hz for <paramref name="seconds"/>.</summary>
        private static void Run(BladeDeflection deflection, float seconds)
        {
            for (var time = 0.0f; time < seconds - 1e-6f; time += FrameSeconds)
            {
                deflection.Step(FrameSeconds);
            }
        }

        /// <summary>How long after a knock the point stays more than <paramref name="meters"/> off its line, and when it is back.</summary>
        private static (float OutSeconds, float BackSeconds) MeasureKnock(BladeClash clash, float meters)
        {
            var deflection = new BladeDeflection();
            deflection.Kick(clash);
            var outSeconds = 0.0f;
            var backSeconds = float.NaN;
            for (var time = FrameSeconds; time < 3.0f; time += FrameSeconds)
            {
                deflection.Step(FrameSeconds);
                if (deflection.TipOffset.Length() > meters)
                {
                    outSeconds += FrameSeconds;
                }
                else if (float.IsNaN(backSeconds) && deflection.TipOffset.Length() < 0.001f)
                {
                    backSeconds = time;
                }
            }

            return (outSeconds, backSeconds);
        }

        // ---- Beat and light touch ----

        [Test]
        public void AFirmBeatOnTheFoibleKeepsThePointOutOfLineForHalfASecond()
        {
            var deflection = new BladeDeflection();
            deflection.Kick(Clash(2.0f, 0.8f, Vector3.UnitX));
            Run(deflection, BladeDeflection.OutSeconds + FrameSeconds);

            Assert.That(deflection.TipOffset.X, Is.EqualTo(BladeDeflection.GetKickMeters(2.0f, 0.8f)).Within(0.001f), "Out to its deflection.");
            Assert.That(BladeDeflection.GetKickMeters(2.0f, 0.8f), Is.EqualTo(0.344f).Within(0.001f));
            var (outSeconds, backSeconds) = MeasureKnock(Clash(2.0f, 0.8f, Vector3.UnitX), BeatJudge.OutOfLineMeters);
            Assert.That(outSeconds, Is.GreaterThanOrEqualTo(0.5f), "Past the Beat threshold for at least half a second: time to attack.");
            Assert.That(backSeconds, Is.LessThan(1.2f), "Back in line afterwards.");
        }

        [Test]
        public void ALightTouchComesCalmlyBackInAboutHalfASecond()
        {
            // Nikita's headset test after PR 72: the hand bounced back too fast (2.5 Hz, about 0.3 s) and overshot. Now a
            // calmer, critically damped return: still well out at 0.3 s, back by 0.6 s.
            var deflection = new BladeDeflection();
            deflection.Kick(Clash(0.3f, 1.0f, Vector3.UnitX));
            var peak = 0.0f;
            for (var time = FrameSeconds; time < 1.0f; time += FrameSeconds)
            {
                deflection.Step(FrameSeconds);
                peak = Math.Max(peak, deflection.TipOffset.Length());
                Assert.That(deflection.Phase, Is.EqualTo(DeflectionPhase.Spring), "A light touch never opens the line.");
                Assert.That(deflection.IsOffLine, Is.False);
                if (Math.Abs(time - 0.3f) < FrameSeconds * 0.5f)
                {
                    Assert.That(deflection.TipOffset.Length(), Is.GreaterThan(0.01f), "Not snapped back at 0.3 s.");
                }

                if (time >= 0.6f)
                {
                    Assert.That(deflection.TipOffset.Length(), Is.LessThan(0.005f), $"Back at {time:0.00} s.");
                }
            }

            Assert.That(peak, Is.EqualTo(BladeDeflection.LightTouchMeters).Within(0.003f), "A slow touch moves it at most 5 cm.");
        }

        [Test]
        public void ThePointNeverCrossesItsLineComingBack(
            [Values("touch", "beat", "small press", "big press")] string what)
        {
            // Nikita's headset test after PR 72: let go after a hit or a press, the Dummy's point came back past its line
            // to the other side, like a spring. Now it comes back and stops on its line, whatever moved it off.
            var deflection = new BladeDeflection();
            switch (what)
            {
                case "touch":
                    deflection.Kick(Clash(0.4f, 1.0f, Vector3.UnitX));
                    break;
                case "beat":
                    deflection.Kick(Clash(2.0f, 0.8f, Vector3.UnitX));
                    break;
                default:
                    var held = what == "small press" ? 0.03f : 0.12f;
                    for (var time = 0.0f; time < 1.0f; time += FrameSeconds)
                    {
                        deflection.HoldAt(Vector3.UnitX * held);
                        deflection.Step(FrameSeconds);
                        Assert.That(deflection.TipOffset.X, Is.LessThanOrEqualTo(held + 1e-4f), "Pressed, it goes where the press holds it, no further.");
                    }

                    deflection.HoldAt(Vector3.Zero);
                    break;
            }

            var most = 0.0f;
            for (var time = 0.0f; time < 3.0f; time += FrameSeconds)
            {
                deflection.Step(FrameSeconds);
                most = Math.Max(most, deflection.TipOffset.X);
                Assert.That(deflection.TipOffset.X, Is.GreaterThanOrEqualTo(-1e-5f), $"{what}: at {time:0.00} s it crossed its line.");
            }

            Assert.That(most, Is.GreaterThan(0.01f), "Setup: it was moved off its line.");
            Assert.That(deflection.TipOffset.Length(), Is.LessThan(0.001f), "Back on its line.");
        }

        [Test]
        public void ABeatOnTheFoibleMovesItMoreThanOneOnTheForteAndAFirmBeatMeetsTheBeatThreshold()
        {
            Assert.That(BladeDeflection.GetKickMeters(1.5f, 1.0f), Is.GreaterThan(BladeDeflection.GetKickMeters(1.5f, 0.2f)));
            Assert.That(BladeDeflection.GetKickMeters(1.5f, 0.0f), Is.EqualTo(BladeDeflection.GetKickMeters(1.5f, 1.0f) * BladeDeflection.GuardLeverage).Within(1e-5f));
            Assert.That(BladeDeflection.GetKickMeters(1.0f, 0.8f), Is.GreaterThanOrEqualTo(BeatJudge.OutOfLineMeters), "1 m/s on the foible is a Beat.");
            Assert.That(BladeDeflection.GetKickMeters(0.5f, 1.0f), Is.LessThanOrEqualTo(BladeDeflection.LightTouchMeters), "Slower than a beat: a touch.");
            Assert.That(BladeDeflection.GetKickMeters(0.0f, 1.0f), Is.EqualTo(0.0f));
        }

        [Test]
        public void RecoveryTakesLongerTheFurtherThePointWasKnocked()
        {
            var small = MeasureKnock(Clash(1.0f, 0.8f, Vector3.UnitX), 0.001f).BackSeconds;
            var middle = MeasureKnock(Clash(2.0f, 0.8f, Vector3.UnitX), 0.001f).BackSeconds;
            var large = MeasureKnock(Clash(3.5f, 1.0f, Vector3.UnitX), 0.001f).BackSeconds;

            Assert.That(middle, Is.GreaterThan(small + 0.1f));
            Assert.That(large, Is.GreaterThan(middle + 0.1f));
            Assert.That(BladeDeflection.GetRecoverSeconds(0.3f), Is.EqualTo(0.55f).Within(1e-5f));
            Assert.That(BladeDeflection.GetOpenSeconds(0.3f), Is.EqualTo(0.30f).Within(1e-5f));
        }

        [Test]
        public void TheDeflectionNeverPassesSixtyCentimeters()
        {
            var deflection = new BladeDeflection();
            for (var hit = 0; hit < 5; hit++)
            {
                deflection.Kick(Clash(6.0f, 1.0f, Vector3.Normalize(new Vector3(1.0f, 0.2f * hit, 0.0f))));
                deflection.HoldAt(new Vector3(0.7f, 0.0f, 0.0f));
                for (var frame = 0; frame < 10; frame++)
                {
                    deflection.Step(FrameSeconds);
                    Assert.That(deflection.TipOffset.Length(), Is.LessThanOrEqualTo(BladeDeflection.MaxDeflectionMeters + 1e-4f));
                    Assert.That(deflection.ArmOffset.Length(), Is.LessThanOrEqualTo(BladeDeflection.ArmMaxMeters + 1e-4f));
                }
            }

            Assert.That(BladeDeflection.MaxDeflectionMeters, Is.EqualTo(0.6f).Within(1e-6f));
        }

        [Test]
        public void AHitWhileTheLineIsOpenAddsToTheDeflectionAndOpensItAgain()
        {
            var deflection = new BladeDeflection();
            deflection.Kick(Clash(1.5f, 0.8f, Vector3.UnitX));
            Run(deflection, 0.2f);
            Assert.That(deflection.Phase, Is.EqualTo(DeflectionPhase.Open), "Setup: held open.");
            var first = deflection.TipOffset.X;

            deflection.Kick(Clash(1.5f, 0.8f, Vector3.UnitX));
            Run(deflection, BladeDeflection.OutSeconds + FrameSeconds);

            Assert.That(deflection.TipOffset.X, Is.EqualTo(2.0f * first).Within(0.002f));
            Assert.That(deflection.Phase, Is.EqualTo(DeflectionPhase.Open));
            Assert.That(deflection.PlanCount, Is.EqualTo(2), "Each knock is logged.");
        }

        [Test]
        public void TheKnockIsTheSameAtAnyFrameRate()
        {
            var fine = new BladeDeflection();
            var coarse = new BladeDeflection();
            fine.Kick(Clash(1.5f, 0.6f, Vector3.UnitY));
            coarse.Kick(Clash(1.5f, 0.6f, Vector3.UnitY));

            for (var frame = 0; frame < 50; frame++)
            {
                coarse.Step(FrameSeconds);
            }

            for (var step = 0; step < 694; step++)
            {
                fine.Step(0.001f);
            }

            Assert.That(coarse.Phase, Is.EqualTo(DeflectionPhase.Recover), "Setup: measured mid-recovery, where it moves fastest.");
            Assert.That(Vector3.Distance(fine.TipOffset, coarse.TipOffset), Is.LessThan(0.003f));
        }

        // ---- Press ----

        [Test]
        public void APressedPointLetGoStaysOpenThenRecovers()
        {
            var deflection = new BladeDeflection();
            deflection.HoldAt(new Vector3(0.0f, -0.15f, 0.0f));
            Run(deflection, 0.8f); // The calmer spring (PER-122 fix) follows a press in about half a second.
            Assert.That(deflection.TipOffset.Y, Is.EqualTo(-0.15f).Within(0.005f), "Setup: held aside by the press.");
            Assert.That(deflection.IsOffLine, Is.True, "A press holding it well aside keeps the Dummy from attacking.");

            deflection.HoldAt(Vector3.Zero);
            Run(deflection, 0.1f);
            Assert.That(deflection.Phase, Is.EqualTo(DeflectionPhase.Open), "Let go, it does not spring straight back.");
            Assert.That(deflection.TipOffset.Y, Is.EqualTo(-0.15f).Within(0.005f));

            Run(deflection, BladeDeflection.GetOpenSeconds(0.15f) + BladeDeflection.GetRecoverSeconds(0.15f));
            Assert.That(deflection.TipOffset.Length(), Is.LessThan(0.001f), "Back in line after the open line and the recovery.");
        }

        [Test]
        public void ABeatOnAPressedBladeStillKnocksItOut()
        {
            var deflection = new BladeDeflection();
            var press = new Vector3(0.0f, -0.05f, 0.0f);
            deflection.HoldAt(press);
            Run(deflection, 0.5f);

            deflection.Kick(Clash(2.0f, 0.8f, Vector3.UnitX));
            Run(deflection, 0.1f);

            Assert.That(deflection.Phase, Is.EqualTo(DeflectionPhase.Open), "The press keeps the knocked line open.");
            Assert.That(deflection.TipOffset.X, Is.EqualTo(BladeDeflection.GetKickMeters(2.0f, 0.8f)).Within(0.005f));
            Assert.That(deflection.TipOffset.Y, Is.EqualTo(press.Y).Within(0.005f), "Still held where the press had it.");
        }

        [Test]
        public void TheHandGivesWayByThePressPastWhatItHoldsOverItsYieldStiffnessSquareToTheBlade()
        {
            // 1 N past what each hand holds (PER-122 fix: a press first bows the blade, the hand gives only past it).
            var push = Vector3.Normalize(new Vector3(0.0f, -1.0f, 0.3f));
            var light = ArmedDummyBlade.FindYieldTipOffset(ArmedDummyBlade.LightHoldNewtons + 1.0f, push, -Vector3.UnitZ, OpponentHand.Light);
            var strong = ArmedDummyBlade.FindYieldTipOffset(ArmedDummyBlade.StrongHoldNewtons + 1.0f, push, -Vector3.UnitZ, OpponentHand.Strong);

            Assert.That(light.Z, Is.EqualTo(0.0f).Within(1e-6f), "Square to its blade.");
            Assert.That(light.Y, Is.EqualTo(-1.0f / ArmedDummyBlade.LightYieldStiffness).Within(1e-5f));
            Assert.That(light.Length(), Is.GreaterThan(3.0f * strong.Length()), "A light hand gives way four times as far as a strong one.");
            Assert.That(ArmedDummyBlade.FindYieldTipOffset(0.0f, push, -Vector3.UnitZ, OpponentHand.Light), Is.EqualTo(Vector3.Zero));
            Assert.That(ArmedDummyBlade.FindYieldTipOffset(ArmedDummyBlade.MediumHoldNewtons, push, -Vector3.UnitZ, OpponentHand.Medium), Is.EqualTo(Vector3.Zero), "A press it holds moves it not at all.");
            Assert.That(ArmedDummyBlade.LightHoldNewtons, Is.LessThan(ArmedDummyBlade.MediumHoldNewtons));
            Assert.That(ArmedDummyBlade.MediumHoldNewtons, Is.LessThan(ArmedDummyBlade.StrongHoldNewtons));
        }

        // ---- The knocked arm ----

        [Test]
        public void TheArmTakesWhatIsPastTheWristNoFasterThanItsLimit()
        {
            var deflection = new BladeDeflection();
            deflection.Kick(Clash(4.0f, 1.0f, Vector3.UnitX));
            var previous = deflection.ArmOffset;
            for (var frame = 0; frame < 30; frame++)
            {
                deflection.Step(FrameSeconds);
                Assert.That(Vector3.Distance(deflection.ArmOffset, previous), Is.LessThanOrEqualTo((BladeDeflection.MaxArmPointSpeedMetersPerSecond * FrameSeconds) + 1e-5f));
                previous = deflection.ArmOffset;
            }

            Assert.That(deflection.ArmOffset.X, Is.EqualTo(BladeDeflection.ArmMaxMeters).Within(0.001f), "The whole arm knocked aside.");
            Assert.That(deflection.TipOffset.X - deflection.ArmOffset.X, Is.EqualTo(BladeDeflection.WristMaxMeters).Within(0.001f), "The wrist the rest.");
        }

        [Test]
        public void TheKnockedArmTurnsAboutTheShoulderAndThePointCarriesTheWholeOffset()
        {
            var deflection = new BladeDeflection();
            deflection.Kick(Clash(3.0f, 0.9f, Vector3.UnitX));
            Run(deflection, 0.3f);
            var shoulder = DummyForte.GuardPosition + new Vector3(0.1f, 0.3f, 0.6f);

            var deflected = deflection.Apply(DummyForte, shoulder);

            Assert.That(deflection.ArmOffset.X, Is.GreaterThan(0.1f), "Setup: the arm is knocked too.");
            Assert.That(Vector3.Distance(deflected.GuardPosition, shoulder), Is.EqualTo(Vector3.Distance(DummyForte.GuardPosition, shoulder)).Within(1e-4f), "The hand stays on the arm.");
            Assert.That(deflected.GuardPosition.X, Is.GreaterThan(DummyForte.GuardPosition.X + 0.01f), "The hand went with the arm.");
            Assert.That(Vector3.Distance(deflected.GuardPosition, deflected.TipPosition), Is.EqualTo(0.9f).Within(1e-4f), "The blade keeps its length.");
            var sideways = deflected.TipPosition - DummyForte.TipPosition;
            sideways -= Vector3.Dot(sideways, DummyForte.Direction) * DummyForte.Direction;
            Assert.That(Vector3.Distance(sideways, deflection.TipOffset), Is.LessThan(0.025f), "The point carries the whole offset sideways of its line, to a few percent.");
        }

        [Test]
        public void AKnockedArmKeepsItsTargetZonesUnderFiveCentimetersAFrameAndTheBladeInItsHand(
            [Values(1.5f, 2.1f)] float height,
            [Values(Hand.Right, Hand.Left)] Hand hand)
        {
            const float distance = 3.0f;
            var layout = DummyLayout.Create(height, hand);
            var enGarde = SwordArmReach.PoseEnGarde(height, BladePose.BladeLengthMeters);
            var blade = new ArmedDummyBlade();
            var settings = new BladeSettings();
            var farAway = new BladeSegment(new Vector3(5.0f, 0.0f, 0.0f), new Vector3(5.9f, 0.0f, 0.0f));
            var zones = new[] { TargetZone.Hand, TargetZone.Wrist, TargetZone.Forearm, TargetZone.UpperArm };
            var push = Vector3.Normalize(new Vector3(1.0f, 0.6f, 0.0f));
            Capsule[] last = null;
            var farthestKnock = 0.0f;

            for (var frame = 0; frame < 144; frame++)
            {
                if (frame == 3)
                {
                    blade.Deflection.Kick(Clash(6.0f, 1.0f, push));
                }

                layout.PoseArm(enGarde);
                var forte = new BladeSegment(
                    DummyLayout.DummyToStrip(layout.Arm.GetGuardPosition(), distance),
                    DummyLayout.DummyToStrip(layout.Arm.GetBladePoint(BladePose.BladeLengthMeters), distance));
                var shoulder = DummyLayout.DummyToStrip(layout.Arm.GetPose(ArmJoint.Shoulder).Position, distance);
                blade.Step(forte, shoulder, Vector3.UnitY, -Vector3.UnitY, settings, OpponentHand.Medium, farAway, default, FrameSeconds);
                layout.PoseArm(blade.GetKnockedArmPose(enGarde, hand));

                var guard = DummyLayout.DummyToStrip(layout.Arm.GetGuardPosition(), distance);
                Assert.That(Vector3.Distance(guard, blade.Chord.GuardPosition), Is.LessThan(0.001f), $"Frame {frame}: the blade stays in the knocked hand.");
                farthestKnock = Math.Max(farthestKnock, Vector3.Dot(guard - forte.GuardPosition, push));

                var now = Array.ConvertAll(zones, zone => layout.GetZone(zone));
                for (var index = 0; last != null && index < now.Length; index++)
                {
                    var moved = Math.Max(Vector3.Distance(now[index].Start, last[index].Start), Vector3.Distance(now[index].End, last[index].End));
                    Assert.That(moved, Is.LessThan(0.05f), $"Frame {frame}: the {zones[index]} moved {moved * 100.0f:0.0} cm.");
                }

                last = now;
            }

            Assert.That(farthestKnock, Is.GreaterThan(0.1f), "The hand was knocked well aside, the way it was pushed.");
            Assert.That(blade.Deflection.TipOffset.Length(), Is.LessThan(0.001f), "Back in line after 2 s.");
        }

        [Test]
        public void AHardPressNearTheGuardKeepsTheArmZonesUnderFiveCentimetersAFrame(
            [Values(1.5f, 2.1f)] float height,
            [Values(Hand.Right, Hand.Left)] Hand hand)
        {
            // PER-119: the Fencer's blade comes down at 3 m/s across the Dummy's, a tenth of the way from its guard, to
            // 30 cm past its line. A hard press on a light hand gives way far (since PER-122 the hand reads the press;
            // the blades' own contact keeps them apart), but the arm follows at its limit, so its Target Zones stay
            // within the Blade's 5 cm a frame.
            const float distance = 3.0f;
            const float pressFraction = 0.1f;
            const float pressMetersPerSecond = 3.0f;
            const float pressDepthMeters = 0.30f;
            var layout = DummyLayout.Create(height, hand);
            var enGarde = SwordArmReach.PoseEnGarde(height, BladePose.BladeLengthMeters);
            var blade = new ArmedDummyBlade();
            var settings = new BladeSettings();
            var zones = new[] { TargetZone.Hand, TargetZone.Wrist, TargetZone.Forearm, TargetZone.UpperArm };
            Capsule[] last = null;
            var above = 0.10f;
            var mostGive = 0.0f;
            var wasPressed = false;

            for (var frame = 0; frame < 144; frame++)
            {
                layout.PoseArm(enGarde);
                var forte = new BladeSegment(
                    DummyLayout.DummyToStrip(layout.Arm.GetGuardPosition(), distance),
                    DummyLayout.DummyToStrip(layout.Arm.GetBladePoint(BladePose.BladeLengthMeters), distance));
                var shoulder = DummyLayout.DummyToStrip(layout.Arm.GetPose(ArmJoint.Shoulder).Position, distance);
                above = Math.Max(-pressDepthMeters, above - (pressMetersPerSecond * FrameSeconds));
                var crossing = Vector3.Lerp(forte.GuardPosition, forte.TipPosition, pressFraction) + new Vector3(0.0f, above, 0.0f);
                var fencerHand = new BladeSegment(crossing - (0.3f * Vector3.UnitX), crossing + ((BladePose.BladeLengthMeters - 0.3f) * Vector3.UnitX));

                blade.Step(forte, shoulder, Vector3.UnitY, -Vector3.UnitY, settings, OpponentHand.Light, fencerHand, default, FrameSeconds);
                mostGive = Math.Max(mostGive, blade.Deflection.TipOffset.Length());
                wasPressed |= blade.IsPressed;

                layout.PoseArm(blade.GetKnockedArmPose(enGarde, hand));
                var guard = DummyLayout.DummyToStrip(layout.Arm.GetGuardPosition(), distance);
                Assert.That(Vector3.Distance(guard, blade.Chord.GuardPosition), Is.LessThan(0.001f), $"Frame {frame}: the blade stays in the knocked hand.");

                var now = Array.ConvertAll(zones, zone => layout.GetZone(zone));
                for (var index = 0; last != null && index < now.Length; index++)
                {
                    var moved = Math.Max(Vector3.Distance(now[index].Start, last[index].Start), Vector3.Distance(now[index].End, last[index].End));
                    Assert.That(moved, Is.LessThan(0.05f), $"Frame {frame}: the {zones[index]} moved {moved * 100.0f:0.0} cm.");
                }

                last = now;
            }

            Assert.That(wasPressed, Is.True, "Setup: the Fencer's hand pressed past the Dummy's line.");
            Assert.That(mostGive, Is.GreaterThan(0.02f), "Setup: the hand gave way.");
        }

        [Test]
        public void TheSparkSpotRunsAlongTheDrawnBlade()
        {
            var blade = new ArmedDummyBlade();
            var farAway = new BladeSegment(new Vector3(5.0f, 0.0f, 0.0f), new Vector3(5.9f, 0.0f, 0.0f));
            blade.Step(DummyForte, DummyForte.GuardPosition + new Vector3(0.0f, 0.3f, 0.6f), Vector3.UnitY, -Vector3.UnitY, new BladeSettings(), OpponentHand.Medium, farAway, default, FrameSeconds);

            Assert.That(blade.GetPointAt(0.0f), Is.EqualTo(blade.Drawn.GetSample(0)));
            Assert.That(blade.GetPointAt(1.0f), Is.EqualTo(blade.Drawn.GetSample(DrawnBlade.SampleCount)));
            Assert.That(Vector3.Distance(blade.GetPointAt(0.5f), blade.Drawn.GetSample(DrawnBlade.SampleCount / 2)), Is.LessThan(1e-5f));
        }
    }
}
