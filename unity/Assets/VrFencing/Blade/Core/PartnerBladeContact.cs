using System;
using System.Collections.Generic;
using System.Numerics;
using VrFencing.Settings;

namespace VrFencing.Blade
{
    /// <summary>
    /// This Fencer's blade against the partner's in a Partner session or "Mirror me" (PER-145, decided there: each
    /// headset solves both blades locally). The partner is just another Fencer: one <see cref="BladeContactSolver"/> takes
    /// this Fencer's hand fresh (A) and the partner's hand as drawn here (B), both with a Fencer's grip
    /// (<see cref="BladeContactSolver.FencerGripStiffness"/>) and their own flex, and both blades are drawn from that one
    /// solve, as against the Armed Dummy: bells and grips solid, swept contact, side latch, slide-off. No hand is ever
    /// moved, and nothing from the partner's own solve is drawn here; the two views may differ for about one lag in fast
    /// actions and are never corrected (only logged).
    /// <para>
    /// The partner's drawn blade keeps out of the obstacles it is given (the floor and this Fencer's body); this Fencer's
    /// drawn blade takes <see cref="MyGive"/>. A partner hand that jumps (<see cref="JumpMeters"/>: a rejoin, the end of a
    /// lag spike) starts the contact over, so the side is read again from the geometry and never latched from a stale
    /// pose. Strip frame, meters, seconds.
    /// </para>
    /// </summary>
    public sealed class PartnerBladeContact
    {
        /// <summary>
        /// A partner guard moving further than this in one frame jumped (about 18 m/s at 72 Hz, past any real hand): the
        /// contact starts over.
        /// </summary>
        public const float JumpMeters = 0.25f;

        private readonly BladeContactSolver _solver = new BladeContactSolver();
        private readonly DrawnBlade _partnerDrawn = new DrawnBlade();
        private BladeFlexSoftness? _partnerFlex;
        private Vector3? _lastPartnerGuard;

        /// <summary>The two blades' contact: A is this Fencer's, B the partner's.</summary>
        public BladeContactSolver Contact => _solver;

        /// <summary>The partner's blade as drawn this frame.</summary>
        public DrawnBlade PartnerDrawn => _partnerDrawn;

        /// <summary>What this Fencer's drawn blade takes this frame: their hand's blade turned and bowed by the contact.</summary>
        public BladeGive MyGive { get; private set; }

        /// <summary>The partner's blade from their hand this frame (guard to the real point), before the contact.</summary>
        public BladeSegment PartnerHandChord { get; private set; }

        /// <summary>The partner's blade as the contact leaves it (guard to the real point).</summary>
        public BladeSegment PartnerChord { get; private set; }

        /// <summary>True once a step has run since the last <see cref="Reset"/>.</summary>
        public bool HasStepped { get; private set; }

        /// <summary>Starts over: no side known, the partner's drawn blade from scratch (the partner appeared, jumped or left).</summary>
        public void Reset()
        {
            _solver.Reset();
            _partnerDrawn.Reset();
            _lastPartnerGuard = null;
            HasStepped = false;
        }

        /// <summary>
        /// One frame: solves the two blades and draws the partner's. Call before this Fencer's drawn blade steps with
        /// <see cref="MyGive"/>.
        /// </summary>
        /// <param name="mine">This Fencer's blade from the hand, fresh.</param>
        /// <param name="partner">The partner's blade from their hand as drawn here.</param>
        /// <param name="partnerObstacles">What the partner's drawn blade cannot pass (the floor, this Fencer's body), same order every frame.</param>
        public void Step(HeldBlade mine, HeldBlade partner, IReadOnlyList<BladeObstacle> partnerObstacles, float deltaSeconds)
        {
            if (partnerObstacles == null)
            {
                throw new ArgumentNullException(nameof(partnerObstacles));
            }

            var guard = partner.Chord.GuardPosition;
            if (_lastPartnerGuard.HasValue && Vector3.Distance(_lastPartnerGuard.Value, guard) > JumpMeters)
            {
                Reset();
            }

            _lastPartnerGuard = guard;
            if (_partnerFlex != partner.Flex)
            {
                _partnerDrawn.Flex.SetSoftness(partner.Flex);
                _partnerFlex = partner.Flex;
            }

            PartnerHandChord = partner.Chord;
            _solver.Solve(mine.ToHold(), partner.ToHold(), deltaSeconds);
            MyGive = _solver.GiveA;
            var give = _solver.GiveB;
            PartnerChord = give.Chord;
            if (_solver.IsNear)
            {
                // Near this Fencer's blade it does not whip, so a whip never carries it through: drawn as the contact leaves it.
                _partnerDrawn.Flex.Reset();
            }

            _partnerDrawn.Step(give.Chord, partnerObstacles, partner.GripUp, deltaSeconds, give.Droop);
            HasStepped = true;
        }

        /// <summary>
        /// One frame with no blade of this Fencer's (their controller is not tracked): the partner's blade is drawn as
        /// their hand holds it, and the contact forgets its side.
        /// </summary>
        public void StepAlone(HeldBlade partner, IReadOnlyList<BladeObstacle> partnerObstacles, float deltaSeconds)
        {
            if (partnerObstacles == null)
            {
                throw new ArgumentNullException(nameof(partnerObstacles));
            }

            if (_partnerFlex != partner.Flex)
            {
                _partnerDrawn.Flex.SetSoftness(partner.Flex);
                _partnerFlex = partner.Flex;
            }

            _solver.Reset();
            _lastPartnerGuard = partner.Chord.GuardPosition;
            PartnerHandChord = partner.Chord;
            PartnerChord = partner.Chord;
            _partnerDrawn.Step(partner.Chord, partnerObstacles, partner.GripUp, deltaSeconds, partner.Droop);
            HasStepped = false;
        }
    }
}
