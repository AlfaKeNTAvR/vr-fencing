using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VrFencing.Settings;

namespace VrFencing.Dummy
{
    /// <summary>
    /// The Dummy's rigged Humanoid body (PER-78): one instance of a Humanoid model (Movement SDK's
    /// <c>Armature.fbx</c>) posed in the Dummy's en garde, so its mesh wraps the Target Zones. The same rig draws the
    /// opponent on a partner's tracked body (PER-81).
    ///
    /// Posing is the Core's <see cref="BodyRigPoser"/> (PER-142): every mapped bone is put on its joint, each limb bone
    /// aimed through its parent so the elbows and knees hinge in the joints' plane whichever way the body faces; the
    /// bones in between follow their parents, and the skinned mesh stretches to fit. This class reads the model's rest
    /// pose into the Core and copies each frame's result onto the bones. A rig built to fit lengths (the opponent) also
    /// scales the fitted bones, undoing each scale on the bones below. All poses are in the parts root's frame.
    ///
    /// Side: the sword arm and the front leg are the anatomical right for a right-handed Dummy and the left for a
    /// left-handed one, so a mirrored Dummy is a real left-hander, not a right-hander turned inside out.
    /// </summary>
    internal sealed class DummyBodyRig
    {
        private static readonly Dictionary<RigBone, HumanBodyBones> PosedBones = new Dictionary<RigBone, HumanBodyBones>
        {
            { RigBone.Hips, HumanBodyBones.Hips },
            { RigBone.Chest, HumanBodyBones.Chest },
            { RigBone.UpperChest, HumanBodyBones.UpperChest },
            { RigBone.Neck, HumanBodyBones.Neck },
            { RigBone.Head, HumanBodyBones.Head },
            { RigBone.LeftUpperArm, HumanBodyBones.LeftUpperArm },
            { RigBone.LeftLowerArm, HumanBodyBones.LeftLowerArm },
            { RigBone.LeftHand, HumanBodyBones.LeftHand },
            { RigBone.RightUpperArm, HumanBodyBones.RightUpperArm },
            { RigBone.RightLowerArm, HumanBodyBones.RightLowerArm },
            { RigBone.RightHand, HumanBodyBones.RightHand },
            { RigBone.LeftUpperLeg, HumanBodyBones.LeftUpperLeg },
            { RigBone.LeftLowerLeg, HumanBodyBones.LeftLowerLeg },
            { RigBone.LeftFoot, HumanBodyBones.LeftFoot },
            { RigBone.RightUpperLeg, HumanBodyBones.RightUpperLeg },
            { RigBone.RightLowerLeg, HumanBodyBones.RightLowerLeg },
            { RigBone.RightFoot, HumanBodyBones.RightFoot },
        };

        /// <summary>
        /// The Armature model's eye transforms: its Humanoid avatar maps no eyes, and the opponent's head is placed by them
        /// (PER-142).
        /// </summary>
        public const string LeftEyeName = "Left_Eye";

        /// <inheritdoc cref="LeftEyeName"/>
        public const string RightEyeName = "Right_Eye";

        // Optional in a Humanoid avatar; used when the model has them.
        private static readonly HumanBodyBones[] OptionalBones =
        {
            HumanBodyBones.UpperChest,
            HumanBodyBones.Neck,
            HumanBodyBones.LeftMiddleProximal,
            HumanBodyBones.RightMiddleProximal,
            HumanBodyBones.LeftIndexProximal,
            HumanBodyBones.RightIndexProximal,
            HumanBodyBones.LeftLittleProximal,
            HumanBodyBones.RightLittleProximal,
            HumanBodyBones.LeftEye,
            HumanBodyBones.RightEye,
        };

        private readonly Transform _partsRoot;
        private readonly Dictionary<HumanBodyBones, Transform> _bones;
        private readonly BodyRigTargets _targets = new BodyRigTargets();
        private readonly Transform[] _rigTransforms = new Transform[BodyRigRest.BoneCount];
        private readonly Vector3[] _restLocalScales = new Vector3[BodyRigRest.BoneCount];
        private readonly int[] _scaleParents = new int[BodyRigRest.BoneCount];
        private BodyRigPoser _poser;

