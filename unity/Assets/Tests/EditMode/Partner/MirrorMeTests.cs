// "Mirror me" (PER-140): this Fencer's own frame goes through the partner messages on a pretend link with a delay and
// comes back as the opponent, placed by the partner path's own turn about the bout centre. The page offers it when no
// session is on, cycles the delay while mirroring, and Leave ends it.
using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Dummy;
using VrFencing.Partner;
using VrFencing.Session;
using VrFencing.Settings;

namespace VrFencing.Tests.Partner
{
    public class MirrorMeTests
    {
        private const float Tolerance = 1e-3f;
        private const double FrameSeconds = 1.0 / 72.0;

        private static readonly StripExtent MyStrip = new StripExtent(8.0f, -1.0f);

        [Test]
        public void TheDelayStartsAtATypicalLinkAndCyclesThroughTheOptions()
        {
            Assert.That(MirrorDelay.DefaultSeconds, Is.EqualTo(0.1));
            Assert.That(MirrorDelay.Options, Is.EqualTo(new[] { 0.0, 0.1, 0.2, 0.5 }));

            var seen = new List<double>();
            var delay = MirrorDelay.DefaultSeconds;
            for (var press = 0; press < 4; press++)
            {
                delay = MirrorDelay.Next(delay);
                seen.Add(delay);
            }

            Assert.That(seen, Is.EqualTo(new[] { 0.2, 0.5, 0.0, 0.1 }));
            Assert.That(MirrorDelay.Next(0.3), Is.EqualTo(0.0), "A value not offered goes back to the first option.");
            Assert.That(MirrorDelay.FormatLabel(0.5), Is.EqualTo("Delay 0.5 s"));
        }

        [Test]
        public void AMessageComesBackOnlyAfterTheDelay()
        {
            var loopback = new MirrorLoopback(0.2);
            loopback.Send(new byte[] { 1 }, 1.0);

            Assert.That(loopback.TakeDue(1.0), Is.Empty);
            Assert.That(loopback.TakeDue(1.19), Is.Empty);
            Assert.That(loopback.TakeDue(1.2), Is.EqualTo(new[] { new byte[] { 1 } }));
            Assert.That(loopback.TakeDue(5.0), Is.Empty, "Each message comes back once.");
        }

        [Test]
        public void MessagesComeBackInTheOrderTheyWereSent()
        {
            var loopback = new MirrorLoopback(0.1);
            loopback.Send(new byte[] { 1 }, 1.0);
            loopback.Send(new byte[] { 2 }, 1.0);
            loopback.Send(new byte[] { 3 }, 1.05);

            var due = loopback.TakeDue(1.12);

            Assert.That(due, Is.EqualTo(new[] { new byte[] { 1 }, new byte[] { 2 } }));
            Assert.That(loopback.InFlightCount, Is.EqualTo(1));
            Assert.That(loopback.TakeDue(1.16), Is.EqualTo(new[] { new byte[] { 3 } }));
        }

        [Test]
        public void WithNoDelayAMessageComesBackTheSameFrame()
        {
            var loopback = new MirrorLoopback(0.0);
            loopback.Send(new byte[] { 7 }, 2.0);

            Assert.That(loopback.TakeDue(2.0), Is.EqualTo(new[] { new byte[] { 7 } }));
        }

        [Test]
        public void OnlyTheBytesTravelNotTheSendersArray()
        {
            var loopback = new MirrorLoopback(0.1);
            var message = new byte[] { 1, 2 };
            loopback.Send(message, 0.0);
            message[0] = 9;

            Assert.That(loopback.TakeDue(0.1)[0], Is.EqualTo(new byte[] { 1, 2 }));
        }

        [Test]
        public void ChangingTheDelayDropsWhatIsOnItsWay()
        {
            var loopback = new MirrorLoopback(0.5);
            loopback.Send(new byte[] { 1 }, 0.0);

            Assert.That(loopback.SetDelay(0.5), Is.False, "The same delay changes nothing.");
            Assert.That(loopback.InFlightCount, Is.EqualTo(1));
            Assert.That(loopback.SetDelay(0.0), Is.True);
            Assert.That(loopback.InFlightCount, Is.EqualTo(0));
            Assert.That(loopback.DelaySeconds, Is.EqualTo(0.0));
        }

        [Test]
        public void ANegativeDelayIsRefused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MirrorLoopback(-0.1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MirrorLoopback(0.1).SetDelay(double.NaN));
        }

        [Test]
        public void MirrorMeIsOfferedWhenNoSessionIsOn()
        {
            var session = new PartnerSession();
            Assert.That(session.CanMirror, Is.True);

            session.BeginHost();
            Assert.That(session.CanMirror, Is.False);
            Assert.That(session.BeginMirror(), Is.False);
            Assert.That(session.Phase, Is.EqualTo(PartnerPhase.Starting));
        }

