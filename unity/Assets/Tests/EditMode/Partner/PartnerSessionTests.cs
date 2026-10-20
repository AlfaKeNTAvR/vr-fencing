// The Partner page's state (PER-80, decided in PER-58): what is offered when, and that solo Drills and the Dummy are
// off while in a session.
using NUnit.Framework;
using VrFencing.Partner;

namespace VrFencing.Tests.Partner
{
    public class PartnerSessionTests
    {
        private static PartnerSession HostWaiting(string code = "FGR78N")
        {
            var session = new PartnerSession();
            session.BeginHost();
            session.OnHostReady(code);
            return session;
        }

        private static PartnerSession GuestConnected(PartnerMode hostMode = PartnerMode.Bout)
        {
            var session = new PartnerSession();
            session.BeginEnteringCode();
            session.BeginJoin("fgr78n");
            session.OnJoined(hostMode);
            return session;
        }

        [Test]
        public void IdleOffersHostAndJoinAndKeepsSoloDrills()
        {
            var session = new PartnerSession();

            Assert.That(session.CanHost, Is.True);
            Assert.That(session.CanJoin, Is.True);
            Assert.That(session.CanLeave, Is.False);
            Assert.That(session.CanSwitchMode, Is.False);
            Assert.That(session.AreSoloDrillsAllowed, Is.True);
            Assert.That(session.IsDummyHidden, Is.False);
        }

        [Test]
        public void HostShowsTheCodeOnceTheSessionIsUp()
        {
            var session = new PartnerSession();
            Assert.That(session.BeginHost(), Is.True);
            Assert.That(session.Phase, Is.EqualTo(PartnerPhase.Starting));
            Assert.That(session.IsBusy, Is.True);
            Assert.That(session.CanLeave, Is.False, "Leave waits until the session is up.");

            session.OnHostReady("FGR78N");

            Assert.That(session.Phase, Is.EqualTo(PartnerPhase.WaitingForPartner));
            Assert.That(session.Role, Is.EqualTo(PartnerRole.Host));
            Assert.That(session.Code, Is.EqualTo("FGR78N"));
            Assert.That(session.CanLeave, Is.True);
            Assert.That(session.CanSwitchMode, Is.True);
        }

        [Test]
        public void SoloDrillsAndTheDummyAreOffFromStartingUntilLeft()
        {
            var session = new PartnerSession();
            session.BeginHost();
            Assert.That(session.AreSoloDrillsAllowed, Is.False);
            Assert.That(session.IsDummyHidden, Is.True);

            session.OnHostReady("FGR78N");
            session.OnPartnerConnected(0.0);
            Assert.That(session.AreSoloDrillsAllowed, Is.False);

            session.BeginLeave();
            Assert.That(session.IsDummyHidden, Is.True, "Still in the session while leaving.");

            session.OnEnded(PartnerEnd.Left, string.Empty);
            Assert.That(session.AreSoloDrillsAllowed, Is.True);
            Assert.That(session.IsDummyHidden, Is.False);
        }

        [Test]
        public void TypingACodeIsNotYetASession()
        {
            var session = new PartnerSession();
            session.BeginEnteringCode();

            Assert.That(session.Phase, Is.EqualTo(PartnerPhase.EnteringCode));
            Assert.That(session.AreSoloDrillsAllowed, Is.True);
            Assert.That(session.CanHost, Is.False);
        }

        [Test]
        public void CancellingTheKeypadGoesBackToIdle()
        {
            var session = new PartnerSession();
            session.BeginEnteringCode();
            session.CancelEnteringCode();

            Assert.That(session.Phase, Is.EqualTo(PartnerPhase.Idle));
        }

        [Test]
        public void AnInvalidCodeStaysOnTheKeypadWithTheReason()
        {
            var session = new PartnerSession();
            session.BeginEnteringCode();

            Assert.That(session.BeginJoin("FG"), Is.False);
            Assert.That(session.Phase, Is.EqualTo(PartnerPhase.EnteringCode));
            Assert.That(session.Message, Does.Contain("6 characters"));
        }

        [Test]
        public void AValidCodeJoinsAsTheGuestAndTakesTheHostsMode()
        {
            var session = new PartnerSession();
            session.BeginEnteringCode();

            Assert.That(session.BeginJoin("fgr-78n"), Is.True);
            Assert.That(session.Phase, Is.EqualTo(PartnerPhase.Joining));
            Assert.That(session.Code, Is.EqualTo("FGR78N"));
            Assert.That(session.Role, Is.EqualTo(PartnerRole.Guest));

            session.OnJoined(PartnerMode.PairedPractice);

            Assert.That(session.Phase, Is.EqualTo(PartnerPhase.Connected));
            Assert.That(session.Mode, Is.EqualTo(PartnerMode.PairedPractice));
        }

