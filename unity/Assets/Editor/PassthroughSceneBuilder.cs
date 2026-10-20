// Builds the passthrough scene: Meta camera rig with passthrough as the
// background, the Strip, the Dummy standing on it under the En Garde Line, and the
// Practice object (Drill runner, session recorder, body, arm and footwork tracking, Fencer recorder), the Blade on the sword hand, and the Menu
// (panels and off-hand pointer), and the Partner object (network session for a second Fencer).
using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrFencing.Blade;
using VrFencing.Drills;
using VrFencing.Dummy;
using VrFencing.Partner;
using VrFencing.Session;
using VrFencing.Strip;
using VrFencing.Tracking;
using VrFencing.UI;

namespace VrFencing.EditorTools
{
    public static class PassthroughSceneBuilder
    {
        private const string CameraRigPrefabPath = "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";
        // Movement SDK's bundled Humanoid rig (imported as Humanoid in the package), the Dummy's body (PER-78).
        private const string DummyBodyModelPath = "Packages/com.meta.xr.sdk.movement/Shared/ThirdParty/Unity/Armature.fbx";
        private const string CameraRigName = "OVRCameraRig";
        // The first scenes had a grey test cube; the Strip and the Dummy are the visual references now.
        private const string ObsoleteReferenceCubeName = "ReferenceCube";

        private const string StripName = "Strip";
        private const string EnGardeLineName = "En Garde Line";
        private const string DummyName = "Dummy";
        private const string PracticeName = "Practice";
        private const string MenuName = "Menu";
        private const string BladeName = "Blade";
        private const string PartnerName = "Partner";

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
            var dummy = PlaceDummyOnStrip(scene, enGardeLine);
            var strip = enGardeLine.GetComponentInParent<StripBehaviour>();
            var practice = FindOrCreatePractice(scene, strip, dummy);
            var blade = FindOrCreateBlade(scene, practice, strip, dummy);
            var partner = FindOrCreatePartner(scene, strip, blade);
            FindOrCreateMenu(scene, practice, strip, dummy, blade, partner);

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

            var passthroughLayer = GetOrAddComponent<OVRPassthroughLayer>(cameraRig);

            // [Life] device-log diagnostics for sleep/wake (PER-71): heartbeat, lifecycle callbacks, passthrough resume.
            var lifecycleLog = GetOrAddComponent<LifecycleLogBehaviour>(cameraRig);
            SetReference(lifecycleLog, "_passthroughLayer", passthroughLayer);

            // Arm tracking (IOBT) needs the body tracking permission; OVRManager asks for it at launch.
            // The field is internal to the Meta SDK, so it is set through its serialized name.
            var serializedManager = new SerializedObject(manager);
            serializedManager.FindProperty("requestBodyTrackingPermissionOnStartup").boolValue = true;
            // Dynamic Resolution off (PER-93): on Quest 3 it allocates the eye textures at its 1.6x maximum (2.56x the
            // pixels, times 4x MSAA), and a sleep/wake was measured adding about 3.2 GB of GPU memory, enough to make the
            // 8 GB headset swap and lag. Set through the serialized field so the property setter's runtime plugin call
            // does not run in the Editor. QuestProjectSetup keeps Meta's "Fix All" from turning it back on.
            serializedManager.FindProperty("_enableDynamicResolution").boolValue = false;
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
        /// settings when the app starts. An older scene's Dummy at the root is moved there. Its body is the
        /// Movement SDK's Humanoid rig, referenced from the package so the build includes it.
        /// </summary>
        private static DummyBehaviour PlaceDummyOnStrip(Scene scene, Transform enGardeLine)
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

            SetReference(dummy, "_bodyModel", LoadBodyModel());
            return dummy;
        }

        /// <summary>
        /// The Practice object hosts the Drill runner, the session recorder that writes the rep log, the body tracking
        /// (the one component that starts and stops IOBT), the arm tracking that adds the IOBT arm pose to each logged
        /// Rep, the footwork tracking that reads footwork from the head for the Drills that judge it, and the Fencer
        /// recorder (off-hand thumbstick) that records the Fencer for replay on the PC. Nothing starts a Drill until the UI does; the runner
        /// stands the Dummy at each Drill's Dummy Distance while it runs.
        /// </summary>
        private static GameObject FindOrCreatePractice(Scene scene, StripBehaviour strip, DummyBehaviour dummy)
        {
            var practice = scene.GetRootGameObjects().FirstOrDefault(root => root.name == PracticeName);
            if (practice == null)
            {
                practice = new GameObject(PracticeName);
                SceneManager.MoveGameObjectToScene(practice, scene);
            }

            var drillRunner = GetOrAddComponent<DrillRunnerBehaviour>(practice);
            var sessionRecorder = GetOrAddComponent<SessionRecorderBehaviour>(practice);
            var bodyTracking = GetOrAddComponent<BodyTrackingBehaviour>(practice);
            var armTracking = GetOrAddComponent<ArmTrackingBehaviour>(practice);
            var footworkTracking = GetOrAddComponent<FootworkTrackingBehaviour>(practice);
            var fencerRecorder = GetOrAddComponent<FencerRecorderBehaviour>(practice);

            SetReference(drillRunner, "_strip", strip);
            SetReference(drillRunner, "_dummy", dummy);
            SetReference(sessionRecorder, "_drillRunner", drillRunner);
            SetReference(armTracking, "_sessionRecorder", sessionRecorder);
            SetReference(armTracking, "_drillRunner", drillRunner);
            SetReference(armTracking, "_strip", strip);
            SetReference(armTracking, "_bodyTracking", bodyTracking);
            SetReference(fencerRecorder, "_bodyTracking", bodyTracking);
            SetReference(fencerRecorder, "_strip", strip);
            SetReference(footworkTracking, "_drillRunner", drillRunner);
            SetReference(footworkTracking, "_strip", strip);
            return practice;
        }

