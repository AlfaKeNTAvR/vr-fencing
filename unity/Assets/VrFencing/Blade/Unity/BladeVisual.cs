using System;
using System.Collections.Generic;
using UnityEngine;
using VrFencing.Settings;
using VrFencing.Strip;

namespace VrFencing.Blade
{
    /// <summary>
    /// Draws the épée on its mount transform (front of the guard at the origin, blade along +z): the hilt for the chosen
    /// grip (PER-41: a bell, grip and pommel modelled by <c>tools/models/build_models.py</c>, loaded from Resources as
    /// <c>Hilts/EpeeHilt*</c>; a French grip, or a Visconti or Belgian pistol grip, mirrored for the left hand), a
    /// tapering blade of épée section (a V with a fuller along its top face, flat face up on the grip) drawn as one mesh
    /// along the drawn centerline so it can bend (<see cref="DrawnBlade"/>), and a red point that flashes on a Touch or
    /// glance. While the drawn blade lags the hand (bent as far as it goes, or held off a body), the hilt is drawn where
    /// the blade stops and a faint ghost hilt shows where the hand really is. Everything gets a URP material from
    /// Resources; the importer makes none (<c>ModelImportSettings</c>) and the default primitive material renders
    /// magenta and doubled per eye on the Quest.
    /// </summary>
    internal sealed class BladeVisual : IDisposable
    {
        private const string MaterialResourcePath = "VrFencingBlade";
        private const string GripMaterialResourcePath = "VrFencingGrip";
        private const string GhostMaterialResourcePath = "VrFencingBladeGhost";
        private const string HiltResourceFolder = "Hilts/";
        private const int BladePieceCount = DrawnBlade.SampleCount;
        // The section's width at the guard and at the point: an épée blade is about 1 cm across at the forte and a few
        // millimeters at the point.
        private const float BladeBaseSideMeters = 0.011f;
        private const float BladePointSideMeters = 0.004f;
        private const float PointDiameterMeters = 0.008f;
        private const float PointLengthMeters = 0.014f;
        private const float FlashSeconds = 0.35f;

        // The blade's section, as (side, up) offsets in units of its width: a V with a shallow fuller along the top face,
        // the bottom edge below. Corner `i` and corner `i + 1` bound face `i`.
        private static readonly Vector2[] SectionCorners =
        {
            new Vector2(-0.5f, 0.25f),
            new Vector2(0.0f, 0.10f),
            new Vector2(0.5f, 0.25f),
            new Vector2(0.0f, -0.50f),
        };

        private static readonly Color BladeColor = new Color(0.80f, 0.82f, 0.86f);
        private static readonly Color PointColor = new Color(0.9f, 0.12f, 0.12f);
        private static readonly Color TouchColor = new Color(0.1f, 0.95f, 0.2f);
        private static readonly Color GlanceColor = new Color(0.3f, 0.5f, 1.0f);
        private static readonly Color GhostColor = new Color(0.85f, 0.9f, 1.0f, 0.25f);
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // The hilt model's parts that are steel; the rest (grip, prong, pad) is the grip's rubber and leather.
        private static readonly HashSet<string> SteelParts = new HashSet<string> { "Bell", "Pommel", "Collar" };

        private readonly Transform _mount;
        private readonly Transform _hilt;
        private readonly GameObject _ghost;
        private readonly Material _material;
        private readonly Material _gripMaterial;
        private readonly Material _ghostMaterial;
        private readonly Mesh _bladeMesh;
        private readonly Vector3[] _bladeVertices = new Vector3[(BladePieceCount + 1) * SectionCorners.Length * 2];
        private readonly Vector3[] _centerline = new Vector3[BladePieceCount + 1];
        private readonly Transform _point;
        private readonly Renderer _pointRenderer;
        private readonly MaterialPropertyBlock _propertyBlock = new MaterialPropertyBlock();
        private float _flashEndsAt = float.NegativeInfinity;
        private (BladeMount Mount, PistolGripShape PistolGrip, Hand Hand)? _grip;

        public BladeVisual(Transform mount)
        {
            _mount = mount;
            _material = LoadMaterial(MaterialResourcePath, "the blade is not drawn");
            _gripMaterial = LoadMaterial(GripMaterialResourcePath, "the grip is not drawn");
            _ghostMaterial = LoadMaterial(GhostMaterialResourcePath, "the hand's ghost hilt is not drawn");

            // The drawn hilt sits at the drawn guard, which lags the hand when it pushes past the most bow (PER-48).
            _hilt = new GameObject("Hilt").transform;
            _hilt.SetParent(_mount, false);

            // A faint hilt where the hand really is, shown only while the drawn blade lags it.
            _ghost = new GameObject("Hand Ghost");
            _ghost.transform.SetParent(_mount, false);
            _ghost.SetActive(false);

            SetGrip(new BladeSettings().Mount, new BladeSettings().PistolGrip, Hand.Right);

            _bladeMesh = CreateBladeMesh(_mount, _material);

            _pointRenderer = CreatePart(PrimitiveType.Cylinder, "Point", _mount, _material, PointColor);
            _point = _pointRenderer.transform;
        }

        /// <summary>The hilt model drawn for a grip: the French grip for both French mounts, the chosen pistol grip for the Pistol and Center mounts.</summary>
        public static string GetHiltResourcePath(BladeMount mount, PistolGripShape pistolGrip)
        {
            switch (mount)
            {
                case BladeMount.French:
                case BladeMount.FrenchPosted:
                    return HiltResourceFolder + "EpeeHiltFrench";
                default:
                    return HiltResourceFolder + (pistolGrip == PistolGripShape.Belgian ? "EpeeHiltBelgian" : "EpeeHiltVisconti");
            }
        }

