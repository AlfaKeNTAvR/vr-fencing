// Dummy rendering (PER-32): every part uses the URP transparent material (the default primitive material renders
// magenta on the Quest), parts have no physics colliders, and the Dummy stands at its distance facing the Fencer.
using System.Linq;
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

        [Test]
        public void ResetDistanceStandsTheDummyBackAtTheScenesPreset()
        {
            var dummy = CreateDummy();
            dummy.Configure(1.65f, Hand.Right);
            var sceneDistance = dummy.DistanceMeters;

            dummy.SetDistance(DummyDistance.Extension);
            dummy.SetDistance(DummyDistance.StepLunge);
            dummy.ResetDistance();

            Assert.That(sceneDistance, Is.EqualTo(DistancePresets.GetDistanceMeters(DummyDistance.Lunge, 1.65f)).Within(Tolerance));
            Assert.That(dummy.DistanceMeters, Is.EqualTo(sceneDistance).Within(Tolerance));
            Assert.That(dummy.transform.localPosition.z, Is.EqualTo(sceneDistance).Within(Tolerance));
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

        [TestCase(Hand.Right)]
        [TestCase(Hand.Left)]
        public void SteppingLegsMoveTheFootZoneAndTheLegSilhouetteWithoutRebuilding(Hand hand)
        {
            var dummy = CreateDummy();
            dummy.Configure(1.65f, hand);
            dummy.SetDistanceMeters(2.0f);
            var footPart = FindPart($"Zone {TargetZone.Foot}");
            var footBefore = footPart.position;
            var renderersBefore = _dummyObject.GetComponentsInChildren<MeshRenderer>().Length;
            var footwork = new DummyFootwork(1.65f, hand, 2.0f, new System.Random(1));
            for (var frame = 0; frame < 10; frame++)
            {
                footwork.Step(1.6f, 1.0f / 72.0f);
            }

            dummy.SetLegs(footwork.Legs);

            Assert.That(FindPart($"Zone {TargetZone.Foot}"), Is.SameAs(footPart), "The foot part was rebuilt.");
            Assert.That(Vector3.Distance(footPart.position, footBefore), Is.GreaterThan(0.02f));
            Assert.That(_dummyObject.GetComponentsInChildren<MeshRenderer>(), Has.Length.EqualTo(renderersBefore));
            AssertRenderedZonesMatchCore(dummy);
            var silhouette = _dummyObject.GetComponentsInChildren<MeshRenderer>()
                .Where(partRenderer => partRenderer.name == "Silhouette")
                .Select(partRenderer => partRenderer.transform)
                .ToArray();
            for (var index = 0; index < dummy.Layout.SilhouetteParts.Count; index++)
            {
                var part = dummy.Layout.SilhouetteParts[index];
                var center = dummy.transform.TransformPoint(((part.Start + part.End) * 0.5f).ToUnity());
                Assert.That(Vector3.Distance(silhouette[index].position, center), Is.LessThan(Tolerance), $"Silhouette part {index}");
            }
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
        public void ZonesAreDrawnThinnerThanTheyAreJudgedAndTheLookRedrawsWithoutRebuilding()
        {
            var dummy = CreateDummy();
            dummy.Configure(1.65f, Hand.Right);
            dummy.SetDistanceMeters(2.0f);
            var judgedRadius = FindZoneOnStrip(dummy, TargetZone.Body).Shape.RadiusMeters;
            var bodyPart = FindPart($"Zone {TargetZone.Body}");

            Assert.That(bodyPart.lossyScale.x, Is.EqualTo(2.0f * judgedRadius * new DummyLookSettings().ZoneThicknessFraction).Within(Tolerance));

            dummy.SetLook(new DummyLookSettings { ZoneThicknessFraction = 1.0f });

            Assert.That(FindPart($"Zone {TargetZone.Body}"), Is.SameAs(bodyPart), "The zone was rebuilt.");
            Assert.That(bodyPart.lossyScale.x, Is.EqualTo(2.0f * judgedRadius).Within(Tolerance));
            Assert.That(FindZoneOnStrip(dummy, TargetZone.Body).Shape.RadiusMeters, Is.EqualTo(judgedRadius), "The judged zone changed.");
            AssertRenderedZonesMatchCore(dummy);
        }

        [Test]
        public void HiddenZonesStillShowWhenLitOrFlashed()
        {
            var dummy = CreateDummy();
            dummy.Configure(1.65f, Hand.Right);

            dummy.SetLook(new DummyLookSettings { ShowTargetZones = false });
            dummy.LightZone(TargetZone.Wrist);
            dummy.FlashZone(TargetZone.Thigh);
            dummy.SetLook(new DummyLookSettings { ShowTargetZones = false });

            Assert.That(FindPart($"Zone {TargetZone.Body}").GetComponent<Renderer>().enabled, Is.False, "A dim zone is hidden.");
            Assert.That(FindPart($"Zone {TargetZone.Wrist}").GetComponent<Renderer>().enabled, Is.True, "A lit zone shows.");
            Assert.That(FindPart($"Zone {TargetZone.Thigh}").GetComponent<Renderer>().enabled, Is.True, "A flashing zone shows.");

            dummy.SetLook(new DummyLookSettings());

            Assert.That(FindPart($"Zone {TargetZone.Body}").GetComponent<Renderer>().enabled, Is.True);
        }

        [Test]
        public void TheLookSetsTheBodyAndDimZoneOpacityAndZonesDrawOverTheBody()
        {
            var dummy = CreateDummy();
            dummy.Configure(1.65f, Hand.Right);

            dummy.SetLook(new DummyLookSettings { BodyOpacity = 0.8f, ZoneOpacity = 0.4f });

            var zoneRenderer = FindPart($"Zone {TargetZone.Body}").GetComponent<Renderer>();
            var bodyRenderer = FindPart("Silhouette").GetComponent<Renderer>();
            Assert.That(GetColor(bodyRenderer).a, Is.EqualTo(0.8f).Within(Tolerance));
            Assert.That(GetColor(zoneRenderer).a, Is.EqualTo(0.4f).Within(Tolerance));
            Assert.That(zoneRenderer.sortingOrder, Is.GreaterThan(bodyRenderer.sortingOrder));
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

        private static ZoneCapsule FindZoneOnStrip(DummyBehaviour dummy, TargetZone zone)
        {
            foreach (var zoneCapsule in dummy.GetZonesOnStrip())
            {
                if (zoneCapsule.Zone == zone)
                {
                    return zoneCapsule;
                }
            }

            throw new System.ArgumentOutOfRangeException(nameof(zone), zone, "No such zone on the Dummy.");
        }

        private static Color GetColor(Renderer partRenderer)
        {
            var block = new MaterialPropertyBlock();
            partRenderer.GetPropertyBlock(block);
            return block.GetColor("_BaseColor");
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
