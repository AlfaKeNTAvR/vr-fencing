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
            session.OnPartnerConnected();
            Assert.That(session.AreSoloDrillsAllowed, Is.False);

            session.BeginLeave();
            Assert.That(session.IsDummyHidden, Is.True, "Still in the session while leaving.");

            session.OnEnded(string.Empty);
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
            session.OnPartnerConnected();
            Assert.That(session.Phase, Is.EqualTo(PartnerPhase.Connected));

            session.OnPartnerGone("Your partner left");

            Assert.That(session.Phase, Is.EqualTo(PartnerPhase.WaitingForPartner));
            Assert.That(session.Code, Is.EqualTo("FGR78N"));
            Assert.That(session.Message, Is.EqualTo("Your partner left"));
        }

        [Test]
        public void AFailedJoinEndsBackOnIdleWithTheReason()
        {
            var session = new PartnerSession();
            session.BeginJoin("FGR78N");

            session.OnEnded("Different app versions: host 1.1, partner 1.0");

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
            session.OnPartnerConnected();
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
        public void TheHomeButtonNamesTheState()
        {
            var session = new PartnerSession();
            Assert.That(PartnerPageText.FormatHomeButton(session), Is.EqualTo("Partner"));

            session.BeginHost();
            Assert.That(PartnerPageText.FormatHomeButton(session), Is.EqualTo("Partner: ..."));

            session.OnHostReady("FGR78N");
            session.OnPartnerConnected();
            Assert.That(PartnerPageText.FormatHomeButton(session), Is.EqualTo("Partner: on"));
        }
    }
}
