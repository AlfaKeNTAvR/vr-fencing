using System;
using UnityEngine;
using VrFencing.Settings;
using VrFencing.Strip;

namespace VrFencing.Blade
{
    /// <summary>
    /// Draws the épée on its mount transform (front of the guard at the origin, blade along +z): the hilt of the chosen
    /// grip (PER-41: a French grip, or a Visconti or Belgian pistol grip, each with its bell guard, pad, socket and
    /// pommel or nut, modelled in Blender by <c>tools/models/build_fencing_models.py</c>), a tapering blade of
    /// triangular section with the wire's groove along its flat face (flat face up on the grip, like a real épée's)
    /// drawn as one mesh along the drawn centerline so it can bend (<see cref="DrawnBlade"/>), and a red point that
    /// flashes on a Touch or glance. While the drawn blade lags the hand (bent as far as it goes, or held off a body),
    /// the hilt is drawn where the blade stops and a faint ghost hilt shows where the hand really is. Every part gets a
    /// URP material from Resources; the default primitive material is not URP and renders magenta and doubled per eye
    /// on the Quest. Without the hilt models, the hilt is drawn from primitives as before.
    /// </summary>
    internal sealed class BladeVisual : IDisposable
    {
        private const string MaterialResourcePath = "VrFencingBlade";
        private const string GhostMaterialResourcePath = "VrFencingBladeGhost";
        private const string SteelMaterialResourcePath = "VrFencingSteel";
        private const string GripMaterialResourcePath = "VrFencingGrip";
        private const string PadMaterialResourcePath = "VrFencingPad";
        private const string HiltModelFolder = "BladeModels/";
        private const int BladePieceCount = DrawnBlade.SampleCount;

        // The triangle's side at the guard and at the point: an épée blade is about 1 cm across at the forte and a few
        // millimeters at the point.
        private const float BladeBaseSideMeters = 0.011f;
        private const float BladePointSideMeters = 0.004f;

        // The groove along the flat face, where the body wire is glued in, as fractions of the triangle's side.
        private const float GrooveWidthPerSide = 0.42f;
        private const float GrooveFloorWidthPerSide = 0.24f;
        private const float GrooveDepthPerSide = 0.16f;

        private const float PointDiameterMeters = 0.009f;
        private const float PointLengthMeters = 0.015f;
        private const float GripDiameterMeters = 0.024f;
        private const float GripLengthMeters = 0.12f;
        private const float PommelDiameterMeters = 0.032f;
        private const float FlashSeconds = 0.35f;

        private static readonly Color BladeColor = new Color(0.78f, 0.8f, 0.84f);
        private static readonly Color GrooveColor = new Color(0.32f, 0.3f, 0.28f);
        private static readonly Color GuardColor = new Color(0.62f, 0.64f, 0.68f);
        private static readonly Color GripColor = new Color(0.12f, 0.12f, 0.14f);
        private static readonly Color PointColor = new Color(0.9f, 0.12f, 0.12f);
        private static readonly Color TouchColor = new Color(0.1f, 0.95f, 0.2f);
        private static readonly Color GlanceColor = new Color(0.3f, 0.5f, 1.0f);
        private static readonly Color GhostColor = new Color(0.85f, 0.9f, 1.0f, 0.25f);
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // The section's corners, counterclockwise seen from the point (side = +x, up = +y), for a side of 1: the flat
        // face's left corner, the bottom edge, the right corner, then back along the flat face through the groove.
        private static readonly Vector2[] CrossSection = CreateCrossSection();
        private static readonly int CrossSectionCorners = CrossSection.Length;

        // The faces that line the groove (each from the corner of the same index): drawn darker, the glued-in wire.
        private static readonly bool[] IsGrooveFace = { false, false, false, true, true, true, false };

        private readonly Transform _mount;
        private readonly Transform _hilt;
        private readonly GameObject _ghost;
        private readonly Material _material;
        private readonly Material _ghostMaterial;
        private readonly Mesh _bladeMesh;
        private readonly Vector3[] _bladeVertices = new Vector3[(BladePieceCount + 1) * CrossSectionCorners * 2];
        private readonly Vector3[] _centerline = new Vector3[BladePieceCount + 1];
        private readonly Transform _point;
        private readonly Renderer _pointRenderer;
        private readonly MaterialPropertyBlock _propertyBlock = new MaterialPropertyBlock();
        private float _flashEndsAt = float.NegativeInfinity;

