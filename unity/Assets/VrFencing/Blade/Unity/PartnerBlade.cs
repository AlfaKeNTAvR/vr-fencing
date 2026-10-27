using System;
using System.Collections.Generic;
using UnityEngine;
using VrFencing.Partner;
using VrFencing.Settings;
using VrFencing.Strip;

namespace VrFencing.Blade
{
    /// <summary>
    /// The partner's blade as a Partner session (or "Mirror me") hands it to this Fencer's Blade each frame (PER-145):
    /// the partner view samples it before the Blade's frame, and draws it once the Blade has solved the two blades.
    /// </summary>
    public interface IPartnerBladeSource
    {
        /// <summary>"Mirror me": the partner is this Fencer, back over the pretend link (for the blade log).</summary>
        bool IsMirror { get; }

        /// <summary>The Strip frame in the room the partner is drawn in: both blades are solved in it.</summary>
        TrackedPose StripInRoom { get; }

        /// <summary>
        /// The partner's blade from their hand as drawn this frame (Strip frame), and what its drawn blade keeps out of
        /// (the floor and this Fencer's body, the same count every frame); false while no partner blade is shown.
        /// </summary>
        bool TryGetPartnerBlade(out HeldBlade blade, out IReadOnlyList<BladeObstacle> obstacles);

        /// <summary>The partner's newest report of their own view of the contact, turned into this Fencer's frame; null for none.</summary>
        BladeContactReport? GetPartnerReport();

        /// <summary>Draws the partner's blade as <paramref name="contact"/> left it this frame.</summary>
        void DrawPartnerBlade(PartnerBladeContact contact, TrackedPose stripInRoom);
    }

    /// <summary>
    /// This Fencer's blade against the partner's (PER-145), the Armed Dummy's contact path for a partner who is just
    /// another Fencer: the local solve (<see cref="PartnerBladeContact"/>), the clash buzz from a swept clash test
    /// (<see cref="BladeClashDetector"/>, rate-limited by <see cref="ClashBuzzGate"/>), the steel scrape while the blades
    /// press, and a <c>[Blade]</c> line for each press, let-go and slide-off. Haptics and sound come only from this
    /// headset's own solve, at the frame it shows the contact; nothing is played from network messages. Strip frame.
    /// </summary>
    internal sealed class PartnerBlade : IDisposable
    {
        private const string LogTag = "[Blade]";

        private readonly PartnerBladeContact _contact = new PartnerBladeContact();
        private readonly BladeClashDetector _detector = new BladeClashDetector();
        private readonly ClashBuzzGate _clashBuzzGate = new ClashBuzzGate();
        private readonly BladeHapticPlayer _haptics;
        private readonly BladeScrapePlayer _scrape;
        private bool _isActive;

        public PartnerBlade(BladeHapticPlayer haptics)
        {
            _haptics = haptics;
            _scrape = BladeScrapePlayer.TryCreate();
        }

        public PartnerBladeContact Contact => _contact;

        /// <summary>True while a partner blade is shown and this Fencer's blade is solved against it.</summary>
        public bool IsActive => _isActive && _contact.HasStepped;

        /// <summary>How far the blades press this frame (0 while apart or inactive): the bind buzz's strength.</summary>
        public float PressMeters => IsActive ? _contact.Contact.PressMeters : 0.0f;

        /// <summary>True while the blades are near enough that a flex whip could carry this Fencer's drawn blade into the partner's.</summary>
        public bool IsBladeNear => IsActive && _contact.Contact.IsNear;

        /// <summary>
        /// This Fencer's view of the contact as sent with their hand (<see cref="BladeContactReport"/>): side from the
        /// partner's blade toward this Fencer's. Null while inactive.
        /// </summary>
        public BladeContactReport? Report
        {
            get
            {
                if (!IsActive)
                {
                    return null;
                }

                var solver = _contact.Contact;
                return new BladeContactReport(
                    solver.IsInContact,
                    solver.PressMeters,
                    solver.Side ?? System.Numerics.Vector3.Zero,
                    solver.SlidingBlade == 0 || solver.ReturningBlade == 0,
                    solver.SlidingBlade == 1 || solver.ReturningBlade == 1);
            }
        }

