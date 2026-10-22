using System;
using System.Numerics;
using VrFencing.Dummy;

namespace VrFencing.Blade
{
    /// <summary>Where a <see cref="BladeDeflection"/> is.</summary>
    public enum DeflectionPhase
    {
        /// <summary>
        /// In line, or moved by a light touch or a press: the point rides a quick damped spring back to its line, or to
        /// where the press holds it (the fingers absorb a light touch).
        /// </summary>
        Spring,

        /// <summary>Knocked out: the point travels to the deflection.</summary>
        Out,

        /// <summary>Held out: the line is open, the Fencer's chance to attack.</summary>
        Open,

        /// <summary>A deliberate, smooth return to the line, with no overshoot.</summary>
        Recover,
    }

    /// <summary>One knock or release that takes the point well off its line, for the log (PER-116).</summary>
    public readonly struct DeflectionPlan
    {
        public DeflectionPlan(float meters, float wristMeters, float armMeters, float openSeconds, float recoverSeconds)
        {
            Meters = meters;
            WristMeters = wristMeters;
            ArmMeters = armMeters;
            OpenSeconds = openSeconds;
            RecoverSeconds = recoverSeconds;
        }

        /// <summary>The point's whole offset from its line.</summary>
        public float Meters { get; }

        /// <summary>The part the blade turns in the hand.</summary>
        public float WristMeters { get; }

        /// <summary>The part the whole arm is knocked aside at the shoulder.</summary>
        public float ArmMeters { get; }

        public float OpenSeconds { get; }

        public float RecoverSeconds { get; }
    }

    /// <summary>
    /// The Armed Dummy's point knocked off its line by the Fencer's blade (PER-116, replacing the PER-95 swing): an
    /// offset of the point, square to the blade, that grows with how hard and where the blades met, and comes back the
    /// way a fencer's arm does, not on a spring.
    /// <list type="bullet">
    /// <item><b>Beat.</b> A clash at <see cref="BeatMinSpeedMetersPerSecond"/> or faster knocks the point
    /// <see cref="BeatMetersPerMeterPerSecond"/> per m/s, scaled from <see cref="GuardLeverage"/> at the Dummy's guard to
    /// the whole at its point (<see cref="GetKickMeters"/>), at most <see cref="MaxDeflectionMeters"/>. A knock of
    /// <see cref="LightTouchMeters"/> or more runs three phases: out in <see cref="OutSeconds"/>, held open for
    /// <see cref="GetOpenSeconds"/>, then a deliberate minimum-jerk return over <see cref="GetRecoverSeconds"/>; both grow
    /// with the deflection. A new hit while out, open or recovering adds to the deflection and opens it again.</item>
    /// <item><b>Light touch.</b> A slower clash, or a knock under <see cref="LightTouchMeters"/>, is the old quick
    /// spring (<see cref="FrequencyHertz"/>), at most <see cref="LightTouchMeters"/> for a slow one.</item>
    /// <item><b>Press.</b> The Fencer's blade held on the Dummy's pulls the spring to where the Dummy's hand gives way
    /// to it (<see cref="HoldAt"/>, <see cref="ArmedDummyBlade.FindYieldTipOffset"/>, PER-122); let go after it moved
    /// the point <see cref="LightTouchMeters"/> or more, the point stays open and recovers as after a beat. The blades
    /// themselves never pass through each other (<see cref="BladeContactSolver"/>): this is only the hand.</item>
    /// <item><b>Wrist and arm.</b> The first <see cref="WristMaxMeters"/> of the offset is the blade turning in the hand;
    /// the rest, up to <see cref="ArmMaxMeters"/>, knocks the whole arm aside at the shoulder (<see cref="ArmOffset"/>,
    /// <see cref="GetArmTurn"/>), following at most <see cref="MaxArmPointSpeedMetersPerSecond"/> so the arm's Target
    /// Zones stay within the Blade's 5 cm a frame (PER-114).</item>
    /// </list>
    /// Positions in the Strip frame, meters, seconds.
    /// </summary>
    public sealed class BladeDeflection
    {
        /// <summary>
        /// The light-touch spring's natural frequency: knocked, it peaks in about 0.1 s; let go off line, it is back
        /// within 5% in about 0.5 s (Nikita's headset test after PR 72: 2.5 Hz came back too fast and felt like a spring).
        /// </summary>
        public const float FrequencyHertz = 1.5f;

        /// <summary>
        /// While a press holds the point aside (<see cref="HoldAt"/>), the spring follows it at this frequency, as
        /// promptly as before the calmer return (a hand gives way at once and comes back slowly). Also critically damped.
        /// </summary>
        public const float PressFollowHertz = 2.5f;

