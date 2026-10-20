using System;
using System.Collections.Generic;
using System.Numerics;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Blade
{
    /// <summary>
    /// The rest of an Armed Dummy thrust, in the Strip frame: where its point is now on its planned straight path, where
    /// it is aimed, and the thrust's direction. Meters.
    /// </summary>
    public readonly struct AttackLine
    {
        /// <exception cref="ArgumentException">The direction is not a unit vector.</exception>
        public AttackLine(Vector3 pointNow, Vector3 aimPoint, Vector3 direction)
        {
            if (Math.Abs(direction.Length() - 1.0f) > 1e-3f)
            {
                throw new ArgumentException("The thrust's direction must be a unit vector.", nameof(direction));
            }

            PointNow = pointNow;
            AimPoint = aimPoint;
            Direction = direction;
        }

        /// <summary>The undeflected point now, on the planned path (at the aim once the thrust has arrived).</summary>
        public Vector3 PointNow { get; }

        public Vector3 AimPoint { get; }

        /// <summary>Unit vector from the thrust's start to its aim.</summary>
        public Vector3 Direction { get; }

        /// <summary>
        /// The rest of a thrust planned in the Dummy frame (<see cref="ArmedDummyAttack.Path"/>) at
        /// <paramref name="nowSeconds"/>, for a Dummy standing <paramref name="dummyDistanceMeters"/> along the Strip.
        /// A path with no length thrusts straight down the Strip at the Fencer.
        /// </summary>
        public static AttackLine OnStrip(AttackPath pathInDummyFrame, float dummyDistanceMeters, double nowSeconds)
        {
            var start = DummyLayout.DummyToStrip(pathInDummyFrame.StartPoint, dummyDistanceMeters);
            var aim = DummyLayout.DummyToStrip(pathInDummyFrame.AimPoint, dummyDistanceMeters);
            var pointNow = DummyLayout.DummyToStrip(pathInDummyFrame.GetPoint(nowSeconds), dummyDistanceMeters);
            var along = aim - start;
            var direction = along.Length() > 1e-6f ? Vector3.Normalize(along) : -Vector3.UnitZ;
            return new AttackLine(pointNow, aim, direction);
        }
    }

    /// <summary>
    /// Judges whether a blade-on-blade contact parries one Armed Dummy thrust (PER-23 resolution, PER-97): the contact
    /// must deflect the Dummy's point off the Fencer's body. One check covers lines 6, 4, 8 and 7 and names the line.
    ///
    /// A clash during the thrust or its hold opens a watch (<see cref="ReportClash"/>). While the blades stay in contact,
    /// or for <see cref="WatchSeconds"/> after the clash, each frame (<see cref="Step"/>) takes the point's deflection:
    /// the larger of the measured tip offset (knocked, pushed and pressed aside, wrist and arm, PER-104, PER-116) and the
    /// deflection the clash's knock will reach (<see cref="BladeDeflection.GetKickMeters"/>, so a fast parry is judged at
    /// once, before the point gets
    /// there). Only the deflection square to the thrust counts. The rest of the thrust shifted by it, carried
    /// <see cref="FollowThroughMeters"/> past the aim, must miss every capsule of the Fencer's body; then the contact is
    /// judged and the watch closes. A watch that ends without that was a contact that did not deflect: the thrust goes on.
    ///
    /// The line: high (6, 4) unless the Fencer's blade pointed down more than <see cref="LowLinePitchDegrees"/> at the
    /// clash (8, 7); outside (6, 8) when the point was turned toward the Fencer's sword-hand side, inside (4, 7) when
    /// across the body. Strict also needs the Fencer's forte on the Dummy's foible (<see cref="IsForteOnFoible"/>).
    /// Positions in the Strip frame (+x the Fencer's right, +y up, +z toward the Dummy), meters, seconds.
    /// </summary>
    public sealed class ParryJudge
    {
        /// <summary>
        /// How long after a clash its deflection is watched once the blades part: past the knock's peak (0.06 s,
        /// <see cref="BladeDeflection.OutSeconds"/>; a light touch's spring peaks at about 0.07 s), short enough that a
        /// light touch is not credited much later.
        /// </summary>
        public const float WatchSeconds = 0.15f;

        /// <summary>The Fencer's forte: the half of the blade nearer the guard (classical halves, not thirds).</summary>
        public const float ForteMaxFraction = 0.5f;

        /// <summary>The Dummy's foible: the half of its blade nearer the point.</summary>
        public const float FoibleMinFraction = 0.5f;

        /// <summary>
        /// The Fencer's blade pointing down more than this below level at the clash closes a low line (8, 7). En garde
        /// and the high parries keep the point level or up; 8 and 7 drop it well below the hand.
        /// </summary>
        public const float LowLinePitchDegrees = 15.0f;

        /// <summary>How far past its aim the deflected point is followed: an attack does not stop at the surface.</summary>
        public const float FollowThroughMeters = 0.25f;

        // A deflection this small sideways does not say which side; the contact's side of the Fencer's guard does.
        private const float SideDeadbandMeters = 0.002f;

        private readonly ParryRule _rule;
        private readonly Hand _fencerHand;
        private BladeClash? _clash;
        private BladeSegment _fencerAtClash;
        private Vector3 _kickDeflection;

        public ParryJudge(ParryRule rule, Hand fencerHand)
        {
            _rule = rule;
            _fencerHand = fencerHand;
        }

        /// <summary>True while a clash is being watched.</summary>
        public bool IsWatching => _clash.HasValue;

        /// <summary>The Fencer's forte met the Dummy's foible (<see cref="ForteMaxFraction"/>, <see cref="FoibleMinFraction"/>).</summary>
        public static bool IsForteOnFoible(float fencerBladeFraction, float dummyBladeFraction)
        {
            return fencerBladeFraction <= ForteMaxFraction && dummyBladeFraction >= FoibleMinFraction;
        }

        /// <summary>
        /// The line a deflection closes: high or low by the Fencer's blade at the clash, outside or inside by the way the
        /// point was turned (<paramref name="squareDeflection"/>), or by which side of the Fencer's guard the contact
        /// was when the point was turned straight up or down.
        /// </summary>
        public static ParryLine NameLine(Vector3 squareDeflection, BladeSegment fencerBlade, Vector3 contactPoint, Hand fencerHand)
        {
            var isLow = fencerBlade.Direction.Y < -(float)Math.Sin(LowLinePitchDegrees * Math.PI / 180.0);
            var towardRight = Math.Abs(squareDeflection.X) >= SideDeadbandMeters
                ? squareDeflection.X
                : contactPoint.X - fencerBlade.GuardPosition.X;
            var isOutside = fencerHand == Hand.Left ? towardRight < 0.0f : towardRight > 0.0f;
            if (isLow)
            {
                return isOutside ? ParryLine.Eight : ParryLine.Seven;
            }

            return isOutside ? ParryLine.Six : ParryLine.Four;
        }

        /// <summary>
        /// True when the rest of the thrust, shifted square to it by <paramref name="squareDeflection"/> and carried
        /// <see cref="FollowThroughMeters"/> past the aim, misses every capsule of <paramref name="fencerBody"/>.
        /// </summary>
        public static bool IsOffBody(AttackLine line, Vector3 squareDeflection, IReadOnlyList<Capsule> fencerBody)
        {
            if (fencerBody == null)
            {
                throw new ArgumentNullException(nameof(fencerBody));
            }

            var start = line.PointNow + squareDeflection;
            var end = line.AimPoint + squareDeflection + (line.Direction * FollowThroughMeters);
            foreach (var capsule in fencerBody)
            {
                var distance = SegmentMath.FindDistanceBetweenSegments(start, end, capsule.Start, capsule.End, out _);
                if (distance <= capsule.RadiusMeters)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Forget the watch: a new attack, or the blades jumped.</summary>
        public void Reset()
        {
            _clash = null;
        }

        /// <summary>A clash during the thrust or its hold: opens (or renews) the watch on its deflection.</summary>
        /// <param name="fencerBlade">The Fencer's blade at the clash (its chord, guard to point).</param>
        public void ReportClash(BladeClash clash, BladeSegment fencerBlade)
        {
            _clash = clash;
            _fencerAtClash = fencerBlade;
            _kickDeflection = clash.PushDirection * BladeDeflection.GetKickMeters(clash.RelativeSpeedMetersPerSecond, clash.OpponentBladeFraction);
        }

        /// <summary>
        /// The blades already touched when the thrust started (a blade held on the Dummy's into its attack), so no new
        /// clash comes: watches from <paramref name="nowSeconds"/> with that contact's place and no kick.
        /// </summary>
        public void ReportHeldContact(BladeClash lastClash, double nowSeconds, BladeSegment fencerBlade)
        {
            ReportClash(
                new BladeClash(nowSeconds, lastClash.ContactPoint, lastClash.FencerBladeFraction, lastClash.OpponentBladeFraction, 0.0f, Vector3.Zero),
                fencerBlade);
        }

        /// <summary>
        /// One frame of the watch. Returns the report once the watched contact has turned the point off the body (then
        /// the watch closes); null while it has not, and once the watch ends without it.
        /// </summary>
        /// <param name="tipDeflection">The Dummy's point off its arm's line now (<see cref="BladeDeflection.TipOffset"/>).</param>
        /// <param name="isInContact">The blades still touch or press, which keeps the watch open.</param>
        /// <param name="line">The rest of the thrust.</param>
        /// <param name="fencerBody">The Fencer's body (<see cref="AttackAim.GetFencerBodyOnStrip"/>).</param>
        public ParryReport? Step(double nowSeconds, Vector3 tipDeflection, bool isInContact, AttackLine line, IReadOnlyList<Capsule> fencerBody)
        {
            if (!_clash.HasValue)
            {
                return null;
            }

            var clash = _clash.Value;
            var deflection = tipDeflection.LengthSquared() >= _kickDeflection.LengthSquared() ? tipDeflection : _kickDeflection;
            var square = deflection - (Vector3.Dot(deflection, line.Direction) * line.Direction);
            if (IsOffBody(line, square, fencerBody))
            {
                _clash = null;
                var isForteOnFoible = IsForteOnFoible(clash.FencerBladeFraction, clash.OpponentBladeFraction);
                return new ParryReport(
                    NameLine(square, _fencerAtClash, clash.ContactPoint, _fencerHand),
                    isForteOnFoible,
                    countsAsParry: _rule == ParryRule.Loose || isForteOnFoible,
                    square.Length(),
                    clash.FencerBladeFraction,
                    clash.OpponentBladeFraction);
            }

            if (!isInContact && nowSeconds > clash.TimeSeconds + WatchSeconds)
            {
                _clash = null;
            }

            return null;
        }
    }
}
