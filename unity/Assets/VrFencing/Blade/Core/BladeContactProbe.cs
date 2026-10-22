using System;
using System.Numerics;

namespace VrFencing.Blade
{
    /// <summary>
    /// Reads the blades' state into a <see cref="BladeContactSample"/> for the blade-contact log (PER-136), and measures
    /// what the solver itself does not: how far each drawn blade keeps out of the other's bell guard, and how far apart
    /// the two drawn blades are. Read-only: it never changes the blades. Strip frame, meters.
    /// </summary>
    public static class BladeContactProbe
    {
        /// <summary>The drawn blade is looked at this many times finer than its samples for the bell gap (a bell is 13.5 cm).</summary>
        public const int BellChecksPerSample = 4;

        /// <summary>
        /// Fills the Fencer's blade from its hand (the chord to the real point) and its drawn blade, with no contact: the
        /// Dummy's fields are left as they are.
        /// </summary>
        public static void ReadFencer(ref BladeContactSample sample, BladeSegment fencerHand, DrawnBlade fencerDrawn)
        {
            if (fencerDrawn == null)
            {
                throw new ArgumentNullException(nameof(fencerDrawn));
            }

            sample.Fencer.Guard = fencerHand.GuardPosition;
            sample.Fencer.RealPoint = fencerHand.TipPosition;
            sample.Fencer.DrawnPoint = fencerDrawn.HasShape ? fencerDrawn.GetSample(DrawnBlade.SampleCount) : fencerHand.TipPosition;
            sample.Fencer.GripTurnDegrees = 0.0f;
            sample.Fencer.BowMeters = 0.0f;
            sample.Fencer.SlideOff = SlideOffPhase.None;
            sample.Fencer.BellGapMeters = float.NaN;
            sample.Fencer.HandleGapMeters = float.NaN;
        }

        /// <summary>
        /// Fills the contact and the Dummy's blade and hand from the Armed Dummy's blade after its step this frame, and the
        /// Fencer's give, slide-off and bell gap; call after <see cref="ReadFencer"/> and after the Fencer's drawn blade
        /// stepped.
        /// </summary>
        public static void ReadArmed(ref BladeContactSample sample, ArmedDummyBlade dummy, DrawnBlade fencerDrawn)
        {
            if (dummy == null)
            {
                throw new ArgumentNullException(nameof(dummy));
            }

            if (fencerDrawn == null)
            {
                throw new ArgumentNullException(nameof(fencerDrawn));
            }

            var contact = dummy.Contact;
            sample.IsDummyArmed = true;
            sample.Fencer.GripTurnDegrees = contact.GiveA.TurnDegrees;
            sample.Fencer.BowMeters = contact.GiveA.BowMeters;
            sample.Fencer.SlideOff = GetSlideOff(contact, 0);

            sample.Dummy.Guard = dummy.HandChord.GuardPosition;
            sample.Dummy.RealPoint = dummy.HandChord.TipPosition;
            sample.Dummy.DrawnPoint = dummy.Drawn.HasShape ? dummy.Drawn.GetSample(DrawnBlade.SampleCount) : dummy.Chord.TipPosition;
            sample.Dummy.GripTurnDegrees = contact.GiveB.TurnDegrees;
            sample.Dummy.BowMeters = contact.GiveB.BowMeters;
            sample.Dummy.SlideOff = GetSlideOff(contact, 1);

            var hasBoth = fencerDrawn.HasShape && dummy.Drawn.HasShape;
            sample.Fencer.BellGapMeters = hasBoth ? FindBellGap(fencerDrawn, dummy.Drawn) : float.NaN;
            sample.Dummy.BellGapMeters = hasBoth ? FindBellGap(dummy.Drawn, fencerDrawn) : float.NaN;
            sample.Fencer.HandleGapMeters = hasBoth ? FindHandleGap(fencerDrawn, dummy.Drawn) : float.NaN;
            sample.Dummy.HandleGapMeters = hasBoth ? FindHandleGap(dummy.Drawn, fencerDrawn) : float.NaN;
            sample.DrawnGapMeters = hasBoth ? FindDrawnGap(fencerDrawn, dummy.Drawn) : float.NaN;

            sample.IsInContact = contact.IsInContact;
            sample.IsEngaged = contact.IsEngaged;
            sample.ContactPoint = contact.ContactPoint;
            sample.ContactOnFencer = contact.FractionA;
            sample.ContactOnDummy = contact.FractionB;
            sample.OverlapMeters = contact.PressMeters;
            sample.PressNewtons = contact.PressNewtons;
            sample.Side = contact.Side ?? Vector3.Zero;
            sample.SaturatedStages = contact.SaturatedStages;
            sample.ClosestMeters = contact.ClosestMeters;

            sample.DummyHandGive = dummy.YieldTipOffset;
            sample.DummyDeflectionPhase = dummy.Deflection.Phase;
            sample.DummyTipOffset = dummy.Deflection.TipOffset;
        }

        /// <summary>Empties the Fencer's blade (written as empty cells): the sword-hand controller is not tracked.</summary>
        public static void ClearFencer(ref BladeContactSample sample)
        {
            var none = new Vector3(float.NaN);
            sample.Fencer = new BladeTrace { Guard = none, RealPoint = none, DrawnPoint = none, BellGapMeters = float.NaN, HandleGapMeters = float.NaN };
        }