        /// <summary>
        /// The spring's damping ratio: critically damped, so the point comes back to its line and never crosses it (0.6
        /// overshot past the line by about a tenth, a springy bounce).
        /// </summary>
        public const float DampingRatio = 1.0f;

        /// <summary>
        /// A clash this fast or faster, square to the Dummy's blade, is a beat; slower is a touch or the start of a press.
        /// Deliberate presses in VR stay under about 0.5 m/s; a full clash is about 2 m/s (PER-99).
        /// </summary>
        public const float BeatMinSpeedMetersPerSecond = 0.6f;

        /// <summary>
        /// The point's deflection per m/s of clash speed, for a contact at the Dummy's point: a 2 m/s beat on its foible
        /// (0.8 of the way out) knocks it 34 cm. The Fencer's own contact place is already in the speed (the foible moves faster).
        /// </summary>
        public const float BeatMetersPerMeterPerSecond = 0.20f;

        /// <summary>Share of the full deflection a contact at the Dummy's guard still gives: the forte barely moves.</summary>
        public const float GuardLeverage = 0.3f;

        /// <summary>The point's largest offset from its line, wrist and arm together.</summary>
        public const float MaxDeflectionMeters = WristMaxMeters + ArmMaxMeters;

        /// <summary>The part of the offset the blade turns in the hand (about 13 degrees) before the arm goes with it.</summary>
        public const float WristMaxMeters = 0.20f;

        /// <summary>The most the whole arm is knocked aside, at the point: about 15 degrees at the shoulder.</summary>
        public const float ArmMaxMeters = 0.40f;

        /// <summary>A deflection under this is a light touch: the quick spring, no open line.</summary>
        public const float LightTouchMeters = 0.05f;

        /// <summary>How long a beat takes to knock the point out: sharp, about the old swing's 74 ms peak.</summary>
        public const float OutSeconds = 0.06f;

        /// <summary>The open line's least length, for the smallest deflection that opens it.</summary>
        public const float OpenBaseSeconds = 0.12f;

        /// <summary>
        /// The open line's growth per meter of deflection: 10 cm opens it 0.18 s, 30 cm 0.30 s, 60 cm 0.48 s (human
        /// reaction is about 0.15 to 0.25 s).
        /// </summary>
        public const float OpenSecondsPerMeter = 0.6f;

        /// <summary>The recovery's least length.</summary>
        public const float RecoverBaseSeconds = 0.25f;

        /// <summary>The recovery's growth per meter of deflection: 30 cm comes back in 0.55 s, 60 cm in 0.85 s.</summary>
        public const float RecoverSecondsPerMeter = 1.0f;

        /// <summary>
        /// The fastest the arm's share of the offset moves, at the point. The hand is at most about half as far from the
        /// shoulder as the point (a 2.1 m Dummy), so its Target Zones move at most about 3 m/s, 4.2 cm in a 72 Hz frame,
        /// under the Blade's 5 cm (PER-114). The wrist takes the rest of a sharp beat until the arm catches up.
        /// </summary>
        public const float MaxArmPointSpeedMetersPerSecond = 6.0f;

        // A press must hold the point this much further than it is knocked to take it over: tracking jitter does not.
        private const float PressTakeOverSlackMeters = 0.005f;

        // Integration step of the spring: well under its period, so it is the same at any frame rate.
        private const float MaxStepSeconds = 0.002f;

        private Vector3 _velocity;
        private Vector3 _heldOffset;
        private Vector3 _from;
        private Vector3 _target;
        private float _phaseSeconds;
        private float _openSeconds;
        private float _recoverSeconds;
        private bool _wasHeld;

        /// <summary>The point's offset from its line now, square to the blade, wrist and arm together.</summary>
        public Vector3 TipOffset { get; private set; }

        /// <summary>The part of <see cref="TipOffset"/> the knocked arm carries, at the point (the rest is the wrist).</summary>
        public Vector3 ArmOffset { get; private set; }

        public DeflectionPhase Phase { get; private set; }

        /// <summary>The last knock or release that took the point well off line; counted by <see cref="PlanCount"/>.</summary>
        public DeflectionPlan LastPlan { get; private set; }

        /// <summary>How many plans so far: a new one since the last look means a new knock to log.</summary>
        public int PlanCount { get; private set; }

