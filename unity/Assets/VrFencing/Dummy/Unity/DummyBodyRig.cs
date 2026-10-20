using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VrFencing.Settings;

namespace VrFencing.Dummy
{
    /// <summary>
    /// The Dummy's rigged Humanoid body (PER-78): one instance of a Humanoid model (Movement SDK's
    /// <c>Armature.fbx</c>) posed in the Dummy's en garde, so its mesh wraps the Target Zones. The same rig is what a
    /// partner's tracked body will later drive.
    ///
    /// Posing: every mapped bone is put on its <see cref="DummySkeleton"/> joint and turned the least amount from its
    /// rest pose so it points at the next joint; the bones in between follow their parents, and the skinned mesh
    /// stretches to fit. The hips, chest and head take their whole frame (side and up) from the stance; the feet only
    /// turn about the vertical, so their soles stay flat on the floor as in the rest pose. All poses are in the parts
    /// root's frame, the Dummy frame.
    ///
    /// Side: the sword arm and the front leg are the anatomical right for a right-handed Dummy and the left for a
    /// left-handed one, so a mirrored Dummy is a real left-hander, not a right-hander turned inside out.
    ///
    /// Look (PER-41): the body is drawn as a white fencing suit, with the fencing mask (<c>FencingMask.fbx</c> from
    /// Resources, built by <c>tools/models/build_models.py</c>) hung on the head bone around the head's center, facing
    /// where the head faces. Every part is one <see cref="RigMeshSlot"/> colored by <see cref="GetPartColor"/> at the
    /// opacity <see cref="SetOpacity"/> gives, so the whole body stays translucent in passthrough.
    /// </summary>
    internal sealed class DummyBodyRig
    {
        /// <summary>
        /// How far below the head's center the rig's head joint sits, as a fraction of height. On the Armature rig
        /// the head joint is about 3.4 cm below the eyes at 1.8 m, at the base of the skull.
        /// </summary>
        private const float HeadJointBelowHeadCenterPerHeight = 0.02f;

        private const string MaskResourcePath = "FencingMask";

        /// <summary>The mask model is sized for a 1.65 m Dummy's head and scaled with the height.</summary>
        private const float MaskReferenceHeightMeters = 1.65f;

        private static readonly Color SuitColor = new Color(0.96f, 0.96f, 0.97f);
        private static readonly Color MaskColor = new Color(0.16f, 0.17f, 0.19f);
        private static readonly Color BibColor = new Color(0.97f, 0.97f, 0.97f);
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private static readonly HumanBodyBones[] RequiredBones =
        {
            HumanBodyBones.Hips,
            HumanBodyBones.Chest,
            HumanBodyBones.Head,
            HumanBodyBones.LeftUpperArm,
            HumanBodyBones.LeftLowerArm,
            HumanBodyBones.LeftHand,
            HumanBodyBones.RightUpperArm,
            HumanBodyBones.RightLowerArm,
            HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg,
            HumanBodyBones.LeftLowerLeg,
            HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg,
            HumanBodyBones.RightLowerLeg,
            HumanBodyBones.RightFoot,
        };

        // Optional in a Humanoid avatar; used when the model has them.
        private static readonly HumanBodyBones[] OptionalBones =
        {
            HumanBodyBones.UpperChest,
            HumanBodyBones.LeftMiddleProximal,
            HumanBodyBones.RightMiddleProximal,
        };

        private readonly Transform _partsRoot;
        private readonly Dictionary<HumanBodyBones, Transform> _bones;
        private readonly Dictionary<HumanBodyBones, Pose> _restPoses = new Dictionary<HumanBodyBones, Pose>();
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<RigMeshSlot> _meshSlots = new List<RigMeshSlot>();
        private readonly MaterialPropertyBlock _propertyBlock = new MaterialPropertyBlock();
        private Transform _mask;
        private float _heightMeters;

        private DummyBodyRig(GameObject instance, Transform partsRoot, Dictionary<HumanBodyBones, Transform> bones)
        {
            Instance = instance;
            _partsRoot = partsRoot;
            _bones = bones;
        }

        /// <summary>The model's instance under the parts root.</summary>
        public GameObject Instance { get; }

        /// <summary>The body's skinned meshes and the mask's meshes, all drawn with the Dummy material.</summary>
        public IReadOnlyList<Renderer> Renderers => _renderers;