        [Test]
        public void TheEditorCanJoinStraightFromIdle()
        {
            var session = new PartnerSession();

            Assert.That(session.BeginJoin("FGR78N"), Is.True);
            Assert.That(session.Phase, Is.EqualTo(PartnerPhase.Joining));
        }

        [Test]
        public void OnlyTheHostSwitchesTheMode()
        {
            var host = HostWaiting();
            Assert.That(host.SwitchMode(), Is.True);
            Assert.That(host.Mode, Is.EqualTo(PartnerMode.PairedPractice));
            Assert.That(host.SwitchMode(), Is.True);
            Assert.That(host.Mode, Is.EqualTo(PartnerMode.Bout));

            var guest = GuestConnected();
            Assert.That(guest.CanSwitchMode, Is.False);
            Assert.That(guest.SwitchMode(), Is.False);
            guest.OnHostModeChanged(PartnerMode.PairedPractice);
            Assert.That(guest.Mode, Is.EqualTo(PartnerMode.PairedPractice));
        }

        [Test]
        public void TheHostKeepsItsSessionWhenThePartnerGoes()
        {
            var session = HostWaiting();
            session.OnPartnerConnected(0.0);
            Assert.That(session.Phase, Is.EqualTo(PartnerPhase.Connected));

            session.OnPartnerGone("Your partner left", 0.0);

            Assert.That(session.Phase, Is.EqualTo(PartnerPhase.WaitingForPartner));
            Assert.That(session.Code, Is.EqualTo("FGR78N"));
            Assert.That(session.Message, Is.EqualTo("Your partner left"));
        }

        [Test]
        public void ADroppedPartnerHasFiveMinutesToRejoinAndCarriesTheSessionOn()
        {
            var session = HostWaiting();
            session.OnPartnerConnected(10.0);

            session.OnPartnerGone(PartnerPageText.PartnerDroppedMessage, 100.0);

            Assert.That(session.IsWaitingForRejoin, Is.True);
            Assert.That(session.Tick(100.0), Is.EqualTo(PartnerTick.CountdownChanged));
            Assert.That(session.RejoinSecondsLeft, Is.EqualTo(300));
            Assert.That(session.Tick(100.2), Is.EqualTo(PartnerTick.Nothing), "Same whole second shown.");
            Assert.That(session.Tick(399.5), Is.EqualTo(PartnerTick.CountdownChanged));
            Assert.That(session.RejoinSecondsLeft, Is.EqualTo(1));

            Assert.That(session.OnPartnerConnected(399.9), Is.True, "A rejoin within the window.");
            Assert.That(session.Phase, Is.EqualTo(PartnerPhase.Connected));
            Assert.That(session.IsWaitingForRejoin, Is.False);
            Assert.That(session.Tick(500.0), Is.EqualTo(PartnerTick.Nothing));
        }

        [Test]
        public void TheRejoinWindowEndsAfterFiveMinutes()
        {
            var session = HostWaiting();
            session.OnPartnerConnected(0.0);
            session.OnPartnerGone(PartnerPageText.PartnerDroppedMessage, 50.0);

            Assert.That(session.Tick(50.0 + PartnerSession.RejoinWindowSeconds - 0.01), Is.Not.EqualTo(PartnerTick.RejoinWindowOver));
            Assert.That(session.Tick(50.0 + PartnerSession.RejoinWindowSeconds), Is.EqualTo(PartnerTick.RejoinWindowOver));

            Assert.That(session.BeginLeave(PartnerEnd.PartnerDidNotReturn), Is.True);
            session.OnEnded(PartnerEnd.PartnerDidNotReturn, PartnerPageText.FormatEndMessage(PartnerEnd.PartnerDidNotReturn, PartnerRole.Host));

            Assert.That(session.Phase, Is.EqualTo(PartnerPhase.Idle));
            Assert.That(session.LastEnd, Is.EqualTo(PartnerEnd.PartnerDidNotReturn));
            Assert.That(session.Message, Does.Contain("did not rejoin"));
            Assert.That(session.IsBackToSoloMenu, Is.False, "The Host stays where it is.");
        }

        [Test]
        public void AFirstPartnerOrARefusedJoinStartsNoRejoinWindow()
        {
            var session = HostWaiting();
            session.OnPartnerGone("Different app versions: host 1.3, partner 1.2", 10.0);
            Assert.That(session.IsWaitingForRejoin, Is.False, "Nobody had joined yet.");
            Assert.That(session.OnPartnerConnected(20.0), Is.False, "A first join, not a rejoin.");

            session.OnPartnerGone(PartnerPageText.PartnerDroppedMessage, 30.0);
            session.OnPartnerGone("Different app versions: host 1.3, partner 1.2", 200.0);

            Assert.That(session.Tick(30.0 + PartnerSession.RejoinWindowSeconds), Is.EqualTo(PartnerTick.RejoinWindowOver), "The refusal did not restart the window.");
        }

