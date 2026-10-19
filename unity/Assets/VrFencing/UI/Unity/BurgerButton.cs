using System;
using UnityEngine;

namespace VrFencing.UI
{
    /// <summary>
    /// The small burger button, low on the off-hand side next to the Running panel's spot: pointing at it and pulling
    /// the trigger folds the menu panels away or opens them again. It is drawn as three bars on a button (the font has
    /// no reliable burger glyph) and shows selected while the panels are open.
    /// </summary>
    internal sealed class BurgerButton
    {
        public static readonly Vector2 Size = new Vector2(0.07f, 0.07f);

        private const float BarWidthMeters = 0.038f;
        private const float BarHeightMeters = 0.006f;
        private const float BarSpacingMeters = 0.013f;

        private readonly UiButton _button;

        public BurgerButton(Transform root, Action toggle)
        {
            _button = UiButton.Create(root, "Burger", Vector2.zero, Size, UiKit.SmallFontSize, toggle);
            for (var bar = -1; bar <= 1; bar++)
            {
                UiKit.CreateQuad(_button.transform, $"Bar {bar + 2}", new Vector2(0.0f, bar * BarSpacingMeters),
                    new Vector2(BarWidthMeters, BarHeightMeters), 2, UiKit.TextColor);
            }
        }

        /// <summary>Selected (lit) while the panels are open, plain while they are folded away.</summary>
        public void Refresh(bool arePanelsOpen)
        {
            _button.SetSelected(arePanelsOpen);
        }
    }
}
