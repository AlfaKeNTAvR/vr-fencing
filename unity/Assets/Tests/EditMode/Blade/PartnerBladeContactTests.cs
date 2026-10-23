// This Fencer's blade against a partner's (PER-145): one local solve gives both drawn blades, as against the Armed Dummy
// with a Fencer's grip on both; a press bends both and neither goes through; a partner hand that jumps starts the contact
// over; with no blade of this Fencer's the partner's is drawn as their hand holds it.
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Blade;
using VrFencing.Settings;

namespace VrFencing.Tests.Blade
{
    public class PartnerBladeContactTests
    {
        private const float FrameSeconds = 1.0f / 72.0f;

        private static readonly List<BladeObstacle> Floor = new List<BladeObstacle> { BladeObstacle.Floor };

        [Test]
        public void APressBendsBothBladesAndNeitherGoesThrough()
        {
            var contact = new PartnerBladeContact();
            var mine = new DrawnBlade();
            var worstGap = float.MaxValue;

            // The partner's blade still along x at 1 m; this Fencer's blade crosses it from above and presses 3 cm down.
            for (var frame = 0; frame < 72; frame++)
            {
                var press = 0.06f * frame / 72.0f;
                var myBlade = Held(new Vector3(0.0f, 1.05f - press, -0.45f), Vector3.UnitZ);
                var partner = Held(new Vector3(-0.45f, 1.0f, 0.0f), Vector3.UnitX);
                contact.Step(myBlade, partner, Floor, FrameSeconds);
                mine.Step(contact.MyGive.Chord, Floor, myBlade.GripUp, FrameSeconds, contact.MyGive.Droop);
                worstGap = System.Math.Min(worstGap, BladeContactProbe.FindDrawnGap(mine, contact.PartnerDrawn));
            }

            Assert.That(contact.Contact.IsInContact, Is.True);
            Assert.That(contact.Contact.Side.Value.Y, Is.GreaterThan(0.9f), "This Fencer's blade stays above the partner's.");
            Assert.That(contact.MyGive.TurnDegrees + contact.Contact.GiveB.TurnDegrees, Is.GreaterThan(0.0f), "Both grips give.");
            Assert.That(contact.PartnerChord.TipPosition, Is.Not.EqualTo(contact.PartnerHandChord.TipPosition), "The partner's drawn blade gives too.");
            Assert.That(worstGap, Is.GreaterThanOrEqualTo(-0.003f), "Never drawn through each other.");
        }

        [Test]
        public void APartnerHandThatJumpsStartsTheContactOver()
        {
            var contact = new PartnerBladeContact();
            var myBlade = Held(new Vector3(0.0f, 1.0f, -0.45f), Vector3.UnitZ);
            contact.Step(myBlade, Held(new Vector3(-0.45f, 1.02f, 0.0f), Vector3.UnitX), Floor, FrameSeconds);
            contact.Step(myBlade, Held(new Vector3(-0.45f, 1.01f, 0.0f), Vector3.UnitX), Floor, FrameSeconds);
            Assert.That(contact.Contact.Side.HasValue, Is.True);

            // A rejoin: the partner's hand reappears 1 m away, under this blade.
            contact.Step(myBlade, Held(new Vector3(-0.45f, 0.0f, 1.0f), Vector3.UnitX), Floor, FrameSeconds);

            Assert.That(contact.Contact.IsInContact, Is.False);
            Assert.That(contact.Contact.IsEngaged, Is.False, "No side carried across the jump.");
        }

        [Test]
        public void WithNoBladeOfMineThePartnersIsDrawnAsTheirHandHoldsIt()
        {
            var contact = new PartnerBladeContact();
            var partner = Held(new Vector3(-0.45f, 1.0f, 0.0f), Vector3.UnitX);

            contact.StepAlone(partner, Floor, FrameSeconds);

            Assert.That(contact.HasStepped, Is.False, "No contact solved.");
            Assert.That(contact.PartnerChord.TipPosition, Is.EqualTo(partner.Chord.TipPosition));
            Assert.That(Vector3.Distance(contact.PartnerDrawn.GuardPosition, partner.Chord.GuardPosition), Is.LessThan(0.01f));
        }

        [Test]
        public void ABladeHeldByAControllerIsTheBladesOwn()
        {
            var settings = new BladeSettings();
            var controller = new TrackedPose(new Vector3(0.1f, 1.0f, 0.2f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.3f));

            var held = HeldBlade.FromController(controller, settings, Hand.Right);

            var mount = BladePose.GetMountInController(settings, Hand.Right);
            var forte = BladePose.GetBlade(controller, mount);
            Assert.That(Vector3.Distance(held.Chord.GuardPosition, forte.GuardPosition), Is.LessThan(1e-5f), "The guard where the hand holds it.");
            Assert.That(held.Chord.TipPosition.Y, Is.LessThan(forte.TipPosition.Y), "The point hangs below the forte's line.");
            Assert.That(held.ToHold().GripStiffness, Is.EqualTo(BladeContactSolver.FencerGripStiffness));
        }

        /// <summary>A straight blade (no set, no sag) from <paramref name="guard"/> along <paramref name="direction"/>, gripped with the thumb up.</summary>
        private static HeldBlade Held(Vector3 guard, Vector3 direction)
        {
            var chord = new BladeSegment(guard, guard + (direction * BladePose.BladeLengthMeters));
            return new HeldBlade(chord, default, Vector3.UnitY, BladeFlexSoftness.Normal);
        }
    }
}
