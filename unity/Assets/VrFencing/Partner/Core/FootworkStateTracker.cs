using System;
using System.Numerics;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Partner
{
    /// <summary>
    /// The sender's side of the remote Fencer's legs (PER-112, stage 3 of PER-108): every rendered frame it reads this
    /// Fencer's footwork from the tracked hips and head and keeps where each foot is, so <see cref="State"/> can go out
    /// with each body sample. It runs on full-rate tracking; the receiver sees the body 30 times a second and 100 ms
    /// late, and would read a lunge late.
    ///
    /// The headset tracks no feet, so the feet are placed by the Dummy's rules (<see cref="DummyFootwork"/>), driven by
    /// the real hips instead of a target:
    /// <list type="bullet">
    /// <item>Standing: both feet planted at their en garde places, pinned to the floor. Breathing and sway move no foot.</item>
    /// <item>Advance, retreat, side step and diagonal (PER-118): the hips start moving over the floor, in any direction.
    /// The lead foot (front on an advance or a step mostly across the Strip, back on a retreat) lifts and swings ahead of
    /// the hips in the step's direction, landing about mid-step; the other foot then follows, landing in en garde under
    /// the hips when they stop. A step mostly across the Strip is sent as an advance (its feet go in an advance's order),
    /// so the wire format is unchanged.</item>
    /// <item>Lunge: the hips move forward fast with the head dropping. The front foot kicks out (toe up) and lands far
    /// ahead as the hips slow; the back foot stays planted.</item>
    /// <item>Recovery: the head comes back up (or the hips move back). The foot that is out of place, the front one in a
    /// backward recovery, the back one in a forward one, steps back into en garde under the hips.</item>
    /// <item>A planted foot left far from its place by a slow drift steps there on its own.</item>
    /// </list>
    /// The hips, not the head, decide where the feet go: leaning the torso to dodge moves the head, not the feet. The head
    /// gives the lunge's drop and stands in for the hips when no body is tracked (<see cref="RemoteFootworkTuning.HeadOnlyConfidence"/>).
    /// Positions in this Fencer's own Strip frame (+x their right, +y up, +z toward the opponent). Nothing is allocated
    /// per frame.
    /// </summary>
    public sealed class FootworkStateTracker
    {
        // Enough frames to look 50 ms back at any frame rate up to about 600 per second.
        private const int SpeedHistoryCapacity = 32;

        private const float DegreesToRadians = (float)(Math.PI / 180.0);

        private readonly double[] _historyTimes = new double[SpeedHistoryCapacity];
        private readonly Vector3[] _historyHips = new Vector3[SpeedHistoryCapacity];
        private readonly Foot _front = new Foot();
        private readonly Foot _back = new Foot();

        private int _historyCount;
        private int _historyNewest = -1;

        private float _heightMeters;
        private Hand _hand;
        private Vector3 _frontRest;
        private Vector3 _backRest;
        private bool _hasFeet;
        private double _nowSeconds;
        private Vector3 _hips;
        private Vector3? _headToHips;
        private float _standingHeadY;
        private Mode _mode;

        // A step, a lunge, a recovery or a correcting step under way.
        private double _moveStartSeconds;
        private Vector3 _stepDirection;
        private Vector3 _stepStart;
        private float _moveStartZ;
        private float _peakSpeed;
        private float _peakTravel;
        private float _leadLandTravel;
        private bool _isLeadLanded;
        private double _quietSeconds;
        private float _maxDrop;
        private float _recoveryStartDrop;
        private Foot _lead;
        private Foot _trail;
        private Foot _adjusting;

        public FootworkStateTracker()
        {
            State = new FootworkState(FootworkPhase.Standing, default, default, RemoteFootworkTuning.NoConfidence);
        }

        private enum Mode
        {
            Standing,
            Step,
            Adjust,
            Lunge,
            Recovery,
        }

        /// <summary>The feet as of the last <see cref="Update"/>, to send with the body.</summary>
        public FootworkState State { get; private set; }

        /// <summary>
        /// One rendered frame. <paramref name="head"/> is the headset's position (null when it is not tracked: nothing
        /// is read then, and the feet hold with no confidence); <paramref name="hips"/> the tracked hip center, null when
        /// body tracking gives none. Times must increase; a frame at the same or an earlier time changes nothing.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The height is not a positive number.</exception>
        public void Update(double timeSeconds, Vector3? head, Vector3? hips, float heightMeters, Hand hand)
        {
            if (!(heightMeters > 0.0f) || float.IsInfinity(heightMeters))
            {
                throw new ArgumentOutOfRangeException(nameof(heightMeters), heightMeters, "Must be a positive number of meters.");
            }

            if (_historyCount > 0 && timeSeconds <= _nowSeconds)
            {
                return;
            }

            var deltaSeconds = _historyCount > 0 ? timeSeconds - _nowSeconds : 0.0;
            _nowSeconds = timeSeconds;
            if (heightMeters != _heightMeters || hand != _hand)
            {
                SetProportions(heightMeters, hand);
            }

            if (!head.HasValue)
            {
                // Nothing to read: the feet hold where they are, and the speeds start over when the head is back.
                _historyCount = 0;
                Publish(RemoteFootworkTuning.NoConfidence);
                return;
            }

            var hipsNow = ReadHips(head.Value, hips);
            if (!_hasFeet || _historyCount == 0 || Vector3.Distance(hipsNow, _hips) > RemoteFootworkTuning.TrackingJumpMeters)
            {
                StandAt(hipsNow, head.Value.Y);
                Publish(GetConfidence(hips.HasValue, Vector3.Zero));
                return;
            }

            _hips = hipsNow;
            Remember(timeSeconds, hipsNow);
            var velocity = MeasureVelocity();
            var drop = _standingHeadY - head.Value.Y;
            var maxFootMove = (float)(RemoteFootworkTuning.MaxFootSpeedMetersPerSecond * deltaSeconds);
            _front.StartFrame(maxFootMove);
            _back.StartFrame(maxFootMove);
            Advance(velocity, drop, deltaSeconds);
            if (_mode == Mode.Standing && Math.Abs(velocity.Z) < RemoteFootworkTuning.StepOnsetMetersPerSecond)
            {
                var follow = (float)Math.Min(1.0, deltaSeconds / RemoteFootworkTuning.StandingHeadFollowSeconds);
                _standingHeadY += (head.Value.Y - _standingHeadY) * follow;
            }

            _front.Land(timeSeconds);
            _back.Land(timeSeconds);
            Publish(GetConfidence(hips.HasValue, velocity));
        }

        private static float GetConfidence(bool hasHips, Vector3 velocity)
        {
            var confidence = hasHips ? RemoteFootworkTuning.TrackedConfidence : RemoteFootworkTuning.HeadOnlyConfidence;
            var sideways = Math.Abs(velocity.X);
            if (sideways > RemoteFootworkTuning.SidewaysMetersPerSecond && sideways > Math.Abs(velocity.Z))
            {
                confidence = Math.Min(confidence, RemoteFootworkTuning.SidewaysConfidence);
            }

            return confidence;
        }

        private static Vector3 OnFloor(Vector3 point)
        {
            return new Vector3(point.X, 0.0f, point.Z);
        }

        private static float GetFloorDistance(Vector3 a, Vector3 b)
        {
            return Vector3.Distance(OnFloor(a), OnFloor(b));
        }

        /// <summary>The en garde ankles relative to the hip center, on the floor, for the height and hand.</summary>
        private void SetProportions(float heightMeters, Hand hand)
        {
            var isHandChanged = hand != _hand;
            _heightMeters = heightMeters;
            _hand = hand;
            var legs = DummyLegs.EnGarde(heightMeters, hand);
            var hipCenter = (legs.Front.Hip + legs.Back.Hip) * 0.5f;
            _frontRest = OnFloor(legs.Front.Ankle - hipCenter);
            _backRest = OnFloor(legs.Back.Ankle - hipCenter);
            if (isHandChanged)
            {
                _hasFeet = false;
            }
        }

        /// <summary>The hip center: tracked, or the head plus the last offset seen between them (the en garde one at first).</summary>
        private Vector3 ReadHips(Vector3 head, Vector3? hips)
        {
            if (hips.HasValue)
            {
                _headToHips = OnFloor(hips.Value - head);
                return OnFloor(hips.Value);
            }

            return OnFloor(head + (_headToHips ?? GetEnGardeHeadToHips()));
        }

        /// <summary>From the headset (the eyes) to the hip center in en garde, on the floor.</summary>
        private Vector3 GetEnGardeHeadToHips()
        {
            var hipCenter = (EnGardePose.FrontHip + EnGardePose.BackHip) * 0.5f;
            var eyes = EnGardePose.HeadCenter + new Vector3(0.0f, 0.0f, FencerTarget.HeadCenterBehindEyesPerHeight);
            var offset = OnFloor(hipCenter - eyes) * _heightMeters;
            return _hand == Hand.Left ? HandMirroring.MirrorPosition(offset) : offset;
        }

        private void StandAt(Vector3 hips, float headY)
        {
            _hips = hips;
            _historyCount = 0;
            Remember(_nowSeconds, hips);
            _front.PlantAt(hips + _frontRest);
            _back.PlantAt(hips + _backRest);
            _hasFeet = true;
            _standingHeadY = headY;
            _mode = Mode.Standing;
        }

        /// <summary>One frame of the footwork, from the hips' velocity on the floor and the head's drop.</summary>
        private void Advance(Vector3 velocity, float drop, double deltaSeconds)
        {
            switch (_mode)
            {
                case Mode.Standing:
                    ContinueStanding(velocity, drop);
                    break;
                case Mode.Step:
                    ContinueStep(velocity, drop, deltaSeconds);
                    break;
                case Mode.Adjust:
                    ContinueAdjust(velocity, drop);
                    break;
                case Mode.Lunge:
                    ContinueLunge(velocity.Z, drop);
                    break;
                case Mode.Recovery:
                    ContinueRecovery(velocity.Z, drop);
                    break;
            }
        }

        private Vector3 GetPlace(Foot foot)
        {
            return _hips + (foot == _front ? _frontRest : _backRest);
        }

        /// <summary>Where the feet's spots put the hips: between their en garde places.</summary>
        private Vector3 GetCarriedHips()
        {
            return ((_front.Anchor - _frontRest) + (_back.Anchor - _backRest)) * 0.5f;
        }

        private bool IsLungeStart(float speed, float drop)
        {
            return speed >= RemoteFootworkTuning.LungeMetersPerSecond
                   && drop >= RemoteFootworkTuning.LungeStartHeadDropPerHeight * _heightMeters;
        }

        /// <summary>
        /// Whether the hips start a step: moving over the floor at the step onset speed or more, in any direction, and
        /// already that way from where the planted feet carry them.
        /// </summary>
        private bool IsStepStart(Vector3 velocity)
        {
            var floorVelocity = OnFloor(velocity);
            var speed = floorVelocity.Length();
            if (speed < RemoteFootworkTuning.StepOnsetMetersPerSecond)
            {
                return false;
            }

            var travel = Vector3.Dot(_hips - GetCarriedHips(), floorVelocity / speed);
            return travel >= RemoteFootworkTuning.StepMinTravelPerHeight * _heightMeters;
        }

        // ---- Standing and correcting steps ----

        private void ContinueStanding(Vector3 velocity, float drop)
        {
            if (IsLungeStart(velocity.Z, drop))
            {
                StartLunge(drop);
                return;
            }

            if (IsStepStart(velocity))
            {
                StartStep(Vector3.Normalize(OnFloor(velocity)));
                return;
            }

            if (_front.IsMoving || _back.IsMoving)
            {
                return;
            }

            if (!TryStartAdjust(_front))
            {
                TryStartAdjust(_back);
            }
        }

        /// <summary>Starts a correcting step for a planted foot that drifted from its place; false if it has not.</summary>
        private bool TryStartAdjust(Foot foot)
        {
            if (GetFloorDistance(foot.Anchor, GetPlace(foot)) <= RemoteFootworkTuning.FootDriftPerHeight * _heightMeters)
            {
                return false;
            }

            _adjusting = foot;
            _moveStartSeconds = _nowSeconds;
            foot.StartMoving(_nowSeconds);
            _mode = Mode.Adjust;
            return true;
        }

        private void ContinueAdjust(Vector3 velocity, float drop)
        {
            if (IsLungeStart(velocity.Z, drop))
            {
                StartLunge(drop);
                return;
            }

            if (IsStepStart(velocity))
            {
                StartStep(Vector3.Normalize(OnFloor(velocity)));
                return;
            }

            var fraction = (float)((_nowSeconds - _moveStartSeconds) / RemoteFootworkTuning.AdjustStepSeconds);
            var shape = DummyFootwork.SwingBump(fraction);
            var place = GetPlace(_adjusting);
            _adjusting.Fly(
                Vector3.Lerp(_adjusting.From, place, DummyFootwork.MinimumJerk(fraction)),
                FootworkTuning.FootLiftPerHeight * _heightMeters * shape,
                GetStepPitch(_adjusting) * shape);
            if (fraction >= 1.0f)
            {
                _adjusting.PlantAt(place);
                _mode = Mode.Standing;
            }
        }

        private float GetStepPitch(Foot foot)
        {
            return foot == _front
                ? FootworkTuning.FrontFootToeUpDegrees * DegreesToRadians
                : -FootworkTuning.BackFootHeelUpDegrees * DegreesToRadians;
        }

        // ---- Advance, retreat, side step and diagonal ----

        /// <summary>Starts a step in <paramref name="direction"/>, a unit vector on the floor.</summary>
        private void StartStep(Vector3 direction)
        {
            _mode = Mode.Step;
            _stepDirection = direction;
            _stepStart = GetCarriedHips();
            _peakSpeed = 0.0f;
            _peakTravel = 0.0f;
            _quietSeconds = 0.0;
            _isLeadLanded = false;

            // The back foot leads only a retreat; the front foot an advance and a step mostly across the Strip.
            var isRetreat = direction.Z < 0.0f && Math.Abs(direction.Z) >= Math.Abs(direction.X);
            _lead = isRetreat ? _back : _front;
            _trail = isRetreat ? _front : _back;
            if (_trail.IsMoving)
            {
                _trail.StartLanding(_nowSeconds, _trail.Aim);
            }

            _lead.StartMoving(_nowSeconds);
        }

        private void ContinueStep(Vector3 velocity, float drop, double deltaSeconds)
        {
            if (IsLungeStart(velocity.Z, drop))
            {
                StartLunge(drop);
                return;
            }

            var speedAlong = Vector3.Dot(OnFloor(velocity), _stepDirection);
            var travel = Math.Max(0.0f, Vector3.Dot(_hips - _stepStart, _stepDirection));
            if (speedAlong > _peakSpeed)
            {
                // The speed is the mean over the last window: it belongs to the middle of that window.
                _peakSpeed = speedAlong;
                _peakTravel = Math.Max(0.0f, travel - (speedAlong * (float)(RemoteFootworkTuning.SpeedWindowSeconds * 0.5)));
            }

            // A smooth step is symmetric: at its top speed the hips are halfway, so the stride is twice that.
            var stride = RemoteFootworkTuning.LeadFootTravelPerBodyTravel * _peakTravel;
            if (!_isLeadLanded)
            {
                // Ahead of where it started in the step's direction; square to it, the foot keeps to its place under the hips.
                var leadTravel = Math.Min(RemoteFootworkTuning.LeadFootTravelPerBodyTravel * travel, stride);
                var place = GetPlace(_lead);
                var leadAlong = Vector3.Dot(_lead.From, _stepDirection) + leadTravel;
                var leadSpot = place + (_stepDirection * (leadAlong - Vector3.Dot(place, _stepDirection)));
                _lead.Fly(leadSpot, GetRisingLift(_lead, FootworkTuning.FootLiftPerHeight), GetRisingPitch(_lead, GetStepPitch(_lead)));
                if (_peakSpeed >= RemoteFootworkTuning.StepOnsetMetersPerSecond && speedAlong < _peakSpeed * RemoteFootworkTuning.LeadFootLandsAtSpeedFraction)
                {
                    _lead.StartLanding(_nowSeconds, _lead.Aim);
                    _isLeadLanded = true;
                    _leadLandTravel = travel;
                    _trail.StartMoving(_nowSeconds);
                }
            }
            else if (_trail.IsMoving)
            {
                // The trailing foot covers the rest of the step as the hips cover the rest of the stride.
                var fraction = (travel - _leadLandTravel) / Math.Max(stride - _leadLandTravel, 0.01f);
                var place = GetPlace(_trail);
                _trail.Fly(
                    Vector3.Lerp(_trail.From, place, DummyFootwork.MinimumJerk(fraction)),
                    GetRisingLift(_trail, FootworkTuning.FootLiftPerHeight),
                    GetRisingPitch(_trail, GetStepPitch(_trail)));
            }

            _quietSeconds = speedAlong < RemoteFootworkTuning.StepEndMetersPerSecond ? _quietSeconds + deltaSeconds : 0.0;
            if (_quietSeconds >= RemoteFootworkTuning.StepEndSeconds || speedAlong <= -RemoteFootworkTuning.StepOnsetMetersPerSecond)
            {
                if (_lead.IsMoving)
                {
                    _lead.StartLanding(_nowSeconds, _lead.Aim);
                }

                if (_trail.IsMoving || _isLeadLanded)
                {
                    _trail.StartLanding(_nowSeconds, GetPlace(_trail));
                }

                _mode = Mode.Standing;
            }
        }

        private float GetRisingLift(Foot foot, float liftPerHeight)
        {
            var rise = DummyFootwork.MinimumJerk((float)((_nowSeconds - foot.MoveStartSeconds) / RemoteFootworkTuning.SwingRiseSeconds));
            return foot.FromLift + (((liftPerHeight * _heightMeters) - foot.FromLift) * rise);
        }

        private float GetRisingPitch(Foot foot, float pitchRadians)
        {
            var rise = DummyFootwork.MinimumJerk((float)((_nowSeconds - foot.MoveStartSeconds) / RemoteFootworkTuning.SwingRiseSeconds));
            return foot.FromPitch + ((pitchRadians - foot.FromPitch) * rise);
        }

        // ---- Lunge and recovery ----

        private void StartLunge(float drop)
        {
            // The lunge is measured from the hips that carried the front foot where its swing started: a lunge out of an
            // advance carries on the front foot's swing.
            if (_back.IsMoving)
            {
                _back.StartLanding(_nowSeconds, _back.Aim);
            }

            if (!_front.IsMoving)
            {
                _front.StartMoving(_nowSeconds);
            }

            _moveStartZ = (_front.From - _frontRest).Z;

            _peakSpeed = 0.0f;
            _maxDrop = drop;
            _mode = Mode.Lunge;
        }

        private void ContinueLunge(float speed, float drop)
        {
            _maxDrop = Math.Max(_maxDrop, drop);
            _peakSpeed = Math.Max(_peakSpeed, speed);
            if (_front.IsMoving)
            {
                var travel = Math.Max(0.0f, _hips.Z - _moveStartZ);
                var frontZ = _front.From.Z + (RemoteFootworkTuning.LungeFrontFootTravelPerBodyTravel * travel);
                _front.Fly(
                    new Vector3(GetPlace(_front).X, 0.0f, Math.Max(frontZ, _front.Anchor.Z)),
                    GetRisingLift(_front, FootworkTuning.LungeFootLiftPerHeight),
                    GetRisingPitch(_front, FootworkTuning.LungeFrontFootToeUpDegrees * DegreesToRadians));
                if (speed < _peakSpeed * RemoteFootworkTuning.LungeFootLandsAtSpeedFraction)
                {
                    _front.StartLanding(_nowSeconds, _front.Aim);
                }
            }

            var hasRisen = drop <= _maxDrop * (1.0f - RemoteFootworkTuning.RecoveryStartRiseFraction);
            if (speed <= -RemoteFootworkTuning.StepOnsetMetersPerSecond || hasRisen)
            {
                StartRecovery(drop);
            }
        }

        private void StartRecovery(float drop)
        {
            _mode = Mode.Recovery;
            _moveStartSeconds = _nowSeconds;
            _recoveryStartDrop = Math.Max(drop, RemoteFootworkTuning.LungeStartHeadDropPerHeight * _heightMeters);
            StartRecoveringFoot(_front);
            StartRecoveringFoot(_back);
        }

        /// <summary>A foot out of its en garde place steps back into it; one in place lands where it is.</summary>
        private void StartRecoveringFoot(Foot foot)
        {
            if (GetFloorDistance(foot.Anchor, GetPlace(foot)) > RemoteFootworkTuning.RecoveryMovesFootPerHeight * _heightMeters)
            {
                foot.StartMoving(_nowSeconds);
            }
            else if (foot.IsMoving)
            {
                foot.StartLanding(_nowSeconds, foot.Aim);
            }
        }

        private void ContinueRecovery(float speed, float drop)
        {
            if (IsLungeStart(speed, drop))
            {
                // A second lunge from the lunge (a redoublement): the feet start from where they are.
                StartLunge(drop);
                return;
            }

            var elapsed = _nowSeconds - _moveStartSeconds;
            var risen = (_recoveryStartDrop - drop) / _recoveryStartDrop;
            var fraction = Math.Min(Math.Max(Math.Max(risen, (float)(elapsed / RemoteFootworkTuning.RecoveryMaxSeconds)), 0.0f), 1.0f);
            RecoverFoot(_front, fraction);
            RecoverFoot(_back, fraction);

            var isDone = fraction >= RemoteFootworkTuning.RecoveryDoneFraction && Math.Abs(speed) < RemoteFootworkTuning.StepEndMetersPerSecond;
            if (isDone || elapsed >= RemoteFootworkTuning.RecoveryMaxSeconds)
            {
                LandInPlace(_front);
                LandInPlace(_back);
                _mode = Mode.Standing;
            }
        }

        /// <summary>A recovering foot at <paramref name="fraction"/> of the way back into its en garde place.</summary>
        private void RecoverFoot(Foot foot, float fraction)
        {
            if (!foot.IsMoving)
            {
                return;
            }

            var shape = DummyFootwork.SwingBump(fraction);
            var eased = DummyFootwork.MinimumJerk(fraction);
            var pitch = foot == _front ? FootworkTuning.RecoveryFrontFootToeUpDegrees * DegreesToRadians : GetStepPitch(foot);
            foot.Fly(
                Vector3.Lerp(foot.From, GetPlace(foot), eased),
                (foot.FromLift * (1.0f - eased)) + (FootworkTuning.FootLiftPerHeight * _heightMeters * shape),
                (foot.FromPitch * (1.0f - eased)) + (pitch * shape));
        }

        private void LandInPlace(Foot foot)
        {
            if (foot.IsMoving)
            {
                foot.StartLanding(_nowSeconds, GetPlace(foot));
            }
        }

        // ---- Speed along the Strip ----

        private void Remember(double timeSeconds, Vector3 hips)
        {
            _historyNewest = (_historyNewest + 1) % SpeedHistoryCapacity;
            _historyTimes[_historyNewest] = timeSeconds;
            _historyHips[_historyNewest] = hips;
            _historyCount = Math.Min(_historyCount + 1, SpeedHistoryCapacity);
        }

        /// <summary>The hips' velocity on the floor over the newest span of at least <see cref="RemoteFootworkTuning.SpeedWindowSeconds"/>; zero until there is one.</summary>
        private Vector3 MeasureVelocity()
        {
            var newestTime = _historyTimes[_historyNewest];
            for (var back = 1; back < _historyCount; back++)
            {
                var index = (_historyNewest - back + SpeedHistoryCapacity) % SpeedHistoryCapacity;
                var span = newestTime - _historyTimes[index];
                if (span >= RemoteFootworkTuning.SpeedWindowSeconds)
                {
                    return (_historyHips[_historyNewest] - _historyHips[index]) / (float)span;
                }
            }

            return Vector3.Zero;
        }

        private void Publish(float confidence)
        {
            var phase = GetPhase();
            State = new FootworkState(phase, _front.ToPlacement(), _back.ToPlacement(), _hasFeet ? confidence : RemoteFootworkTuning.NoConfidence);
        }

        private FootworkPhase GetPhase()
        {
            switch (_mode)
            {
                case Mode.Step:
                    return _lead == _back ? FootworkPhase.Retreating : FootworkPhase.Advancing;
                case Mode.Adjust:
                    return GetPlace(_adjusting).Z >= _adjusting.From.Z ? FootworkPhase.Advancing : FootworkPhase.Retreating;
                case Mode.Lunge:
                    return FootworkPhase.Lunging;
                case Mode.Recovery:
                    return FootworkPhase.Recovering;
                default:
                    return FootworkPhase.Standing;
            }
        }

        /// <summary>
        /// One foot: its spot on the floor, lift and pitch, and the swing or landing it is in. A swinging or landing foot
        /// moves at most <see cref="RemoteFootworkTuning.MaxFootSpeedMetersPerSecond"/> over the floor (PER-118): when
        /// the footwork asks for more in a frame, it lags and catches up.
        /// </summary>
        private sealed class Foot
        {
            private float _moveLeftMeters = float.PositiveInfinity;
            private bool _isLanding;
            private double _landStartSeconds;
            private Vector3 _landFrom;
            private Vector3 _landTo;
            private float _landFromLift;
            private float _landFromPitch;

            /// <summary>The ankle's spot on the floor (y = 0).</summary>
            public Vector3 Anchor { get; private set; }

            /// <summary>
            /// Where the foot is headed on the floor: the spot the footwork last asked for, which a foot held to its top
            /// speed has not reached yet; the spot it stands on once planted. A foot told to land where it is lands here.
            /// </summary>
            public Vector3 Aim { get; private set; }

            public float Lift { get; private set; }

            public float Pitch { get; private set; }

            /// <summary>In a swing driven by the footwork (not planted, not landing).</summary>
            public bool IsMoving { get; private set; }

            public bool IsPlanted => !IsMoving && !_isLanding;

            /// <summary>Where the current swing started.</summary>
            public Vector3 From { get; private set; }

            public float FromLift { get; private set; }

            public float FromPitch { get; private set; }

            public double MoveStartSeconds { get; private set; }

            public void PlantAt(Vector3 spot)
            {
                Anchor = OnFloor(spot);
                Aim = Anchor;
                Lift = 0.0f;
                Pitch = 0.0f;
                IsMoving = false;
                _isLanding = false;
            }

            public void StartMoving(double nowSeconds)
            {
                From = Anchor;
                FromLift = Lift;
                FromPitch = Pitch;
                MoveStartSeconds = nowSeconds;
                IsMoving = true;
                _isLanding = false;
            }

            /// <summary>A new frame: the foot may move <paramref name="maxMoveMeters"/> over the floor in it.</summary>
            public void StartFrame(float maxMoveMeters)
            {
                _moveLeftMeters = maxMoveMeters;
            }

            public void Fly(Vector3 spot, float liftMeters, float pitchRadians)
            {
                Aim = OnFloor(spot);
                Anchor = MoveToward(spot);
                Lift = Math.Max(0.0f, liftMeters);
                Pitch = pitchRadians;
            }

            public void StartLanding(double nowSeconds, Vector3 spot)
            {
                _landStartSeconds = nowSeconds;
                _landFrom = Anchor;
                _landTo = OnFloor(spot);
                Aim = _landTo;
                _landFromLift = Lift;
                _landFromPitch = Pitch;
                IsMoving = false;
                _isLanding = true;
            }

            /// <summary>Carries a landing on to <paramref name="nowSeconds"/>; a foot not landing is left alone.</summary>
            public void Land(double nowSeconds)
            {
                if (!_isLanding)
                {
                    return;
                }

                var fraction = (float)((nowSeconds - _landStartSeconds) / RemoteFootworkTuning.LandingSeconds);
                var eased = DummyFootwork.MinimumJerk(fraction);
                Anchor = MoveToward(Vector3.Lerp(_landFrom, _landTo, eased));
                Lift = _landFromLift * (1.0f - eased);
                Pitch = _landFromPitch * (1.0f - eased);
                if (fraction >= 1.0f && Anchor == _landTo)
                {
                    PlantAt(_landTo);
                }
            }

            /// <summary>The spot this frame on the way to <paramref name="spot"/>, within what is left of the frame's move.</summary>
            private Vector3 MoveToward(Vector3 spot)
            {
                var target = OnFloor(spot);
                var distance = Vector3.Distance(Anchor, target);
                if (distance <= _moveLeftMeters)
                {
                    _moveLeftMeters -= distance;
                    return target;
                }

                var moved = Anchor + ((target - Anchor) * (_moveLeftMeters / distance));
                _moveLeftMeters = 0.0f;
                return moved;
            }

            public FootPlacement ToPlacement()
            {
                return new FootPlacement(Anchor.X, Anchor.Z, Lift, Pitch);
            }
        }
    }
}
