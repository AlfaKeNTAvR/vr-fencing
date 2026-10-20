// Renders the models (PER-41) to PNGs in Builds/model-snapshots: the Dummy in en garde from the Fencer's view and from
// the side, the mask close up, the épée with each hilt, and the Strip, lit as the passthrough scene is, against a plain
// room-like background. A desktop check of the look when the headset is not available. Batchmode needs graphics for
// this: run it without -nographics.
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VrFencing.Blade;
using VrFencing.Dummy;
using VrFencing.Settings;
using VrFencing.Strip;

namespace VrFencing.EditorTools
{
    public static class ModelSnapshots
    {
        private const string OutputFolder = "../Builds/model-snapshots";
        private const int ImageWidth = 1600;
        private const int ImageHeight = 1200;
        private const float FencerHeightMeters = 1.75f;
        private const float EyeHeightMeters = 1.62f;
        private const float DummyDistanceMeters = 2.2f;
        private const float StripLengthMeters = 4.0f;

        // A mid grey-brown, roughly a room seen through passthrough.
        private static readonly Color RoomColor = new Color(0.46f, 0.44f, 0.4f, 1.0f);

        [MenuItem("VR Fencing/Render model snapshots")]
        public static void RenderAll()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Directory.CreateDirectory(OutputFolder);
            LightLikeTheScene();
            var camera = CreateCamera();

            var enGardeLine = new GameObject("En Garde Line").transform;
            var markings = new StripMarkings(enGardeLine);
            markings.Build(StripGeometry.Create(StripLengthMeters, FencerHeightMeters, new WarningZoneSettings()));
            markings.ShowGlow(StripEnd.Far, 0.125);

            var dummy = CreateDummy(enGardeLine);
            var headCenter = dummy.transform.TransformPoint(DummySkeleton.GetJointPosition(dummy.Layout, SkeletonJoint.HeadCenter).ToUnity());
            var chest = dummy.transform.TransformPoint(DummySkeleton.GetJointPosition(dummy.Layout, SkeletonJoint.ShoulderCenter).ToUnity());

            // The Fencer's view: standing on the En Garde Line, the Dummy ahead, the épée held in the Pistol grip.
            var epee = CreateEpee(BladeMount.Pistol, PistolGripShape.Visconti, new Vector3(0.2f, 1.05f, 0.45f), Quaternion.Euler(-4.0f, -6.0f, 0.0f));
            Render(camera, "dummy-fencer-view", new Vector3(0.0f, EyeHeightMeters, 0.0f), chest + (Vector3.down * 0.25f), 70.0f);
            Render(camera, "dummy-fencer-view-close", new Vector3(0.0f, EyeHeightMeters, 1.0f), chest, 45.0f);
            epee.SetVisible(false);
            Render(camera, "dummy-side", new Vector3(2.6f, 1.2f, DummyDistanceMeters + 0.1f), chest + (Vector3.down * 0.35f), 50.0f);
            Render(camera, "mask", headCenter + new Vector3(0.35f, 0.12f, -0.5f), headCenter, 30.0f);
            Render(camera, "mask-side", headCenter + new Vector3(0.6f, 0.05f, 0.05f), headCenter, 30.0f);

            // The Strip from behind the En Garde Line, the far Warning Zone glowing.
            dummy.SetDistanceMeters(StripLengthMeters);
            Render(camera, "strip", new Vector3(0.0f, 1.9f, -1.6f), new Vector3(0.0f, 0.0f, 2.0f), 60.0f);