        [Test]
        public void MirroringIsLikeAPartnerSessionWithoutThePartner()
        {
            var session = new PartnerSession();

            Assert.That(session.BeginMirror(), Is.True);

            Assert.That(session.Phase, Is.EqualTo(PartnerPhase.Mirroring));
            Assert.That(session.IsMirroring, Is.True);
            Assert.That(session.Role, Is.EqualTo(PartnerRole.None));
            Assert.That(session.IsDummyHidden, Is.True);
            Assert.That(session.AreSoloDrillsAllowed, Is.False);
            Assert.That(session.CanHost, Is.False);
            Assert.That(session.CanJoin, Is.False);
            Assert.That(session.CanMirror, Is.False);
            Assert.That(session.CanSwitchMode, Is.False);
            Assert.That(session.CanLeave, Is.True);
            Assert.That(session.IsBusy, Is.False);
            Assert.That(PartnerPageText.FormatHeadline(session), Is.EqualTo("Mirror"));
            Assert.That(PartnerPageText.FormatStatus(session), Is.EqualTo("You as your partner sees you, 0.1 s late. Not judged"));
            Assert.That(PartnerPageText.FormatHomeButton(session), Is.EqualTo("Partner: mirror"));
        }

        [Test]
        public void LeaveEndsTheMirrorAndBringsTheDummyAndDrillsBack()
        {
            var session = new PartnerSession();
            session.BeginMirror();
            session.CycleMirrorDelay();

            Assert.That(session.BeginLeave(), Is.True);
            session.OnEnded(PartnerEnd.Left, string.Empty);

            Assert.That(session.Phase, Is.EqualTo(PartnerPhase.Idle));
            Assert.That(session.IsMirroring, Is.False);
            Assert.That(session.IsDummyHidden, Is.False);
            Assert.That(session.AreSoloDrillsAllowed, Is.True);
            Assert.That(session.IsBackToSoloMenu, Is.False);
            Assert.That(session.Message, Is.Empty);
            Assert.That(session.CanMirror, Is.True);
            Assert.That(session.MirrorDelaySeconds, Is.EqualTo(0.2), "The picked delay is kept for the next mirror.");
        }

        [Test]
        public void TheDelayCyclesOnlyWhileMirroring()
        {
            var session = new PartnerSession();
            Assert.That(session.CycleMirrorDelay(), Is.False);
            Assert.That(session.MirrorDelaySeconds, Is.EqualTo(0.1));

            session.BeginMirror();
            Assert.That(session.CycleMirrorDelay(), Is.True);
            Assert.That(session.MirrorDelaySeconds, Is.EqualTo(0.2));
            Assert.That(PartnerPageText.FormatStatus(session), Does.Contain("0.2 s late"));
        }

        [Test]
        public void AMirrorSurvivesASleep()
        {
            var session = new PartnerSession();
            session.BeginMirror();

            session.OnPaused(100.0);

            Assert.That(session.OnResumed(200.0), Is.False, "No network went with the sleep: the mirror carries on.");
            Assert.That(session.IsMirroring, Is.True);
        }

