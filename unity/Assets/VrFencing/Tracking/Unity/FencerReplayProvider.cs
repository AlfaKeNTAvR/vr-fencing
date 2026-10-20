using System;
using System.IO;
using Meta.XR.Movement.Retargeting;
using Unity.Collections;
using UnityEngine;
using VrFencing.Settings;
using VrFencing.Session;
using NativeTransform = Meta.XR.Movement.MSDKUtility.NativeTransform;

namespace VrFencing.Tracking
{
    /// <summary>
    /// Plays a Fencer recording in the PC Editor as if a headset were tracking it (PER-79, decided in PER-55). It is a
    /// Movement SDK <see cref="ISourceDataProvider"/>: put it on the same GameObject as a <c>CharacterRetargeter</c>, in
    /// the slot the headset's <c>MetaSourceDataProvider</c> fills, and the same retargeting (and later the same network
    /// send) runs unchanged. The recorded sword controller moves <see cref="SwordController"/>, so the blade pose can be
    /// built from it exactly as from a live controller.
    /// <para>
    /// The recording's Strip frame is placed at <c>_enGardeLine</c>, or at the world origin when that is empty. Without a
    /// rigged body yet, the Scene view draws the replayed skeleton, head and controller as gizmos.
    /// </para>
    /// </summary>
    // Before the default order: CharacterRetargeter asks for the pose in its Update.
    [DefaultExecutionOrder(-100)]
    public sealed class FencerReplayProvider : MonoBehaviour, ISourceDataProvider
    {
        private const float HeadGizmoRadiusMeters = 0.1f;
        private const float JointGizmoRadiusMeters = 0.015f;
        private const float ControllerGizmoLengthMeters = 0.3f;

        [SerializeField]
        [Tooltip("The .fencer file: absolute, or relative to the repo root (for example Recordings/fencer-20261019-213005.fencer).")]
        private string _recordingPath;

        [SerializeField]
        [Tooltip("Start again from the beginning after the last frame; otherwise hold the last frame.")]
        private bool _isLooping = true;

        [SerializeField]
        [Min(0.0f)]
        [Tooltip("1 is real time; below 1 is slow motion.")]
        private float _playbackSpeed = 1.0f;

        [SerializeField]
        [Min(0.0f)]
        [Tooltip("Where in the recording playback starts, in seconds.")]
        private float _startSeconds;

        [SerializeField]
        [Tooltip("Where the recorded Strip frame sits in this scene (its En Garde Line). Empty: the world origin.")]
        private Transform _enGardeLine;

        [SerializeField]
        [Tooltip("Draw the replayed skeleton, head and sword controller in the Scene view.")]
        private bool _isSkeletonDrawn = true;

        private FencerReplay _replay;
        private FencerFrame _frame;
        private double _playbackSeconds;
        private int _bindPoseIndex = -1;
        private int _deliveredBindPoseIndex = -1;
        private Transform _swordController;

        /// <summary>Follows the recorded sword-hand controller anchor; it stays put while the controller was not tracked.</summary>
        public Transform SwordController => _swordController;

        /// <summary>The replayed frame for this Editor frame, in the recording's Strip frame; null until a recording loads.</summary>
        public FencerFrame CurrentFrame => _frame;

        /// <summary>The recording's header (the Fencer's height, hand and Strip), or null until a recording loads.</summary>
        public FencerRecordingHeader Header => _replay?.Recording.Header;

        private int JointCount => _replay?.Recording.Header.JointCount ?? FencerRecordingHeader.FullBodyJointCount;

        private void Awake()
        {
            _swordController = new GameObject("Sword Controller").transform;
            _swordController.SetParent(transform, false);
            _replay = LoadReplay(_recordingPath);
        }

        private void Update()
        {
            if (_replay == null)
            {
                return;
            }

            _playbackSeconds += Time.deltaTime * _playbackSpeed;
            var playbackSeconds = _playbackSeconds + _startSeconds;
            _frame = _replay.Sample(playbackSeconds, _isLooping);
            _bindPoseIndex = _replay.GetBindPoseIndex(playbackSeconds, _isLooping);
            if (_frame.SwordController.HasValue)
            {
                var controller = ToWorld(_frame.SwordController.Value);
                _swordController.SetPositionAndRotation(controller.position, controller.rotation);
            }
        }

        /// <inheritdoc />
        public NativeArray<NativeTransform> GetSkeletonPose()
        {
            // Untracked joints are identity, as Meta's own provider hands out when its data is not valid.
            var pose = new NativeArray<NativeTransform>(JointCount, Allocator.Temp);
            for (var joint = 0; joint < pose.Length; joint++)
            {
                var recorded = _frame != null && joint < _frame.Joints.Count ? _frame.Joints[joint] : null;
                pose[joint] = recorded.HasValue ? new NativeTransform(ToWorld(recorded.Value)) : NativeTransform.Identity();
            }

            return pose;
        }

