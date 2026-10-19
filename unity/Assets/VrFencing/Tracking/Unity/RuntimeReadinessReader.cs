namespace VrFencing.Tracking
{
    /// <summary>Reads the XR runtime's focus and headset state for the Core <see cref="RuntimeReadiness"/>.</summary>
    public static class RuntimeReadinessReader
    {
        /// <summary>
        /// OVRManager's per-frame focus and headset values; user presence is the same proximity reading OVRManager turns
        /// into its HMDMounted / HMDUnmounted events.
        /// </summary>
        public static RuntimeReadiness Read()
        {
            return new RuntimeReadiness(
                OVRManager.hasVrFocus,
                OVRManager.hasInputFocus,
                OVRManager.isHmdPresent,
                OVRPlugin.userPresent);
        }
    }
}