        /// <summary>Draws the French grip until <see cref="SetGrip"/> picks another.</summary>
        public BladeVisual(Transform mount)
        {
            _mount = mount;
            _material = Resources.Load<Material>(MaterialResourcePath);
            if (_material == null)
            {
                Debug.LogError($"[Blade] Material 'Resources/{MaterialResourcePath}' is missing; the blade is not drawn.");
            }

            // The drawn hilt sits at the drawn guard, which lags the hand when it pushes past the most bow (PER-48).
            _hilt = new GameObject("Hilt").transform;
            _hilt.SetParent(_mount, false);

            // A faint hilt where the hand really is, shown only while the drawn blade lags it.
            _ghostMaterial = Resources.Load<Material>(GhostMaterialResourcePath);
            if (_ghostMaterial == null)
            {
                Debug.LogError($"[Blade] Material 'Resources/{GhostMaterialResourcePath}' is missing; the hand's ghost hilt is not drawn.");
            }

            _ghost = new GameObject("Hand Ghost");
            _ghost.transform.SetParent(_mount, false);
            _ghost.SetActive(false);
            SetGrip(BladeMount.French, PistolGripShape.Visconti);

            // The blade is steel; without the steel material it keeps the flat blade material and its colour.
            var steel = Resources.Load<Material>(SteelMaterialResourcePath);
            _bladeMesh = CreateBladeMesh(_mount, steel != null ? steel : _material, steel == null);

            _pointRenderer = CreatePart(PrimitiveType.Cylinder, "Point", _mount, _material, PointColor, Vector3.zero, Quaternion.identity, Vector3.one);
            _point = _pointRenderer.transform;
        }

        /// <summary>The name of the hilt model drawn, e.g. "HiltVisconti".</summary>
        public string HiltModelName { get; private set; }

        /// <summary>The hilt model in Resources for a grip: each pistol grip has its own, the other grips hold a French grip.</summary>
        public static string GetHiltModelName(BladeMount mount, PistolGripShape pistolGrip)
        {
            if (mount != BladeMount.Pistol)
            {
                // Center is the controller held as is (no real grip); French posted is the same grip held further back.
                return "HiltFrench";
            }

            return pistolGrip == PistolGripShape.Belgian ? "HiltBelgian" : "HiltVisconti";
        }

        /// <summary>
        /// Draws the hilt of <paramref name="mount"/> (and, for a pistol grip, <paramref name="pistolGrip"/>'s shape).
        /// Only the drawing changes: where the blade sits on the controller is the grip's mount (<see cref="BladePose"/>).
        /// The models are symmetric side to side, so a left hand needs no mirror.
        /// </summary>
        public void SetGrip(BladeMount mount, PistolGripShape pistolGrip)
        {
            var modelName = GetHiltModelName(mount, pistolGrip);
            if (modelName == HiltModelName)
            {
                return;
            }

            HiltModelName = modelName;
            var model = Resources.Load<GameObject>(HiltModelFolder + modelName);
            if (model == null)
            {
                Debug.LogError($"[Blade] Hilt model 'Resources/{HiltModelFolder}{modelName}' is missing; drawing the hilt from primitives.");
            }

            RebuildHilt(_hilt, model, isGhost: false);
            RebuildHilt(_ghost.transform, model, isGhost: true);
        }

        public void Flash(BladeEventType eventType, float nowSeconds)
        {
            if (eventType == BladeEventType.FlatContact)
            {
                return;
            }

            SetColor(_pointRenderer, eventType == BladeEventType.Touch ? TouchColor : GlanceColor);
            _flashEndsAt = nowSeconds + FlashSeconds;
        }

