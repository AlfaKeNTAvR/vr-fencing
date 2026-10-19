using TMPro;
using UnityEngine;

namespace VrFencing.UI
{
    /// <summary>
    /// The thrust calibration's target (PER-52): a dot where the Fencer thrusts, orange until the arm points at it, green
    /// while the hold counts, with the prompt just above it, turned toward the head. Only the current target shows.
    /// </summary>
    internal sealed class CalibrationTargetView
    {
        private const float DotDiameterMeters = 0.06f;
        private const float LabelAboveMeters = 0.12f;

        // Panel fonts are sized for about 1.1 m; the targets stand about 2 m away.
        private const float LabelScale = 1.8f;

        private static readonly Color WaitingColor = new Color(1.0f, 0.6f, 0.15f, 1.0f);
        private static readonly Color AimedColor = new Color(0.2f, 0.95f, 0.35f, 1.0f);

        private readonly GameObject _root;
        private readonly Renderer _dot;
        private readonly Transform _label;
        private readonly TextMeshPro _labelText;

        public CalibrationTargetView(Transform parent)
        {
            _root = new GameObject("Calibration Target");
            _root.transform.SetParent(parent, false);

            var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dot.name = "Dot";
            UiKit.DestroyObject(dot.GetComponent<Collider>());
            dot.transform.SetParent(_root.transform, false);
            dot.transform.localScale = Vector3.one * DotDiameterMeters;
            _dot = dot.GetComponent<MeshRenderer>();
            _dot.sharedMaterial = UiKit.Material;
            _dot.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _dot.receiveShadows = false;

            _label = new GameObject("Label").transform;
            _label.SetParent(_root.transform, false);
            _label.localScale = Vector3.one * LabelScale;
            _labelText = UiKit.CreateText(_label, "Prompt", Vector2.zero, new Vector2(0.5f, 0.12f), 0, UiKit.BodyFontSize,
                TextAlignmentOptions.Center, UiKit.TextColor);
            _root.SetActive(false);
        }

        public void Hide()
        {
            _root.SetActive(false);
        }

        public void Show(Vector3 target, Vector3 headPosition, bool isAimed, string prompt)
        {
            _root.SetActive(true);
            _dot.transform.position = target;
            UiKit.SetColor(_dot, isAimed ? AimedColor : WaitingColor);
            _label.position = target + (Vector3.up * LabelAboveMeters);
            var away = _label.position - headPosition;
            if (away.sqrMagnitude > 1e-6f)
            {
                _label.rotation = Quaternion.LookRotation(away, Vector3.up);
            }

            UiKit.SetText(_labelText, prompt);
        }
    }
}
