using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;
using VrFencing.Session;
using VrFencing.Settings;

namespace VrFencing.Tracking
{
    /// <summary>
    /// The one owner of body tracking (PER-79): starts and stops OVRPlugin body tracking (Full Body, high fidelity
    /// requested) and reads the body once per frame for everyone who needs it: the arm tracking for the rep log and the
    /// Fencer recorder. Two components starting and stopping the same runtime tracker would fight over it.
    /// <para>
    /// Full Body (84 joints: the 70 upper-body joints in the same order, then the legs) because the Fencer recorder and
    /// the multiplayer body stream need the legs (PER-55); the arm joints the rep log reads are the same as before.
    /// </para>
    /// <para>
    /// Body tracking stops at once while the app is paused (the headset asleep), has lost focus, or the headset is off the
    /// head (PER-43: left running through sleep it lagged passthrough and the system menu after wake). It restarts from
    /// <see cref="Update"/>, never from a lifecycle callback, once the runtime has stayed ready for a settle time
    /// (PER-71: restarting inside the wake callbacks, under the lock screen, left a black frozen view).
    /// <see cref="BodyTrackingLifecycle"/> decides; this forwards the events and the runtime state each frame. This
    /// logic moved here unchanged from <c>ArmTrackingBehaviour</c>.
    /// </para>
    /// </summary>
    // Before the default order, so every reader's Update sees this frame's body.
    [DefaultExecutionOrder(-50)]
    public sealed class BodyTrackingBehaviour : MonoBehaviour
    {
        /// <summary>Joints in each frame's body: OVRPlugin's Full Body set.</summary>
        public const int JointCount = (int)OVRPlugin.BoneId.FullBody_End;

        private readonly BodyTrackingLifecycle _lifecycle = new BodyTrackingLifecycle();
        private OVRPlugin.BodyState _bodyState;
        private OVRPlugin.Skeleton2 _skeleton;
        private OVRCameraRig _cameraRig;
        private bool _isBodyTrackingRunning;
        private bool _hasBodyState;
        private string _lastUnavailableMessage;
        private BodyTrackingFidelity? _lastLoggedFidelity;

        /// <summary>This frame's body fidelity, or null when there is no body this frame (not running, or no valid state).</summary>
        public BodyTrackingFidelity? Fidelity { get; private set; }

        /// <summary>Counts the runtime's re-measures of the Fencer's skeleton; a change means a new bind pose.</summary>
        public uint SkeletonChangedCount => _bodyState.SkeletonChangedCount;

        private void Awake()
        {
            _cameraRig = FindFirstObjectByType<OVRCameraRig>();
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
        }

        private void Update()
        {
            ApplyDecision(_lifecycle.Tick(Time.realtimeSinceStartupAsDouble, RuntimeReadinessReader.Read(), _isBodyTrackingRunning));
            _hasBodyState = _isBodyTrackingRunning
                            && _cameraRig != null
                            && OVRPlugin.GetBodyState4(OVRPlugin.Step.Render, OVRPlugin.BodyJointSet.FullBody, ref _bodyState);
            Fidelity = _hasBodyState
                ? _bodyState.Fidelity == OVRPlugin.BodyTrackingFidelity2.High ? BodyTrackingFidelity.High : BodyTrackingFidelity.Low
                : (BodyTrackingFidelity?)null;
            if (Fidelity.HasValue)
            {
                LogFidelityChange(Fidelity.Value);
            }
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
            StopBodyTracking("component disabled");
        }

        /// <summary>
        /// A joint of this frame's body in the room (Unity world), or null when there is no body this frame or the tracker
        /// has no valid position and rotation for it. <paramref name="jointIndex"/> is an <see cref="OVRPlugin.BoneId"/>.
        /// </summary>
        public TrackedPose? ReadJointInRoom(int jointIndex)
        {
            if (!_hasBodyState || _bodyState.JointLocations == null || jointIndex < 0 || jointIndex >= _bodyState.JointLocations.Length)
            {
                return null;
            }

            var location = _bodyState.JointLocations[jointIndex];
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

        /// <summary>
        /// The Fencer's skeleton in its rest pose, in Unity's axes, as Meta's retargeter reads it
        /// (<c>SkeletonUtilities.GetBindPoses</c>). False while body tracking is not running or the runtime has no skeleton.
        /// </summary>
        public bool TryReadBindPose(out FencerBindPose bindPose)
        {
            bindPose = null;
            if (!_isBodyTrackingRunning || !OVRPlugin.GetSkeleton2(OVRPlugin.SkeletonType.FullBody, ref _skeleton) || _skeleton.Bones == null)
            {
                return false;
            }

            var joints = new TrackedPose[JointCount];
            var parents = new int[JointCount];
            for (var joint = 0; joint < JointCount; joint++)
            {
                joints[joint] = new TrackedPose(System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity);
                parents[joint] = -1;
            }

            for (var bone = 0; bone < _skeleton.NumBones && bone < _skeleton.Bones.Length; bone++)
            {
                var skeletonBone = _skeleton.Bones[bone];
                var joint = (int)skeletonBone.Id;
                if (joint < 0 || joint >= JointCount)
                {
                    continue;
                }

                var pose = new Pose(skeletonBone.Pose.Position.FromFlippedZVector3f(), skeletonBone.Pose.Orientation.FromFlippedZQuatf());
                joints[joint] = pose.ToTrackedPose();
                parents[joint] = skeletonBone.ParentBoneIndex;
            }

            bindPose = new FencerBindPose(joints, parents);
            return true;
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
            _hasBodyState = false;
            Fidelity = null;
            // The next start logs its fidelity and any unavailability afresh.
            _lastLoggedFidelity = null;
            _lastUnavailableMessage = null;
            var failureNote = isStopped ? string.Empty : ", but the runtime reported a stop failure";
            Debug.Log($"[Tracking] Body tracking stopped ({reason}){failureNote}.");
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
                LogUnavailableOnChange("[Tracking] Body tracking is not supported here; the rep log gets no arm pose and recordings no body.");
                return;
            }

            if (!OVRPermissionsRequester.IsPermissionGranted(OVRPermissionsRequester.Permission.BodyTracking))
            {
                LogUnavailableOnChange("[Tracking] Waiting for the body tracking permission; the rep log gets no arm pose and recordings no body until it is granted.");
                return;
            }

            var stopwatch = Stopwatch.StartNew();
            var isStarted = OVRPlugin.StartBodyTracking2(OVRPlugin.BodyJointSet.FullBody);
            LogRuntimeCallDuration("StartBodyTracking2", stopwatch, isStarted);
            if (!isStarted)
            {
                LogUnavailableOnChange("[Tracking] Body tracking failed to start; the rep log gets no arm pose and recordings no body.");
                return;
            }

            _isBodyTrackingRunning = true;
            // High fidelity is IOBT. If the headset cannot give it, it quietly falls back to Low; the log records which.
            stopwatch.Restart();
            var isHighFidelityAccepted = OVRPlugin.RequestBodyTrackingFidelity(OVRPlugin.BodyTrackingFidelity2.High);
            LogRuntimeCallDuration("RequestBodyTrackingFidelity", stopwatch, isHighFidelityAccepted);
            Debug.Log($"[Tracking] Body tracking started (full body, high fidelity {(isHighFidelityAccepted ? "requested" : "request refused")}).");
        }

        private void OnPermissionGranted(string permissionId)
        {
            if (permissionId == OVRPermissionsRequester.GetPermissionId(OVRPermissionsRequester.Permission.BodyTracking))
            {
                TryStartBodyTracking();
            }
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
