using System.Collections.Generic;
using UnityEngine;
using VrFencing.Blade;
using VrFencing.Dummy;
using VrFencing.Settings;
using VrFencing.Strip;

namespace VrFencing.Partner
{
    /// <summary>
    /// Draws the Opponent, the partner as shown in this Fencer's room (PER-81, decided in PER-59), from
    /// <see cref="OpponentLinkBehaviour.Link"/>:
    /// <list type="bullet">
    /// <item>the body: the Dummy's rigged Humanoid (PER-78) in the Dummy's translucent look, posed on the partner's tracked
    /// joints, built for their height and sword hand. Parts the partner's tracker is unsure about fade
    /// (<see cref="OpponentFade"/>): the generated legs always, more at low fidelity; the whole body is a ghost while
    /// it is not coming;</item>
    /// <item>the blade: built from the partner's hand and grip settings and drawn with the same droop, flex and bend code as
    /// this Fencer's own (<see cref="DrawnBlade"/>), bending against this Fencer's body, the floor and this Fencer's
    /// blade. Cosmetic only: no Touch is judged here.</item>
    /// </list>
    /// It also hands the opponent's body capsules to this Fencer's blade, so it bends on the opponent as on the Dummy.
    /// Everything is placed in this Fencer's Strip frame (the room's origin when no Strip is placed).
    /// </summary>
    // After the link has read and received this frame.
    [DefaultExecutionOrder(110)]
    public sealed class OpponentViewBehaviour : MonoBehaviour
    {
        private const string LogTag = "[Opponent]";
        private const string MaterialResourcePath = "VrFencingDummy";

        /// <summary>This Fencer's blade as an obstacle for the opponent's: about the blade's half-width at the forte.</summary>
        private const float MyBladeRadiusMeters = 0.005f;

        // The Dummy's silhouette color (DummyBehaviour), so the opponent reads as the same kind of figure.
        private static readonly Color SilhouetteColor = new Color(0.85f, 0.9f, 1.0f);
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField]
        [Tooltip("The partner streams. Found on this object or in the scene if empty.")]
        private OpponentLinkBehaviour _link;

        [SerializeField]
        [Tooltip("This Fencer's Strip: its En Garde Line is the frame the opponent is drawn in. Found in the scene if empty.")]
        private StripBehaviour _strip;

        [SerializeField]
        [Tooltip("This Fencer's blade: the opponent's blade bends against it, and it bends on the opponent. Found in the scene if empty.")]
        private BladeBehaviour _myBlade;

        [SerializeField]
        [Tooltip("Humanoid model drawn as the opponent's body (the Dummy's: Movement SDK's Armature.fbx). Empty: no body, only the blade.")]
        private GameObject _bodyModel;

        private readonly List<Capsule> _capsules = new List<Capsule>(FencerBodyShape.CapsuleCount);
        private readonly List<BladeObstacle> _opponentBodyObstacles = new List<BladeObstacle>(FencerBodyShape.CapsuleCount);
        private readonly List<BladeObstacle> _opponentBladeObstacles = new List<BladeObstacle>(FencerBodyShape.CapsuleCount + 2);
        private readonly DrawnBlade _drawnBlade = new DrawnBlade();
        private readonly Dictionary<OpponentBodyPart, float> _fades = new Dictionary<OpponentBodyPart, float>();

        private Transform _root;
        private Transform _bladeMount;
        private BladeVisual _bladeVisual;
        private DummyBodyRig _rig;
        private FencerInfo? _rigBuiltFor;
        private MaterialPropertyBlock _propertyBlock;
        private TrainerSettings _settings;
        private BladeFlexSoftness? _appliedFlex;
        private bool _isShown;
        private bool _isBladeShown;

        /// <summary>The body model, for a scene built without one (the PC Editor's Partner window sets it).</summary>
        public GameObject BodyModel
        {
            get => _bodyModel;
            set => _bodyModel = value;
        }

        /// <summary>True while the opponent is drawn.</summary>
        public bool IsShown => _isShown;

        /// <summary>Takes changed Settings: the body's opacity, and this Fencer's height for their own capsules.</summary>
        public void ApplySettings(TrainerSettings settings)
        {
            _settings = settings.Clone();
            _link?.ApplySettings(_settings);
        }

