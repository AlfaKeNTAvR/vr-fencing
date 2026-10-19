using System;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;
using VrFencing.Drills;
using VrFencing.Session;
using VrFencing.Settings;
using VrFencing.Strip;

namespace VrFencing.Tracking
{
    /// <summary>
    /// Scene host for the Core <see cref="RepArmTracker"/>: runs Inside-Out Body Tracking (upper body, high
    /// fidelity requested) through OVRPlugin, feeds the head and the blade arm's joints to the tracker every frame,
    /// clears it on each Rep cue, and hands the rep log its <see cref="ArmPoseSnapshot"/> when a Rep is judged.
    /// <para>
    /// Logged only: nothing is shown and nothing depends on it. When body tracking is unavailable (no permission,
    /// not supported, or stopped) the rep log gets a null arm pose; when the headset falls back to low fidelity
    /// the poses are still logged with their fidelity. Both are noted in the device log with a <c>[Tracking]</c> tag.
    /// </para>
    /// <para>
    /// Body tracking stops at once while the app is paused (the headset asleep), has lost focus, or the headset is off the
    /// head (PER-43: left running through sleep it lagged passthrough and the system menu after wake). It restarts from
    /// <see cref="Update"/>, never from a lifecycle callback, once the runtime has stayed ready for a settle time
    /// (PER-71: restarting inside the wake callbacks, under the lock screen, left a black frozen view).
    /// <see cref="BodyTrackingLifecycle"/> decides; this forwards the events and the runtime state each frame.
    /// </para>
    /// </summary>
    public sealed class ArmTrackingBehaviour : MonoBehaviour
    {
        // In the upper-body skeleton the "ArmUpper" joint sits at the shoulder and "ArmLower" at the elbow.
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

        private readonly RepArmTracker _tracker = new RepArmTracker();
        private readonly BodyTrackingLifecycle _lifecycle = new BodyTrackingLifecycle();
        private OVRPlugin.BodyState _bodyState;
        private OVRCameraRig _cameraRig;
        private OVRPlugin.BoneId[] _bladeArmJoints = RightArmJoints;
        private DrillRunner _subscribedRunner;
        private bool _isBodyTrackingRunning;
        private string _lastUnavailableMessage;
        private BodyTrackingFidelity? _lastLoggedFidelity;

        private void Awake()
        {
            _sessionRecorder = _sessionRecorder != null ? _sessionRecorder : FindFirstObjectByType<SessionRecorderBehaviour>();
            _drillRunner = _drillRunner != null ? _drillRunner : FindFirstObjectByType<DrillRunnerBehaviour>();
            _strip = _strip != null ? _strip : FindFirstObjectByType<StripBehaviour>();
            _cameraRig = FindFirstObjectByType<OVRCameraRig>();
            _bladeArmJoints = SettingsFile.Load().Hand == Hand.Left ? LeftArmJoints : RightArmJoints;
        }

        private void OnEnable()
        {
            OVRPermissionsRequester.PermissionGranted += OnPermissionGranted;
            OVRManager.InputFocusLost += OnFocusLost;
            OVRManager.InputFocusAcquired += OnFocusAcquired;
            OVRManager.VrFocusLost += OnFocusLost;
            OVRManager.VrFocusAcquired += OnFocusAcquired;
            OVRManager.HMDUnmounted += OnHeadsetUnmounted;
            OVRManager.HMDMounted += OnHeadsetMounted;
            OVRManager.HMDLost += OnHeadsetUnmounted;
            OVRManager.HMDAcquired += OnHeadsetMounted;
            // No start here: the first start comes from Update once the runtime has settled, as after a wake.
            if (_sessionRecorder != null)
            {
                _sessionRecorder.ArmPoseSource = SnapshotJudgedRep;
            }
        }

        // Start, not OnEnable: the runner is created in DrillRunnerBehaviour.Awake, which may run after this OnEnable.
        private void Start()
        {
            if (_sessionRecorder == null || _drillRunner == null || _strip == null || _cameraRig == null)
            {
                Debug.LogWarning("[Tracking] Missing the session recorder, Drill runner, Strip or camera rig; arm poses will not be logged.");
                return;
            }

            _subscribedRunner = _drillRunner.Runner;
            _subscribedRunner.RepCued += OnRepCued;
        }

