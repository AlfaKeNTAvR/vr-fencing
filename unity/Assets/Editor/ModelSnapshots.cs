// Renders the Dummy in its fencing suit and mask, the épée in each grip and the Strip to PNGs in
// Builds/model-snapshots (PER-41), on a room-coloured backdrop that stands in for passthrough. A desktop check of the
// models when the headset is not available. Batchmode needs graphics for this: run it without -nographics:
//   Unity -batchmode -projectPath unity -executeMethod VrFencing.EditorTools.ModelSnapshots.RenderAll -quit
// MODEL_SNAPSHOT_DIR sets another output folder. It also logs the polycount and draw calls of each model.
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private const string DefaultOutputFolder = "../Builds/model-snapshots";
        private const string BodyModelPath = "Packages/com.meta.xr.sdk.movement/Shared/ThirdParty/Unity/Armature.fbx";
        private const int ImageWidth = 1600;
        private const int ImageHeight = 1200;
        private const float FencerHeightMeters = 1.75f;
        private const float PlacedStripMeters = 4.0f;

        // A mid grey-brown room seen through passthrough, and a darker floor.
        private static readonly Color RoomColor = new Color(0.42f, 0.4f, 0.36f, 1.0f);
        private static readonly Color FloorColor = new Color(0.3f, 0.28f, 0.25f, 1.0f);

        // Where the lone épées for the close-ups float, away from the Dummy.
        private static readonly Vector3 CloseUpGuard = new Vector3(20.0f, 1.2f, 0.0f);

        [MenuItem("VR Fencing/Render model snapshots")]
        public static void RenderAll()
        {
            var outputFolder = System.Environment.GetEnvironmentVariable("MODEL_SNAPSHOT_DIR");
            outputFolder = string.IsNullOrEmpty(outputFolder) ? DefaultOutputFolder : outputFolder;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Directory.CreateDirectory(outputFolder);
            var camera = CreateCamera();
            CreateFloor();

            var stripFrame = new GameObject("En Garde Line").transform;
            var markings = new StripMarkings(stripFrame);
            markings.Build(StripGeometry.Create(PlacedStripMeters, FencerHeightMeters, new WarningZoneSettings()));

            var dummy = CreateDummy(stripFrame);
            var mask = dummy.GetComponentsInChildren<Transform>().First(part => part.name == "Mask");
            LogHeadFit(dummy, mask);
            var chest = dummy.transform.TransformPoint(new Vector3(0.0f, 0.68f * FencerHeightMeters, -0.12f * FencerHeightMeters));

            // The Fencer's own épée in en garde, pointing at the Dummy's chest.
            var fencerGuard = new Vector3(0.21f, 1.1f, 0.36f);
            var fencerBlade = CreateEpee("Fencer Blade", BladeMount.French, PistolGripShape.Visconti, fencerGuard, chest - fencerGuard);
            foreach (var part in dummy.GetComponentsInChildren<DummySuitRegions>())
            {
                part.Refresh();
            }

            Render(camera, outputFolder, "dummy-fencer-view", new Vector3(0.04f, 1.63f, -0.05f), new Vector3(0.06f, 1.12f, 1.5f), 78.0f);
            fencerBlade.SetVisible(false);
            var dummyMiddle = dummy.transform.position + new Vector3(0.0f, 0.95f, 0.0f);
            Render(camera, outputFolder, "dummy-side", dummyMiddle + new Vector3(3.0f, 0.25f, -0.35f), dummyMiddle, 45.0f);
            Render(camera, outputFolder, "dummy-three-quarter", dummyMiddle + new Vector3(1.7f, 0.45f, -2.3f), dummyMiddle, 45.0f);

            var face = mask.position;
            Render(camera, outputFolder, "mask", face + (dummy.transform.forward * 0.55f) + (dummy.transform.right * 0.28f) + new Vector3(0.0f, 0.06f, 0.0f), face, 40.0f);
            Render(camera, outputFolder, "mask-side", face + (dummy.transform.right * 0.6f) + (dummy.transform.forward * 0.1f), face, 40.0f);

            dummy.gameObject.SetActive(false);
            Render(camera, outputFolder, "strip", new Vector3(1.9f, 2.7f, -2.6f), new Vector3(0.0f, 0.0f, 1.6f), 60.0f);
            dummy.gameObject.SetActive(true);

            var grips = new (string Name, BladeMount Mount, PistolGripShape Shape)[]
            {
                ("french", BladeMount.French, PistolGripShape.Visconti),
                ("visconti", BladeMount.Pistol, PistolGripShape.Visconti),
                ("belgian", BladeMount.Pistol, PistolGripShape.Belgian),
            };
            foreach (var grip in grips)
            {
                var epee = CreateEpee("Epee " + grip.Name, grip.Mount, grip.Shape, CloseUpGuard, Vector3.forward);
                var hiltMiddle = CloseUpGuard + new Vector3(0.0f, -0.01f, -0.06f);
                Render(camera, outputFolder, $"epee-{grip.Name}", hiltMiddle + new Vector3(0.3f, 0.13f, -0.3f), hiltMiddle + new Vector3(0.0f, 0.0f, 0.03f), 40.0f);
                Render(camera, outputFolder, $"epee-{grip.Name}-side", hiltMiddle + new Vector3(0.42f, 0.0f, 0.0f), hiltMiddle, 35.0f);
                Render(camera, outputFolder, $"epee-{grip.Name}-full", CloseUpGuard + new Vector3(0.55f, 0.25f, 0.35f), CloseUpGuard + new Vector3(0.0f, -0.02f, 0.4f), 75.0f);
                LogModel($"épée ({grip.Name})", GameObject.Find("Epee " + grip.Name));
                epee.Dispose();
            }

            LogModel("Dummy (body, mask, zones)", dummy.gameObject);
            LogModel("Strip markings", stripFrame.gameObject);
            Debug.Log($"[ModelSnapshots] Wrote snapshots to {Path.GetFullPath(outputFolder)}.");
        }

        private static Camera CreateCamera()
        {
            var camera = new GameObject("Snapshot Camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = RoomColor;
            camera.nearClipPlane = 0.02f;
            camera.farClipPlane = 60.0f;
            camera.targetTexture = new RenderTexture(ImageWidth, ImageHeight, 24) { antiAliasing = 8 };
            return camera;
        }

        private static void CreateFloor()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Room Floor";
            Object.DestroyImmediate(floor.GetComponent<Collider>());
            floor.transform.SetPositionAndRotation(new Vector3(0.0f, -0.001f, 0.0f), Quaternion.identity);
            floor.transform.localScale = new Vector3(8.0f, 1.0f, 8.0f);
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetColor("_BaseColor", FloorColor);
            floor.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static DummyBehaviour CreateDummy(Transform stripFrame)
        {
            var dummyObject = new GameObject("Dummy");
            dummyObject.transform.SetParent(stripFrame, false);
            var dummy = dummyObject.AddComponent<DummyBehaviour>();
            var serialized = new SerializedObject(dummy);
            serialized.FindProperty("_bodyModel").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(BodyModelPath);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            dummy.SetLook(new DummyLookSettings());
            dummy.Configure(FencerHeightMeters, Hand.Right);
            return dummy;
        }

        /// <summary>An épée hanging in its rest shape (set and sag) with its guard at <paramref name="guard"/>.</summary>
        private static BladeVisual CreateEpee(string name, BladeMount mount, PistolGripShape shape, Vector3 guard, Vector3 pointing)
        {
            var mountTransform = new GameObject(name).transform;
            var direction = pointing.normalized;
            var rotation = Quaternion.LookRotation(direction, Vector3.up);
            mountTransform.SetPositionAndRotation(guard, rotation);
            var visual = new BladeVisual(mountTransform);
            visual.SetGrip(mount, shape);

            var settings = new BladeSettings { Mount = mount, PistolGrip = shape };
            var forte = new BladeSegment(guard.ToNumerics(), (guard + (direction * BladePose.BladeLengthMeters)).ToNumerics());
            var gripUp = (rotation * Vector3.up).ToNumerics();
            var droop = BladeDroop.Create(forte.Direction, gripUp, -System.Numerics.Vector3.UnitY, settings);
            var drawnBlade = new DrawnBlade();
            for (var step = 0; step < 200; step++)
            {
                drawnBlade.Step(droop.GetChord(forte), new List<BladeObstacle>(), gripUp, 1.0f / 72.0f, droop);
            }

            visual.Draw(drawnBlade, new TrackedPose(System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity), 0.0f);
            return visual;
        }

        private static void Render(Camera camera, string outputFolder, string name, Vector3 from, Vector3 lookAt, float fieldOfView)
        {
            camera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(lookAt - from, Vector3.up));
            camera.fieldOfView = fieldOfView;
            camera.Render();
            var resolved = RenderTexture.GetTemporary(ImageWidth, ImageHeight, 0);
            Graphics.Blit(camera.targetTexture, resolved);
            var previous = RenderTexture.active;
            RenderTexture.active = resolved;
            var image = new Texture2D(ImageWidth, ImageHeight, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, ImageWidth, ImageHeight), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(resolved);
            File.WriteAllBytes(Path.Combine(outputFolder, name + ".png"), image.EncodeToPNG());
            Object.DestroyImmediate(image);
        }

        /// <summary>Logs how the rig's head sits in the mask, in the mask's frame (x right, y up, z where it looks).</summary>
        private static void LogHeadFit(DummyBehaviour dummy, Transform mask)
        {
            var headBounds = new Bounds();
            var isFirst = true;
            foreach (var skinned in dummy.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var baked = new Mesh();
                skinned.BakeMesh(baked, true);
                foreach (var vertex in baked.vertices)
                {
                    var world = skinned.transform.position + (skinned.transform.rotation * vertex);
                    var inMask = Quaternion.Inverse(mask.rotation) * (world - mask.position);
                    if (inMask.magnitude > 0.2f || inMask.y < -0.16f)
                    {
                        continue;
                    }

                    if (isFirst)
                    {
                        headBounds = new Bounds(inMask, Vector3.zero);
                        isFirst = false;
                    }
                    else
                    {
                        headBounds.Encapsulate(inMask);
                    }
                }

                Object.DestroyImmediate(baked);
            }

            var maskBounds = mask.GetComponentsInChildren<Renderer>().Select(part => part.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
            Debug.Log($"[ModelSnapshots] Head vertices near the mask, in the mask's frame: min {headBounds.min:F3}, max {headBounds.max:F3}. Mask world size {maskBounds.size:F3}, scale {mask.lossyScale.x:F3}.");
        }

        private static void LogModel(string label, GameObject root)
        {
            var triangles = 0;
            var drawCalls = 0;
            foreach (var partRenderer in root.GetComponentsInChildren<Renderer>())
            {
                var mesh = partRenderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh : partRenderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null || !partRenderer.enabled)
                {
                    continue;
                }

                drawCalls += mesh.subMeshCount;
                for (var subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                {
                    triangles += (int)(mesh.GetIndexCount(subMesh) / 3);
                }
            }

            Debug.Log($"[ModelSnapshots] {label}: {triangles} triangles, {drawCalls} draw calls (before batching).");
        }
    }
}