            dummy.gameObject.SetActive(false);
            markings.SetVisible(false);
            epee.Dispose();
            foreach (var (name, mount, pistolGrip) in new[]
            {
                ("french", BladeMount.French, PistolGripShape.Visconti),
                ("visconti", BladeMount.Pistol, PistolGripShape.Visconti),
                ("belgian", BladeMount.Pistol, PistolGripShape.Belgian),
            })
            {
                var visual = CreateEpee(mount, pistolGrip, new Vector3(0.0f, 1.0f, 0.0f), Quaternion.identity);
                Render(camera, "epee-" + name + "-hilt", new Vector3(0.32f, 1.1f, -0.2f), new Vector3(0.0f, 0.97f, -0.06f), 32.0f);
                Render(camera, "epee-" + name + "-hilt-hand-side", new Vector3(-0.2f, 0.88f, -0.32f), new Vector3(0.0f, 0.98f, -0.05f), 32.0f);
                Render(camera, "epee-" + name + "-whole", new Vector3(0.9f, 1.25f, 0.35f), new Vector3(0.0f, 0.99f, 0.4f), 60.0f);
                visual.Dispose();
            }

            Debug.Log($"[ModelSnapshots] Wrote snapshots to {Path.GetFullPath(OutputFolder)}.");
        }

        private static void LightLikeTheScene()
        {
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.0f;
            light.color = Color.white;
            light.transform.rotation = Quaternion.Euler(50.0f, -30.0f, 0.0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.52f);
        }

        private static DummyBehaviour CreateDummy(Transform enGardeLine)
        {
            var dummy = new GameObject("Dummy").AddComponent<DummyBehaviour>();
            dummy.transform.SetParent(enGardeLine, false);
            var serialized = new SerializedObject(dummy);
            serialized.FindProperty("_bodyModel").objectReferenceValue = PassthroughSceneBuilder.LoadOpponentBodyModel();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            dummy.SetLook(new DummyLookSettings());
            dummy.Configure(FencerHeightMeters, Hand.Right);
            dummy.SetDistanceMeters(DummyDistanceMeters);
            return dummy;
        }

        /// <summary>A straight, drooping épée drawn on a mount at <paramref name="position"/>, the guard's front there and the blade along the mount's +z.</summary>
        private static BladeVisual CreateEpee(BladeMount mount, PistolGripShape pistolGrip, Vector3 position, Quaternion rotation)
        {
            var mountTransform = new GameObject("Blade Mount").transform;
            mountTransform.SetPositionAndRotation(position, rotation);
            var visual = new BladeVisual(mountTransform);
            visual.SetGrip(mount, pistolGrip, Hand.Right);

            var world = new TrackedPose(System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity);
            var forte = new BladeSegment(position.ToNumerics(), (position + (rotation * Vector3.forward * BladePose.BladeLengthMeters)).ToNumerics());
            var gripUp = (rotation * Vector3.up).ToNumerics();
            var droop = BladeDroop.Create(forte.Direction, gripUp, -System.Numerics.Vector3.UnitY, new BladeSettings { Mount = mount });
            var drawnBlade = new DrawnBlade();
            for (var step = 0; step < 30; step++)
            {
                drawnBlade.Step(droop.GetChord(forte), new BladeObstacle[0], gripUp, 1.0f / 72.0f, droop);
            }

            visual.Draw(drawnBlade, world, 1.0f);
            return visual;
        }

        private static Camera CreateCamera()
        {
            var camera = new GameObject("Snapshot Camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = RoomColor;
            camera.nearClipPlane = 0.02f;
            camera.targetTexture = new RenderTexture(ImageWidth, ImageHeight, 24);
            return camera;
        }

        private static void Render(Camera camera, string name, Vector3 from, Vector3 lookAt, float fieldOfViewDegrees)
        {
            camera.transform.position = from;
            camera.transform.rotation = Quaternion.LookRotation(lookAt - from, Vector3.up);
            camera.fieldOfView = fieldOfViewDegrees;
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = camera.targetTexture;
            var image = new Texture2D(ImageWidth, ImageHeight, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, ImageWidth, ImageHeight), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(OutputFolder, name + ".png"), image.EncodeToPNG());
            Object.DestroyImmediate(image);
        }
    }
}
