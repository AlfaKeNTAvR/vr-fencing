// Analytic two-bone IK (PER-110): the end lands on a reachable target, the bones keep their lengths, the middle joint
// bends toward the pole, and an unreachable target gets a straight chain pointing at it.
using System.Numerics;
using NUnit.Framework;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Tests.Dummy
{
    public class TwoBoneIkTests
    {
        private const float Tolerance = 1e-4f;
        private const float Thigh = 0.42f;
        private const float Shin = 0.44f;

        private static readonly Vector3 Hip = new Vector3(0.0f, 0.9f, 0.0f);

        [Test]
        public void AReachableTargetIsReachedWithBothBonesAtTheirLength()
        {
            var target = new Vector3(0.05f, 0.1f, 0.25f);

            var solution = TwoBoneIk.Solve(Hip, target, Thigh, Shin, Vector3.UnitZ);

            Assert.That(solution.IsReached, Is.True);
            Assert.That(Vector3.Distance(solution.End, target), Is.LessThan(Tolerance));
            Assert.That(Vector3.Distance(Hip, solution.Middle), Is.EqualTo(Thigh).Within(Tolerance));
            Assert.That(Vector3.Distance(solution.Middle, solution.End), Is.EqualTo(Shin).Within(Tolerance));
        }

        [Test]
        public void TheKneeBendsTowardThePole()
        {
            var target = new Vector3(0.0f, 0.2f, 0.0f);

            var forward = TwoBoneIk.Solve(Hip, target, Thigh, Shin, Vector3.UnitZ);
            var outward = TwoBoneIk.Solve(Hip, target, Thigh, Shin, -Vector3.UnitX);

            Assert.That(forward.Middle.Z, Is.GreaterThan(0.1f));
            Assert.That(System.Math.Abs(forward.Middle.X), Is.LessThan(Tolerance));
            Assert.That(outward.Middle.X, Is.LessThan(-0.1f));
        }

        [Test]
        public void APoleAlongTheLegStillGivesAKnee()
        {
            var target = new Vector3(0.0f, 0.2f, 0.0f);

            var solution = TwoBoneIk.Solve(Hip, target, Thigh, Shin, -Vector3.UnitY);

            Assert.That(Vector3.Distance(Hip, solution.Middle), Is.EqualTo(Thigh).Within(Tolerance));
            Assert.That(Vector3.Distance(solution.Middle, solution.End), Is.EqualTo(Shin).Within(Tolerance));
        }

        [Test]
        public void AnUnreachableTargetGetsAStraightLegPointingAtIt()
        {
            var target = new Vector3(0.0f, -2.0f, 0.0f);

            var solution = TwoBoneIk.Solve(Hip, target, Thigh, Shin, Vector3.UnitZ);

            Assert.That(solution.IsReached, Is.False);
            Assert.That(Vector3.Distance(Hip, solution.End), Is.EqualTo(Thigh + Shin).Within(1e-3f));
            Assert.That(solution.End.X, Is.EqualTo(0.0f).Within(Tolerance));
            Assert.That(solution.End.Z, Is.EqualTo(0.0f).Within(Tolerance));
        }

        [Test]
        public void TheEnGardeLegsSolveBackToThemselves()
        {
            foreach (var leg in new[] { DummyLegs.EnGarde(1.8f, Hand.Right).Front, DummyLegs.EnGarde(1.8f, Hand.Right).Back })
            {
                var thigh = Vector3.Distance(leg.Hip, leg.Knee);
                var shin = Vector3.Distance(leg.Knee, leg.Ankle);
                var hipToAnkle = Vector3.Normalize(leg.Ankle - leg.Hip);
                var hipToKnee = leg.Knee - leg.Hip;
                var pole = hipToKnee - (hipToAnkle * Vector3.Dot(hipToKnee, hipToAnkle));

                var solution = TwoBoneIk.Solve(leg.Hip, leg.Ankle, thigh, shin, pole);

                Assert.That(Vector3.Distance(solution.Middle, leg.Knee), Is.LessThan(Tolerance));
            }
        }

        [Test]
        public void ABoneLengthThatIsNotPositiveIsRejected()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => TwoBoneIk.Solve(Hip, Vector3.Zero, 0.0f, Shin, Vector3.UnitZ));
        }
    }
}
