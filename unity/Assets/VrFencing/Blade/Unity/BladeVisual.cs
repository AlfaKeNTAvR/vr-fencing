using System;
using UnityEngine;
using VrFencing.Settings;
using VrFencing.Strip;

namespace VrFencing.Blade
{
    /// <summary>
    /// Draws the épée on its mount transform (front of the guard at the origin, blade along +z): grip and pommel behind
    /// the guard, the bell guard, a tapering blade drawn in pieces so it can bend (<see cref="DrawnBlade"/>), and a red
    /// point that flashes on a Touch or glance. While the drawn blade lags the hand (bent as far as it goes, or held off
    /// a body), the hilt is drawn where the blade stops and a faint ghost hilt shows where the hand really is. Primitives get the URP material from Resources; the default primitive
    /// material is not URP and renders magenta and doubled per eye on the Quest.
    /// </summary>
    internal sealed class BladeVisual : IDisposable
    {
        private const string MaterialResourcePath = "VrFencingBlade";
        private const string GhostMaterialResourcePath = "VrFencingBladeGhost";
        private const int BladePieceCount = DrawnBlade.SampleCount;
        private const float BladeBaseDiameterMeters = 0.010f;
        private const float BladePointDiameterMeters = 0.005f;
        private const float PointDiameterMeters = 0.009f;
        private const float PointLengthMeters = 0.015f;
        private const float GripDiameterMeters = 0.024f;
        private const float GripLengthMeters = 0.12f;
        private const float PommelDiameterMeters = 0.032f;
        private const float FlashSeconds = 0.35f;

        private static readonly Color BladeColor = new Color(0.78f, 0.8f, 0.84f);
        private static readonly Color GuardColor = new Color(0.62f, 0.64f, 0.68f);
        private static readonly Color GripColor = new Color(0.12f, 0.12f, 0.14f);
        private static readonly Color PointColor = new Color(0.9f, 0.12f, 0.12f);
        private static readonly Color TouchColor = new Color(0.1f, 0.95f, 0.2f);
        private static readonly Color GlanceColor = new Color(0.3f, 0.5f, 1.0f);
        private static readonly Color GhostColor = new Color(0.85f, 0.9f, 1.0f, 0.25f);
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private readonly Transform _mount;
        private readonly Transform _hilt;
        private readonly GameObject _ghost;
        private readonly Transform[] _bladePieces = new Transform[BladePieceCount];
        private readonly Transform _point;
        private readonly Renderer _pointRenderer;
        private readonly MaterialPropertyBlock _propertyBlock = new MaterialPropertyBlock();
        private float _flashEndsAt = float.NegativeInfinity;

        public BladeVisual(Transform mount)
        {
            _mount = mount;
            var material = Resources.Load<Material>(MaterialResourcePath);
            if (material == null)
            {
                Debug.LogError($"[Blade] Material 'Resources/{MaterialResourcePath}' is missing; the blade is not drawn.");
            }

            // The drawn hilt sits at the drawn guard, which lags the hand when it pushes past the most bow (PER-48).
            _hilt = new GameObject("Hilt").transform;
            _hilt.SetParent(_mount, false);
            CreateHilt(_hilt, material, GuardColor, GripColor);

            // A faint hilt where the hand really is, shown only while the drawn blade lags it.
            var ghostMaterial = Resources.Load<Material>(GhostMaterialResourcePath);
            if (ghostMaterial == null)
            {
                Debug.LogError($"[Blade] Material 'Resources/{GhostMaterialResourcePath}' is missing; the hand's ghost hilt is not drawn.");
            }

            _ghost = new GameObject("Hand Ghost");
            _ghost.transform.SetParent(_mount, false);
            CreateHilt(_ghost.transform, ghostMaterial, GhostColor, GhostColor);
            _ghost.SetActive(false);

            for (var index = 0; index < BladePieceCount; index++)
            {
                _bladePieces[index] = CreatePart(PrimitiveType.Cylinder, $"Blade {index}", _mount, material, BladeColor, Vector3.zero, Quaternion.identity, Vector3.one).transform;
            }

            _pointRenderer = CreatePart(PrimitiveType.Cylinder, "Point", _mount, material, PointColor, Vector3.zero, Quaternion.identity, Vector3.one);
            _point = _pointRenderer.transform;
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
                System.Numerics.Vector3.Transform(drawnBlade.GuardDirection, stripInRoom.Rotation).ToUnity());
            _hilt.localRotation = hiltDirection.sqrMagnitude > 1e-8f ? Quaternion.FromToRotation(Vector3.forward, hiltDirection) : Quaternion.identity;
            if (_ghost.activeSelf != drawnBlade.IsDecoupled)
            {
                _ghost.SetActive(drawnBlade.IsDecoupled);
            }

            for (var index = 0; index < BladePieceCount; index++)
            {
                var start = ToMount(stripInRoom, drawnBlade.GetSample(index));
                var end = ToMount(stripInRoom, drawnBlade.GetSample(index + 1));
                var diameter = Mathf.Lerp(BladeBaseDiameterMeters, BladePointDiameterMeters, (index + 0.5f) / BladePieceCount);
                PlaceAlong(_bladePieces[index], start, end, diameter);
            }

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
            if (_mount != null)
            {
                UnityEngine.Object.Destroy(_mount.gameObject);
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
            UnityEngine.Object.Destroy(part.GetComponent<Collider>());
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
    }
}