        /// <summary>Every material slot of every mesh (the body's skinned meshes and the mask's), with the part it draws.</summary>
        public IReadOnlyList<RigMeshSlot> MeshSlots => _meshSlots;

        /// <summary>The mask's transform under the head bone, or null when the mask model is missing.</summary>
        public Transform Mask => _mask;

        /// <summary>The color a part is drawn in: a white suit, a dark wire mask with a white bib.</summary>
        public static Color GetPartColor(RigMeshPart part)
        {
            switch (part)
            {
                case RigMeshPart.Mask:
                    return MaskColor;
                case RigMeshPart.Bib:
                    return BibColor;
                default:
                    return SuitColor;
            }
        }

        /// <summary>Draws every part in its own color (<see cref="GetPartColor"/>) at <paramref name="opacity"/> (the Dummy look's body opacity).</summary>
        public void SetOpacity(float opacity)
        {
            foreach (var slot in _meshSlots)
            {
                var color = GetPartColor(slot.Part);
                _propertyBlock.SetColor(BaseColorId, new Color(color.r, color.g, color.b, Mathf.Clamp01(opacity)));
                slot.Renderer.SetPropertyBlock(_propertyBlock, slot.MaterialIndex);
            }
        }

        /// <summary>
        /// Instantiates <paramref name="model"/> under <paramref name="partsRoot"/>, draws it with
        /// <paramref name="material"/> and poses it on <paramref name="layout"/>. Returns null, after logging why, if
        /// the model is not a valid Humanoid with every bone the pose needs or has no skinned mesh; the caller then
        /// draws the capsule silhouette instead.
        /// </summary>
        public static DummyBodyRig TryCreate(GameObject model, Transform partsRoot, Material material, DummyLayout layout)
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

            // Bones are posed directly; an Animator without a controller has nothing to play. A later slice that
            // drives the body from tracking enables it again.
            var animator = instance.GetComponent<Animator>();
            animator.enabled = false;

            var rig = new DummyBodyRig(instance, partsRoot, bones);
            foreach (var skinnedRenderer in renderers)
            {
                // The model's own materials name its parts (Armature.fbx: M_Body, M_Arms, M_Legs); keep them before
                // every slot is given the Dummy material, so a part can be faded on its own (the opponent, PER-81).
                var modelMaterials = skinnedRenderer.sharedMaterials;
                for (var slot = 0; slot < modelMaterials.Length; slot++)
                {
                    rig._meshSlots.Add(new RigMeshSlot(skinnedRenderer, slot, RigMeshSlot.GetPart(modelMaterials[slot] != null ? modelMaterials[slot].name : skinnedRenderer.name)));
                }

                rig.DrawWith(skinnedRenderer, material);

                // The en garde pose reaches outside the rest pose's bounds; without this the Quest culls the body early.
                skinnedRenderer.updateWhenOffscreen = true;
            }

            rig.ScaleToHeight(layout.HeightMeters);
            rig.AttachMask(material);
            rig.Pose(layout);
            rig.SetOpacity(material.color.a);
            return rig;
        }

        /// <summary>
        /// The fencing mask under the head bone, every part drawn with the Dummy material (PER-41). Without the model,
        /// the body is drawn bare and an error is logged.
        /// </summary>
        private void AttachMask(Material material)
        {
            var model = Resources.Load<GameObject>(MaskResourcePath);
            if (model == null)
            {
                Debug.LogError($"[Dummy] Mask model 'Resources/{MaskResourcePath}' is missing; the Dummy has no mask.");
                return;
            }

            var mask = Object.Instantiate(model, GetBone(HumanBodyBones.Head), false);
            mask.name = "Mask";
            _mask = mask.transform;
            foreach (var maskRenderer in mask.GetComponentsInChildren<MeshRenderer>(true))
            {
                _meshSlots.Add(new RigMeshSlot(maskRenderer, 0, RigMeshSlot.GetPart(maskRenderer.name)));
                DrawWith(maskRenderer, material);
            }
        }

        private void DrawWith(Renderer partRenderer, Material material)
        {
            partRenderer.sharedMaterials = Enumerable.Repeat(material, Mathf.Max(1, partRenderer.sharedMaterials.Length)).ToArray();
            partRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            partRenderer.receiveShadows = false;
            _renderers.Add(partRenderer);
        }

