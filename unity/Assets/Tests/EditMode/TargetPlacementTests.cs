// Targets must land in front of the Fencer, at the layout heights, in world space.
using NUnit.Framework;
using UnityEngine;
using VrFencing.Prototypes.BladeFeel;

namespace VrFencing.Tests
{
    public class TargetPlacementTests
    {
        private static readonly BladeFeelPresets.TargetLayout[] Layout =
        {
            new("hand", 1.6f, 0.15f, 1.0f, 0.05f),
            new("foot", 2.0f, 0f, 0.1f, 0.06f),
        };

        [Test]
        public void HeadFacingPlusXPlacesTargetsAtPlusX()
        {
            var headPosition = new Vector3(1f, 1.7f, -2f);
            var facingPlusX = Quaternion.LookRotation(Vector3.right, Vector3.up);

            var targets = TargetPlacement.PlaceTargets(headPosition, facingPlusX, 0f, Layout);

            // Forward is +x; the Fencer's right is then -z.
            Assert.That(targets[0].Center.x, Is.EqualTo(1f + 1.6f).Within(1e-4f));
            Assert.That(targets[0].Center.y, Is.EqualTo(1.0f).Within(1e-4f));
            Assert.That(targets[0].Center.z, Is.EqualTo(-2f - 0.15f).Within(1e-4f));
            Assert.That(targets[1].Center, Is.EqualTo(new Vector3(3f, 0.1f, -2f)).Using(Vector3EqualityComparer));
        }

        [Test]
        public void HeightsAreAboveTheFloorNotTheHead()
        {
            var targets = TargetPlacement.PlaceTargets(new Vector3(0f, 1.7f, 0f), Quaternion.identity, 0.25f, Layout);

            Assert.That(targets[0].Center.y, Is.EqualTo(1.25f).Within(1e-4f));
            Assert.That(targets[0].Center.z, Is.EqualTo(1.6f).Within(1e-4f));
        }

        [TestCase(80f)]
        [TestCase(-80f)]
        [TestCase(89.9f)]
        public void SteepHeadPitchStillPlacesTargetsInFront(float pitchDownDegrees)
        {
            // Facing +x while looking steeply down (e.g. at the left controller) or up.
            var head = Quaternion.LookRotation(Vector3.right, Vector3.up) * Quaternion.Euler(pitchDownDegrees, 0f, 0f);

            var targets = TargetPlacement.PlaceTargets(Vector3.zero, head, 0f, Layout);

            Assert.That(targets[1].Center.x, Is.EqualTo(2f).Within(1e-3f));
            Assert.That(targets[1].Center.z, Is.EqualTo(0f).Within(1e-3f));
        }

        private static readonly System.Collections.Generic.IEqualityComparer<Vector3> Vector3EqualityComparer =
            new UnityEngine.TestTools.Utils.Vector3EqualityComparer(1e-4f);
    }
}
