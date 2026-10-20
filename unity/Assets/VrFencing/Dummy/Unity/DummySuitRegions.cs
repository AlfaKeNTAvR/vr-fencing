using UnityEngine;

namespace VrFencing.Dummy
{
    /// <summary>
    /// Keeps the fencing suit's regions on the rig (PER-41): the shoes on the feet, the glove on the sword hand and the
    /// dark head under the mask. The rigged body is one white mesh whose data cannot be read on the Quest (the model is
    /// imported not readable), so the suit shader colours these regions by position instead: spheres and capsules in
    /// world space, taken from the bones every frame after they are posed. The rig owns the material instance it sets
    /// them on, and this destroys it with the rig.
    /// </summary>
    public sealed class DummySuitRegions : MonoBehaviour
    {
        // Sizes for a 1.8 m Dummy, scaled with its height.
        private const float ReferenceHeightMeters = 1.8f;
        private const float HeadRadiusMeters = 0.15f;
        private const float GloveRadiusMeters = 0.052f;
        private const float GauntletAlongForearm = 0.3f;
        private const float HandLengthMeters = 0.1f;
        private const float ShoeRadiusMeters = 0.062f;
        private const float HeelBehindAnkleMeters = 0.045f;
        private const float ShoeAheadOfToeMeters = 0.05f;
        private const float ToeAheadOfAnkleMeters = 0.15f;
        private const float ShoeTopAboveAnkleMeters = 0.022f;

        private static readonly int HeadSphereId = Shader.PropertyToID("_HeadSphere");
        private static readonly int GloveStartId = Shader.PropertyToID("_GloveStart");
        private static readonly int GloveEndId = Shader.PropertyToID("_GloveEnd");
        private static readonly int ShoeAStartId = Shader.PropertyToID("_ShoeAStart");
        private static readonly int ShoeAEndId = Shader.PropertyToID("_ShoeAEnd");
        private static readonly int ShoeBStartId = Shader.PropertyToID("_ShoeBStart");
        private static readonly int ShoeBEndId = Shader.PropertyToID("_ShoeBEnd");

        private Material _material;
        private Transform _headCenter;
        private Transform _swordElbow;
        private Transform _swordWrist;
        private Transform _swordFinger;
        private Transform _frontFoot;
        private Transform _frontToes;
        private Transform _backFoot;
        private Transform _backToes;
        private float _scale = 1.0f;

        /// <summary>The material instance the regions are set on (the body's).</summary>
        public Material Material => _material;

        /// <summary>
        /// Sets what to follow: <paramref name="headCenter"/> (the mask's centre), the sword arm's lower arm, hand and
        /// a finger (optional), and each foot with its toes (optional). Takes ownership of <paramref name="material"/>.
        /// </summary>
        public void Configure(
            Material material, float heightMeters, Transform headCenter, Transform swordElbow, Transform swordWrist, Transform swordFinger,
            Transform frontFoot, Transform frontToes, Transform backFoot, Transform backToes)
        {
            _material = material;
            _scale = heightMeters / ReferenceHeightMeters;
            _headCenter = headCenter;
            _swordElbow = swordElbow;
            _swordWrist = swordWrist;
            _swordFinger = swordFinger;
            _frontFoot = frontFoot;
            _frontToes = frontToes;
            _backFoot = backFoot;
            _backToes = backToes;
            Refresh();
        }

        /// <summary>Puts the regions where the bones are now. Runs every frame in Play mode; editor tools call it after posing.</summary>
        public void Refresh()
        {
            if (_material == null)
            {
                return;
            }

            if (_headCenter != null)
            {
                _material.SetVector(HeadSphereId, WithW(_headCenter.position, HeadRadiusMeters * _scale));
            }

            if (_swordElbow != null && _swordWrist != null)
            {
                var wrist = _swordWrist.position;
                var forearm = wrist - _swordElbow.position;
                var handEnd = _swordFinger != null
                    ? _swordFinger.position + ((_swordFinger.position - wrist).normalized * (HandLengthMeters * 0.4f * _scale))
                    : wrist + (forearm.normalized * (HandLengthMeters * _scale));
                _material.SetVector(GloveStartId, WithW(wrist - (forearm * GauntletAlongForearm), GloveRadiusMeters * _scale));
                _material.SetVector(GloveEndId, WithW(handEnd, 0.0f));
            }

            SetShoe(ShoeAStartId, ShoeAEndId, _frontFoot, _frontToes);
            SetShoe(ShoeBStartId, ShoeBEndId, _backFoot, _backToes);
        }

        private void LateUpdate()
        {
            Refresh();
        }

        private void OnDestroy()
        {
            if (_material != null)
            {
                Destroy(_material);
            }
        }

        /// <summary>A shoe from behind the heel to past the toes, cut off just above the ankle (w of the end).</summary>
        private void SetShoe(int startId, int endId, Transform foot, Transform toes)
        {
            if (foot == null)
            {
                return;
            }

            var ankle = foot.position;
            var toward = toes != null ? toes.position - ankle : foot.forward * (ToeAheadOfAnkleMeters * _scale);
            toward.y = 0.0f;
            var forward = toward.sqrMagnitude > 1e-8f ? toward.normalized : Vector3.forward;
            var toe = toes != null ? toes.position : ankle + (forward * (ToeAheadOfAnkleMeters * _scale));
            var sole = Mathf.Min(ankle.y, toe.y);
            var start = new Vector3(ankle.x, sole, ankle.z) - (forward * (HeelBehindAnkleMeters * _scale));
            var end = new Vector3(toe.x, sole, toe.z) + (forward * (ShoeAheadOfToeMeters * _scale));
            _material.SetVector(startId, WithW(start, ShoeRadiusMeters * _scale));
            _material.SetVector(endId, WithW(end, ankle.y + (ShoeTopAboveAnkleMeters * _scale)));
        }

        private static Vector4 WithW(Vector3 xyz, float w)
        {
            return new Vector4(xyz.x, xyz.y, xyz.z, w);
        }
    }
}
