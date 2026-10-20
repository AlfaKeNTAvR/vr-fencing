using System;
using System.Collections.Generic;
using UnityEngine;

namespace VrFencing.Partner
{
    /// <summary>
    /// Speaks the referee's calls (PER-83, voice from PER-75): the LoquaxFR clips in <c>Resources/BoutCalls/</c>, played
    /// flat (not placed in the room) so both Fencers hear them alike. A clip that is missing falls back to a short tone,
    /// a lower one for "Halte!" (the ticket's text-plus-tone fallback); the large text shows either way.
    /// </summary>
    internal sealed class BoutVoice : IDisposable
    {
        /// <summary>The Resources folder the clips live in.</summary>
        public const string ClipFolder = "BoutCalls";

        private const int ToneSampleRate = 48000;
        private const float ToneSeconds = 0.25f;
        private const float CallToneHertz = 880.0f;
        private const float HalteToneHertz = 440.0f;
        private const float ToneVolume = 0.4f;

        private static readonly Dictionary<BoutCallClip, string> ClipNames = new Dictionary<BoutCallClip, string>
        {
            [BoutCallClip.EnGarde] = "en_garde",
            [BoutCallClip.Prets] = "prets",
            [BoutCallClip.Allez] = "allez",
            [BoutCallClip.Halte] = "halte",
        };

        private readonly Dictionary<BoutCallClip, AudioClip> _clips = new Dictionary<BoutCallClip, AudioClip>();
        private readonly GameObject _speaker;
        private readonly AudioSource _source;

        public BoutVoice()
        {
            _speaker = new GameObject("Bout Voice");
            _source = _speaker.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0.0f;
            foreach (var pair in ClipNames)
            {
                var clip = Resources.Load<AudioClip>($"{ClipFolder}/{pair.Value}");
                if (clip == null)
                {
                    Debug.LogWarning($"[Bout] Voice clip 'Resources/{ClipFolder}/{pair.Value}' is missing; a tone plays for \"{BoutCall.Word(pair.Key)}\".");
                    clip = CreateTone(pair.Key == BoutCallClip.Halte ? HalteToneHertz : CallToneHertz);
                }

                _clips[pair.Key] = clip;
            }
        }

        /// <summary>Speaks <paramref name="call"/>, cutting off a call still sounding.</summary>
        public void Say(BoutCallClip call)
        {
            _source.Stop();
            _source.clip = _clips[call];
            _source.Play();
        }

        public void Dispose()
        {
            if (_speaker != null)
            {
                UnityEngine.Object.Destroy(_speaker);
            }
        }

        private static AudioClip CreateTone(float hertz)
        {
            var count = (int)(ToneSampleRate * ToneSeconds);
            var samples = new float[count];
            for (var index = 0; index < count; index++)
            {
                // A short fade in and out, so the tone does not click.
                var fade = Mathf.Min(1.0f, Mathf.Min(index, count - index) / (ToneSampleRate * 0.01f));
                samples[index] = ToneVolume * fade * Mathf.Sin(2.0f * Mathf.PI * hertz * index / ToneSampleRate);
            }

            var tone = AudioClip.Create($"Tone {hertz} Hz", count, 1, ToneSampleRate, false);
            tone.SetData(samples, 0);
            return tone;
        }
    }
}
