using UnityEngine;

namespace VrFencing.UI
{
    /// <summary>Marks a panel background, so the pointer's laser stops on it even where there is no button.</summary>
    internal sealed class UiSurface : MonoBehaviour
    {
        /// <summary>A panel background with a collider the laser can hit.</summary>
        public static Renderer Create(Transform parent, string name, Vector2 size)
        {
            var background = UiKit.CreateQuad(parent, name, Vector2.zero, size, 0, UiKit.PanelColor);
            var collider = background.gameObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(1.0f, 1.0f, 0.01f);
            background.gameObject.AddComponent<UiSurface>();
            return background;
        }
    }
}
