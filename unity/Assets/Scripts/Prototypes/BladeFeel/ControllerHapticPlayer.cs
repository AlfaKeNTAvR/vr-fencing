// Plays the prototype's haptic patterns on the right Touch controller through one of
// two OpenXR-capable paths, switchable at runtime so the Fencer can tell which one
// actually buzzes (OVRInput.SetControllerVibration did nothing under Unity OpenXR):
//   UnityXr        UnityEngine.XR.InputDevice.SendHapticImpulse, one impulse per
//                  segment, timed per frame (about 14 ms steps at 72 Hz); no frequency.
//   MetaHapticsSdk Oculus.Haptics.HapticClipPlayer with a clip built from the pattern,
//                  timed by the runtime, frequency honoured.
// Every play logs the path and whether the call succeeded.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Oculus.Haptics;
using UnityEngine;
using UnityEngine.XR;

namespace VrFencing.Prototypes.BladeFeel
{
    public enum HapticBackend
    {
        UnityXr,
        MetaHapticsSdk,
    }

    public sealed class ControllerHapticPlayer : IDisposable
    {
        private const string LogTag = "[BladeFeel]";

        private readonly Dictionary<HapticSegment[], HapticClipPlayer> _clipPlayers = new();
        private HapticSegment[] _xrPattern;
        private int _xrSegmentIndex;
        private float _xrSegmentEndTime;

        public HapticBackend Backend { get; private set; }

        public ControllerHapticPlayer()
        {
            var device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            var hasCapabilities = device.TryGetHapticCapabilities(out var capabilities);
            var supportsImpulse = device.isValid && hasCapabilities && capabilities.supportsImpulse;
            Backend = supportsImpulse ? HapticBackend.UnityXr : HapticBackend.MetaHapticsSdk;
            Debug.Log($"{LogTag} haptics startup: xr_device_valid={device.isValid} name='{device.name}' " +
                      $"capabilities={hasCapabilities} supports_impulse={(hasCapabilities && capabilities.supportsImpulse)} " +
                      $"channels={(hasCapabilities ? capabilities.numChannels : 0)} -> backend={Backend}");
        }

        public void ToggleBackend()
        {
            Stop();
            Backend = Backend == HapticBackend.UnityXr ? HapticBackend.MetaHapticsSdk : HapticBackend.UnityXr;
            Debug.Log($"{LogTag} haptics backend -> {Backend}");
        }

        /// <summary>Starts a pattern, replacing whatever is playing.</summary>
        public void Play(HapticSegment[] pattern, float now)
        {
            Stop();
            if (Backend == HapticBackend.MetaHapticsSdk)
            {
                PlayWithMetaHaptics(pattern);
                return;
            }

            _xrPattern = pattern;
            var sent = StartXrSegment(0, now);
            Debug.Log($"{LogTag} haptics play backend=UnityXr sent={sent}");
        }

        /// <summary>Advances the UnityXr pattern; the Meta SDK times its clips itself.</summary>
        public void Tick(float now)
        {
            if (_xrPattern == null || now < _xrSegmentEndTime)
                return;
            StartXrSegment(_xrSegmentIndex + 1, now);
        }

        public void Stop()
        {
            _xrPattern = null;
            var device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (device.isValid)
                device.StopHaptics();
            foreach (var player in _clipPlayers.Values)
                player.Stop();
        }

        public void Dispose()
        {
            Stop();
            foreach (var player in _clipPlayers.Values)
                player.Dispose();
            _clipPlayers.Clear();
        }

        private bool StartXrSegment(int index, float now)
        {
            if (index >= _xrPattern.Length)
            {
                _xrPattern = null;
                return false;
            }

            _xrSegmentIndex = index;
            var segment = _xrPattern[index];
            _xrSegmentEndTime = now + segment.DurationSeconds;
            if (segment.Amplitude <= 0f)
                return true; // A gap: nothing to send.

            var device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            return device.isValid && device.SendHapticImpulse(0, segment.Amplitude, segment.DurationSeconds);
        }

        private void PlayWithMetaHaptics(HapticSegment[] pattern)
        {
            try
            {
                if (!_clipPlayers.TryGetValue(pattern, out var player))
                {
                    var clip = ScriptableObject.CreateInstance<HapticClip>();
                    clip.json = BuildClipJson(pattern);
                    player = new HapticClipPlayer(clip);
                    _clipPlayers[pattern] = player;
                }

                player.Play(Oculus.Haptics.Controller.Right);
                Debug.Log($"{LogTag} haptics play backend=MetaHapticsSdk ok=True duration_s={player.clipDuration:0.000}");
            }
            catch (Exception exception)
            {
                Debug.LogError($"{LogTag} haptics play backend=MetaHapticsSdk ok=False: {exception.Message}");
            }
        }

        /// <summary>A Meta Haptics Studio (.haptic v1) clip: stepped amplitude and frequency envelopes.</summary>
        private static string BuildClipJson(IReadOnlyList<HapticSegment> pattern)
        {
            const float EdgeSeconds = 0.0005f;
            var amplitude = new StringBuilder();
            var frequency = new StringBuilder();
            var time = 0f;
            foreach (var segment in pattern)
            {
                var end = time + segment.DurationSeconds;
                AppendPoint(amplitude, "amplitude", time, segment.Amplitude);
                AppendPoint(amplitude, "amplitude", end - EdgeSeconds, segment.Amplitude);
                AppendPoint(frequency, "frequency", time, segment.Frequency);
                AppendPoint(frequency, "frequency", end - EdgeSeconds, segment.Frequency);
                time = end;
            }

            AppendPoint(amplitude, "amplitude", time, 0f);
            AppendPoint(frequency, "frequency", time, 0f);
            return "{\"version\":{\"major\":1,\"minor\":0,\"patch\":0}," +
                   "\"metadata\":{\"editor\":\"\",\"source\":\"\",\"project\":\"vr-fencing\",\"tags\":[],\"description\":\"\"}," +
                   "\"signals\":{\"continuous\":{\"envelopes\":{" +
                   $"\"amplitude\":[{amplitude}],\"frequency\":[{frequency}]" + "}}}}";
        }

        private static void AppendPoint(StringBuilder points, string key, float time, float value)
        {
            if (points.Length > 0)
                points.Append(',');
            points.Append("{\"time\":").Append(time.ToString("0.####", CultureInfo.InvariantCulture))
                .Append(",\"").Append(key).Append("\":").Append(Mathf.Clamp01(value).ToString("0.###", CultureInfo.InvariantCulture))
                .Append('}');
        }
    }
}