        private void Awake()
        {
            _link = _link != null ? _link : GetComponent<OpponentLinkBehaviour>();
            _link = _link != null ? _link : FindFirstObjectByType<OpponentLinkBehaviour>();
            _strip = _strip != null ? _strip : FindFirstObjectByType<StripBehaviour>();
            _myBlade = _myBlade != null ? _myBlade : FindFirstObjectByType<BladeBehaviour>();
            _settings = SettingsFile.Load();
            _propertyBlock = new MaterialPropertyBlock();

            _root = new GameObject("Opponent").transform;
            _bladeMount = new GameObject("Opponent Blade Mount").transform;
            _bladeMount.SetParent(_root, false);
            _bladeVisual = new BladeVisual(_bladeMount);
            _bladeVisual.SetVisible(false);
            _root.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            if (_strip != null)
            {
                _strip.SettingsChanged += ApplySettings;
            }
        }

        private void OnDisable()
        {
            if (_strip != null)
            {
                _strip.SettingsChanged -= ApplySettings;
            }
        }

        private void OnDestroy()
        {
            _bladeVisual?.Dispose();
            if (_root != null)
            {
                Destroy(_root.gameObject);
            }
        }

        private void LateUpdate()
        {
            var view = _link != null && _link.IsConnected ? _link.Link.Sample(Time.realtimeSinceStartupAsDouble) : null;
            SetShown(view != null);
            if (view == null)
            {
                return;
            }

            var stripInRoom = PlaceRoot();
            DrawBody(view, Time.deltaTime);
            DrawBlade(view, stripInRoom, Time.deltaTime);
        }

        private void SetShown(bool isShown)
        {
            if (isShown == _isShown)
            {
                return;
            }

            _isShown = isShown;
            _root.gameObject.SetActive(isShown);
            if (_myBlade != null)
            {
                // Filled below every frame; parked capsules until the first body.
                if (isShown)
                {
                    FencerBodyShape.GetCapsules(null, 1.0f, _capsules);
                    _opponentBodyObstacles.Clear();
                    foreach (var capsule in _capsules)
                    {
                        _opponentBodyObstacles.Add(BladeObstacle.FromCapsule(capsule));
                    }
                }

                _myBlade.SetExtraObstacles(isShown ? _opponentBodyObstacles : null);
            }
            _drawnBlade.Reset();
            _fades.Clear();
            Debug.Log(isShown ? $"{LogTag} The opponent is shown." : $"{LogTag} The opponent is hidden.");
        }

        /// <summary>Puts the opponent's root on this Fencer's Strip frame; returns that frame's pose in the room.</summary>
        private TrackedPose PlaceRoot()
        {
            var enGardeLine = _strip != null && _strip.IsPlaced ? _strip.EnGardeLine : null;
            var pose = enGardeLine != null ? new Pose(enGardeLine.position, enGardeLine.rotation) : Pose.identity;
            _root.SetPositionAndRotation(pose.position, pose.rotation);
            return pose.ToTrackedPose();
        }

        private void DrawBody(OpponentView view, float deltaSeconds)
        {
            FencerBodyShape.GetCapsules(view.Body, view.Info.HeightMeters, _capsules);
            _opponentBodyObstacles.Clear();
            foreach (var capsule in _capsules)
            {
                _opponentBodyObstacles.Add(BladeObstacle.FromCapsule(capsule));
            }

            if (view.Body == null || !EnsureRig(view.Info))
            {
                return;
            }

            var body = view.Body;
            _rig.PoseTracked(
                view.Info.Hand,
                view.Info.HeightMeters,
                joint => body.Get(joint) is System.Numerics.Vector3 position ? position.ToUnity() : (Vector3?)null,
                body.BackHandEnd is System.Numerics.Vector3 backHandEnd ? backHandEnd.ToUnity() : (Vector3?)null,
                body.HeadRotation is System.Numerics.Quaternion head ? head.ToUnity() : (Quaternion?)null);

            foreach (var slot in _rig.MeshSlots)
            {
                var part = ToOpponentPart(slot.Part);
                var target = OpponentFade.GetTargetFactor(part, body.Fidelity, view.IsBodyStale);
                var factor = _fades.TryGetValue(part, out var current) ? OpponentFade.StepToward(current, target, deltaSeconds) : target;
                _fades[part] = factor;
                _propertyBlock.SetColor(BaseColorId, new Color(SilhouetteColor.r, SilhouetteColor.g, SilhouetteColor.b, Mathf.Clamp01(_settings.DummyLook.BodyOpacity * factor)));
                slot.Renderer.SetPropertyBlock(_propertyBlock, slot.MaterialIndex);
            }
        }

