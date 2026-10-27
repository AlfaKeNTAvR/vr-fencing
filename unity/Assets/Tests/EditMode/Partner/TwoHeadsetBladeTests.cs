// Multiplayer blade contact (PER-145): two headsets each solving both blades locally, at 0, 50, 100 and 200 ms one way,
// clean or with 20 ms jitter, 2% loss and a 15 ms clock offset error, and the Host's replay judge ruling the Touches.
// In every view the drawn blades never pass through each other, a slow bind is seen alike in both views, a parry inside
// the watch keeps the attack from lighting (no claim, or a Parried verdict), a parry after the point arrived does not,
// and a clean hit lights. The two headsets' frame cost is measured and kept within the blades' budget. A "Mirror me"
// sweep (PER-147: the partner this Fencer's own hand mirrored, at 0, 100 and 200 ms, clean and rough) pushes the blades
// 60 cm through each other and back: in both views no turn passes the release's, no drawn point jumps beyond its hand,
// nothing shakes, and the hands never press on past the through depth.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Blade;
using VrFencing.Partner;
using VrFencing.Tests.Blade;

namespace VrFencing.Tests.Partner
{
    public class TwoHeadsetBladeTests
    {
        private const double ParryStartSeconds = 0.33;

        /// <summary>The Partner's frame, both blades and their one solve, as against the Dummy (BladeGuardPressTests: 0.45 ms).</summary>
        private const double PartnerBudgetMilliseconds = 0.45;

        /// <summary>
        /// The Host's frame adds the replay judge's second solve (no drawn blade) in a sustained deep press: about twice the
        /// Partner's, held to 2.5 ms (in the Editor), well under the 4 ms slowest-frame budget of FastHitReplayTests.
        /// </summary>
        private const double HostBudgetMilliseconds = 2.5;

        // The Host's chest (torso centre) and the Partner's, in the world (the Host's Strip frame).
        private static readonly Vector3 HostChest = TwoHeadsetScene.GetTorsoCenter(TwoHeadsetScene.BodyAt(TwoHeadsetScene.HostBodyOffset));

        private static readonly Vector3 PartnerChest = new Vector3(-HostChest.X, HostChest.Y, 6.0f - HostChest.Z);

        /// <summary>The Partner's guard en garde: their sword hand is on the world's -x (they face -z).</summary>
        private static readonly Vector3 PartnerGuard = PartnerChest + new Vector3(-0.2f, -0.05f, -0.4f);

        /// <summary>Each one-way delay, clean, and with jitter, loss and a clock offset error.</summary>
        public static IEnumerable<TestCaseData> Links()
        {
            foreach (var oneWay in new[] { 0.0, 0.05, 0.1, 0.2 })
            {
                yield return new TestCaseData(new TwoHeadsetLink(oneWay, 0.0, 0.0, 0.0)).SetName($"Clean {oneWay * 1000:0} ms");
                yield return new TestCaseData(new TwoHeadsetLink(oneWay, 0.02, 0.02, 0.015)).SetName($"Rough {oneWay * 1000:0} ms");
            }
        }

        [TestCaseSource(nameof(Links))]
        public void AParryInsideTheWatchKeepsTheAttackFromLighting(TwoHeadsetLink link)
        {
            var scene = Run(HostParry(ParryStartSeconds), PartnerLunge, link);

            TestContext.WriteLine(Describe(scene, link));
            Assert.That(scene.Lights.Where(light => light.Lights(BoutSide.Partner)), Is.Empty, Describe(scene, link));
            var partnerClaims = scene.Claims.Count(claim => claim.Attacker == BoutSide.Partner);
            Assert.That(scene.Lights.Count(light => light.Kind == LightKind.Parried), Is.EqualTo(Math.Min(1, partnerClaims)),
                "A claim the attacker's view made is refused as Parried; no claim, no verdict. " + Describe(scene, link));
        }

