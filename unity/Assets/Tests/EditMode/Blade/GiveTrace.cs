// What the blade-contact tests of PER-147 measure on the contact solver's output each frame: how far each drawn blade
// turned in the grip, how far each drawn point moved beyond its own hand's point (past what the other hand's push that
// frame bends it by), and how often that move turned back on itself (a shake). Meters, seconds, degrees.
using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Blade;

namespace VrFencing.Tests.Blade
{
    internal sealed class GiveTrace
    {
        /// <summary>The most a drawn blade may turn in the grip: the release's 90 degrees, and a few for the grip's own turn.</summary>
        public const float MostTurnDegrees = 95.0f;

        /// <summary>
        /// The most a drawn point may move in one frame beyond its own hand's point's move, past the bend the press itself
        /// brings (<see cref="PressLever"/>).
        /// </summary>
        public const float MostTipJumpMeters = 0.10f;

        /// <summary>
        /// While the other hand presses deeper, the drawn point follows the bend: a bow loaded at the middle of the blade
        /// moves the point 2.5 times as far as the spot pressed. So much of a frame's deeper press is the push, not a jump.
        /// </summary>
        public const float PressLever = 3.0f;

        /// <summary>A reversal: a drawn point's move off its hand of at least this, against the frame before's.</summary>
        public const float ReversalMeters = 0.03f;

        public const double ReversalWindowSeconds = 0.3;

        /// <summary>A shake is this many reversals within the window; fewer is a single bounce at most.</summary>
        public const int ShakeReversals = 2;

        private readonly Blade _a = new Blade();
        private readonly Blade _b = new Blade();
        private float _lastPressMeters;

        public float MostTurn { get; private set; }

        /// <summary>The most a drawn point moved in a frame beyond its hand's, past what the deeper press accounts for.</summary>
        public float MostTipJump { get; private set; }

        /// <summary>The most a drawn point moved in a frame beyond its hand's, the press's bend included.</summary>
        public float MostRawTipJump { get; private set; }

        public double MostTipJumpSeconds { get; private set; }

        public int MostReversals => Math.Max(_a.MostReversals, _b.MostReversals);

        /// <summary>When each drawn point reversed, A's and B's, for the report.</summary>
        public string ReversalTimes => FormattableString.Invariant($"A at {string.Join(", ", _a.AllReversals.ConvertAll(t => t.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)))}; B at {string.Join(", ", _b.AllReversals.ConvertAll(t => t.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)))}");

        /// <summary>This frame's move of each drawn point beyond its hand's.</summary>
        public float LastJumpA { get; private set; }

        public float LastJumpB { get; private set; }

        /// <summary>
        /// One frame: each hand's blade and what the contact gives it, and how far the hands' blades press into each
        /// other (0 when they do not).
        /// </summary>
        public void Add(double seconds, BladeSegment handA, BladeGive giveA, BladeSegment handB, BladeGive giveB, float pressMeters)
        {
            MostTurn = Math.Max(MostTurn, Math.Max(giveA.TurnDegrees, giveB.TurnDegrees));
            LastJumpA = _a.Add(seconds, giveA.Chord.TipPosition - handA.TipPosition);
            LastJumpB = _b.Add(seconds, giveB.Chord.TipPosition - handB.TipPosition);
            var pushed = PressLever * Math.Max(0.0f, pressMeters - _lastPressMeters);
            _lastPressMeters = pressMeters;
            foreach (var jump in new[] { LastJumpA, LastJumpB })
            {
                MostRawTipJump = Math.Max(MostRawTipJump, jump);
                if (jump - pushed > MostTipJump)
                {
                    MostTipJump = jump - pushed;
                    MostTipJumpSeconds = seconds;
                }
            }
        }

        /// <summary>The contact started over (a blade appeared or jumped): the next frame's move is not compared with the last.</summary>
        public void Break()
        {
            _a.Break();
            _b.Break();
            _lastPressMeters = 0.0f;
        }

        public void AssertBounded(string context)
        {
            Assert.That(MostTurn, Is.LessThanOrEqualTo(MostTurnDegrees), $"Every turn within the release's. {this} {context}");
            Assert.That(MostTipJump, Is.LessThanOrEqualTo(MostTipJumpMeters), $"No drawn point jumps beyond its hand. {this} {context}");
            Assert.That(MostReversals, Is.LessThan(ShakeReversals), $"No shake. {this} {context}");
        }

        public override string ToString()
        {
            return FormattableString.Invariant(
                $"Most turn {MostTurn:0.0} deg, most point jump beyond the hand {MostTipJump * 100:0.0} cm past the press's bend (at {MostTipJumpSeconds:0.000} s; {MostRawTipJump * 100:0.0} cm with it), most reversals {MostReversals} in {ReversalWindowSeconds} s ({ReversalTimes}).");
        }

        private sealed class Blade
        {
            private readonly List<double> _reversals = new List<double>();

            public List<double> AllReversals { get; } = new List<double>();
            private Vector3? _lastOffset;
            private Vector3? _lastStep;

            public int MostReversals { get; private set; }

            /// <summary>Takes this frame's offset of the drawn point from the hand's; returns its move since the last frame.</summary>
            public float Add(double seconds, Vector3 offset)
            {
                var jump = 0.0f;
                if (_lastOffset.HasValue)
                {
                    var step = offset - _lastOffset.Value;
                    jump = step.Length();
                    if (_lastStep.HasValue && jump >= ReversalMeters && _lastStep.Value.LengthSquared() > 1e-12f
                        && Vector3.Dot(step, _lastStep.Value) < -0.3f * jump * _lastStep.Value.Length())
                    {
                        _reversals.Add(seconds);
                        AllReversals.Add(seconds);
                        while (seconds - _reversals[0] > ReversalWindowSeconds)
                        {
                            _reversals.RemoveAt(0);
                        }

                        MostReversals = Math.Max(MostReversals, _reversals.Count);
                    }

                    _lastStep = step;
                }

                _lastOffset = offset;
                return jump;
            }

            public void Break()
            {
                _lastOffset = null;
                _lastStep = null;
            }
        }
    }
}