        /// <summary>Empties the contact and the Dummy's fields: the Dummy holds no blade this frame.</summary>
        public static void ClearArmed(ref BladeContactSample sample)
        {
            sample.IsDummyArmed = false;
            sample.Dummy = new BladeTrace { BellGapMeters = float.NaN, HandleGapMeters = float.NaN };
            sample.IsInContact = false;
            sample.IsEngaged = false;
            sample.ContactPoint = Vector3.Zero;
            sample.ContactOnFencer = float.NaN;
            sample.ContactOnDummy = float.NaN;
            sample.OverlapMeters = 0.0f;
            sample.PressNewtons = 0.0f;
            sample.Side = Vector3.Zero;
            sample.SaturatedStages = 0;
            sample.ClosestMeters = float.MaxValue;
            sample.DrawnGapMeters = float.NaN;
            sample.DummyHandGive = Vector3.Zero;
            sample.DummyDeflectionPhase = DeflectionPhase.Spring;
            sample.DummyTipOffset = Vector3.Zero;
        }

        /// <summary>
        /// How far <paramref name="other"/>, drawn, keeps out of <paramref name="owner"/>'s bell guard at its closest,
        /// surface to surface (negative: through the bell). The other blade's guard end is left out: its own hilt is
        /// never near this bell's front.
        /// </summary>
        public static float FindBellGap(DrawnBlade owner, DrawnBlade other)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            var bell = BladeObstacle.FromBell(owner.GuardPosition, owner.HiltDirection);
            var steps = DrawnBlade.SampleCount * BellChecksPerSample;
            var closest = float.MaxValue;
            for (var step = 1; step <= steps; step++)
            {
                var distance = bell.FindDistance(other.GetCenterlinePoint((float)step / steps));
                closest = Math.Min(closest, distance);
            }

            return closest - DrawnBlade.BladeRadiusMeters;
        }

        /// <summary>
        /// How far <paramref name="other"/>, drawn, keeps out of <paramref name="owner"/>'s grip behind its bell at its
        /// closest, surface to surface (negative: through the grip): the cylinder the contact solver keeps the other blade
        /// out of (<see cref="BladeContactSolver.GripRadiusMeters"/>, <see cref="BladeContactSolver.GripLengthMeters"/>).
        /// </summary>
        public static float FindHandleGap(DrawnBlade owner, DrawnBlade other)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            const float HalfLength = BladeContactSolver.GripLengthMeters * 0.5f;
            var axis = owner.HiltDirection;
            var center = owner.GuardPosition - (axis * (BladePose.GuardDepthMeters + HalfLength));
            var steps = DrawnBlade.SampleCount * BellChecksPerSample;
            var closest = float.MaxValue;
            for (var step = 1; step <= steps; step++)
            {
                var distance = BellGuard.FindCylinderDistance(other.GetCenterlinePoint((float)step / steps), center, axis, BladeContactSolver.GripRadiusMeters, HalfLength, out _);
                closest = Math.Min(closest, distance);
            }

            return closest - DrawnBlade.BladeRadiusMeters;
        }

        /// <summary>How far apart the two drawn blades are at their closest, surface to surface (negative: through each other).</summary>
        public static float FindDrawnGap(DrawnBlade a, DrawnBlade b)
        {
            if (a == null)
            {
                throw new ArgumentNullException(nameof(a));
            }

            if (b == null)
            {
                throw new ArgumentNullException(nameof(b));
            }

            // The closest pair of samples bounds it from above, so the pieces below need only look near it.
            var closestSquared = float.MaxValue;
            for (var indexA = 0; indexA <= DrawnBlade.SampleCount; indexA++)
            {
                var sampleA = a.GetSample(indexA);
                for (var indexB = 0; indexB <= DrawnBlade.SampleCount; indexB++)
                {
                    closestSquared = Math.Min(closestSquared, Vector3.DistanceSquared(sampleA, b.GetSample(indexB)));
                }
            }

            var closest = (float)Math.Sqrt(closestSquared);
            for (var pieceA = 0; pieceA < DrawnBlade.SampleCount; pieceA++)
            {
                var a0 = a.GetSample(pieceA);
                var a1 = a.GetSample(pieceA + 1);
                var middleA = (a0 + a1) * 0.5f;
                var halfA = Vector3.Distance(a0, a1) * 0.5f;
                for (var pieceB = 0; pieceB < DrawnBlade.SampleCount; pieceB++)
                {
                    var b0 = b.GetSample(pieceB);
                    var b1 = b.GetSample(pieceB + 1);

                    // Pieces whose middles are further apart than their half-lengths and the closest so far cannot be closer.
                    var reach = closest + halfA + (Vector3.Distance(b0, b1) * 0.5f);
                    if (Vector3.DistanceSquared(middleA, (b0 + b1) * 0.5f) > reach * reach)
                    {
                        continue;
                    }

                    var distance = SegmentMath.FindDistanceBetweenSegments(a0, a1, b0, b1, out _);
                    closest = Math.Min(closest, distance);
                }
            }

            return closest - (2.0f * DrawnBlade.BladeRadiusMeters);
        }

        private static SlideOffPhase GetSlideOff(BladeContactSolver contact, int blade)
        {
            if (contact.SlidingBlade == blade)
            {
                return SlideOffPhase.Sliding;
            }

            return contact.ReturningBlade == blade ? SlideOffPhase.Returning : SlideOffPhase.None;
        }
    }
}
