// The Armed Dummy's point knocked off its line (PER-116): a firm beat opens the line long enough to attack, a light
// touch comes straight back, a press yields by where each blade is pressed, the recovery grows with the deflection and
// never passes the 60 cm cap, and a big knock carries the whole arm (its Target Zones within the Blade's 5 cm a frame)
// with the drawn blade staying in the Dummy's hand. Meters, seconds, Strip frame.
using System;
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
        public void ALightTouchIsBackWithinAThirdOfASecond()
        {
            var deflection = new BladeDeflection();
            deflection.Kick(Clash(0.3f, 1.0f, Vector3.UnitX));
            var peak = 0.0f;
            for (var time = FrameSeconds; time < 1.0f; time += FrameSeconds)
            {
                deflection.Step(FrameSeconds);
                peak = Math.Max(peak, deflection.TipOffset.Length());
                Assert.That(deflection.Phase, Is.EqualTo(DeflectionPhase.Spring), "A light touch never opens the line.");
                Assert.That(deflection.IsOffLine, Is.False);
                if (time >= 0.3f)
                {
                    Assert.That(deflection.TipOffset.Length(), Is.LessThan(0.005f), $"Back at {time:0.00} s.");
                }
            }

            Assert.That(peak, Is.EqualTo(BladeDeflection.LightTouchMeters).Within(0.003f), "A slow touch moves it at most 5 cm.");
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
                deflection.Push(new Vector3(0.1f, 0.0f, 0.0f));
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
            Run(deflection, 0.5f);
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
        public void APushWhileRecoveringHoldsTheLineOpen()
        {
            var deflection = new BladeDeflection();
            deflection.Kick(Clash(2.0f, 0.8f, Vector3.UnitX));
            Run(deflection, 0.5f);
            Assert.That(deflection.Phase, Is.EqualTo(DeflectionPhase.Recover), "Setup: recovering.");
            var before = deflection.TipOffset;

            deflection.Push(new Vector3(0.01f, 0.0f, 0.0f));

            Assert.That(deflection.Phase, Is.EqualTo(DeflectionPhase.Open), "The Fencer's blade in the way: it cannot recover through it.");
            Assert.That(deflection.TipOffset.X, Is.EqualTo(before.X + 0.01f).Within(1e-5f));
        }

        [Test]
        public void AForteOnTheFoiblePressYieldsMoreThanAFoibleOnTheForte()
        {
            var forteOnFoible = PressYield(dummyFraction: 0.8f, fencerFraction: 0.2f);
            var foibleOnForte = PressYield(dummyFraction: 0.2f, fencerFraction: 0.8f);

            Assert.That(forteOnFoible, Is.GreaterThan(3.0f * foibleOnForte));
            Assert.That(BladeBind.GetPressYieldShare(1.0f, 0.0f), Is.EqualTo(BladeBind.PressYieldAtDummyPoint).Within(1e-6f));
            Assert.That(BladeBind.GetPressYieldShare(0.0f, 0.0f), Is.EqualTo(BladeBind.PressYieldAtDummyGuard).Within(1e-6f));
            Assert.That(BladeBind.GetPressYieldShare(1.0f, 1.0f), Is.EqualTo(BladeBind.PressYieldAtDummyPoint * BladeBind.FencerFoiblePressFactor).Within(1e-6f));
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
                blade.Step(forte, shoulder, Vector3.UnitY, -Vector3.UnitY, settings, farAway, null, FrameSeconds);
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
        public void TheSparkSpotRunsAlongTheDrawnBlade()
        {
            var blade = new ArmedDummyBlade();
            var farAway = new BladeSegment(new Vector3(5.0f, 0.0f, 0.0f), new Vector3(5.9f, 0.0f, 0.0f));
            blade.Step(DummyForte, DummyForte.GuardPosition + new Vector3(0.0f, 0.3f, 0.6f), Vector3.UnitY, -Vector3.UnitY, new BladeSettings(), farAway, null, FrameSeconds);

            Assert.That(blade.GetPointAt(0.0f), Is.EqualTo(blade.Shape[0]));
            Assert.That(blade.GetPointAt(1.0f), Is.EqualTo(blade.Shape[BladeBind.PieceCount]));
            Assert.That(Vector3.Distance(blade.GetPointAt(0.5f), Vector3.Lerp(blade.Shape[0], blade.Shape[BladeBind.PieceCount], 0.5f)), Is.LessThan(0.02f));
        }

        /// <summary>
        /// The Dummy's point's yield to a 4 cm press, with the Fencer's blade across it at <paramref name="dummyFraction"/>
        /// of its blade, at <paramref name="fencerFraction"/> of the Fencer's.
        /// </summary>
        private static float PressYield(float dummyFraction, float fencerFraction)
        {
            var line = new Vector3[BladeBind.PieceCount + 1];
            for (var piece = 0; piece <= BladeBind.PieceCount; piece++)
            {
                line[piece] = Vector3.Lerp(DummyForte.GuardPosition, DummyForte.TipPosition, (float)piece / BladeBind.PieceCount);
            }

            var crossing = Vector3.Lerp(DummyForte.GuardPosition, DummyForte.TipPosition, dummyFraction);
            BladeSegment Across(float height)
            {
                var guard = new Vector3(-0.9f * fencerFraction, height, crossing.Z);
                return new BladeSegment(guard, guard + (Vector3.UnitX * 0.9f));
            }

            var bind = new BladeBind();
            bind.UpdateSide(Across(crossing.Y + 0.1f), null, line);
            return bind.FindYieldTipOffset(Across(crossing.Y - 0.04f), line).Length();
        }
    }
}
