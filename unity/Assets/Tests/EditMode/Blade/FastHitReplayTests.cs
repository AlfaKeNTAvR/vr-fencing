// PER-138: two fast hits replayed from Nikita's headset blade log (blade-20261022-164920.csv, Free Practice "guard",
// Opponent's hand Medium), frame by frame as the app stepped them: the Fencer's sword hand (its guard and real point)
// and each frame's step, against the Armed Dummy en garde. On the headset the Fencer's blade ended up on the far side
// of the Dummy's on the frame after the clash (a one-frame press of 400 to 500 N, all four give stages saturated), and
// pulling back then dragged the Dummy's point 46 cm against the beat in one frame ("bounce back"). Replayed, the side
// latched at first contact holds through the clash, the beat carries the Dummy's blade away on the side it was hit,
// the press stays small, and on the pull-back the Dummy's point never jumps back against the beat. Strip frame
// (+x the Fencer's right, +y up, +z toward the Dummy), meters, seconds, newtons.
using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Blade;
using VrFencing.Settings;

namespace VrFencing.Tests.Blade
{
    public class FastHitReplayTests
    {
        /// <summary>A press of no more than this on any replayed frame: the headset's one-frame spikes were 400 to 515 N.</summary>
        private const float MostPressNewtons = 50.0f;

        /// <summary>"A few cm at most": how far the hands' blades may press into each other on any replayed frame.</summary>
        private const float MostOverlapMeters = 0.05f;

        /// <summary>
        /// The most the Dummy's drawn point may move back against the beat in one frame: its recovery moves it at most
        /// about 1.5 cm a frame; the headset's bounce moved it 46 cm.
        /// </summary>
        private const float MostBackMetersPerFrame = 0.05f;

        /// <summary>
        /// The drawn blades may come this much closer than touching: the scenes' tolerance and a millimeter for the drawn
        /// blade's own curve against the solver's twelve straight pieces, at the speed of a hit.
        /// </summary>
        private const float DrawnToleranceMeters = BladePairScene.ToleranceMeters + 0.001f;

        // The Dummy's en garde line in the log (no knock): its guard and its real point, Strip frame.
        private static readonly BladeSegment DummyForte = new BladeSegment(new Vector3(-0.0389f, 1.0717f, 1.7946f), new Vector3(0.0506f, 1.1050f, 0.8997f));

        // Its sword shoulder, behind and above the guard (not in the log; it only sets how the knocked arm turns).
        private static readonly Vector3 DummyShoulder = DummyForte.GuardPosition + new Vector3(-0.15f, 0.35f, 0.5f);

        // 1.705 to 2.136 s: a hit from the Fencer's left at about 2.3 m/s (the clash at 1.887 s); the sword hand then
        // sweeps on past the Dummy's point. Each row: the frame's step, the Fencer's guard (x, y, z), its real point (x, y, z).
        private const double HitAt1887StartSeconds = 1.6920;

