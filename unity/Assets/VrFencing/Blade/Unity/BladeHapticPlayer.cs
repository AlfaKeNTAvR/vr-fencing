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
        private ClipPlayer _pressPlayer;
        private ClipPlayer _lagPlayer;
        private ClipPlayer _clickPlayer;
        private readonly Dictionary<HapticPattern, ClipPlayer> _clashPlayers = new Dictionary<HapticPattern, ClipPlayer>();
        private ClipPlayer _beingTouchedPlayer;
        private ClipPlayer _armedDummyHitPlayer;
        private float _eventEndsAt = float.NegativeInfinity;
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
                    var segments = BladeHaptics.GetSegments(eventType, pattern);
                    player = new ClipPlayer(BladeHaptics.BuildClipJson(segments), segments);
                    _players[(eventType, pattern)] = player;
                }

                StartPlaying(player, strength, swordHand);
                _eventEndsAt = Time.time + player.DurationSeconds;
            }
            catch (Exception error)
            {
                Fail(error);
            }
        }

        /// <summary>
        /// One pulse of the press buzz (<see cref="BladeHaptics.PressPulse"/>) while the point is pinned, at
        /// <paramref name="strength"/>. An event pattern still playing (the Touch) is not cut short.
        /// </summary>
        public void PlayPress(float strength, Hand swordHand)
        {
            if (_hasFailed || Time.time < _eventEndsAt)
            {
                return;
            }

            try
            {
                _pressPlayer ??= new ClipPlayer(BladeHaptics.BuildClipJson(BladeHaptics.PressPulse), BladeHaptics.PressPulse);
                StartPlaying(_pressPlayer, strength, swordHand);
            }
            catch (Exception error)
            {
                Fail(error);
            }
        }

        /// <summary>
        /// One pulse of the lag buzz (<see cref="BladeHaptics.LagPulse"/>, PER-121) while the drawn hilt trails the hand,
        /// at <paramref name="strength"/>. An event pattern still playing is not cut short.
        /// </summary>
        public void PlayLag(float strength, Hand swordHand)
        {
            if (_hasFailed || Time.time < _eventEndsAt || !(strength > 0.0f))
            {
                return;
            }

            try
            {
                _lagPlayer ??= new ClipPlayer(BladeHaptics.BuildClipJson(BladeHaptics.LagPulse), BladeHaptics.LagPulse);
                StartPlaying(_lagPlayer, strength, swordHand);
            }
            catch (Exception error)
            {
                Fail(error);
            }
        }

        /// <summary>The leash's click (<see cref="BladeHaptics.LeashClick"/>, PER-121) at full strength: the drawn blade popped back to the hand.</summary>
        public void PlayLeashClick(Hand swordHand)
        {
            if (_hasFailed)
            {
                return;
            }

            try
            {
                _clickPlayer ??= new ClipPlayer(BladeHaptics.BuildClipJson(BladeHaptics.LeashClick), BladeHaptics.LeashClick);
                StartPlaying(_clickPlayer, 1.0f, swordHand);
                _eventEndsAt = Time.time + _clickPlayer.DurationSeconds;
            }
            catch (Exception error)
            {
                Fail(error);
            }
        }

        /// <summary>
        /// The chosen clash buzz (<see cref="ClashHaptics"/>) when the Fencer's blade meets the Armed Dummy's (PER-99), at
        /// <paramref name="strength"/>; it replaces whatever is playing. Off plays nothing.
        /// </summary>
        public void PlayClash(HapticPattern pattern, float strength, Hand swordHand)
        {
            if (_hasFailed || pattern == HapticPattern.Off)
            {
                return;
            }

            try
            {
                if (!_clashPlayers.TryGetValue(pattern, out var player))
                {
                    var segments = ClashHaptics.GetSegments(pattern);
                    player = new ClipPlayer(BladeHaptics.BuildClipJson(segments), segments);
                    _clashPlayers[pattern] = player;
                }

                StartPlaying(player, strength, swordHand);
                _eventEndsAt = Time.time + player.DurationSeconds;
            }
            catch (Exception error)
            {
                Fail(error);
            }
        }

        /// <summary>
        /// The being-touched buzz (<see cref="BladeHaptics.BeingTouched"/>, PER-82) on both controllers at full strength;
        /// it replaces whatever is playing.
        /// </summary>
        public void PlayBeingTouched()
        {
            PlayOnBothControllers(ref _beingTouchedPlayer, BladeHaptics.BeingTouched);
        }

        /// <summary>
        /// The Armed Dummy's point landing on the Fencer (<see cref="BladeHaptics.ArmedDummyHit"/>, PER-99) on both
        /// controllers at full strength; it replaces whatever is playing.
        /// </summary>
        public void PlayArmedDummyHit()
        {
            PlayOnBothControllers(ref _armedDummyHitPlayer, BladeHaptics.ArmedDummyHit);
        }

        private void PlayOnBothControllers(ref ClipPlayer player, IReadOnlyList<HapticSegment> segments)
        {
            if (_hasFailed)
            {
                return;
            }

            try
            {
                player ??= new ClipPlayer(BladeHaptics.BuildClipJson(segments), segments);
                _playing?.Player.Stop();
                player.Player.amplitude = 1.0f;
                player.Player.Play(Controller.Both);
                _playing = player;
                _eventEndsAt = Time.time + player.DurationSeconds;
            }
            catch (Exception error)
            {
                Fail(error);
            }
        }

        private void StartPlaying(ClipPlayer player, float strength, Hand swordHand)
        {
            _playing?.Player.Stop();
            player.Player.amplitude = Mathf.Clamp01(strength);
            player.Player.Play(swordHand == Hand.Left ? Controller.Left : Controller.Right);
            _playing = player;
        }

        private void Fail(Exception error)
        {
            // Haptics are feedback, not the judgement: Touches still count and flash without them.
            _hasFailed = true;
            Debug.LogError($"[Blade] Haptics failed, off for this session: {error.Message}");
        }

        public void Dispose()
        {
            foreach (var player in _players.Values)
            {
                player.Dispose();
            }

            _players.Clear();
            _pressPlayer?.Dispose();
            _pressPlayer = null;
            _lagPlayer?.Dispose();
            _lagPlayer = null;
            _clickPlayer?.Dispose();
            _clickPlayer = null;
            foreach (var player in _clashPlayers.Values)
            {
                player.Dispose();
            }

            _clashPlayers.Clear();
            _beingTouchedPlayer?.Dispose();
            _beingTouchedPlayer = null;
            _armedDummyHitPlayer?.Dispose();
            _armedDummyHitPlayer = null;
            _playing = null;
        }

        private sealed class ClipPlayer : IDisposable
        {
            private readonly HapticClip _clip;

            public ClipPlayer(string clipJson, IReadOnlyList<HapticSegment> segments)
            {
                _clip = ScriptableObject.CreateInstance<HapticClip>();
                _clip.json = clipJson;
                Player = new HapticClipPlayer(_clip);
                foreach (var segment in segments)
                {
                    DurationSeconds += segment.DurationSeconds;
                }
            }

            public HapticClipPlayer Player { get; }

            public float DurationSeconds { get; }

            public void Dispose()
            {
                Player.Dispose();
                UnityEngine.Object.Destroy(_clip);
            }
        }
    }
}
