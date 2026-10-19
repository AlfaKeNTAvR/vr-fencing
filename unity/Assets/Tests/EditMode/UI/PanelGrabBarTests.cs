// The Unity side of grabbing the menu panels (PER-86): the grab bar sits under its panel where the laser can find it,
// dragging moves the panel and hands it over for pinning on release, and the pin button toggles.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VrFencing.UI;

namespace VrFencing.Tests.UI
{
    public class PanelGrabBarTests
    {
        private static readonly Vector2 PanelSize = new Vector2(0.78f, 0.58f);

        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void DestroyCreatedObjects()
        {
            foreach (var created in _created)
            {
                Object.DestroyImmediate(created);
            }

            _created.Clear();
        }

        private Transform CreatePanel(Vector3 position)
        {
            var panel = new GameObject("Test Panel");
            _created.Add(panel);
            panel.transform.position = position;
            UiSurface.Create(panel.transform, "Background", PanelSize);
            return panel.transform;
        }

        [Test]
        public void TheBarSitsUnderThePanelWithACollider()
        {
            var panel = CreatePanel(Vector3.zero);
            var bar = PanelGrabBar.Create(panel, PanelSize, _ => { }, _ => { });

            Assert.That(bar.transform.parent, Is.SameAs(panel));
            Assert.That(bar.transform.localPosition.y, Is.LessThan(-PanelSize.y * 0.5f), "Below the panel's bottom edge.");
            Assert.That(bar.GetComponent<BoxCollider>(), Is.Not.Null);
            Assert.That(bar.PinButton.transform.localPosition.x, Is.GreaterThan(PanelGrabBar.BarSize.x * 0.5f), "The pin button is beside the bar.");
        }

        [Test]
        public void DraggingMovesThePanelAndReleasingHandsItOverForPinning()
        {
            var head = new Vector3(0.0f, 1.5f, 0.0f);
            var panel = CreatePanel(new Vector3(0.0f, 1.5f, 1.0f));
            var released = new List<PanelGrabBar>();
            var bar = PanelGrabBar.Create(panel, PanelSize, released.Add, _ => { });
            var controller = new Vector3(0.0f, 1.0f, 0.3f);

            bar.BeginDrag(controller, bar.transform.position);
            Assert.That(bar.IsDragging, Is.True);
            var laserEnd = bar.Drag(controller, Vector3.right, head, deltaSeconds: 1.0f);

            Assert.That(panel.position.x, Is.GreaterThan(0.3f), "The panel swung to the right with the laser.");
            Assert.That(laserEnd.x, Is.GreaterThan(0.3f));
            Assert.That(released, Is.Empty);

            bar.EndDrag();

            Assert.That(bar.IsDragging, Is.False);
            Assert.That(released, Is.EqualTo(new[] { bar }));
        }

        [Test]
        public void ThePinButtonAsksToToggleAndShowsThePin()
        {
            var panel = CreatePanel(Vector3.zero);
            var pinPresses = new List<PanelGrabBar>();
            var bar = PanelGrabBar.Create(panel, PanelSize, _ => { }, pinPresses.Add);

            bar.PinButton.Press();
            bar.SetPinned(true);

            Assert.That(pinPresses, Is.EqualTo(new[] { bar }));
            Assert.That(bar.PinButton.Label, Is.EqualTo("Pinned"));
        }
    }
}
