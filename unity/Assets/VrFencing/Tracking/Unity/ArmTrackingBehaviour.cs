using System;
using UnityEngine;
using VrFencing.Drills;
using VrFencing.Session;
using VrFencing.Settings;
using VrFencing.Strip;

namespace VrFencing.Tracking
{
    /// <summary>
    /// Scene host for the Core <see cref="RepArmTracker"/>: feeds the head and the blade arm's joints from Inside-Out Body
    /// Tracking to the tracker every frame, clears it on each Rep cue, and hands the rep log its
    /// <see cref="ArmPoseSnapshot"/> when a Rep is judged.
    /// <para>
    /// Logged only: nothing is shown and nothing depends on it. When body tracking is unavailable (no permission,
    /// not supported, or stopped) the rep log gets a null arm pose; when the headset falls back to low fidelity
    /// the poses are still logged with their fidelity. Both are noted in the device log with a <c>[Tracking]</c> tag.
    /// </para>
    /// <para>
    /// Starting and stopping body tracking, with its sleep/wake rules (PER-43, PER-71), belongs to
    /// <see cref="BodyTrackingBehaviour"/>, which the Fencer recorder reads too (PER-79); this only reads its body.
    /// </para>
    /// </summary>
    public sealed class ArmTrackingBehaviour : MonoBehaviour
    {
        // In the body skeleton the "ArmUpper" joint sits at the shoulder and "ArmLower" at the elbow. The upper-body
        // joints keep these indices in the Full Body set.
        private static readonly OVRPlugin.BoneId[] RightArmJoints =
        {
            OVRPlugin.BoneId.Body_RightArmUpper, OVRPlugin.BoneId.Body_RightArmLower, OVRPlugin.BoneId.Body_RightHandWrist,
        };

        private static readonly OVRPlugin.BoneId[] LeftArmJoints =
        {
            OVRPlugin.BoneId.Body_LeftArmUpper, OVRPlugin.BoneId.Body_LeftArmLower, OVRPlugin.BoneId.Body_LeftHandWrist,
        };

        [SerializeField]
        [Tooltip("Receives the arm pose for each judged Rep. Found in the scene if empty.")]
        private SessionRecorderBehaviour _sessionRecorder;

        [SerializeField]
        [Tooltip("Its Rep cues start a new Rep's samples. Found in the scene if empty.")]
        private DrillRunnerBehaviour _drillRunner;

        [SerializeField]
        [Tooltip("Its En Garde Line is the Strip frame the poses are logged in. Found in the scene if empty.")]
        private StripBehaviour _strip;

        [SerializeField]
        [Tooltip("Runs body tracking and reads the body each frame. Found in the scene if empty.")]
        private BodyTrackingBehaviour _bodyTracking;

        private readonly RepArmTracker _tracker = new RepArmTracker();
        private OVRCameraRig _cameraRig;
        private OVRPlugin.BoneId[] _bladeArmJoints = RightArmJoints;
        private DrillRunner _subscribedRunner;

        private void Awake()
        {
            _sessionRecorder = _sessionRecorder != null ? _sessionRecorder : FindFirstObjectByType<SessionRecorderBehaviour>();
            _drillRunner = _drillRunner != null ? _drillRunner : FindFirstObjectByType<DrillRunnerBehaviour>();
            _strip = _strip != null ? _strip : FindFirstObjectByType<StripBehaviour>();
            _bodyTracking = _bodyTracking != null ? _bodyTracking : FindFirstObjectByType<BodyTrackingBehaviour>();
            _cameraRig = FindFirstObjectByType<OVRCameraRig>();
            _bladeArmJoints = SettingsFile.Load().Hand == Hand.Left ? LeftArmJoints : RightArmJoints;
        }

        private void OnEnable()
        {
            if (_sessionRecorder != null)
            {
                _sessionRecorder.ArmPoseSource = SnapshotJudgedRep;
            }
        }

        // Start, not OnEnable: the runner is created in DrillRunnerBehaviour.Awake, which may run after this OnEnable.
        private void Start()
        {
            if (_sessionRecorder == null || _drillRunner == null || _strip == null || _cameraRig == null || _bodyTracking == null)
            {
                Debug.LogWarning("[Tracking] Missing the session recorder, Drill runner, Strip, camera rig or body tracking; arm poses will not be logged.");
                return;
            }

            _subscribedRunner = _drillRunner.Runner;
            _subscribedRunner.RepCued += OnRepCued;
        }

        private void Update()
        {
            if (_cameraRig == null || _bodyTracking == null)
            {
                return;
            }

            var head = _cameraRig.centerEyeAnchor.position.ToNumerics();
            var fidelity = _bodyTracking.Fidelity;
            if (!fidelity.HasValue)
            {
                _tracker.AddSample(new BodySample(Time.timeAsDouble, head, null, null, null, null));
                return;
            }

            _tracker.AddSample(new BodySample(
                Time.timeAsDouble,
                head,
                fidelity,
                _bodyTracking.ReadJointInRoom((int)_bladeArmJoints[0]),
                _bodyTracking.ReadJointInRoom((int)_bladeArmJoints[1]),
                _bodyTracking.ReadJointInRoom((int)_bladeArmJoints[2])));
        }

        private void OnDisable()
        {
            if (_sessionRecorder != null && _sessionRecorder.ArmPoseSource == (Func<ArmPoseSnapshot>)SnapshotJudgedRep)
            {
                _sessionRecorder.ArmPoseSource = null;
            }
        }

        private void OnDestroy()
        {
            if (_subscribedRunner != null)
            {
                _subscribedRunner.RepCued -= OnRepCued;
            }
        }

        private void OnRepCued(RepCue cue)
        {
            _tracker.BeginRep();
        }

        private ArmPoseSnapshot SnapshotJudgedRep()
        {
            var enGardeLine = new Pose(_strip.EnGardeLine.position, _strip.EnGardeLine.rotation).ToTrackedPose();
            var snapshot = _tracker.SnapshotAtJudging(enGardeLine, Time.timeAsDouble);
            Debug.Log(DescribeForDeviceLog(snapshot));
            return snapshot;
        }

        private static string DescribeForDeviceLog(ArmPoseSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return "[Tracking] Rep judged: no arm pose (body tracking not running).";
            }

            var joints = (snapshot.Shoulder.HasValue ? 1 : 0) + (snapshot.Elbow.HasValue ? 1 : 0) + (snapshot.Wrist.HasValue ? 1 : 0);
            var lunge = snapshot.LungeStartSecondsBeforeJudging.HasValue
                ? $"lunge started {snapshot.LungeStartSecondsBeforeJudging.Value:0.00} s before judging, arm extended before it: "
                  + (snapshot.ArmExtendedBeforeLunge.HasValue ? $"{snapshot.ArmExtendedBeforeLunge.Value} (elbow {snapshot.ElbowAngleAtLungeStartDegrees:0} deg)" : "unknown (arm not tracked)")
                : "no lunge found";
            return $"[Tracking] Rep judged: fidelity {snapshot.Fidelity}, {joints}/3 arm joints, {lunge}.";
        }
    }
}
