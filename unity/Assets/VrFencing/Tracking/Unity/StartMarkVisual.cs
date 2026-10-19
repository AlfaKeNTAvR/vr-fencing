using UnityEngine;

namespace VrFencing.Tracking
{
    /// <summary>
    /// Draws the Start Mark (PER-89): a foot-sized pad on the floor at the center of the En Garde Line, where the front
    /// foot goes between Reps. Built under the Strip frame (the En Garde Line transform), so it moves with the Strip.
    /// Shown only while a Drill that waits for the Fencer there runs; brighter while the run asks the Fencer back.
    /// </summary>
    internal sealed class StartMarkVisual
    {
        // The Strip's opaque line material (URP); the default primitive material renders magenta on the Quest.
        private const string MaterialResourcePath = "VrFencingStripLine";

        // About a shoe: 12 cm wide, 28 cm long, centered on the line. It lies 1 cm up, above the Strip's lines (4-9 mm).
        private static readonly Vector3 PadSize = new Vector3(0.12f, 0.004f, 0.28f);
        private const float PadHeightMeters = 0.01f;

        private static readonly Color MarkColor = new Color(0.2f, 0.75f, 1.0f, 1.0f);
        private static readonly Color PromptColor = new Color(1.0f, 0.85f, 0.2f, 1.0f);
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private readonly MeshRenderer _pad;
        private readonly MaterialPropertyBlock _propertyBlock = new MaterialPropertyBlock();

        /// <param name="enGardeLine">The Strip frame.</param>
        public StartMarkVisual(Transform enGardeLine)
        {
            var material = Resources.Load<Material>(MaterialResourcePath);
            if (material == null)
            {
                Debug.LogError($"[Tracking] Material 'Resources/{MaterialResourcePath}' is missing; the Start Mark is not drawn.");
                return;
            }

            var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = "Start Mark";
            Object.Destroy(pad.GetComponent<Collider>());
            pad.transform.SetParent(enGardeLine, false);
            pad.transform.localPosition = new Vector3(0.0f, PadHeightMeters, StartMark.FrontFootZ);
            pad.transform.localScale = PadSize;

            _pad = pad.GetComponent<MeshRenderer>();
            _pad.sharedMaterial = material;
            _pad.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _pad.receiveShadows = false;
            Show(false);
        }

        public void Show(bool isVisible)
        {
            if (_pad != null)
            {
                _pad.gameObject.SetActive(isVisible);
                SetPrompting(false);
            }
        }

        /// <summary>Lights the mark up while the run asks the Fencer back to it.</summary>
        public void SetPrompting(bool isPrompting)
        {
            if (_pad == null)
            {
                return;
            }

            _propertyBlock.SetColor(BaseColorId, isPrompting ? PromptColor : MarkColor);
            _pad.SetPropertyBlock(_propertyBlock);
        }
    }
}