        private static readonly float[,] HitAt1887 =
        {
            { 0.0133f, -0.0785f, 1.0741f, 0.6502f, -0.4805f, 1.3314f, 1.4133f }, // 1.7053 s
            { 0.0143f, -0.0779f, 1.0742f, 0.6502f, -0.4800f, 1.3314f, 1.4132f }, // 1.7196 s
            { 0.0140f, -0.0764f, 1.0745f, 0.6501f, -0.4775f, 1.3354f, 1.4124f }, // 1.7336 s
            { 0.0132f, -0.0748f, 1.0750f, 0.6499f, -0.4748f, 1.3420f, 1.4106f }, // 1.7468 s
            { 0.0148f, -0.0726f, 1.0755f, 0.6499f, -0.4701f, 1.3513f, 1.4088f }, // 1.7617 s
            { 0.0133f, -0.0698f, 1.0755f, 0.6507f, -0.4687f, 1.3533f, 1.4082f }, // 1.7750 s
            { 0.0142f, -0.0654f, 1.0753f, 0.6524f, -0.4644f, 1.3496f, 1.4111f }, // 1.7892 s
            { 0.0132f, -0.0587f, 1.0753f, 0.6548f, -0.4513f, 1.3478f, 1.4174f }, // 1.8024 s
            { 0.0142f, -0.0500f, 1.0746f, 0.6582f, -0.4336f, 1.3442f, 1.4265f }, // 1.8166 s
            { 0.0147f, -0.0386f, 1.0746f, 0.6628f, -0.4131f, 1.3483f, 1.4340f }, // 1.8313 s
            { 0.0130f, -0.0232f, 1.0729f, 0.6708f, -0.3838f, 1.3601f, 1.4438f }, // 1.8443 s
            { 0.0143f, -0.0023f, 1.0684f, 0.6868f, -0.3308f, 1.3503f, 1.4758f }, // 1.8586 s
            { 0.0143f, 0.0330f, 1.0605f, 0.7114f, -0.1925f, 1.3141f, 1.5450f }, // 1.8729 s
            { 0.0136f, 0.0831f, 1.0497f, 0.7364f, 0.0325f, 1.2481f, 1.6128f }, // 1.8865 s
            { 0.0141f, 0.1452f, 1.0331f, 0.7549f, 0.3045f, 1.1295f, 1.6354f }, // 1.9006 s
            { 0.0136f, 0.2084f, 1.0146f, 0.7618f, 0.5673f, 0.9926f, 1.5868f }, // 1.9142 s
            { 0.0138f, 0.2725f, 0.9937f, 0.7520f, 0.8227f, 0.8338f, 1.4460f }, // 1.9280 s
            { 0.0132f, 0.3245f, 0.9707f, 0.7241f, 1.0164f, 0.6726f, 1.2166f }, // 1.9412 s
            { 0.0146f, 0.3502f, 0.9579f, 0.6946f, 1.1153f, 0.5934f, 0.9978f }, // 1.9558 s
            { 0.0139f, 0.3572f, 0.9584f, 0.6901f, 1.1537f, 0.6271f, 0.9464f }, // 1.9697 s
            { 0.0134f, 0.3594f, 0.9662f, 0.7069f, 1.1394f, 0.7273f, 1.0871f }, // 1.9831 s
            { 0.0145f, 0.3624f, 0.9753f, 0.7125f, 1.1110f, 0.8145f, 1.1855f }, // 1.9976 s
            { 0.0138f, 0.3617f, 0.9873f, 0.7144f, 1.0729f, 0.9095f, 1.2603f }, // 2.0113 s
            { 0.0139f, 0.3604f, 0.9963f, 0.7139f, 1.0423f, 0.9840f, 1.3012f }, // 2.0252 s
            { 0.0138f, 0.3583f, 1.0013f, 0.7148f, 1.0174f, 1.0302f, 1.3270f }, // 2.0390 s
            { 0.0142f, 0.3571f, 1.0062f, 0.7164f, 1.0004f, 1.0572f, 1.3438f }, // 2.0532 s
            { 0.0128f, 0.3564f, 1.0112f, 0.7161f, 0.9926f, 1.0749f, 1.3496f }, // 2.0660 s
            { 0.0143f, 0.3559f, 1.0153f, 0.7155f, 0.9855f, 1.0908f, 1.3542f }, // 2.0803 s
            { 0.0155f, 0.3565f, 1.0176f, 0.7148f, 0.9854f, 1.0916f, 1.3543f }, // 2.0958 s
            { 0.0130f, 0.3568f, 1.0209f, 0.7137f, 0.9909f, 1.0962f, 1.3479f }, // 2.1087 s
            { 0.0138f, 0.3576f, 1.0242f, 0.7128f, 0.9995f, 1.0992f, 1.3391f }, // 2.1225 s
            { 0.0137f, 0.3581f, 1.0274f, 0.7121f, 1.0073f, 1.1026f, 1.3309f }, // 2.1362 s
        };

        // 17.859 to 18.387 s: a hit from the Fencer's left at about 2.5 m/s (the clash at 18.012 s), on past the Dummy's
        // line, then pulled straight back (18.08 to 18.15 s) to where it started.
        private const double HitAt18012StartSeconds = 17.8444;

