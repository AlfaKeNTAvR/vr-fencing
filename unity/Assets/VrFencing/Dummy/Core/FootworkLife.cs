using System;
using System.Numerics;

namespace VrFencing.Dummy
{
    /// <summary>
    /// The small motions that make the standing Dummy look alive (PER-111): it breathes, the hips rising and settling a
    /// little with each breath, and its weight drifts between the front and back leg. Both move the hips in the Dummy
    /// frame, with the feet planted; the upper body rides on them and leans a little with the weight shift (PER-115,
    /// <see cref="DummyTorso"/>).
    ///
    /// Each breath's length is drawn anew, and the weight shift is two slow waves of unrelated periods with seeded
    /// phases, so the motion never repeats exactly; the same seed repeats it exactly, for tests. Continuous in time: no
    /// frame jumps. Nothing is allocated per frame.
    /// </summary>
    internal sealed class FootworkLife
    {
        private const float FullTurn = (float)(2.0 * Math.PI);

        private readonly float _heightMeters;
        private readonly Random _random;
        private readonly float _slowPhase;
        private readonly float _fastPhase;
        private float _seconds;
        private float _breathPhase;
        private float _breathSeconds;

        public FootworkLife(float heightMeters, Random random)
        {
            _heightMeters = heightMeters;
            _random = random;
            _slowPhase = (float)random.NextDouble() * FullTurn;
            _fastPhase = (float)random.NextDouble() * FullTurn;
            _breathPhase = (float)random.NextDouble();
            _breathSeconds = DrawBreathSeconds();
            Advance(0.0f);
        }

        /// <summary>How far the hips are from their en garde place now, in the Dummy frame (y up, z forward).</summary>
        public Vector3 HipOffset { get; private set; }

        public void Advance(float deltaSeconds)
        {
            _seconds += deltaSeconds;
            _breathPhase += deltaSeconds / _breathSeconds;
            if (_breathPhase >= 1.0f)
            {
                // A new breath starts at the bottom of the wave, where its length can change without a jump.
                _breathPhase -= (float)Math.Floor(_breathPhase);
                _breathSeconds = DrawBreathSeconds();
            }

            // In from the bottom (0) to the top (1) and out again: (1 - cos) / 2.
            var breath = 0.5f * (1.0f - (float)Math.Cos(_breathPhase * FullTurn));
            var slow = (float)Math.Sin((_seconds / FootworkTuning.WeightShiftSlowSeconds * FullTurn) + _slowPhase);
            var fast = (float)Math.Sin((_seconds / FootworkTuning.WeightShiftFastSeconds * FullTurn) + _fastPhase);
            var shift = ((1.0f - FootworkTuning.WeightShiftFastShare) * slow) + (FootworkTuning.WeightShiftFastShare * fast);

            HipOffset = new Vector3(
                0.0f,
                FootworkTuning.BreathHipRisePerHeight * _heightMeters * breath,
                FootworkTuning.WeightShiftPerHeight * _heightMeters * shift);
        }

        private float DrawBreathSeconds()
        {
            var variation = ((float)_random.NextDouble() * 2.0f) - 1.0f;
            return FootworkTuning.BreathSeconds * (1.0f + (variation * FootworkTuning.BreathVariationFraction));
        }
    }
}