        private void Update()
        {
            ApplyDecision(_lifecycle.Tick(Time.realtimeSinceStartupAsDouble, RuntimeReadinessReader.Read(), _isBodyTrackingRunning));
            if (_cameraRig == null)
            {
                return;
            }

            var head = _cameraRig.centerEyeAnchor.position.ToNumerics();
            if (!_isBodyTrackingRunning || !OVRPlugin.GetBodyState4(OVRPlugin.Step.Render, OVRPlugin.BodyJointSet.UpperBody, ref _bodyState))
            {
                _tracker.AddSample(new BodySample(Time.timeAsDouble, head, null, null, null, null));
                return;
            }

            var fidelity = _bodyState.Fidelity == OVRPlugin.BodyTrackingFidelity2.High ? BodyTrackingFidelity.High : BodyTrackingFidelity.Low;
            LogFidelityChange(fidelity);
            _tracker.AddSample(new BodySample(
                Time.timeAsDouble,
                head,
                fidelity,
                ReadJointInRoom(_bladeArmJoints[0]),
                ReadJointInRoom(_bladeArmJoints[1]),
                ReadJointInRoom(_bladeArmJoints[2])));
        }

        private void OnDisable()
        {
            OVRPermissionsRequester.PermissionGranted -= OnPermissionGranted;
            OVRManager.InputFocusLost -= OnFocusLost;
            OVRManager.InputFocusAcquired -= OnFocusAcquired;
            OVRManager.VrFocusLost -= OnFocusLost;
            OVRManager.VrFocusAcquired -= OnFocusAcquired;
            OVRManager.HMDUnmounted -= OnHeadsetUnmounted;
            OVRManager.HMDMounted -= OnHeadsetMounted;
            OVRManager.HMDLost -= OnHeadsetUnmounted;
            OVRManager.HMDAcquired -= OnHeadsetMounted;
            if (_sessionRecorder != null && _sessionRecorder.ArmPoseSource == (Func<ArmPoseSnapshot>)SnapshotJudgedRep)
            {
                _sessionRecorder.ArmPoseSource = null;
            }

            StopBodyTracking("component disabled");
        }

        // Lifecycle callbacks only record the event (and stop at once); starting is left to Update.
        private void OnApplicationPause(bool isPaused)
        {
            RecordLifecycleEvent(isPaused ? LifecycleEvent.AppPaused : LifecycleEvent.AppResumed);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            RecordLifecycleEvent(hasFocus ? LifecycleEvent.FocusRegained : LifecycleEvent.FocusLost);
        }

        private void OnFocusLost() => RecordLifecycleEvent(LifecycleEvent.FocusLost);

        private void OnFocusAcquired() => RecordLifecycleEvent(LifecycleEvent.FocusRegained);

        private void OnHeadsetUnmounted() => RecordLifecycleEvent(LifecycleEvent.HeadsetRemoved);

        private void OnHeadsetMounted() => RecordLifecycleEvent(LifecycleEvent.HeadsetPutOn);

        private void RecordLifecycleEvent(LifecycleEvent lifecycleEvent)
        {
            ApplyDecision(_lifecycle.RecordEvent(lifecycleEvent, _isBodyTrackingRunning));
        }

        private void ApplyDecision(BodyTrackingDecision decision)
        {
            switch (decision.Action)
            {
                case BodyTrackingAction.Stop:
                    StopBodyTracking(decision.Reason);
                    break;
                case BodyTrackingAction.Start:
                    Debug.Log($"[Tracking] Starting body tracking ({decision.Reason}).");
                    TryStartBodyTracking();
                    break;
            }
        }