        private static readonly float[,] HitAt18012 =
        {
            { 0.0143f, -0.0377f, 1.0692f, 0.8193f, -0.4631f, 1.5454f, 1.4535f }, // 17.8587 s
            { 0.0140f, -0.0360f, 1.0685f, 0.8209f, -0.4601f, 1.5431f, 1.4573f }, // 17.8727 s
            { 0.0141f, -0.0338f, 1.0678f, 0.8227f, -0.4553f, 1.5402f, 1.4625f }, // 17.8868 s
            { 0.0140f, -0.0311f, 1.0672f, 0.8247f, -0.4478f, 1.5387f, 1.4681f }, // 17.9008 s
            { 0.0135f, -0.0278f, 1.0666f, 0.8268f, -0.4390f, 1.5360f, 1.4753f }, // 17.9142 s
            { 0.0141f, -0.0234f, 1.0657f, 0.8298f, -0.4269f, 1.5304f, 1.4865f }, // 17.9283 s
            { 0.0134f, -0.0173f, 1.0645f, 0.8344f, -0.4130f, 1.5184f, 1.5033f }, // 17.9417 s
            { 0.0144f, -0.0079f, 1.0628f, 0.8415f, -0.3939f, 1.4971f, 1.5287f }, // 17.9561 s
            { 0.0137f, 0.0055f, 1.0605f, 0.8516f, -0.3781f, 1.4815f, 1.5485f }, // 17.9698 s
            { 0.0144f, 0.0262f, 1.0575f, 0.8702f, -0.3326f, 1.4824f, 1.5778f }, // 17.9842 s
            { 0.0137f, 0.0605f, 1.0550f, 0.8978f, -0.1970f, 1.4825f, 1.6468f }, // 17.9979 s
            { 0.0140f, 0.1132f, 1.0502f, 0.9292f, 0.0355f, 1.4334f, 1.7398f }, // 18.0118 s
            { 0.0143f, 0.1712f, 1.0424f, 0.9545f, 0.2945f, 1.3426f, 1.7939f }, // 18.0262 s
            { 0.0130f, 0.2389f, 1.0317f, 0.9584f, 0.6254f, 1.2286f, 1.7469f }, // 18.0391 s
            { 0.0148f, 0.2897f, 1.0193f, 0.9430f, 0.9062f, 1.1320f, 1.5890f }, // 18.0540 s
            { 0.0131f, 0.3040f, 1.0088f, 0.9276f, 1.0475f, 1.0938f, 1.4276f }, // 18.0671 s
            { 0.0141f, 0.2891f, 1.0108f, 0.9354f, 1.0118f, 1.1430f, 1.4552f }, // 18.0812 s
            { 0.0142f, 0.2546f, 1.0214f, 0.9547f, 0.7850f, 1.2569f, 1.6426f }, // 18.0954 s
            { 0.0137f, 0.2069f, 1.0298f, 0.9529f, 0.4695f, 1.3608f, 1.7476f }, // 18.1091 s
            { 0.0148f, 0.1684f, 1.0313f, 0.9432f, 0.2026f, 1.3950f, 1.7657f }, // 18.1239 s
            { 0.0126f, 0.1358f, 1.0315f, 0.9305f, -0.0047f, 1.4054f, 1.7370f }, // 18.1365 s
            { 0.0142f, 0.1142f, 1.0316f, 0.9188f, -0.1173f, 1.4131f, 1.7004f }, // 18.1507 s
            { 0.0140f, 0.0978f, 1.0344f, 0.9104f, -0.1715f, 1.4253f, 1.6750f }, // 18.1647 s
            { 0.0136f, 0.0858f, 1.0373f, 0.9037f, -0.1865f, 1.4416f, 1.6602f }, // 18.1783 s
            { 0.0140f, 0.0758f, 1.0396f, 0.8998f, -0.1980f, 1.4438f, 1.6558f }, // 18.1923 s
            { 0.0134f, 0.0691f, 1.0407f, 0.8981f, -0.1893f, 1.4410f, 1.6617f }, // 18.2057 s
            { 0.0147f, 0.0610f, 1.0417f, 0.8945f, -0.1917f, 1.4460f, 1.6579f }, // 18.2205 s
            { 0.0133f, 0.0529f, 1.0432f, 0.8919f, -0.2103f, 1.4491f, 1.6508f }, // 18.2338 s
            { 0.0141f, 0.0466f, 1.0444f, 0.8899f, -0.2260f, 1.4530f, 1.6440f }, // 18.2480 s
            { 0.0140f, 0.0411f, 1.0456f, 0.8880f, -0.2385f, 1.4569f, 1.6381f }, // 18.2620 s
            { 0.0134f, 0.0363f, 1.0467f, 0.8861f, -0.2496f, 1.4620f, 1.6317f }, // 18.2754 s
            { 0.0144f, 0.0318f, 1.0476f, 0.8841f, -0.2625f, 1.4661f, 1.6245f }, // 18.2898 s
            { 0.0154f, 0.0283f, 1.0490f, 0.8826f, -0.2741f, 1.4699f, 1.6184f }, // 18.3052 s
            { 0.0127f, 0.0269f, 1.0503f, 0.8819f, -0.2715f, 1.4755f, 1.6168f }, // 18.3179 s
            { 0.0141f, 0.0261f, 1.0510f, 0.8817f, -0.2613f, 1.4775f, 1.6202f }, // 18.3320 s
            { 0.0123f, 0.0252f, 1.0513f, 0.8812f, -0.2521f, 1.4751f, 1.6252f }, // 18.3443 s
            { 0.0144f, 0.0239f, 1.0518f, 0.8797f, -0.2469f, 1.4777f, 1.6248f }, // 18.3587 s
            { 0.0138f, 0.0215f, 1.0520f, 0.8778f, -0.2515f, 1.4811f, 1.6203f }, // 18.3725 s
            { 0.0141f, 0.0188f, 1.0518f, 0.8763f, -0.2623f, 1.4799f, 1.6164f }, // 18.3865 s
        };

