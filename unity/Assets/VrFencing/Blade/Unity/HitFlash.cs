using System;
using UnityEngine;

namespace VrFencing.Blade
{
    /// <summary>
    /// The red flash when the Armed Dummy's point lands on the Fencer (PER-23 resolution, PER-98): a translucent red
    /// sheet just in front of the eyes, covering the view, that fades out over <see cref="Seconds"/>. Head-locked, so it
    /// shows wherever the Fencer looks; the passthrough stays visible through it.
    /// </summary>
    internal sealed class HitFlash : IDisposable
    {
        /// <summary>How long the flash takes to fade out.</summary>
        public const float Seconds = 0.25f;

        // The translucent blade material (URP, alpha blended), recoloured per renderer.
        private const string MaterialResourcePath = "VrFencingBladeGhost";

        // In front of the eyes, past the near clip plane, and wide enough to fill the Quest 3's view at that distance.
        private const float DistanceMeters = 0.3f;
        private const float SizeMeters = 1.5f;

        private static readonly Color FlashColor = new Color(1.0f, 0.08f, 0.05f, 0.45f);
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private readonly Renderer _sheet;
        private readonly MaterialPropertyBlock _propertyBlock = new MaterialPropertyBlock();
        private float _startedAt = float.NegativeInfinity;

        /// <param name="eyes">The camera rig's center eye anchor; null draws nothing.</param>
        public HitFlash(Transform eyes)
        {
            var material = Resources.Load<Material>(MaterialResourcePath);
            if (eyes == null || material == null)
            {
                Debug.LogError($"[ArmedDummy] No eye anchor or material 'Resources/{MaterialResourcePath}': the hit flash is not drawn.");
                return;
            }

            var sheet = GameObject.CreatePrimitive(PrimitiveType.Quad);
            sheet.name = "Armed Dummy Hit Flash";
            UnityEngine.Object.Destroy(sheet.GetComponent<Collider>());
            sheet.transform.SetParent(eyes, false);
            sheet.transform.localPosition = new Vector3(0.0f, 0.0f, DistanceMeters);
            sheet.transform.localScale = Vector3.one * SizeMeters;
            _sheet = sheet.GetComponent<MeshRenderer>();
            _sheet.sharedMaterial = material;
            _sheet.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _sheet.receiveShadows = false;
            _sheet.enabled = false;
        }

        /// <summary>Starts the flash (again, if one is fading).</summary>
        public void Show()
        {
            _startedAt = Time.time;
            Fade();
        }

        /// <summary>Each frame: fades the flash and hides it when done.</summary>
        public void Update()
        {
            if (_sheet != null && _sheet.enabled)
            {
                Fade();
            }
        }

        public void Dispose()
        {
            if (_sheet != null)
            {
                UnityEngine.Object.Destroy(_sheet.gameObject);
            }
        }

        private void Fade()
        {
            if (_sheet == null)
            {
                return;
            }

            var fraction = (Time.time - _startedAt) / Seconds;
            if (fraction >= 1.0f)
            {
                _sheet.enabled = false;
                return;
            }

            var color = FlashColor;
            color.a *= 1.0f - Mathf.Clamp01(fraction);
            _propertyBlock.SetColor(BaseColorId, color);
            _sheet.SetPropertyBlock(_propertyBlock);
            _sheet.enabled = true;
        }
    }
}