        [TestCaseSource(nameof(Links))]
        public void AParryAfterThePointArrivedDoesNotStopTheTouch(TwoHeadsetLink link)
        {
            // The point is on the chest from about 0.55 s; the same parry 0.3 s later is too late.
            var scene = Run(HostParry(ParryStartSeconds + 0.3), PartnerLunge, link);

            Assert.That(scene.Lights.Count(light => light.Kind == LightKind.PartnerTouch), Is.EqualTo(1), Describe(scene, link));
            Assert.That(scene.Lights.Any(light => light.Kind == LightKind.Parried), Is.False, Describe(scene, link));
        }

        [TestCaseSource(nameof(Links))]
        public void ACleanHitLights(TwoHeadsetLink link)
        {
            var scene = Run(HostOutOfLine, PartnerLunge, link);

            Assert.That(scene.Lights.Count(light => light.Kind == LightKind.PartnerTouch), Is.EqualTo(1), Describe(scene, link));
            Assert.That(scene.Log, Has.Some.Contains("Touch,"), "The replay judge let it stand.");
        }

        [TestCaseSource(nameof(Links))]
        public void TheDrawnBladesNeverPassThroughInEitherView(TwoHeadsetLink link)
        {
            foreach (var (name, host, partner) in new (string, Func<double, BladeAim>, Func<double, BladeAim>)[]
            {
                ("parry", HostParry(ParryStartSeconds), PartnerLunge),
                ("beat", HostBeat, PartnerEnGarde),
                ("bind", HostSlowBind, PartnerEnGarde),
            })
            {
                var scene = Run(host, partner, link);
                Assert.That(scene.WorstHostGapMeters, Is.GreaterThanOrEqualTo(-TwoHeadsetScene.ToleranceMeters), $"{name}: the Host's view. {Describe(scene, link)}");
                Assert.That(scene.WorstPartnerGapMeters, Is.GreaterThanOrEqualTo(-TwoHeadsetScene.ToleranceMeters), $"{name}: the Partner's view. {Describe(scene, link)}");
            }
        }

        [TestCaseSource(nameof(Links))]
        public void ASlowBindIsSeenAlikeInBothViews(TwoHeadsetLink link)
        {
            var scene = Run(HostSlowBind, PartnerEnGarde, link, 2.5);

            // The Partner sees the Host's blade arrive one lag later (the trip, the jitter, the link's 40 ms buffer).
            var lagFrames = (link.OneWaySeconds + link.JitterSeconds + 0.15) / TwoHeadsetScene.FrameSeconds;
            Assert.That(scene.BothPressingFrames, Is.GreaterThan(72), "Both views press for over a second. " + Describe(scene, link));
            Assert.That(scene.AnyPressingFrames - scene.BothPressingFrames, Is.LessThanOrEqualTo(lagFrames), "They press together but for about one lag. " + Describe(scene, link));
            Assert.That(scene.SameSideFrames, Is.EqualTo(scene.BothPressingFrames), "On the same side all along. " + Describe(scene, link));
        }

        /// <summary>"Mirror me": each hand speed at each one-way delay, clean and with jitter, loss and a clock error.</summary>
        public static IEnumerable<TestCaseData> MirrorSweeps()
        {
            foreach (var speed in new[] { 0.5f, 1.0f, 1.5f })
            {
                foreach (var oneWay in new[] { 0.0, 0.1, 0.2 })
                {
                    yield return new TestCaseData(speed, new TwoHeadsetLink(oneWay, 0.0, 0.0, 0.0)).SetName($"Mirror {speed:0.0} m/s clean {oneWay * 1000:0} ms");
                    yield return new TestCaseData(speed, new TwoHeadsetLink(oneWay, 0.02, 0.02, 0.015)).SetName($"Mirror {speed:0.0} m/s rough {oneWay * 1000:0} ms");
                }
            }
        }