        /// <summary>
        /// One frame: solves the two blades, plays the clash buzz and the scrape, logs. Call before this Fencer's drawn
        /// blade steps with <see cref="PartnerBladeContact.MyGive"/>.
        /// </summary>
        public void Step(double nowSeconds, float deltaSeconds, HeldBlade mine, HeldBlade partner, IReadOnlyList<BladeObstacle> partnerObstacles,
            TrackedPose stripInRoom, TrainerSettings settings)
        {
            if (!_isActive)
            {
                _isActive = true;
                Restart();
                Debug.Log($"{LogTag} The partner's blade is solved against this one: both drawn from this headset's own solve.");
            }

            var solver = _contact.Contact;
            var wasPressed = solver.IsInContact;
            var wasReleasing = solver.IsReleasing;
            var fractionsBefore = (solver.FractionA, solver.FractionB);
            _contact.Step(mine, partner, partnerObstacles, deltaSeconds);
            LogContact(wasPressed, wasReleasing);
            PlayScrape(stripInRoom, wasPressed, fractionsBefore, deltaSeconds, settings);
            var clash = _detector.Step(nowSeconds, mine.Chord, _contact.PartnerChord);
            if (clash.HasValue)
            {
                OnClash(clash.Value, nowSeconds, settings);
            }
        }

        /// <summary>One frame with this Fencer's controller not tracked: the partner's blade drawn as their hand holds it.</summary>
        public void StepAlone(HeldBlade partner, IReadOnlyList<BladeObstacle> partnerObstacles, float deltaSeconds)
        {
            _isActive = true;
            _contact.StepAlone(partner, partnerObstacles, deltaSeconds);
            _detector.Reset();
            _scrape?.Silence();
        }

        /// <summary>No partner blade shown any more: forgets the contact and silences the scrape.</summary>
        public void Stop()
        {
            if (!_isActive)
            {
                return;
            }

            _isActive = false;
            Restart();
            Debug.Log($"{LogTag} The partner's blade is gone; this blade is drawn as the hand holds it.");
        }

        /// <summary>A jump (the Strip moved, the blade lost tracking): no sweep across the gap, no side remembered.</summary>
        public void Restart()
        {
            _contact.Reset();
            _detector.Reset();
            _clashBuzzGate.Reset();
            _scrape?.Silence();
        }

        public void Dispose()
        {
            _scrape?.Dispose();
        }

        private void LogContact(bool wasPressed, bool wasReleasing)
        {
            var solver = _contact.Contact;
            if (solver.IsInContact != wasPressed)
            {
                Debug.Log(solver.IsInContact
                    ? $"{LogTag} Blades pressed with the partner's at {solver.FractionA:0.00} of this blade, {solver.FractionB:0.00} of theirs"
                    : $"{LogTag} Blades let go of the partner's");
            }

            if (solver.IsThrough)
            {
                Debug.Log($"{LogTag} Pushed through the partner's blade past every give (PER-147): let go, both blades swing back to the hands");
            }
            else if (solver.IsReleasing != wasReleasing)
            {
                Debug.Log(solver.IsReleasing
                    ? $"{LogTag} Pressed past every give ({solver.PressMeters * 100.0f:0} cm): the {(solver.SlidingBlade == 0 ? "this" : "partner's")} blade slides off toward its point"
                    : $"{LogTag} Slide-off or swing back over");
            }
        }

        /// <summary>The steel scrape (PER-122): louder the harder the blades press, higher the faster the contact slides.</summary>
        private void PlayScrape(TrackedPose stripInRoom, bool wasPressed, (float A, float B) fractionsBefore, float deltaSeconds, TrainerSettings settings)
        {
            if (_scrape == null)
            {
                return;
            }

            var solver = _contact.Contact;
            if (!settings.Blade.ContactSound || !solver.IsInContact)
            {
                _scrape.Silence();
                return;
            }

            var slide = wasPressed
                ? ScrapeSound.FindSlideSpeed(fractionsBefore.A, solver.FractionA, fractionsBefore.B, solver.FractionB, BladePose.BladeLengthMeters, deltaSeconds)
                : 0.0f;
            _scrape.Play(
                StripFrame.ToRoom(stripInRoom, solver.ContactPoint).ToUnity(),
                ScrapeSound.GetVolume(solver.PressMeters, slide),
                ScrapeSound.GetPitch(slide));
        }

        private void OnClash(BladeClash clash, double nowSeconds, TrainerSettings settings)
        {
            var buzzStrength = ClashHaptics.GetStrength(settings.Blade.ClashHapticFloor, clash.RelativeSpeedMetersPerSecond);
            var isBuzzed = _clashBuzzGate.TryPass(nowSeconds);
            if (isBuzzed)
            {
                _haptics?.PlayClash(settings.Blade.ClashHaptic, buzzStrength, settings.Hand);
            }

            Debug.Log($"{LogTag} Clash with the partner's blade {clash.RelativeSpeedMetersPerSecond:0.00} m/s, this blade at {clash.FencerBladeFraction:0.00}, " +
                $"theirs at {clash.OpponentBladeFraction:0.00}, buzz {(isBuzzed ? buzzStrength.ToString("0.00") : "skipped (rate limit)")}");
        }
    }
}
