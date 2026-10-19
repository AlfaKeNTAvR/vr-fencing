using UnityEngine;

namespace VrFencing.UI
{
    /// <summary>
    /// The menu pointer (PER-27): a laser from the off-hand controller, the hand that does not hold the blade, and its
    /// trigger to press. The caller passes the off-hand controller and trigger each frame, so it mirrors with the hand
    /// setting. It only runs while a panel is shown and the Strip is not being placed (placement owns the trigger then).
    ///
    /// A trigger that is already held when the pointer turns on (the press that just finished placing the Strip)
    /// must be released before it can press a button.
    ///
    /// Pressing the trigger on a panel's grab bar (PER-86) drags that panel until the trigger is released; while
    /// dragging, the laser ends at the grabbed point and presses nothing else.
    /// </summary>
    internal sealed class OffHandPointer
    {
        private const float MaxLengthMeters = 3.0f;
        private const float LaserWidthMeters = 0.004f;
        private const float CursorSizeMeters = 0.018f;
        private const float RepeatDelaySeconds = 0.45f;
        private const float RepeatIntervalSeconds = 0.1f;

        private readonly LineRenderer _laser;
        private readonly Transform _cursor;
        private readonly RaycastHit[] _hits = new RaycastHit[16];

        private UiButton _hovered;
        private UiButton _held;
        private PanelGrabBar _hoveredBar;
        private PanelGrabBar _dragged;
        private double _nextRepeatAtSeconds;
        private bool _wasActive;
        private bool _needsRelease;

        public OffHandPointer(Transform parent)
        {
            var laserObject = new GameObject("Off-hand Pointer");
            laserObject.transform.SetParent(parent, false);
            _laser = laserObject.AddComponent<LineRenderer>();
            _laser.sharedMaterial = UiKit.Material;
            _laser.positionCount = 2;
            _laser.useWorldSpace = true;
            _laser.widthMultiplier = LaserWidthMeters;
            _laser.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _laser.receiveShadows = false;
            UiKit.SetColor(_laser, UiKit.LaserColor);

            var cursor = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            cursor.name = "Pointer Cursor";
            UiKit.DestroyObject(cursor.GetComponent<Collider>());
            cursor.transform.SetParent(laserObject.transform, false);
            cursor.transform.localScale = Vector3.one * CursorSizeMeters;
            var cursorRenderer = cursor.GetComponent<MeshRenderer>();
            cursorRenderer.sharedMaterial = UiKit.Material;
            cursorRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            UiKit.SetColor(cursorRenderer, Color.white);
            _cursor = cursor.transform;

            SetVisible(false);
        }

        /// <param name="controller">The off-hand controller anchor, or null when it is not tracked.</param>
        /// <param name="head">The center eye, which a dragged panel turns to face; null when there is no camera rig.</param>
        /// <param name="trigger">The off-hand index trigger.</param>
        /// <param name="isActive">A panel is shown and the trigger is free for the menu.</param>
        /// <param name="tiltDegrees">The laser's tilt down (positive) from the controller's forward axis, the laser tilt Setting.</param>
        public void Tick(
            Transform controller, Transform head, OVRInput.RawButton trigger, bool isActive, float tiltDegrees, double nowSeconds, float deltaSeconds)
        {
            if (!isActive || controller == null)
            {
                Deactivate();
                return;
            }

            var isTriggerHeld = OVRInput.Get(trigger, OVRInput.Controller.Touch);
            if (!_wasActive)
            {
                _wasActive = true;
                _needsRelease = isTriggerHeld;
            }

            var origin = controller.position;
            // A positive turn about the controller's own x axis tips its forward down, the same for either hand.
            var direction = controller.rotation * Quaternion.Euler(tiltDegrees, 0.0f, 0.0f) * Vector3.forward;
            if (_dragged != null)
            {
                if (isTriggerHeld && head != null && _dragged.isActiveAndEnabled)
                {
                    DrawLaser(origin, _dragged.Drag(origin, direction, head.position, deltaSeconds), showsCursor: true);
                    return;
                }

                EndDrag();
            }

            var hitButton = FindTarget(origin, direction, out var hitBar, out var end);
            SetHovered(hitButton != null && hitButton.IsInteractable ? hitButton : null);
            SetHoveredBar(hitBar);
            DrawLaser(origin, end, showsCursor: end != origin + (direction * MaxLengthMeters));

            var isPressedNow = OVRInput.GetDown(trigger, OVRInput.Controller.Touch);
            if (!_needsRelease && isPressedNow && _hoveredBar != null && head != null)
            {
                _dragged = _hoveredBar;
                _held = null;
                _dragged.BeginDrag(origin, end);
                return;
            }

            UpdateTrigger(isTriggerHeld, isPressedNow, nowSeconds);
        }