        [Test]
        public void TheHitAt1887HoldsItsSideAndPressesLightly([Values(OpponentHand.Light, OpponentHand.Medium, OpponentHand.Strong)] OpponentHand hand)
        {
            var replay = Replay.Run(HitAt1887, HitAt1887StartSeconds, hand);
            replay.AssertClash();
            replay.AssertNoSideFlipWhileEngaged();
            replay.AssertSmallPress();
            replay.AssertDrawnBladesApart();
            replay.AssertNoJumpAgainstTheBeat();
        }

        [Test]
        public void TheHitAt18012AndItsPullBackNeverBounceTheDummysBladeBack([Values(OpponentHand.Light, OpponentHand.Medium, OpponentHand.Strong)] OpponentHand hand)
        {
            var replay = Replay.Run(HitAt18012, HitAt18012StartSeconds, hand);
            replay.AssertClash();
            replay.AssertNoSideFlipWhileEngaged();
            replay.AssertSmallPress();
            replay.AssertDrawnBladesApart();
            replay.AssertNoJumpAgainstTheBeat();

            // Pulled straight back to where it started: still on the side it hit from, never across.
            Assert.That(Vector3.Dot(replay.FinalSide, replay.LatchedSide), Is.GreaterThan(0.0f), "Back where it started, the Fencer's blade is on the side it hit from.");
        }

        [Test]
        public void TheReplayedHitsStayWithinTheFrameBudget()
        {
            // Both replays, each frame's Dummy step (the contact solve included) timed, the best of three rounds (the first
            // also compiles the code). A hit's frame sweeps the hands' move, so it costs more than a press's (about 1 ms with
            // dotnet, 2 ms in the Unity Editor): still a small share of the 13.9 ms of a 72 Hz frame, and only on a hit.
            var mostMilliseconds = double.MaxValue;
            for (var round = 0; round < 3; round++)
            {
                mostMilliseconds = Math.Min(mostMilliseconds, Math.Max(Replay.Run(HitAt1887, HitAt1887StartSeconds, OpponentHand.Medium).MostStepMilliseconds, Replay.Run(HitAt18012, HitAt18012StartSeconds, OpponentHand.Medium).MostStepMilliseconds));
            }

            TestContext.WriteLine($"Slowest replayed frame {mostMilliseconds:0.000} ms.");
            Assert.That(mostMilliseconds, Is.LessThan(4.0), "One hit's frame within the budget.");
        }

        /// <summary>One replay: every frame stepped as the app steps it, and what the checks need kept.</summary>
        private sealed class Replay
        {
            private readonly List<Frame> _frames = new List<Frame>();