        /// <summary>
        /// Lays the blade pieces along the drawn centerline. <paramref name="drawnBlade"/> is in the Strip frame,
        /// <paramref name="stripInRoom"/> places that frame in the world.
        /// </summary>
        public void Draw(DrawnBlade drawnBlade, TrackedPose stripInRoom, float nowSeconds)
        {
            // The drawn hilt sits at the drawn guard, turned onto the drawn blade (it lags the hand when the blade cannot
            // bend further or is held off a body).
            _hilt.localPosition = ToMount(stripInRoom, drawnBlade.GuardPosition);
            var hiltDirection = _mount.InverseTransformDirection(
                System.Numerics.Vector3.Transform(drawnBlade.HiltDirection, stripInRoom.Rotation).ToUnity());
            _hilt.localRotation = hiltDirection.sqrMagnitude > 1e-8f ? Quaternion.FromToRotation(Vector3.forward, hiltDirection) : Quaternion.identity;
            if (_ghost.activeSelf != drawnBlade.IsDecoupled)
            {
                _ghost.SetActive(drawnBlade.IsDecoupled);
            }

            for (var index = 0; index <= BladePieceCount; index++)
            {
                _centerline[index] = ToMount(stripInRoom, drawnBlade.GetSample(index));
            }

            UpdateBladeMesh();

            var tip = ToMount(stripInRoom, drawnBlade.GetCenterlinePoint(1.0f));
            var beforeTip = ToMount(stripInRoom, drawnBlade.GetCenterlinePoint(0.98f));
            var pointDirection = (tip - beforeTip).normalized;
            PlaceAlong(_point, tip - (pointDirection * PointLengthMeters), tip, PointDiameterMeters);

            if (nowSeconds >= _flashEndsAt && !float.IsNegativeInfinity(_flashEndsAt))
            {
                SetColor(_pointRenderer, PointColor);
                _flashEndsAt = float.NegativeInfinity;
            }
        }

        public void SetVisible(bool isVisible)
        {
            _mount.gameObject.SetActive(isVisible);
        }

        public void Dispose()
        {
            if (_bladeMesh != null)
            {
                DestroyObject(_bladeMesh);
            }

            if (_mount != null)
            {
                DestroyObject(_mount.gameObject);
            }
        }

        private Vector3 ToMount(TrackedPose stripInRoom, System.Numerics.Vector3 pointInStrip)
        {
            var worldPoint = StripFrame.ToRoom(stripInRoom, pointInStrip);
            return _mount.InverseTransformPoint(worldPoint.ToUnity());
        }

        private static void PlaceAlong(Transform piece, Vector3 start, Vector3 end, float diameter)
        {
            var axis = end - start;
            var length = axis.magnitude;
            piece.localPosition = (start + end) * 0.5f;
            piece.localRotation = length > 1e-6f ? Quaternion.FromToRotation(Vector3.up, axis / length) : Quaternion.identity;
            piece.localScale = new Vector3(diameter, length * 0.5f, diameter);
        }

        /// <summary>
        /// The blade's mesh: the grooved triangle (<see cref="CrossSection"/>) around every centerline point, its flat
        /// face toward the mount's up, tapering to the point. Each face has its own vertices so it shades flat, like
        /// the blade's faces; the groove's faces are darker through the vertex colours.
        /// </summary>
        private Mesh CreateBladeMesh(Transform parent, Material material, bool isFlatMaterial)
        {
            var blade = new GameObject("Blade");
            blade.transform.SetParent(parent, false);
            var mesh = new Mesh { name = "Blade" };
            mesh.MarkDynamic();
            mesh.vertices = _bladeVertices;
            var triangles = new int[BladePieceCount * CrossSectionCorners * 6];
            var cursor = 0;
            for (var piece = 0; piece < BladePieceCount; piece++)
            {
                for (var face = 0; face < CrossSectionCorners; face++)
                {
                    // Face `face` runs from corner `face` to the next; vertex (ring, face, 0 or 1) at FaceVertex.
                    var a = FaceVertex(piece, face, 0);
                    var b = FaceVertex(piece, face, 1);
                    var c = FaceVertex(piece + 1, face, 0);
                    var d = FaceVertex(piece + 1, face, 1);
                    // Wound so Unity's normal, Cross(second - first, third - first), points out of the blade.
                    triangles[cursor++] = a;
                    triangles[cursor++] = b;
                    triangles[cursor++] = c;
                    triangles[cursor++] = b;
                    triangles[cursor++] = d;
                    triangles[cursor++] = c;
                }
            }

            mesh.triangles = triangles;
            mesh.colors = CreateBladeColors();
            blade.AddComponent<MeshFilter>().sharedMesh = mesh;
            var bladeRenderer = blade.AddComponent<MeshRenderer>();
            if (material != null)
            {
                bladeRenderer.sharedMaterial = material;
            }
            else
            {
                bladeRenderer.enabled = false;
            }

            bladeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            bladeRenderer.receiveShadows = false;
            if (isFlatMaterial)
            {
                SetColor(bladeRenderer, BladeColor);
            }

            return mesh;
        }

