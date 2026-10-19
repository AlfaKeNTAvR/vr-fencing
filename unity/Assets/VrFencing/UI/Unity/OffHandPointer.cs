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
        /// <param name="trigger">The off-hand index trigger.</param>
        /// <param name="isActive">A panel is shown and the trigger is free for the menu.</param>
        public void Tick(Transform controller, OVRInput.RawButton trigger, bool isActive, double nowSeconds)
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
            var direction = controller.forward;
            var hitButton = FindTarget(origin, direction, out var end);
            SetHovered(hitButton != null && hitButton.IsInteractable ? hitButton : null);

            SetVisible(true);
            _laser.SetPosition(0, origin);
            _laser.SetPosition(1, end);
            _cursor.position = end;
            _cursor.gameObject.SetActive(end != origin + (direction * MaxLengthMeters));

            UpdateTrigger(isTriggerHeld, OVRInput.GetDown(trigger, OVRInput.Controller.Touch), nowSeconds);
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

        /// <summary>The nearest panel hit along the ray: a button, or a panel background that just stops the laser.</summary>
        private UiButton FindTarget(Vector3 origin, Vector3 direction, out Vector3 end)
        {
            // Panels move with the head every frame; colliders follow only after a sync.
            Physics.SyncTransforms();
            var hitCount = Physics.RaycastNonAlloc(origin, direction, _hits, MaxLengthMeters, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);

            end = origin + (direction * MaxLengthMeters);
            UiButton nearestButton = null;
            var nearestDistance = float.MaxValue;
            for (var i = 0; i < hitCount; i++)
            {
                var hit = _hits[i];
                if (hit.distance >= nearestDistance)
                {
                    continue;
                }

                var button = hit.collider.GetComponent<UiButton>();
                if (button == null && hit.collider.GetComponent<UiSurface>() == null)
                {
                    continue;
                }

                nearestDistance = hit.distance;
                nearestButton = button;
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

        private void Deactivate()
        {
            _wasActive = false;
            _held = null;
            SetHovered(null);
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