        /// <summary>The mask around the head's center, facing where the head faces, sized for the Dummy's height.</summary>
        private void PlaceMask(Vector3 headCenterInPartsFrame, Quaternion headTurnInPartsFrame)
        {
            if (_mask == null)
            {
                return;
            }

            _mask.SetPositionAndRotation(_partsRoot.TransformPoint(headCenterInPartsFrame), _partsRoot.rotation * headTurnInPartsFrame);
            var parentScale = _mask.parent.lossyScale.x;
            _mask.localScale = Vector3.one * (parentScale > 1e-6f ? _heightMeters / MaskReferenceHeightMeters / parentScale : 1.0f);
        }

        /// <summary>Puts every mapped bone on its joint for <paramref name="layout"/>, e.g. after the sword arm moved.</summary>
        public void Pose(DummyLayout layout)
        {
            Vector3? Joint(SkeletonJoint joint) => DummySkeleton.GetJointPosition(layout, joint).ToUnity();

            // The back hand carries on the forearm's line.
            var backElbow = Joint(SkeletonJoint.BackElbow).Value;
            var backHand = Joint(SkeletonJoint.BackHand).Value;
            PoseJoints(layout.Hand, layout.HeightMeters, Joint, backHand + (backHand - backElbow), Quaternion.identity);
        }

        /// <summary>
        /// Puts every mapped bone on a tracked body's joints (PER-81, the opponent): <paramref name="getJoint"/> gives
        /// each <see cref="SkeletonJoint"/> in the parts root's frame, named by fencing side for a Fencer holding the
        /// blade in <paramref name="hand"/>, or null when not tracked. <paramref name="backHandEnd"/> is the back hand's
        /// end (the Dummy has none), <paramref name="headRotation"/> the headset's turn in the parts frame (+z where the
        /// Fencer looks). A part with a joint missing keeps its last pose.
        /// </summary>
        public void PoseTracked(Hand hand, float heightMeters, System.Func<SkeletonJoint, Vector3?> getJoint, Vector3? backHandEnd, Quaternion? headRotation)
        {
            if (getJoint == null)
            {
                throw new System.ArgumentNullException(nameof(getJoint));
            }

            PoseJoints(hand, heightMeters, getJoint, backHandEnd, headRotation);
        }

        private void PoseJoints(Hand hand, float heightMeters, System.Func<SkeletonJoint, Vector3?> getJoint, Vector3? backHandEnd, Quaternion? headRotation)
        {
            _heightMeters = heightMeters;
            var swordSide = hand == Hand.Left ? BodySide.Left : BodySide.Right;
            var backSide = swordSide == BodySide.Right ? BodySide.Left : BodySide.Right;

            var swordHip = getJoint(SkeletonJoint.FrontHip);
            var backHip = getJoint(SkeletonJoint.BackHip);
            var swordShoulder = getJoint(SkeletonJoint.SwordShoulder);
            var backShoulder = getJoint(SkeletonJoint.BackShoulder);
            var hipCenter = getJoint(SkeletonJoint.HipCenter);
            var shoulderCenter = getJoint(SkeletonJoint.ShoulderCenter);
            if (swordHip.HasValue && backHip.HasValue && swordShoulder.HasValue && backShoulder.HasValue && hipCenter.HasValue && shoulderCenter.HasValue)
            {
                // Anatomical right minus left: the sword side is the right for a right-handed Dummy.
                var hipsRight = swordSide == BodySide.Right ? swordHip.Value - backHip.Value : backHip.Value - swordHip.Value;
                var shouldersRight = swordSide == BodySide.Right ? swordShoulder.Value - backShoulder.Value : backShoulder.Value - swordShoulder.Value;
                PoseTorso(hipCenter.Value, shoulderCenter.Value, hipsRight, shouldersRight, shoulderCenter.Value - hipCenter.Value);
            }

            var headCenter = getJoint(SkeletonJoint.HeadCenter);
            if (headCenter.HasValue)
            {
                var headJoint = headCenter.Value + (Vector3.down * (HeadJointBelowHeadCenterPerHeight * heightMeters));

                // The head looks where the headset looks; the Dummy's looks at the Fencer: upright, facing the Dummy frame's +z.
                var turn = headRotation ?? Quaternion.identity;
                SetBone(HumanBodyBones.Head, headJoint, RotateFrame(HumanBodyBones.Head, RestShouldersRight(), RestSpineUp(), turn * Vector3.right, turn * Vector3.up));
                PlaceMask(headCenter.Value, turn);
            }

            PoseArm(swordSide, swordShoulder, getJoint(SkeletonJoint.SwordElbow), getJoint(SkeletonJoint.SwordWrist), getJoint(SkeletonJoint.SwordHandEnd));
            PoseArm(backSide, backShoulder, getJoint(SkeletonJoint.BackElbow), getJoint(SkeletonJoint.BackHand), backHandEnd);
            PoseLeg(swordSide, swordHip, getJoint(SkeletonJoint.FrontKnee), getJoint(SkeletonJoint.FrontAnkle), getJoint(SkeletonJoint.FrontToe));
            PoseLeg(backSide, backHip, getJoint(SkeletonJoint.BackKnee), getJoint(SkeletonJoint.BackAnkle), getJoint(SkeletonJoint.BackToe));
        }

