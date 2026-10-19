using UnityEngine;

namespace VrFencing.UI
{
    /// <summary>
    /// Keeps a panel at a comfortable spot in front of the Fencer: a fixed distance ahead and a little below eye level,
    /// turned to face the head. It follows the head's position smoothly, but only turns with the head once the Fencer
    /// looks more than a threshold away, so the panel holds still while the Fencer reads it or glances aside.
    /// </summary>
    internal sealed class PanelFollower
    {
        private const float FollowSharpness = 5.0f;

        private readonly float _distanceMeters;
        private readonly float _belowEyeMeters;
        private readonly float _sideMeters;
        private readonly float _turnThresholdDegrees;

        private float? _yawDegrees;
        private bool _hasPose;

        /// <param name="sideMeters">Offset to the off-hand side (left for a right-handed Fencer).</param>
        public PanelFollower(float distanceMeters, float belowEyeMeters, float sideMeters, float turnThresholdDegrees)
        {
            _distanceMeters = distanceMeters;
            _belowEyeMeters = belowEyeMeters;
            _sideMeters = sideMeters;
            _turnThresholdDegrees = turnThresholdDegrees;
        }

        /// <summary>Jumps straight in front of the head on the next tick, e.g. when the panel opens.</summary>
        public void Snap()
        {
            _yawDegrees = null;
            _hasPose = false;
        }

        /// <param name="isOffHandLeft">The off hand is the left one (a right-handed Fencer).</param>
        public void Tick(Transform panel, Transform head, float deltaSeconds, bool isOffHandLeft)
        {
            if (head == null)
            {
                return;
            }

            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f)
            {
                forward = Vector3.ProjectOnPlane(head.up, Vector3.up);
            }

            var headYaw = Quaternion.LookRotation(forward.normalized, Vector3.up).eulerAngles.y;
            if (!_yawDegrees.HasValue || Mathf.Abs(Mathf.DeltaAngle(headYaw, _yawDegrees.Value)) > _turnThresholdDegrees)
            {
                _yawDegrees = headYaw;
            }

            var side = isOffHandLeft ? -_sideMeters : _sideMeters;
            var offset = Quaternion.Euler(0.0f, _yawDegrees.Value, 0.0f) * new Vector3(side, 0.0f, _distanceMeters);
            var target = head.position + offset + (Vector3.down * _belowEyeMeters);
            var follow = _hasPose ? 1.0f - Mathf.Exp(-FollowSharpness * deltaSeconds) : 1.0f;
            panel.position = Vector3.Lerp(panel.position, target, follow);

            // Panel quads and text face -z, so the panel's forward points from the head through the panel.
            panel.rotation = Quaternion.LookRotation(panel.position - head.position, Vector3.up);
            _hasPose = true;
        }
    }
}