        private DummyBodyRig(GameObject instance, Transform partsRoot, Dictionary<HumanBodyBones, Transform> bones, Renderer[] renderers)
        {
            Instance = instance;
            _partsRoot = partsRoot;
            _bones = bones;
            Renderers = renderers;
        }

        /// <summary>The model's instance under the parts root.</summary>
        public GameObject Instance { get; }

        /// <summary>The body's skinned meshes, all drawn with the Dummy material.</summary>
        public IReadOnlyList<Renderer> Renderers { get; }

        /// <summary>Every material slot of every skinned mesh, with the body part it draws.</summary>
        public IReadOnlyList<RigMeshSlot> MeshSlots { get; private set; } = new RigMeshSlot[0];

        /// <summary>
        /// Instantiates <paramref name="model"/> under <paramref name="partsRoot"/>, draws it with
        /// <paramref name="material"/> and poses it on <paramref name="layout"/>. <paramref name="fitsLengths"/>: a rig for a
        /// tracked body, whose bones are fitted to its lengths (the opponent). Returns null, after logging why, if the model
        /// is not a valid Humanoid with every bone the pose needs or has no skinned mesh; the caller then draws the capsule
        /// silhouette instead.
        /// </summary>
        public static DummyBodyRig TryCreate(GameObject model, Transform partsRoot, Material material, DummyLayout layout, bool fitsLengths = false)
        {
            var instance = Object.Instantiate(model, partsRoot, false);
            instance.name = "Body";
            instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;

            var problem = FindBones(instance, out var bones);
            var renderers = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (problem == null && renderers.Length == 0)
            {
                problem = "it has no skinned mesh";
            }

            if (problem != null)
            {
                Debug.LogError($"[Dummy] The body model '{model.name}' cannot be used: {problem}. Drawing the capsule silhouette instead.");
                DestroyObject(instance);
                return null;
            }

            // Bones are posed directly; an Animator without a controller has nothing to play.
            var animator = instance.GetComponent<Animator>();
            animator.enabled = false;

            var meshSlots = new List<RigMeshSlot>();
            foreach (var skinnedRenderer in renderers)
            {
                // The model's own materials name its parts (Armature.fbx: M_Body, M_Arms, M_Legs); keep them before
                // every slot is given the Dummy material, so a part can be faded on its own (the opponent, PER-81).
                var modelMaterials = skinnedRenderer.sharedMaterials;
                for (var slot = 0; slot < modelMaterials.Length; slot++)
                {
                    meshSlots.Add(new RigMeshSlot(skinnedRenderer, slot, RigMeshSlot.GetPart(modelMaterials[slot] != null ? modelMaterials[slot].name : skinnedRenderer.name)));
                }

                skinnedRenderer.sharedMaterials = Enumerable.Repeat(material, skinnedRenderer.sharedMaterials.Length).ToArray();
                skinnedRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                skinnedRenderer.receiveShadows = false;

                // The en garde pose reaches outside the rest pose's bounds; without this the Quest culls the body early.
                skinnedRenderer.updateWhenOffscreen = true;
            }

            var rig = new DummyBodyRig(instance, partsRoot, bones, renderers.Cast<Renderer>().ToArray()) { MeshSlots = meshSlots };
            rig.ScaleToHeight(layout.HeightMeters, fitsLengths);
            rig.Pose(layout);
            return rig;
        }

        /// <summary>Puts every mapped bone on its joint for <paramref name="layout"/>, e.g. after the sword arm moved.</summary>
        public void Pose(DummyLayout layout)
        {
            DummyRigTargets.Fill(_targets, layout);
            PoseTracked(_targets);
        }