        private static Color[] CreateBladeColors()
        {
            var colors = new Color[(BladePieceCount + 1) * CrossSectionCorners * 2];
            for (var ring = 0; ring <= BladePieceCount; ring++)
            {
                for (var face = 0; face < CrossSectionCorners; face++)
                {
                    var color = IsGrooveFace[face] ? GrooveColor : Color.white;
                    colors[FaceVertex(ring, face, 0)] = color;
                    colors[FaceVertex(ring, face, 1)] = color;
                }
            }

            return colors;
        }

        /// <summary>
        /// An épée's section for a side of 1: an equilateral triangle with its flat face up and its centroid at the
        /// origin, the groove sunk into the middle of the flat face.
        /// </summary>
        private static Vector2[] CreateCrossSection()
        {
            var flatFaceHeight = 0.5f / Mathf.Sqrt(3.0f);
            var bottomDepth = 1.0f / Mathf.Sqrt(3.0f);
            var grooveFloor = flatFaceHeight - GrooveDepthPerSide;
            return new[]
            {
                new Vector2(-0.5f, flatFaceHeight),
                new Vector2(0.0f, -bottomDepth),
                new Vector2(0.5f, flatFaceHeight),
                new Vector2(GrooveWidthPerSide * 0.5f, flatFaceHeight),
                new Vector2(GrooveFloorWidthPerSide * 0.5f, grooveFloor),
                new Vector2(-GrooveFloorWidthPerSide * 0.5f, grooveFloor),
                new Vector2(-GrooveWidthPerSide * 0.5f, flatFaceHeight),
            };
        }

        private static int FaceVertex(int ring, int face, int end)
        {
            return (((ring * CrossSectionCorners) + face) * 2) + end;
        }

        private void UpdateBladeMesh()
        {
            for (var ring = 0; ring <= BladePieceCount; ring++)
            {
                var before = _centerline[Mathf.Max(0, ring - 1)];
                var after = _centerline[Mathf.Min(BladePieceCount, ring + 1)];
                var tangent = after - before;
                tangent = tangent.sqrMagnitude > 1e-12f ? tangent.normalized : Vector3.forward;
                var up = Vector3.ProjectOnPlane(Vector3.up, tangent);
                up = up.sqrMagnitude > 1e-8f ? up.normalized : Vector3.ProjectOnPlane(Vector3.right, tangent).normalized;
                var side = Vector3.Cross(up, tangent);

                // The grooved triangle: a flat face on top (two upper corners and the groove), the third corner below.
                var sideLength = Mathf.Lerp(BladeBaseSideMeters, BladePointSideMeters, (float)ring / BladePieceCount);
                for (var corner = 0; corner < CrossSectionCorners; corner++)
                {
                    var position = _centerline[ring] + (((CrossSection[corner].x * side) + (CrossSection[corner].y * up)) * sideLength);
                    _bladeVertices[FaceVertex(ring, corner, 0)] = position;
                    _bladeVertices[FaceVertex(ring, (corner + CrossSectionCorners - 1) % CrossSectionCorners, 1)] = position;
                }
            }

            _bladeMesh.vertices = _bladeVertices;
            _bladeMesh.RecalculateNormals();
            _bladeMesh.RecalculateBounds();
        }

