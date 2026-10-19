// Builds the minimal passthrough scene: Meta camera rig with passthrough as the
// background, plus one small reference cube so a headset screenshot shows that
// virtual content is composited over the real room.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VrFencing.EditorTools
{
    public static class PassthroughSceneBuilder
    {
        private const string CameraRigPrefabPath = "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";
        private const string CameraRigName = "OVRCameraRig";
        private const string ReferenceCubeName = "ReferenceCube";

        private const float ReferenceCubeEdgeMeters = 0.1f;
        private static readonly Vector3 ReferenceCubePositionMeters = new(0f, 1.2f, 1.0f);

        /// <summary>Creates the scene if missing, otherwise opens it; then makes sure every required object is set up.</summary>
        public static void CreateOrUpdateScene(string scenePath)
        {
            var scene = System.IO.File.Exists(scenePath)
                ? EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single)
                : EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            RemoveDefaultCamera(scene);
            var cameraRig = FindOrCreateCameraRig(scene);
            EnablePassthrough(cameraRig);
            MakeCameraBackgroundTransparent(cameraRig);
            FindOrCreateReferenceCube(scene);

            EnsureSceneFolder(scenePath);
            EditorSceneManager.SaveScene(scene, scenePath);
        }

        private static void RemoveDefaultCamera(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == "Main Camera" && root.GetComponent<Camera>() != null)
                    Object.DestroyImmediate(root);
            }
        }

        private static GameObject FindOrCreateCameraRig(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<OVRCameraRig>() != null)
                    return root;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CameraRigPrefabPath);
            if (prefab == null)
                throw new System.InvalidOperationException($"Camera rig prefab not found at {CameraRigPrefabPath}.");

            var cameraRig = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            cameraRig.name = CameraRigName;
            return cameraRig;
        }

        private static void EnablePassthrough(GameObject cameraRig)
        {
            // Explicit null check: Unity's fake-null for missing components defeats "??".
            var manager = cameraRig.GetComponent<OVRManager>();
            if (manager == null)
                manager = cameraRig.AddComponent<OVRManager>();
            manager.isInsightPassthroughEnabled = true;
            EditorUtility.SetDirty(manager);

            if (cameraRig.GetComponent<OVRPassthroughLayer>() == null)
                cameraRig.AddComponent<OVRPassthroughLayer>();
        }

        private static void MakeCameraBackgroundTransparent(GameObject cameraRig)
        {
            // Passthrough is composited underneath the app, so the app must clear to
            // fully transparent black or the room is hidden behind the skybox.
            foreach (var eyeCamera in cameraRig.GetComponentsInChildren<Camera>(true))
            {
                eyeCamera.clearFlags = CameraClearFlags.SolidColor;
                eyeCamera.backgroundColor = Color.clear;
                EditorUtility.SetDirty(eyeCamera);
            }
        }

        private static void FindOrCreateReferenceCube(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == ReferenceCubeName)
                    return;
            }

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = ReferenceCubeName;
            cube.transform.position = ReferenceCubePositionMeters;
            cube.transform.localScale = Vector3.one * ReferenceCubeEdgeMeters;
            SceneManager.MoveGameObjectToScene(cube, scene);
        }

        private static void EnsureSceneFolder(string scenePath)
        {
            var folder = System.IO.Path.GetDirectoryName(scenePath)!.Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets", System.IO.Path.GetFileName(folder));
        }
    }
}