        /// <summary>
        /// How long until the point is back in line if nothing touches it again: the rest of the phases, or the open line
        /// and recovery a held press will start when let go; 0 for a light touch.
        /// </summary>
        public float SecondsUntilInLine
        {
            get
            {
                switch (Phase)
                {
                    case DeflectionPhase.Out:
                        return (OutSeconds - _phaseSeconds) + _openSeconds + _recoverSeconds;
                    case DeflectionPhase.Open:
                        return (_openSeconds - _phaseSeconds) + _recoverSeconds;
                    case DeflectionPhase.Recover:
                        return Math.Max(0.0f, _recoverSeconds - _phaseSeconds);
                    default:
                        var meters = TipOffset.Length();
                        return _heldOffset != Vector3.Zero && meters >= LightTouchMeters ? GetOpenSeconds(meters) + GetRecoverSeconds(meters) : 0.0f;
                }
            }
        }

        /// <summary>True while the point is knocked or pressed well off its line (<see cref="SecondsUntilInLine"/> above 0).</summary>
        public bool IsOffLine => SecondsUntilInLine > 0.0f;

        /// <summary>
        /// The deflection a clash gives from rest: <see cref="BeatMetersPerMeterPerSecond"/> per m/s times the leverage,
        /// at most <see cref="MaxDeflectionMeters"/> for a beat and <see cref="LightTouchMeters"/> for a slower touch.
        /// </summary>
        public static float GetKickMeters(float relativeSpeedMetersPerSecond, float opponentBladeFraction)
        {
            var leverage = GuardLeverage + ((1.0f - GuardLeverage) * Math.Clamp(opponentBladeFraction, 0.0f, 1.0f));
            var meters = BeatMetersPerMeterPerSecond * Math.Max(0.0f, relativeSpeedMetersPerSecond) * leverage;
            return Math.Min(meters, IsBeat(relativeSpeedMetersPerSecond) ? MaxDeflectionMeters : LightTouchMeters);
        }

        /// <summary>How long the line stays open after a deflection of <paramref name="meters"/>.</summary>
        public static float GetOpenSeconds(float meters)
        {
            return OpenBaseSeconds + (OpenSecondsPerMeter * meters);
        }

        /// <summary>How long the deliberate return from <paramref name="meters"/> off line takes.</summary>
        public static float GetRecoverSeconds(float meters)
        {
            return RecoverBaseSeconds + (RecoverSecondsPerMeter * meters);
        }

        /// <summary>
        /// The kick speed that, from rest, takes the light-touch spring out to <paramref name="peakMeters"/>. Critically
        /// damped, the peak of v t exp(-w t) is v / (w e); underdamped, that of v / w * exp(-z w t) * sin(w_d t) is
        /// v / w * exp(-z acos(z) / sqrt(1 - z^2)).
        /// </summary>
        public static float GetKickSpeed(float peakMeters)
        {
            var omega = 2.0 * Math.PI * FrequencyHertz;
            const double Damping = DampingRatio;
            var peakPerSpeed = Damping >= 1.0
                ? 1.0 / (omega * Math.E)
                : Math.Exp(-Damping * Math.Acos(Damping) / Math.Sqrt(1.0 - (Damping * Damping))) / omega;
            return (float)(peakMeters / peakPerSpeed);
        }

        /// <summary>
        /// Knocks the point for a clash: a light touch on the spring, a beat out to its deflection (added to any under way).
        /// A slow touch on a point already knocked out changes nothing: the contact solver keeps the blades apart.
        /// </summary>
        public void Kick(BladeClash clash)
        {
            var meters = GetKickMeters(clash.RelativeSpeedMetersPerSecond, clash.OpponentBladeFraction);
            if (meters <= 0.0f || clash.PushDirection == Vector3.Zero)
            {
                return;
            }

            var isBeat = IsBeat(clash.RelativeSpeedMetersPerSecond);
            if (Phase == DeflectionPhase.Spring && (!isBeat || meters < LightTouchMeters))
            {
                _velocity += clash.PushDirection * GetKickSpeed(meters);
                return;
            }

            if (isBeat)
            {
                StartOut(LimitToMax(TipOffset + (clash.PushDirection * meters)));
            }
        }

        /// <summary>
        /// Where the Dummy's hand gives way to the press (<see cref="ArmedDummyBlade.FindYieldTipOffset"/>), zero when
        /// nothing presses. A press past a knocked-out point takes it over on the spring; one within it keeps the line open.
        /// </summary>
        public void HoldAt(Vector3 tipOffset)
        {
            _heldOffset = LimitToMax(tipOffset);
        }

        /// <summary>In line at once: the blade appeared or jumped.</summary>
        public void Reset()
        {
            TipOffset = Vector3.Zero;
            ArmOffset = Vector3.Zero;
            _velocity = Vector3.Zero;
            _heldOffset = Vector3.Zero;
            _wasHeld = false;
            Phase = DeflectionPhase.Spring;
        }

