using System;
using UnityEngine;
using VrFencing.Settings;

namespace VrFencing.UI
{
    /// <summary>
    /// The small bar under a menu panel (PER-86): point the off-hand laser at it and hold the trigger to drag the panel
    /// anywhere; on release the panel is pinned there. The pin button beside it toggles pinned and following. The
    /// pointer drives the drag (<see cref="BeginDrag"/>, <see cref="Drag"/>, <see cref="EndDrag"/>); the pose math is
    /// <see cref="PanelDrag"/> in the UI Core, and <see cref="MenuBehaviour"/> decides what pinning means.
    /// </summary>
    internal sealed class PanelGrabBar : MonoBehaviour
    {
        public static readonly Vector2 BarSize = new Vector2(0.24f, 0.03f);
        public static readonly Vector2 PinButtonSize = new Vector2(0.13f, 0.04f);

        // Clear of the panel's bottom row of buttons, so a press meant for them never grabs the panel.
        private const float GapBelowPanelMeters = 0.025f;
        private const float GapBeforePinMeters = 0.02f;

        private Renderer _background;
        private UiButton _pinButton;
        private Action<PanelGrabBar> _released;
        private PanelDrag _drag;
        private bool _isHovered;

        /// <summary>The panel this bar moves (its parent).</summary>
        public Transform Panel { get; private set; }

        public bool IsDragging => _drag != null;

        /// <summary>The pin button beside the bar.</summary>
        public UiButton PinButton => _pinButton;

        /// <summary>A bar centered under a panel of <paramref name="panelSize"/>, with the pin button to its right.</summary>
        /// <param name="released">The drag ended; the panel stands where it should be pinned.</param>
        /// <param name="pinPressed">The pin button was pressed; carries this bar, whose panel shows where it stands.</param>
        public static PanelGrabBar Create(Transform panel, Vector2 panelSize, Action<PanelGrabBar> released, Action<PanelGrabBar> pinPressed)
        {
            var rowY = (-panelSize.y * 0.5f) - GapBelowPanelMeters - (PinButtonSize.y * 0.5f);
            var barObject = new GameObject("Grab Bar");
            barObject.transform.SetParent(panel, false);
            barObject.transform.localPosition = new Vector3(0.0f, rowY, 0.0f);

            // Taller than it looks, so the laser finds it easily.
            var collider = barObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.center = new Vector3(0.0f, 0.0f, -UiKit.LayerStepMeters);
            collider.size = new Vector3(BarSize.x, PinButtonSize.y, 0.01f);

            var bar = barObject.AddComponent<PanelGrabBar>();
            bar.Panel = panel;
            bar._released = released;
            bar._background = UiKit.CreateQuad(barObject.transform, "Background", Vector2.zero, BarSize, 1, UiKit.ButtonColor);

            var pinX = (BarSize.x * 0.5f) + GapBeforePinMeters + (PinButtonSize.x * 0.5f);
            bar._pinButton = UiButton.Create(panel, "Pin", new Vector2(pinX, rowY), PinButtonSize, UiKit.SmallFontSize, () => pinPressed(bar));
            bar.SetPinned(false);
            return bar;
        }

        /// <summary>Shows whether the panels are pinned: the pin button is lit while pinned.</summary>
        public void SetPinned(bool isPinned)
        {
            _pinButton.SetLabel(isPinned ? "Pinned" : "Pin");
            _pinButton.SetSelected(isPinned);
        }

        public void SetHovered(bool isHovered)
        {
            if (_isHovered == isHovered)
            {
                return;
            }

            _isHovered = isHovered;
            UpdateLook();
        }

        /// <summary>The trigger went down with the laser on the bar at <paramref name="grabPoint"/>.</summary>
        public void BeginDrag(Vector3 rayOrigin, Vector3 grabPoint)
        {
            _drag = new PanelDrag(rayOrigin.ToNumerics(), grabPoint.ToNumerics(), new TrackedPose(Panel.position.ToNumerics(), Panel.rotation.ToNumerics()));
            UpdateLook();
        }

        /// <summary>Moves the panel with the laser; returns the grabbed point, where the laser ends.</summary>
        public Vector3 Drag(Vector3 rayOrigin, Vector3 rayDirection, Vector3 headPosition, float deltaSeconds)
        {
            if (_drag == null)
            {
                return rayOrigin;
            }

            var pose = _drag.Move(rayOrigin.ToNumerics(), rayDirection.ToNumerics(), headPosition.ToNumerics(), deltaSeconds);
            Panel.SetPositionAndRotation(pose.Position.ToUnity(), pose.Rotation.ToUnity());
            return _drag.GrabPoint.ToUnity();
        }

        /// <summary>The trigger was released, or the panel went away mid-drag: the panel is pinned where it stands.</summary>
        public void EndDrag()
        {
            if (_drag == null)
            {
                return;
            }

            _drag = null;
            UpdateLook();
            _released?.Invoke(this);
        }

        private void UpdateLook()
        {
            var color = IsDragging ? UiKit.SelectedColor : _isHovered ? UiKit.HoverColor : UiKit.ButtonColor;
            UiKit.SetColor(_background, color);
        }
    }
}
