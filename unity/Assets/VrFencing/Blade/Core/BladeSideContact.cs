using System;
using System.Collections.Generic;
using System.Numerics;

namespace VrFencing.Blade
{
    /// <summary>
    /// The blade's side pressed on a body (PER-69): the hand sweeps the flat into the Dummy, or holds it against an arm.
    /// The drawn blade bends round the contact as a cantilever (<see cref="BladeArc.TrySolveSideLoad"/>): clamped at the
    /// guard, pushed square at one point of its length where it touches the body, bent between the guard and that
    /// point, and straight past it to a free point that still whips with the blade's flex. The contact sits where the
    /// bent blade runs along the surface (tangent to it), so the blade lies against the body instead of cutting into
    /// it, and moves along the blade as the push grows. It lets go when the hand's own blade is clear again (it came
    /// back, or slid past the end of the body), and the drawn blade springs back from where it was bent. Not a Touch:
    /// detection still uses the straight blade.
    ///
    /// Only bodies with a round side hold the side: the Dummy's capsules and the Armed Dummy's blade (PER-104, kept to
    /// the side the drawn blade came from); a blade laid on the floor is held off it as before.
    /// Positions in any one frame (the Strip frame in the app), meters.
    /// </summary>
    public sealed class BladeSideContact
    {
        /// <summary>The drawn contact keeps this far out of the body's surface: the blade's half-width and a little.</summary>
        public const float ContactMarginMeters = DrawnBlade.BladeRadiusMeters + 0.0005f;

        /// <summary>
        /// A contact this close to the point is the point's own (pinned or held off as before), not the side's: past it
        /// the blade has slid off the body's end.
        /// </summary>
        public const float PointZoneMeters = 0.03f;

        /// <summary>
        /// A side pressed this near the point can slide off the body's end when the hand goes on past it: the point goes
        /// round the end and the blade springs free. Pressed further up the blade, it cannot, and the drawn hilt lags.
        /// </summary>
        public const float SlideOffMeters = 0.12f;

        // The straight blade is searched for where it lies deepest in a body at this many points.
        private const int SearchSamples = 48;

        // The contact is moved to where the bent blade runs along the surface this many times (each moves it less).
        private const int TangentPasses = 2;

        // Nearer the guard than this the blade cannot bend round anything: the hilt is held off instead.
        private const float MinContactMeters = 0.08f;

        private readonly Vector3[] _bent = new Vector3[DrawnBlade.SampleCount + 1];
        private ElasticaShape _shape;

        /// <summary>How far along the blade from the guard the last built contact is.</summary>
        public float ContactMeters { get; private set; }

        /// <summary>Where the last built contact touches the body (its surface, the blade's half-width out).</summary>
        public Vector3 ContactPoint { get; private set; }

