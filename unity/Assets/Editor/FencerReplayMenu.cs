// Menu "VR Fencing > Replay a Fencer recording...": opens a scratch scene with a Fencer replay for a recording
// pulled from the headset into Recordings/ (PER-79). Press Play to watch it in the Scene view.
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VrFencing.Tracking;

namespace VrFencing.EditorTools
{
    public static class FencerReplayMenu
    {
        private const string ReplayObjectName = "Fencer Replay";

        // Beside the Strip, at about chest height, looking across it toward the En Garde Line.
        private static readonly Vector3 CameraPositionMeters = new Vector3(3.0f, 1.4f, 0.5f);
        private static readonly Vector3 CameraTargetMeters = new Vector3(0.0f, 1.0f, 0.5f);

        /// <summary>
        /// A new untitled scene, not the passthrough scene, so the replay never ends up in the Quest build and the Editor
        /// plays it without a headset. The recorded Strip frame sits at the world origin.
        /// </summary>
        [MenuItem("VR Fencing/Replay a Fencer recording...")]
        public static void OpenReplayScene()
        {
            var recordingsFolder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Recordings"));
            var startFolder = Directory.Exists(recordingsFolder) ? recordingsFolder : Application.dataPath;
            var path = EditorUtility.OpenFilePanel("Fencer recording", startFolder, FencerRecordingFile.Extension.TrimStart('.'));
            if (string.IsNullOrEmpty(path) || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var camera = Camera.main;
            if (camera != null)
            {
                camera.transform.SetPositionAndRotation(CameraPositionMeters, Quaternion.LookRotation(CameraTargetMeters - CameraPositionMeters));
            }

            var replay = new GameObject(ReplayObjectName).AddComponent<FencerReplayProvider>();
            var serialized = new SerializedObject(replay);
            serialized.FindProperty("_recordingPath").stringValue = path;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Selection.activeGameObject = replay.gameObject;
            Debug.Log($"[Recorder] Replay scene ready for {path}. Press Play; the Scene view draws the skeleton, head and sword controller.");
        }
    }
}