            public Vector3 LatchedSide { get; private set; }

            public Vector3 FinalSide { get; private set; }

            public BladeClash? FirstClash { get; private set; }

            public double MostStepMilliseconds { get; private set; }

            /// <param name="startSeconds">The log's time just before the first row (the times printed are the log's).</param>
            public static Replay Run(float[,] rows, double startSeconds, OpponentHand hand)
            {
                var replay = new Replay();
                var dummy = new ArmedDummyBlade();
                var detector = new BladeClashDetector();
                var fencer = new DrawnBlade();
                var fencerSettings = new BladeSettings();

                // The Dummy's line in the log is its real point already: drawn straight on it (no set, no sag).
                var dummySettings = new BladeSettings { SetSagittaMeters = 0.0f };
                var obstacles = new List<BladeObstacle> { BladeObstacle.Floor };
                var watch = new System.Diagnostics.Stopwatch();
                var now = startSeconds;
                for (var row = 0; row < rows.GetLength(0); row++)
                {
                    var deltaSeconds = rows[row, 0];
                    now += deltaSeconds;
                    var guard = new Vector3(rows[row, 1], rows[row, 2], rows[row, 3]);
                    var point = new Vector3(rows[row, 4], rows[row, 5], rows[row, 6]);
                    var fencerHand = new BladeSegment(guard, point);
                    var fencerDroop = BladeDroop.Create(fencerHand.Direction, Vector3.UnitY, -Vector3.UnitY, fencerSettings);

                    watch.Restart();
                    // As the Armed Dummy steps it: its hand and the contact, then the clash, whose knock the next step sees.
                    dummy.Step(DummyForte, DummyShoulder, Vector3.UnitY, Vector3.Zero, dummySettings, hand, fencerHand, fencerDroop, deltaSeconds);
                    var clash = detector.Step(now, fencerHand, dummy.Chord);
                    if (clash.HasValue)
                    {
                        dummy.Knock(clash.Value);
                    }

                    watch.Stop();
                    replay.MostStepMilliseconds = Math.Max(replay.MostStepMilliseconds, watch.Elapsed.TotalMilliseconds);
                    if (dummy.Contact.IsNear)
                    {
                        fencer.Flex.Reset();
                    }

                    fencer.Step(dummy.FencerGive.Chord, obstacles, Vector3.UnitY, deltaSeconds, dummy.FencerGive.Droop);
                    replay.Keep(dummy, fencer, clash, now, fencerHand);
                }

                replay.Print();
                return replay;
            }

            public void AssertClash()
            {
                Assert.That(FirstClash.HasValue, Is.True, "Setup: the blades clash.");
                Assert.That(FirstClash.Value.RelativeSpeedMetersPerSecond, Is.GreaterThan(BladeDeflection.BeatMinSpeedMetersPerSecond), "Setup: a beat.");
            }

            public void AssertNoSideFlipWhileEngaged()
            {
                for (var index = 1; index < _frames.Count; index++)
                {
                    var frame = _frames[index];
                    if (frame.Time < FirstClash.Value.TimeSeconds || !_frames[index - 1].IsEngaged)
                    {
                        continue;
                    }

                    Assert.That(Vector3.Dot(frame.Side, _frames[index - 1].Side), Is.GreaterThan(0.0f), $"{frame.Time:0.000} s: the side flipped while engaged.");
                }
            }

            public void AssertSmallPress()
            {
                foreach (var frame in _frames)
                {
                    Assert.That(frame.OverlapMeters, Is.LessThanOrEqualTo(MostOverlapMeters), $"{frame.Time:0.000} s: the hands' blades {frame.OverlapMeters * 100.0f:0} cm into each other.");
                    Assert.That(frame.PressNewtons, Is.LessThan(MostPressNewtons), $"{frame.Time:0.000} s: a {frame.PressNewtons:0} N press.");
                }
            }

            public void AssertDrawnBladesApart()
            {
                foreach (var frame in _frames)
                {
                    Assert.That(frame.DrawnMeters, Is.GreaterThan(BladeContactSolver.ContactMeters - DrawnToleranceMeters), $"{frame.Time:0.000} s: the drawn blades into each other.");
                }
            }

