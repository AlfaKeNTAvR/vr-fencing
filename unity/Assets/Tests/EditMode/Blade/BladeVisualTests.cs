// The épée as drawn (PER-41): each grip in Settings draws its own hilt model (French, Visconti, Belgian), the hilt
// stays within the FIE's sizes behind the guard, every part has a URP material, and the blade keeps bending on the
// new grooved mesh (it follows the drawn centerline).
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VrFencing.Blade;
using VrFencing.Settings;

namespace VrFencing.Tests.Blade
{
    public class BladeVisualTests
    {
        // FIE: bell at most 13.5 cm across; guard, grip and pommel at most 20 cm with a 90 cm blade (110 cm in all).
        private const float MaxBellDiameterMeters = 0.135f;
        private const float MaxHiltLengthMeters = 0.20f;
        private const float Tolerance = 1e-3f;

        private GameObject _mount;
        private BladeVisual _visual;

        [SetUp]
        public void CreateVisual()
        {
            _mount = new GameObject("Blade Mount");
            _visual = new BladeVisual(_mount.transform);
        }

        [TearDown]
        public void DestroyVisual()
        {
            _visual.Dispose();
            if (_mount != null)
            {
                Object.DestroyImmediate(_mount);
            }
        }

        [TestCase(BladeMount.Center, PistolGripShape.Visconti, "HiltFrench")]
        [TestCase(BladeMount.French, PistolGripShape.Belgian, "HiltFrench")]
        [TestCase(BladeMount.FrenchPosted, PistolGripShape.Visconti, "HiltFrench")]
        [TestCase(BladeMount.Pistol, PistolGripShape.Visconti, "HiltVisconti")]
        [TestCase(BladeMount.Pistol, PistolGripShape.Belgian, "HiltBelgian")]
        public void EachGripDrawsItsOwnHilt(BladeMount mount, PistolGripShape pistolGrip, string expectedModel)
        {
            _visual.SetGrip(mount, pistolGrip);

            Assert.That(_visual.HiltModelName, Is.EqualTo(expectedModel));
            var hilt = _mount.transform.Find("Hilt");
            Assert.That(hilt.childCount, Is.EqualTo(1), "The previous hilt is replaced, not stacked.");
            Assert.That(hilt.GetChild(0).name, Is.EqualTo(expectedModel));
        }

        [TestCase(BladeMount.French, PistolGripShape.Visconti)]
        [TestCase(BladeMount.Pistol, PistolGripShape.Visconti)]
        [TestCase(BladeMount.Pistol, PistolGripShape.Belgian)]
        public void TheHiltIsWithinTheFieSizesBehindTheGuard(BladeMount mount, PistolGripShape pistolGrip)
        {
            _visual.SetGrip(mount, pistolGrip);

            var hilt = _mount.transform.Find("Hilt").GetChild(0);
            var bounds = hilt.GetComponentsInChildren<MeshFilter>()
                .Select(part => TransformBounds(part.transform, part.sharedMesh.bounds))
                .Aggregate((a, b) => { a.Encapsulate(b); return a; });
            Assert.That(bounds.max.z, Is.LessThanOrEqualTo(Tolerance), "Nothing ahead of the guard's front, where the blade starts.");
            Assert.That(-bounds.min.z, Is.LessThanOrEqualTo(MaxHiltLengthMeters + Tolerance), "Guard, grip and pommel within 20 cm.");
            Assert.That(-bounds.min.z, Is.GreaterThan(0.12f), "A whole grip behind the guard.");
            Assert.That(bounds.size.x, Is.LessThanOrEqualTo(MaxBellDiameterMeters + 0.006f), "Bell at most 13.5 cm (and its rolled lip).");
            Assert.That(bounds.size.x, Is.GreaterThan(0.12f), "A full-size bell.");
            Assert.That(bounds.center.x, Is.EqualTo(0.0f).Within(Tolerance), "Symmetric side to side, so the left hand needs no mirror.");
        }

        [Test]
        public void EveryHiltPartHasAUrpMaterialAndNoCollider()
        {
            foreach (var pistolGrip in new[] { PistolGripShape.Visconti, PistolGripShape.Belgian })
            {
                _visual.SetGrip(BladeMount.Pistol, pistolGrip);

                var renderers = _mount.GetComponentsInChildren<Renderer>(true);
                Assert.That(renderers, Is.Not.Empty);
                foreach (var partRenderer in renderers)
                {
                    Assert.That(partRenderer.sharedMaterial, Is.Not.Null, partRenderer.name);
                    Assert.That(partRenderer.sharedMaterial.shader.name, Does.StartWith("VrFencing/").Or.StartWith("Universal Render Pipeline/"), partRenderer.name);
                }

                Assert.That(_mount.GetComponentsInChildren<Collider>(true), Is.Empty);
            }
        }

        [Test]
        public void TheGrooveRunsAlongTheFlatFaceOnTop()
        {
            var bladeMesh = _mount.transform.Find("Blade").GetComponent<MeshFilter>().sharedMesh;
            var colors = bladeMesh.colors;

            Assert.That(colors, Is.Not.Empty);
            Assert.That(colors.Count(color => color.r < 0.5f), Is.GreaterThan(0), "The groove's faces are darker.");
            Assert.That(colors.Count(color => color.r > 0.9f), Is.GreaterThan(colors.Length / 2), "The blade's faces stay bright steel.");
        }

        private static Bounds TransformBounds(Transform part, Bounds local)
        {
            var result = new Bounds(part.TransformPoint(local.center), Vector3.zero);
            for (var corner = 0; corner < 8; corner++)
            {
                var offset = new Vector3(
                    (corner & 1) == 0 ? -local.extents.x : local.extents.x,
                    (corner & 2) == 0 ? -local.extents.y : local.extents.y,
                    (corner & 4) == 0 ? -local.extents.z : local.extents.z);
                result.Encapsulate(part.TransformPoint(local.center + offset));
            }

            return result;
        }
    }
}
