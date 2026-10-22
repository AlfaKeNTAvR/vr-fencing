// Netcode's scene sync never replaces an app's own scene (PER-143): the PC Editor's Fencer replay survives a join.
using NUnit.Framework;
using VrFencing.Partner;

namespace VrFencing.Tests.Partner
{
    public class PartnerScenesTests
    {
        private const string Passthrough = "Passthrough";

        [Test]
        public void TwoHeadsetsBothInPassthroughKeepItSynced()
        {
            Assert.That(PartnerScenes.ShouldSyncScene(0, Passthrough, Passthrough), Is.True);
        }

        [Test]
        public void TheEditorJoiningFromTheReplaySceneDoesNotLoadTheHostsScene()
        {
            // The replay scene is a new untitled scene: its name is empty.
            Assert.That(PartnerScenes.ShouldSyncScene(0, Passthrough, string.Empty), Is.False);
        }

        [Test]
        public void TheEditorHostingFromTheReplaySceneDoesNotOfferIt()
        {
            Assert.That(PartnerScenes.ShouldSyncScene(-1, string.Empty, string.Empty), Is.False);
        }

        [Test]
        public void ASceneOutsideTheBuildIsNeverSyncedEvenWhenActive()
        {
            Assert.That(PartnerScenes.ShouldSyncScene(-1, "Scratch", "Scratch"), Is.False);
        }

        [Test]
        public void AnotherBuildSceneIsNotLoadedOverTheActiveOne()
        {
            Assert.That(PartnerScenes.ShouldSyncScene(1, "Other", Passthrough), Is.False);
        }
    }
}