        /// <summary>The bone's transform, or null if the model has no such bone.</summary>
        public Transform GetBone(HumanBodyBones bone)
        {
            return _bones.TryGetValue(bone, out var boneTransform) ? boneTransform : null;
        }

        private enum BodySide
        {
            Left,
            Right,
        }

        /// <summary>
        /// Scales the model so its rest-pose shoulders stand at a real shoulder height for the Dummy's height, then
        /// keeps every bone's rest pose in the Dummy frame for posing.
        /// </summary>
        private void ScaleToHeight(float heightMeters)
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

            foreach (var bone in _bones)
            {
                _restPoses[bone.Key] = new Pose(ToPartsFrame(bone.Value.position), Quaternion.Inverse(_partsRoot.rotation) * bone.Value.rotation);
            }
        }

        private void PoseTorso(Vector3 hipCenter, Vector3 shoulderCenter, Vector3 hipsRight, Vector3 shouldersRight, Vector3 spineUp)
        {
            // The hips bone sits a little above the line between the hip joints; it keeps that offset, turned with the hips.
            var hipsTurn = Frame(hipsRight, spineUp) * Quaternion.Inverse(Frame(RestHipsRight(), RestSpineUp()));
            var restHipJointCenter = (_restPoses[HumanBodyBones.LeftUpperLeg].position + _restPoses[HumanBodyBones.RightUpperLeg].position) * 0.5f;
            var hips = hipCenter + (hipsTurn * (_restPoses[HumanBodyBones.Hips].position - restHipJointCenter));
            SetBone(HumanBodyBones.Hips, hips, hipsTurn * _restPoses[HumanBodyBones.Hips].rotation);

            // The chest bones keep their rest place along the spine, between the hips bone and the shoulders.
            var restHipsHeight = _restPoses[HumanBodyBones.Hips].position.y;
            foreach (var chestBone in new[] { HumanBodyBones.Chest, HumanBodyBones.UpperChest })
            {
                if (GetBone(chestBone) == null)
                {
                    continue;
                }

                var fraction = (_restPoses[chestBone].position.y - restHipsHeight) / (RestShoulderCenter().y - restHipsHeight);
                SetBone(chestBone, Vector3.Lerp(hips, shoulderCenter, fraction), RotateFrame(chestBone, RestShouldersRight(), RestSpineUp(), shouldersRight, spineUp));
            }
        }

        private void PoseArm(BodySide side, Vector3? shoulderJoint, Vector3? elbowJoint, Vector3? wristJoint, Vector3? handEndJoint)
        {
            if (!shoulderJoint.HasValue || !elbowJoint.HasValue || !wristJoint.HasValue || !handEndJoint.HasValue)
            {
                return;
            }

            var shoulder = shoulderJoint.Value;
            var elbow = elbowJoint.Value;
            var wrist = wristJoint.Value;
            var handEnd = handEndJoint.Value;
            var upperArm = side == BodySide.Right ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm;
            var lowerArm = side == BodySide.Right ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm;
            var hand = side == BodySide.Right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand;
            var middleFinger = side == BodySide.Right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal;

            AimBone(upperArm, shoulder, elbow, RestDirection(upperArm, lowerArm));
            AimBone(lowerArm, elbow, wrist, RestDirection(lowerArm, hand));

            // Without finger bones the hand carries on the rest forearm's line.
            var restHandDirection = GetBone(middleFinger) != null ? RestDirection(hand, middleFinger) : RestDirection(lowerArm, hand);
            AimBone(hand, wrist, handEnd, restHandDirection);
        }

        private void PoseLeg(BodySide side, Vector3? hipJoint, Vector3? kneeJoint, Vector3? ankleJoint, Vector3? toeJoint)
        {
            if (!hipJoint.HasValue || !kneeJoint.HasValue || !ankleJoint.HasValue || !toeJoint.HasValue)
            {
                return;
            }

            var hip = hipJoint.Value;
            var knee = kneeJoint.Value;
            var ankle = ankleJoint.Value;
            var toe = toeJoint.Value;
            var upperLeg = side == BodySide.Right ? HumanBodyBones.RightUpperLeg : HumanBodyBones.LeftUpperLeg;
            var lowerLeg = side == BodySide.Right ? HumanBodyBones.RightLowerLeg : HumanBodyBones.LeftLowerLeg;
            var foot = side == BodySide.Right ? HumanBodyBones.RightFoot : HumanBodyBones.LeftFoot;

            // The rig's ankle stands at its own height over the sole, so the foot rests on the floor rather than in it.
            var footJoint = new Vector3(ankle.x, Mathf.Max(ankle.y, _restPoses[foot].position.y), ankle.z);

            AimBone(upperLeg, hip, knee, RestDirection(upperLeg, lowerLeg));
            AimBone(lowerLeg, knee, footJoint, RestDirection(lowerLeg, foot));

            // Turn the foot about the vertical only, from the body's rest forward to the heel-to-toe line.
            var turn = Quaternion.FromToRotation(Flatten(RestForward()), Flatten(toe - ankle));
            SetBone(foot, footJoint, turn * _restPoses[foot].rotation);
        }

        /// <summary>Puts the bone at <paramref name="position"/>, turned the least from rest to point at <paramref name="target"/>.</summary>
        private void AimBone(HumanBodyBones bone, Vector3 position, Vector3 target, Vector3 restDirection)
        {
            SetBone(bone, position, Quaternion.FromToRotation(restDirection, target - position) * _restPoses[bone].rotation);
        }

        /// <summary>The bone's rest rotation carried from the rest frame (side, up) to the target frame (side, up).</summary>
        private Quaternion RotateFrame(HumanBodyBones bone, Vector3 restRight, Vector3 restUp, Vector3 right, Vector3 up)
        {
            return Frame(right, up) * Quaternion.Inverse(Frame(restRight, restUp)) * _restPoses[bone].rotation;
        }

        private void SetBone(HumanBodyBones bone, Vector3 positionInPartsFrame, Quaternion rotationInPartsFrame)
        {
            _bones[bone].SetPositionAndRotation(_partsRoot.TransformPoint(positionInPartsFrame), _partsRoot.rotation * rotationInPartsFrame);
        }

        private Vector3 RestDirection(HumanBodyBones from, HumanBodyBones to)
        {
            return _restPoses[to].position - _restPoses[from].position;
        }

        private Vector3 RestHipsRight()
        {
            return RestDirection(HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg);
        }

        private Vector3 RestShouldersRight()
        {
            return RestDirection(HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm);
        }

        private Vector3 RestShoulderCenter()
        {
            return (_restPoses[HumanBodyBones.LeftUpperArm].position + _restPoses[HumanBodyBones.RightUpperArm].position) * 0.5f;
        }

        private Vector3 RestSpineUp()
        {
            return RestShoulderCenter() - _restPoses[HumanBodyBones.Hips].position;
        }

        private Vector3 RestForward()
        {
            return Vector3.Cross(RestHipsRight(), RestSpineUp());
        }

        private Vector3 ToPartsFrame(Vector3 worldPosition)
        {
            return _partsRoot.InverseTransformPoint(worldPosition);
        }

        /// <summary>The rotation whose +x is <paramref name="right"/> and whose +y is as near <paramref name="up"/> as it can be.</summary>
        private static Quaternion Frame(Vector3 right, Vector3 up)
        {
            return Quaternion.LookRotation(Vector3.Cross(right, up), up);
        }

        private static Vector3 Flatten(Vector3 direction)
        {
            return new Vector3(direction.x, 0.0f, direction.z);
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

            foreach (var bone in RequiredBones.Concat(OptionalBones))
            {
                var boneTransform = FindBone(animator, bone);
                if (boneTransform != null)
                {
                    bones[bone] = boneTransform;
                }
                else if (RequiredBones.Contains(bone))
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
