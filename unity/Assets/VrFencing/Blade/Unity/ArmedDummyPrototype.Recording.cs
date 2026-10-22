using VrFencing.Dummy;

namespace VrFencing.Blade
{
    /// <summary>
    /// What the blade-contact log (PER-136) reads from the Armed Dummy each frame: its blade, hand and contact through
    /// <see cref="BladeContactProbe"/>, why it is armed and what its arm does, and the events since the last read, found
    /// from its state (a new clash, a Beat or Parry reported, a hit landed) so the Dummy's own code needs no hooks.
    /// Read-only.
    /// </summary>
    internal sealed partial class ArmedDummyPrototype
    {
        // AttackPhase in declaration order.
        private static readonly string[] AttackPhaseNames = { "waiting", "wind_up", "thrust", "hold", "recover", "done" };

        private double _recordedClashSeconds = double.NaN;
        private bool _hasRecordedBeat;
        private ArmedDummyAttack _recordedAttack;
        private bool _hasRecordedParry;
        private bool _hasRecordedHit;

        /// <summary>
        /// Fills the Dummy's part of <paramref name="sample"/> after this frame's step (the contact and Dummy fields, or
        /// empties them while unarmed) and returns the events since the last read.
        /// </summary>
        public BladeLogEvents ReadForRecording(ref BladeContactSample sample, DrawnBlade fencerDrawn, double nowSeconds)
        {
            sample.OpponentHand = _settings.ArmedDummy.OpponentHand;
            sample.DummyMode = GetModeName();
            sample.DummyAction = GetActionName(nowSeconds);
            if (_isActive)
            {
                BladeContactProbe.ReadArmed(ref sample, _blade, fencerDrawn);
            }
            else
            {
                BladeContactProbe.ClearArmed(ref sample);
            }

            return TakeEvents();
        }

        /// <summary>Forgets the events so far: a recording starts with none from before it.</summary>
        public void ForgetRecordingEvents()
        {
            TakeEvents();
        }

        private BladeLogEvents TakeEvents()
        {
            var events = BladeLogEvents.None;
            if (_lastClash.HasValue && _lastClash.Value.TimeSeconds != _recordedClashSeconds)
            {
                _recordedClashSeconds = _lastClash.Value.TimeSeconds;
                events |= BladeLogEvents.Clash;
            }

            if (_hasReportedBeat && !_hasRecordedBeat)
            {
                events |= BladeLogEvents.Beat;
            }

            _hasRecordedBeat = _hasReportedBeat;
            if (_attack != _recordedAttack)
            {
                // A new attack starts with nothing judged yet.
                _recordedAttack = _attack;
                _hasRecordedParry = false;
                _hasRecordedHit = false;
            }

            if (_attack != null && _attack.IsParried && !_hasRecordedParry)
            {
                _hasRecordedParry = true;
                events |= BladeLogEvents.Parry;
            }

            if (_attack != null && _attack.HasLanded && !_hasRecordedHit)
            {
                _hasRecordedHit = true;
                events |= BladeLogEvents.HitOnFencer;
            }

            return events;
        }

        private string GetModeName()
        {
            switch (_mode)
            {
                case ArmedMode.Drill: return "drill";
                case ArmedMode.FreeGuard: return "free_guard";
                case ArmedMode.FreeAttacks: return "free_attacks";
                default: return "unarmed";
            }
        }

        private string GetActionName(double nowSeconds)
        {
            if (!_isActive)
            {
                return "none";
            }

            if (_attack != null)
            {
                var phase = (int)_attack.GetPhase(nowSeconds);
                return phase >= 0 && phase < AttackPhaseNames.Length ? AttackPhaseNames[phase] : "attack";
            }

            return _inLinePose.HasValue ? "blade_in_line" : "en_garde";
        }
    }
}