        /// <summary>
        /// Draws the hilt of <paramref name="mount"/> (<paramref name="pistolGrip"/> for the pistol and Center mounts),
        /// mirrored for the left <paramref name="hand"/>. Nothing happens when the grip is unchanged.
        /// </summary>
        public void SetGrip(BladeMount mount, PistolGripShape pistolGrip, Hand hand)
        {
            if (_grip.HasValue && _grip.Value == (mount, pistolGrip, hand))
            {
                return;
            }

            _grip = (mount, pistolGrip, hand);
            var resourcePath = GetHiltResourcePath(mount, pistolGrip);
            var model = Resources.Load<GameObject>(resourcePath);
            if (model == null)
            {
                Debug.LogError($"[Blade] Hilt model 'Resources/{resourcePath}' is missing; the hilt is not drawn.");
            }

            BuildHilt(_hilt, model, hand, isGhost: false);
            BuildHilt(_ghost.transform, model, hand, isGhost: true);
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

        private static Material LoadMaterial(string resourcePath, string consequence)
        {
            var material = Resources.Load<Material>(resourcePath);
            if (material == null)
            {
                Debug.LogError($"[Blade] Material 'Resources/{resourcePath}' is missing; {consequence}.");
            }

            return material;
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
        /// The blade's mesh: the épée section around every centerline point, its top face toward the mount's up, tapering
        /// to the point. Each face has its own vertices so it shades flat, like the blade's ground faces.
        /// </summary>
        private Mesh CreateBladeMesh(Transform parent, Material material)
        {
            var blade = new GameObject("Blade");
            blade.transform.SetParent(parent, false);
            var mesh = new Mesh { name = "Blade" };
            mesh.MarkDynamic();
            mesh.vertices = _bladeVertices;
            var triangles = new int[BladePieceCount * SectionCorners.Length * 6];
            var cursor = 0;
            for (var piece = 0; piece < BladePieceCount; piece++)
            {
                for (var face = 0; face < SectionCorners.Length; face++)
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
            blade.AddComponent<MeshFilter>().sharedMesh = mesh;
            var bladeRenderer = blade.AddComponent<MeshRenderer>();
            AssignMaterial(bladeRenderer, material);
            SetColor(bladeRenderer, BladeColor);
            return mesh;
        }

        private static int FaceVertex(int ring, int face, int end)
        {
            return (((ring * SectionCorners.Length) + face) * 2) + end;
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

                var width = Mathf.Lerp(BladeBaseSideMeters, BladePointSideMeters, (float)ring / BladePieceCount);
                for (var corner = 0; corner < SectionCorners.Length; corner++)
                {
                    var offset = SectionCorners[corner] * width;
                    var position = _centerline[ring] + (offset.x * side) + (offset.y * up);
                    _bladeVertices[FaceVertex(ring, corner, 0)] = position;
                    _bladeVertices[FaceVertex(ring, (corner + SectionCorners.Length - 1) % SectionCorners.Length, 1)] = position;
                }
            }

            _bladeMesh.vertices = _bladeVertices;
            _bladeMesh.RecalculateNormals();
            _bladeMesh.RecalculateBounds();
        }

        /// <summary>
        /// The hilt model under <paramref name="parent"/>: the model's parts with the steel or grip material (the ghost
        /// material for the ghost), mirrored across the blade's vertical plane for the left hand. Replaces any hilt there.
        /// </summary>
        private void BuildHilt(Transform parent, GameObject model, Hand hand, bool isGhost)
        {
            for (var child = parent.childCount - 1; child >= 0; child--)
            {
                DestroyObject(parent.GetChild(child).gameObject);
            }

            if (model == null)
            {
                return;
            }

            var hilt = UnityEngine.Object.Instantiate(model, parent, false);
            hilt.name = model.name;
            hilt.transform.localPosition = Vector3.zero;
            hilt.transform.localRotation = Quaternion.identity;
            hilt.transform.localScale = new Vector3(hand == Hand.Left ? -1.0f : 1.0f, 1.0f, 1.0f);
            foreach (var partRenderer in hilt.GetComponentsInChildren<MeshRenderer>(true))
            {
                var material = isGhost ? _ghostMaterial : SteelParts.Contains(partRenderer.name) ? _material : _gripMaterial;
                AssignMaterial(partRenderer, material);
                if (isGhost)
                {
                    SetColor(partRenderer, GhostColor);
                }
            }
        }

        private Renderer CreatePart(PrimitiveType type, string partName, Transform parent, Material material, Color color)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = partName;

            // Touches are judged on the Core blade segment, not by physics.
            DestroyObject(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);

            var partRenderer = part.GetComponent<MeshRenderer>();
            AssignMaterial(partRenderer, material);
            SetColor(partRenderer, color);
            return partRenderer;
        }

        /// <summary>Every slot of the renderer gets <paramref name="material"/>; without one the part is not drawn at all.</summary>
        private static void AssignMaterial(Renderer partRenderer, Material material)
        {
            if (material != null)
            {
                var materials = new Material[Mathf.Max(1, partRenderer.sharedMaterials.Length)];
                for (var slot = 0; slot < materials.Length; slot++)
                {
                    materials[slot] = material;
                }

                partRenderer.sharedMaterials = materials;
            }
            else
            {
                partRenderer.enabled = false;
            }

            partRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            partRenderer.receiveShadows = false;
        }

        private void SetColor(Renderer partRenderer, Color color)
        {
            _propertyBlock.SetColor(BaseColorId, color);
            partRenderer.SetPropertyBlock(_propertyBlock);
        }

        private static void DestroyObject(UnityEngine.Object target)
        {
            // Edit-mode tests and the editor's model snapshots draw the blade outside Play mode, where Destroy is not allowed.
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
