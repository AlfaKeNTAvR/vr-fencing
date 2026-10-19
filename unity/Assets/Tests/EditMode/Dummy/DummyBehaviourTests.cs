// Dummy rendering (PER-32): every part uses the URP transparent material (the default primitive material renders
// magenta on the Quest), parts have no physics colliders, and the Dummy stands at its distance facing the Fencer.
using NUnit.Framework;
using UnityEngine;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Tests.Dummy
{
    public class DummyBehaviourTests
    {
        private const string MaterialResourcePath = "VrFencingDummy";
        private const float Tolerance = 1e-4f;

        private GameObject _dummyObject;

        [TearDown]
        public void DestroyDummy()
        {
            if (_dummyObject != null)
            {
                Object.DestroyImmediate(_dummyObject);
            }
        }

        [Test]
        public void MaterialIsUrpUnlitAndTransparent()
        {
            var material = Resources.Load<Material>(MaterialResourcePath);

            Assert.That(material, Is.Not.Null);
            Assert.That(material.shader.name, Is.EqualTo("Universal Render Pipeline/Unlit"));
            Assert.That(material.GetFloat("_Surface"), Is.EqualTo(1.0f), "1 is URP's transparent surface type.");
            Assert.That(material.renderQueue, Is.EqualTo((int)UnityEngine.Rendering.RenderQueue.Transparent));
        }

        [Test]
        public void ConfigureDrawsEveryCapsuleWithTheUrpMaterialAndNoColliders()
        {
            var dummy = CreateDummy();

            dummy.Configure(1.65f, Hand.Right);

            var material = Resources.Load<Material>(MaterialResourcePath);
            var renderers = _dummyObject.GetComponentsInChildren<MeshRenderer>();
            Assert.That(renderers, Has.Length.EqualTo(dummy.Layout.Zones.Count + dummy.Layout.SilhouetteParts.Count));
            Assert.That(renderers, Has.All.Matches<MeshRenderer>(partRenderer => partRenderer.sharedMaterial == material));
            Assert.That(_dummyObject.GetComponentsInChildren<Collider>(), Is.Empty);
        }

        [Test]
        public void ConfiguringAgainReplacesTheParts()
        {
            var dummy = CreateDummy();
            dummy.Configure(1.65f, Hand.Right);

            dummy.Configure(1.80f, Hand.Left);

            Assert.That(_dummyObject.GetComponentsInChildren<MeshRenderer>(), Has.Length.EqualTo(dummy.Layout.Zones.Count + dummy.Layout.SilhouetteParts.Count));
            Assert.That(dummy.Layout.HeightMeters, Is.EqualTo(1.80f));
        }

        [Test]
        public void StandsAtThePresetDistanceFacingTheFencer()
        {
            var dummy = CreateDummy();
            dummy.Configure(1.65f, Hand.Right);

            dummy.SetDistance(DummyDistance.Extension);

            var expected = DistancePresets.GetDistanceMeters(DummyDistance.Extension, 1.65f);
            Assert.That(dummy.transform.localPosition.z, Is.EqualTo(expected).Within(Tolerance));
            Assert.That(Vector3.Dot(dummy.transform.forward, Vector3.back), Is.EqualTo(1.0f).Within(Tolerance));
        }

        [TestCase(Hand.Right)]
        [TestCase(Hand.Left)]
        public void RenderedZonesMatchTheZonesOnTheStrip(Hand hand)
        {
            var dummy = CreateDummy();
            dummy.Configure(1.65f, hand);
            dummy.SetDistanceMeters(2.0f);

            AssertRenderedZonesMatchCore(dummy);
        }

        [TestCase(Hand.Right)]
        [TestCase(Hand.Left)]
        public void PosingTheArmMovesTheRenderedArmZonesWithoutRebuilding(Hand hand)
        {
            var dummy = CreateDummy();
            dummy.Configure(1.65f, hand);
            dummy.SetDistanceMeters(2.0f);
            var handPart = FindPart($"Zone {TargetZone.Hand}");
            var handBefore = handPart.position;

            dummy.SetArmPose(SwordArmPose.ArmInLine.WithElbowBend(0.6f));

            Assert.That(FindPart($"Zone {TargetZone.Hand}"), Is.SameAs(handPart), "The hand part was rebuilt.");
            Assert.That(Vector3.Distance(handPart.position, handBefore), Is.GreaterThan(0.05f));
            AssertRenderedZonesMatchCore(dummy);
        }

        [Test]
        public void TheHandHangsUnderTheWristJoint()
        {
            var dummy = CreateDummy();
            dummy.Configure(1.65f, Hand.Right);

            var wrist = dummy.GetJointTransform(ArmJoint.Wrist);

            Assert.That(wrist, Is.Not.Null);
            Assert.That(FindPart($"Zone {TargetZone.Hand}").parent, Is.SameAs(wrist));
            Assert.That(wrist.parent, Is.SameAs(dummy.GetJointTransform(ArmJoint.Elbow)));
            Assert.That(dummy.GetJointTransform(ArmJoint.Elbow).parent, Is.SameAs(dummy.GetJointTransform(ArmJoint.Shoulder)));
        }

        [Test]
        public void RejectsANegativeDistance()
        {
            var dummy = CreateDummy();

            Assert.Throws<System.ArgumentOutOfRangeException>(() => dummy.SetDistanceMeters(-0.5f));
        }

        private void AssertRenderedZonesMatchCore(DummyBehaviour dummy)
        {
            foreach (var zone in dummy.GetZonesOnStrip())
            {
                var part = FindPart($"Zone {zone.Zone}");
                Assert.That(part, Is.Not.Null, zone.Zone.ToString());
                Assert.That(Vector3.Distance(part.position, zone.Shape.Center.ToUnity()), Is.LessThan(Tolerance), zone.Zone.ToString());
            }
        }

        private Transform FindPart(string partName)
        {
            foreach (var child in _dummyObject.GetComponentsInChildren<Transform>())
            {
                if (child.name == partName)
                {
                    return child;
                }
            }

            return null;
        }

        private DummyBehaviour CreateDummy()
        {
            _dummyObject = new GameObject("Test Dummy");
            return _dummyObject.AddComponent<DummyBehaviour>();
        }
    }
}