        /// <summary>
        /// The Blade puts the épée on the sword-hand controller at runtime, tests it against the Dummy's Target Zones in
        /// the Strip frame, and reports Touches and flat contacts to the Practice object's Drill runner.
        /// </summary>
        private static BladeBehaviour FindOrCreateBlade(Scene scene, GameObject practice, StripBehaviour strip, DummyBehaviour dummy)
        {
            var bladeObject = scene.GetRootGameObjects().FirstOrDefault(root => root.name == BladeName);
            if (bladeObject == null)
            {
                bladeObject = new GameObject(BladeName);
                SceneManager.MoveGameObjectToScene(bladeObject, scene);
            }

            var blade = GetOrAddComponent<BladeBehaviour>(bladeObject);
            SetReference(blade, "_drillRunner", practice.GetComponent<DrillRunnerBehaviour>());
            SetReference(blade, "_dummy", dummy);
            SetReference(blade, "_strip", strip);
            return blade;
        }

        /// <summary>
        /// The Menu builds its panels and the off-hand pointer at runtime. It drives the Practice object's Drill runner
        /// and session recorder, re-places the Strip, and hands changed Settings to the Strip, the Dummy and the Blade.
        /// </summary>
        private static void FindOrCreateMenu(
            Scene scene, GameObject practice, StripBehaviour strip, DummyBehaviour dummy, BladeBehaviour blade, PartnerSessionBehaviour partner)
        {
            var menuObject = scene.GetRootGameObjects().FirstOrDefault(root => root.name == MenuName);
            if (menuObject == null)
            {
                menuObject = new GameObject(MenuName);
                SceneManager.MoveGameObjectToScene(menuObject, scene);
            }

            var menu = GetOrAddComponent<MenuBehaviour>(menuObject);
            SetReference(menu, "_drillRunner", practice.GetComponent<DrillRunnerBehaviour>());
            SetReference(menu, "_sessionRecorder", practice.GetComponent<SessionRecorderBehaviour>());
            SetReference(menu, "_strip", strip);
            SetReference(menu, "_dummy", dummy);
            SetReference(menu, "_blade", blade);
            SetReference(menu, "_partner", partner);
        }

        /// <summary>
        /// The Partner object (PER-80): Netcode for GameObjects' NetworkManager with its UnityTransport (the Relay
        /// connection), and the session that hosts or joins a second Fencer by join code. The Menu's Partner page drives it.
        /// While connected, the opponent link streams both Fencers' body and blade and the opponent view draws the partner
        /// (PER-81) with the Dummy's body model. The bout judges Touches between them and shows the scoring box (PER-82).
        /// </summary>
        private static PartnerSessionBehaviour FindOrCreatePartner(Scene scene, StripBehaviour strip, BladeBehaviour blade)
        {
            var partnerObject = scene.GetRootGameObjects().FirstOrDefault(root => root.name == PartnerName);
            if (partnerObject == null)
            {
                partnerObject = new GameObject(PartnerName);
                SceneManager.MoveGameObjectToScene(partnerObject, scene);
            }

            var transport = GetOrAddComponent<UnityTransport>(partnerObject);
            var network = GetOrAddComponent<NetworkManager>(partnerObject);
            network.NetworkConfig.NetworkTransport = transport;
            // Nothing is spawned yet. Netcode's generated default list holds every NetworkObject prefab in the project
            // (the Movement SDK's samples among them), which would all ship in the APK; later slices register their own.
            network.NetworkConfig.Prefabs.NetworkPrefabsLists.Clear();
            EditorUtility.SetDirty(network);
            var partner = GetOrAddComponent<PartnerSessionBehaviour>(partnerObject);

            var link = GetOrAddComponent<OpponentLinkBehaviour>(partnerObject);
            var view = GetOrAddComponent<OpponentViewBehaviour>(partnerObject);
            SetReference(view, "_link", link);
            SetReference(view, "_strip", strip);
            SetReference(view, "_myBlade", blade);
            SetReference(view, "_bodyModel", LoadBodyModel());

            // The judge, the Touch claims and the scoring box (PER-82).
            var bout = GetOrAddComponent<BoutBehaviour>(partnerObject);
            SetReference(bout, "_blade", blade);
            SetReference(bout, "_strip", strip);
            return partner;
        }

        private static GameObject LoadBodyModel()
        {
            var bodyModel = AssetDatabase.LoadAssetAtPath<GameObject>(DummyBodyModelPath);
            if (bodyModel == null)
                throw new System.InvalidOperationException($"Dummy body model not found at {DummyBodyModelPath}.");
            return bodyModel;
        }

        /// <summary>The body model for an opponent view made at runtime outside the passthrough scene (the PC Editor's Partner window).</summary>
        public static GameObject LoadOpponentBodyModel()
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(DummyBodyModelPath);
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