        /// <summary>
        /// Puts every mapped bone on <paramref name="targets"/> (PER-81, the opponent; positions in the parts root's
        /// frame). A part with a joint missing keeps its last pose.
        /// </summary>
        public void PoseTracked(BodyRigTargets targets)
        {
            if (targets == null)
            {
                throw new System.ArgumentNullException(nameof(targets));
            }

            _poser.Pose(targets);
            for (var index = 0; index < BodyRigRest.BoneCount; index++)
            {
                var bone = (RigBone)index;
                var boneTransform = _rigTransforms[index];
                if (boneTransform == null || !_poser.IsPosed(bone))
                {
                    continue;
                }

                var pose = _poser.GetPose(bone);
                boneTransform.SetPositionAndRotation(_partsRoot.TransformPoint(pose.Position.ToUnity()), _partsRoot.rotation * pose.Rotation.ToUnity());
                if (_poser.FitsLengths)
                {
                    // Each scale is the bone's own: the nearest posed bone above it is undone.
                    var parent = _scaleParents[index];
                    var parentScale = parent >= 0 ? _poser.GetPose((RigBone)parent).Scale : 1.0f;
                    boneTransform.localScale = _restLocalScales[index] * (pose.Scale / parentScale);
                }
            }
        }

        /// <summary>The bone's transform, or null if the model has no such bone.</summary>
        public Transform GetBone(HumanBodyBones bone)
        {
            return _bones.TryGetValue(bone, out var boneTransform) ? boneTransform : null;
        }

        /// <summary>
        /// Scales the model so its rest-pose shoulders stand at a real shoulder height for the Dummy's height, then
        /// hands every bone's rest pose in the Dummy frame to the poser.
        /// </summary>
        private void ScaleToHeight(float heightMeters, bool fitsLengths)
        {
            var restShoulderHeight = (ToPartsFrame(GetBone(HumanBodyBones.LeftUpperArm).position).y + ToPartsFrame(GetBone(HumanBodyBones.RightUpperArm).position).y) * 0.5f;
            if (restShoulderHeight > 0.0f)
            {
                Instance.transform.localScale = Vector3.one * (DummySkeleton.StandingShoulderHeightPerHeight * heightMeters / restShoulderHeight);
            }
            else
            {
                Debug.LogWarning($"[Dummy] The body model's shoulders are not above its origin ({restShoulderHeight} m); drawing it unscaled.");
            }

            var rest = new BodyRigRest();
            foreach (var pair in PosedBones)
            {
                var boneTransform = GetBone(pair.Value);
                _rigTransforms[(int)pair.Key] = boneTransform;
                if (boneTransform != null)
                {
                    rest.Set(pair.Key, ToPartsFrame(boneTransform.position).ToNumerics(), (Quaternion.Inverse(_partsRoot.rotation) * boneTransform.rotation).ToNumerics());
                    _restLocalScales[(int)pair.Key] = boneTransform.localScale;
                }
            }

            rest.LeftMiddleFinger = GetRestPoint(HumanBodyBones.LeftMiddleProximal);
            rest.RightMiddleFinger = GetRestPoint(HumanBodyBones.RightMiddleProximal);
            rest.LeftPalm = GetRestPalm(true, rest.LeftMiddleFinger);
            rest.RightPalm = GetRestPalm(false, rest.RightMiddleFinger);
            var leftEye = GetRestPoint(HumanBodyBones.LeftEye) ?? GetNamedRestPoint(LeftEyeName);
            var rightEye = GetRestPoint(HumanBodyBones.RightEye) ?? GetNamedRestPoint(RightEyeName);
            if (!leftEye.HasValue || !rightEye.HasValue)
            {
                Debug.LogWarning("[Dummy] The body model has no eyes: a tracked head is placed by its head joint instead.");
            }
            rest.Eyes = leftEye.HasValue && rightEye.HasValue ? (leftEye.Value + rightEye.Value) * 0.5f : (System.Numerics.Vector3?)null;
            FindScaleParents();
            _poser = new BodyRigPoser(rest, fitsLengths);
        }

