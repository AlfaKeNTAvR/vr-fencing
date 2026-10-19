// Online checks of the Partner connection (PER-80) against the real Unity Cloud project: anonymous sign-in, a
// 2-player Relay session with a join code, leaving it, and a failed join. They need the internet and the linked
// project, so they are Play-mode tests outside the Edit-mode merge gate. Run them with:
//   Unity -batchmode -nographics -projectPath unity -runTests -testPlatform PlayMode -testCategory Online
//         -testResults Builds/test-results/playmode-online.xml
// A second player cannot join from the same process (one signed-in player), so host-and-join is tested between the
// headset and the PC Editor (see the PER-80 steps).
using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VrFencing.Partner;

namespace VrFencing.Tests.Partner
{
    [Category("Online")]
    public class PartnerOnlineTests
    {
        private const float NetworkTimeoutSeconds = 45.0f;

        private PartnerSessionBehaviour _partner;

        [SetUp]
        public void CreatePartner()
        {
            _partner = PartnerSessionBehaviour.FindOrCreate();
        }

        [UnityTearDown]
        public IEnumerator LeaveAndDestroy()
        {
            if (_partner != null && _partner.State.CanLeave)
            {
                yield return WaitFor(_partner.LeaveAsync());
            }

            if (_partner != null)
            {
                Object.Destroy(_partner.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator HostSignsInAndCreatesARelaySessionWithAJoinCode()
        {
            yield return WaitFor(_partner.HostAsync());

            Assert.That(_partner.State.Phase, Is.EqualTo(PartnerPhase.WaitingForPartner), _partner.State.Message);
            Assert.That(JoinCode.IsValid(_partner.State.Code), Is.True, $"Code '{_partner.State.Code}' is not one the keypad can type.");
            Debug.Log($"[PartnerOnlineTests] Session up with code {_partner.State.Code}.");

            yield return WaitFor(_partner.LeaveAsync());

            Assert.That(_partner.State.Phase, Is.EqualTo(PartnerPhase.Idle));
            Assert.That(_partner.State.Message, Is.Empty);
        }

        [UnityTest]
        public IEnumerator JoiningACodeWithNoSessionEndsOnThePageWithTheReason()
        {
            // A well-formed code no session is likely to have.
            yield return WaitFor(_partner.JoinAsync("BBBBBB"));

            Assert.That(_partner.State.Phase, Is.EqualTo(PartnerPhase.Idle));
            Assert.That(_partner.State.Message, Does.StartWith("Could not join BBB BBB"));
            Debug.Log($"[PartnerOnlineTests] Join refused as expected: {_partner.State.Message}");
        }

        private static IEnumerator WaitFor(Task task)
        {
            var deadline = Time.realtimeSinceStartup + NetworkTimeoutSeconds;
            while (!task.IsCompleted)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "The network call did not finish in time.");
                yield return null;
            }

            if (task.IsFaulted)
            {
                throw task.Exception;
            }
        }
    }
}
