namespace VrFencing.Settings
{
    /// <summary>
    /// Converts between Unity's vector types and the System.Numerics ones the Core assemblies use.
    /// Core values stay in Unity's frame (x right, y up, z forward), so the conversion is a straight copy.
    /// </summary>
    public static class PoseConversions
    {
        public static System.Numerics.Vector3 ToNumerics(this UnityEngine.Vector3 vector)
        {
            return new System.Numerics.Vector3(vector.x, vector.y, vector.z);
        }

        public static UnityEngine.Vector3 ToUnity(this System.Numerics.Vector3 vector)
        {
            return new UnityEngine.Vector3(vector.X, vector.Y, vector.Z);
        }

        public static System.Numerics.Quaternion ToNumerics(this UnityEngine.Quaternion rotation)
        {
            return new System.Numerics.Quaternion(rotation.x, rotation.y, rotation.z, rotation.w);
        }

        public static UnityEngine.Quaternion ToUnity(this System.Numerics.Quaternion rotation)
        {
            return new UnityEngine.Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W);
        }

        public static TrackedPose ToTrackedPose(this UnityEngine.Pose pose)
        {
            return new TrackedPose(pose.position.ToNumerics(), pose.rotation.ToNumerics());
        }

        public static UnityEngine.Pose ToUnity(this TrackedPose pose)
        {
            return new UnityEngine.Pose(pose.Position.ToUnity(), pose.Rotation.ToUnity());
        }
    }
}