        /// <summary>
        /// Builds the blade bent round the body its straight pose lies deepest in, into <paramref name="samples"/>
        /// (<see cref="DrawnBlade.SampleCount"/> + 1 centerline points, guard first). False when the straight blade is in
        /// no round body, only at its point, or the bend would be sharper than the blade allows; the caller then holds
        /// the drawn blade off as before.
        /// </summary>
        /// <param name="straightBlade">The guard to the real point (the rest shape's chord).</param>
        /// <param name="drawnBefore">The blade as drawn last step, if any: the contact stays on the side it came from.</param>
        /// <param name="flexBend">The point's flex (<see cref="BladeFlex.Bend"/>): the free part past the contact whips with it.</param>
        /// <param name="droop">The rest shape: drawn on top, so the contact is solved for the drooped blade.</param>
        public bool TryShape(BladeSegment straightBlade, IReadOnlyList<BladeObstacle> bodies, Vector3[] drawnBefore, Vector3 flexBend, BladeDroop droop, Vector3[] samples)
        {
            if (!TryFindDeepest(straightBlade, bodies, out var body, out var deepest, out var fraction))
            {
                return false;
            }

            var guard = straightBlade.GuardPosition;
            var direction = straightBlade.Direction;
            Vector3? cameFrom = null;
            if (drawnBefore != null)
            {
                var scaled = fraction * DrawnBlade.SampleCount;
                var index = Math.Min((int)scaled, DrawnBlade.SampleCount - 1);
                cameFrom = Vector3.Lerp(drawnBefore[index], drawnBefore[index + 1], scaled - index);
            }

            var contact = body.PushOut(deepest, ContactMarginMeters, cameFrom);
            var shape = _shape;
            var contactMeters = fraction * BladePose.BladeLengthMeters;
            var across = Vector3.Zero;
            for (var pass = 0; pass < TangentPasses; pass++)
            {
                // Solve for the chord's blade: the rest curve is added on top, so aim it off by the curve there.
                var aim = contact - droop.GetChordOffset(contactMeters / BladePose.BladeLengthMeters) - guard;
                var along = Vector3.Dot(aim, direction);
                var sideways = aim - (along * direction);
                var off = sideways.Length();
                if (off < 1e-6f)
                {
                    return false;
                }

                across = sideways / off;
                if (!BladeArc.TrySolveSideLoad(along, off, ref shape, out contactMeters) || contactMeters < MinContactMeters)
                {
                    return false;
                }

                if (contactMeters > BladePose.BladeLengthMeters - PointZoneMeters)
                {
                    // Wrapped so far the contact reached the point: the point now rides on the body's surface where the
                    // straight point would be, and slides off its end as the hand goes on past.
                    return TryRestPointOnSurface(straightBlade, body, drawnBefore, droop, samples);
                }

                var tangent = BladeArc.FillSideLoad(guard, direction, across, shape, contactMeters, _bent);
                contact = body.FindTangentPoint(contact, tangent, ContactMarginMeters);
            }

            if (BladeArc.FindMaxCurvature(shape, contactMeters) > BladeArc.MaxCurvaturePerMeter)
            {
                return false;
            }

            var tangentAtContact = BladeArc.FillSideLoad(guard, direction, across, shape, contactMeters, samples);
            AddFreeWhipAndRest(samples, contactMeters, tangentAtContact, flexBend, droop);
            _shape = shape;
            ContactMeters = contactMeters;
            ContactPoint = contact;
            return true;
        }

        /// <summary>
        /// The side has wrapped round the body to the point: the point rests on the surface where the straight point
        /// lies inside it (pushed out to the side it came from), and the blade bends to it as an elastica clamped at the
        /// guard. As the hand goes on, that spot slides round the surface until the straight blade is clear of the body
        /// and the blade lets go.
        /// </summary>
        private bool TryRestPointOnSurface(BladeSegment straightBlade, BladeObstacle body, Vector3[] drawnBefore, BladeDroop droop, Vector3[] samples)
        {
            var tip = straightBlade.TipPosition;
            if (body.FindDistance(tip) >= ContactMarginMeters)
            {
                // The straight point is already outside: nothing holds the point, so the side has slid off the end.
                return false;
            }

            var rest = body.PushOut(tip, ContactMarginMeters, drawnBefore?[DrawnBlade.SampleCount]) - droop.GetChordOffset(1.0f);
            var guard = straightBlade.GuardPosition;
            var direction = straightBlade.Direction;
            var chord = rest - guard;
            var along = Vector3.Dot(chord, direction);
            var sideways = chord - (along * direction);
            var off = sideways.Length();
            if (off < 1e-6f)
            {
                return false;
            }

            // Pushed toward the side it is off the hand's line, the blade bulges the other way from the line to the point.
            var bulge = -sideways / off;
            var shape = default(ElasticaShape);
            if (!BladeArc.TrySolve(along, -off, ref shape) || BladeArc.FindMaxCurvature(shape) > BladeArc.MaxCurvaturePerMeter)
            {
                return false;
            }

            BladeArc.FillSamples(guard, direction, bulge, shape, samples);
            AddFreeWhipAndRest(samples, BladePose.BladeLengthMeters, direction, Vector3.Zero, droop);
            _shape = default;
            ContactMeters = BladePose.BladeLengthMeters;
            ContactPoint = rest;
            return true;
        }