        [TestCaseSource(nameof(MirrorSweeps))]
        public void AMirroredSweepThroughAndBackStaysBoundedInBothViews(float speedMetersPerSecond, TwoHeadsetLink link)
        {
            var scene = Run(HostSweep(speedMetersPerSecond), PartnerMirroring(HostSweep(speedMetersPerSecond)), link, 4.0);

            var context = $"Link {link}: host {scene.HostTrace} (deepest {scene.DeepestHostPressMeters * 100:0.0} cm); partner {scene.PartnerTrace} (deepest {scene.DeepestPartnerPressMeters * 100:0.0} cm).";
            TestContext.WriteLine(context);
            Assert.That(scene.AnyPressingFrames, Is.GreaterThan(0), "Setup: the blades press. " + context);
            scene.HostTrace.AssertBounded("The Host's view. " + context);
            scene.PartnerTrace.AssertBounded("The Partner's view. " + context);
            Assert.That(scene.DeepestHostPressMeters, Is.LessThanOrEqualTo(DeepCrossingTests.MostThroughMeters), "The Host's view lets go by the through depth. " + context);
            Assert.That(scene.DeepestPartnerPressMeters, Is.LessThanOrEqualTo(DeepCrossingTests.MostThroughMeters), "The Partner's view lets go by the through depth. " + context);
        }

        [Test]
        public void BothHeadsetsStayWithinTheBladesFrameBudget()
        {
            // The parry at 100 ms with a rough link: a press, a slide-off and a ruling. Best of three (a busy laptop).
            var hostMilliseconds = double.MaxValue;
            var replayMilliseconds = double.MaxValue;
            var partnerMilliseconds = double.MaxValue;
            for (var run = 0; run < 3; run++)
            {
                var scene = Run(HostParry(ParryStartSeconds), PartnerLunge, new TwoHeadsetLink(0.1, 0.02, 0.02, 0.015));
                hostMilliseconds = Math.Min(hostMilliseconds, (scene.HostBladeMilliseconds + scene.ReplayWatch.Elapsed.TotalMilliseconds) / scene.Frames);
                replayMilliseconds = Math.Min(replayMilliseconds, scene.ReplayWatch.Elapsed.TotalMilliseconds / scene.Frames);
                partnerMilliseconds = Math.Min(partnerMilliseconds, scene.PartnerBladeMilliseconds / scene.Frames);
            }

            TestContext.WriteLine($"Host frame (own solve, two drawn blades, replay judge): {hostMilliseconds:0.000} ms, of which the replay judge {replayMilliseconds:0.000} ms; " +
                $"Partner frame (own solve, two drawn blades): {partnerMilliseconds:0.000} ms.");
            Assert.That(partnerMilliseconds, Is.LessThan(PartnerBudgetMilliseconds));
            Assert.That(hostMilliseconds, Is.LessThan(HostBudgetMilliseconds));
        }

        private static TwoHeadsetScene Run(Func<double, BladeAim> host, Func<double, BladeAim> partner, TwoHeadsetLink link, double seconds = 2.0)
        {
            var scene = new TwoHeadsetScene(host, partner, link);
            scene.RunFor(seconds);
            return scene;
        }

        private static string Describe(TwoHeadsetScene scene, TwoHeadsetLink link)
        {
            return $"Link {link}: claims {string.Join(", ", scene.Claims)}; lights {string.Join(", ", scene.Lights.Select(light => light.Kind))}; " +
                $"gaps {scene.WorstHostGapMeters:0.0000} / {scene.WorstPartnerGapMeters:0.0000} m; pressing both {scene.BothPressingFrames}, any {scene.AnyPressingFrames}, same side {scene.SameSideFrames}; " +
                string.Join(" ", scene.Log);
        }

        /// <summary>En garde, still: the point toward the Host's chest from 0.4 m before the Partner's.</summary>
        private static BladeAim PartnerEnGarde(double t)
        {
            return PartnerLunge(0.0);
        }

        /// <summary>Lunge from 0.3 s: the point from en garde straight at the Host's chest in 0.3 s, 8 cm past its middle.</summary>
        private static BladeAim PartnerLunge(double t)
        {
            var direction = AttackDirection();
            var travel = Vector3.Distance(AttackTarget(), PartnerGuard) - 0.9f + 0.08f;
            var s = SmoothStep((t - 0.3) / 0.3);
            return new BladeAim(PartnerGuard + (direction * (travel * s)), direction, Vector3.UnitY);
        }

        private static Vector3 AttackTarget()
        {
            return HostChest + new Vector3(0.0f, 0.05f, 0.0f);
        }

        private static Vector3 AttackDirection()
        {
            return Vector3.Normalize(AttackTarget() - PartnerGuard);
        }

