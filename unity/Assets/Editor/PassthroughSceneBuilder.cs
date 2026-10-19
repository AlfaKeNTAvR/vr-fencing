// Builds the passthrough scene: Meta camera rig with passthrough as the
// background, the Strip, the Dummy standing on it under the En Garde Line, and the
// Practice object (Drill runner, session recorder, arm tracking).
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrFencing.Drills;
using VrFencing.Dummy;
using VrFencing.Session;
using VrFencing.Strip;
using VrFencing.Tracking;

namespace VrFencing.EditorTools
{
    public static class PassthroughSceneBuilder
    {
        private const string CameraRigPrefabPath = "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";
        private const string CameraRigName = "OVRCameraRig";
        // The first scenes had a grey test cube; the Strip and the Dummy are the visual references now.
        private const string ObsoleteReferenceCubeName = "ReferenceCube";

        private const string StripName = "Strip";
        private const string EnGardeLineName = "En Garde Line";
        private const string DummyName = "Dummy";
        private const string PracticeName = "Practice";

        // Only where the Dummy shows in the editor; at runtime it moves itself to its distance preset along the Strip.
        private static readonly Vector3 DummyPlaceholderPositionMeters = new(0f, 0f, 2.2f);

        /// <summary>Batchmode and menu entry point: brings the passthrough scene up to date.</summary>
        [MenuItem("VR Fencing/Update passthrough scene")]
        public static void UpdatePassthroughScene()
        {
            CreateOrUpdateScene(QuestProjectSetup.PassthroughScenePath);
        }

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
            RemoveObsoleteReferenceCube(scene);
            var enGardeLine = FindOrCreateStrip(scene);
            PlaceDummyOnStrip(scene, enGardeLine);
            FindOrCreatePractice(scene, enGardeLine.GetComponentInParent<StripBehaviour>());

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
            // Floor level puts the tracking origin on the real floor, so scene heights are
            // heights above the floor. The default (eye level) puts it at head height,
            // which floated the reference cube ~1.2 m above the Fencer's head.
            manager.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
            EditorUtility.SetDirty(manager);

            if (cameraRig.GetComponent<OVRPassthroughLayer>() == null)
                cameraRig.AddComponent<OVRPassthroughLayer>();

            // Arm tracking (IOBT) needs the body tracking permission; OVRManager asks for it at launch.
            // The field is internal to the Meta SDK, so it is set through its serialized name.
            var serializedManager = new SerializedObject(manager);
            serializedManager.FindProperty("requestBodyTrackingPermissionOnStartup").boolValue = true;
            serializedManager.ApplyModifiedPropertiesWithoutUndo();
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

        private static void RemoveObsoleteReferenceCube(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == ObsoleteReferenceCubeName)
                    Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// The Strip at the scene origin with its En Garde Line child (the Strip frame). At runtime the Strip moves the
        /// En Garde Line to the placed or restored Strip. Returns the En Garde Line.
        /// </summary>
        private static Transform FindOrCreateStrip(Scene scene)
        {
            StripBehaviour strip = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                strip = root.GetComponent<StripBehaviour>();
                if (strip != null)
                    break;
            }

            if (strip == null)
            {
                var stripObject = new GameObject(StripName);
                SceneManager.MoveGameObjectToScene(stripObject, scene);
                strip = stripObject.AddComponent<StripBehaviour>();
            }

            var serializedStrip = new SerializedObject(strip);
            var enGardeLineProperty = serializedStrip.FindProperty("_enGardeLine");
            var enGardeLine = enGardeLineProperty.objectReferenceValue as Transform;
            if (enGardeLine == null)
            {
                enGardeLine = new GameObject(EnGardeLineName).transform;
                enGardeLine.SetParent(strip.transform, false);
                enGardeLineProperty.objectReferenceValue = enGardeLine;
                serializedStrip.ApplyModifiedPropertiesWithoutUndo();
            }

            return enGardeLine;
        }

        /// <summary>
        /// The Dummy stands under the En Garde Line, so it follows the Strip; it builds its silhouette from the
        /// settings when the app starts. An older scene's Dummy at the root is moved there.
        /// </summary>
        private static void PlaceDummyOnStrip(Scene scene, Transform enGardeLine)
        {
            var dummy = Object.FindObjectsByType<DummyBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(candidate => candidate.gameObject.scene == scene);
            if (dummy == null)
            {
                dummy = new GameObject(DummyName).AddComponent<DummyBehaviour>();
                SceneManager.MoveGameObjectToScene(dummy.gameObject, scene);
            }

            dummy.transform.SetParent(enGardeLine, false);
            dummy.transform.SetLocalPositionAndRotation(DummyPlaceholderPositionMeters, Quaternion.Euler(0f, 180f, 0f));
        }

        /// <summary>
        /// The Practice object hosts the Drill runner, the session recorder that writes the rep log, and the arm
        /// tracking that adds the IOBT arm pose to each logged Rep. Nothing starts a Drill until the UI does.
        /// </summary>
        private static void FindOrCreatePractice(Scene scene, StripBehaviour strip)
        {
            var practice = scene.GetRootGameObjects().FirstOrDefault(root => root.name == PracticeName);
            if (practice == null)
            {
                practice = new GameObject(PracticeName);
                SceneManager.MoveGameObjectToScene(practice, scene);
            }

            var drillRunner = GetOrAddComponent<DrillRunnerBehaviour>(practice);
            var sessionRecorder = GetOrAddComponent<SessionRecorderBehaviour>(practice);
            var armTracking = GetOrAddComponent<ArmTrackingBehaviour>(practice);

            SetReference(drillRunner, "_strip", strip);
            SetReference(sessionRecorder, "_drillRunner", drillRunner);
            SetReference(armTracking, "_sessionRecorder", sessionRecorder);
            SetReference(armTracking, "_drillRunner", drillRunner);
            SetReference(armTracking, "_strip", strip);
        }

        private static T GetOrAddComponent<T>(GameObject target) where T : Component
        {
            var component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }

        private static void SetReference(Component owner, string fieldName, Object value)
        {
            var serialized = new SerializedObject(owner);
            serialized.FindProperty(fieldName).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureSceneFolder(string scenePath)
        {
            var folder = System.IO.Path.GetDirectoryName(scenePath)!.Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets", System.IO.Path.GetFileName(folder));
        }
    }
}