        /// <summary>Forgets the last shape, e.g. after the blade jumped.</summary>
        public void Reset()
        {
            _shape = default;
        }

        /// <summary>
        /// The free part past the contact whips with the flex, scaled to its shorter length and shaped like a cantilever
        /// from the contact (none there, all at the point); the whole blade gets the rest curve on top.
        /// </summary>
        private static void AddFreeWhipAndRest(Vector3[] samples, float contactMeters, Vector3 tangent, Vector3 flexBend, BladeDroop droop)
        {
            var freeMeters = BladePose.BladeLengthMeters - contactMeters;
            var whip = flexBend - (Vector3.Dot(flexBend, tangent) * tangent);
            whip *= freeMeters / BladePose.BladeLengthMeters;
            for (var index = 0; index <= DrawnBlade.SampleCount; index++)
            {
                var fraction = (float)index / DrawnBlade.SampleCount;
                var alongBlade = fraction * BladePose.BladeLengthMeters;
                if (alongBlade > contactMeters)
                {
                    var free = (alongBlade - contactMeters) / freeMeters;
                    samples[index] += whip * (free * free * (3.0f - free) * 0.5f);
                }

                samples[index] += droop.GetChordOffset(fraction);
            }
        }

        /// <summary>
        /// Where the straight blade lies deepest inside a round body (closer to its surface than the blade's half-width),
        /// short of the point's own zone. False when it is in none.
        /// </summary>
        private static bool TryFindDeepest(BladeSegment straightBlade, IReadOnlyList<BladeObstacle> bodies, out BladeObstacle deepestBody, out Vector3 deepestPoint, out float deepestFraction)
        {
            deepestBody = default;
            deepestPoint = Vector3.Zero;
            deepestFraction = 0.0f;
            var deepest = DrawnBlade.BladeRadiusMeters;
            var lastFraction = 1.0f - (PointZoneMeters / BladePose.BladeLengthMeters);
            foreach (var body in bodies)
            {
                // Another blade is thin: where the straight blade passes it closest (or lies across it on the wrong
                // side), found exactly rather than at the search points, which could step over it.
                if (!body.IsBlade)
                {
                    continue;
                }

                var distance = body.FindPieceSeparation(straightBlade.GuardPosition, straightBlade.TipPosition, 0, out var point, out var fraction);
                if (distance < deepest && fraction > 0.0f && fraction <= lastFraction)
                {
                    deepest = distance;
                    deepestBody = body;
                    deepestPoint = point;
                    deepestFraction = fraction;
                }
            }

            var boxMin = Vector3.Min(straightBlade.GuardPosition, straightBlade.TipPosition);
            var boxMax = Vector3.Max(straightBlade.GuardPosition, straightBlade.TipPosition);
            foreach (var body in bodies)
            {
                // Bodies nowhere near the straight blade are skipped: the same answer, cheaper.
                if (body.IsPlane || body.IsBlade || !body.IsNearBox(boxMin, boxMax, DrawnBlade.BladeRadiusMeters))
                {
                    continue;
                }

                for (var sample = 1; sample <= SearchSamples; sample++)
                {
                    var fraction = lastFraction * sample / SearchSamples;
                    var point = Vector3.Lerp(straightBlade.GuardPosition, straightBlade.TipPosition, fraction);
                    var distance = body.FindDistance(point);
                    if (distance < deepest)
                    {
                        deepest = distance;
                        deepestBody = body;
                        deepestPoint = point;
                        deepestFraction = fraction;
                    }
                }
            }

            return deepest < DrawnBlade.BladeRadiusMeters;
        }
    }
}