        public void Step(float deltaSeconds)
        {
            if (!(deltaSeconds > 0.0f))
            {
                return;
            }

            var isHeld = _heldOffset != Vector3.Zero;
            if (isHeld && Phase != DeflectionPhase.Spring)
            {
                TakeOverByPress();
            }

            if (Phase == DeflectionPhase.Spring && !isHeld && _wasHeld && TipOffset.Length() >= LightTouchMeters)
            {
                // Let go after a press moved it well aside: the line stays open, then the point recovers.
                StartOpen(TipOffset, isNewPlan: true);
            }

            if (Phase == DeflectionPhase.Spring)
            {
                StepSpring(deltaSeconds);
            }
            else
            {
                StepPhases(deltaSeconds);
            }

            _wasHeld = isHeld;
            StepArm(deltaSeconds);
        }

        /// <summary>
        /// The turn about the shoulder that knocks the arm aside by <see cref="ArmOffset"/> at the point of
        /// <paramref name="forte"/> (the blade where the arm holds it, undeflected); identity with the arm in line.
        /// </summary>
        public Quaternion GetArmTurn(BladeSegment forte, Vector3 shoulder)
        {
            var reach = forte.TipPosition - shoulder;
            var reachLength = reach.Length();
            if (reachLength < 1e-6f || ArmOffset.LengthSquared() < 1e-12f)
            {
                return Quaternion.Identity;
            }

            var direction = reach / reachLength;
            var square = ArmOffset - (Vector3.Dot(ArmOffset, direction) * direction);
            var squareLength = square.Length();
            if (squareLength < 1e-6f)
            {
                return Quaternion.Identity;
            }

            var axis = Vector3.Normalize(Vector3.Cross(direction, square));
            return Quaternion.CreateFromAxisAngle(axis, GetTurnRadians(squareLength, reachLength));
        }

        /// <summary>
        /// The blade deflected: <paramref name="forte"/> (where the arm holds it, in line) turned with the knocked arm
        /// about <paramref name="shoulder"/>, then about its guard (the wrist) so its point carries the whole
        /// <see cref="TipOffset"/> sideways of its line. Keeps the blade's length.
        /// </summary>
        public BladeSegment Apply(BladeSegment forte, Vector3 shoulder)
        {
            var turn = GetArmTurn(forte, shoulder);
            var guard = shoulder + Vector3.Transform(forte.GuardPosition - shoulder, turn);
            var tip = shoulder + Vector3.Transform(forte.TipPosition - shoulder, turn);
            var along = tip - guard;
            var length = along.Length();
            if (length < 1e-6f)
            {
                return new BladeSegment(guard, tip);
            }

            var axis = along / length;
            var rest = forte.TipPosition + TipOffset - tip;
            var square = rest - (Vector3.Dot(rest, axis) * axis);
            var squareLength = square.Length();
            if (squareLength < 1e-7f)
            {
                return new BladeSegment(guard, tip);
            }

            var radians = GetTurnRadians(squareLength, length);
            var swung = (axis * (float)Math.Cos(radians)) + (square / squareLength * (float)Math.Sin(radians));
            return new BladeSegment(guard, guard + (swung * length));
        }

        /// <summary>The turn that moves the end of a <paramref name="radiusMeters"/> lever sideways by <paramref name="sidewaysMeters"/>.</summary>
        private static float GetTurnRadians(float sidewaysMeters, float radiusMeters)
        {
            return (float)Math.Asin(Math.Min(1.0f, sidewaysMeters / radiusMeters));
        }

        private static bool IsBeat(float relativeSpeedMetersPerSecond)
        {
            return relativeSpeedMetersPerSecond >= BeatMinSpeedMetersPerSecond;
        }

        private static Vector3 LimitToMax(Vector3 tipOffset)
        {
            var length = tipOffset.Length();
            return length > MaxDeflectionMeters ? tipOffset * (MaxDeflectionMeters / length) : tipOffset;
        }

        /// <summary>
        /// A press on a knocked-out point: one that holds it further than it is takes it over on the spring; otherwise
        /// the line stays open while it lasts (a point held aside does not recover into the Fencer's blade).
        /// </summary>
        private void TakeOverByPress()
        {
            if (Phase == DeflectionPhase.Out)
            {
                // A beat on a pressed blade runs out first; the press is looked at again once it is open.
                return;
            }

            var heldMeters = _heldOffset.Length();
            if (Vector3.Dot(TipOffset, _heldOffset / heldMeters) < heldMeters - PressTakeOverSlackMeters)
            {
                Phase = DeflectionPhase.Spring;
                _velocity = Vector3.Zero;
            }
            else
            {
                StartOpen(TipOffset, isNewPlan: false);
            }
        }