        private void DrawLaser(Vector3 origin, Vector3 end, bool showsCursor)
        {
            SetVisible(true);
            _laser.SetPosition(0, origin);
            _laser.SetPosition(1, end);
            _cursor.position = end;
            _cursor.gameObject.SetActive(showsCursor);
        }

        private void UpdateTrigger(bool isHeld, bool isPressedNow, double nowSeconds)
        {
            if (_needsRelease)
            {
                _needsRelease = isHeld;
                return;
            }

            if (!isHeld)
            {
                _held = null;
                return;
            }

            if (isPressedNow && _hovered != null)
            {
                _held = _hovered;
                _nextRepeatAtSeconds = nowSeconds + RepeatDelaySeconds;
                _held.Press();
                return;
            }

            if (_held != null && _held == _hovered && _held.RepeatsWhileHeld && nowSeconds >= _nextRepeatAtSeconds)
            {
                _nextRepeatAtSeconds = nowSeconds + RepeatIntervalSeconds;
                _held.Press();
            }
        }

        /// <summary>
        /// The nearest panel hit along the ray: a button, a grab bar (<paramref name="nearestBar"/>), or a panel
        /// background that just stops the laser.
        /// </summary>
        private UiButton FindTarget(Vector3 origin, Vector3 direction, out PanelGrabBar nearestBar, out Vector3 end)
        {
            // Panels move with the head every frame; colliders follow only after a sync.
            Physics.SyncTransforms();
            var hitCount = Physics.RaycastNonAlloc(origin, direction, _hits, MaxLengthMeters, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);

            end = origin + (direction * MaxLengthMeters);
            UiButton nearestButton = null;
            nearestBar = null;
            var nearestDistance = float.MaxValue;
            for (var i = 0; i < hitCount; i++)
            {
                var hit = _hits[i];
                if (hit.distance >= nearestDistance)
                {
                    continue;
                }

                var button = hit.collider.GetComponent<UiButton>();
                var bar = button == null ? hit.collider.GetComponent<PanelGrabBar>() : null;
                if (button == null && bar == null && hit.collider.GetComponent<UiSurface>() == null)
                {
                    continue;
                }

                nearestDistance = hit.distance;
                nearestButton = button;
                nearestBar = bar;
                end = hit.point;
            }

            return nearestButton;
        }

        private void SetHovered(UiButton button)
        {
            if (_hovered == button)
            {
                return;
            }

            if (_hovered != null)
            {
                _hovered.SetHovered(false);
            }

            _hovered = button;
            if (_hovered != null)
            {
                _hovered.SetHovered(true);
            }
        }

        private void SetHoveredBar(PanelGrabBar bar)
        {
            if (_hoveredBar == bar)
            {
                return;
            }

            if (_hoveredBar != null)
            {
                _hoveredBar.SetHovered(false);
            }

            _hoveredBar = bar;
            if (_hoveredBar != null)
            {
                _hoveredBar.SetHovered(true);
            }
        }

        /// <summary>Ends a drag, which pins the panel where it stands.</summary>
        private void EndDrag()
        {
            var dragged = _dragged;
            _dragged = null;
            if (dragged != null)
            {
                dragged.EndDrag();
            }
        }

        private void Deactivate()
        {
            EndDrag();
            _wasActive = false;
            _held = null;
            SetHovered(null);
            SetHoveredBar(null);
            SetVisible(false);
        }

        private void SetVisible(bool isVisible)
        {
            if (_laser.gameObject.activeSelf != isVisible)
            {
                _laser.gameObject.SetActive(isVisible);
            }
        }
    }
}
