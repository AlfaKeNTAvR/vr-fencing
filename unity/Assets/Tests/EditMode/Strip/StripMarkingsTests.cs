// The Strip's floor markings and status text on the headset: lines solid and fully above the pulsing Warning
// Zones (no see-through or z-fighting), and status text drawn with TextMeshPro so both eyes see it.
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using VrFencing.Settings;
using VrFencing.Strip;

namespace VrFencing.Tests.Strip
{
    public class StripMarkingsTests
    {
        private GameObject _parent;

        [SetUp]
        public void CreateParent()
        {
            _parent = new GameObject("En Garde Line");
        }

        [TearDown]
        public void DestroyCreated()
        {
            Object.DestroyImmediate(_parent);
            var statusText = GameObject.Find("Strip Status Text");
            if (statusText != null)
            {
                Object.DestroyImmediate(statusText);
            }
        }

        private Renderer[] BuildMarkings()
        {
            var markings = new StripMarkings(_parent.transform);
            markings.Build(StripGeometry.Create(4.0f, 1.65f, new WarningZoneSettings()));
            return _parent.GetComponentsInChildren<Renderer>();
        }

        [Test]
        public void LinesAreOpaqueAndZonesTranslucent()
        {
            var renderers = BuildMarkings();
            var lines = renderers.Where(slab => slab.name.Contains("Line")).ToArray();
            var zones = renderers.Where(slab => slab.name.StartsWith("Warning Zone")).ToArray();

            Assert.That(lines, Has.Length.EqualTo(7));
            Assert.That(zones, Has.Length.EqualTo(2));
            foreach (var line in lines)
            {
                Assert.That(line.sharedMaterial.renderQueue, Is.LessThan(2500), $"{line.name} must be opaque.");
            }

            foreach (var zone in zones)
            {
                Assert.That(zone.sharedMaterial.renderQueue, Is.GreaterThanOrEqualTo(3000), $"{zone.name} stays translucent.");
            }
        }

        [Test]
        public void EveryLineLiesAboveEveryWarningZone()
        {
            var renderers = BuildMarkings();
            var highestZoneTop = renderers.Where(slab => slab.name.StartsWith("Warning Zone"))
                .Max(zone => zone.transform.localPosition.y + (zone.transform.localScale.y * 0.5f));
            var lowestLineBottom = renderers.Where(slab => slab.name.Contains("Line"))
                .Min(line => line.transform.localPosition.y - (line.transform.localScale.y * 0.5f));

            Assert.That(lowestLineBottom, Is.GreaterThan(highestZoneTop));
        }

        [Test]
        public void ThePisteSurfaceIsTranslucentUnderEverythingAndTheZonesAreHatched()
        {
            var renderers = BuildMarkings();
            var surface = renderers.Single(slab => slab.name == "Piste Surface");
            var zones = renderers.Where(slab => slab.name.StartsWith("Warning Zone")).ToArray();

            Assert.That(surface.sharedMaterial.renderQueue, Is.GreaterThanOrEqualTo(3000), "The real floor shows through the surface.");
            Assert.That(surface.transform.localScale.x, Is.EqualTo(StripGeometry.WidthMeters).Within(1e-4f));
            var lowestOther = renderers.Where(slab => slab != surface).Min(slab => slab.transform.localPosition.y - (slab.transform.localScale.y * 0.5f));
            Assert.That(surface.transform.localPosition.y + (surface.transform.localScale.y * 0.5f), Is.LessThanOrEqualTo(lowestOther));
            foreach (var zone in zones)
            {
                Assert.That(zone.sharedMaterial.mainTexture, Is.Not.Null, $"{zone.name} carries the hatch (PER-41).");
                var block = new MaterialPropertyBlock();
                zone.GetPropertyBlock(block);
                Assert.That(block.GetVector("_BaseMap_ST").y, Is.GreaterThan(1.0f), "The hatch repeats along the zone, not stretched over it.");
            }
        }

        [Test]
        public void BothEnGardeLinesAreDrawnWhereThePlacementPointsWere()
        {
            var renderers = BuildMarkings();

            Assert.That(renderers.Single(slab => slab.name == "En Garde Line").transform.localPosition.z, Is.EqualTo(0.0f).Within(1e-4f));
            Assert.That(renderers.Single(slab => slab.name == "Dummy En Garde Line").transform.localPosition.z, Is.EqualTo(4.0f).Within(1e-4f));
        }

        [Test]
        public void StatusTextIsTextMeshPro()
        {
            var status = new StripStatusText();
            status.SetStandingMessage("Off the Strip: step back on");
            status.Tick(null, 0.0, 0.0f);

            var text = GameObject.Find("Strip Status Text").GetComponent<TextMeshPro>();

            Assert.That(text, Is.Not.Null);
            Assert.That(text.font, Is.Not.Null, "TMP Essentials (default font) are missing.");
            Assert.That(text.text, Is.EqualTo("Off the Strip: step back on"));
        }
    }
}