        /// <inheritdoc />
        public NativeArray<NativeTransform> GetSkeletonTPose()
        {
            if (_replay == null || _bindPoseIndex < 0)
            {
                return new NativeArray<NativeTransform>(0, Allocator.Temp);
            }

            var bindPose = _replay.Recording.BindPoses[_bindPoseIndex].BindPose;
            var pose = new NativeArray<NativeTransform>(bindPose.Joints.Count, Allocator.Temp);
            for (var joint = 0; joint < pose.Length; joint++)
            {
                pose[joint] = new NativeTransform(bindPose.Joints[joint].ToUnity());
            }

            _deliveredBindPoseIndex = _bindPoseIndex;
            return pose;
        }

        /// <summary>Null: the recording is Full Body, and Meta's provider names a manifestation only for upper body.</summary>
        public string GetManifestation()
        {
            return null;
        }

        /// <summary>True on frames the headset tracked the body, once a rest pose is known to retarget against.</summary>
        public bool IsPoseValid()
        {
            return _frame != null && _frame.BodyFidelity.HasValue && _bindPoseIndex >= 0;
        }

        /// <inheritdoc />
        public bool IsNewTPoseAvailable()
        {
            return _bindPoseIndex >= 0 && _bindPoseIndex != _deliveredBindPoseIndex;
        }

        private static FencerReplay LoadReplay(string recordingPath)
        {
            if (string.IsNullOrWhiteSpace(recordingPath))
            {
                Debug.LogError("[Recorder] No recording set on the Fencer replay; use VR Fencing > Replay a Fencer recording.");
                return null;
            }

            var path = ResolvePath(recordingPath);
            try
            {
                var recording = FencerRecordingFile.Read(path);
                var header = recording.Header;
                var truncatedNote = recording.IsTruncated ? " (cut short at the end; the complete frames play)" : string.Empty;
                var replay = new FencerReplay(recording);
                Debug.Log($"[Recorder] Replaying {path}: {recording.Frames.Count} frames, {replay.DurationSeconds:0.0} s, "
                          + $"{header.Hand}-handed Fencer of {header.FencerHeightMeters:0.00} m, recorded {header.StartedUtc:u}{truncatedNote}.");
                return replay;
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                Debug.LogError($"[Recorder] Cannot replay {path}: {exception.Message}");
                return null;
            }
        }

        /// <summary>Relative paths start at the repo root, the folder above the Unity project, where <c>Recordings/</c> is.</summary>
        private static string ResolvePath(string recordingPath)
        {
            return Path.IsPathRooted(recordingPath)
                ? recordingPath
                : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", recordingPath));
        }

        /// <summary>A Strip-frame pose in this scene: turned and moved by the En Garde Line, never scaled.</summary>
        private Pose ToWorld(TrackedPose poseInStrip)
        {
            var pose = poseInStrip.ToUnity();
            if (_enGardeLine == null)
            {
                return pose;
            }

            return new Pose(_enGardeLine.position + (_enGardeLine.rotation * pose.position), _enGardeLine.rotation * pose.rotation);
        }

        private void OnDrawGizmos()
        {
            if (!_isSkeletonDrawn || _frame == null)
            {
                return;
            }

            Gizmos.color = _frame.BodyFidelity == BodyTrackingFidelity.High ? Color.green : Color.yellow;
            var parents = _bindPoseIndex >= 0 ? _replay.Recording.BindPoses[_bindPoseIndex].BindPose.ParentIndices : null;
            for (var joint = 0; joint < _frame.Joints.Count; joint++)
            {
                var recorded = _frame.Joints[joint];
                if (!recorded.HasValue)
                {
                    continue;
                }

                var position = ToWorld(recorded.Value).position;
                Gizmos.DrawSphere(position, JointGizmoRadiusMeters);
                var parent = parents != null && joint < parents.Count ? parents[joint] : -1;
                if (parent >= 0 && parent < _frame.Joints.Count && _frame.Joints[parent].HasValue)
                {
                    Gizmos.DrawLine(position, ToWorld(_frame.Joints[parent].Value).position);
                }
            }

            if (_frame.Head.HasValue)
            {
                Gizmos.color = Color.white;
                Gizmos.DrawWireSphere(ToWorld(_frame.Head.Value).position, HeadGizmoRadiusMeters);
            }

            if (_frame.SwordController.HasValue)
            {
                var controller = ToWorld(_frame.SwordController.Value);
                Gizmos.color = Color.red;
                Gizmos.DrawLine(controller.position, controller.position + (controller.rotation * Vector3.forward * ControllerGizmoLengthMeters));
            }
        }
    }
}
