using System;

namespace VrFencing.Dummy
{
    /// <summary>One move of <see cref="FootworkShowcase"/>.</summary>
    public enum ShowcaseMove
    {
        /// <summary>Standing in en garde: breathing and shifting weight.</summary>
        Stand,

        /// <summary>Two advances toward the Fencer.</summary>
        Advance,

        /// <summary>Retreats back to where the showcase started.</summary>
        Retreat,

        /// <summary>A lunge, a short hold, and the recovery.</summary>
        Lunge,

        /// <summary>A fleche toward the Fencer.</summary>
        Fleche,

        /// <summary>A side step to the Fencer's right (PER-118).</summary>
        SideStep,

        /// <summary>A diagonal advance back onto the Strip's center line (PER-118).</summary>
        Diagonal,
    }

    /// <summary>
    /// The footwork feel test's script (PER-111, development builds only): drives a <see cref="DummyFootwork"/> through
    /// every move it has, over and over, so the moves can be watched on the headset. Stand, two advances, stand, retreat
    /// home, stand, lunge and recover, stand, fleche, stand, retreat home, stand, a side step, stand, a diagonal advance
    /// back to the center line (PER-118), stand, retreat home, and again. The lunge is timed here, as long as
    /// the Armed Dummy's (<see cref="ArmedDummyAttack.GetLungeMeters"/>), on a minimum-jerk curve.
    /// </summary>
    public sealed class FootworkShowcase
    {
        /// <summary>The pause in en garde between moves, long enough to see the breathing and weight shift.</summary>
        public const float StandSeconds = 1.5f;

        /// <summary>How many advances the advance move makes.</summary>
        public const int AdvanceCount = 2;

        /// <summary>
        /// The showcase lunge's way out: about the Armed Dummy's at medium speed, and slow enough that no part moves more
        /// than the Blade's 5 cm sweep in a 72 Hz frame for a Dummy of up to 2.1 m.
        /// </summary>
        public const float LungeOutSeconds = 1.2f;

        /// <summary>The showcase lunge holds at full length this long.</summary>
        public const float LungeHoldSeconds = 0.4f;

        /// <summary>
        /// The showcase recovery: a little slower than the Armed Dummy's 0.6 s (<see cref="ArmedDummyTuning.RecoverSeconds"/>,
        /// stretched to 0.73 s for a 2.1 m Dummy so it comes back at no more than 4.5 cm a frame).
        /// </summary>
        public const float LungeBackSeconds = 0.8f;

        private static readonly ShowcaseMove[] Script =
        {
            ShowcaseMove.Stand, ShowcaseMove.Advance, ShowcaseMove.Stand, ShowcaseMove.Retreat,
            ShowcaseMove.Stand, ShowcaseMove.Lunge, ShowcaseMove.Stand, ShowcaseMove.Fleche,
            ShowcaseMove.Stand, ShowcaseMove.Retreat, ShowcaseMove.Stand, ShowcaseMove.SideStep,
            ShowcaseMove.Stand, ShowcaseMove.Diagonal, ShowcaseMove.Stand, ShowcaseMove.Retreat,
        };

        private readonly DummyFootwork _footwork;
        private readonly float _homeMeters;
        private readonly float _advanceMeters;
        private readonly float _sideStepMeters;
        private readonly float _lungeMeters;
        private int _scriptIndex;
        private float _moveSeconds;
        private bool _hasMoveStarted;

        /// <param name="footwork">The footwork to drive, standing where the showcase is to start.</param>
        /// <param name="heightMeters">The Dummy's height: sets the advance and lunge lengths.</param>
        /// <exception cref="ArgumentOutOfRangeException">The height is not positive.</exception>
        public FootworkShowcase(DummyFootwork footwork, float heightMeters)
        {
            _footwork = footwork ?? throw new ArgumentNullException(nameof(footwork));
            DummyLayout.RequirePositiveHeight(heightMeters);
            _homeMeters = footwork.DistanceMeters;
            _advanceMeters = AdvanceCount * FootworkTuning.MaxStridePerHeight * heightMeters;
            _lungeMeters = ArmedDummyAttack.GetLungeMeters(heightMeters);
            _sideStepMeters = FootworkTuning.MaxSideStridePerHeight * heightMeters;
        }

