// Two headsets in one test (PER-145): a Host and a Partner, each solving both blades locally (its own hand fresh, the
// other's as its OpponentLink draws it after the link's delay, jitter and loss), each claiming its Touches on its own
// solved blade against the other's body as drawn there, and the Host's replay judge ruling every claim on both real
// hands at the claim's moment. Both Fencers are scripted in the Host's Strip frame (the "world"); the Partner sends
// its poses in its own Strip frame (the bout Strip's half turn), with a clock 500 s behind the Host's. Each view's own
// solve is traced (GiveTrace, PER-147). Meters, seconds.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using VrFencing.Blade;
using VrFencing.Dummy;
using VrFencing.Partner;
using VrFencing.Session;
using VrFencing.Settings;
using VrFencing.Tests.Blade;

namespace VrFencing.Tests.Partner
{
    /// <summary>Where a scripted Fencer holds their blade: the guard and the blade's direction, with the grip's up.</summary>
    internal readonly struct BladeAim
    {
        public BladeAim(Vector3 guard, Vector3 direction, Vector3 up)
        {
            Guard = guard;
            Direction = Vector3.Normalize(direction);
            Up = up;
        }

        public Vector3 Guard { get; }

        public Vector3 Direction { get; }

        public Vector3 Up { get; }
    }

    /// <summary>The pretend link between the two headsets, the same both ways.</summary>
    public readonly struct TwoHeadsetLink
    {
        public TwoHeadsetLink(double oneWaySeconds, double jitterSeconds, double lossFraction, double clockErrorSeconds)
        {
            OneWaySeconds = oneWaySeconds;
            JitterSeconds = jitterSeconds;
            LossFraction = lossFraction;
            ClockErrorSeconds = clockErrorSeconds;
        }

        public double OneWaySeconds { get; }

        public double JitterSeconds { get; }

        public double LossFraction { get; }

        /// <summary>How wrong the synced clock offset is (an asymmetric route), seconds.</summary>
        public double ClockErrorSeconds { get; }

        public override string ToString()
        {
            return FormattableString.Invariant($"{OneWaySeconds * 1000:0} ms, jitter {JitterSeconds * 1000:0} ms, loss {LossFraction * 100:0}%, clock error {ClockErrorSeconds * 1000:0} ms");
        }
    }

    internal sealed class TwoHeadsetScene
    {
        public const double FrameSeconds = 1.0 / 72.0;

        /// <summary>The Partner's clock reads this much less than the Host's.</summary>
        public const double PartnerBehindHostSeconds = 500.0;

        public const float HeightMeters = 1.7f;

        /// <summary>The drawn blades may come this much closer than touching (pieces of a curve), as in the Dummy's tests.</summary>
        public const float ToleranceMeters = 0.003f;

        private static readonly StripExtent Strip = new StripExtent(8.0f, -1.0f);
        private static readonly TouchRules Rules = TouchRules.For(TouchRule.Loose, TouchPressure.Realistic);

        private readonly Func<double, BladeAim> _hostScript;
        private readonly Func<double, BladeAim> _partnerScript;
        private readonly TwoHeadsetLink _link;
        private readonly BoutStrip _bout = BoutStrip.Create(Strip, Strip);
        private readonly BladeSettings _blade = new BladeSettings();
        private readonly Headset _host;
        private readonly Headset _partner;
        private readonly MirrorLoopback _toHost;
        private readonly MirrorLoopback _toPartner;
        private readonly List<(double ArriveHostSeconds, TouchClaim Claim)> _claimsToHost = new List<(double, TouchClaim)>();
        private readonly SendSchedule _bodySchedule = new SendSchedule(30.0);
        private int _partnerClaims;