        /// <summary>The Host's blade low and out of line (seconde-ish), never in the attack's way.</summary>
        private static BladeAim HostOutOfLine(double t)
        {
            return new BladeAim(HostChest + new Vector3(0.25f, -0.2f, 0.35f), new Vector3(0.45f, -0.55f, 1.0f), Vector3.UnitY);
        }

        /// <summary>
        /// A parry from out of line at <paramref name="startSeconds"/>: in 40 ms the blade comes onto the attack's line
        /// (back toward the attacker, 12 cm to its right), then in 70 ms sweeps 22 cm to the left through it, and holds.
        /// The lunge's point would arrive about 0.2 s after a start at 0.33 s.
        /// </summary>
        private static Func<double, BladeAim> HostParry(double startSeconds)
        {
            return t =>
            {
                var start = HostOutOfLine(t);
                var ready = AlongAttack(0.12f - (0.22f * SmoothStep((t - startSeconds - 0.04) / 0.07)));
                var s = SmoothStep((t - startSeconds) / 0.04);
                return new BladeAim(Vector3.Lerp(start.Guard, ready.Guard, s), Vector3.Lerp(start.Direction, ready.Direction, s), Vector3.UnitY);
            };
        }

        /// <summary>A beat on the still blade from 0.3 s: 27 cm across it in 60 ms (about 4.5 m/s) and back.</summary>
        private static BladeAim HostBeat(double t)
        {
            var across = SmoothStep((t - 0.3) / 0.06) - SmoothStep((t - 0.5) / 0.1);
            return AlongAttack(0.12f - (0.27f * across));
        }

        /// <summary>A slow bind on the still blade from 0.3 s: from 10 cm to its right to 3 cm past it in 1 s (13 cm/s), held.</summary>
        private static BladeAim HostSlowBind(double t)
        {
            return AlongAttack(0.10f - (0.13f * (float)Math.Max(0.0, Math.Min(1.0, (t - 0.3) / 1.0))));
        }

        /// <summary>The Host's blade on the attack's line (its guard 35 cm before the chest), pointing back along it, moved sideways.</summary>
        private static BladeAim AlongAttack(float sidewaysMeters)
        {
            var direction = AttackDirection();
            var guardZ = HostChest.Z + 0.35f;
            var onLine = PartnerGuard + (direction * ((guardZ - PartnerGuard.Z) / direction.Z));
            return new BladeAim(onLine + new Vector3(sidewaysMeters, 0.0f, 0.0f), -direction, Vector3.UnitY);
        }

        /// <summary>
        /// The Host's blade on the line toward the partner, rising a little, swept side to side 30 cm each way through the
        /// middle of the Strip, its guard at most <paramref name="speedMetersPerSecond"/> fast; from 30 cm to the left.
        /// </summary>
        private static Func<double, BladeAim> HostSweep(float speedMetersPerSecond)
        {
            const float Reach = 0.3f;
            var guard = new Vector3(0.0f, HostChest.Y - 0.05f, HostChest.Z + 0.25f);
            return t => new BladeAim(guard + new Vector3(-Reach * (float)Math.Cos(speedMetersPerSecond / Reach * t), 0.0f, 0.0f), new Vector3(0.0f, 0.15f, 1.0f), Vector3.UnitY);
        }

        /// <summary>The partner as "Mirror me" draws them: the Host's own hand turned half round the middle of the Strip.</summary>
        private static Func<double, BladeAim> PartnerMirroring(Func<double, BladeAim> host)
        {
            var middle = (HostChest + PartnerChest) * 0.5f;
            return t =>
            {
                var aim = host(t);
                var guard = aim.Guard - middle;
                return new BladeAim(middle + new Vector3(-guard.X, guard.Y, -guard.Z), new Vector3(-aim.Direction.X, aim.Direction.Y, -aim.Direction.Z), aim.Up);
            };
        }

        private static float SmoothStep(double x)
        {
            var clamped = Math.Max(0.0, Math.Min(1.0, x));
            return (float)(clamped * clamped * (3.0 - (2.0 * clamped)));
        }
    }
}
