using System;
using System.Collections.Generic;
using Oculus.Haptics;
using UnityEngine;
using VrFencing.Settings;

namespace VrFencing.Blade
{
    /// <summary>
    /// Plays the Blade's haptic patterns on the sword-hand controller through the Meta Haptics SDK (OVRInput vibration
    /// does nothing under OpenXR, PER-22). Clips are built at runtime from <see cref="BladeHaptics"/> and kept per event
    /// and pattern; the player's amplitude carries the speed-scaled strength.
    /// </summary>
    internal sealed class BladeHapticPlayer : IDisposable
    {
        private readonly Dictionary<(BladeEventType, HapticPattern), ClipPlayer> _players = new Dictionary<(BladeEventType, HapticPattern), ClipPlayer>();
        private ClipPlayer _playing;
        private bool _hasFailed;

        /// <summary>Plays the pattern for an event, replacing whatever is playing. Off plays nothing.</summary>
        public void Play(BladeEventType eventType, HapticPattern pattern, float strength, Hand swordHand)
        {
            if (_hasFailed || pattern == HapticPattern.Off)
            {
                return;
            }

            try
            {
                if (!_players.TryGetValue((eventType, pattern), out var player))
                {
                    player = new ClipPlayer(BladeHaptics.BuildClipJson(BladeHaptics.GetSegments(eventType, pattern)));
                    _players[(eventType, pattern)] = player;
                }

                _playing?.Player.Stop();
                player.Player.amplitude = Mathf.Clamp01(strength);
                player.Player.Play(swordHand == Hand.Left ? Controller.Left : Controller.Right);
                _playing = player;
            }
            catch (Exception error)
            {
                // Haptics are feedback, not the judgement: Touches still count and flash without them.
                _hasFailed = true;
                Debug.LogError($"[Blade] Haptics failed, off for this session: {error.Message}");
            }
        }

        public void Dispose()
        {
            foreach (var player in _players.Values)
            {
                player.Dispose();
            }

            _players.Clear();
            _playing = null;
        }

        private sealed class ClipPlayer : IDisposable
        {
            private readonly HapticClip _clip;

            public ClipPlayer(string clipJson)
            {
                _clip = ScriptableObject.CreateInstance<HapticClip>();
                _clip.json = clipJson;
                Player = new HapticClipPlayer(_clip);
            }

            public HapticClipPlayer Player { get; }

            public void Dispose()
            {
                Player.Dispose();
                UnityEngine.Object.Destroy(_clip);
            }
        }
    }
}