        /// <summary>The hand's rest palm from its knuckles, or null without the finger bones.</summary>
        private System.Numerics.Vector3? GetRestPalm(bool isLeft, System.Numerics.Vector3? middle)
        {
            var wrist = GetRestPoint(isLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            var index = GetRestPoint(isLeft ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
            var little = GetRestPoint(isLeft ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
            if (!wrist.HasValue || !middle.HasValue || !index.HasValue || !little.HasValue)
            {
                return null;
            }

            return BodyRigRest.GetPalm(middle.Value - wrist.Value, index.Value - little.Value, isLeft);
        }

        /// <summary>A rest point by its transform's name, for bones the Humanoid avatar does not map; null when absent.</summary>
        private System.Numerics.Vector3? GetNamedRestPoint(string transformName)
        {
            var found = Instance.GetComponentsInChildren<Transform>(true).FirstOrDefault(child => child.name == transformName);
            return found != null ? ToPartsFrame(found.position).ToNumerics() : (System.Numerics.Vector3?)null;
        }

        private System.Numerics.Vector3? GetRestPoint(HumanBodyBones bone)
        {
            var boneTransform = GetBone(bone);
            return boneTransform != null ? ToPartsFrame(boneTransform.position).ToNumerics() : (System.Numerics.Vector3?)null;
        }

        /// <summary>For each posed bone, the nearest posed bone above it in the hierarchy (-1 for none), whose scale it inherits.</summary>
        private void FindScaleParents()
        {
            for (var index = 0; index < BodyRigRest.BoneCount; index++)
            {
                _scaleParents[index] = -1;
                var boneTransform = _rigTransforms[index];
                for (var above = boneTransform != null ? boneTransform.parent : null; above != null && _scaleParents[index] < 0; above = above.parent)
                {
                    _scaleParents[index] = System.Array.IndexOf(_rigTransforms, above);
                }
            }
        }

        private Vector3 ToPartsFrame(Vector3 worldPosition)
        {
            return _partsRoot.InverseTransformPoint(worldPosition);
        }

        /// <summary>Finds every bone the pose needs through the model's Humanoid avatar; returns why not, or null.</summary>
        private static string FindBones(GameObject instance, out Dictionary<HumanBodyBones, Transform> bones)
        {
            bones = new Dictionary<HumanBodyBones, Transform>();
            var animator = instance.GetComponent<Animator>();
            if (animator == null || animator.avatar == null)
            {
                return "it has no Animator with an avatar";
            }

            if (!animator.avatar.isValid || !animator.avatar.isHuman)
            {
                return "its avatar is not a valid Humanoid (import it with Animation Type Humanoid)";
            }

            foreach (var bone in PosedBones.Values.Concat(OptionalBones).Distinct())
            {
                var boneTransform = FindBone(animator, bone);
                if (boneTransform != null)
                {
                    bones[bone] = boneTransform;
                }
                else if (!OptionalBones.Contains(bone))
                {
                    return $"its avatar maps no {bone} bone";
                }
            }

            return null;
        }

        /// <summary>
        /// The Animator's bone, or, if the Animator is not bound yet (Edit mode), the transform the avatar's
        /// description maps to that bone.
        /// </summary>
        private static Transform FindBone(Animator animator, HumanBodyBones bone)
        {
            var boneTransform = animator.GetBoneTransform(bone);
            if (boneTransform != null)
            {
                return boneTransform;
            }

            var humanName = HumanTrait.BoneName[(int)bone];
            var mapping = animator.avatar.humanDescription.human.FirstOrDefault(human => human.humanName == humanName);
            if (string.IsNullOrEmpty(mapping.boneName))
            {
                return null;
            }

            return animator.GetComponentsInChildren<Transform>(true).FirstOrDefault(child => child.name == mapping.boneName);
        }

        private static void DestroyObject(Object target)
        {
            // Edit-mode tests and editor tools build the Dummy outside Play mode, where Destroy is not allowed.
            if (Application.isPlaying)
            {
                Object.Destroy(target);
            }
            else
            {
                Object.DestroyImmediate(target);
            }
        }
    }
}