        public TwoHeadsetScene(Func<double, BladeAim> hostScript, Func<double, BladeAim> partnerScript, TwoHeadsetLink link, int seed = 145)
        {
            _hostScript = hostScript;
            _partnerScript = partnerScript;
            _link = link;
            _toHost = new MirrorLoopback(link.OneWaySeconds, seed);
            _toPartner = new MirrorLoopback(link.OneWaySeconds, seed + 1);
            _toHost.SetNoise(link.JitterSeconds, link.LossFraction);
            _toPartner.SetNoise(link.JitterSeconds, link.LossFraction);
            HostBody = BodyAt(HostBodyOffset);
            PartnerBodyInItsFrame = BodyAt(HostBodyOffset);
            PartnerBodyInWorld = PartnerBodyInItsFrame.Transform(_bout.ToMyStripFrame, Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)Math.PI));
            _host = new Headset(Strip);
            Judge.SetRoundTrip(2.0 * link.OneWaySeconds);
            _partner = new Headset(Strip);
        }

        /// <summary>Each body's place in its own Strip frame: the two chests about 1.6 m apart on the bout Strip.</summary>
        public static Vector3 HostBodyOffset => new Vector3(0.0f, 0.0f, 2.2f);

        public FencerBody HostBody { get; }

        public FencerBody PartnerBodyInItsFrame { get; }

        public FencerBody PartnerBodyInWorld { get; }

        /// <summary>The Host's own solve (A the Host, B the Partner), and the Partner's (A the Partner), in their own frames.</summary>
        public PartnerBladeContact HostView => _host.Contact;

        public PartnerBladeContact PartnerView => _partner.Contact;

        public double HostNow { get; private set; } = 1000.0;

        public double PartnerNow => HostNow - PartnerBehindHostSeconds;

        public BoutJudge Judge { get; } = new BoutJudge();

        public ParryReplay Replay { get; } = new ParryReplay();

        public List<BoutLight> Lights { get; } = new List<BoutLight>();

        public List<string> Log { get; } = new List<string>();

        public List<(BoutSide Attacker, ClaimVerdict Verdict)> Claims { get; } = new List<(BoutSide, ClaimVerdict)>();

        /// <summary>The worst drawn clearance between the two blades in the Host's view and the Partner's (negative: through).</summary>
        public float WorstHostGapMeters => _host.WorstGapMeters;

        public float WorstPartnerGapMeters => _partner.WorstGapMeters;

        /// <summary>
        /// Each view's own solve as its contact gives it (PER-147): turns, drawn points' moves beyond their hands, and
        /// reversals; and the deepest the hands pressed while the side was held.
        /// </summary>
        public GiveTrace HostTrace => _host.Trace;

        public GiveTrace PartnerTrace => _partner.Trace;

        public float DeepestHostPressMeters => _host.DeepestPressMeters;

        public float DeepestPartnerPressMeters => _partner.DeepestPressMeters;

        /// <summary>Frames both views had the blades pressing, and of those, frames they had this Fencer's blade on the same side.</summary>
        public int BothPressingFrames { get; private set; }

        public int SameSideFrames { get; private set; }

        /// <summary>Frames either view had the blades pressing.</summary>
        public int AnyPressingFrames { get; private set; }

        /// <summary>The Host's local blade work so far (its one solve and the two drawn blades), ms; the replay judge's apart.</summary>
        public double HostBladeMilliseconds => _host.BladeWatch.Elapsed.TotalMilliseconds;

        /// <summary>The Partner's blade work so far (its one solve and the two drawn blades), ms.</summary>
        public double PartnerBladeMilliseconds => _partner.BladeWatch.Elapsed.TotalMilliseconds;

        /// <summary>The replay judge's part of the Host's frames.</summary>
        public Stopwatch ReplayWatch { get; } = new Stopwatch();

        public double MostHostFrameMilliseconds { get; private set; }

        public double MostPartnerFrameMilliseconds { get; private set; }

        public int Frames { get; private set; }

        /// <summary>The time each script started at (Host's clock).</summary>
        public double StartSeconds { get; } = 1000.0;

        /// <summary>
        /// The world chest point the scripts aim at: the middle of the Host's torso capsule (index 1), and the same on the
        /// Partner.
        /// </summary>
        public static Vector3 GetTorsoCenter(FencerBody body)
        {
            var capsules = new List<Capsule>();
            FencerBodyShape.GetCapsules(body, HeightMeters, capsules);
            return (capsules[1].Start + capsules[1].End) * 0.5f;
        }

        public static FencerBody BodyAt(Vector3 offset)
        {
            var layout = DummyLayout.Create(HeightMeters, Hand.Right);
            var joints = new Vector3?[FencerBody.JointCount];
            foreach (SkeletonJoint joint in Enum.GetValues(typeof(SkeletonJoint)))
            {
                joints[(int)joint] = DummySkeleton.GetJointPosition(layout, joint) + offset;
            }

            joints[FencerBody.BackHandEndIndex] = joints[(int)SkeletonJoint.BackHand] + new Vector3(0.0f, 0.0f, 0.08f);

            // Only the torso and head: a sword arm posed for the Dummy would stand in the scripted blades' way.
            joints[(int)SkeletonJoint.SwordShoulder] = null;
            joints[(int)SkeletonJoint.SwordElbow] = null;
            joints[(int)SkeletonJoint.SwordWrist] = null;
            joints[(int)SkeletonJoint.SwordHandEnd] = null;
            return new FencerBody(joints, Quaternion.Identity, BodyTrackingFidelity.High);
        }

        /// <summary>The controller pose that holds a blade as <paramref name="aim"/> says, with the scene's grip.</summary>
        public TrackedPose ToController(BladeAim aim)
        {
            var mount = BladePose.GetMountInController(_blade, Hand.Right);
            var bladeRotation = Look(aim.Direction, aim.Up);
            var rotation = Quaternion.Normalize(bladeRotation * Quaternion.Inverse(mount.Rotation));
            var position = aim.Guard - Vector3.Transform(mount.Position, rotation);
            return new TrackedPose(position, rotation);
        }

        /// <summary>The rotation that turns +z to <paramref name="forward"/>, keeping <paramref name="up"/> up.</summary>
        public static Quaternion Look(Vector3 forward, Vector3 up)
        {
            var z = Vector3.Normalize(forward);
            var x = Vector3.Normalize(Vector3.Cross(up, z));
            var y = Vector3.Cross(z, x);
            var matrix = new Matrix4x4(x.X, x.Y, x.Z, 0.0f, y.X, y.Y, y.Z, 0.0f, z.X, z.Y, z.Z, 0.0f, 0.0f, 0.0f, 0.0f, 1.0f);
            return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(matrix));
        }

        public void RunFor(double seconds)
        {
            var end = HostNow + seconds;
            while (HostNow < end - 1e-9)
            {
                Step();
            }
        }

        public void Step()
        {
            HostNow += FrameSeconds;
            Frames++;
            var t = HostNow - StartSeconds;
            var hostController = ToController(_hostScript(t));
            var partnerControllerWorld = ToController(_partnerScript(t));
            var partnerController = TurnToPartner(partnerControllerWorld);

            Deliver(_toHost, _host, HostNow);
            Deliver(_toPartner, _partner, PartnerNow);
            DeliverClaims();

            // Each headset's frame, as the app runs it: the link received, the opponent sampled, both blades solved, drawn.
            // Only the blades' work is timed (the solve and the two drawn blades, and the Host's replay judge).
            var hostBefore = _host.BladeWatch.Elapsed.TotalMilliseconds + ReplayWatch.Elapsed.TotalMilliseconds;
            _host.Frame(HostNow, hostController, _blade);
            ReplayWatch.Start();
            Replay.AddHostFrame(HostNow, hostController, _blade, Hand.Right, HostBody, HeightMeters);
            Replay.Advance(HostNow, -PartnerBehindHostSeconds + _link.ClockErrorSeconds, _host.Link);
            ReplayWatch.Stop();
            MostHostFrameMilliseconds = Math.Max(MostHostFrameMilliseconds, HostBladeMilliseconds + ReplayWatch.Elapsed.TotalMilliseconds - hostBefore);

            var partnerBefore = PartnerBladeMilliseconds;
            _partner.Frame(PartnerNow, partnerController, _blade);
            MostPartnerFrameMilliseconds = Math.Max(MostPartnerFrameMilliseconds, PartnerBladeMilliseconds - partnerBefore);

            // Touches: the Host's go straight into its judge, the Partner's on the Host's clock over the link.
            foreach (var pressed in _host.TakeTouches())
            {
                var claim = new TouchClaim(Claims.Count + 1, BoutSide.Host, pressed.Zone, pressed.Seconds, false);
                Claims.Add((BoutSide.Host, Judge.Submit(claim, HostNow)));
            }

            foreach (var pressed in _partner.TakeTouches())
            {
                var claim = new TouchClaim(++_partnerClaims, BoutSide.Partner, pressed.Zone, pressed.Seconds + PartnerBehindHostSeconds - _link.ClockErrorSeconds, false);
                _claimsToHost.Add((HostNow + _link.OneWaySeconds, claim));
            }

            var light = Judge.Step(HostNow, Replay);
            if (light.HasValue)
            {
                Lights.Add(light.Value);
            }

            Log.AddRange(Replay.TakeLog());
            CompareViews();
            SendStreams(hostController, partnerController);
        }

        /// <summary>A pose in the Host's (world) Strip frame as the Partner holds it in theirs: the bout Strip's half turn.</summary>
        private TrackedPose TurnToPartner(TrackedPose world)
        {
            return _bout.ToMyStripFrame(world);
        }

        private void CompareViews()
        {
            var host = _host.Contact.Contact;
            var partner = _partner.Contact.Contact;
            var isHostPressing = _host.Contact.HasStepped && host.IsInContact;
            var isPartnerPressing = _partner.Contact.HasStepped && partner.IsInContact;
            if (isHostPressing || isPartnerPressing)
            {
                AnyPressingFrames++;
            }

            if (!isHostPressing || !isPartnerPressing || !host.Side.HasValue || !partner.Side.HasValue)
            {
                return;
            }

            BothPressingFrames++;

            // The Host's side runs from the Partner's blade to the Host's; the Partner's (turned to the world) the other way.
            var partnerSideInWorld = _bout.DirectionToMyStripFrame(partner.Side.Value);
            if (Vector3.Dot(host.Side.Value, -partnerSideInWorld) > 0.0f)
            {
                SameSideFrames++;
            }
        }

        private void SendStreams(TrackedPose hostController, TrackedPose partnerController)
        {
            if (Frames == 1 || Frames % 72 == 0)
            {
                _toPartner.Send(PartnerMessages.EncodeInfo(new FencerInfo(Strip, HeightMeters, Hand.Right)), HostNow);
                _toHost.Send(PartnerMessages.EncodeInfo(new FencerInfo(Strip, HeightMeters, Hand.Right)), HostNow);
            }

            _toPartner.Send(PartnerMessages.EncodeHand(_host.MakeHand(HostNow, hostController, _blade)), HostNow, mayBeLost: true);
            _toHost.Send(PartnerMessages.EncodeHand(_partner.MakeHand(PartnerNow, partnerController, _blade)), HostNow, mayBeLost: true);
            if (_bodySchedule.TakeIfDue(HostNow))
            {
                _toPartner.Send(PartnerMessages.EncodeBody(new BodySample(HostNow, HostBody)), HostNow, mayBeLost: true);
                _toHost.Send(PartnerMessages.EncodeBody(new BodySample(PartnerNow, PartnerBodyInItsFrame)), HostNow, mayBeLost: true);
            }
        }

        /// <summary>The link runs on the Host's clock; each headset receives on its own (<paramref name="localNow"/>).</summary>
        private void Deliver(MirrorLoopback link, Headset headset, double localNow)
        {
            foreach (var message in link.TakeDue(HostNow))
            {
                headset.Link.Receive(message, localNow);
            }
        }

        private void DeliverClaims()
        {
            for (var index = 0; index < _claimsToHost.Count; index++)
            {
                if (_claimsToHost[index].ArriveHostSeconds > HostNow)
                {
                    continue;
                }

                Claims.Add((BoutSide.Partner, Judge.Submit(_claimsToHost[index].Claim, HostNow)));
                _claimsToHost.RemoveAt(index);
                index--;
            }
        }

        /// <summary>One headset: its link from the other, its local solve and drawn blades, and its Touch sensor.</summary>
        private sealed class Headset
        {
            private readonly DrawnBlade _myDrawn = new DrawnBlade();
            private readonly OpponentTouchSensor _sensor = new OpponentTouchSensor();
            private readonly HandMotion _motion = new HandMotion();
            private readonly List<BladeObstacle> _floor = new List<BladeObstacle> { BladeObstacle.Floor };
            private readonly List<(TargetZone Zone, double Seconds)> _touches = new List<(TargetZone, double)>();

            public Headset(StripExtent strip)
            {
                Link.SetMyStrip(strip);
            }

            public OpponentLink Link { get; } = new OpponentLink();

            public PartnerBladeContact Contact { get; } = new PartnerBladeContact();

            public float WorstGapMeters { get; private set; } = float.MaxValue;

            public GiveTrace Trace { get; } = new GiveTrace();

            public float DeepestPressMeters { get; private set; }

            public Stopwatch BladeWatch { get; } = new Stopwatch();

            public void Frame(double now, TrackedPose controller, BladeSettings blade)
            {
                var mine = HeldBlade.FromController(controller, blade, Hand.Right);
                var view = Link.Sample(now);
                if (view == null || !view.Controller.HasValue || !view.Grip.HasValue)
                {
                    Contact.Reset();
                    Trace.Break();
                    return;
                }

                var partner = HeldBlade.FromController(view.Controller.Value, view.Grip.Value.ToBladeSettings(), view.Grip.Value.Hand);
                BladeWatch.Start();
                Contact.Step(mine, partner, _floor, (float)FrameSeconds);
                if (Contact.Contact.IsNear)
                {
                    _myDrawn.Flex.Reset();
                }

                _myDrawn.Step(Contact.MyGive.Chord, _floor, mine.GripUp, (float)FrameSeconds, Contact.MyGive.Droop);
                BladeWatch.Stop();
                var solver = Contact.Contact;
                Trace.Add(now, mine.Chord, Contact.MyGive, partner.Chord, solver.GiveB, solver.IsInContact ? solver.PressMeters : 0.0f);
                DeepestPressMeters = Math.Max(DeepestPressMeters, solver.IsInContact ? solver.PressMeters : 0.0f);
                if (_myDrawn.HasShape && Contact.PartnerDrawn.HasShape)
                {
                    WorstGapMeters = Math.Min(WorstGapMeters, BladeContactProbe.FindDrawnGap(_myDrawn, Contact.PartnerDrawn));
                }

                // This Fencer's Touches on the opponent as drawn here, on the blade as the contact leaves it.
                var body = !view.IsBodyStale ? view.Body : null;
                foreach (var bladeEvent in _sensor.Step(now, Contact.MyGive.Chord, body, view.Info.HeightMeters, Rules))
                {
                    if (bladeEvent.Type == BladeEventType.Touch)
                    {
                        _touches.Add((bladeEvent.Zone, bladeEvent.TimeSeconds));
                    }
                }
            }

            public HandSample MakeHand(double now, TrackedPose controller, BladeSettings blade)
            {
                _motion.Add(now, controller);
                var solver = Contact.Contact;
                BladeContactReport? report = Contact.HasStepped
                    ? new BladeContactReport(solver.IsInContact, solver.PressMeters, solver.Side ?? Vector3.Zero, false, false)
                    : (BladeContactReport?)null;
                return new HandSample(now, controller, _motion.Velocity, _motion.AngularVelocity, Hand.Right, blade.Mount,
                    GripGeometry.GetActiveAngles(blade), blade.SetSagittaMeters, blade.Flex, report);
            }

            public IReadOnlyList<(TargetZone Zone, double Seconds)> TakeTouches()
            {
                var taken = _touches.ToArray();
                _touches.Clear();
                return taken;
            }
        }
    }
}
