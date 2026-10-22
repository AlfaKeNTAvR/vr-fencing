namespace VrFencing.Partner
{
    /// <summary>
    /// Which scenes Netcode's scene sync may offer or load (PER-143). The Partner session spawns nothing over the network
    /// (the streams and the bout are named messages), so each app keeps its own scene: a scene is synced only when it is
    /// in the build and is already this app's active scene.
    /// <list type="bullet">
    /// <item>Two headsets: both are in the Passthrough scene, so it is synced as before (nothing is reloaded).</item>
    /// <item>The PC Editor joining from a Fencer replay scene: the Host's Passthrough scene is not loaded, so the replay
    /// stays and is what goes out.</item>
    /// <item>The PC Editor hosting from a Fencer replay scene: that untitled scene is not in the build, so it is not
    /// offered (Netcode would throw on it).</item>
    /// </list>
    /// </summary>
    public static class PartnerScenes
    {
        /// <param name="buildIndex">The scene's index in the build's scene list; negative when it is not in the build.</param>
        /// <param name="sceneName">The scene to be offered (on the Host) or loaded (on the joining side).</param>
        /// <param name="activeSceneName">This app's active scene.</param>
        public static bool ShouldSyncScene(int buildIndex, string sceneName, string activeSceneName)
        {
            return buildIndex >= 0 && !string.IsNullOrEmpty(sceneName) && sceneName == activeSceneName;
        }
    }
}