        /// <summary>The move under way.</summary>
        public ShowcaseMove CurrentMove => Script[_scriptIndex];

        /// <summary>Raised when a new move starts, for the log.</summary>
        public event Action<ShowcaseMove> MoveStarted;

        /// <summary>One frame: steps the footwork for the move under way, and starts the next once it is done.</summary>
        public void Step(float deltaSeconds)
        {
            if (!(deltaSeconds > 0.0f))
            {
                return;
            }

            if (!_hasMoveStarted)
            {
                _hasMoveStarted = true;
                _moveSeconds = 0.0f;
                if (CurrentMove == ShowcaseMove.Fleche)
                {
                    _footwork.StartFleche();
                }

                MoveStarted?.Invoke(CurrentMove);
            }

            _moveSeconds += deltaSeconds;
            if (StepMove(deltaSeconds))
            {
                _scriptIndex = (_scriptIndex + 1) % Script.Length;
                _hasMoveStarted = false;
            }
        }

        /// <summary>Steps the move under way; true once it is over and the Dummy stands again.</summary>
        private bool StepMove(float deltaSeconds)
        {
            switch (CurrentMove)
            {
                case ShowcaseMove.Advance:
                    _footwork.Step(_homeMeters - _advanceMeters, 0.0f, deltaSeconds);
                    return IsStandingAt(_homeMeters - _advanceMeters, 0.0f);
                case ShowcaseMove.Retreat:
                    _footwork.Step(_homeMeters, 0.0f, deltaSeconds);
                    return IsStandingAt(_homeMeters, 0.0f);
                case ShowcaseMove.SideStep:
                    _footwork.Step(_homeMeters, _sideStepMeters, deltaSeconds);
                    return IsStandingAt(_homeMeters, _sideStepMeters);
                case ShowcaseMove.Diagonal:
                    // As far forward as across: one diagonal step, within a stride.
                    _footwork.Step(_homeMeters - _sideStepMeters, 0.0f, deltaSeconds);
                    return IsStandingAt(_homeMeters - _sideStepMeters, 0.0f);
                case ShowcaseMove.Lunge:
                    _footwork.StepLunge(GetLungeOffsetMeters(_moveSeconds), _lungeMeters, deltaSeconds);
                    return _moveSeconds >= LungeOutSeconds + LungeHoldSeconds + LungeBackSeconds
                        && _footwork.CurrentStep == FootworkStep.Standing;
                case ShowcaseMove.Fleche:
                    _footwork.Step(_footwork.DistanceMeters, deltaSeconds);
                    return _footwork.CurrentStep == FootworkStep.Standing;
                default:
                    _footwork.Step(_footwork.DistanceMeters, deltaSeconds);
                    return _moveSeconds >= StandSeconds;
            }
        }

        private bool IsStandingAt(float distanceMeters, float sideMeters)
        {
            return _footwork.CurrentStep == FootworkStep.Standing
                && Math.Abs(_footwork.DistanceMeters - distanceMeters) <= FootworkTuning.MinStepMeters
                && Math.Abs(_footwork.SideMeters - sideMeters) <= FootworkTuning.MinStepMeters;
        }

        /// <summary>Out on a minimum-jerk curve, held, then back on one.</summary>
        private float GetLungeOffsetMeters(float seconds)
        {
            if (seconds < LungeOutSeconds)
            {
                return _lungeMeters * DummyFootwork.MinimumJerk(seconds / LungeOutSeconds);
            }

            var backSeconds = seconds - LungeOutSeconds - LungeHoldSeconds;
            return backSeconds <= 0.0f ? _lungeMeters : _lungeMeters * (1.0f - DummyFootwork.MinimumJerk(backSeconds / LungeBackSeconds));
        }
    }
}