        [Test]
        public void ALongSleepEndsTheSessionAndABlinkDoesNot()
        {
            var session = HostWaiting();
            session.OnPartnerConnected(0.0);

            session.OnPaused(1000.0);
            Assert.That(session.OnResumed(1000.0 + PartnerSession.SleepEndsSessionSeconds - 1.0), Is.False);

            session.OnPaused(2000.0);
            Assert.That(session.OnResumed(2000.0 + PartnerSession.SleepEndsSessionSeconds), Is.True);
            Assert.That(session.OnResumed(5000.0), Is.False, "One resume per pause.");

            var idle = new PartnerSession();
            idle.OnPaused(0.0);
            Assert.That(idle.OnResumed(100.0), Is.False, "No session, nothing to end.");
        }

        [Test]
        public void TheHostLeavingSendsThePartnerBackToSoloPractice()
        {
            var guest = GuestConnected();

            guest.OnEnded(PartnerSession.GetHostLossEnd(PartnerSession.HostLeftReason), PartnerPageText.FormatEndMessage(PartnerEnd.HostLeft, PartnerRole.Guest));

            Assert.That(guest.LastEnd, Is.EqualTo(PartnerEnd.HostLeft));
            Assert.That(guest.IsBackToSoloMenu, Is.True);
            Assert.That(guest.Message, Does.StartWith("Host left"));
            Assert.That(guest.RejoinCode, Is.Empty, "The Host's session is gone: nothing to rejoin.");
            Assert.That(guest.EndCount, Is.EqualTo(1));
        }

        [Test]
        public void APartnerThatLostTheHostKeepsTheCodeToRejoin()
        {
            var guest = GuestConnected();
            Assert.That(PartnerSession.GetHostLossEnd(string.Empty), Is.EqualTo(PartnerEnd.LostHost));
            Assert.That(PartnerSession.GetHostLossEnd(null), Is.EqualTo(PartnerEnd.LostHost));
            Assert.That(guest.IsRejoinExpected(string.Empty), Is.True, "The connection went without a word.");
            Assert.That(guest.IsRejoinExpected(PartnerSession.HostLeftReason), Is.False);

            Assert.That(guest.BeginLeave(PartnerEnd.LostHost), Is.True);
            Assert.That(guest.IsRejoinExpected(null), Is.True);
            guest.OnEnded(PartnerEnd.LostHost, PartnerPageText.FormatEndMessage(PartnerEnd.LostHost, PartnerRole.Guest));

            Assert.That(guest.RejoinCode, Is.EqualTo("FGR78N"));
            Assert.That(guest.IsBackToSoloMenu, Is.True);
            Assert.That(guest.IsRejoinExpected(null), Is.True);

            // A rejoin that fails keeps the code; a plain Leave after a rejoin drops it.
            guest.BeginJoin(guest.RejoinCode);
            guest.OnEnded(PartnerEnd.Failed, "Could not join FGR 78N");
            Assert.That(guest.RejoinCode, Is.EqualTo("FGR78N"));
            Assert.That(guest.IsBackToSoloMenu, Is.False);

            guest.BeginJoin(guest.RejoinCode);
            guest.OnJoined(PartnerMode.Bout);
            guest.BeginLeave();
            Assert.That(guest.IsRejoinExpected(null), Is.False, "Left on purpose.");
            guest.OnEnded(PartnerEnd.Left, string.Empty);
            Assert.That(guest.RejoinCode, Is.Empty);
        }

        [Test]
        public void TheHostExpectsItsPartnerBackOnlyWhileWaiting()
        {
            var session = HostWaiting();
            session.OnPartnerConnected(0.0);
            Assert.That(session.IsRejoinExpected(null), Is.False, "Still connected: the drop is not reported yet.");

            session.OnPartnerGone(PartnerPageText.PartnerDroppedMessage, 1.0);
            Assert.That(session.IsRejoinExpected(null), Is.True);

            session.BeginLeave();
            Assert.That(session.IsRejoinExpected(null), Is.False);
        }

        [Test]
        public void AFailedJoinEndsBackOnIdleWithTheReason()
        {
            var session = new PartnerSession();
            session.BeginJoin("FGR78N");

            session.OnEnded(PartnerEnd.Failed, "Different app versions: host 1.1, partner 1.0");

            Assert.That(session.Phase, Is.EqualTo(PartnerPhase.Idle));
            Assert.That(session.Role, Is.EqualTo(PartnerRole.None));
            Assert.That(session.Code, Is.Empty);
            Assert.That(session.Message, Does.Contain("host 1.1"));
            Assert.That(session.CanHost, Is.True);
        }