            public void AssertNoJumpAgainstTheBeat()
            {
                var beat = FirstClash.Value.PushDirection;
                for (var index = 1; index < _frames.Count; index++)
                {
                    if (_frames[index].Time <= FirstClash.Value.TimeSeconds)
                    {
                        continue;
                    }

                    var back = Vector3.Dot(_frames[index - 1].DummyPoint - _frames[index].DummyPoint, beat);
                    Assert.That(back, Is.LessThan(MostBackMetersPerFrame), $"{_frames[index].Time:0.000} s: the Dummy's point jumped {back * 100.0f:0} cm back against the beat.");
                }
            }

            private void Keep(ArmedDummyBlade dummy, DrawnBlade fencer, BladeClash? clash, double now, BladeSegment fencerHand)
            {
                var contact = dummy.Contact;
                if (clash.HasValue && !FirstClash.HasValue)
                {
                    FirstClash = clash;
                    LatchedSide = contact.Side.Value;
                }

                var side = contact.Side ?? Vector3.Zero;
                FinalSide = side;
                _frames.Add(new Frame
                {
                    Time = now,
                    Side = side,
                    IsEngaged = contact.IsEngaged,
                    OverlapMeters = contact.PressMeters,
                    PressNewtons = contact.PressNewtons,
                    Saturated = contact.SaturatedStages,
                    Sliding = contact.SlidingBlade,
                    Returning = contact.ReturningBlade,
                    FencerPoint = fencer.GetSample(DrawnBlade.SampleCount),
                    DummyPoint = dummy.Drawn.GetSample(DrawnBlade.SampleCount),
                    DummyOffset = dummy.Deflection.TipOffset,
                    FencerTurn = contact.GiveA.TurnDegrees,
                    DummyTurn = contact.GiveB.TurnDegrees,
                    DrawnMeters = BladePairScene.FindDistance(BladePairScene.Samples(fencer), BladePairScene.Samples(dummy.Drawn)),
                    IsClash = clash.HasValue,
                    FractionA = contact.IsInContact ? contact.FractionA : float.NaN,
                    FractionB = contact.IsInContact ? contact.FractionB : float.NaN,
                    DummyOffsetLength = dummy.Deflection.TipOffset.Length(),
                    HandsApartMeters = SegmentMath.FindDistanceBetweenSegments(fencerHand.GuardPosition, fencerHand.TipPosition, dummy.HandChord.GuardPosition, dummy.HandChord.TipPosition, out _),
                });
            }

            private void Print()
            {
                TestContext.WriteLine("time    side(x y z)          eng overlap  press  sat sl rt f.pt.x d.pt.x d.off.x f.turn d.turn  drawn  fA   fB  |off| hands");
                foreach (var f in _frames)
                {
                    TestContext.WriteLine(
                        $"{f.Time:0.000} {f.Side.X,6:0.00}{f.Side.Y,6:0.00}{f.Side.Z,6:0.00} {(f.IsEngaged ? 1 : 0),4} {f.OverlapMeters,7:0.000} {f.PressNewtons,6:0.0} {f.Saturated,4} {f.Sliding,2} {f.Returning,2} " +
                        $"{f.FencerPoint.X,6:0.00} {f.DummyPoint.X,6:0.00} {f.DummyOffset.X,7:0.00} {f.FencerTurn,6:0.0} {f.DummyTurn,6:0.0} {f.DrawnMeters * 100.0f,5:0.0} {f.FractionA,4:0.00} {f.FractionB,4:0.00} {f.DummyOffsetLength,5:0.00} {f.HandsApartMeters * 100.0f,5:0.0}{(f.IsClash ? " clash" : string.Empty)}");
                }
            }

            private struct Frame
            {
                public double Time;
                public Vector3 Side;
                public bool IsEngaged;
                public float OverlapMeters;
                public float PressNewtons;
                public int Saturated;
                public int Sliding;
                public int Returning;
                public Vector3 FencerPoint;
                public Vector3 DummyPoint;
                public Vector3 DummyOffset;
                public float FencerTurn;
                public float DummyTurn;
                public float DrawnMeters;
                public bool IsClash;
                public float FractionA;
                public float FractionB;
                public float DummyOffsetLength;
                public float HandsApartMeters;
            }
        }
    }
}