        /// <summary>
        /// Runs out, open and recover for <paramref name="deltaSeconds"/>, carrying the time left over from one phase into
        /// the next, so the knock is the same at any frame rate; what is left once back in line goes to the spring.
        /// </summary>
        private void StepPhases(float deltaSeconds)
        {
            var remaining = deltaSeconds;
            while (remaining > 0.0f && Phase != DeflectionPhase.Spring)
            {
                var phaseLength = Phase == DeflectionPhase.Out ? OutSeconds : Phase == DeflectionPhase.Open ? _openSeconds : _recoverSeconds;
                var used = Math.Min(remaining, Math.Max(0.0f, phaseLength - _phaseSeconds));
                _phaseSeconds += used;
                remaining -= used;
                var fraction = Math.Min(1.0f, _phaseSeconds / phaseLength);
                if (Phase == DeflectionPhase.Out)
                {
                    TipOffset = LimitToMax(Vector3.Lerp(_from, _target, AttackPath.MinimumJerk(fraction)));
                }
                else if (Phase == DeflectionPhase.Recover)
                {
                    TipOffset = _target * (1.0f - AttackPath.MinimumJerk(fraction));
                }

                if (_phaseSeconds < phaseLength)
                {
                    return;
                }

                AdvancePhase();
            }

            if (remaining > 0.0f)
            {
                StepSpring(remaining);
            }
        }

        private void AdvancePhase()
        {
            switch (Phase)
            {
                case DeflectionPhase.Out:
                    StartOpen(_target, isNewPlan: false);
                    break;
                case DeflectionPhase.Open:
                    Phase = DeflectionPhase.Recover;
                    _phaseSeconds = 0.0f;
                    break;
                default:
                    TipOffset = Vector3.Zero;
                    _velocity = Vector3.Zero;
                    Phase = DeflectionPhase.Spring;
                    break;
            }
        }

        private void StartOut(Vector3 target)
        {
            _from = TipOffset;
            _target = target;
            _velocity = Vector3.Zero;
            _phaseSeconds = 0.0f;
            Phase = DeflectionPhase.Out;
            SetTimes(target.Length(), isNewPlan: true);
        }

        private void StartOpen(Vector3 offset, bool isNewPlan)
        {
            TipOffset = offset;
            _target = offset;
            _velocity = Vector3.Zero;
            _phaseSeconds = 0.0f;
            Phase = DeflectionPhase.Open;
            SetTimes(offset.Length(), isNewPlan);
        }

        private void SetTimes(float meters, bool isNewPlan)
        {
            _openSeconds = GetOpenSeconds(meters);
            _recoverSeconds = GetRecoverSeconds(meters);
            if (!isNewPlan)
            {
                return;
            }

            var armMeters = Math.Min(ArmMaxMeters, Math.Max(0.0f, meters - WristMaxMeters));
            LastPlan = new DeflectionPlan(meters, meters - armMeters, armMeters, _openSeconds, _recoverSeconds);
            PlanCount++;
        }

        private void StepSpring(float deltaSeconds)
        {
            // A held press is followed promptly; let go, the point comes back calmly. Critically damped either way.
            var omega = (float)(2.0 * Math.PI * (_heldOffset != Vector3.Zero ? PressFollowHertz : FrequencyHertz));
            var steps = (int)Math.Ceiling(deltaSeconds / MaxStepSeconds);
            var step = deltaSeconds / steps;
            var offset = TipOffset;
            for (var index = 0; index < steps; index++)
            {
                // Semi-implicit Euler: stable for a spring at this step.
                var acceleration = (-omega * omega * (offset - _heldOffset)) - (2.0f * DampingRatio * omega * _velocity);
                _velocity += acceleration * step;
                offset += _velocity * step;
            }

            TipOffset = LimitToMax(offset);
        }

        /// <summary>The arm's share follows what is past the wrist's, no faster than <see cref="MaxArmPointSpeedMetersPerSecond"/>.</summary>
        private void StepArm(float deltaSeconds)
        {
            var meters = TipOffset.Length();
            var armMeters = Math.Min(ArmMaxMeters, Math.Max(0.0f, meters - WristMaxMeters));
            var wanted = meters > 1e-7f ? TipOffset * (armMeters / meters) : Vector3.Zero;
            var change = wanted - ArmOffset;
            var most = MaxArmPointSpeedMetersPerSecond * deltaSeconds;
            var changeLength = change.Length();
            ArmOffset += changeLength > most ? change * (most / changeLength) : change;
        }
    }
}