        [Test]
        public void ReportsOutOfOrderAreIgnored()
        {
            var session = new PartnerSession();
            session.OnHostReady("FGR78N");
            session.OnPartnerConnected(0.0);
            session.OnJoined(PartnerMode.PairedPractice);

            Assert.That(session.Phase, Is.EqualTo(PartnerPhase.Idle));
            Assert.That(session.BeginLeave(), Is.False);
        }

        [Test]
        public void HostAndJoinAreRefusedWhileInASession()
        {
            var session = HostWaiting();

            Assert.That(session.BeginHost(), Is.False);
            Assert.That(session.BeginEnteringCode(), Is.False);
            Assert.That(session.BeginJoin("FGR78N"), Is.False);
        }

        [Test]
        public void TheModeIsStoredAsText()
        {
            Assert.That(PartnerModeProperty.Decode(PartnerModeProperty.Encode(PartnerMode.PairedPractice)), Is.EqualTo(PartnerMode.PairedPractice));
            Assert.That(PartnerModeProperty.Decode(PartnerModeProperty.Encode(PartnerMode.Bout)), Is.EqualTo(PartnerMode.Bout));
            Assert.That(PartnerModeProperty.Decode(null), Is.EqualTo(PartnerMode.Bout));
            Assert.That(PartnerModeProperty.Decode("something new"), Is.EqualTo(PartnerMode.Bout));
        }
    }

    public class PartnerPageTextTests
    {
        [Test]
        public void TheWaitingHostSeesTheCodeInTwoHalves()
        {
            var session = new PartnerSession();
            session.BeginHost();
            session.OnHostReady("FGR78N");

            Assert.That(PartnerPageText.FormatHeadline(session), Is.EqualTo("FGR 78N"));
            Assert.That(PartnerPageText.FormatStatus(session), Does.Contain("Read this code"));
            Assert.That(PartnerPageText.FormatMode(session), Is.EqualTo("Mode: Bout"));
        }

        [Test]
        public void TheGuestSeesThatTheHostPicksTheMode()
        {
            var session = new PartnerSession();
            session.BeginJoin("FGR78N");
            session.OnJoined(PartnerMode.PairedPractice);

            Assert.That(PartnerPageText.FormatHeadline(session), Is.EqualTo("Connected"));
            Assert.That(PartnerPageText.FormatMode(session), Is.EqualTo("Mode: Paired Practice (the host picks)"));
        }

        [Test]
        public void TheWaitingHostSeesTheRejoinCountdown()
        {
            var session = new PartnerSession();
            session.BeginHost();
            session.OnHostReady("FGR78N");
            session.OnPartnerConnected(0.0);
            session.OnPartnerGone(PartnerPageText.PartnerDroppedMessage, 0.0);
            session.Tick(55.0);

            Assert.That(PartnerPageText.FormatHeadline(session), Is.EqualTo("FGR 78N"));
            Assert.That(PartnerPageText.FormatStatus(session), Is.EqualTo("Waiting for partner: rejoin with this code within 4:05"));
            Assert.That(session.Message, Is.EqualTo(PartnerPageText.PartnerDroppedMessage));
        }

        [Test]
        public void EveryEndHasAShortLineAndALeaveHasNone()
        {
            Assert.That(PartnerPageText.FormatEndMessage(PartnerEnd.Left, PartnerRole.Host), Is.Empty);
            foreach (var end in new[] { PartnerEnd.HostLeft, PartnerEnd.LostHost, PartnerEnd.Slept, PartnerEnd.PartnerDidNotReturn })
            {
                foreach (var role in new[] { PartnerRole.Host, PartnerRole.Guest })
                {
                    var line = PartnerPageText.FormatEndMessage(end, role);
                    Assert.That(line, Is.Not.Empty);
                    Assert.That(line.Length, Is.LessThanOrEqualTo(64), $"One line on the page: {line}");
                }
            }

            Assert.That(PartnerPageText.FormatEndMessage(PartnerEnd.LostHost, PartnerRole.Guest), Does.Contain("5 min"));
            Assert.That(PartnerPageText.FormatMinutes(65), Is.EqualTo("1:05"));
            Assert.That(PartnerPageText.FormatMinutes(-3), Is.EqualTo("0:00"));
        }

        [Test]
        public void TheHomeButtonNamesTheState()
        {
            var session = new PartnerSession();
            Assert.That(PartnerPageText.FormatHomeButton(session), Is.EqualTo("Partner"));

            session.BeginHost();
            Assert.That(PartnerPageText.FormatHomeButton(session), Is.EqualTo("Partner: ..."));

            session.OnHostReady("FGR78N");
            session.OnPartnerConnected(0.0);
            Assert.That(PartnerPageText.FormatHomeButton(session), Is.EqualTo("Partner: on"));
        }
    }
}