        private void StopBodyTracking(string reason)
        {
            if (!_isBodyTrackingRunning)
            {
                return;
            }

            var stopwatch = Stopwatch.StartNew();
            var isStopped = OVRPlugin.StopBodyTracking();
            LogRuntimeCallDuration("StopBodyTracking", stopwatch, isStopped);
            _isBodyTrackingRunning = false;
            // The next start logs its fidelity and any unavailability afresh.
            _lastLoggedFidelity = null;
            _lastUnavailableMessage = null;
            var failureNote = isStopped ? string.Empty : ", but the runtime reported a stop failure";
            Debug.Log($"[Tracking] Body tracking stopped ({reason}){failureNote}.");
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

        private void TryStartBodyTracking()
        {
            if (_isBodyTrackingRunning)
            {
                return;
            }

            if (!_lifecycle.ShouldRun)
            {
                LogUnavailableOnChange($"[Tracking] Body tracking held off ({_lifecycle.DescribeHolds()}); it restarts once that clears.");
                return;
            }

            if (!OVRPlugin.bodyTrackingSupported)
            {
                LogUnavailableOnChange("[Tracking] Body tracking is not supported here; the rep log gets no arm pose.");
                return;
            }

            if (!OVRPermissionsRequester.IsPermissionGranted(OVRPermissionsRequester.Permission.BodyTracking))
            {
                LogUnavailableOnChange("[Tracking] Waiting for the body tracking permission; the rep log gets no arm pose until it is granted.");
                return;
            }

            var stopwatch = Stopwatch.StartNew();
            var isStarted = OVRPlugin.StartBodyTracking2(OVRPlugin.BodyJointSet.UpperBody);
            LogRuntimeCallDuration("StartBodyTracking2", stopwatch, isStarted);
            if (!isStarted)
            {
                LogUnavailableOnChange("[Tracking] Body tracking failed to start; the rep log gets no arm pose.");
                return;
            }

            _isBodyTrackingRunning = true;
            // High fidelity is IOBT. If the headset cannot give it, it quietly falls back to Low; the log records which.
            stopwatch.Restart();
            var isHighFidelityAccepted = OVRPlugin.RequestBodyTrackingFidelity(OVRPlugin.BodyTrackingFidelity2.High);
            LogRuntimeCallDuration("RequestBodyTrackingFidelity", stopwatch, isHighFidelityAccepted);
            Debug.Log($"[Tracking] Body tracking started (upper body, high fidelity {(isHighFidelityAccepted ? "requested" : "request refused")}).");
        }

        private void OnPermissionGranted(string permissionId)
        {
            if (permissionId == OVRPermissionsRequester.GetPermissionId(OVRPermissionsRequester.Permission.BodyTracking))
            {
                TryStartBodyTracking();
            }
        }

        /// <summary>A joint in the room (Unity world), or null when the tracker has no valid position and rotation for it.</summary>
        private TrackedPose? ReadJointInRoom(OVRPlugin.BoneId joint)
        {
            var index = (int)joint;
            if (_bodyState.JointLocations == null || index >= _bodyState.JointLocations.Length)
            {
                return null;
            }

            var location = _bodyState.JointLocations[index];
            if (!location.PositionValid || !location.OrientationValid)
            {
                return null;
            }

            // Joint poses are in the camera rig's tracking space with OpenXR's flipped z.
            var trackingSpace = _cameraRig.trackingSpace;
            var position = trackingSpace.TransformPoint(location.Pose.Position.FromFlippedZVector3f());
            var rotation = trackingSpace.rotation * location.Pose.Orientation.FromFlippedZQuatf();
            return new Pose(position, rotation).ToTrackedPose();
        }

        /// <summary>How long an OVRPlugin body tracking call blocked the frame (PER-71: a slow call around a wake shows here).</summary>
        private static void LogRuntimeCallDuration(string callName, Stopwatch stopwatch, bool isSuccess)
        {
            Debug.Log($"[Tracking] OVRPlugin.{callName} took {stopwatch.Elapsed.TotalMilliseconds:0.0} ms ({(isSuccess ? "ok" : "failed")}).");
        }

        private void LogFidelityChange(BodyTrackingFidelity fidelity)
        {
            if (_lastLoggedFidelity == fidelity)
            {
                return;
            }

            _lastLoggedFidelity = fidelity;
            Debug.Log($"[Tracking] Body tracking fidelity reported by the runtime: {fidelity}.");
        }

        private void LogUnavailableOnChange(string message)
        {
            if (_lastUnavailableMessage == message)
            {
                return;
            }

            _lastUnavailableMessage = message;
            Debug.Log(message);
        }
    }
}
