using System;
using Oculus.Haptics;
using UnityEngine;

namespace VrFencing.Strip
{
    /// <summary>
    /// The short buzz on entering a Warning Zone, played on both controllers through the Meta Haptics SDK
    /// (OVRInput vibration does nothing under OpenXR). The clip is built from JSON here instead of an asset made in
    /// Haptics Studio: a flat 0.12 s pulse at strong amplitude.
    /// </summary>
    internal sealed class WarningZoneBuzz : IDisposable
    {
        // Meta .haptic format: amplitude and frequency envelopes over time (seconds), values 0 to 1.
        private const string BuzzClipJson =
            "{\"version\": {\"major\": 1, \"minor\": 0, \"patch\": 0}, " +
            "\"metadata\": {\"editor\": \"VR Fencing\", \"source\": \"\", \"project\": \"warning-zone-buzz\", \"tags\": [], \"description\": \"Short buzz on entering a Warning Zone\"}, " +
            "\"signals\": {\"continuous\": {\"envelopes\": {" +
            "\"amplitude\": [{\"time\": 0, \"amplitude\": 0.9}, {\"time\": 0.12, \"amplitude\": 0.9}, {\"time\": 0.13, \"amplitude\": 0}], " +
            "\"frequency\": [{\"time\": 0, \"frequency\": 0.6}, {\"time\": 0.13, \"frequency\": 0.6}]}}}}";

        private HapticClip _clip;
        private HapticClipPlayer _player;
        private bool _hasFailed;

        public void Play()
        {
            if (_hasFailed)
            {
                return;
            }

            try
            {
                if (_player == null)
                {
                    _clip = ScriptableObject.CreateInstance<HapticClip>();
                    _clip.json = BuzzClipJson;
                    _player = new HapticClipPlayer(_clip);
                }

                _player.Play(Controller.Both);
            }
            catch (Exception error)
            {
                // Haptics are a cue, not a safety stop: the glow and the off-strip pause still work without them.
                _hasFailed = true;
                Debug.LogError($"[Strip] Warning Zone buzz failed, haptics off for this session: {error.Message}");
            }
        }

        public void Dispose()
        {
            _player?.Dispose();
            _player = null;
            if (_clip != null)
            {
                UnityEngine.Object.Destroy(_clip);
                _clip = null;
            }
        }
    }
}
