// The épée hilt models (PER-41, built by tools/models/build_models.py): one per grip in Resources, imported without
// materials of their own, sized to the FIE bell and lying behind the guard's front along -z, light enough for the Quest,
// and drawn by BladeVisual for every grip Setting with the blade's and grip's URP materials, mirrored for the left hand.
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VrFencing.Blade;
using VrFencing.Settings;

namespace VrFencing.Tests.Blade
{
    public class HiltModelTests
    {
        private const int MaxTrianglesPerHilt = 4000;
        private GameObject _mount;

        [TearDown]
        public void DestroyMount()
        {
            if (_mount != null)
            {
                Object.DestroyImmediate(_mount);
            }
        }

        [TestCase(BladeMount.French, PistolGripShape.Visconti, "EpeeHiltFrench")]
        [TestCase(BladeMount.FrenchPosted, PistolGripShape.Belgian, "EpeeHiltFrench")]
        [TestCase(BladeMount.Pistol, PistolGripShape.Visconti, "EpeeHiltVisconti")]
        [TestCase(BladeMount.Pistol, PistolGripShape.Belgian, "EpeeHiltBelgian")]
        [TestCase(BladeMount.Center, PistolGripShape.Visconti, "EpeeHiltVisconti")]
        [TestCase(BladeMount.Center, PistolGripShape.Belgian, "EpeeHiltBelgian")]
        public void EveryGripHasItsHiltModel(BladeMount mount, PistolGripShape pistolGrip, string expectedModel)
        {
            var path = BladeVisual.GetHiltResourcePath(mount, pistolGrip);

            Assert.That(path, Is.EqualTo("Hilts/" + expectedModel));
            Assert.That(Resources.Load<GameObject>(path), Is.Not.Null, $"No model at Resources/{path}.");
        }

        [TestCase("EpeeHiltFrench")]
        [TestCase("EpeeHiltVisconti")]
        [TestCase("EpeeHiltBelgian")]
        public void TheHiltModelIsTheFieBellWithTheGripBehindItAndNoMaterialsOfItsOwn(string modelName)
        {
            var model = Resources.Load<GameObject>("Hilts/" + modelName);
            var parts = model.GetComponentsInChildren<MeshFilter>().ToDictionary(part => part.name, part => part.sharedMesh);

            Assert.That(parts.Keys, Is.SupersetOf(new[] { "Bell", "Grip", "Pommel" }));
            Assert.That(model.GetComponentsInChildren<Collider>(), Is.Empty);
            // With no materials of its own, the importer puts the pipeline's default material on every part until BladeVisual replaces it.
            var modelPath = UnityEditor.AssetDatabase.GetAssetPath(model);
            Assert.That(UnityEditor.AssetDatabase.LoadAllAssetRepresentationsAtPath(modelPath).OfType<Material>(), Is.Empty,
                "The importer must make no materials (ModelImportSettings): they would be Standard-shader materials, magenta on the Quest.");

            Assert.That(parts.Values.Sum(mesh => mesh.triangles.Length / 3), Is.LessThanOrEqualTo(MaxTrianglesPerHilt));

            // The mount frame: the guard's front at the origin, the blade along +z, the grip behind along -z. Measured
            // on an instance at the origin, since the importer keeps the file's axes on the parts' transforms.
            _mount = Object.Instantiate(model);
            _mount.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var bounds = _mount.GetComponentsInChildren<MeshRenderer>().ToDictionary(part => part.name, part => part.bounds);
            var bell = bounds["Bell"];
            Assert.That(bell.size.x, Is.EqualTo(BladePose.GuardDiameterMeters).Within(0.002f), "The bell is 13.5 cm across (FIE m.17.1).");
            Assert.That(bell.size.y, Is.EqualTo(BladePose.GuardDiameterMeters).Within(0.002f));
            Assert.That(bell.max.z, Is.EqualTo(0.0f).Within(0.002f), "The bell's apex is at the guard's front.");
            Assert.That(bell.min.z, Is.InRange(-0.055f, -0.03f), "The bell is 3 to 5.5 cm deep (FIE m.17.1).");
            var grip = bounds["Grip"];
            Assert.That(grip.max.z, Is.LessThan(0.0f), "The grip lies behind the guard.");
            Assert.That(grip.min.z, Is.InRange(-0.25f, -0.10f), "A hand's length of grip.");
            Assert.That(bounds["Pommel"].center.z, Is.LessThan(grip.center.z), "The pommel is at the grip's end.");
            if (bounds.TryGetValue("Prong", out var prong))
            {
                Assert.That(prong.center.y, Is.LessThan(0.0f), "A pistol grip's finger prong hangs below the blade, on the palm's side.");
                Assert.That(prong.center.x, Is.GreaterThan(0.0f), "For the right hand the prong sits on the outside, between the index and middle fingers.");
            }
        }

        [TestCase(Hand.Right, 1.0f)]
        [TestCase(Hand.Left, -1.0f)]
        public void TheBladeVisualDrawsTheHiltInTheUrpMaterialsMirroredForTheLeftHand(Hand hand, float expectedSideScale)
        {
            _mount = new GameObject("Mount");
            var visual = new BladeVisual(_mount.transform);

            visual.SetGrip(BladeMount.Pistol, PistolGripShape.Belgian, hand);

            var hilt = _mount.transform.Find("Hilt");
            Assert.That(hilt.childCount, Is.EqualTo(1));
            var model = hilt.GetChild(0);
            Assert.That(model.name, Is.EqualTo("EpeeHiltBelgian"));
            Assert.That(model.localScale.x, Is.EqualTo(expectedSideScale));
            var steel = Resources.Load<Material>("VrFencingBlade");
            var rubber = Resources.Load<Material>("VrFencingGrip");
            foreach (var partRenderer in model.GetComponentsInChildren<MeshRenderer>())
            {
                var expected = partRenderer.name == "Bell" || partRenderer.name == "Pommel" || partRenderer.name == "Collar" ? steel : rubber;
                Assert.That(partRenderer.sharedMaterials, Has.All.SameAs(expected), partRenderer.name);
            }

            var ghost = _mount.transform.Find("Hand Ghost");
            Assert.That(ghost.GetComponentsInChildren<MeshRenderer>(true).Select(part => part.sharedMaterial.name), Has.All.EqualTo("VrFencingBladeGhost"));

            visual.SetGrip(BladeMount.French, PistolGripShape.Belgian, hand);
            Assert.That(hilt.childCount, Is.EqualTo(1), "Changing the grip replaces the hilt.");
            Assert.That(hilt.GetChild(0).name, Is.EqualTo("EpeeHiltFrench"));
        }
    }
}