        /// <summary>The rig for the partner's height and hand, built again when either changes; false when it cannot be built.</summary>
        private bool EnsureRig(FencerInfo info)
        {
            if (_rigBuiltFor.HasValue && _rigBuiltFor.Value.HeightMeters == info.HeightMeters && _rigBuiltFor.Value.Hand == info.Hand)
            {
                return _rig != null;
            }

            if (_rig != null)
            {
                Destroy(_rig.Instance);
                _rig = null;
            }

            _rigBuiltFor = info;
            var material = Resources.Load<Material>(MaterialResourcePath);
            if (_bodyModel == null || material == null)
            {
                Debug.LogWarning($"{LogTag} No body model or material ('Resources/{MaterialResourcePath}'); only the opponent's blade is drawn.");
                return false;
            }

            _rig = DummyBodyRig.TryCreate(_bodyModel, _root, material, DummyLayout.Create(info.HeightMeters, info.Hand));
            if (_rig != null)
            {
                _rig.Instance.name = "Opponent Body";
                Debug.Log($"{LogTag} Opponent body built for a {info.HeightMeters:0.00} m {info.Hand}-handed Fencer.");
            }

            return _rig != null;
        }

        private void DrawBlade(OpponentView view, TrackedPose stripInRoom, float deltaSeconds)
        {
            var isBladeShown = view.Controller.HasValue && view.Grip.HasValue;
            if (isBladeShown != _isBladeShown)
            {
                _isBladeShown = isBladeShown;
                _bladeVisual.SetVisible(isBladeShown);
                _drawnBlade.Reset();
            }

            if (!isBladeShown)
            {
                return;
            }

            var grip = view.Grip.Value;
            var blade = grip.ToBladeSettings();
            if (_appliedFlex != grip.Flex)
            {
                _drawnBlade.Flex.SetSoftness(grip.Flex);
                _appliedFlex = grip.Flex;
            }

            // The same blade as the partner's own: their grip on their controller, hanging by their set and flex.
            var controller = view.Controller.Value;
            var mountInController = BladePose.GetMountInController(blade, grip.Hand);
            var forte = BladePose.GetBlade(controller, mountInController);
            var bladeRotation = controller.Rotation * mountInController.Rotation;
            var gripUp = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitY, bladeRotation);
            var droop = BladeDroop.Create(forte.Direction, gripUp, -System.Numerics.Vector3.UnitY, blade);
            _drawnBlade.Step(droop.GetChord(forte), GatherObstaclesForOpponentBlade(), gripUp, deltaSeconds, droop);

            // The mount sits where the partner's hand holds the blade, so the ghost hilt shows it when the drawn one lags.
            var mountOnStrip = new TrackedPose(forte.GuardPosition, bladeRotation);
            _bladeMount.SetPositionAndRotation(
                StripFrame.ToRoom(stripInRoom, mountOnStrip.Position).ToUnity(),
                (stripInRoom.Rotation * mountOnStrip.Rotation).ToUnity());
            _bladeVisual.Draw(_drawnBlade, stripInRoom, Time.time);
        }

        /// <summary>The floor, this Fencer's body and this Fencer's blade, always in the same order and count.</summary>
        private IReadOnlyList<BladeObstacle> GatherObstaclesForOpponentBlade()
        {
            _opponentBladeObstacles.Clear();
            _opponentBladeObstacles.Add(BladeObstacle.Floor);

            var local = _link.Local;
            FencerBodyShape.GetCapsules(local?.Body, local?.HeightMeters ?? _settings.FencerHeightMeters, _capsules);
            foreach (var capsule in _capsules)
            {
                _opponentBladeObstacles.Add(BladeObstacle.FromCapsule(capsule));
            }

            var myBlade = _myBlade != null ? _myBlade.DrawnBladeOnStrip : null;
            _opponentBladeObstacles.Add(BladeObstacle.FromCapsule(myBlade.HasValue
                ? new Capsule(myBlade.Value.GuardPosition, myBlade.Value.TipPosition, MyBladeRadiusMeters)
                : FencerBodyShape.Parked));
            return _opponentBladeObstacles;
        }

        private static OpponentBodyPart ToOpponentPart(RigMeshPart part)
        {
            switch (part)
            {
                case RigMeshPart.Arms:
                    return OpponentBodyPart.Arms;
                case RigMeshPart.Legs:
                    return OpponentBodyPart.Legs;
                default:
                    return OpponentBodyPart.Torso;
            }
        }
    }
}
