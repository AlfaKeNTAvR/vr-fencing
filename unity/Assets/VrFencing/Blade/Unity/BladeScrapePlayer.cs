using System;
using UnityEngine;

namespace VrFencing.Blade
{
    /// <summary>
    /// Plays the steel scrape where the blades press on each other (PER-122): one looping procedural clip
    /// (<see cref="ScrapeSound.BuildLoop"/>), placed at the contact in 3D, its volume and pitch eased toward what
    /// <see cref="ScrapeSound"/> asks each frame so it never clicks. All numbers are in the Core.
    /// </summary>
    internal sealed class BladeScrapePlayer : IDisposable
    {
        // How quickly the volume and pitch follow what is asked, per second: about 30 ms to settle, no click.
        private const float FollowPerSecond = 30.0f;

        // Heard fully within half a meter, fading beyond: the blades are always within arm's reach.
        private const float MinDistanceMeters = 0.5f;
        private const float MaxDistanceMeters = 6.0f;

        private readonly GameObject _speaker;
        private readonly AudioSource _source;
        private float _targetVolume;
        private float _targetPitch = ScrapeSound.LowPitch;

        private BladeScrapePlayer(GameObject speaker, AudioSource source)
        {
            _speaker = speaker;
            _source = source;
        }

        /// <summary>The player, or null (with an error logged) when the clip cannot be made.</summary>
        public static BladeScrapePlayer TryCreate()
        {
            try
            {
                var sampleRate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
                var samples = ScrapeSound.BuildLoop(sampleRate);
                var clip = AudioClip.Create("Blade Scrape", samples.Length, 1, sampleRate, false);
                clip.SetData(samples, 0);
                var speaker = new GameObject("Blade Scrape");
                var source = speaker.AddComponent<AudioSource>();
                source.clip = clip;
                source.loop = true;
                source.playOnAwake = false;
                source.volume = 0.0f;
                source.spatialBlend = 1.0f;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = MinDistanceMeters;
                source.maxDistance = MaxDistanceMeters;
                return new BladeScrapePlayer(speaker, source);
            }
            catch (Exception error)
            {
                Debug.LogError($"[Blade] The scrape sound could not be made, so it stays silent: {error.Message}");
                return null;
            }
        }

        /// <summary>This frame's scrape at <paramref name="position"/> (room frame), at the volume and pitch asked.</summary>
        public void Play(Vector3 position, float volume, float pitch)
        {
            _speaker.transform.position = position;
            _targetVolume = Mathf.Clamp01(volume);
            _targetPitch = pitch;
            if (!_source.isPlaying)
            {
                _source.pitch = pitch;
                _source.Play();
            }

            Follow();
        }

        /// <summary>Fades the scrape out: the blades let go.</summary>
        public void Silence()
        {
            _targetVolume = 0.0f;
            if (_source.isPlaying)
            {
                Follow();
                if (_source.volume < 1e-3f)
                {
                    _source.Stop();
                }
            }
        }

        public void Dispose()
        {
            if (_speaker != null)
            {
                UnityEngine.Object.Destroy(_speaker);
            }
        }

        private void Follow()
        {
            var step = 1.0f - Mathf.Exp(-FollowPerSecond * Time.deltaTime);
            _source.volume = Mathf.Lerp(_source.volume, _targetVolume, step);
            _source.pitch = Mathf.Lerp(_source.pitch, _targetPitch, step);
        }
    }
}
