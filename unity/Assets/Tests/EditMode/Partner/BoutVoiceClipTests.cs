// The referee's voice (PER-83, clips from PER-75): the four LoquaxFR calls ship in Resources, short enough to fit the
// one second between calls.
using NUnit.Framework;
using UnityEngine;

namespace VrFencing.Tests.Partner
{
    public class BoutVoiceClipTests
    {
        [TestCase("en_garde")]
        [TestCase("prets")]
        [TestCase("allez")]
        [TestCase("halte")]
        public void EachCallShipsAsAShortClip(string clipName)
        {
            var clip = Resources.Load<AudioClip>($"BoutCalls/{clipName}");

            Assert.That(clip, Is.Not.Null, $"Resources/BoutCalls/{clipName} is missing; the call would fall back to a tone.");
            Assert.That(clip.length, Is.InRange(0.2f, 0.9f), "Under the 1 s between calls.");
        }
    }
}