        [Test]
        public void TheMirroredFencerIsPlacedByThePartnerPathsTurn()
        {
            var link = new OpponentLink();
            link.SetMyStrip(MyStrip);
            var loopback = new MirrorLoopback(0.1);
            var hand = new Vector3(0.25f, 1.1f, 0.4f);
            var body = PartnerTestData.Body(BodyTrackingFidelity.High, Vector3.Zero);
            var last = RunFrames(link, loopback, 0.0, 72, _ => hand, body);

            var view = link.Sample(last);

            // The partner's own headset applies this turn to what it receives; the mirror must match it exactly.
            var partnerPath = BoutStrip.Create(MyStrip, MyStrip);
            var sent = PartnerTestData.Hand(0.0, hand, Vector3.Zero).Controller;
            var expected = partnerPath.ToMyStripFrame(sent);
            Assert.That(Vector3.Distance(view.Controller.Value.Position, expected.Position), Is.LessThan(Tolerance));
            Assert.That(Math.Abs(Quaternion.Dot(view.Controller.Value.Rotation, expected.Rotation)), Is.EqualTo(1.0f).Within(Tolerance));

            // A half turn about the vertical through the centre (rear ends at -1 and 7, centre 3): never a reflection.
            Assert.That(view.Controller.Value.Position.X, Is.EqualTo(-hand.X).Within(Tolerance));
            Assert.That(view.Controller.Value.Position.Z, Is.EqualTo(6.0f - hand.Z).Within(Tolerance));
            var halfTurn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)Math.PI);
            Assert.That(Math.Abs(Quaternion.Dot(view.Controller.Value.Rotation, Quaternion.Normalize(halfTurn * sent.Rotation))), Is.EqualTo(1.0f).Within(Tolerance));

            // The body stands on the far side facing back, the sword hand still the right one.
            var hip = body.Get(SkeletonJoint.HipCenter).Value;
            Assert.That(view.Body.Get(SkeletonJoint.HipCenter).Value.Z, Is.EqualTo(6.0f - hip.Z).Within(Tolerance));
            Assert.That(view.Body.Get(SkeletonJoint.SwordShoulder).Value.X, Is.EqualTo(-body.Get(SkeletonJoint.SwordShoulder).Value.X).Within(Tolerance));
            Assert.That(view.Info.Hand, Is.EqualTo(Hand.Right));
            Assert.That(view.Grip.Value.Mount, Is.EqualTo(BladeMount.French));
            Assert.That(link.RejectedMessages, Is.EqualTo(0));
        }

        [Test]
        public void TheMirroredHandMovesTheDelayLate()
        {
            // The hand steps 30 cm along the Strip at 1 s, as sent; drawn mirrored, the far-side z drops by 30 cm.
            var before = new Vector3(0.25f, 1.1f, 0.4f);
            var after = before + new Vector3(0.0f, 0.0f, 0.3f);
            Func<double, Vector3> path = time => time < 1.0 ? before : after;
            var body = PartnerTestData.Body(BodyTrackingFidelity.High, Vector3.Zero);

            var delayed = MirroredHandZ(0.2, path, body, 1.15);
            var delayedLater = MirroredHandZ(0.2, path, body, 1.35);
            var immediate = MirroredHandZ(0.0, path, body, 1.15);

            Assert.That(delayed, Is.EqualTo(6.0f - before.Z).Within(Tolerance), "0.15 s after the step, a 0.2 s link still shows the old place.");
            Assert.That(delayedLater, Is.EqualTo(6.0f - after.Z).Within(Tolerance));
            Assert.That(immediate, Is.EqualTo(6.0f - after.Z).Within(Tolerance), "With no delay only the hand's 0.04 s buffer is left.");
        }

        [Test]
        public void AFreshLinkAfterADelayChangeDrawsAgainOnceTheNewDelayHasPassed()
        {
            var link = new OpponentLink();
            link.SetMyStrip(MyStrip);
            var loopback = new MirrorLoopback(0.5);
            var body = PartnerTestData.Body(BodyTrackingFidelity.High, Vector3.Zero);
            var hand = new Vector3(0.25f, 1.1f, 0.4f);
            var last = RunFrames(link, loopback, 0.0, 72, _ => hand, body);
            Assert.That(link.Sample(last), Is.Not.Null);

            // As the Unity layer does on a new delay: the loopback drops what is on its way and the receiver starts over.
            Assert.That(loopback.SetDelay(0.1), Is.True);
            link.Clear();
            var restart = last + FrameSeconds;
            Assert.That(link.Sample(restart), Is.Null);

            last = RunFrames(link, loopback, restart, 10, _ => hand, body);

            var view = link.Sample(last);
            Assert.That(view, Is.Not.Null);
            Assert.That(view.Controller.Value.Position.Z, Is.EqualTo(6.0f - hand.Z).Within(Tolerance));
            Assert.That(view.IsBodyStale, Is.False);
        }

        /// <summary>The mirrored hand's z on this Fencer's Strip at <paramref name="sampleSeconds"/>, over a link of <paramref name="delaySeconds"/>.</summary>
        private static float MirroredHandZ(double delaySeconds, Func<double, Vector3> path, FencerBody body, double sampleSeconds)
        {
            var link = new OpponentLink();
            link.SetMyStrip(MyStrip);
            var loopback = new MirrorLoopback(delaySeconds);
            var frames = (int)Math.Round(sampleSeconds / FrameSeconds);
            var last = RunFrames(link, loopback, 0.0, frames + 1, path, body);
            return link.Sample(last).Controller.Value.Position.Z;
        }

        /// <summary>
        /// Runs <paramref name="frames"/> frames of 1/72 s from <paramref name="startSeconds"/> as the Unity layer does
        /// while mirroring: the info once, the hand every frame and the body every other frame, packed as partner messages
        /// into the loopback, then whatever is due back received by the link. Returns the last frame's time.
        /// </summary>
        private static double RunFrames(OpponentLink link, MirrorLoopback loopback, double startSeconds, int frames, Func<double, Vector3> handPath, FencerBody body)
        {
            var now = startSeconds;
            for (var frame = 0; frame < frames; frame++)
            {
                now = startSeconds + (frame * FrameSeconds);
                if (frame == 0)
                {
                    loopback.Send(PartnerMessages.EncodeInfo(new FencerInfo(MyStrip, PartnerTestData.HeightMeters, Hand.Right)), now);
                }

                loopback.Send(PartnerMessages.EncodeHand(PartnerTestData.Hand(now, handPath(now), Vector3.Zero)), now);
                if (frame % 2 == 0)
                {
                    loopback.Send(PartnerMessages.EncodeBody(new BodySample(now, body)), now);
                }

                foreach (var message in loopback.TakeDue(now))
                {
                    Assert.That(link.Receive(message, now), Is.True);
                }
            }

            return now;
        }
    }
}