        /// <summary>
        /// Replaces <paramref name="parent"/>'s hilt with an instance of <paramref name="model"/> (its parts named by
        /// material: Steel, Grip, Pad), or the primitive hilt without a model. The ghost draws every part faint.
        /// </summary>
        private void RebuildHilt(Transform parent, GameObject model, bool isGhost)
        {
            for (var child = parent.childCount - 1; child >= 0; child--)
            {
                DestroyObject(parent.GetChild(child).gameObject);
            }

            if (model == null)
            {
                CreateHilt(parent, isGhost ? _ghostMaterial : _material, isGhost ? GhostColor : GuardColor, isGhost ? GhostColor : GripColor);
                return;
            }

            var instance = UnityEngine.Object.Instantiate(model, parent, false);
            instance.name = model.name;
            instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var partRenderer in instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                var material = isGhost ? _ghostMaterial : LoadHiltMaterial(partRenderer.name);
                partRenderer.sharedMaterial = material;
                partRenderer.enabled = material != null;
                partRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                partRenderer.receiveShadows = false;
                if (isGhost)
                {
                    SetColor(partRenderer, GhostColor);
                }
            }
        }

        /// <summary>The material of a hilt part by its name in the model, or the flat blade material if it is missing.</summary>
        private Material LoadHiltMaterial(string partName)
        {
            var path = partName.StartsWith("Pad", StringComparison.Ordinal) ? PadMaterialResourcePath
                : partName.StartsWith("Grip", StringComparison.Ordinal) ? GripMaterialResourcePath
                : SteelMaterialResourcePath;
            var material = Resources.Load<Material>(path);
            if (material == null)
            {
                Debug.LogError($"[Blade] Material 'Resources/{path}' is missing; drawing the hilt's {partName} flat.");
                return _material;
            }

            return material;
        }

        /// <summary>Guard, grip and pommel behind the guard's front at <paramref name="parent"/>'s origin, the blade along +z.</summary>
        private void CreateHilt(Transform parent, Material material, Color guardColor, Color gripColor)
        {
            // Unity's cylinder primitive is 2 m tall along its local y; turned onto z, the blade axis.
            var alongBlade = Quaternion.Euler(90.0f, 0.0f, 0.0f);
            CreatePart(PrimitiveType.Cylinder, "Guard", parent, material, guardColor, new Vector3(0.0f, 0.0f, -BladePose.GuardDepthMeters * 0.5f), alongBlade,
                new Vector3(BladePose.GuardDiameterMeters, BladePose.GuardDepthMeters * 0.5f, BladePose.GuardDiameterMeters));
            var gripCenter = -BladePose.GuardDepthMeters - (GripLengthMeters * 0.5f);
            CreatePart(PrimitiveType.Cylinder, "Grip", parent, material, gripColor, new Vector3(0.0f, 0.0f, gripCenter), alongBlade,
                new Vector3(GripDiameterMeters, GripLengthMeters * 0.5f, GripDiameterMeters));
            CreatePart(PrimitiveType.Sphere, "Pommel", parent, material, guardColor, new Vector3(0.0f, 0.0f, gripCenter - (GripLengthMeters * 0.5f)), Quaternion.identity,
                Vector3.one * PommelDiameterMeters);
        }

        private Renderer CreatePart(PrimitiveType type, string partName, Transform parent, Material material, Color color, Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = partName;

            // Touches are judged on the Core blade segment, not by physics.
            DestroyObject(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.transform.SetLocalPositionAndRotation(localPosition, localRotation);
            part.transform.localScale = localScale;

            var partRenderer = part.GetComponent<MeshRenderer>();
            if (material != null)
            {
                partRenderer.sharedMaterial = material;
            }
            else
            {
                partRenderer.enabled = false;
            }

            partRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            partRenderer.receiveShadows = false;
            SetColor(partRenderer, color);
            return partRenderer;
        }

        private void SetColor(Renderer partRenderer, Color color)
        {
            _propertyBlock.SetColor(BaseColorId, color);
            partRenderer.SetPropertyBlock(_propertyBlock);
        }

        private static void DestroyObject(UnityEngine.Object target)
        {
            // Edit-mode tests and editor tools build the blade outside Play mode, where Destroy is not allowed.
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(target);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
